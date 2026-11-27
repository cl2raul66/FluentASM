using System.Text;
using FluentASM.Lsp.Diagnostics;
using FluentASM.Lsp.Protocol;
using FluentASM.Lsp.Server;

namespace FluentASM.Lsp.Formatting;

/// <summary>
/// Motor de formateo de FluentASM. Aplica el Pacto v1
/// (<c>plan-0002-pacto-v1.md</c>) sobre el texto del documento y devuelve
/// ediciones mínimas (<see cref="TextEdit"/>), ordenadas y sin solapamientos.
/// </summary>
/// <remarks>
/// <para>
/// El motor es 100 % libre de WinUI: no conoce el editor, no abre ventanas y
/// no depende de hilos de interfaz. El mismo código sirve al IDE propio
/// (<em>in-process</em>) y al futuro host JSON-RPC de la Fase 3.
/// </para>
/// <para>
/// Reglas aplicadas: indentación por bloques <c>{ }</c>, nivel raíz
/// <em>type-state</em> (<c>Proyecto</c> → <c>.data(...)</c> →
/// <c>.execute(...)</c> → <c>.initialize()</c>), un espacio alrededor de
/// <c>-&gt;</c>, sin espacio antes de <c>(</c> ni <c>&lt;</c>, sin espacios
/// internos en listas y genéricos, <c>Bit:N</c>/<c>Byte:N</c>, separación
/// correcta entre magnitud y <c>[registro]</c> y comentarios intactos.
/// </para>
/// <para>
/// <c>trim_trailing_whitespace = false</c> es sagrado en <c>.fasm</c>: nunca se
/// recortan los espacios de alineación manual anteriores a un comentario
/// <c>//</c>. Ante código incompleto, comentarios sin cerrar o símbolos
/// desconocidos, la línea se deja intacta en lugar de adivinar.
/// </para>
/// <para>
/// Incidencias: <c>FASM1D01</c> si <c>.editorconfig</c> es ilegible o inválido,
/// <c>FASM1D02</c> por cada formateo manual, <c>FASM1D04</c> por cada
/// formateo <em>on-type</em> y <c>FASM9999</c> si el motor falla de forma
/// inesperada. Ninguna excepción se propaga al llamador.
/// </para>
/// </remarks>
public sealed class FasmFormatter
{
    private readonly EditorConfigResolver resolver;
    private readonly IncidentRecorder? incidents;

    /// <summary>
    /// Crea el motor con un resolvedor de <c>.editorconfig</c> y un registrador
    /// de sucesos opcionales.
    /// </summary>
    public FasmFormatter(EditorConfigResolver? resolver = null, IncidentRecorder? incidents = null)
    {
        this.resolver = resolver ?? new EditorConfigResolver();
        this.incidents = incidents;
    }

