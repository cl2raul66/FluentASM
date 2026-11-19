using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace FluentASM_IDE.Services;

/// <summary>
/// Resaltador de sintaxis para FluentASM en <see cref="RichEditBox"/>.
/// Aplica colores según el tema (claro/oscuro) usando <see cref="ITextCharacterFormat"/>.
/// </summary>
public static class AsmHighlighter
{
    // ==============================================
    // PALETA DEL TEMA OSCURO (Stitch minimalista)
    // ==============================================
    public static readonly Color DarkColorKeyword = Color.FromArgb(255, 255, 212, 190);    // #FFD4BE
    public static readonly Color DarkColorSection = Color.FromArgb(255, 255, 219, 201);    // #FFDBC9
    public static readonly Color DarkColorType = Color.FromArgb(255, 180, 196, 229);       // #B4C4E5
    public static readonly Color DarkColorRegister = Color.FromArgb(255, 147, 196, 125);   // #93C47D
    public static readonly Color DarkColorNumber = Color.FromArgb(255, 181, 206, 168);     // #B5CEA8
    public static readonly Color DarkColorString = Color.FromArgb(255, 206, 145, 120);     // #CE9178
    public static readonly Color DarkColorComment = Color.FromArgb(255, 106, 153, 85);     // #6A9955
    public static readonly Color DarkColorOperator = Color.FromArgb(255, 212, 212, 212);   // #D4D4D4
    public static readonly Color DarkColorDirective = Color.FromArgb(255, 180, 196, 229);  // #B4C4E5
    public static readonly Color DarkColorDefault = Color.FromArgb(255, 229, 226, 225);    // #E5E2E1

    // ==============================================
    // PALETA DEL TEMA CLARO (Fluent limpio)
    // ==============================================
    public static readonly Color LightColorKeyword = Color.FromArgb(255, 161, 56, 0);      // #A13800
    public static readonly Color LightColorSection = Color.FromArgb(255, 0, 103, 192);     // #0067C0
    public static readonly Color LightColorType = Color.FromArgb(255, 0, 72, 128);         // #004880
    public static readonly Color LightColorRegister = Color.FromArgb(255, 0, 96, 0);       // #006000
    public static readonly Color LightColorNumber = Color.FromArgb(255, 9, 87, 79);        // #09574F
    public static readonly Color LightColorString = Color.FromArgb(255, 163, 21, 21);      // #A31515
    public static readonly Color LightColorComment = Color.FromArgb(255, 0, 128, 0);       // #008000
    public static readonly Color LightColorOperator = Color.FromArgb(255, 25, 28, 30);     // #191C1E
    public static readonly Color LightColorDirective = Color.FromArgb(255, 0, 72, 128);    // #004880
    public static readonly Color LightColorDefault = Color.FromArgb(255, 25, 28, 30);      // #191C1E

    public static bool IsCurrentDark { get; set; } = true;

    /// <summary>Color por defecto según tema actual.</summary>
    public static Color ColorDefault => IsCurrentDark ? DarkColorDefault : LightColorDefault;

    /// <summary>
    /// Aplica resaltado de sintaxis al documento completo del <see cref="RichEditBox"/>.
    /// </summary>
    public static void Highlight(RichEditBox editor)
    {
        editor.Document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var allRange = editor.Document.GetRange(0, text.Length);
        allRange.CharacterFormat.ForegroundColor = ColorDefault;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        int offset = 0;
        foreach (var line in lines)
        {
            int lineStart = offset;
            int lineEnd = offset + line.Length;

            // Saltar líneas vacías
            if (string.IsNullOrWhiteSpace(line))
            {
                offset += line.Length + 1; // +1 por el salto de línea
                continue;
            }

            // Detectar secciones (terminan en ':')
            if (line.TrimEnd().EndsWith(":"))
            {
                var range = editor.Document.GetRange(lineStart, lineEnd);
                range.CharacterFormat.ForegroundColor = IsCurrentDark ? DarkColorSection : LightColorSection;
                range.CharacterFormat.Bold = FormatEffect.On;
            }

            // Palabras clave y directivas
            HighlightKeywords(editor, line, lineStart, lineEnd);

            // Registros (GprN, Frf, Ipr, Stack, Cpu, Port, Bit, Byte)
            HighlightRegisters(editor, line, lineStart, lineEnd);

            // Números (hex, decimal, binario)
            HighlightNumbers(editor, line, lineStart, lineEnd);

            // Cadenas
            HighlightStrings(editor, line, lineStart, lineEnd);

            // Comentarios (// y /* */)
            HighlightComments(editor, line, lineStart, lineEnd);

            // Operadores y puntuación
            HighlightOperators(editor, line, lineStart, lineEnd);

            offset += line.Length + 1; // +1 por el salto de línea
        }
    }

