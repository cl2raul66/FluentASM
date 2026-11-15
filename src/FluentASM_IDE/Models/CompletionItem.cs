using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Models;

public partial class CompletionItem : ObservableObject
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SolidColorBrush SelectedBrush = new(Color.FromArgb(50, 0, 157, 216));
    private static readonly SolidColorBrush TransparentBrush = new(Color.FromArgb(0, 0, 0, 0));

    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = "method";
    public string Detail { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string Inputs { get; set; } = string.Empty;
    public string Modifies { get; set; } = string.Empty;
    public string Returns { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE756";
    public string IconColorHex { get; set; } = "#60CDFF";
    public string Category { get; set; } = "Methods";
    public bool HasDocumentation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemBackground))]
    [NotifyPropertyChangedFor(nameof(DocCardVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedBorderThickness))]
    public partial bool IsSelected { get; set; }

    public Thickness SelectedBorderThickness => IsSelected ? new Thickness(3, 0, 0, 0) : new Thickness(0);

    public SolidColorBrush ItemBackground => IsSelected ? SelectedBrush : TransparentBrush;
    public Visibility DocCardVisibility => (IsSelected && HasDocumentation) ? Visibility.Visible : Visibility.Collapsed;

    public SolidColorBrush IconBrush
    {
        get
        {
            if (BrushCache.TryGetValue(IconColorHex, out var cached))
                return cached;

            var hex = IconColorHex.TrimStart('#');
            byte a = 255;
            byte r = 96, g = 205, b = 255;
            try
            {
                if (hex.Length == 6)
                {
                    r = Convert.ToByte(hex.Substring(0, 2), 16);
                    g = Convert.ToByte(hex.Substring(2, 2), 16);
                    b = Convert.ToByte(hex.Substring(4, 2), 16);
                }
            }
            catch { }

            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            BrushCache[IconColorHex] = brush;
            return brush;
        }
    }
}
