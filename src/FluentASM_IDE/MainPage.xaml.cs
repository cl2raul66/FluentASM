using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentASM.Lsp.Protocol;
using FluentASM_IDE.Models;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Services;
using FluentASM_IDE.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace FluentASM_IDE;

public sealed partial class MainPage : Page
{
    #region Fields

    public MainPageViewModel ViewModel { get; } = new();

    private readonly DispatcherTimer _analysisDebounceTimer = new();
    private readonly DiagnosticAdorner _diagnosticAdorner;
    private readonly EditorKeyboardHandler _keyboardHandler;
    private readonly EditorUndoHistory _undoHistory = new();
    private readonly ProjectService _projectService;
    private readonly HoverService _hoverService;
    private readonly OutlineService _outlineService;
    private ScrollViewer? _editorInternalScrollViewer;
    private CancellationTokenSource? _analysisCts;
    private IReadOnlyList<HighlightToken>? _highlightTokens;
    private string _highlightedText = string.Empty;
    private string _lastNormalizedText = string.Empty;
    private bool _isHighlighting;
    private bool _applyingUndo;

    #endregion

    #region Constructor & Initialization

    public MainPage()
    {
        InitializeComponent();

        _diagnosticAdorner = new DiagnosticAdorner(EditorBox, ViewModel);
        _keyboardHandler = new EditorKeyboardHandler(EditorBox, ViewModel, IntelliSenseControl);
        _projectService = ViewModel.ProjectService;
        _hoverService = new HoverService(EditorBox, ViewModel);
        _outlineService = new OutlineService(ViewModel, EditorBox);

        InitializeTimers();
        SubscribeViewModelEvents();
        SubscribeProjectEvents();

        ActualThemeChanged += OnActualThemeChanged;
        Loaded += MainPage_Loaded;
    }

    private void SubscribeProjectEvents()
    {
        ViewModel.OpenProjectRequested += OnOpenProjectRequested;
        ViewModel.SaveRequested += OnSaveRequested;
        ViewModel.OutlineRequested += OnOutlineRequested;
    }

    private void OnOutlineRequested()
    {
        _outlineService.Show();
    }

    private void InitializeTimers()
    {
        _analysisDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
        _analysisDebounceTimer.Tick += (_, _) =>
        {
            _analysisDebounceTimer.Stop();
            RunAnalysisAsync();
        };
    }

    private void SubscribeViewModelEvents()
    {
        ViewModel.RevalidationRequested += OnRevalidationRequested;
        ViewModel.AnalysisRequested += ScheduleAnalysis;
        ViewModel.DiagnosticNavigationRequested += NavigateToDiagnostic;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _diagnosticAdorner.SetLoaded(true);

        ConfigureEditorParagraphFormat();

        // El historial de undo propio evita que el resaltado contamine Ctrl+Z.
        EditorBox.Document.UndoLimit = 0;

        // Procesar argumentos de lanzamiento y activación por archivo.
        ProcessLaunchArguments();

        if (!ViewModel.IsProjectOpen)
        {
            LoadInitialSample();
        }

        CaptureEditorState();
        UpdateLineCountFromDocument();
        SetupScrollSync();
        PositionCaretAtInitialLine();

        bool isDark = ActualTheme != ElementTheme.Light;
        AsmHighlighter.IsCurrentDark = isDark;
        ViewModel.UpdateTheme(isDark);

        RunAnalysisAsync();
    }

