using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace FluentASM_IDE.Models;

public partial class AsmLine : ObservableObject
{
    private static readonly SolidColorBrush ActiveGutterBrush = new(Color.FromArgb(255, 229, 226, 225));
    private static readonly SolidColorBrush InactiveGutterBrush = new(Color.FromArgb(120, 136, 146, 153));
    private static readonly SolidColorBrush ActiveRowBrush = new(Color.FromArgb(200, 42, 42, 42));
    private static readonly SolidColorBrush TransparentBrush = new(Color.FromArgb(0, 0, 0, 0));

    public int LineNumber { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GutterForeground))]
    [NotifyPropertyChangedFor(nameof(GutterFontWeight))]
    [NotifyPropertyChangedFor(nameof(ActiveLineVisibility))]
    [NotifyPropertyChangedFor(nameof(LineBackground))]
    public partial bool IsActive { get; set; }

    public ObservableCollection<AsmToken> Tokens { get; set; } = new();

    public string RawText { get; set; } = string.Empty;

    public SolidColorBrush GutterForeground => IsActive ? ActiveGutterBrush : InactiveGutterBrush;
    public FontWeight GutterFontWeight => IsActive ? FontWeights.SemiBold : FontWeights.Normal;
    public Visibility ActiveLineVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public SolidColorBrush LineBackground => IsActive ? ActiveRowBrush : TransparentBrush;
}
