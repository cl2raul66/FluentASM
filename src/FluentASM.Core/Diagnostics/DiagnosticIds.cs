namespace FluentASM.Core.Diagnostics;

/// <summary>
/// Identificadores de diagnóstico definidos por la especificación oficial
/// (véase la sección «Pipeline del compilador y Diagnósticos» de <c>CONTRIBUTING.md</c>).
/// </summary>
public static class DiagnosticIds
{
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
