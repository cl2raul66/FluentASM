using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Projects;

/// <summary>
/// Lee archivos <c>.fproj</c> de Fase 1 en texto simple legible.
/// </summary>
/// <remarks>
/// <para>
/// El formato es una asignación <c>clave = valor</c> por línea. Se admiten comentarios
/// con <c>//</c>, líneas en blanco y valores entre comillas dobles cuando contienen espacios.
/// Las claves son sensibles a mayúsculas y minúsculas.
/// </para>
/// <para>
/// Bajo el Principio de Garantía de Completitud, los campos opcionales toman valores
/// predeterminados documentados: <c>architecture = x64</c>, <c>entryPoint = main</c> y
/// <c>output = &lt;name&gt;.x64</c>.
/// </para>
/// </remarks>
public static class FprojReader
{
    /// <summary>
    /// Lee y valida un <c>.fproj</c> desde disco, incluyendo la existencia de sus archivos fuente.
    /// </summary>
    /// <param name="projectFilePath">Ruta del archivo <c>.fproj</c>.</param>
    /// <param name="diagnostics">Bolsa compartida opcional; si se omite se crea una nueva.</param>
    public static FprojReadResult Read(string projectFilePath, DiagnosticBag? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        var bag = diagnostics ?? new DiagnosticBag();
        var fullPath = Path.GetFullPath(projectFilePath);

        if (!File.Exists(fullPath))
        {
            bag.Error(
                DiagnosticIds.ProjectFileNotFound,
                $"No se encontró el archivo de proyecto '{projectFilePath}'.");
            return FprojReadResult.Failed(bag);
        }

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (IOException exception)
        {
            bag.Error(
                DiagnosticIds.ProjectFileNotFound,
                $"No se pudo leer el archivo de proyecto '{projectFilePath}': {exception.Message}");
            return FprojReadResult.Failed(bag);
        }
        catch (UnauthorizedAccessException exception)
        {
            bag.Error(
                DiagnosticIds.ProjectFileNotFound,
                $"No se pudo leer el archivo de proyecto '{projectFilePath}': {exception.Message}");
            return FprojReadResult.Failed(bag);
        }

        var result = Parse(text, fullPath, bag);
        if (result.Project is not { } project || bag.HasErrors)
        {
            return FprojReadResult.Failed(bag);
        }

        foreach (var source in project.Sources)
        {
            if (!source.Exists)
            {
                bag.Error(
                    DiagnosticIds.ProjectSourceFileNotFound,
                    $"No se encontró el archivo fuente '{source.RelativePath}'.",
                    source.Location);
            }
        }

        return bag.HasErrors
            ? FprojReadResult.Failed(bag)
            : FprojReadResult.Succeeded(project, bag);
    }

    /// <summary>
    /// Interpreta el contenido de un <c>.fproj</c> sin tocar el disco.
    /// </summary>
    /// <param name="text">Contenido completo del archivo.</param>
    /// <param name="projectFilePath">Ruta del archivo, usada para resolver fuentes y validar el nombre.</param>
    /// <param name="diagnostics">Bolsa compartida opcional; si se omite se crea una nueva.</param>
    public static FprojReadResult Parse(
        string text,
        string projectFilePath,
        DiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        var bag = diagnostics ?? new DiagnosticBag();
        var fullPath = Path.GetFullPath(projectFilePath);
        var projectDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            projectDirectory = Directory.GetCurrentDirectory();
        }

        FieldValue? name = null;
        FieldValue? architectureName = null;
        FieldValue? entryPoint = null;
        FieldValue? output = null;
        var sources = new List<FieldValue>();

        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = StripComment(lines[index]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            var lineLocation = new SourceLocation(fullPath, lineNumber, 1);
            if (separator <= 0 || separator == line.Length - 1)
            {
                bag.Error(
                    DiagnosticIds.ProjectFileSyntaxError,
                    "Se esperaba una asignación con la forma 'clave = valor'.",
                    lineLocation);
                continue;
            }

            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            var valueLocation = new SourceLocation(fullPath, lineNumber, separator + 2);

            if (key.Length == 0)
            {
                bag.Error(
                    DiagnosticIds.ProjectFileSyntaxError,
                    "La clave de la asignación está vacía.",
                    valueLocation);
                continue;
            }

            if (value.Length == 0)
            {
                bag.Error(
                    DiagnosticIds.ProjectFileSyntaxError,
                    $"La clave '{key}' no tiene valor.",
                    valueLocation);
                continue;
            }

            switch (key)
            {
                case FprojSyntax.NameKey:
                    AssignSingle(bag, key, value, valueLocation, ref name);
                    break;

                case FprojSyntax.ArchitectureKey:
                    AssignSingle(bag, key, value, valueLocation, ref architectureName);
                    break;

                case FprojSyntax.EntryPointKey:
                    AssignSingle(bag, key, value, valueLocation, ref entryPoint);
                    break;

                case FprojSyntax.OutputKey:
                    AssignSingle(bag, key, value, valueLocation, ref output);
                    break;

                case FprojSyntax.SourceKey:
                    if (sources.Exists(source => string.Equals(source.Value, value, StringComparison.Ordinal)))
                    {
                        bag.Error(
                            DiagnosticIds.ProjectFieldDuplicated,
                            $"El archivo fuente '{value}' está declarado más de una vez.",
                            valueLocation);
                        break;
                    }

                    sources.Add(new FieldValue(value, valueLocation));
                    break;

                default:
                    bag.Error(
                        DiagnosticIds.ProjectFileSyntaxError,
                        $"Clave desconocida '{key}'. Claves válidas: {FprojSyntax.ValidKeys}.",
                        valueLocation);
                    break;
            }
        }

