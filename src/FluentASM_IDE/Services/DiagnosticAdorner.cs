using System;
using System.Collections.Generic;
using System.Linq;
using FluentASM_IDE.Models;
using FluentASM_IDE.Common.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace FluentASM_IDE.Services;

/// <summary>
/// Encapsula toda la lógica de adornos de diagnóstico (subrayados, colores, Flyout hover)
/// para mantener el componente de editor limpio y enfocado en coordinación de alto nivel.
/// </summary>
/// <remarks>
/// Usa <see cref="Flyout"/> con <see cref="FlyoutShowMode.Standard"/> para que el usuario pueda
/// mover el ratón sobre la tarjeta y pulsar el botón de quick-fix sin que se cierre.
/// Se cierra al hacer clic fuera, pulsar Escape, o al cambiar de diagnóstico.
/// </remarks>
public sealed class DiagnosticAdorner
{
    private const double HoverMoveTolerance = 3.0;

    private static readonly Color ErrorColor = Color.FromArgb(255, 255, 180, 171);
    private static readonly Color WarningColor = Color.FromArgb(255, 255, 212, 190);
    private static readonly Color InfoColor = Color.FromArgb(255, 189, 200, 207);

    private readonly RichEditBox _editor;
    private readonly IEditorViewModel _vm;
    private readonly DispatcherTimer _hoverTimer;
    private readonly Flyout _flyout;
    private readonly StackPanel _flyoutContent;
    private readonly TextBlock _flyoutCode;
    private readonly TextBlock _flyoutSeverity;
    private readonly TextBlock _flyoutMessage;
    private readonly Button _flyoutQuickFixButton;
    private readonly List<(int Start, int End)> _appliedRanges = new();
    private DiagnosticItem? _hoveredDiagnostic;
    private QuickFixAction? _hoveredQuickFix;
    private Point _lastPointerPosition;
    private bool _isApplying;
    private bool _isLoaded;
    private bool _flyoutOpenedByKeyboard;

