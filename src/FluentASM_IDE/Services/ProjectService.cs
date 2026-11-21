using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentASM.Core.Projects;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Models;
using Microsoft.Extensions.Logging;

namespace FluentASM_IDE.Services;

/// <summary>
/// Gestiona la apertura y guardado de proyectos FluentASM (<c>.fproj</c> + <c>sourcesCode.fasm</c>).
/// El IDE no modifica el <c>.fproj</c>: solo lo lee en modo solo lectura para localizar
/// el archivo fuente principal y abrirlo en el editor.
/// </summary>
public sealed class ProjectService
{
    private readonly ILogger<ProjectService>? _logger;

    public ProjectService(ILogger<ProjectService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Ruta del archivo <c>.fproj</c> abierto, o <c>null</c> si no hay proyecto.</summary>
    public string? ProjectFilePath { get; private set; }

    /// <summary>Directorio del proyecto abierto.</summary>
    public string? ProjectDirectory { get; private set; }

    /// <summary>Ruta del archivo fuente <c>.fasm</c> abierto en el editor.</summary>
    public string? SourceFilePath { get; private set; }

    /// <summary>Nombre del proyecto (del campo <c>name</c> del <c>.fproj</c>).</summary>
    public string? ProjectName { get; private set; }

    /// <summary>Indica si hay un proyecto real abierto (no el ejemplo en memoria).</summary>
    public bool HasProject => ProjectFilePath is not null;

    /// <summary>
    /// Abre un proyecto desde una carpeta. Busca <c>&lt;nombreCarpeta&gt;.fproj</c>
    /// (según Decisión 0008) o el único <c>*.fproj</c> de la carpeta.
    /// </summary>
    public ProjectOpenResult OpenFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var fprojCandidate = Path.Combine(folderPath, Path.GetFileName(folderPath) + ".fproj");
        string? fprojPath = null;

        if (File.Exists(fprojCandidate))
        {
            fprojPath = fprojCandidate;
        }
        else
        {
            // Fallback: el único *.fproj de la carpeta.
            var fprojFiles = Directory.GetFiles(folderPath, "*.fproj");
            if (fprojFiles.Length == 1)
            {
                fprojPath = fprojFiles[0];
            }
            else if (fprojFiles.Length > 1)
            {
                return ProjectOpenResult.Failed(
                    $"La carpeta '{folderPath}' contiene {fprojFiles.Length} archivos .fproj. "
                    + "Especifique uno explícitamente.");
            }
            else
            {
                return ProjectOpenResult.Failed(
                    $"No se encontró ningún archivo .fproj en '{folderPath}'.");
            }
        }

        return OpenFproj(fprojPath);
    }

    /// <summary>
    /// Abre un proyecto desde un archivo <c>.fproj</c> explícito.
    /// </summary>
    public ProjectOpenResult OpenFproj(string fprojPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fprojPath);

        var fullPath = Path.GetFullPath(fprojPath);
        if (!File.Exists(fullPath))
        {
            return ProjectOpenResult.Failed($"No se encontró el archivo de proyecto '{fullPath}'.");
        }

        var result = FprojReader.Read(fullPath);
        if (!result.Success)
        {
            var messages = new List<string>();
            foreach (var d in result.Diagnostics)
            {
                messages.Add($"{(d.Severity == FluentASM.Core.Diagnostics.DiagnosticSeverity.Error ? "Error" : "Advertencia")} FASM{d.Id}: {d.Message}");
            }
            return ProjectOpenResult.Failed(string.Join("; ", messages));
        }

        var project = result.Project!;
        ProjectFilePath = fullPath;
        ProjectDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        ProjectName = project.Name;

        var source = project.Sources.Count > 0
            ? project.Sources[0]
            : throw new InvalidOperationException("El proyecto no declara archivos fuente.");

        SourceFilePath = source.FullPath;
        _logger?.LogInformation(
            "Proyecto abierto: {Name} ({Path}) -> fuente {Source}",
            project.Name, fullPath, source.FullPath);

        return ProjectOpenResult.Succeeded(project, source.FullPath);
    }

    /// <summary>
    /// Abre un archivo <c>.fasm</c> directamente. Si existe un <c>.fproj</c> en la misma
    /// carpeta con un nombre coincidente, se carga ese proyecto; de lo contrario se
    /// abre el archivo fuente sin proyecto asociado.
    /// </summary>
    public ProjectOpenResult OpenSourceFile(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            return ProjectOpenResult.Failed($"No se encontró el archivo fuente '{fullPath}'.");
        }

        var folder = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var fprojFiles = Directory.GetFiles(folder, "*.fproj");

        // Intentar asociar con un .fproj cuyo name coincida con el nombre del .fasm
        // o que sea el único de la carpeta.
        if (fprojFiles.Length == 1)
        {
            var fprojResult = OpenFproj(fprojFiles[0]);
            if (fprojResult.Success)
            {
                return fprojResult;
            }
        }

        // Sin proyecto asociado: abrir el archivo suelto.
        SourceFilePath = fullPath;
        ProjectDirectory = folder;
        ProjectName = Path.GetFileNameWithoutExtension(fullPath);
        ProjectFilePath = null;

        _logger?.LogInformation("Archivo fuente abierto sin proyecto: {Path}", fullPath);
        return ProjectOpenResult.Succeeded(null, fullPath);
    }

    /// <summary>
    /// Guarda el texto actual en el archivo fuente abierto (<c>.fasm</c>).
    /// Normaliza el formato del editor a UTF-8 sin BOM y saltos de línea LF.
    /// </summary>
    public bool SaveSource(string editorText)
    {
        if (SourceFilePath is null)
        {
            _logger?.LogWarning("No hay archivo fuente abierto para guardar.");
            return false;
        }

        try
        {
            // RichEditBox usa \r como separador de párrafo. Se canonicaliza a LF
            // y se conserva un salto final único, conforme al repositorio.
            string normalized = DocumentText.Normalize(editorText);
            string content = normalized.Length == 0 ? string.Empty : normalized + "\n";
            File.WriteAllText(
                SourceFilePath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _logger?.LogInformation("Archivo guardado: {Path}", SourceFilePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "No se pudo guardar '{Path}'.", SourceFilePath);
            return false;
        }
    }

    /// <summary>Limpia el estado del proyecto (vuelve al ejemplo en memoria).</summary>
    public void Clear()
    {
        ProjectFilePath = null;
        ProjectDirectory = null;
        SourceFilePath = null;
        ProjectName = null;
    }
}

/// <summary>Resultado de abrir un proyecto o archivo fuente.</summary>
public sealed class ProjectOpenResult
{
    private ProjectOpenResult(bool success, FluentProject? project, string? sourcePath, string? error)
    {
        Success = success;
        Project = project;
        SourcePath = sourcePath;
        Error = error;
    }

    public bool Success { get; }
    public FluentProject? Project { get; }
    public string? SourcePath { get; }
    public string? Error { get; }

    public static ProjectOpenResult Succeeded(FluentProject? project, string? sourcePath) =>
        new(true, project, sourcePath, null);

    public static ProjectOpenResult Failed(string error) =>
        new(false, null, null, error);
}
