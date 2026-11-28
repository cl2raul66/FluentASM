namespace FluentASM_IDE.Services;

/// <summary>
/// Abstrae la selección de carpetas para que los ViewModels no dependan del
/// selector nativo ni del <em>code-behind</em> de la ventana.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// Muestra el selector de carpetas y devuelve la ruta elegida, o
    /// <c>null</c> si el usuario cancela.
    /// </summary>
    /// <param name="initialDirectory">Carpeta inicial; debe existir.</param>
    /// <param name="title">Título opcional del diálogo.</param>
    string? PickFolder(string? initialDirectory = null, string? title = null);
}
