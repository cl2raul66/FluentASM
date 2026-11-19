using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Projects;

/// <summary>
/// Resultado de leer o interpretar un archivo <c>.fproj</c>.
/// </summary>
public sealed class FprojReadResult
{
    private FprojReadResult(FluentProject? project, DiagnosticBag diagnostics)
    {
        Project = project;
        Diagnostics = diagnostics;
    }

    /// <summary>Proyecto resuelto, o <see langword="null"/> si la lectura falló.</summary>
    public FluentProject? Project { get; }

    /// <summary>Diagnósticos emitidos durante la lectura.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>Indica si el proyecto se resolvió sin errores.</summary>
    public bool Success => Project is not null && !Diagnostics.HasErrors;

    /// <summary>Crea un resultado correcto.</summary>
    public static FprojReadResult Succeeded(FluentProject project, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new FprojReadResult(project, diagnostics);
    }

    /// <summary>Crea un resultado fallido.</summary>
    public static FprojReadResult Failed(DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new FprojReadResult(null, diagnostics);
    }
}


