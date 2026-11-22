using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Models;

/// <summary>
/// Categoría dinámica del overlay. Sus categorías proceden de los metadatos
/// del LSP; la IDE no mantiene catálogos del lenguaje.
/// </summary>
public partial class CompletionCategory : ObservableObject
{
    private static readonly SolidColorBrush ActiveBackgroundDark = new(Color.FromArgb(80, 96, 205, 255));
    private static readonly SolidColorBrush ActiveBackgroundLight = new(Color.FromArgb(80, 0, 103, 192));
    private static readonly SolidColorBrush InactiveBackground = new(Color.FromArgb(0, 0, 0, 0));

    private bool _isActive;

    public CompletionCategory(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }

    public string Key { get; }
    public string DisplayName { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(BorderThickness));
        }
    }

    public SolidColorBrush Background => IsActive
        ? (CompletionItem.IsDarkTheme ? ActiveBackgroundDark : ActiveBackgroundLight)
        : InactiveBackground;

    public Thickness BorderThickness => IsActive ? new Thickness(1) : new Thickness(0);

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(Background));
    }
}
