namespace FluentASM.Lsp.Protocol;

/// <summary>
/// Categoría visual de un token de FluentASM para el resaltado sintáctico.
/// </summary>
public enum HighlightTokenKind
{
    /// <summary>Palabra contextual o constructor del flujo type-state.</summary>
    Keyword,

    /// <summary>Identificador declarado por el usuario.</summary>
    Identifier,

    /// <summary>Registro físico o vista de registro (<c>Gpr0</c>, <c>Frf</c>, <c>Gpr1.low32</c>).</summary>
    Register,

    /// <summary>Bandera física o alias de condición (<c>Frf.carry</c>).</summary>
    Flag,

    /// <summary>Verbo canónico (<c>copy</c>, <c>flowJump</c>).</summary>
    Verb,

    /// <summary>Directiva dentro de <c>.data</c> o <c>.execute</c> (<c>id</c>, <c>define</c>).</summary>
    Directive,

    /// <summary>Magnitud física (<c>Byte</c>, <c>KiloByte</c>).</summary>
    Magnitude,

    /// <summary>Literal entero.</summary>
    Number,

    /// <summary>Literal de cadena.</summary>
    String,

    /// <summary>Etiqueta local <c>@nombre</c>.</summary>
    Label,

    /// <summary>Operador (<c>-&gt;</c>, <c>+</c>, <c>-</c>, <c>*</c>, <c>_</c>).</summary>
    Operator,

    /// <summary>Signo de puntuación estructural.</summary>
    Punctuation,
}

/// <summary>
/// Token clasificado para que el editor aplique colores sin lógica propia.
/// </summary>
/// <param name="Range">Rango exacto que ocupa el token en el documento.</param>
/// <param name="Kind">Categoría visual resuelta contra el catálogo del lenguaje.</param>
/// <param name="Text">Texto original del token.</param>
public sealed record HighlightToken(
    LspRange Range,
    HighlightTokenKind Kind,
    string Text);
