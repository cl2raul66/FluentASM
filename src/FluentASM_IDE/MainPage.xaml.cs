using System;
using System.Linq;
using FluentASM_IDE.Models;
using FluentASM_IDE.Services;
using FluentASM_IDE.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace FluentASM_IDE;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    private readonly DispatcherTimer _highlightDebounceTimer = new();
    private ScrollViewer? _editorInternalScrollViewer;
    private bool _isHighlighting;

    public MainPage()
    {
        InitializeComponent();

        _highlightDebounceTimer.Interval = TimeSpan.FromMilliseconds(120);
        _highlightDebounceTimer.Tick += (s, e) =>
        {
            _highlightDebounceTimer.Stop();
            RunSyntaxHighlighting();
        };

        Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            PulseStoryboard.Begin();
        }
        catch { }

        // Populate Editor with FluentASM sample code (CalculoPuro)
        EditorBox.Document.SetText(TextSetOptions.None, MainPageViewModel.InitialSampleAsm);

        // Update line numbers count
        UpdateLineCountFromDocument();

        // Run initial syntax highlighting
        RunSyntaxHighlighting();

        // Position caret at line 8 (Gpr0.imult<2,0>(Gpr1))
        PositionCaretAtInitialLine();

        // Synchronize Gutter scroll with Editor internal scroll viewer
        HookEditorScrollViewer();
    }

    private void HookEditorScrollViewer()
    {
        _editorInternalScrollViewer = FindVisualChild<ScrollViewer>(EditorBox);
        if (_editorInternalScrollViewer != null)
        {
            _editorInternalScrollViewer.ViewChanged += (s, args) =>
            {
                GutterScrollViewer.ChangeView(null, _editorInternalScrollViewer.VerticalOffset, null, true);
            };
        }
    }

    private void RunSyntaxHighlighting()
    {
        if (_isHighlighting) return;
        _isHighlighting = true;
        try
        {
            AsmHighlighter.Highlight(EditorBox.Document);
        }
        catch { }
        finally
        {
            _isHighlighting = false;
        }
    }

    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        if (_isHighlighting) return;

        UpdateLineCountFromDocument();
        UpdateCursorCoordinates();

        // Restart debounce timer for syntax highlighting
        _highlightDebounceTimer.Stop();
        _highlightDebounceTimer.Start();
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateCursorCoordinates();
    }

    private void UpdateLineCountFromDocument()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text))
        {
            ViewModel.UpdateLineCount(1);
            return;
        }

        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' || text[i] == '\n')
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                lines++;
            }
        }

        ViewModel.UpdateLineCount(lines);
    }

    private void UpdateCursorCoordinates()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        int pos = Math.Min(EditorBox.Document.Selection.StartPosition, text?.Length ?? 0);

        int line = 1;
        int col = 1;

        if (text != null && pos > 0)
        {
            for (int i = 0; i < pos; i++)
            {
                if (text[i] == '\r' || text[i] == '\n')
                {
                    if (text[i] == '\r' && i + 1 < pos && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    line++;
                    col = 1;
                }
                else
                {
                    col++;
                }
            }
        }

        ViewModel.UpdateCursorPosition(line, col);
    }

    private void PositionCaretAtInitialLine()
    {
        EditorBox.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text)) return;

        int targetLine = 8;
        int currentLine = 1;
        int targetPos = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (currentLine == targetLine)
            {
                targetPos = i;
                break;
            }

            if (text[i] == '\r' || text[i] == '\n')
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                currentLine++;
            }
        }

        // Set selection cursor to line 8
        EditorBox.Document.Selection.SetRange(targetPos + 12, targetPos + 12);
        UpdateCursorCoordinates();
    }

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isCtrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
                      Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        if (ViewModel.IsIntelliSenseOpen)
        {
            if (e.Key == VirtualKey.Down)
            {
                IntelliSenseControl.SelectNext();
                e.Handled = true;
                return;
            }
            else if (e.Key == VirtualKey.Up)
            {
                IntelliSenseControl.SelectPrevious();
                e.Handled = true;
                return;
            }
            else if (e.Key == VirtualKey.Enter)
            {
                IntelliSenseControl.CommitSelected();
                e.Handled = true;
                return;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                ViewModel.CloseIntelliSense();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == VirtualKey.Tab)
        {
            // Insert 4 spaces instead of losing focus
            e.Handled = true;
            EditorBox.Document.Selection.TypeText("    ");
        }
        else if (isCtrl && e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            ViewModel.ToggleIntelliSense();
        }
        else if (isCtrl && e.Key == VirtualKey.P)
        {
            e.Handled = true;
            ViewModel.ToggleCommandPalette();
            if (ViewModel.IsCommandPaletteOpen)
            {
                CommandInputBox?.Focus(FocusState.Programmatic);
            }
        }
        else if (e.Key == VirtualKey.F5)
        {
            e.Handled = true;
            var runCmd = ViewModel.AllCommands.FirstOrDefault(c => c.Command == "/run");
            if (runCmd != null) _ = ViewModel.ExecutePaletteItem(runCmd);
        }
        else if (isCtrl && e.Key == VirtualKey.B)
        {
            e.Handled = true;
            var buildCmd = ViewModel.AllCommands.FirstOrDefault(c => c.Command == "/build");
            if (buildCmd != null) _ = ViewModel.ExecutePaletteItem(buildCmd);
        }
    }

    private void OnIntelliSenseItemCommitted(object? sender, CompletionItem item)
    {
        if (item != null)
        {
            EditorBox.Document.Selection.TypeText(item.Label);
            ViewModel.CloseIntelliSense();
            EditorBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnBackdropPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.CloseCommandPalette();
    }

    private void OnPaletteContainerClicked(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnEscBadgeClicked(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.CloseCommandPalette();
        e.Handled = true;
    }

    private void OnPaletteItemHover(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CommandPaletteItem item)
        {
            foreach (var c in ViewModel.FilteredCommands) c.IsActive = (c == item);
        }
    }

    private void OnPaletteItemUnhover(object sender, PointerRoutedEventArgs e)
    {
    }

    private async void OnPaletteItemClicked(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CommandPaletteItem item)
        {
            await ViewModel.ExecutePaletteItem(item);
        }
    }

    private void OnCommandTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            ViewModel.CommandQuery = tb.Text;
        }
    }

    private async void OnCommandInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.CloseCommandPalette();
            e.Handled = true;
            EditorBox.Focus(FocusState.Programmatic);
        }
        else if (e.Key == VirtualKey.Down)
        {
            ViewModel.SelectNextPaletteItem();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up)
        {
            ViewModel.SelectPrevPaletteItem();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter)
        {
            await ViewModel.ExecuteActivePaletteItem();
            e.Handled = true;
            EditorBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isCtrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
                      Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        if (e.Key == VirtualKey.Escape)
        {
            if (ViewModel.IsCommandPaletteOpen)
            {
                ViewModel.CloseCommandPalette();
                e.Handled = true;
            }
            else if (ViewModel.IsIntelliSenseOpen)
            {
                ViewModel.CloseIntelliSense();
                e.Handled = true;
            }
        }
        else if (isCtrl && e.Key == VirtualKey.P)
        {
            ViewModel.ToggleCommandPalette();
            if (ViewModel.IsCommandPaletteOpen)
            {
                CommandInputBox?.Focus(FocusState.Programmatic);
            }
            e.Handled = true;
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
            if (result != null) return result;
        }
        return null;
    }
}