    private static void HighlightKeywords(RichEditBox editor, string line, int lineStart, int lineEnd)
    {
        var keywords = new[]
        {
            "routine", "returns", "run", "data", "execute", "initialize",
            "define", "copy", "add", "sub", "mul", "div", "and", "or", "xor", "not",
            "cmp", "test", "jmp", "je", "jne", "jz", "jnz", "jg", "jl", "jge", "jle",
            "call", "ret", "push", "pop", "mov", "lea", "inc", "dec", "neg",
            "shl", "shr", "sal", "sar", "rol", "ror", "rcl", "rcr",
            "if", "else", "endif", "while", "endwhile", "for", "endfor",
            "break", "continue", "return", "goto", "label",
            "struct", "union", "enum", "typedef", "sizeof", "align",
            "extern", "public", "private", "static", "const", "volatile",
            "true", "false", "null", "this", "base", "new", "delete"
        };

        var pattern = @"\b(" + string.Join("|", keywords.Select(Regex.Escape)) + @")\b";
        var matches = Regex.Matches(line, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var range = editor.Document.GetRange(lineStart + match.Index, lineStart + match.Index + match.Length);
            range.CharacterFormat.ForegroundColor = IsCurrentDark ? DarkColorKeyword : LightColorKeyword;
            range.CharacterFormat.Bold = FormatEffect.On;
        }
    }

    private static void HighlightRegisters(RichEditBox editor, string line, int lineStart, int lineEnd)
    {
        // Gpr0-Gpr15, Frf, Ipr, Stack, Cpu, Port, Bit, Byte, Bit:N, Byte:N
        var pattern = @"\b(Gpr(?:[0-9]|1[0-5])(?:\.low(?:8|16|32)|\.high8)?|Frf(?:\.(?:carry|zero|sign|overflow|parity|adjust|interrupt|direction))?|Ipr(?:\.(?:flowJump|flowCall|flowStackJump|current))?|Stack(?:\.(?:push|pop|reserve|release))?|Cpu(?:\.(?:noOperation|pause|halt))?|Port(?:\[[^\]]+\])?|Bit(?::[0-9]+)?|Byte(?::[0-9]+)?)\b";
        var matches = Regex.Matches(line, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var range = editor.Document.GetRange(lineStart + match.Index, lineStart + match.Index + match.Length);
            range.CharacterFormat.ForegroundColor = IsCurrentDark ? DarkColorRegister : LightColorRegister;
        }
    }

    private static void HighlightNumbers(RichEditBox editor, string line, int lineStart, int lineEnd)
    {
        // Hex: 0x..., decimal: \d+, binary: 0b...
        var pattern = @"\b(0x[0-9A-Fa-f]+|0b[01]+|\d+)\b";
        var matches = Regex.Matches(line, pattern);

        foreach (Match match in matches)
        {
            var range = editor.Document.GetRange(lineStart + match.Index, lineStart + match.Index + match.Length);
            range.CharacterFormat.ForegroundColor = IsCurrentDark ? DarkColorNumber : LightColorNumber;
        }
    }

    private static void HighlightStrings(RichEditBox editor, string line, int lineStart, int lineEnd)
    {
        // "..." y '...'
        var pattern = @"(""[^""]*""|'[^']*')";
        var matches = Regex.Matches(line, pattern);

        foreach (Match match in matches)
        {
            var range = editor.Document.GetRange(lineStart + match.Index, lineStart + match.Index + match.Length);
            range.CharacterFormat.ForegroundColor = IsCurrentDark ? DarkColorString : LightColorString;
        }
    }

    private static void HighlightComments(RichEditBox editor, string line, int lineStart, int lineEnd)
    {
        // Comentarios de línea //
        int commentIdx = line.IndexOf("//");
        if (commentIdx >= 0)
        {
            var range = editor.Document.GetRange(lineStart + commentIdx, lineEnd);
            range.CharacterFormat.ForegroundColor = IsCurrentDark ? DarkColorComment : LightColorComment;
            range.CharacterFormat.Italic = FormatEffect.On;
        }
    }

    private static void HighlightOperators(RichEditBox editor, string line, int lineStart, int lineEnd)
    {
        var operators = new[] { "->", "=>", "==", "!=", "<=", ">=", "<", ">", "+", "-", "*", "/", "%", "&", "|", "^", "~", "!", "=", ",", ";", ":", ".", "(", ")", "[", "]", "{", "}" };
        foreach (var op in operators)
        {
            int idx = 0;
            while ((idx = line.IndexOf(op, idx, StringComparison.Ordinal)) >= 0)
            {
                var range = editor.Document.GetRange(lineStart + idx, lineStart + idx + op.Length);
                range.CharacterFormat.ForegroundColor = IsCurrentDark ? DarkColorOperator : LightColorOperator;
                idx += op.Length;
            }
        }
    }
}