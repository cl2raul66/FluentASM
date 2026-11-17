namespace FluentASM.Core.Diagnostics;

/// <summary>
/// Mensaje emitido por una etapa del compilador, con su código estable <c>FASMxxxx</c>.
/// </summary>
/// <param name="Id">Código estable del diagnóstico (por ejemplo <c>FASM2003</c>).</param>
/// <param name="Severity">Severidad del mensaje.</param>
/// <param name="Message">Descripción legible para el programador.</param>
/// <param name="Location">Ubicación física, si aplica.</param>
public sealed record Diagnostic(
    string Id,
    DiagnosticSeverity Severity,
    string Message,
    SourceLocation? Location = null)
{
    /// <summary>Representa el diagnóstico en formato <c>ruta(linea,columna): error FASM2003: mensaje</c>.</summary>
    public override string ToString()
    {
        var severity = Severity == DiagnosticSeverity.Error ? "error" : "warning";
        return Location is null
            ? $"{severity} {Id}: {Message}"
            : $"{Location}: {severity} {Id}: {Message}";
    }
}
