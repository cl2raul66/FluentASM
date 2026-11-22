using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Models;

/// <summary>
/// Interfaz pública para que el compilador XAML pueda resolver el tipo en DataTemplates.
/// La implementación concreta <see cref="CommandPaletteItem"/> es partial (source generator MVVM).
/// </summary>
public interface ICommandPaletteItem
{
    string Command { get; set; }
    string Description { get; set; }
    string Shortcut { get; set; }
    string Category { get; set; }
    string IconGlyph { get; set; }
    string IconColorHex { get; set; }
    bool IsCommand { get; set; }
    bool IsActive { get; set; }

    SolidColorBrush ItemBackground { get; }
    Visibility ActiveIndicatorVisibility { get; }
    SolidColorBrush IconBrush { get; }

    void RefreshTheme();
}









