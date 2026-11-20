namespace FluentASM.Core.Diagnostics;

/// <summary>
/// Severidad de un diagnóstico emitido por el compilador.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Advertencia: no impide generar la imagen, pero debe revisarse.</summary>
    Warning,

    /// <summary>Error: la compilación no puede continuar.</summary>
    Error,
}


