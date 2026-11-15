using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Models;

public partial class CommandPaletteItem : ObservableObject
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SolidColorBrush ActiveItemBrush = new(Color.FromArgb(180, 53, 53, 53));
    private static readonly SolidColorBrush TransparentBrush = new(Color.FromArgb(0, 0, 0, 0));

    public string Command { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Shortcut { get; set; } = string.Empty;
    public string Category { get; set; } = "Comandos (/)";
    public string IconGlyph { get; set; } = "\uE756";
    public string IconColorHex { get; set; } = "#60CDFF";
    public bool IsCommand { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemBackground))]
    [NotifyPropertyChangedFor(nameof(ActiveIndicatorVisibility))]
    public partial bool IsActive { get; set; }

    public SolidColorBrush ItemBackground => IsActive ? ActiveItemBrush : TransparentBrush;
    public Visibility ActiveIndicatorVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

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
