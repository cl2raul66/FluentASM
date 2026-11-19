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

    // FASM2xxx — Programa, símbolos, secciones y datos duros en .execute.

    /// <summary>Dato literal prohibido escrito dentro de <c>.execute(...)</c>.</summary>
    public const string LiteralInExecute = "FASM2007";

    /// <summary>Símbolo, rutina, parámetro o etiqueta duplicado.</summary>
    public const string DuplicateSymbol = "FASM2010";

    /// <summary>El payload declarado en <c>.value(...)</c> no cabe en la capacidad del símbolo.</summary>
    public const string DataPayloadOverflow = "FASM2011";

    /// <summary>Referencia a un símbolo o etiqueta que no existe en su ámbito.</summary>
    public const string UnknownSymbol = "FASM2013";

    /// <summary>Una rutina declara más de cuatro parámetros de entrada.</summary>
    public const string TooManyParameters = "FASM2015";

    /// <summary>Parámetro de <c>d.run(...)</c> sin capacidad física declarada.</summary>
    public const string ParameterWithoutMagnitude = "FASM2016";

    /// <summary>Valor de datos con forma no admitida (solo entero, cadena o <c>defaultValue</c>).</summary>
    public const string InvalidDataValue = "FASM2018";

    /// <summary>Identificador reservado por el modelo físico usado como símbolo de usuario.</summary>
    public const string ReservedIdentifier = "FASM2019";

    // FASM3xxx — ATM (Bit/Byte), registros, vistas y banderas.

    /// <summary>Conflicto entre <c>.high8</c> y <c>.low8</c> con prefijo <c>REX</c> en la misma instrucción.</summary>
    public const string High8Low8Conflict = "FASM3002";

    /// <summary>Nombre legacy prohibido, con sugerencia al nombre canónico.</summary>
    public const string LegacyName = "FASM3012";

    /// <summary>Índice de <c>Gpr</c> fuera del rango 0–15.</summary>
    public const string GprIndexOutOfRange = "FASM3013";

    /// <summary>Vista de registro desconocida.</summary>
    public const string InvalidRegisterView = "FASM3014";

    /// <summary><c>.high8</c> usado en un registro que no lo posee (<c>Gpr4</c>–<c>Gpr15</c>).</summary>
    public const string High8NotAvailable = "FASM3015";

    /// <summary>Se esperaba una magnitud en bytes y se recibió una en bits (o viceversa).</summary>
    public const string ByteContextExpected = "FASM3020";

    /// <summary>Se esperaba una magnitud en bits y se recibió una en bytes (o viceversa).</summary>
    public const string BitContextExpected = "FASM3021";

    /// <summary>Ancho incompatible entre receptor, fuente y capacidad de memoria.</summary>
    public const string WidthMismatch = "FASM3022";

    /// <summary>Falta la capacidad explícita <c>Byte:N</c> en un acceso a memoria por registro.</summary>
    public const string MemoryCapacityRequired = "FASM3023";

    /// <summary>Magnitud mal formada, vacía o fuera de rango.</summary>
    public const string InvalidMagnitude = "FASM3024";

    /// <summary>Receptor de retorno de rutina inválido.</summary>
    public const string InvalidReturnRegister = "FASM3025";

    /// <summary>Acceso directo inválido a <c>Ipr</c> con <c>.copy</c>.</summary>
    public const string IprDirectCopy = "FASM3060";

    // FASM4xxx — Operandos, direccionamiento, verbos y flujo.

    /// <summary>Verbo no válido para el tipo de receptor.</summary>
    public const string InvalidVerb = "FASM4001";

    /// <summary>Escala de direccionamiento inválida (solo 1, 2, 4 u 8).</summary>
    public const string InvalidScale = "FASM4002";

    /// <summary><c>Gpr4</c> usado como índice en direccionamiento SIB.</summary>
    public const string Gpr4AsIndex = "FASM4003";

    /// <summary>Número de argumentos incorrecto para el verbo.</summary>
    public const string InvalidArgumentCount = "FASM4005";

    /// <summary>Lectura de una bandera de <c>Frf</c> dejada indefinida por la instrucción previa.</summary>
    public const string UndefinedFlag = "FASM4012";

    /// <summary>Referencia relativa (<c>rel8</c>/<c>rel32</c>) cuyo destino no cabe en su hueco.</summary>
    /// <remarks>
    /// Se emite en el layout de M3 cuando <c>Ipr.flowLoop(@etiqueta)</c> no cabe en
    /// <c>rel8</c> (la instrucción <c>LOOP</c> no tiene forma cercana) o cuando una
    /// relocalización excede el ancho de su campo.
    /// </remarks>
    public const string RelativeOutOfRange = "FASM4022";

    /// <summary>Copia directa memoria → memoria con <c>.copy(...)</c>.</summary>
    public const string MemoryToMemoryCopy = "FASM4026";

    // FASM5xxx — Contratos de llamada (flowCall), pila y cierre de rutinas.

    /// <summary>El destino de <c>Ipr.flowCall(...)</c> no existe.</summary>
    public const string CallTargetNotFound = "FASM5001";

    /// <summary>Contrato de <c>Ipr.flowCall(...)</c> desajustado con la rutina destino.</summary>
    public const string CallContractMismatch = "FASM5002";

    /// <summary>Lectura de un registro invalidado lógicamente tras <c>Ipr.flowCall(...)</c>.</summary>
    public const string InvalidatedInputRead = "FASM5003";

    /// <summary>Balance de pila incorrecto (<c>push</c>/<c>pop</c>/<c>reserve</c>/<c>release</c>).</summary>
    public const string StackImbalance = "FASM5004";

    /// <summary>La rutina no cierra explícitamente con <c>Ipr.flowStackJump()</c> o <c>Cpu.halt()</c>.</summary>
    public const string RoutineNotClosed = "FASM5005";

    /// <summary>Entrada de <c>Ipr.flowCall(...)</c> usada antes de ser cargada.</summary>
    public const string CallInputNotLoaded = "FASM5006";

    /// <summary>Pila desalineada a 16 bytes en el momento de <c>Ipr.flowCall(...)</c>.</summary>
    public const string StackNotAligned = "FASM5007";
}