    public DiagnosticAdorner(RichEditBox editor, IEditorViewModel vm)
    {
        _editor = editor;
        _vm = vm;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _hoverTimer.Tick += (_, _) => ShowFlyoutAt(_lastPointerPosition);

        _flyoutCode = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 96, 205, 255))
        };
        _flyoutSeverity = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 226, 225))
        };
        _flyoutMessage = new TextBlock
        {
            FontSize = 12.5,
            MaxWidth = 380,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 226, 225))
        };

        _flyoutQuickFixButton = new Button
        {
            Content = "Aplicar corrección",
            FontSize = 12,
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        _flyoutQuickFixButton.Click += (_, _) => ApplyHoveredQuickFix();

        _flyoutContent = new StackPanel { MinWidth = 260, Spacing = 4 };
        _flyoutContent.Children.Add(_flyoutCode);
        _flyoutContent.Children.Add(_flyoutSeverity);
        _flyoutContent.Children.Add(_flyoutMessage);
        _flyoutContent.Children.Add(_flyoutQuickFixButton);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 25, 28, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 96, 205, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Child = _flyoutContent
        };

        _flyout = new Flyout
        {
            Content = border,
            Placement = FlyoutPlacementMode.Top,
            ShowMode = FlyoutShowMode.Standard
        };
        // Con ShowMode.Standard, el Flyout no se cierra al hacer clic fuera
        // inmediatamente; se cierra al pulsar Escape o al llamar a Hide().

        _vm.DiagnosticsChanged += OnDiagnosticsChanged;
    }

    public void SetLoaded(bool loaded) => _isLoaded = loaded;

    public void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (!_isLoaded || _vm.Diagnostics.Count == 0)
        {
            HideFlyout();
            return;
        }

        var position = e.GetCurrentPoint(_editor).Position;

        // Si el flyout fue abierto por teclado, no dejar que el ratón lo oculte
        // ni reinicie el temporizador. Solo cambiar a modo ratón si el puntero
        // entra en un diagnóstico diferente O si está sobre el mismo (para que
        // PointerExited pueda cerrarlo al salir).
        if (_flyoutOpenedByKeyboard && _flyout.IsOpen)
        {
            // Verificar si el puntero está sobre un diagnóstico (el mismo o diferente)
            var diagAtPointer = FindDiagnosticAtPoint(position, out _);
            if (diagAtPointer is not null)
            {
                // Ratón sobre un diagnóstico (sea el mismo u otro): cambiar a modo ratón
                // para que PointerExited funcione correctamente.
                _flyoutOpenedByKeyboard = false;
            }
            else
            {
                // Ratón fuera de cualquier diagnóstico: ignorar, mantener modo teclado
                return;
            }
        }

        // Si el temporizador ya está contando y el puntero apenas se movió,
        // no se reinicia: así el temblor del ratón no impide que aparezca.
        if (_hoverTimer.IsEnabled && Distance(_lastPointerPosition, position) < HoverMoveTolerance)
        {
            return;
        }

        _lastPointerPosition = position;
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    public void OnPointerExited()
    {
        // No ocultar si el flyout fue abierto por teclado
        if (!_flyoutOpenedByKeyboard)
        {
            HideFlyout();
        }
    }

    public void OnPointerEntered()
    {
        // No-op: reservado para futura lógica de transición ratón/teclado
    }

    /// <summary>
    /// Muestra el Flyout en la posición del caret actual (navegación por teclado).
    /// </summary>
    public void ShowAtCaret()
    {
        if (!_isLoaded || _vm.Diagnostics.Count == 0) return;

        // Obtener posición del caret en pantalla.
        _editor.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text)) return;

        int caretOffset = _editor.Document.Selection.StartPosition;
        var diag = FindDiagnosticAtOffset(caretOffset);
        if (diag is null)
        {
            // No hay diagnóstico en el caret: ocultar si estaba abierto por teclado
            if (_flyoutOpenedByKeyboard)
            {
                HideFlyout();
            }
            return;
        }

        // Obtener rectángulo del diagnóstico para anclar el Flyout.
        if (TryGetDiagnosticOffsets(text, diag, out int s, out int e))
        {
            try
            {
                var range = _editor.Document.GetRange(s, Math.Min(e, text.Length));
                range.GetRect(PointOptions.ClientCoordinates, out Rect rect, out _);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    // Convertir a coordenadas del control (viewport).
                    var visual = new Point(rect.X, rect.Y - _vm.ScrollOffset);
                    _flyoutOpenedByKeyboard = true;
                    ShowFlyoutAt(visual, diag);
                }
            }
            catch
            {
                // Fallback: usar posición del caret.
                _editor.Document.Selection.GetRect(PointOptions.ClientCoordinates, out Rect caretRect, out _);
                var visual = new Point(caretRect.X, caretRect.Y - _vm.ScrollOffset);
                _flyoutOpenedByKeyboard = true;
                ShowFlyoutAt(visual, diag);
            }
        }
    }

    private void ShowFlyoutAt(Point position, DiagnosticItem? diag = null)
    {
        try
        {
            var foundDiag = diag ?? FindDiagnosticAtPoint(position, out int matchedOffset);
            if (foundDiag is null)
            {
                HideFlyout();
                return;
            }

            if (ReferenceEquals(_hoveredDiagnostic, foundDiag) && _flyout.IsOpen) return;

            _hoveredDiagnostic = foundDiag;
            _flyoutCode.Text = foundDiag.Code;
            _flyoutSeverity.Text = foundDiag.Severity;
            _flyoutSeverity.Foreground = foundDiag.SeverityBrush;
            _flyoutMessage.Text = foundDiag.Message;

            // Buscar acción rápida asociada a este diagnóstico
            _hoveredQuickFix = _vm.QuickFixActions
                .FirstOrDefault(q => string.Equals(q.DiagnosticId, foundDiag.Code, StringComparison.OrdinalIgnoreCase));

            _flyoutQuickFixButton.Visibility = _hoveredQuickFix is not null ? Visibility.Visible : Visibility.Collapsed;
            if (_hoveredQuickFix is not null)
            {
                _flyoutQuickFixButton.Content = $"Aplicar: {_hoveredQuickFix.Title}";
            }

            _flyout.ShowAt(_editor, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.Top,
                Position = new Point(Math.Max(0, position.X), Math.Max(0, position.Y)),
                ShowMode = FlyoutShowMode.Standard
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShowFlyoutAt] Exception: {ex}");
            HideFlyout();
        }
    }

    private void ApplyHoveredQuickFix()
    {
        if (_hoveredQuickFix is not null)
        {
            _vm.ApplyQuickFix(_hoveredQuickFix);
        }
        HideFlyout();
    }

    private DiagnosticItem? FindDiagnosticAtPoint(Point position, out int matchedOffset)
    {
        matchedOffset = -1;
        _editor.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text)) return null;

        // 1) Prueba geométrica: el rectángulo del diagnóstico cubre la palabra
        //    completa. GetRect devuelve coordenadas del documento, así que se
        //    restaura el scroll para compararlo con el puntero del viewport.
        foreach (var d in _vm.Diagnostics)
        {
            if (!TryGetDiagnosticOffsets(text, d, out int s, out int e)) continue;

            try
            {
                var range = _editor.Document.GetRange(s, Math.Min(e, text.Length));
                range.GetRect(PointOptions.ClientCoordinates, out Rect rect, out _);
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                var visual = new Rect(
                    rect.X,
                    rect.Y - _vm.ScrollOffset,
                    rect.Width,
                    rect.Height);
                var padded = new Rect(
                    visual.X - 6,
                    visual.Y - 4,
                    visual.Width + 12,
                    visual.Height + 8);

                if (padded.Contains(position))
                {
                    matchedOffset = s;
                    return d;
                }
            }
            catch
            {
                // Sin rectángulo: se usará la vía por offset.
            }
        }

        // 2) Respaldo por offset ya corregido con el scroll.
        int offset = EditorHitTesting.GetOffset(_editor, position, _vm.ScrollOffset);
        var fallback = FindDiagnosticAtOffset(offset);
        if (fallback is not null)
        {
            matchedOffset = offset;
        }

        return fallback;
    }

    private static bool TryGetDiagnosticOffsets(
        string text,
        DiagnosticItem d,
        out int start,
        out int end)
    {
        start = DocumentText.ToEditorOffset(text, d.Line - 1, d.Column - 1);
        end = DocumentText.ToEditorOffset(text, d.EndLine - 1, d.EndColumn - 1);
        if (end <= start)
        {
            end = Math.Min(start + 1, text.Length);
        }

        return start >= 0 && start < text.Length && end > start;
    }

    private DiagnosticItem? FindDiagnosticAtOffset(int offset)
    {
        _editor.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text)) return null;

        foreach (var d in _vm.Diagnostics)
        {
            if (TryGetDiagnosticOffsets(text, d, out int s, out int e) && offset >= s && offset < e)
            {
                return d;
            }
        }
        return null;
    }

    private void HideFlyout()
    {
        _hoverTimer.Stop();
        _hoveredDiagnostic = null;
        _hoveredQuickFix = null;
        _flyoutQuickFixButton.Visibility = Visibility.Collapsed;
        _flyoutOpenedByKeyboard = false;
        if (_flyout.IsOpen) _flyout.Hide();
    }

    private void OnDiagnosticsChanged()
    {
        HideFlyout();
        ApplyFormatting();
    }

    public void ApplyFormatting()
    {
        if (!_isLoaded || _isApplying) return;

        _isApplying = true;
        try
        {
            _editor.Document.GetText(TextGetOptions.None, out string text);
            if (string.IsNullOrEmpty(text)) return;

            int selStart = _editor.Document.Selection.StartPosition;
            int selEnd = _editor.Document.Selection.EndPosition;

            _editor.Document.BatchDisplayUpdates();
            try
            {
                // Restore previous ranges to default color (highlighter will restore syntax colors)
                foreach (var (ps, pe) in _appliedRanges)
                {
                    int s = Math.Clamp(ps, 0, text.Length);
                    int e = Math.Clamp(pe, s, text.Length);
                    if (e > s)
                        _editor.Document.GetRange(s, e).CharacterFormat.ForegroundColor = AsmHighlighter.ColorDefault;
                }

                _appliedRanges.Clear();

                // Clear all underlines first
                var allRange = _editor.Document.GetRange(0, text.Length);
                allRange.CharacterFormat.Underline = UnderlineType.None;

                foreach (var diag in _vm.Diagnostics)
                {
                    int start = DocumentText.ToEditorOffset(text, diag.Line - 1, diag.Column - 1);
                    int end = DocumentText.ToEditorOffset(text, diag.EndLine - 1, diag.EndColumn - 1);

                    if (end <= start)
                    {
                        if (start < text.Length && text[start] != '\r' && text[start] != '\n')
                            end = start + 1;
                        else
                        {
                            start = Math.Max(0, start - 1);
                            end = Math.Min(text.Length, start + 1);
                        }
                    }
                    if (start >= text.Length || end <= start) continue;

                    int safeEnd = Math.Min(end, text.Length);
                    var range = _editor.Document.GetRange(start, safeEnd);
                    range.CharacterFormat.ForegroundColor = diag.IsError
                        ? ErrorColor
                        : diag.IsWarning ? WarningColor : InfoColor;
                    range.CharacterFormat.Underline = diag.IsError
                        ? UnderlineType.Wave
                        : diag.IsWarning ? UnderlineType.Dotted : UnderlineType.Single;

                    _appliedRanges.Add((start, safeEnd));
                }
            }
            finally
            {
                _editor.Document.Selection.SetRange(selStart, selEnd);
                _editor.Document.ApplyDisplayUpdates();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiagnosticAdorner.ApplyFormatting] Exception: {ex}");
        }
        finally { _isApplying = false; }
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
