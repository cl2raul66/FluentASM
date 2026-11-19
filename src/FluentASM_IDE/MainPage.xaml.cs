using System;
using FluentASM_IDE.Models;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Services;
using FluentASM_IDE.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace FluentASM_IDE;

public sealed partial class MainPage : Page
{
    #region Fields

    public MainPageViewModel ViewModel { get; } = new();

    private readonly DispatcherTimer _highlightDebounceTimer = new();
    private readonly DispatcherTimer _lspDebounceTimer = new();
    private readonly DiagnosticAdorner _diagnosticAdorner;
    private readonly EditorKeyboardHandler _keyboardHandler;
    private ScrollViewer? _editorInternalScrollViewer;
    private bool _isHighlighting;

    #endregion

    #region Constructor & Initialization

    public MainPage()
    {
        InitializeComponent();

        _diagnosticAdorner = new DiagnosticAdorner(EditorBox, ViewModel);
        _keyboardHandler = new EditorKeyboardHandler(EditorBox, ViewModel, IntelliSenseControl);

        InitializeTimers();
        SubscribeViewModelEvents();

        ActualThemeChanged += OnActualThemeChanged;
        Loaded += MainPage_Loaded;
    }

    private void InitializeTimers()
    {
        _highlightDebounceTimer.Interval = TimeSpan.FromMilliseconds(120);
        _highlightDebounceTimer.Tick += (_, _) =>
        {
            _highlightDebounceTimer.Stop();
            RunSyntaxHighlighting();
        };

        _lspDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _lspDebounceTimer.Tick += (_, _) =>
        {
            _lspDebounceTimer.Stop();
            SyncCurrentDocument();
        };
    }

    private void SubscribeViewModelEvents()
    {
        ViewModel.RevalidationRequested += OnRevalidationRequested;
        ViewModel.DiagnosticsChanged += () => _diagnosticAdorner.ApplyFormatting();
        ViewModel.DiagnosticNavigationRequested += NavigateToDiagnostic;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _diagnosticAdorner.SetLoaded(true);

        ConfigureEditorParagraphFormat();
        LoadInitialSample();
        SyncInitialDocument();
        SetupScrollSync();
        PositionCaretAtInitialLine();

        bool isDark = ActualTheme != ElementTheme.Light;
        AsmHighlighter.IsCurrentDark = isDark;
        ViewModel.UpdateTheme(isDark);

        RunSyntaxHighlighting();
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

    private void SyncInitialDocument()
    {
        UpdateLineCountFromDocument();
        ViewModel.SyncDocumentWithLsp(MainPageViewModel.InitialSampleAsm);
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
        RunSyntaxHighlighting();
    }

    private void RunSyntaxHighlighting()
    {
        if (_isHighlighting) return;
        _isHighlighting = true;
        try
        {
            AsmHighlighter.Highlight(EditorBox);
            _diagnosticAdorner.ApplyFormatting();
        }
        catch { }
        finally { _isHighlighting = false; }
    }

    #endregion

    #region Editor Events (Text, Selection, Pointer)

    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        if (_isHighlighting) return;

        _diagnosticAdorner.OnPointerExited(); // close any open flyout
        UpdateLineCountFromDocument();
        UpdateCursorCoordinates();

        // Validación LSP INMEDIATA para feedback instantáneo (errores/warnings al momento)
        SyncCurrentDocument();

        // Debounce highlighter (solo resaltado sintaxis, no LSP)
        _highlightDebounceTimer.Stop();
        _highlightDebounceTimer.Start();

        // Debounce LSP adicional para evitar saturación en escritura rápida
        _lspDebounceTimer.Stop();
        _lspDebounceTimer.Start();
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
        => UpdateCursorCoordinates();

    private void OnEditorPointerMoved(object sender, PointerRoutedEventArgs e)
        => _diagnosticAdorner.OnPointerMoved(e);

    private void OnEditorPointerExited(object sender, PointerRoutedEventArgs e)
        => _diagnosticAdorner.OnPointerExited();

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
        => _keyboardHandler.OnKeyDown(e);

    #endregion

    #region LSP Synchronization

    private void SyncCurrentDocument()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        ViewModel.SyncDocumentWithLsp(text ?? string.Empty);
    }

    private void OnRevalidationRequested()
    {
        SyncCurrentDocument();
        ViewModel.StatusText = "Documento revalidado";

        _highlightDebounceTimer.Stop();
        _highlightDebounceTimer.Start();
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

