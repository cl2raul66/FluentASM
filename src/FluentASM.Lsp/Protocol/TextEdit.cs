namespace FluentASM.Lsp.Protocol;

/// <summary>
/// Edición mínima de texto devuelta por el motor de formateo. Reutiliza
/// <see cref="LspRange"/> y <see cref="Position"/> para que el IDE pueda
/// aplicarla sin conversiones adicionales.
/// </summary>
/// <param name="Range">Rango que se reemplaza.</param>
/// <param name="NewText">Texto que sustituye al rango; puede ser vacío.</param>
public readonly record struct TextEdit(LspRange Range, string NewText);
