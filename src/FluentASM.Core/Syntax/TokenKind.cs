namespace FluentASM.Core.Syntax;

/// <summary>
/// Categoría léxica de un token de FluentASM.
/// </summary>
/// <remarks>
/// El lenguaje es sensible a mayúsculas y minúsculas. Las palabras contextuales
/// (<c>defaultValue</c>, <c>if</c>, <c>not</c>, <c>and</c>, <c>or</c>, <c>equal</c>,
/// <c>unequal</c>, <c>Port</c>) y las magnitudes (<c>Bit</c>, <c>Byte</c>, <c>Kilo*</c>…)
/// tienen su propia categoría para que el parser las reconozca por contexto.
/// </remarks>
public enum TokenKind
{
    /// <summary>Identificador de usuario, registro o unidad (<c>Gpr0</c>, <c>sumar</c>).</summary>
    Identifier,

    /// <summary>Etiqueta local con prefijo <c>@</c> (<c>@bucle</c>).</summary>
    Label,

    /// <summary>Literal entero decimal, hexadecimal o binario; puede ser negativo.</summary>
    Integer,

    /// <summary>Literal de cadena entre comillas dobles (solo válido en <c>.data</c>).</summary>
    String,

    /// <summary>Unidad de medida física (<c>Bit</c>, <c>Byte</c>, <c>KiloByte</c>…).</summary>
    Magnitude,

    /// <summary>Valor de estado base limpio: <c>defaultValue</c>.</summary>
    DefaultValue,

    /// <summary>Palabra contextual <c>if</c>.</summary>
    If,

    /// <summary>Palabra contextual <c>not</c> (condiciones y verbo de bits).</summary>
    Not,

    /// <summary>Palabra contextual <c>and</c> (condiciones y verbo lógico).</summary>
    And,

    /// <summary>Palabra contextual <c>or</c> (condiciones y verbo lógico).</summary>
    Or,

    /// <summary>Palabra contextual <c>equal</c>.</summary>
    Equal,

    /// <summary>Palabra contextual <c>unequal</c>.</summary>
    Unequal,

    /// <summary>Unidad de puerto hardware <c>Port</c>.</summary>
    Port,

    /// <summary>Punto de navegación <c>.</c>.</summary>
    Dot,

    /// <summary>Calificador de magnitud <c>:</c>.</summary>
    Colon,

    /// <summary>Separador de listas homogéneas <c>,</c>.</summary>
    Comma,

    /// <summary>Paréntesis de apertura <c>(</c>.</summary>
    OpenParen,

    /// <summary>Paréntesis de cierre <c>)</c>.</summary>
    CloseParen,

    /// <summary>Corchete de apertura <c>[</c>.</summary>
    OpenBracket,

    /// <summary>Corchete de cierre <c>]</c>.</summary>
    CloseBracket,

    /// <summary>Llave de apertura <c>{</c>.</summary>
    OpenBrace,

    /// <summary>Llave de cierre <c>}</c>.</summary>
    CloseBrace,

    /// <summary>Ángulo de apertura <c>&lt;</c>.</summary>
    OpenAngle,

    /// <summary>Ángulo de cierre <c>&gt;</c>.</summary>
    CloseAngle,

    /// <summary>Flecha de constructor fluido <c>-&gt;</c>.</summary>
    Arrow,

    /// <summary>Marcador implícito <c>_</c>.</summary>
    Underscore,

    /// <summary>Suma en expresiones de dirección <c>+</c>.</summary>
    Plus,

    /// <summary>Resta en expresiones de dirección <c>-</c>.</summary>
    Minus,

    /// <summary>Producto o escala en expresiones de dirección <c>*</c>.</summary>
    Star,

    /// <summary>Fin del archivo fuente.</summary>
    EndOfFile,
}


