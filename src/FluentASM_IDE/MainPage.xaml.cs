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
    private readonly DispatcherTimer _completionDebounceTimer = new();
    private readonly DiagnosticAdorner _diagnosticAdorner;
    private readonly EditorKeyboardHandler _keyboardHandler;
    private readonly EditorUndoHistory _undoHistory = new();
    private readonly ProjectService _projectService;
    private readonly HoverService _hoverService;
    private readonly OutlineService _outlineService;
    private ScrollViewer? _editorInternalScrollViewer;
    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource? _completionCts;
    private IReadOnlyList<HighlightToken>? _highlightTokens;
    private string _highlightedText = string.Empty;
    private string _lastNormalizedText = string.Empty;
    private string _savedNormalizedText = string.Empty;
    private string? _suppressedAutomaticCompletionText;
    private int _lastTextChangedCaretOffset = -1;
    private bool _isHighlighting;
    private bool _applyingUndo;
    private bool _applyingCompletion;

    #endregion

    #region Constructor & Initialization

    public MainPage()
    {
        InitializeComponent();

        _diagnosticAdorner = new DiagnosticAdorner(EditorBox, ViewModel);
        _keyboardHandler = new EditorKeyboardHandler(EditorBox, ViewModel);
        _projectService = ViewModel.ProjectService;
        _hoverService = new HoverService(EditorBox, ViewModel);
        _outlineService = new OutlineService(ViewModel, EditorBox);

        // El RichEditBox interno marca PointerMoved/PointerExited como handled,
        // así que hay que suscribirse con handledEventsToo para recibirlos.
        EditorBox.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(OnEditorPointerMoved),
            handledEventsToo: true);
        EditorBox.AddHandler(
            UIElement.PointerExitedEvent,
            new PointerEventHandler(OnEditorPointerExited),
            handledEventsToo: true);

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

        _completionDebounceTimer.Interval = TimeSpan.FromMilliseconds(120);
        _completionDebounceTimer.Tick += (_, _) =>
        {
            _completionDebounceTimer.Stop();
            RequestCompletionsAtCaret();
        };
    }

    private void SubscribeViewModelEvents()
    {
        ViewModel.RevalidationRequested += OnRevalidationRequested;
        ViewModel.AnalysisRequested += ScheduleAnalysis;
        ViewModel.DiagnosticNavigationRequested += NavigateToDiagnostic;
        ViewModel.ProjectOpened += OnProjectOpened;
        ViewModel.DocumentTextReplaced += OnDocumentTextReplaced;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _diagnosticAdorner.SetLoaded(true);

        ConfigureEditorParagraphFormat();

        // El historial de undo propio evita que el resaltado contamine Ctrl+Z.
        EditorBox.Document.UndoLimit = 0;

        bool isDark = ActualTheme != ElementTheme.Light;
        AsmHighlighter.IsCurrentDark = isDark;
        ViewModel.UpdateTheme(isDark);

        // Procesar argumentos de lanzamiento y activación por archivo.
        ProcessLaunchArguments();

        if (!ViewModel.IsProjectOpen)
        {
            LoadInitialSample();
            CaptureEditorState();
            UpdateLineCountFromDocument();
            PositionCaretAtInitialLine();
        }

        SetupScrollSync();

        // Un proyecto real ya fue sincronizado por OnProjectOpened.
        if (!ViewModel.IsProjectOpen)
        {
            RunAnalysisAsync();
        }

        // Asegurar foco para atajos de teclado de página (Ctrl+O, Ctrl+P, etc.).
        this.Focus(FocusState.Programmatic);
        this.GotFocus += (_, _) => EditorBox.Focus(FocusState.Programmatic);
    }

    private void ProcessLaunchArguments()
    {
        // Activación por archivo (clic derecho → Abrir con FluentASM).
        if (App.ActivatedFilePath is { } activatedPath)
        {
            if (TryGetExistingPath(activatedPath, out string activatedFullPath))
            {
                OpenProjectOrFolder(activatedFullPath);
            }
            else
            {
                ViewModel.StatusText = $"No se encontró la ruta activada: {activatedPath}";
            }

            return;
        }

        // Argumentos de línea de comandos. Los flags del sistema y los argumentos
        // que no correspondan a una ruta existente se ignoran sin interrumpir el arranque.
        foreach (var arg in App.LaunchArguments)
        {
            if (!TryGetExistingPath(arg, out string fullPath))
            {
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                OpenProjectOrFolder(fullPath);
                return;
            }

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (extension == ".fproj" || extension == ".fasm")
            {
                OpenProjectOrFolder(fullPath);
                return;
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

        if (!TryGetFullPath(path, out string fullPath))
        {
            ViewModel.StatusText = $"Ruta no válida: {path}";
            return;
        }

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
            else
            {
                ViewModel.StatusText = $"Tipo de archivo no compatible: {extension}";
            }
        }
        else
        {
            ViewModel.StatusText = $"No se encontró la ruta: {fullPath}";
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

    private void OnProjectOpened(string sourceText)
    {
        CancelPendingCompletionRequest();
        ViewModel.InvalidateCompletions();

        // La carga del documento no es una edición del usuario: evita marcarlo
        // como modificado y contaminar el historial de deshacer.
        _applyingUndo = true;
        try
        {
            EditorBox.Document.SetText(
                TextSetOptions.None,
                DocumentText.ToEditorText(sourceText));
            EditorBox.Document.Selection.SetRange(0, 0);
        }
        finally
        {
            _applyingUndo = false;
        }

        ConfigureEditorParagraphFormat();
        CaptureEditorState();
        _savedNormalizedText = DocumentText.Normalize(sourceText);
        _suppressedAutomaticCompletionText = null;
        UpdateLineCountFromDocument();
        UpdateCursorCoordinates();
        ViewModel.MarkUnsavedChanges(false);
        RunAnalysisAsync();
    }

    private void OnDocumentTextReplaced(string sourceText, int caretOffset)
    {
        CancelPendingCompletionRequest();
        ViewModel.InvalidateCompletions();
        _suppressedAutomaticCompletionText = null;
        string normalized = DocumentText.Normalize(sourceText);
        int normalizedCaret = Math.Clamp(caretOffset, 0, normalized.Length);

        _applyingUndo = true;
        try
        {
            EditorBox.Document.SetText(
                TextSetOptions.None,
                DocumentText.ToEditorText(normalized));

            EditorBox.Document.GetText(TextGetOptions.None, out string editorText);
            int[] offsetMap = DocumentText.BuildNormalizedToEditorMap(editorText);
            int editorCaret = offsetMap[normalizedCaret];
            EditorBox.Document.Selection.SetRange(editorCaret, editorCaret);
        }
        finally
        {
            _applyingUndo = false;
        }

        // Registra el quick-fix como un paso de deshacer y actualiza el texto
        // base contra el que se comparará la siguiente pulsación de tecla.
        RecordUndoStep();

        UpdateLineCountFromDocument();
        UpdateCursorCoordinates();
        ViewModel.MarkUnsavedChanges(true);
        ScheduleAnalysis();
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
        if (_editorInternalScrollViewer is not null)
        {
            _editorInternalScrollViewer.ViewChanged += (_, _) =>
            {
                GutterScrollViewer.ChangeView(null, _editorInternalScrollViewer.VerticalOffset, null, true);
                ViewModel.ScrollOffset = _editorInternalScrollViewer.VerticalOffset;
                PositionIntelliSenseOverlay();
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
        text ??= string.Empty;
        string normalized = DocumentText.Normalize(text);
        if (string.Equals(normalized, _lastNormalizedText, StringComparison.Ordinal))
        {
            return;
        }

        // Un TextChanged tardío producido por el commit debe impedir que el
        // propio texto insertado vuelva a disparar el overlay. La supresión
        // solo sigue vigente mientras el documento conserve exactamente ese
        // texto; la siguiente pulsación del usuario la cancela.
        bool suppressAutomaticCompletion =
            _suppressedAutomaticCompletionText is not null &&
            string.Equals(
                normalized,
                _suppressedAutomaticCompletionText,
                StringComparison.Ordinal);
        if (!suppressAutomaticCompletion)
        {
            _suppressedAutomaticCompletionText = null;
        }

        UpdateLineCountFromDocument();

        // Durante un commit el paso de undo se registra de forma atómica al
        // finalizar la inserción, no desde este evento intermediario.
        if (!_applyingCompletion)
        {
            RecordUndoStep();
        }

        // El estado "sin guardar" se deriva del contenido guardado para que
        // una reversión manual o un undo al baseline también lo limpien.
        UpdateUnsavedChangesFromText(normalized);

        // Un único debounce para diagnósticos y resaltado.
        ScheduleAnalysis();
        if (_applyingCompletion)
        {
            return;
        }

        _completionDebounceTimer.Stop();
        CancelPendingCompletionRequest();
        ViewModel.CloseIntelliSense();

        int caretOffset = GetNormalizedCaretOffset(text, normalized);
        _lastTextChangedCaretOffset = caretOffset;
        if (!suppressAutomaticCompletion &&
            ShouldTriggerAutomaticCompletion(normalized, caretOffset))
        {
            ScheduleAutomaticCompletion();
        }
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateCursorCoordinates();

        // Una escritura puede cambiar el caret y producir SelectionChanged
        // después de TextChanged. En ese orden, el evento pertenece al texto
        // recién editado y no debe cancelar el debounce que acaba de programar
        // el autocompletado. Un movimiento explícito del caret sí invalida la
        // sesión actual.
        EditorBox.Document.GetText(TextGetOptions.None, out string editorText);
        editorText ??= string.Empty;
        string normalized = DocumentText.Normalize(editorText);
        int caretOffset = GetNormalizedCaretOffset(editorText, normalized);
        bool followsTextEdit =
            _completionDebounceTimer.IsEnabled &&
            caretOffset == _lastTextChangedCaretOffset;

        if (!_applyingCompletion && !followsTextEdit)
        {
            _completionDebounceTimer.Stop();
            CancelPendingCompletionRequest();
        }

        if (!_applyingCompletion && !followsTextEdit && ViewModel.IsIntelliSenseOpen)
        {
            ViewModel.CloseIntelliSense();
        }

        // Mostrar TeachingTip de diagnóstico si el caret está sobre un error (navegación por teclado).
        _diagnosticAdorner.ShowAtCaret();

        PositionIntelliSenseOverlay();
    }

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

    private void OnEditorContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        // Suprimir el menú contextual predeterminado del RichEditBox (negrita, cursiva, etc.)
        args.Handled = true;
    }

    private void OnEditorGotFocus(object sender, RoutedEventArgs e)
    {
        // Re-aplicar resaltado y diagnósticos al recuperar el foco
        ApplyHighlightAndDiagnostics();
    }

    private void OnEditorPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _diagnosticAdorner.OnPointerEntered();
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

        // PreviewKeyDown tiene prioridad sobre el bubbling KeyDown. Así las
        // flechas, Intro y Escape nunca se insertan en el editor mientras el
        // overlay está abierto.
        if (ViewModel.IsIntelliSenseOpen)
        {
            switch (e.Key)
            {
                case VirtualKey.Up:
                    IntelliSenseControl.SelectPrevious();
                    e.Handled = true;
                    return;
                case VirtualKey.Down:
                    IntelliSenseControl.SelectNext();
                    e.Handled = true;
                    return;
                case VirtualKey.Enter:
                    IntelliSenseControl.CommitSelected();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == VirtualKey.Escape)
        {
            _completionDebounceTimer.Stop();
            CancelPendingCompletionRequest();
            if (ViewModel.IsIntelliSenseOpen)
            {
                ViewModel.CloseIntelliSense();
                e.Handled = true;
            }

            return;
        }

        if (isCtrl && e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            RequestCompletionsAtCaret();
            return;
        }

        if (isCtrl && e.Key == VirtualKey.P)
        {
            e.Handled = true;
            _completionDebounceTimer.Stop();
            CancelPendingCompletionRequest();
            ViewModel.ToggleCommandPalette();
            if (ViewModel.IsCommandPaletteOpen)
            {
                CommandInputBox.Focus(FocusState.Programmatic);
            }

            return;
        }

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

    private void ScheduleAutomaticCompletion()
    {
        _completionDebounceTimer.Stop();
        _completionDebounceTimer.Start();
    }

    private bool ShouldTriggerAutomaticCompletion(string normalized, int offset)
    {
        if (offset <= 0 || offset > normalized.Length)
        {
            return false;
        }

        char previous = normalized[offset - 1];
        if (previous is '.' or '@' or '[')
        {
            return true;
        }

        int prefixStart = offset;
        int letters = 0;
        while (prefixStart > 0 && IsCompletionPrefixChar(normalized[prefixStart - 1]))
        {
            prefixStart--;
            if (char.IsLetter(normalized[prefixStart]))
            {
                letters++;
            }
        }

        if (letters >= 2)
        {
            return true;
        }

        // El contexto se deduce del documento actual, no de la notificación
        // anterior de TextChanged. Así una edición coalescida como `.x` o
        // `[ab` conserva el disparador aunque RichEditBox entregue una sola
        // notificación para varios caracteres.
        if (prefixStart < offset &&
            prefixStart > 0 &&
            normalized[prefixStart - 1] is '.' or '@' or '[')
        {
            return true;
        }

        return false;
    }

    private static bool IsCompletionPrefixChar(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private async void RequestCompletionsAtCaret()
    {
        // La invocación explícita sustituye cualquier sugerencia automática
        // pendiente; un debounce posterior no debe cancelar ni reemplazar esta
        // solicitud.
        _completionDebounceTimer.Stop();
        CancelPendingCompletionRequest();
        var cts = new CancellationTokenSource();
        CancellationToken requestToken = cts.Token;
        _completionCts = cts;

        UpdateCursorCoordinates();
        EditorBox.Document.GetText(TextGetOptions.None, out string editorText);
        editorText ??= string.Empty;
        string requestedText = DocumentText.Normalize(editorText);
        int requestedOffset = GetNormalizedCaretOffset(editorText, requestedText);
        var (line, character) = DocumentText.ToLineCharacter(requestedText, requestedOffset);
        var requestedCaret = new Position(line, character);

        try
        {
            await ViewModel.RequestLspCompletionsAsync(
                requestedText,
                requestedCaret,
                openPopup: true,
                requestToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fallo al solicitar autocompletado: {ex}");
            return;
        }
        finally
        {
            if (ReferenceEquals(_completionCts, cts))
            {
                _completionCts = null;
            }

            cts.Dispose();
        }

        if (requestToken.IsCancellationRequested)
        {
            return;
        }

        // El resultado solo se presenta si el documento y el caret no cambiaron
        // mientras el LSP calculaba la lista.
        EditorBox.Document.GetText(TextGetOptions.None, out string currentEditorText);
        currentEditorText ??= string.Empty;
        string currentText = DocumentText.Normalize(currentEditorText);
        int currentOffset = GetNormalizedCaretOffset(currentEditorText, currentText);
        if (!string.Equals(currentText, requestedText, StringComparison.Ordinal) ||
            currentOffset != requestedOffset ||
            !ViewModel.IsCompletionCurrent(requestedCaret))
        {
            ViewModel.InvalidateCompletions();
            return;
        }

        if (ViewModel.IsIntelliSenseOpen)
        {
            PositionIntelliSenseOverlay();
        }
    }

    private void CancelPendingCompletionRequest()
    {
        CancellationTokenSource? cts = _completionCts;
        _completionCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async void RunAnalysisAsync()
    {
        _analysisCts?.Cancel();
        var cts = new CancellationTokenSource();
        _analysisCts = cts;

        EditorBox.Document.GetText(TextGetOptions.None, out string editorText);
        System.Diagnostics.Debug.WriteLine($"[RunAnalysisAsync] editorText length: {editorText?.Length ?? 0}");

        DocumentAnalysisResult? result;
        try
        {
            result = await ViewModel.AnalyzeDocumentAsync(editorText ?? string.Empty, cts.Token);
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[RunAnalysisAsync] Canceled");
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RunAnalysisAsync] Exception: {ex}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[RunAnalysisAsync] result: {(result is null ? "NULL" : $"OK, diagnostics={result.Diagnostics.Count}, highlights={result.HighlightTokens.Count}")}");

        if (result is null || cts.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[RunAnalysisAsync] Early return: null result or canceled");
            return;
        }

        // El texto pudo cambiar mientras se analizaba: nunca se aplican spans viejos.
        EditorBox.Document.GetText(TextGetOptions.None, out string currentText);
        if (!string.Equals(DocumentText.Normalize(currentText), result.Text, StringComparison.Ordinal))
        {
            System.Diagnostics.Debug.WriteLine("[RunAnalysisAsync] Text changed during analysis, discarding");
            return;
        }

        _highlightTokens = result.HighlightTokens;
        _highlightedText = result.Text;

        // Primero se publican los diagnósticos de esta versión y después se
        // resalta y se reaplica el subrayado sobre el texto ya validado.
        System.Diagnostics.Debug.WriteLine("[RunAnalysisAsync] Calling ApplyAnalysis");
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
        text ??= string.Empty;
        if (ViewModel.SaveSource(text))
        {
            _savedNormalizedText = DocumentText.Normalize(text);
        }
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

    private void PositionIntelliSenseOverlay()
    {
        if (!ViewModel.IsIntelliSenseOpen)
        {
            return;
        }

        double left = 0;
        double top = 0;
        double caretTop = 0;
        bool positioned = false;

        try
        {
            EditorBox.Document.GetText(TextGetOptions.None, out string documentText);
            int caret = Math.Clamp(
                EditorBox.Document.Selection.StartPosition,
                0,
                documentText.Length);
            ITextRange caretRange = EditorBox.Document.GetRange(caret, caret);
            caretRange.GetRect(PointOptions.ClientCoordinates, out Rect rect, out int clipped);

            if (double.IsFinite(rect.X) && double.IsFinite(rect.Y) &&
                double.IsFinite(rect.Width) && double.IsFinite(rect.Height) &&
                rect.Height > 0)
            {
                GeneralTransform transform = EditorBox.TransformToVisual(EditorHost);
                Point clientPoint = transform.TransformPoint(new Point(rect.X, rect.Bottom));
                Point clientTopPoint = transform.TransformPoint(new Point(rect.X, rect.Y));
                left = clientPoint.X;
                top = clientPoint.Y;
                caretTop = clientTopPoint.Y;
                positioned = true;
            }
        }
        catch
        {
            // El control puede todavía estar compositando; se usa el fallback.
        }

        if (!positioned)
        {
            left = 16;
            top = ViewModel.ActiveLineMargin.Top + 22;
        }

        if (EditorHost.ActualWidth > 0 && IntelliSenseControl.ActualWidth > 0)
        {
            double maxLeft = Math.Max(0, EditorHost.ActualWidth - IntelliSenseControl.ActualWidth - 8);
            left = Math.Clamp(left, 0, maxLeft);
        }
        else
        {
            left = Math.Max(0, left);
        }

        if (EditorHost.ActualHeight > 0)
        {
            double availableHeight = EditorHost.ActualHeight;
            double overlayHeight = IntelliSenseControl.ActualHeight;

            if (positioned &&
                overlayHeight > 0 &&
                top + overlayHeight > availableHeight - 4)
            {
                double above = caretTop - overlayHeight - 4;
                if (above >= 0)
                {
                    top = above;
                }
            }

            double maxTop = overlayHeight > 0
                ? Math.Max(0, availableHeight - overlayHeight - 4)
                : Math.Max(0, availableHeight - 40);
            top = Math.Clamp(top, 0, maxTop);
        }
        else
        {
            top = Math.Max(0, top);
        }

        IntelliSenseControl.Margin = new Thickness(left, top, 0, 0);
    }

    private void OnEditorHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PositionIntelliSenseOverlay();
        // Reaplicar resaltado y diagnósticos por si el cambio de layout los afectó.
        ApplyHighlightAndDiagnostics();
    }

    private void OnIntelliSenseSizeChanged(object sender, SizeChangedEventArgs e)
        => PositionIntelliSenseOverlay();

    #endregion

    #region IntelliSense Commit (from overlay)

    private void OnIntelliSenseItemCommitted(object? sender, CompletionItem item)
    {
        if (item is null) return;

        EditorBox.Document.GetText(TextGetOptions.None, out string editorText);
        editorText ??= string.Empty;
        string normalized = DocumentText.Normalize(editorText);
        int currentCaretOffset = GetNormalizedCaretOffset(editorText, normalized);
        var currentCaret = DocumentText.ToLineCharacter(normalized, currentCaretOffset);
        if (!ViewModel.IsCompletionCurrent(new Position(currentCaret.Line, currentCaret.Character)))
        {
            ViewModel.InvalidateCompletions();
            return;
        }

        if (item.ReplaceRange is not { } range ||
            !DocumentText.TryGetNormalizedOffset(
                normalized,
                range.Start.Line,
                range.Start.Character,
                out int replaceStart) ||
            !DocumentText.TryGetNormalizedOffset(
                normalized,
                range.End.Line,
                range.End.Character,
                out int replaceEnd) ||
            replaceEnd < replaceStart ||
            replaceEnd != currentCaretOffset)
        {
            ViewModel.InvalidateCompletions();
            ViewModel.StatusText = "No se pudo aplicar el completado: rango de edición inválido.";
            EditorBox.Focus(FocusState.Programmatic);
            return;
        }

        if (item.InsertTextFormat is not (InsertTextFormat.PlainText or InsertTextFormat.Snippet))
        {
            ViewModel.InvalidateCompletions();
            ViewModel.StatusText = "No se pudo aplicar el completado: formato de inserción desconocido.";
            EditorBox.Focus(FocusState.Programmatic);
            return;
        }

        string completionText = item.HasExplicitInsertText
            ? item.InsertText
            : item.Label;
        string indentation = GetLineIndentation(normalized, replaceStart);
        (string insertion, int insertionCaretOffset) = ApplyCompletionText(
            completionText,
            item.InsertTextFormat,
            item.CaretOffset,
            indentation);

        // Ninguna solicitud o debounce anterior puede reabrir el overlay
        // usando el texto que el propio commit acaba de insertar.
        _completionDebounceTimer.Stop();
        CancelPendingCompletionRequest();

        // El estado anterior conserva exactamente el rango sustituido para
        // que el undo devuelva también el cursor al lugar del commit.
        var undoBefore = new EditorUndoState(normalized, replaceStart, replaceEnd);
        int[] editorMap = DocumentText.BuildNormalizedToEditorMap(editorText);
        int editorStart = editorMap[replaceStart];
        int editorEnd = editorMap[replaceEnd];

        string updatedNormalized;
        _applyingCompletion = true;
        try
        {
            EditorBox.Document.Selection.SetRange(editorStart, editorEnd);
            EditorBox.Document.Selection.TypeText(insertion);

            // RichEditBox puede convertir los saltos del snippet en párrafos.
            // El caret se calcula en texto normalizado y se traduce de nuevo a
            // offsets nativos mediante el mapa actualizado.
            EditorBox.Document.GetText(TextGetOptions.None, out string updatedEditorText);
            updatedNormalized = DocumentText.Normalize(updatedEditorText);
            int normalizedCaret = Math.Clamp(
                replaceStart + insertionCaretOffset,
                0,
                updatedNormalized.Length);
            int[] updatedMap = DocumentText.BuildNormalizedToEditorMap(updatedEditorText);
            int editorCaret = updatedMap[normalizedCaret];
            EditorBox.Document.Selection.SetRange(editorCaret, editorCaret);
        }
        finally
        {
            _applyingCompletion = false;
        }

        // Cada commit es una operación atómica e independiente de la
        // agrupación normal de escritura por pulsaciones.
        if (!string.Equals(updatedNormalized, normalized, StringComparison.Ordinal))
        {
            _undoHistory.Record(
                undoBefore,
                insertion,
                DateTime.UtcNow,
                forceNewStep: true);
            _lastNormalizedText = updatedNormalized;
        }

        CancelPendingCompletionRequest();
        ViewModel.InvalidateCompletions();
        _suppressedAutomaticCompletionText = updatedNormalized;
        UpdateUnsavedChangesFromText(updatedNormalized);

        EditorBox.Focus(FocusState.Programmatic);
        UpdateCursorCoordinates();
    }

    private static string GetLineIndentation(string text, int offset)
    {
        int lineStart = Math.Clamp(offset, 0, text.Length);
        while (lineStart > 0 && text[lineStart - 1] is not ('\r' or '\n'))
        {
            lineStart--;
        }

        int end = lineStart;
        while (end < text.Length && text[end] is ' ' or '\t')
        {
            end++;
        }

        return text[lineStart..end];
    }

    private int GetNormalizedCaretOffset(string editorText, string normalized)
    {
        int editorOffset = Math.Clamp(
            EditorBox.Document.Selection.StartPosition,
            0,
            editorText.Length);
        return Math.Min(
            DocumentText.ToNormalizedOffset(editorText, editorOffset),
            normalized.Length);
    }

    private static (string Insertion, int CaretOffset) ApplyCompletionText(
        string text,
        InsertTextFormat format,
        int? requestedCaretOffset,
        string indentation)
    {
        if (format == InsertTextFormat.PlainText)
        {
            int caret = Math.Clamp(
                requestedCaretOffset ?? text.Length,
                0,
                text.Length);
            return (text, caret);
        }

        int marker = text.IndexOf("$0", StringComparison.Ordinal);
        if (marker >= 0)
        {
            string before = text[..marker];
            string after = text[(marker + 2)..];
            string indentedBefore = IndentSnippetLines(before, indentation);
            string indentedAfter = IndentSnippetLines(after, indentation);
            return (indentedBefore + indentedAfter, indentedBefore.Length);
        }

        string insertion = IndentSnippetLines(text, indentation);
        int fallbackCaret = Math.Clamp(
            requestedCaretOffset ?? insertion.Length,
            0,
            insertion.Length);
        return (insertion, fallbackCaret);
    }

    private static string IndentSnippetLines(string text, string indentation)
    {
        if (!text.Contains('\n') || indentation.Length == 0)
        {
            return text;
        }

        string[] lines = text.Split('\n');
        for (int index = 1; index < lines.Length; index++)
        {
            lines[index] = indentation + lines[index];
        }

        return string.Join('\n', lines);
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
            CancelPendingCompletionRequest();
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
            _completionDebounceTimer.Stop();
            CancelPendingCompletionRequest();
            ViewModel.ToggleCommandPalette();
            if (ViewModel.IsCommandPaletteOpen)
                EditorBox.Focus(FocusState.Programmatic); // CommandInputBox focused via XAML
            e.Handled = true;
        }
        else if (isCtrl && (int)e.Key == 190) // 190 = OemPeriod (tecla punto normal)
        {
            // Ctrl+. (punto normal) - Quick fix
            _keyboardHandler.ApplyQuickFixAtCaret();
            e.Handled = true;
        }
        else if (isCtrl && e.Key == VirtualKey.S)
        {
            // Respaldo cuando el foco no está en el editor.
            SaveCurrentFile();
            e.Handled = true;
        }
        else if (isCtrl && e.Key == VirtualKey.O)
        {
            // Respaldo cuando el foco no está en el editor.
            OpenFolderPickerAsync();
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
            CancelPendingCompletionRequest();
            ViewModel.InvalidateCompletions();
            _suppressedAutomaticCompletionText = null;
            ApplyUndoState(target);
        }
    }

    private void RedoEditor()
    {
        var target = _undoHistory.Redo(CaptureUndoState());
        if (target is not null)
        {
            CancelPendingCompletionRequest();
            ViewModel.InvalidateCompletions();
            _suppressedAutomaticCompletionText = null;
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
        ViewModel.InvalidateCompletions();
        _applyingUndo = true;
        try
        {
            EditorBox.Document.SetText(TextSetOptions.None, DocumentText.ToEditorText(state.Text));

            // Las posiciones del historial están en offsets normalizados. El
            // editor usa \r como separador de párrafo, por lo que deben
            // traducirse con el mapa antes de fijar la selección.
            EditorBox.Document.GetText(TextGetOptions.None, out string editorText);
            int[] offsetMap = DocumentText.BuildNormalizedToEditorMap(editorText);
            int selectionStart = offsetMap[Math.Clamp(state.SelectionStart, 0, state.Text.Length)];
            int selectionEnd = offsetMap[Math.Clamp(state.SelectionEnd, 0, state.Text.Length)];
            EditorBox.Document.Selection.SetRange(selectionStart, selectionEnd);
            _lastNormalizedText = state.Text;
        }
        finally
        {
            _applyingUndo = false;
        }

        UpdateLineCountFromDocument();
        UpdateCursorCoordinates();
        UpdateUnsavedChangesFromText(state.Text);
        ScheduleAnalysis();
    }

    private void UpdateUnsavedChangesFromText(string normalizedText)
    {
        if (string.IsNullOrEmpty(ViewModel.SourceFilePath))
        {
            ViewModel.MarkUnsavedChanges(false);
            return;
        }

        ViewModel.MarkUnsavedChanges(
            !string.Equals(normalizedText, _savedNormalizedText, StringComparison.Ordinal));
    }

    #endregion

    #region Helpers

    private static bool TryGetExistingPath(string? argument, out string fullPath)
    {
        if (!TryGetFullPath(argument, out fullPath))
        {
            return false;
        }

        return Directory.Exists(fullPath) || File.Exists(fullPath);
    }

    private static bool TryGetFullPath(string? argument, out string fullPath)
    {
        fullPath = string.Empty;
        string candidate = (argument ?? string.Empty).Trim().Trim('"');
        if (candidate.Length == 0)
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    #endregion
}

