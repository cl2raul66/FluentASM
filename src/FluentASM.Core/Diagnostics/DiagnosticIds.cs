namespace FluentASM.Core.Diagnostics;

/// <summary>
/// Identificadores de diagnóstico definidos por la especificación oficial
/// (véase la sección «Pipeline del compilador y Diagnósticos» de <c>CONTRIBUTING.md</c>).
/// </summary>
public static class DiagnosticIds
{
    // FASM1xxx — Léxico y sintáctico.

    /// <summary>Carácter que no pertenece al lenguaje.</summary>
    public const string LexUnexpectedCharacter = "FASM1001";

    /// <summary>Comentario de bloque <c>/* ... */</c> sin cerrar.</summary>
    public const string LexUnterminatedBlockComment = "FASM1002";

    /// <summary>Cadena de texto sin comilla de cierre.</summary>
    public const string LexUnterminatedString = "FASM1003";

    /// <summary>Secuencia de escape desconocida o incompleta dentro de una cadena.</summary>
    public const string LexInvalidEscape = "FASM1004";

    /// <summary>Literal numérico mal formado o fuera de rango.</summary>
    public const string LexInvalidNumber = "FASM1005";

    /// <summary>Etiqueta local <c>@nombre</c> sin un nombre válido.</summary>
    public const string LexInvalidLabel = "FASM1006";

    // FASM11xx — Sintáctico (parser).

    /// <summary>Token inesperado o ausente en una construcción fluida.</summary>
    public const string SyntaxUnexpectedToken = "FASM1101";

    /// <summary>Flujo <em>type-state</em> incompleto o fuera de orden.</summary>
    public const string SyntaxIncompleteTypeState = "FASM1102";

    /// <summary>Definición de datos inválida dentro de <c>.data(...)</c>.</summary>
    public const string SyntaxInvalidDataDefinition = "FASM1103";

    /// <summary>Definición de rutina inválida dentro de <c>.execute(...)</c>.</summary>
    public const string SyntaxInvalidRoutine = "FASM1104";

    /// <summary>Sentencia o expresión fluida no reconocida.</summary>
    public const string SyntaxInvalidStatement = "FASM1105";

    /// <summary>Firma de <c>d.run(...)</c> o lista de parámetros inválida.</summary>
    public const string SyntaxInvalidSignature = "FASM1106";

    /// <summary>Operando de memoria o expresión de dirección inválida.</summary>
    public const string SyntaxInvalidAddress = "FASM1107";

    /// <summary>Condición de <c>if</c> inválida.</summary>
    public const string SyntaxInvalidCondition = "FASM1108";

    // FASM2xxx — Programa, símbolos, secciones y datos: archivo .fproj.

    /// <summary>El archivo <c>.fproj</c> indicado no existe en disco.</summary>
    public const string ProjectFileNotFound = "FASM2001";

    /// <summary>Línea de <c>.fproj</c> con sintaxis inválida, valor vacío o clave desconocida.</summary>
    public const string ProjectFileSyntaxError = "FASM2002";

    /// <summary>Falta un campo obligatorio en el <c>.fproj</c>.</summary>
    public const string ProjectFieldMissing = "FASM2003";

    /// <summary>Campo de valor único repetido en el <c>.fproj</c> (o archivo fuente duplicado).</summary>
    public const string ProjectFieldDuplicated = "FASM2004";

    /// <summary>Arquitectura destino no soportada en Fase 1 (solo <c>x64</c>).</summary>
    public const string ProjectArchitectureUnsupported = "FASM2005";

    /// <summary>Identificador de proyecto o punto de entrada inválido.</summary>
    public const string ProjectIdentifierInvalid = "FASM2006";

    /// <summary>Un archivo fuente declarado en el <c>.fproj</c> no existe en disco.</summary>
    public const string ProjectSourceFileNotFound = "FASM2008";

    /// <summary>El nombre del proyecto no coincide con el nombre del archivo <c>.fproj</c>.</summary>
    public const string ProjectNameMismatch = "FASM2009";
}
