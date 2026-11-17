using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Projects;

/// <summary>
/// Archivo fuente declarado en un <c>.fproj</c>.
/// </summary>
/// <param name="RelativePath">Ruta tal como se escribió en el <c>.fproj</c>.</param>
/// <param name="FullPath">Ruta absoluta resuelta contra el directorio del proyecto.</param>
/// <param name="Location">Ubicación de la declaración dentro del <c>.fproj</c>.</param>
public sealed record SourceFile(string RelativePath, string FullPath, SourceLocation? Location = null)
{
    /// <summary>Indica si el archivo fuente existe en disco.</summary>
    public bool Exists => File.Exists(FullPath);
}
