namespace FluentASM.Lsp.Protocol;

/// <summary>
/// Severidad de un diagnóstico según LSP.
/// </summary>
public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

/// <summary>
/// Representa un diagnóstico (error o advertencia) emitido por el Language Server.
/// </summary>
public sealed record LspDiagnostic(
    LspRange Range,
    DiagnosticSeverity Severity,
    string Message,
    string? Code = null,
    string? Source = "FluentASM");
