using FluentASM_IDE.Models;
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
    }
}




