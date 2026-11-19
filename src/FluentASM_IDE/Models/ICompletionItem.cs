using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Models;

/// <summary>
/// Interfaz pública para que el compilador XAML pueda resolver el tipo en DataTemplates.
/// La implementación concreta <see cref="CompletionItem"/> es partial (source generator MVVM).
/// </summary>
public interface ICompletionItem
{
    string Label { get; set; }
    string Kind { get; set; }
    string Detail { get; set; }
    string Signature { get; set; }
    string Inputs { get; set; }
    string Modifies { get; set; }
    string Returns { get; set; }
    string IconGlyph { get; set; }
    string IconColorHex { get; set; }
    string Category { get; set; }
    bool HasDocumentation { get; set; }
    string InsertText { get; set; }

    bool IsSelected { get; set; }
    Thickness SelectedBorderThickness { get; }
    SolidColorBrush ItemBackground { get; }
    Visibility DocCardVisibility { get; }
    SolidColorBrush IconBrush { get; }

    void RefreshTheme();
}




