using System;
using System.Threading;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Models;
using FluentASM_IDE.ViewModels;
using FluentASM.Lsp.Protocol;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace FluentASM_IDE.Services;

/// <summary>
/// Gestiona el hover de símbolos del LSP sobre el editor de código.
/// Al posar el cursor sobre un registro, verbo, bandera, rutina, dato o
/// cualquier elemento del lenguaje, muestra una tarjeta Flyout con su
/// documentación, firma y metadatos.
/// </summary>
public sealed class HoverService
{
    private readonly RichEditBox _editor;
    private readonly MainPageViewModel _vm;
    private readonly Flyout _flyout;
    private readonly TextBlock _flyoutContent;
    private readonly DispatcherTimer _hoverTimer;
    private Point _lastPointerPosition;

    public HoverService(RichEditBox editor, MainPageViewModel vm)
    {
        _editor = editor;
        _vm = vm;

        _flyoutContent = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"),
            FontSize = 13,
            MaxWidth = 420,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 226, 225))
        };

        var background = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 25, 28, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 96, 205, 255)),
            BorderThickness = new Thickness(1, 1, 1, 1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Child = _flyoutContent
        };

        _flyout = new Flyout
        {
            Content = background,
            Placement = FlyoutPlacementMode.Top,
            ShowMode = FlyoutShowMode.Transient
        };

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            ShowHoverAt(_lastPointerPosition);
        };
    }

    /// <summary>
    /// Llama desde <see cref="RichEditBox.PointerMoved"/> para iniciar el temporizador
    /// de hover y mostrar la documentación del símbolo bajo el cursor.
    /// </summary>
    public void OnPointerMoved(Point position)
    {
        _lastPointerPosition = position;
        _hoverTimer.Stop();
        HideFlyout();
        _hoverTimer.Start();
    }

    /// <summary>
    /// Oculta el flyout de hover (llamar desde <see cref="RichEditBox.PointerExited"/>).
    /// </summary>
    public void OnPointerExited()
    {
        _hoverTimer.Stop();
        HideFlyout();
    }

    private void ShowHoverAt(Point position)
    {
        // Obtener el carácter bajo el cursor.
        ITextRange pointRange;
        try
        {
            pointRange = _editor.Document.GetRangeFromPoint(position, PointOptions.ClientCoordinates);
        }
        catch
        {
            HideFlyout();
            return;
        }

        int offset = pointRange.StartPosition;
        _editor.Document.GetText(TextGetOptions.None, out string editorText);
        if (string.IsNullOrEmpty(editorText)) return;

        if (offset < 0 || offset >= editorText.Length)
        {
            HideFlyout();
            return;
        }

        // Convertir a coordenadas LSP.
        string normalized = DocumentText.Normalize(editorText);
        int normalizedOffset = Math.Min(DocumentText.ToNormalizedOffset(editorText, offset), normalized.Length);
        var (line, character) = DocumentText.ToLineCharacter(normalized, normalizedOffset);
        var lspPosition = new Position(line, character);

        // Obtener el hover del LSP (método síncrono).
        Hover? hover;
        try
        {
            hover = _vm.LspServer.GetHover(_vm.DocumentUri, lspPosition);
        }
        catch
        {
            hover = null;
        }

        if (hover is null || string.IsNullOrWhiteSpace(hover.Contents?.Value))
        {
            HideFlyout();
            return;
        }

        _flyoutContent.Text = hover.Contents.Value;

        _flyout.ShowAt(_editor, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Top,
            Position = new Point(Math.Max(0, position.X), Math.Max(0, position.Y)),
        });
    }

    private void HideFlyout()
    {
        _hoverTimer.Stop();
        if (_flyout.IsOpen) _flyout.Hide();
    }
}
