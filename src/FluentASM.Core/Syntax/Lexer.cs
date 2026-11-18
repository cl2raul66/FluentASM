using System.Text;
using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax;

/// <summary>
/// Analizador léxico de FluentASM (Fase 1).
/// </summary>
/// <remarks>
/// <para>
/// Es sensible a mayúsculas y minúsculas, omite comentarios de línea (<c>//</c>) y de
/// bloque (<c>/* ... */</c>), y conserva línea, columna y span de cada token.
/// </para>
/// <para>
/// Los literales enteros admiten separadores <c>_</c> entre dígitos, prefijos
/// <c>0x</c>/<c>0b</c> y signo negativo cuando el <c>-</c> está pegado al número
/// (por ejemplo, un desplazamiento <c>[Gpr5 -16]</c>). Los literales de cadena
/// admiten los escapes <c>\n</c>, <c>\t</c>, <c>\r</c>, <c>\0</c>, <c>\\</c>,
/// <c>\"</c> y <c>\xNN</c>.
/// </para>
/// </remarks>
public sealed class Lexer
{
    private readonly string text;
    private readonly string filePath;
    private readonly DiagnosticBag diagnostics;
    private readonly List<Token> tokens = [];
    private int position;
    private int line = 1;
    private int column = 1;

    private Lexer(string text, string filePath, DiagnosticBag diagnostics)
    {
        this.text = text;
        this.filePath = filePath;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Tokeniza un texto fuente completo.
    /// </summary>
    /// <param name="text">Contenido del archivo <c>.fasm</c>.</param>
    /// <param name="filePath">Ruta usada en las ubicaciones de diagnóstico y de token.</param>
    /// <param name="diagnostics">Bolsa compartida opcional; si se omite se crea una nueva.</param>
    public static LexResult Tokenize(string text, string filePath, DiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var bag = diagnostics ?? new DiagnosticBag();
        var lexer = new Lexer(text, filePath, bag);
        lexer.ScanAll();
        return new LexResult(lexer.tokens, bag);
    }

    private bool IsAtEnd => position >= text.Length;

    private char Current => position < text.Length ? text[position] : '\0';

    private char Peek(int offset)
    {
        var index = position + offset;
        return index >= 0 && index < text.Length ? text[index] : '\0';
    }

    private SourceLocation Location(int atLine, int atColumn) => new(filePath, atLine, atColumn);

    private void ScanAll()
    {
        // Un BOM UTF-8 al inicio no forma parte del lenguaje.
        if (position < text.Length && text[position] == '\uFEFF')
        {
            position = 1;
        }

        while (!IsAtEnd)
        {
            var startLine = line;
            var startColumn = column;
            var current = Current;

            if (char.IsWhiteSpace(current))
            {
                Advance();
                continue;
            }

            if (current == '/' && Peek(1) == '/')
            {
                SkipLineComment();
                continue;
            }

            if (current == '/' && Peek(1) == '*')
            {
                SkipBlockComment(startLine, startColumn);
                continue;
            }

            if (current == '"')
            {
                ScanString(startLine, startColumn);
                continue;
            }

            if (current == '@')
            {
                ScanLabel(startLine, startColumn);
                continue;
            }

            if (char.IsAsciiDigit(current) || (current == '-' && char.IsAsciiDigit(Peek(1))))
            {
                ScanInteger(startLine, startColumn, negative: current == '-');
                continue;
            }

            if (current == '_' && !IsIdentifierPart(Peek(1)))
            {
                AddSymbol(TokenKind.Underscore, startLine, startColumn, 1);
                continue;
            }

            if (IsIdentifierStart(current))
            {
                ScanWord(startLine, startColumn);
                continue;
            }

            ScanSymbol(startLine, startColumn);
        }

        tokens.Add(new Token
        {
            Kind = TokenKind.EndOfFile,
            Text = string.Empty,
            Span = new TextSpan(position, 0),
            Location = Location(line, column),
        });
    }

    private void ScanSymbol(int startLine, int startColumn)
    {
        switch (Current)
        {
            case '.':
                AddSymbol(TokenKind.Dot, startLine, startColumn, 1);
                return;
            case ':':
                AddSymbol(TokenKind.Colon, startLine, startColumn, 1);
                return;
            case ',':
                AddSymbol(TokenKind.Comma, startLine, startColumn, 1);
                return;
            case '(':
                AddSymbol(TokenKind.OpenParen, startLine, startColumn, 1);
                return;
            case ')':
                AddSymbol(TokenKind.CloseParen, startLine, startColumn, 1);
                return;
            case '[':
                AddSymbol(TokenKind.OpenBracket, startLine, startColumn, 1);
                return;
            case ']':
                AddSymbol(TokenKind.CloseBracket, startLine, startColumn, 1);
                return;
            case '{':
                AddSymbol(TokenKind.OpenBrace, startLine, startColumn, 1);
                return;
            case '}':
                AddSymbol(TokenKind.CloseBrace, startLine, startColumn, 1);
                return;
            case '<':
                AddSymbol(TokenKind.OpenAngle, startLine, startColumn, 1);
                return;
            case '>':
                AddSymbol(TokenKind.CloseAngle, startLine, startColumn, 1);
                return;
            case '+':
                AddSymbol(TokenKind.Plus, startLine, startColumn, 1);
                return;
            case '*':
                AddSymbol(TokenKind.Star, startLine, startColumn, 1);
                return;
            case '-':
                AddSymbol(
                    Peek(1) == '>' ? TokenKind.Arrow : TokenKind.Minus,
                    startLine,
                    startColumn,
                    Peek(1) == '>' ? 2 : 1);
                return;
            default:
                diagnostics.Error(
                    DiagnosticIds.LexUnexpectedCharacter,
                    $"Carácter inesperado {Describe(Current)}.",
                    Location(startLine, startColumn),
                    new TextSpan(position, 1));
                Advance();
                return;
        }
    }

    private void ScanWord(int startLine, int startColumn)
    {
        var start = position;
        while (!IsAtEnd && IsIdentifierPart(Current))
        {
            Advance();
        }

        var word = text[start..position];
        var kind = TokenKind.Identifier;
        var magnitude = MagnitudeKind.None;

        if (MagnitudeNames.TryParse(word, out var parsedMagnitude))
        {
            kind = TokenKind.Magnitude;
            magnitude = parsedMagnitude;
        }
        else
        {
            kind = word switch
            {
                "defaultValue" => TokenKind.DefaultValue,
                "if" => TokenKind.If,
                "not" => TokenKind.Not,
                "and" => TokenKind.And,
                "or" => TokenKind.Or,
                "equal" => TokenKind.Equal,
                "unequal" => TokenKind.Unequal,
                "Port" => TokenKind.Port,
                _ => TokenKind.Identifier,
            };
        }

        tokens.Add(new Token
        {
            Kind = kind,
            Text = word,
            Span = new TextSpan(start, word.Length),
            Location = Location(startLine, startColumn),
            Magnitude = magnitude,
        });
    }

    private void ScanLabel(int startLine, int startColumn)
    {
        var start = position;
        Advance();

        if (!IsIdentifierStart(Current))
        {
            while (!IsAtEnd && IsIdentifierPart(Current))
            {
                Advance();
            }

            diagnostics.Error(
                DiagnosticIds.LexInvalidLabel,
                "La etiqueta local debe tener un nombre después de '@' (por ejemplo, '@bucle').",
                Location(startLine, startColumn),
                new TextSpan(start, position - start));
            return;
        }

        while (!IsAtEnd && IsIdentifierPart(Current))
        {
            Advance();
        }

        var labelText = text[start..position];
        tokens.Add(new Token
        {
            Kind = TokenKind.Label,
            Text = labelText,
            Span = new TextSpan(start, labelText.Length),
            Location = Location(startLine, startColumn),
        });
    }

    private void ScanInteger(int startLine, int startColumn, bool negative)
    {
        const ulong limit = (ulong)long.MaxValue;

        var start = position;
        var valid = true;
        var value = 0UL;

        if (negative)
        {
            Advance();
        }

        var isHex = false;
        var isBinary = false;
        if (Current == '0' && Peek(1) == 'x')
        {
            isHex = true;
            Advance();
            Advance();
        }
        else if (Current == '0' && Peek(1) == 'b')
        {
            isBinary = true;
            Advance();
            Advance();
        }

        var radix = isHex ? 16UL : isBinary ? 2UL : 10UL;
        var digitCount = 0;
        var lastWasSeparator = false;

        while (!IsAtEnd)
        {
            var current = Current;
            if (current == '_')
            {
                if (digitCount == 0 || lastWasSeparator || !IsDigitForBase(Peek(1), isHex, isBinary))
                {
                    valid = false;
                }

                lastWasSeparator = true;
                Advance();
                continue;
            }

            if (!IsDigitForBase(current, isHex, isBinary))
            {
                break;
            }

            var digit = (ulong)DigitValue(current);
            if (value > (limit - digit) / radix)
            {
                valid = false;
            }
            else
            {
                value = (value * radix) + digit;
            }

            digitCount++;
            lastWasSeparator = false;
            Advance();
        }

        if (digitCount == 0)
        {
            valid = false;
        }

        if (!IsAtEnd && IsIdentifierPart(Current))
        {
            valid = false;
            while (!IsAtEnd && IsIdentifierPart(Current))
            {
                Advance();
            }
        }

        var tokenText = text[start..position];
        if (!valid)
        {
            diagnostics.Error(
                DiagnosticIds.LexInvalidNumber,
                $"Literal numérico inválido '{tokenText}'.",
                Location(startLine, startColumn),
                new TextSpan(start, tokenText.Length));
            return;
        }

        tokens.Add(new Token
        {
            Kind = TokenKind.Integer,
            Text = tokenText,
            Span = new TextSpan(start, tokenText.Length),
            Location = Location(startLine, startColumn),
            IntegerValue = negative ? -(long)value : (long)value,
        });
    }

    private void ScanString(int startLine, int startColumn)
    {
        var start = position;
        Advance();

        var value = new StringBuilder();
        var terminated = false;

        while (!IsAtEnd)
        {
            var current = Current;
            if (current == '"')
            {
                Advance();
                terminated = true;
                break;
            }

            if (current == '\n' || current == '\r')
            {
                break;
            }

            if (current != '\\')
            {
                value.Append(current);
                Advance();
                continue;
            }

            var escapeLine = line;
            var escapeColumn = column;
            var escapeStart = position;
            Advance();

            if (IsAtEnd)
            {
                break;
            }

            switch (Current)
            {
                case 'n':
                    value.Append('\n');
                    Advance();
                    break;
                case 't':
                    value.Append('\t');
                    Advance();
                    break;
                case 'r':
                    value.Append('\r');
                    Advance();
                    break;
                case '0':
                    value.Append('\0');
                    Advance();
                    break;
                case '\\':
                    value.Append('\\');
                    Advance();
                    break;
                case '"':
                    value.Append('"');
                    Advance();
                    break;
                case 'x':
                    Advance();
                    if (char.IsAsciiHexDigit(Current) && char.IsAsciiHexDigit(Peek(1)))
                    {
                        value.Append((char)((HexValue(Current) * 16) + HexValue(Peek(1))));
                        Advance();
                        Advance();
                    }
                    else
                    {
                        diagnostics.Error(
                            DiagnosticIds.LexInvalidEscape,
                            "El escape '\\x' requiere exactamente dos dígitos hexadecimales.",
                            Location(escapeLine, escapeColumn),
                            new TextSpan(escapeStart, position - escapeStart));
                    }

                    break;
                default:
                    diagnostics.Error(
                        DiagnosticIds.LexInvalidEscape,
                        $"Secuencia de escape desconocida '\\{Current}'.",
                        Location(escapeLine, escapeColumn),
                        new TextSpan(escapeStart, position - escapeStart + 1));
                    value.Append(Current);
                    Advance();
                    break;
            }
        }

        if (!terminated)
        {
            diagnostics.Error(
                DiagnosticIds.LexUnterminatedString,
                "La cadena de texto no se cerró antes del final de la línea.",
                Location(startLine, startColumn),
                new TextSpan(start, position - start));
        }

        var tokenText = text[start..position];
        tokens.Add(new Token
        {
            Kind = TokenKind.String,
            Text = tokenText,
            Span = new TextSpan(start, tokenText.Length),
            Location = Location(startLine, startColumn),
            StringValue = value.ToString(),
        });
    }

    private void SkipLineComment()
    {
        while (!IsAtEnd && Current != '\n' && Current != '\r')
        {
            Advance();
        }
    }

    private void SkipBlockComment(int startLine, int startColumn)
    {
        var start = position;
        Advance();
        Advance();

        while (!IsAtEnd)
        {
            if (Current == '*' && Peek(1) == '/')
            {
                Advance();
                Advance();
                return;
            }

            Advance();
        }

        diagnostics.Error(
            DiagnosticIds.LexUnterminatedBlockComment,
            "El comentario de bloque no se cerró con '*/'.",
            Location(startLine, startColumn),
            new TextSpan(start, position - start));
    }

    private void AddSymbol(TokenKind kind, int startLine, int startColumn, int length)
    {
        var start = position;
        for (var index = 0; index < length; index++)
        {
            Advance();
        }

        tokens.Add(new Token
        {
            Kind = kind,
            Text = text[start..position],
            Span = new TextSpan(start, length),
            Location = Location(startLine, startColumn),
        });
    }

    private void Advance()
    {
        if (IsAtEnd)
        {
            return;
        }

        var current = text[position];
        position++;

        if (current == '\r')
        {
            // En CRLF, el salto de línea lo cuenta el '\n' siguiente.
            if (position >= text.Length || text[position] != '\n')
            {
                line++;
                column = 1;
            }

            return;
        }

        if (current == '\n')
        {
            line++;
            column = 1;
            return;
        }

        column++;
    }

    private static bool IsIdentifierStart(char value) => char.IsAsciiLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';

    private static bool IsDigitForBase(char value, bool isHex, bool isBinary)
    {
        if (isHex)
        {
            return char.IsAsciiHexDigit(value);
        }

        if (isBinary)
        {
            return value is '0' or '1';
        }

        return char.IsAsciiDigit(value);
    }

    private static int DigitValue(char value)
    {
        if (value >= '0' && value <= '9')
        {
            return value - '0';
        }

        if (value >= 'a' && value <= 'f')
        {
            return value - 'a' + 10;
        }

        return value - 'A' + 10;
    }

    private static int HexValue(char value) => DigitValue(value);

    private static string Describe(char value) =>
        char.IsControl(value) ? $"'\\u{(int)value:X4}'" : $"'{value}'";
}
