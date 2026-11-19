using System;
using System.Collections.Generic;
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
/// para mantener <see cref="MainPage"/> limpia y enfocada en coordinación de alto nivel.
/// </summary>
public sealed class DiagnosticAdorner
{
    private static readonly Color ErrorColor = Color.FromArgb(255, 255, 180, 171);
    private static readonly Color WarningColor = Color.FromArgb(255, 255, 212, 190);
    private static readonly Color InfoColor = Color.FromArgb(255, 189, 200, 207);

    private readonly RichEditBox _editor;
    private readonly IMainPageViewModel _vm;
    private readonly DispatcherTimer _hoverTimer;
    private readonly Flyout _flyout;
    private readonly TextBlock _flyoutCode;
    private readonly TextBlock _flyoutSeverity;
    private readonly TextBlock _flyoutMessage;
    private readonly List<(int Start, int End)> _appliedRanges = new();
    private DiagnosticItem? _hoveredDiagnostic;
    private Point _lastPointerPosition;
    private bool _isApplying;
    private bool _isLoaded;

    public DiagnosticAdorner(RichEditBox editor, IMainPageViewModel vm)
    {
        _editor = editor;
        _vm = vm;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
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
            FontSize = 11
        };
        _flyoutMessage = new TextBlock
        {
            FontSize = 12.5,
            MaxWidth = 380,
            TextWrapping = TextWrapping.Wrap
        };

        var content = new StackPanel { MinWidth = 260, Spacing = 4 };
        content.Children.Add(_flyoutCode);
        content.Children.Add(_flyoutSeverity);
        content.Children.Add(_flyoutMessage);

        _flyout = new Flyout
        {
            Content = content,
            Placement = FlyoutPlacementMode.Top
        };

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
        _lastPointerPosition = e.GetCurrentPoint(_editor).Position;
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    public void OnPointerExited() => HideFlyout();

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
        catch { /* editor not ready */ }
        finally { _isApplying = false; }
    }

    private void ShowFlyoutAt(Point position)
    {
        try
        {
            var pointRange = _editor.Document.GetRangeFromPoint(position, PointOptions.ClientCoordinates);
            var diag = FindDiagnosticAtOffset(pointRange.StartPosition);
            if (diag == null) { HideFlyout(); return; }

            if (ReferenceEquals(_hoveredDiagnostic, diag) && _flyout.IsOpen) return;

            _hoveredDiagnostic = diag;
            _flyoutCode.Text = diag.Code;
            _flyoutSeverity.Text = diag.Severity;
            _flyoutSeverity.Foreground = diag.SeverityBrush;
            _flyoutMessage.Text = diag.Message;

            _flyout.ShowAt(_editor, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.Top,
                Position = new Point(Math.Max(0, position.X), Math.Max(0, position.Y)),
                ShowMode = FlyoutShowMode.Transient
            });
        }
        catch { HideFlyout(); }
    }

    private DiagnosticItem? FindDiagnosticAtOffset(int offset)
    {
        _editor.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text)) return null;

        foreach (var d in _vm.Diagnostics)
        {
            int s = DocumentText.ToEditorOffset(text, d.Line - 1, d.Column - 1);
            int e = DocumentText.ToEditorOffset(text, d.EndLine - 1, d.EndColumn - 1);
            if (e <= s) e = Math.Min(s + 1, text.Length);
            if (offset >= s && offset < e) return d;
        }
        return null;
    }

    private void HideFlyout()
    {
        _hoverTimer.Stop();
        _hoveredDiagnostic = null;
        if (_flyout.IsOpen) _flyout.Hide();
    }
}













