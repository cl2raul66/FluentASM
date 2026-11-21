using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentASM.Lsp.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Models;

public partial class CompletionItem : ObservableObject, ICompletionItem
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SolidColorBrush SelectedBrushDark = new(Color.FromArgb(50, 0, 157, 216));
    private static readonly SolidColorBrush SelectedBrushLight = new(Color.FromArgb(50, 0, 103, 192));
    private static readonly SolidColorBrush TransparentBrush = new(Color.FromArgb(0, 0, 0, 0));

    public static bool IsDarkTheme { get; set; } = true;

    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = "method";
    public string Detail { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string Inputs { get; set; } = string.Empty;
    public string Modifies { get; set; } = string.Empty;
    public string Returns { get; set; } = string.Empty;
    public string FlagsAffected { get; set; } = "Ninguna";
    public string IconGlyph { get; set; } = "\uE756";
    public string IconColorHex { get; set; } = "#60CDFF";
    public string Category { get; set; } = "Methods";
    public bool HasDocumentation { get; set; }
    public string Documentation { get; set; } = string.Empty;
    public bool HasExplicitInsertText { get; set; }
    public string InsertText { get; set; } = string.Empty;
    public string FilterText { get; set; } = string.Empty;
    public string SortText { get; set; } = string.Empty;
    public LspRange? ReplaceRange { get; set; }
    public int? CaretOffset { get; set; }
    public InsertTextFormat InsertTextFormat { get; set; } = InsertTextFormat.PlainText;

    public static CompletionItem FromLsp(
        FluentASM.Lsp.Protocol.LspCompletionItem lsp,
        LspRange? replaceRange = null)
    {
        string glyph = lsp.Kind switch
        {
            FluentASM.Lsp.Protocol.CompletionItemKind.Method => "\uE756",
            FluentASM.Lsp.Protocol.CompletionItemKind.Variable => "\uE713",
            FluentASM.Lsp.Protocol.CompletionItemKind.Field => "\uE805",
            FluentASM.Lsp.Protocol.CompletionItemKind.Function => "\uE8EE",
            FluentASM.Lsp.Protocol.CompletionItemKind.Keyword => "\uE8D4",
            FluentASM.Lsp.Protocol.CompletionItemKind.Reference => "\uE8EC",
            _ => "\uE756"
        };

        string colorHex = lsp.Kind switch
        {
            FluentASM.Lsp.Protocol.CompletionItemKind.Method => "#60CDFF",
            FluentASM.Lsp.Protocol.CompletionItemKind.Variable => "#81CFFF",
            FluentASM.Lsp.Protocol.CompletionItemKind.Field => "#A4C9DE",
            FluentASM.Lsp.Protocol.CompletionItemKind.Function => "#FFD4BE",
            FluentASM.Lsp.Protocol.CompletionItemKind.Keyword => "#BDC8CF",
            FluentASM.Lsp.Protocol.CompletionItemKind.Reference => "#B4E4FF",
            _ => "#60CDFF"
        };

        string category = lsp.Category ?? lsp.Metadata?.Category ?? lsp.Kind switch
        {
            FluentASM.Lsp.Protocol.CompletionItemKind.Method => "Verbos",
            FluentASM.Lsp.Protocol.CompletionItemKind.Variable or FluentASM.Lsp.Protocol.CompletionItemKind.Field => "Registros",
            FluentASM.Lsp.Protocol.CompletionItemKind.Function => "Rutinas",
            FluentASM.Lsp.Protocol.CompletionItemKind.Reference => "Etiquetas",
            FluentASM.Lsp.Protocol.CompletionItemKind.Keyword => "Directivas",
            _ => "Verbos"
        };

        return new CompletionItem
        {
            Label = lsp.Label,
            Kind = lsp.Kind.ToString().ToLowerInvariant(),
            Detail = lsp.Detail ?? lsp.Metadata?.Description ?? string.Empty,
            InsertText = lsp.TextEdit?.NewText ?? lsp.InsertText ?? lsp.Label,
            HasExplicitInsertText = lsp.TextEdit is not null || lsp.InsertText is not null,
            FilterText = lsp.FilterText ?? lsp.Label,
            SortText = lsp.SortText ?? string.Empty,
            ReplaceRange = lsp.TextEdit?.Range ?? replaceRange,
            CaretOffset = lsp.CaretOffset,
            InsertTextFormat = lsp.InsertTextFormat,
            Signature = lsp.Metadata?.Signature ?? lsp.Label,
            Inputs = lsp.Metadata?.Inputs ?? "Ninguno",
            Modifies = lsp.Metadata?.Modifies ?? "Ninguno",
            Returns = lsp.Metadata?.Returns ?? "void",
            FlagsAffected = lsp.Metadata?.FlagsAffected ?? "Ninguna",
            IconGlyph = glyph,
            IconColorHex = colorHex,
            Category = category,
            Documentation = lsp.Documentation?.Value ?? string.Empty,
            HasDocumentation = lsp.Metadata != null || lsp.Documentation != null
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemBackground))]
    [NotifyPropertyChangedFor(nameof(DocCardVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedBorderThickness))]
    public partial bool IsSelected { get; set; }

    public Thickness SelectedBorderThickness => IsSelected ? new Thickness(3, 0, 0, 0) : new Thickness(0);

    public SolidColorBrush ItemBackground => IsSelected ? (IsDarkTheme ? SelectedBrushDark : SelectedBrushLight) : TransparentBrush;
    public Visibility DocCardVisibility => (IsSelected && HasDocumentation) ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(ItemBackground));
    }

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














