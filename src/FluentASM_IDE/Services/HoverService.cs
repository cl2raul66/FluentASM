using System;
using System.Threading;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Models;
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
/// <remarks>
/// Si la posición está dentro de un diagnóstico (error/advertencia), prioriza
/// el flyout del diagnóstico (gestionado por <see cref="DiagnosticAdorner"/>)
/// y no muestra el hover del LSP.
/// </remarks>
public sealed class HoverService
{
    private const double HoverMoveTolerance = 3.0;

    private readonly RichEditBox _editor;
    private readonly IEditorViewModel _vm;
    private readonly Flyout _flyout;
    private readonly TextBlock _flyoutContent;
    private readonly DispatcherTimer _hoverTimer;
    private Point _lastPointerPosition;
    private bool _hasUserInteracted;

    public HoverService(RichEditBox editor, IEditorViewModel vm)
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

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            ShowHoverAt(_lastPointerPosition);
        };

        // Solo activar hover tras interacción real del usuario (foco o clic).
        _editor.GotFocus += (_, _) => _hasUserInteracted = true;
        _editor.PointerPressed += (_, _) => _hasUserInteracted = true;
    }

    /// <summary>
    /// Llama desde <see cref="RichEditBox.PointerMoved"/> para iniciar el temporizador
    /// de hover y mostrar la documentación del símbolo bajo el cursor.
    /// </summary>
    public void OnPointerMoved(Point position)
    {
        if (!_hasUserInteracted) return;

        // Si el temporizador ya está contando y el puntero apenas se movió,
        // no se reinicia: así el temblor del ratón no impide que aparezca.
        if (_hoverTimer.IsEnabled && Distance(_lastPointerPosition, position) < HoverMoveTolerance)
        {
            return;
        }

        _lastPointerPosition = position;
        _hoverTimer.Stop();
        HideFlyout();
        _hoverTimer.Start();
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
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
        if (!_hasUserInteracted) return;

        _editor.Document.GetText(TextGetOptions.None, out string editorText);
        if (string.IsNullOrEmpty(editorText))
        {
            HideFlyout();
            return;
        }

        string normalized = DocumentText.Normalize(editorText);

        int offset = EditorHitTesting.GetOffset(_editor, position, _vm.ScrollOffset);
        if (offset < 0 || offset >= editorText.Length)
        {
            HideFlyout();
            return;
        }

        // PRIORIDAD: Si hay un diagnóstico en esta posición, NO mostrar hover LSP.
        // El DiagnosticAdorner ya muestra su propio flyout para diagnósticos.
        if (HasDiagnosticAtOffset(editorText, offset))
        {
            HideFlyout();
            return;
        }

        // Convertir a coordenadas LSP.
        int normalizedOffset = Math.Min(
            DocumentText.ToNormalizedOffset(editorText, offset),
            normalized.Length);
        var (line, character) = DocumentText.ToLineCharacter(normalized, normalizedOffset);

        // Obtener el hover del LSP (método síncrono).
        Hover? hover;
        try
        {
            hover = _vm.LspServer.GetHover(_vm.DocumentUri, new Position(line, character));
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

    private bool HasDiagnosticAtOffset(string editorText, int offset)
    {
        foreach (var diag in _vm.Diagnostics)
        {
            int start = DocumentText.ToEditorOffset(editorText, diag.Line - 1, diag.Column - 1);
            int end = DocumentText.ToEditorOffset(editorText, diag.EndLine - 1, diag.EndColumn - 1);
            if (end <= start) end = Math.Min(start + 1, editorText.Length);
            if (offset >= start && offset < end) return true;
        }
        return false;
    }

    private void HideFlyout()
    {
        _hoverTimer.Stop();
        if (_flyout.IsOpen) _flyout.Hide();
    }
}
