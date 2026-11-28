using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentASM_IDE.Services;

/// <summary>
/// Implementación de <see cref="IDialogService"/> basada en
/// <see cref="ContentDialog"/> sobre la ventana principal del IDE.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Func<Window?> _windowProvider;

    public DialogService(Func<Window?> windowProvider)
    {
        ArgumentNullException.ThrowIfNull(windowProvider);
        _windowProvider = windowProvider;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmButtonText,
        string cancelButtonText)
    {
        Window? window = _windowProvider();
        if (window?.Content is not FrameworkElement root || root.XamlRoot is not { } xamlRoot)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = confirmButtonText,
            CloseButtonText = cancelButtonText,
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result is ContentDialogResult.Primary;
    }
}
