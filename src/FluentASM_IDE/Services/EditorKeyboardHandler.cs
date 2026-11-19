using System;
using FluentASM_IDE.Controls;
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
    private readonly IntelliSenseOverlay _intelliSense;

    public EditorKeyboardHandler(RichEditBox editor, IMainPageViewModel vm, IntelliSenseOverlay intelliSense)
    {
        _editor = editor;
        _vm = vm;
        _intelliSense = intelliSense;
    }

    public void OnKeyDown(KeyRoutedEventArgs e)
    {
        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
                       CoreVirtualKeyStates.Down) != 0;

        // IntelliSense navigation takes priority when open
        if (_vm.IsIntelliSenseOpen)
        {
            if (HandleIntelliSenseKeys(e)) return;
        }

        // Global editor shortcuts
        if (e.Key == VirtualKey.Tab)
        {
            e.Handled = true;
            _editor.Document.Selection.TypeText("    ");
        }
        else if (isCtrl && e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            RequestCompletions();
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
    }

    private bool HandleIntelliSenseKeys(KeyRoutedEventArgs e)
    {
        return e.Key switch
        {
            VirtualKey.Down => Handle(() => _intelliSense.SelectNext()),
            VirtualKey.Up => Handle(() => _intelliSense.SelectPrevious()),
            VirtualKey.Enter => Handle(() => _intelliSense.CommitSelected()),
            VirtualKey.Escape => Handle(() => _vm.CloseIntelliSense()),
            _ => false
        };

        bool Handle(Action action)
        {
            action();
            e.Handled = true;
            return true;
        }
    }

    private void RequestCompletions()
    {
        _editor.Document.GetText(TextGetOptions.None, out string text);
        _vm.RequestLspCompletions(text ?? string.Empty, openPopup: true);
        _intelliSense.RefreshFilter();
    }
}