        ValidateProjectName(bag, fullPath, name);
        ValidateEntryPoint(bag, entryPoint);

        var architecture = Architecture.X64;
        if (architectureName is { } declaredArchitecture
            && !ArchitectureNames.TryParse(declaredArchitecture.Value, out architecture))
        {
            bag.Error(
                DiagnosticIds.ProjectArchitectureUnsupported,
                $"Arquitectura '{declaredArchitecture.Value}' no soportada en Fase 1. "
                + $"Solo se admite '{ArchitectureNames.SupportedList}'.",
                declaredArchitecture.Location);
        }

        // No se reporta la ausencia de 'source' si ya hay errores de sintaxis:
        // una línea mal formada suele ser el origen real del problema.
        if (sources.Count == 0 && !bag.HasErrors)
        {
            bag.Error(
                DiagnosticIds.ProjectFieldMissing,
                "Falta al menos un campo 'source' con un archivo fuente del proyecto.");
        }

        ValidateOutputName(bag, output);

        if (bag.HasErrors || name is not { } projectName)
        {
            return FprojReadResult.Failed(bag);
        }

        var outputFileName = output?.Value ?? projectName.Value + FprojSyntax.OutputExtension;
        var resolvedSources = new List<SourceFile>(sources.Count);
        foreach (var source in sources)
        {
            var sourceFullPath = Path.IsPathRooted(source.Value)
                ? Path.GetFullPath(source.Value)
                : Path.GetFullPath(Path.Combine(projectDirectory, source.Value));
            resolvedSources.Add(new SourceFile(source.Value, sourceFullPath, source.Location));
        }

        var project = new FluentProject
        {
            Name = projectName.Value,
            Architecture = architecture,
            EntryPoint = entryPoint?.Value ?? FprojSyntax.DefaultEntryPoint,
            OutputFileName = outputFileName,
            ProjectFilePath = fullPath,
            Sources = resolvedSources,
        };

        return FprojReadResult.Succeeded(project, bag);
    }

    private static void AssignSingle(
        DiagnosticBag diagnostics,
        string key,
        string value,
        SourceLocation location,
        ref FieldValue? target)
    {
        if (target is not null)
        {
            diagnostics.Error(
                DiagnosticIds.ProjectFieldDuplicated,
                $"La clave '{key}' está declarada más de una vez.",
                location);
            return;
        }

        target = new FieldValue(value, location);
    }

    private static void ValidateProjectName(DiagnosticBag diagnostics, string projectFilePath, FieldValue? name)
    {
        if (name is not { } declaredName)
        {
            diagnostics.Error(
                DiagnosticIds.ProjectFieldMissing,
                "Falta el campo obligatorio 'name'.");
            return;
        }

        if (!FprojSyntax.IsValidIdentifier(declaredName.Value))
        {
            diagnostics.Error(
                DiagnosticIds.ProjectIdentifierInvalid,
                $"El nombre de proyecto '{declaredName.Value}' no es válido: debe empezar con "
                + "minúscula y usar camelCase (letras, dígitos y guion bajo).",
                declaredName.Location);
            return;
        }

        var expectedFileName = declaredName.Value + ".fproj";
        var actualFileName = Path.GetFileName(projectFilePath);
        if (!string.Equals(actualFileName, expectedFileName, StringComparison.Ordinal))
        {
            diagnostics.Error(
                DiagnosticIds.ProjectNameMismatch,
                $"El archivo de proyecto debe llamarse '{expectedFileName}' para coincidir "
                + $"con el nombre '{declaredName.Value}' (nombre actual: '{actualFileName}').",
                declaredName.Location);
        }
    }

    private static void ValidateEntryPoint(DiagnosticBag diagnostics, FieldValue? entryPoint)
    {
        if (entryPoint is { } declaredEntryPoint && !FprojSyntax.IsValidIdentifier(declaredEntryPoint.Value))
        {
            diagnostics.Error(
                DiagnosticIds.ProjectIdentifierInvalid,
                $"El punto de entrada '{declaredEntryPoint.Value}' no es válido: debe empezar con "
                + "minúscula y usar camelCase (letras, dígitos y guion bajo).",
                declaredEntryPoint.Location);
        }
    }

    private static void ValidateOutputName(DiagnosticBag diagnostics, FieldValue? output)
    {
        if (output is not { } declaredOutput)
        {
            return;
        }

        var isPlainFileName =
            declaredOutput.Value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !declaredOutput.Value.Contains('/')
            && !declaredOutput.Value.Contains('\\');

        if (!isPlainFileName)
        {
            diagnostics.Error(
                DiagnosticIds.ProjectFileSyntaxError,
                $"El campo 'output' debe ser un nombre de archivo sin carpetas: '{declaredOutput.Value}'.",
                declaredOutput.Location);
        }
    }

    private static string StripComment(string line)
    {
        var commentIndex = line.IndexOf(FprojSyntax.CommentToken, StringComparison.Ordinal);
        return commentIndex < 0 ? line : line[..commentIndex];
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;

    private readonly record struct FieldValue(string Value, SourceLocation Location);
}