    private void ProcessLaunchArguments()
    {
        // Activación por archivo (clic derecho → Abrir con FluentASM).
        if (App.ActivatedFilePath is { } activatedPath)
        {
            OpenProjectOrFolder(activatedPath);
            return;
        }

        // Argumentos de línea de comandos.
        foreach (var arg in App.LaunchArguments)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            var fullPath = Path.GetFullPath(arg);
            if (Directory.Exists(fullPath))
            {
                OpenProjectOrFolder(fullPath);
                return;
            }

            if (File.Exists(fullPath))
            {
                var extension = Path.GetExtension(fullPath).ToLowerInvariant();
                if (extension == ".fproj")
                {
                    OpenProjectOrFolder(fullPath);
                    return;
                }

                if (extension == ".fasm")
                {
                    OpenProjectOrFolder(fullPath);
                    return;
                }
            }
        }
    }

    private async void OpenProjectOrFolder(string path)
    {
        // Si hay cambios no guardados, pedir confirmación antes de abrir otro proyecto.
        if (ViewModel.HasUnsavedChanges)
        {
            if (!await ConfirmDiscardChangesAsync())
            {
                return;
            }
        }

        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            var result = _projectService.OpenFolder(fullPath);
            ApplyProjectOpenResult(result);
        }
        else if (File.Exists(fullPath))
        {
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (extension == ".fproj")
            {
                var result = _projectService.OpenFproj(fullPath);
                ApplyProjectOpenResult(result);
            }
            else if (extension == ".fasm")
            {
                var result = _projectService.OpenSourceFile(fullPath);
                ApplyProjectOpenResult(result);
            }
        }
    }

    private void ApplyProjectOpenResult(ProjectOpenResult result)
    {
        if (!result.Success)
        {
            ViewModel.StatusText = $"Error al abrir: {result.Error}";
            return;
        }

        ViewModel.OpenProject(result.SourcePath!, result.Project?.ProjectFilePath);
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Cambios sin guardar",
            Content = "Hay cambios sin guardar en el archivo fuente. ¿Descartarlos y abrir otro proyecto?",
            PrimaryButtonText = "Descartar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close
        };

        // XAML Root para mostrar el ContentDialog.
        if (App.Window.Content is FrameworkElement root)
        {
            dialog.XamlRoot = root.XamlRoot;
        }

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void ConfigureEditorParagraphFormat()
    {
        // 22 DIP ≈ 16.5 pt line height for RichEditBox
        var para = EditorBox.Document.GetDefaultParagraphFormat();
        para.SetLineSpacing(LineSpacingRule.Exactly, 16.5f);
        para.SpaceBefore = 0;
        para.SpaceAfter = 0;

        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        if (!string.IsNullOrEmpty(text))
        {
            var allRange = EditorBox.Document.GetRange(0, text.Length);
            allRange.ParagraphFormat.SetLineSpacing(LineSpacingRule.Exactly, 16.5f);
            allRange.ParagraphFormat.SpaceBefore = 0;
            allRange.ParagraphFormat.SpaceAfter = 0;
        }
    }

    private void LoadInitialSample()
    {
        EditorBox.Document.SetText(
            TextSetOptions.None,
            DocumentText.ToEditorText(MainPageViewModel.InitialSampleAsm));
    }

    private void SetupScrollSync()
    {
        _editorInternalScrollViewer = FindVisualChild<ScrollViewer>(EditorBox);
        if (_editorInternalScrollViewer != null)
        {
            _editorInternalScrollViewer.ViewChanged += (_, _) =>
            {
                GutterScrollViewer.ChangeView(null, _editorInternalScrollViewer.VerticalOffset, null, true);
                ViewModel.ScrollOffset = _editorInternalScrollViewer.VerticalOffset;
            };
        }
    }

    #endregion

    #region Theme & Highlighting

    private void OnActualThemeChanged(FrameworkElement sender, object _)
    {
        bool isDark = sender.ActualTheme != ElementTheme.Light;
        AsmHighlighter.IsCurrentDark = isDark;
        ViewModel.UpdateTheme(isDark);

        // Se reutiliza el último análisis: solo cambian los colores.
        ApplyHighlightAndDiagnostics();
    }

    private void ApplyHighlightAndDiagnostics()
    {
        if (_highlightTokens is null)
        {
            return;
        }

        _isHighlighting = true;
        try
        {
            AsmHighlighter.Apply(EditorBox, _highlightTokens, _highlightedText);

            // El subrayado de errores se aplica siempre después del resaltado.
            _diagnosticAdorner.ApplyFormatting();
        }
        catch
        {
            // El editor aún no está listo; se reintentará en el próximo análisis.
        }
        finally
        {
            _isHighlighting = false;
        }
    }

    #endregion

    #region Editor Events (Text, Selection, Pointer)

    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        if (_isHighlighting || _applyingUndo) return;

        _diagnosticAdorner.OnPointerExited(); // close any open flyout
        UpdateCursorCoordinates();

        // Los cambios de solo formato (resaltado, subrayados) también pueden
        // disparar TextChanged; solo se reanaliza cuando el texto cambió.
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        string normalized = DocumentText.Normalize(text);
        if (string.Equals(normalized, _lastNormalizedText, StringComparison.Ordinal))
        {
            return;
        }

        UpdateLineCountFromDocument();
        RecordUndoStep();

        // Marcar cambios no guardados solo si hay un proyecto/archivo real abierto.
        if (ViewModel.IsProjectOpen)
        {
            ViewModel.MarkUnsavedChanges(true);
        }

        // Un único debounce para diagnósticos y resaltado.
        ScheduleAnalysis();
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
        => UpdateCursorCoordinates();

    private void OnEditorPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _diagnosticAdorner.OnPointerMoved(e);
        _hoverService.OnPointerMoved(e.GetCurrentPoint(EditorBox).Position);
    }

    private void OnEditorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _diagnosticAdorner.OnPointerExited();
        _hoverService.OnPointerExited();
    }

    private void OnEditorPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Ctrl+Z / Ctrl+Y se atienden antes que el control para que el Undo
        // nativo (desactivado con UndoLimit = 0) no interfiera.
        if (TryHandleUndoRedo(e))
        {
            return;
        }

        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
                       CoreVirtualKeyStates.Down) != 0;

        if (isCtrl && e.Key == VirtualKey.O)
        {
            e.Handled = true;
            OpenFolderPickerAsync();
            return;
        }

        if (isCtrl && e.Key == VirtualKey.S)
        {
            e.Handled = true;
            SaveCurrentFile();
            return;
        }
    }

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
        => _keyboardHandler.OnKeyDown(e);

    #endregion

    #region LSP Synchronization

    private void ScheduleAnalysis()
    {
        _analysisDebounceTimer.Stop();
        _analysisDebounceTimer.Start();
    }

    private async void RunAnalysisAsync()
    {
        _analysisCts?.Cancel();
        var cts = new CancellationTokenSource();
        _analysisCts = cts;

        EditorBox.Document.GetText(TextGetOptions.None, out string editorText);

        DocumentAnalysisResult? result;
        try
        {
            result = await ViewModel.AnalyzeDocumentAsync(editorText ?? string.Empty, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fallo al analizar el documento: {ex}");
            return;
        }

        if (result is null || cts.IsCancellationRequested)
        {
            return;
        }

        // El texto pudo cambiar mientras se analizaba: nunca se aplican spans viejos.
        EditorBox.Document.GetText(TextGetOptions.None, out string currentText);
        if (!string.Equals(DocumentText.Normalize(currentText), result.Text, StringComparison.Ordinal))
        {
            return;
        }

        _highlightTokens = result.HighlightTokens;
        _highlightedText = result.Text;

        // Primero se publican los diagnósticos de esta versión y después se
        // resalta y se reaplica el subrayado sobre el texto ya validado.
        ViewModel.ApplyAnalysis(result);
        ApplyHighlightAndDiagnostics();
    }

    private void OnRevalidationRequested()
    {
        ViewModel.StatusText = "Documento revalidado";
        _analysisDebounceTimer.Stop();
        RunAnalysisAsync();
    }

    private void OnOpenProjectRequested()
    {
        OpenFolderPickerAsync();
    }

    private void OnSaveRequested()
    {
        SaveCurrentFile();
    }

    private async void OpenFolderPickerAsync()
    {
        if (ViewModel.HasUnsavedChanges)
        {
            if (!await ConfirmDiscardChangesAsync())
            {
                return;
            }
        }

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        // Inicializar el picker con el handle de la ventana (requerido en WinUI 3).
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            OpenProjectOrFolder(folder.Path);
        }
    }

    private void SaveCurrentFile()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        ViewModel.SaveSource(text ?? string.Empty);
    }

    #endregion

    #region Diagnostics Navigation

    private void NavigateToDiagnostic(DiagnosticItem diagnostic)
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text)) return;

        int start = DocumentText.ToEditorOffset(text, diagnostic.Line - 1, diagnostic.Column - 1);
        int end = DocumentText.ToEditorOffset(text, diagnostic.EndLine - 1, diagnostic.EndColumn - 1);

        if (end <= start) end = Math.Min(start + 1, text.Length);

        EditorBox.Document.Selection.SetRange(start, end);
        EditorBox.Focus(FocusState.Programmatic);
        UpdateCursorCoordinates();
    }

    #endregion

    #region Caret & Coordinates

    private void PositionCaretAtInitialLine()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text)) return;

        // Line 13 (1-based): "Gpr1.copy([numA])" inside main routine.
        int targetPos = DocumentText.ToEditorOffset(text, 12, 0);
        while (targetPos < text.Length && (text[targetPos] == ' ' || text[targetPos] == '\t'))
            targetPos++;

        int tokenEnd = targetPos;
        while (tokenEnd < text.Length && (char.IsLetterOrDigit(text[tokenEnd]) || text[tokenEnd] == '_'))
            tokenEnd++;

        EditorBox.Document.Selection.SetRange(targetPos, tokenEnd);
        UpdateCursorCoordinates();
    }

    private void UpdateLineCountFromDocument()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        ViewModel.UpdateLineCount(DocumentText.CountLines(DocumentText.Normalize(text)));
    }

    private void UpdateCursorCoordinates()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        string normalized = DocumentText.Normalize(text);
        int editorOffset = Math.Clamp(EditorBox.Document.Selection.StartPosition, 0, text.Length);
        int normalizedOffset = Math.Min(DocumentText.ToNormalizedOffset(text, editorOffset), normalized.Length);
        var (line, character) = DocumentText.ToLineCharacter(normalized, normalizedOffset);
        ViewModel.UpdateCursorPosition(line + 1, character + 1);
    }

    #endregion

    #region IntelliSense Commit (from overlay)

    private void OnIntelliSenseItemCommitted(object? sender, CompletionItem item)
    {
        if (item == null) return;

        string insert = string.IsNullOrEmpty(item.InsertText) ? item.Label : item.InsertText;
        insert = insert.Replace("$0", "");
        EditorBox.Document.Selection.TypeText(insert);
        ViewModel.CloseIntelliSense();
        EditorBox.Focus(FocusState.Programmatic);
    }

    #endregion

    #region Command Palette Events (UI-only, delegated to ViewModel)

    private void OnBackdropPointerPressed(object sender, PointerRoutedEventArgs e)
        => ViewModel.CloseCommandPalette();

    private void OnPaletteContainerClicked(object sender, PointerRoutedEventArgs e)
        => e.Handled = true;

    private void OnEscBadgeClicked(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.CloseCommandPalette();
        e.Handled = true;
    }

    private void OnPaletteItemHover(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CommandPaletteItem item)
            foreach (var c in ViewModel.FilteredCommands) c.IsActive = (c == item);
    }

    private void OnPaletteItemUnhover(object sender, PointerRoutedEventArgs e) { }

    private void OnPaletteItemClicked(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CommandPaletteItem item)
            ViewModel.ExecutePaletteItem(item);
    }

    private void OnCommandTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
    {
        if (sender is TextBox tb) ViewModel.CommandQuery = tb.Text;
    }

    private void OnCommandInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.CloseCommandPalette();
            e.Handled = true;
            EditorBox.Focus(FocusState.Programmatic);
        }
        else if (e.Key == VirtualKey.Down)
        {
            ViewModel.SelectNextPaletteItem(); e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up)
        {
            ViewModel.SelectPrevPaletteItem(); e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter)
        {
            ViewModel.ExecuteActivePaletteItem(); e.Handled = true;
            EditorBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
                       CoreVirtualKeyStates.Down) != 0;

        if (e.Key == VirtualKey.Escape)
        {
            if (ViewModel.IsCommandPaletteOpen)
            {
                ViewModel.CloseCommandPalette(); e.Handled = true;
            }
            else if (ViewModel.IsIntelliSenseOpen)
            {
                ViewModel.CloseIntelliSense(); e.Handled = true;
            }
        }
        else if (isCtrl && e.Key == VirtualKey.P)
        {
            ViewModel.ToggleCommandPalette();
            if (ViewModel.IsCommandPaletteOpen)
                EditorBox.Focus(FocusState.Programmatic); // CommandInputBox focused via XAML
            e.Handled = true;
        }
    }

    #endregion

    #region Undo/Redo de texto

    private void CaptureEditorState()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        _lastNormalizedText = DocumentText.Normalize(text);
        _undoHistory.Clear();
    }

    private void RecordUndoStep()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        string normalized = DocumentText.Normalize(text);
        if (string.Equals(normalized, _lastNormalizedText, StringComparison.Ordinal))
        {
            return;
        }

        string previous = _lastNormalizedText;
        var (prefix, removed, inserted) = DiffText(previous, normalized);

        // La selección previa se deduce del propio cambio: es el tramo que el
        // usuario reemplazó, así Ctrl+Z devuelve el cursor donde editó.
        var before = new EditorUndoState(previous, prefix, prefix + removed);
        _undoHistory.Record(before, inserted, DateTime.UtcNow);
        _lastNormalizedText = normalized;
    }

    /// <summary>
    /// Calcula el prefijo común, los caracteres eliminados y el texto insertado
    /// entre dos versiones del documento.
    /// </summary>
    private static (int Prefix, int Removed, string Inserted) DiffText(string previous, string current)
    {
        int prefix = 0;
        int maxPrefix = Math.Min(previous.Length, current.Length);
        while (prefix < maxPrefix && previous[prefix] == current[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        int maxSuffix = Math.Min(previous.Length - prefix, current.Length - prefix);
        while (suffix < maxSuffix
               && previous[previous.Length - 1 - suffix] == current[current.Length - 1 - suffix])
        {
            suffix++;
        }

        int removed = previous.Length - prefix - suffix;
        string inserted = current.Substring(prefix, current.Length - prefix - suffix);
        return (prefix, removed, inserted);
    }

    private bool TryHandleUndoRedo(KeyRoutedEventArgs e)
    {
        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
                       CoreVirtualKeyStates.Down) != 0;
        if (!isCtrl)
        {
            return false;
        }

        bool isShift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) &
                        CoreVirtualKeyStates.Down) != 0;

        if (e.Key == VirtualKey.Z)
        {
            e.Handled = true;
            if (isShift)
            {
                RedoEditor();
            }
            else
            {
                UndoEditor();
            }

            return true;
        }

        if (e.Key == VirtualKey.Y)
        {
            e.Handled = true;
            RedoEditor();
            return true;
        }

        return false;
    }

    private void UndoEditor()
    {
        var target = _undoHistory.Undo(CaptureUndoState());
        if (target is not null)
        {
            ApplyUndoState(target);
        }
    }

    private void RedoEditor()
    {
        var target = _undoHistory.Redo(CaptureUndoState());
        if (target is not null)
        {
            ApplyUndoState(target);
        }
    }

    private EditorUndoState CaptureUndoState()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        var selection = EditorBox.Document.Selection;
        string normalized = DocumentText.Normalize(text);
        return new EditorUndoState(
            normalized,
            Math.Min(DocumentText.ToNormalizedOffset(text, selection.StartPosition), normalized.Length),
            Math.Min(DocumentText.ToNormalizedOffset(text, selection.EndPosition), normalized.Length));
    }

    private void ApplyUndoState(EditorUndoState state)
    {
        _applyingUndo = true;
        try
        {
            // Los offsets guardados son del texto normalizado; ToEditorText
            // reemplaza \n por \r uno a uno, así que se conservan.
            EditorBox.Document.SetText(TextSetOptions.None, DocumentText.ToEditorText(state.Text));
            EditorBox.Document.Selection.SetRange(state.SelectionStart, state.SelectionEnd);
            _lastNormalizedText = state.Text;
        }
        finally
        {
            _applyingUndo = false;
        }

        UpdateLineCountFromDocument();
        UpdateCursorCoordinates();
        ScheduleAnalysis();
    }

    #endregion

    #region Helpers

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    #endregion
}

