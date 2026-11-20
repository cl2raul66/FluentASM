namespace FluentASM.Core.Projects;

/// <summary>
/// Configuración resuelta de un proyecto FluentASM leída desde su archivo <c>.fproj</c>.
/// </summary>
public sealed record FluentProject
{
    /// <summary>Nombre del proyecto; coincide con el identificador raíz de <c>sourcesCode.fasm</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Arquitectura destino.</summary>
    public required Architecture Architecture { get; init; }

    /// <summary>Rutina de entrada del programa.</summary>
    public required string EntryPoint { get; init; }

    /// <summary>Nombre del archivo de imagen generado (sin carpetas).</summary>
    public required string OutputFileName { get; init; }

    /// <summary>Ruta absoluta del archivo <c>.fproj</c>.</summary>
    public required string ProjectFilePath { get; init; }

    /// <summary>Archivos fuente declarados, en el orden de aparición.</summary>
    public required IReadOnlyList<SourceFile> Sources { get; init; }

    /// <summary>Directorio que contiene el <c>.fproj</c>.</summary>
    public string ProjectDirectory =>
        Path.GetDirectoryName(ProjectFilePath) ?? Directory.GetCurrentDirectory();

    /// <summary>Ruta absoluta de la imagen que generará el compilador.</summary>
    public string OutputPath => Path.Combine(ProjectDirectory, OutputFileName);
}


