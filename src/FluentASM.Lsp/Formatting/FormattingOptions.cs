namespace FluentASM.Lsp.Formatting;

/// <summary>Estilo de indentación efectivo.</summary>
public enum IndentStyleKind
{
    /// <summary>Indentación con espacios (valor del repositorio).</summary>
    Space,

    /// <summary>Indentación con tabuladores.</summary>
    Tab,
}

/// <summary>Fin de línea efectivo declarado en <c>.editorconfig</c>.</summary>
public enum EndOfLineKind
{
    /// <summary>Salto de línea <c>\n</c> (valor del repositorio).</summary>
    Lf,

    /// <summary>Salto de línea <c>\r\n</c>.</summary>
    CrLf,

    /// <summary>Salto de línea <c>\r</c>.</summary>
    Cr,
}

/// <summary>
/// Opciones de formateo resueltas desde <c>.editorconfig</c>. Es inmutable y
/// totalmente independiente de WinUI para que el LSP pueda ejecutarse en un
/// proceso sin interfaz.
/// </summary>
public sealed record FormattingOptions
{
    /// <summary>Valores por defecto del repositorio: 4 espacios, LF y sin recorte final.</summary>
    public static FormattingOptions Default { get; } = new();

    /// <summary>Estilo de indentación.</summary>
    public IndentStyleKind IndentStyle { get; init; } = IndentStyleKind.Space;

    /// <summary>Ancho de indentación en espacios (ignorado si el estilo es tabulador).</summary>
    public int IndentSize { get; init; } = 4;

    /// <summary>Indica si el documento debe terminar con un salto de línea.</summary>
    public bool InsertFinalNewline { get; init; } = true;

    /// <summary>
    /// Indica si se pueden recortar espacios finales. En <c>[*.fasm]</c> vale
    /// <c>false</c>: nunca se destruyen alineaciones manuales.
    /// </summary>
    public bool TrimTrailingWhitespace { get; init; }

    /// <summary>Fin de línea efectivo; el editor mantiene <c>\n</c> normalizado.</summary>
    public EndOfLineKind EndOfLine { get; init; } = EndOfLineKind.Lf;

    /// <summary>Secuencia de fin de línea solicitada por la configuración.</summary>
    public string NewLine => EndOfLine switch
    {
        EndOfLineKind.CrLf => "\r\n",
        EndOfLineKind.Cr => "\r",
        _ => "\n",
    };

    /// <summary>Devuelve la cadena de indentación para un nivel lógico (base 0).</summary>
    public string Indent(int level)
    {
        if (level <= 0)
        {
            return string.Empty;
        }

        return IndentStyle == IndentStyleKind.Tab
            ? new string('\t', level)
            : new string(' ', Math.Clamp(IndentSize, 1, 16) * level);
    }
}

/// <summary>
/// Resultado de resolver <c>.editorconfig</c>. Cuando <see cref="WarningCode"/>
/// no es <c>null</c>, el formateador aplicó los valores por defecto y registrará
/// el incidente correspondiente.
/// </summary>
/// <param name="Options">Opciones efectivas.</param>
/// <param name="WarningCode">Código de incidencia (<c>FASM1D01</c>) o <c>null</c>.</param>
/// <param name="WarningMessage">Detalle legible de la incidencia.</param>
public sealed record ResolvedFormattingOptions(
    FormattingOptions Options,
    string? WarningCode = null,
    string? WarningMessage = null);
