namespace FluentASM.Lsp.Protocol;

/// <summary>Edición de texto propuesta por una acción rápida.</summary>
/// <param name="Range">Rango que se debe reemplazar.</param>
/// <param name="NewText">Texto que sustituye al rango.</param>
public sealed record LspTextEdit(LspRange Range, string NewText);

/// <summary>
/// Acción rápida ofrecida por el servicio de lenguaje para el documento actual.
/// </summary>
/// <param name="Title">Texto breve que describe la acción.</param>
/// <param name="Edits">Ediciones que aplican la acción.</param>
/// <param name="DiagnosticCode">Diagnóstico relacionado con la acción, si existe.</param>
public sealed record LspCodeAction(
    string Title,
    IReadOnlyList<LspTextEdit> Edits,
    string? DiagnosticCode = null);
