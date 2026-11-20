namespace FluentASM.Lsp.Protocol;

/// <summary>
/// Categoría visual de un token de FluentASM para el resaltado sintáctico.
/// </summary>
public enum HighlightTokenKind
{
    /// <summary>Palabra contextual (<c>defaultValue</c>, <c>if</c>, <c>not</c>, ...).</summary>
    Keyword,

    /// <summary>Identificador declarado por el usuario sin símbolo semántico resuelto.</summary>
    Identifier,

    /// <summary>Registro físico o vista de registro (<c>Gpr0</c>, <c>Frf</c>, <c>Gpr1.low32</c>).</summary>
    Register,

    /// <summary>Bandera física o alias de condición (<c>Frf.carry</c>).</summary>
    Flag,

    /// <summary>Verbo canónico (<c>copy</c>, <c>flowJump</c>).</summary>
    Verb,

    /// <summary>Constructor del flujo type-state (<c>.data</c>, <c>.execute</c>, <c>.initialize</c>).</summary>
    FlowConstructor,

    /// <summary>Directiva de configuración (<c>define</c>, <c>id</c>, <c>routines</c>, ...).</summary>
    Directive,

    /// <summary>Magnitud física (<c>Bit</c>, <c>Byte</c>, <c>KiloByte</c>).</summary>
    Magnitude,

    /// <summary>Literal entero decimal, hexadecimal o binario.</summary>
    Number,

    /// <summary>Literal de cadena.</summary>
    String,

    /// <summary>Etiqueta local <c>@nombre</c>.</summary>
    Label,

    /// <summary>Símbolo declarado en <c>.data(...)</c>.</summary>
    Data,

    /// <summary>Rutina declarada en <c>.execute(...)</c>.</summary>
    Routine,

    /// <summary>Comentario de línea o de bloque.</summary>
    Comment,

    /// <summary>Operador (<c>-&gt;</c>, <c>+</c>, <c>-</c>, <c>*</c>, <c>_</c>).</summary>
    Operator,

    /// <summary>Signo de puntuación estructural.</summary>
    Punctuation,
}

/// <summary>
/// Token clasificado para que el editor aplique colores sin lógica propia.
/// Los offsets son absolutos, base 0, sobre el texto normalizado del documento.
/// </summary>
/// <param name="Start">Offset absoluto del primer carácter del token.</param>
/// <param name="Length">Número de caracteres que ocupa el token.</param>
/// <param name="Kind">Categoría visual resuelta contra el catálogo del lenguaje.</param>
/// <param name="Text">Texto original del token.</param>
/// <param name="Range">Rango LSP equivalente, listo para consumidores estándar.</param>
public sealed record HighlightToken(
    int Start,
    int Length,
    HighlightTokenKind Kind,
    string Text,
    LspRange Range);