    /// <summary>Formatea el documento completo.</summary>
    /// <param name="text">Texto normalizado del documento (<c>\n</c>).</param>
    /// <param name="filePath">Ruta física para resolver <c>.editorconfig</c>; puede ser <c>null</c>.</param>
    public IReadOnlyList<TextEdit> FormatDocument(string text, string? filePath)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<TextEdit>();
        }

        try
        {
            var lines = new DocumentLines(text);
            FormattingOptions options = ResolveOptions(filePath);
            var builder = new TextEditBuilder();
            FormatLines(text, lines, options, 0, lines.LineCount - 1, builder);
            ApplyFinalNewline(text, lines, options, builder);

            IReadOnlyList<TextEdit> edits = builder.Build();
            if (edits.Count > 0)
            {
                ReportInfo(
                    "FASM1D02",
                    $"Formateo de documento aplicado: {edits.Count} edición(es).",
                    filePath);
            }

            return edits;
        }
        catch (Exception ex)
        {
            ReportException(ex, "FormatDocument", filePath);
            return Array.Empty<TextEdit>();
        }
    }

    /// <summary>
    /// Formatea únicamente las líneas tocadas por <paramref name="range"/>.
    /// La indentación se calcula siempre desde el inicio del documento para no
    /// desalinear bloques, pero solo se emiten ediciones dentro del rango.
    /// </summary>
    public IReadOnlyList<TextEdit> FormatRange(string text, string? filePath, LspRange range)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<TextEdit>();
        }

        try
        {
            var lines = new DocumentLines(text);
            int firstLine = Math.Clamp(range.Start.Line, 0, lines.LineCount - 1);
            int lastLine = Math.Clamp(range.End.Line, 0, lines.LineCount - 1);
            if (range.End.Character == 0 && lastLine > firstLine)
            {
                // El final de rango LSP es exclusivo: si cae al inicio de la
                // línea siguiente, esa línea no forma parte de la selección.
                lastLine--;
            }

            FormattingOptions options = ResolveOptions(filePath);
            var builder = new TextEditBuilder();
            FormatLines(text, lines, options, firstLine, lastLine, builder);

            IReadOnlyList<TextEdit> edits = builder.Build();
            if (edits.Count > 0)
            {
                ReportInfo(
                    "FASM1D02",
                    $"Formateo de selección aplicado: {edits.Count} edición(es) en las líneas {firstLine + 1}-{lastLine + 1}.",
                    filePath);
            }

            return edits;
        }
        catch (Exception ex)
        {
            ReportException(ex, "FormatRange", filePath);
            return Array.Empty<TextEdit>();
        }
    }

    /// <summary>
    /// Formateo <em>on-type</em> acotado exclusivamente a <c>}</c> y al
    /// <c>&gt;</c> que completa <c>-&gt;</c>. Nunca procesa <c>\n</c> ni
    /// reindenta otras líneas.
    /// </summary>
    public IReadOnlyList<TextEdit> FormatOnType(
        string text,
        string? filePath,
        Position position,
        char trigger)
    {
        if (string.IsNullOrEmpty(text) || (trigger != '}' && trigger != '>'))
        {
            return Array.Empty<TextEdit>();
        }

        try
        {
            var lines = new DocumentLines(text);
            int line = Math.Clamp(position.Line, 0, lines.LineCount - 1);
            string lineText = lines.GetLineText(line);

            if (trigger == '}' && !lineText.Contains('}'))
            {
                return Array.Empty<TextEdit>();
            }

            if (trigger == '>' &&
                !lineText.Contains("->", StringComparison.Ordinal))
            {
                // Un '>' que no completa '->' (por ejemplo, el cierre de un
                // genérico) no dispara formateo.
                return Array.Empty<TextEdit>();
            }

            FormattingOptions options = ResolveOptions(filePath);
            var builder = new TextEditBuilder();
            FormatLines(text, lines, options, line, line, builder);

            IReadOnlyList<TextEdit> edits = builder.Build();
            if (edits.Count > 0)
            {
                ReportInfo(
                    "FASM1D04",
                    $"On-type aplicado en la línea {line + 1} tras '{trigger}': {edits.Count} edición(es).",
                    filePath);
            }

            return edits;
        }
        catch (Exception ex)
        {
            ReportException(ex, "FormatOnType", filePath);
            return Array.Empty<TextEdit>();
        }
    }

    /// <summary>
    /// Devuelve la unidad de indentación efectiva (un tabulador o
    /// <c>indent_size</c> espacios) para que la Capa A del IDE mantenga
    /// <c>Enter</c> alineado con el formateo sin leer <c>.editorconfig</c>.
    /// </summary>
    public string GetIndentUnit(string? filePath)
    {
        try
        {
            return ResolveOptions(filePath).Indent(1);
        }
        catch (Exception ex)
        {
            ReportException(ex, nameof(GetIndentUnit), filePath);
            return FormattingOptions.Default.Indent(1);
        }
    }

    private FormattingOptions ResolveOptions(string? filePath)
    {
        ResolvedFormattingOptions resolved = resolver.Resolve(filePath);
        if (resolved.WarningCode is { Length: > 0 } code)
        {
            string message = resolved.WarningMessage is { Length: > 0 } detail
                ? detail
                : "Se aplicaron los valores por defecto de formateo.";
            if (incidents is not null)
            {
                incidents.RecordMappingError(code, message, "FasmFormatter", filePath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[{code}] {message}");
            }
        }

        return resolved.Options;
    }

    private static void FormatLines(
        string text,
        DocumentLines lines,
        FormattingOptions options,
        int firstLine,
        int lastLine,
        TextEditBuilder builder)
    {
        int depth = 0;
        bool inBlockComment = false;
        bool previousBlank = false;
        bool previousBlankInRange = false;

        for (int line = 0; line < lines.LineCount; line++)
        {
            int start = lines.GetLineStart(line);
            int end = lines.GetLineEnd(line);
            string original = text[start..end];
            LineScan scan = ScanLine(original, ref inBlockComment);

            bool isBlank = !scan.ProtectedLine && !scan.HasRawToken && original.Trim().Length == 0;
            bool inRange = line >= firstLine && line <= lastLine;

            if (isBlank && previousBlank && previousBlankInRange && inRange)
            {
                // Máximo una línea en blanco consecutiva: se elimina la actual
                // junto con su salto de línea.
                int deleteEnd = line + 1 < lines.LineCount
                    ? lines.GetLineStart(line + 1)
                    : text.Length;
                if (deleteEnd > start)
                {
                    builder.Add(
                        new LspRange(lines.ToPosition(start), lines.ToPosition(deleteEnd)),
                        string.Empty);
                }

                continue;
            }

            string formatted;
            if (scan.ProtectedLine || scan.HasRawToken)
            {
                // Comentario multilínea, cierre de comentario con código detrás
                // o símbolo desconocido: se conserva la línea tal cual.
                formatted = original;
            }
            else if (isBlank)
            {
                // Con trim_trailing_whitespace = false tampoco se recortan los
                // espacios de una línea en blanco.
                formatted = options.TrimTrailingWhitespace ? string.Empty : original;
            }
            else
            {
                int indentLevel;
                if (scan.StartsWithCloseBrace || scan.StartsWithLabel)
                {
                    // Cierres de bloque y etiquetas @nombre se alinean con el
                    // nivel del bloque que los contiene, no con su contenido.
                    indentLevel = Math.Max(0, depth);
                }
                else if (depth == 0 && !scan.StartsWithDot)
                {
                    // En la raíz, el nombre del proyecto y los comentarios
                    // iniciales van en columna 0; los encadenamientos
                    // (.data/.execute/.initialize) van un nivel dentro.
                    indentLevel = 0;
                }
                else
                {
                    indentLevel = depth + 1;
                }

                string indent = options.Indent(indentLevel);
                string code = EmitTokens(scan.Tokens);

                if (code.Length == 0 && scan.Tail.Length > 0)
                {
                    formatted = indent + scan.Tail;
                }
                else
                {
                    string gap = scan.Tail.Length > 0
                        ? scan.GapBeforeComment
                        : (options.TrimTrailingWhitespace ? string.Empty : scan.TrailingWhitespace);
                    formatted = indent + code + gap + scan.Tail;
                }
            }

            if (inRange && !string.Equals(original, formatted, StringComparison.Ordinal))
            {
                AddLineEdit(lines, line, original, formatted, builder);
            }

            previousBlank = isBlank;
            previousBlankInRange = isBlank && inRange;

            depth += scan.OpenBraces - scan.CloseBraces;
            if (depth < 0)
            {
                depth = 0;
            }
        }
    }

    private static void ApplyFinalNewline(
        string text,
        DocumentLines lines,
        FormattingOptions options,
        TextEditBuilder builder)
    {
        if (text.Length == 0)
        {
            return;
        }

        bool endsWithNewline = text[^1] is '\n' or '\r';

        if (options.InsertFinalNewline)
        {
            if (endsWithNewline)
            {
                return;
            }

            Position end = lines.ToPosition(text.Length);
            builder.Add(new LspRange(end, end), "\n");
            return;
        }

        if (!endsWithNewline)
        {
            return;
        }

        int removeLength = 1;
        if (text.Length >= 2 &&
            ((text[^1] == '\n' && text[^2] == '\r') ||
             (text[^1] == '\r' && text[^2] == '\n')))
        {
            removeLength = 2;
        }

        Position removeStart = lines.ToPosition(text.Length - removeLength);
        Position removeEnd = lines.ToPosition(text.Length);
        builder.Add(new LspRange(removeStart, removeEnd), string.Empty);
    }

    private static void AddLineEdit(
        DocumentLines lines,
        int line,
        string original,
        string formatted,
        TextEditBuilder builder)
    {
        int lineStart = lines.GetLineStart(line);
        int sharedLimit = Math.Min(original.Length, formatted.Length);

        int prefix = 0;
        while (prefix < sharedLimit && original[prefix] == formatted[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < sharedLimit - prefix &&
               original[original.Length - 1 - suffix] == formatted[formatted.Length - 1 - suffix])
        {
            suffix++;
        }

        int changeStart = lineStart + prefix;
        int changeEnd = lineStart + original.Length - suffix;
        string newText = formatted.Substring(prefix, formatted.Length - prefix - suffix);

        if (changeStart == changeEnd && newText.Length == 0)
        {
            return;
        }

        builder.Add(
            new LspRange(lines.ToPosition(changeStart), lines.ToPosition(changeEnd)),
            newText);
    }

    private static string EmitTokens(IReadOnlyList<FmtToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (int index = 0; index < tokens.Count; index++)
        {
            if (index > 0 && NeedsSpace(tokens, index - 1, index))
            {
                builder.Append(' ');
            }

            builder.Append(tokens[index].Text);
        }

        return builder.ToString();
    }

    private static bool NeedsSpace(IReadOnlyList<FmtToken> tokens, int previousIndex, int index)
    {
        FmtToken previous = tokens[previousIndex];
        FmtToken current = tokens[index];

        // Cierres y separadores pegados al token anterior.
        if (current.Kind is FmtKind.CloseParen
            or FmtKind.CloseBracket
            or FmtKind.CloseAngle
            or FmtKind.CloseBrace
            or FmtKind.Comma
            or FmtKind.Colon
            or FmtKind.Dot)
        {
            return false;
        }

        // Aperturas y puntos pegados al token anterior.
        if (previous.Kind is FmtKind.OpenParen
            or FmtKind.OpenBracket
            or FmtKind.OpenAngle
            or FmtKind.Dot
            or FmtKind.Colon)
        {
            return false;
        }

        // Llamadas y genéricos: no hay espacio antes de '(' ni '<'.
        if (current.Kind is FmtKind.OpenParen or FmtKind.OpenAngle)
        {
            return false;
        }

        // Corchetes pegados, salvo la separación explícita magnitud + [reg].
        if (current.Kind == FmtKind.OpenBracket)
        {
            return previous.Kind == FmtKind.Number &&
                   previousIndex > 0 &&
                   tokens[previousIndex - 1].Kind == FmtKind.Colon;
        }

        // La flecha lambda siempre lleva un espacio a cada lado.
        if (current.Kind == FmtKind.Arrow || previous.Kind == FmtKind.Arrow)
        {
            return true;
        }

        // Operadores aritméticos: espacio entre operandos, sin espacio cuando
        // el signo es unario y estaba pegado al número en el original.
        if (current.Kind == FmtKind.Operator)
        {
            bool unary = current.Text is "-" or "+" &&
                         (previous.Kind is FmtKind.OpenParen
                             or FmtKind.OpenBracket
                             or FmtKind.Comma
                             or FmtKind.Colon
                             or FmtKind.OpenBrace ||
                          (current.Attached && previous.Kind is FmtKind.Operator));
            return !unary;
        }

        if (previous.Kind == FmtKind.Operator)
        {
            bool unary = current.Kind == FmtKind.Number &&
                         (current.Attached ||
                          previousIndex == 0 ||
                          tokens[previousIndex - 1].Kind is FmtKind.OpenParen
                              or FmtKind.OpenBracket
                              or FmtKind.Comma
                              or FmtKind.Colon
                              or FmtKind.OpenBrace
                              or FmtKind.Operator
                              or FmtKind.Arrow);
            return !unary;
        }

        // La llave de apertura se separa con un espacio; el cierre ya se trató.
        if (current.Kind == FmtKind.OpenBrace || previous.Kind == FmtKind.OpenBrace)
        {
            return true;
        }

        if (previous.Kind == FmtKind.Comma)
        {
            return true;
        }

        return true;
    }

    private static LineScan ScanLine(string line, ref bool inBlockComment)
    {
        var scan = new LineScan();
        int length = line.Length;

        if (inBlockComment)
        {
            scan.ProtectedLine = true;
            if (line.Contains("*/", StringComparison.Ordinal))
            {
                inBlockComment = false;
            }

            return scan;
        }

        int index = 0;
        while (index < length)
        {
            char current = line[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '/' && index + 1 < length)
            {
                char next = line[index + 1];
                if (next == '/')
                {
                    scan.Tail = line[index..];
                    break;
                }

                if (next == '*')
                {
                    scan.Tail = line[index..];
                    if (line.IndexOf("*/", index + 2, StringComparison.Ordinal) < 0)
                    {
                        inBlockComment = true;
                    }

                    break;
                }
            }

            if (current == '"')
            {
                int tokenStart = index;
                index++;
                while (index < length)
                {
                    char inside = line[index];
                    if (inside == '\\' && index + 1 < length)
                    {
                        index += 2;
                        continue;
                    }

                    if (inside == '"')
                    {
                        index++;
                        break;
                    }

                    index++;
                }

                AddToken(scan, line[tokenStart..index], FmtKind.String, tokenStart);
                continue;
            }

            if (current == '@')
            {
                int tokenStart = index;
                index++;
                while (index < length && IsIdentifierPart(line[index]))
                {
                    index++;
                }

                AddToken(scan, line[tokenStart..index], FmtKind.Label, tokenStart);
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                int tokenStart = index;
                index++;
                while (index < length && IsIdentifierPart(line[index]))
                {
                    index++;
                }

                string token = line[tokenStart..index];
                AddToken(
                    scan,
                    token,
                    token == "_" ? FmtKind.Underscore : FmtKind.Identifier,
                    tokenStart);
                continue;
            }

            if (char.IsDigit(current))
            {
                int tokenStart = index;
                index++;
                if (line[tokenStart] == '0' &&
                    index < length &&
                    line[index] is 'x' or 'X' or 'b' or 'B')
                {
                    char prefix = line[index];
                    index++;
                    while (index < length)
                    {
                        char digit = line[index];
                        bool valid = digit == '_' ||
                                     char.IsDigit(digit) ||
                                     ((prefix is 'x' or 'X') && Uri.IsHexDigit(digit));
                        if (!valid)
                        {
                            break;
                        }

                        index++;
                    }
                }
                else
                {
                    while (index < length && (char.IsDigit(line[index]) || line[index] == '_'))
                    {
                        index++;
                    }
                }

                AddToken(scan, line[tokenStart..index], FmtKind.Number, tokenStart);
                continue;
            }

            if (current == '-' && index + 1 < length && line[index + 1] == '>')
            {
                AddToken(scan, "->", FmtKind.Arrow, index);
                index += 2;
                continue;
            }

            FmtKind kind = current switch
            {
                '(' => FmtKind.OpenParen,
                ')' => FmtKind.CloseParen,
                '[' => FmtKind.OpenBracket,
                ']' => FmtKind.CloseBracket,
                '{' => FmtKind.OpenBrace,
                '}' => FmtKind.CloseBrace,
                '<' => FmtKind.OpenAngle,
                '>' => FmtKind.CloseAngle,
                ',' => FmtKind.Comma,
                ':' => FmtKind.Colon,
                '.' => FmtKind.Dot,
                '+' or '-' or '*' => FmtKind.Operator,
                _ => FmtKind.Raw,
            };

            if (kind == FmtKind.Raw)
            {
                scan.HasRawToken = true;
            }

            AddToken(scan, current.ToString(), kind, index);
            index++;
        }

        if (scan.Tokens.Count > 0)
        {
            scan.StartsWithCloseBrace = scan.Tokens[0].Kind == FmtKind.CloseBrace;
            scan.StartsWithDot = scan.Tokens[0].Kind == FmtKind.Dot;
            scan.StartsWithLabel = scan.Tokens[0].Kind == FmtKind.Label;
            if (scan.Tail.Length > 0)
            {
                int tailStart = length - scan.Tail.Length;
                scan.GapBeforeComment = line[scan.LastTokenEnd..tailStart];
            }
            else
            {
                scan.TrailingWhitespace = line[scan.LastTokenEnd..];
            }
        }
        else
        {
            scan.TrailingWhitespace = scan.Tail.Length == 0 ? line : string.Empty;
        }

        return scan;
    }

    private static void AddToken(LineScan scan, string tokenText, FmtKind kind, int start)
    {
        bool attached = scan.Tokens.Count > 0 && start == scan.LastTokenEnd;
        scan.Tokens.Add(new FmtToken(tokenText, kind, attached));
        scan.LastTokenEnd = start + tokenText.Length;

        if (kind == FmtKind.OpenBrace)
        {
            scan.OpenBraces++;
        }
        else if (kind == FmtKind.CloseBrace)
        {
            scan.CloseBraces++;
        }
    }

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private void ReportInfo(string code, string message, string? filePath)
    {
        if (incidents is not null)
        {
            incidents.RecordInfo(code, message, "FasmFormatter", filePath);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[{code}] {message}");
        }
    }

    private void ReportException(Exception exception, string operation, string? filePath)
    {
        if (incidents is not null)
        {
            incidents.RecordException(exception, $"FasmFormatter.{operation}", filePath);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FASM9999] FasmFormatter.{operation}: {exception}");
        }
    }

    private enum FmtKind
    {
        Identifier,
        Number,
        String,
        Label,
        OpenParen,
        CloseParen,
        OpenBracket,
        CloseBracket,
        OpenBrace,
        CloseBrace,
        OpenAngle,
        CloseAngle,
        Arrow,
        Comma,
        Colon,
        Dot,
        Operator,
        Underscore,
        Raw,
    }

    private readonly record struct FmtToken(string Text, FmtKind Kind, bool Attached);

    private sealed class LineScan
    {
        public bool ProtectedLine { get; set; }

        public bool HasRawToken { get; set; }

        public bool StartsWithCloseBrace { get; set; }

        public bool StartsWithDot { get; set; }

        public bool StartsWithLabel { get; set; }

        public List<FmtToken> Tokens { get; } = [];

        public string Tail { get; set; } = string.Empty;

        public string GapBeforeComment { get; set; } = string.Empty;

        public string TrailingWhitespace { get; set; } = string.Empty;

        public int LastTokenEnd { get; set; }

        public int OpenBraces { get; set; }

        public int CloseBraces { get; set; }
    }
}
