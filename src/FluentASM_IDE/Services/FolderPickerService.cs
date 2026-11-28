namespace FluentASM_IDE.Services;

/// <summary>
/// Implementación de <see cref="IFolderPickerService"/> que delega en el
/// selector nativo Win32 (<c>IFileOpenDialog</c> + <c>FOS_PICKFOLDERS</c>)
/// centralizado en <see cref="NativeFolderPicker"/>.
/// </summary>
public sealed class FolderPickerService : IFolderPickerService
{
    private readonly Func<nint> _ownerWindowHandleProvider;

    public FolderPickerService(Func<nint> ownerWindowHandleProvider)
    {
        ArgumentNullException.ThrowIfNull(ownerWindowHandleProvider);
        _ownerWindowHandleProvider = ownerWindowHandleProvider;
    }

    /// <inheritdoc />
    public string? PickFolder(string? initialDirectory = null, string? title = null) =>
        NativeFolderPicker.PickFolder(
            _ownerWindowHandleProvider(),
            initialDirectory,
            title);
}
