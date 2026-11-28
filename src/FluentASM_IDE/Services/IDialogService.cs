namespace FluentASM_IDE.Services;

/// <summary>
/// Abstrae los diálogos modales del IDE para que los comandos de los
/// ViewModels no dependan de <c>ContentDialog</c> ni del <em>code-behind</em>.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Muestra una confirmación modal y devuelve <c>true</c> solo si el
    /// usuario elige el botón principal.
    /// </summary>
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmButtonText,
        string cancelButtonText);
}
