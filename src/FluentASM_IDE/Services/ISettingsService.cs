namespace FluentASM_IDE.Services;

/// <summary>
/// Persiste y recupera las preferencias de aplicación del IDE.
/// </summary>
/// <remarks>
/// El alcance es deliberadamente mínimo: solo se guarda la última carpeta
/// abierta para inicializar el selector de carpetas. No se reabren proyectos
/// automáticamente al iniciar.
/// </remarks>
public interface ISettingsService
{
    /// <summary>
    /// Última carpeta abierta por el usuario, o <c>null</c> si no hay ninguna
    /// guardada o no se pudo leer.
    /// </summary>
    string? LastOpenedFolder { get; }

    /// <summary>
    /// Devuelve la carpeta inicial del selector: la última abierta si todavía
    /// existe en disco; en su defecto, la carpeta Documentos del usuario.
    /// </summary>
    string GetInitialFolder();

    /// <summary>
    /// Guarda la carpeta del proyecto abierto más recientemente.
    /// </summary>
    /// <param name="folderPath">Ruta absoluta de la carpeta abierta.</param>
    void SaveLastOpenedFolder(string folderPath);
}
