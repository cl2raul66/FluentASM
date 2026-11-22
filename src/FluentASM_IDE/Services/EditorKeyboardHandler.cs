using FluentASM_IDE.Models;
using FluentASM_IDE.Common.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace FluentASM_IDE.Services;

/// <summary>
/// Maneja atajos de teclado y navegación del editor, desacoplando la lógica de <see cref="MainPage"/>.
/// </summary>
internal sealed class EditorKeyboardHandler
{
    private readonly RichEditBox _editor;
    private readonly IMainPageViewModel _vm;

    public EditorKeyboardHandler(RichEditBox editor, IMainPageViewModel vm)
    {
        _editor = editor;
        _vm = vm;
    }

    public void OnKeyDown(KeyRoutedEventArgs e)
    {
        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
                       CoreVirtualKeyStates.Down) != 0;
        bool isShift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) &
                        CoreVirtualKeyStates.Down) != 0;

        // La navegación del overlay se atiende en PreviewKeyDown para que
        // Ningún control pueda consumir antes las flechas o Intro.

        // Global editor shortcuts
        if (e.Key == VirtualKey.Tab)
        {
            e.Handled = true;
            _editor.Document.Selection.TypeText("    ");
        }
        else if (e.Key == VirtualKey.Enter)
        {
            // Auto-indent: mantener la indentación de la línea actual.
            e.Handled = true;
            HandleEnterKey();
        }
        else if (e.Key == VirtualKey.F5)
        {
            e.Handled = true;
            _vm.ExecuteCommand("/run");
        }
        else if (isCtrl && e.Key == VirtualKey.B)
        {
            e.Handled = true;
            _vm.ExecuteCommand("/build");
        }
        else if (isCtrl && e.Key == VirtualKey.P)
        {
            e.Handled = true;
            _vm.ToggleCommandPalette();
            if (_vm.IsCommandPaletteOpen)
                _editor.Focus(FocusState.Programmatic); // CommandInputBox focused via XAML
        }
        else if (isCtrl && isShift && e.Key == VirtualKey.O)
        {
            e.Handled = true;
            _vm.ExecuteCommand("/simbolos");
        }
        else if (isCtrl && e.Key == VirtualKey.Decimal)
        {
            // Ctrl+. (teclado numérico) - Quick fix
            // Para tecla punto normal (OemPeriod no existe en WinUI), usar Decimal que cubre ambos en la práctica
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine($"[EditorKeyboardHandler] Ctrl+. pressed, calling ApplyQuickFixAtCaret");
            ApplyQuickFixAtCaret();
        }
    }

    private void HandleEnterKey()
    {
        _editor.Document.GetText(TextGetOptions.None, out string editorText);
        editorText ??= string.Empty;

        int caretOffset = _editor.Document.Selection.StartPosition;
        if (caretOffset < 0 || caretOffset >= editorText.Length)
        {
            _editor.Document.Selection.TypeText("\n");
            return;
        }

        // Encontrar el inicio de la línea actual.
        int lineStart = caretOffset;
        while (lineStart > 0 && editorText[lineStart - 1] != '\n' && editorText[lineStart - 1] != '\r')
        {
            lineStart--;
        }

        // Extraer la indentación de la línea actual (espacios/tabs iniciales).
        string currentLine = editorText.Substring(lineStart, caretOffset - lineStart);
        string indent = "";
        foreach (char c in currentLine)
        {
            if (c == ' ' || c == '\t') indent += c;
            else break;
        }

        // Insertar salto de línea + indentación.
        _editor.Document.Selection.TypeText("\n" + indent);
    }

    public void ApplyQuickFixAtCaret()
    {
        System.Diagnostics.Debug.WriteLine($"[ApplyQuickFixAtCaret] Start");
        // Obtener el offset del caret en el texto del editor
        int caretOffset = _editor.Document.Selection.StartPosition;
        _editor.Document.GetText(TextGetOptions.None, out string editorText);
        if (string.IsNullOrEmpty(editorText))
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyQuickFixAtCaret] Empty editorText");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ApplyQuickFixAtCaret] caretOffset={caretOffset}, editorText.Length={editorText.Length}, Diagnostics={_vm.Diagnostics.Count}, QuickFixActions={_vm.QuickFixActions.Count}");

        if (caretOffset < 0 || caretOffset >= editorText.Length)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyQuickFixAtCaret] caretOffset out of range");
            return;
        }

        // Buscar diagnóstico en la posición del caret
        foreach (var diag in _vm.Diagnostics)
        {
            int start = DocumentText.ToEditorOffset(editorText, diag.Line - 1, diag.Column - 1);
            int end = DocumentText.ToEditorOffset(editorText, diag.EndLine - 1, diag.EndColumn - 1);
            if (end <= start) end = Math.Min(start + 1, editorText.Length);
            System.Diagnostics.Debug.WriteLine($"[ApplyQuickFixAtCaret] Checking diag {diag.Code}: start={start}, end={end}, caretOffset={caretOffset}, match={caretOffset >= start && caretOffset < end}");
            if (caretOffset >= start && caretOffset < end)
            {
                // Buscar acción rápida para este diagnóstico
                var quickFix = _vm.QuickFixActions
                    .FirstOrDefault(q => string.Equals(q.DiagnosticId, diag.Code, StringComparison.OrdinalIgnoreCase));
                System.Diagnostics.Debug.WriteLine($"[ApplyQuickFixAtCaret] Found diag, quickFix={quickFix is not null}");
                if (quickFix is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ApplyQuickFixAtCaret] Applying quickFix: {quickFix.Title}");
                    _vm.ApplyQuickFix(quickFix);
                }
                else
                {
                    foreach (var q in _vm.QuickFixActions)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ApplyQuickFixAtCaret] Available QF: DiagnosticId={q.DiagnosticId}, diag.Code={diag.Code}, match={string.Equals(q.DiagnosticId, diag.Code, StringComparison.OrdinalIgnoreCase)}");
                    }
                }
                break;
            }
        }
    }
}




