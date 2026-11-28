using FluentASM_IDE.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentASM_IDE.Controls;

/// <summary>
/// Dock de estado inferior. Recibe su estado de <see cref="StatusDockViewModel"/>
/// y delega en el host la acción de formatear, que necesita acceso al editor.
/// </summary>
public sealed partial class StatusDock : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(StatusDockViewModel), typeof(StatusDock),
            new PropertyMetadata(null));

    /// <summary>ViewModel que alimenta la telemetría del dock.</summary>
    public StatusDockViewModel? ViewModel
    {
        get => (StatusDockViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Se produce al pulsar «Formatear».</summary>
    public event EventHandler? FormatRequested;

    public StatusDock()
    {
        InitializeComponent();
    }

    private void OnFormatButtonClick(object sender, RoutedEventArgs e)
    {
        FormatRequested?.Invoke(this, EventArgs.Empty);
    }
}
