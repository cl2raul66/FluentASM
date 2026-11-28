using System;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Models;
using FluentASM.Lsp.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;

namespace FluentASM_IDE.Services;

/// <summary>
/// Muestra un outline (árbol de símbolos) del documento actual mediante un Flyout.
/// Los símbolos se obtienen de <see cref="ILanguageService.GetDocumentSymbols"/> y
/// se organizan por rutina con sus etiquetas locales como hijos.
/// </summary>
public sealed class OutlineService
{
    private readonly IEditorViewModel _vm;
    private readonly RichEditBox _editor;
    private readonly Flyout _flyout;
    private readonly StackPanel _stack;
    private bool _isOpen;

    public OutlineService(IEditorViewModel vm, RichEditBox editor)
    {
        _vm = vm;
        _editor = editor;

        _stack = new StackPanel
        {
            Spacing = 6,
            MaxWidth = 360
        };

        var closeButton = new Button
        {
            Content = "Cerrar",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 8, 0)
        };
        closeButton.Click += (_, _) => Hide();

        var title = new TextBlock
        {
            Text = "Outline del documento",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 226, 225))
        };

        var scroll = new ScrollViewer
        {
            Content = _stack,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 25, 28, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 96, 205, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    title,
                    scroll,
                    closeButton
                }
            },
            MaxWidth = 400
        };

        _flyout = new Flyout
        {
            Content = border,
            Placement = FlyoutPlacementMode.Top,
            ShowMode = FlyoutShowMode.Transient
        };
    }

    /// <summary>
    /// Muestra el outline del documento actual.
    /// </summary>
    public void Show()
    {
        if (_isOpen) return;

        _stack.Children.Clear();

        var symbols = _vm.LspServer.GetDocumentSymbols(_vm.DocumentUri);
        foreach (var symbol in symbols)
        {
            AddSymbolBlock(symbol, 0);
        }

        if (_stack.Children.Count > 0)
        {
            _isOpen = true;
            _flyout.ShowAt(_editor, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.Top,
                Position = new Point(16, 80),
                ShowMode = FlyoutShowMode.Transient
            });
        }
    }

    private void AddSymbolBlock(DocumentSymbol symbol, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        var kindLabel = symbol.Kind switch
        {
            SymbolKind.Module => "▤ ",
            SymbolKind.Namespace => "§ ",
            SymbolKind.Function => "ƒ ",
            SymbolKind.Field => "• ",
            SymbolKind.Method => "▶ ",
            _ => ""
        };

        var textBlock = new TextBlock
        {
            Text = indent + kindLabel + symbol.Name,
            FontSize = 12.5,
            FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 226, 225)),
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 2, 0, 2),
        };

        // Al hacer clic, navegar al símbolo.
        textBlock.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            NavigateToSymbol(symbol);
            Hide();
        };

        _stack.Children.Add(textBlock);

        if (symbol.Children is { Count: > 0 } children)
        {
            foreach (var child in children)
            {
                AddSymbolBlock(child, indentLevel + 1);
            }
        }
    }

    private void NavigateToSymbol(DocumentSymbol symbol)
    {
        var start = symbol.Range.Start;
        _editor.Document.GetText(TextGetOptions.None, out string editorText);
        if (string.IsNullOrEmpty(editorText)) return;

        int startOffset = DocumentText.ToEditorOffset(
            editorText, start.Line, start.Character);
        int endOffset = DocumentText.ToEditorOffset(
            editorText, symbol.Range.End.Line, symbol.Range.End.Character);

        if (endOffset <= startOffset)
            endOffset = Math.Min(startOffset + 1, editorText.Length);

        _editor.Document.Selection.SetRange(startOffset, endOffset);
        _editor.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Oculta el flyout de outline.
    /// </summary>
    public void Hide()
    {
        _isOpen = false;
        if (_flyout.IsOpen) _flyout.Hide();
    }

    /// <summary>
    /// Indica si el outline está actualmente abierto.
    /// </summary>
    public bool IsOpen => _isOpen;
}
