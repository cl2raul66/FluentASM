using System;
using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace FluentASM_IDE.Models;

public class AsmToken
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public string Text { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#E5E2E1";
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public double LeftMargin { get; set; }

    public FontWeight TokenFontWeight => IsBold ? FontWeights.SemiBold : FontWeights.Normal;
    public FontStyle TokenFontStyle => IsItalic ? FontStyle.Italic : FontStyle.Normal;
    public Thickness TokenMargin => new Thickness(LeftMargin, 0, 0, 0);

    public SolidColorBrush ForegroundBrush
    {
        get
        {
            if (BrushCache.TryGetValue(ColorHex, out var cached))
                return cached;

            var hex = ColorHex.TrimStart('#');
            byte a = 255;
            byte r = 229, g = 226, b = 225;
            try
            {
                if (hex.Length == 6)
                {
                    r = Convert.ToByte(hex.Substring(0, 2), 16);
                    g = Convert.ToByte(hex.Substring(2, 2), 16);
                    b = Convert.ToByte(hex.Substring(4, 2), 16);
                }
                else if (hex.Length == 8)
                {
                    a = Convert.ToByte(hex.Substring(0, 2), 16);
                    r = Convert.ToByte(hex.Substring(2, 2), 16);
                    g = Convert.ToByte(hex.Substring(4, 2), 16);
                    b = Convert.ToByte(hex.Substring(6, 2), 16);
                }
            }
            catch { }

            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            BrushCache[ColorHex] = brush;
            return brush;
        }
    }

    public AsmToken() { }

    public AsmToken(string text, string colorHex, bool isBold = false, bool isItalic = false, double leftMargin = 0)
    {
        Text = text;
        ColorHex = colorHex;
        IsBold = isBold;
        IsItalic = isItalic;
        LeftMargin = leftMargin;
    }
}
