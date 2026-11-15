using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Windows.UI;

namespace FluentASM_IDE.Services;

public static class AsmHighlighter
{
    // Color Palette from Stitch FluentASM Minimalist IDE
    public static readonly Color ColorKeyword = Color.FromArgb(255, 255, 212, 190);    // #FFD4BE (Keywords: program, routine, end, parameter)
    public static readonly Color ColorSection = Color.FromArgb(255, 255, 219, 201);    // #FFDBC9 (Section: .execute, .rodata)
    public static readonly Color ColorMethod = Color.FromArgb(255, 96, 205, 255);      // #60CDFF (Fluent methods: .copy, .imult, .flowCall)
    public static readonly Color ColorRegister = Color.FromArgb(255, 129, 207, 255);   // #81CFFF (Registers: Gpr0, Gpr1, rFlags)
    public static readonly Color ColorType = Color.FromArgb(255, 0, 157, 216);         // #009DD8 (Types: Bit, Byte)
    public static readonly Color ColorLiteral = Color.FromArgb(255, 180, 228, 255);    // #B4E4FF (Literals: numbers, <2,0>)
    public static readonly Color ColorString = Color.FromArgb(255, 255, 219, 201);     // #FFDBC9 (Strings: "...")
    public static readonly Color ColorComment = Color.FromArgb(255, 136, 146, 153);    // #889299 (Comments: //, ;)
    public static readonly Color ColorLabel = Color.FromArgb(255, 96, 205, 255);       // #60CDFF (Labels: @loop:, @loop)
    public static readonly Color ColorDefault = Color.FromArgb(255, 229, 226, 225);    // #E5E2E1 (Default on-surface)

    private static readonly HashSet<string> StructuralKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "program", "section", "entry", "routine", "parameter", "end",
        "if", "not", "and", "or", "to", "as", "is"
    };

    private static readonly HashSet<string> NativeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bit", "Byte", "Word", "DWord", "QWord"
    };

    private static readonly Regex TokenRegex = new(
        @"(?<comment>(?://|;).*$)|" +
        @"(?<string>""(?:[^""\\]|\\.)*"")|" +
        @"(?<labelDef>@[a-zA-Z0-9_]+:)|" +
        @"(?<labelRef>@[a-zA-Z0-9_]+)|" +
        @"(?<generic><\d+(?:,\d+)*>)|" +
        @"(?<method>\.(?:flowJump|flowCall|flowStackJump|flowLoop|copy|imult|mult|sub|add|cmp|test|and|or|xor|shl|shr|sar|div|mod|execute|rodata|data|bss)\b)|" +
        @"(?<register>\b(?:Gpr\d+|rFlags|rip|Frf|Crf\d+|Srf\d+)(?:\.(?:low32|low16|low8|high8|zero|carry|sign|overflow|parity))?\b)|" +
        @"(?<hex>0x[0-9a-fA-F]+)|" +
        @"(?<num>\b\d+\b)|" +
        @"(?<word>[a-zA-Z_][\w]*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static void Highlight(RichEditTextDocument document)
    {
        if (document == null) return;

        document.GetText(TextGetOptions.None, out string text);
        if (string.IsNullOrEmpty(text)) return;

        int selStart = document.Selection.StartPosition;
        int selEnd = document.Selection.EndPosition;

        document.BatchDisplayUpdates();
        try
        {
            // Reset entire range to default
            var allRange = document.GetRange(0, text.Length);
            allRange.CharacterFormat.ForegroundColor = ColorDefault;
            allRange.CharacterFormat.Bold = FormatEffect.Off;
            allRange.CharacterFormat.Italic = FormatEffect.Off;

            var matches = TokenRegex.Matches(text);
            foreach (Match match in matches)
            {
                int start = match.Index;
                int end = match.Index + match.Length;
                var range = document.GetRange(start, end);

                if (match.Groups["comment"].Success)
                {
                    range.CharacterFormat.ForegroundColor = ColorComment;
                    range.CharacterFormat.Italic = FormatEffect.On;
                }
                else if (match.Groups["string"].Success)
                {
                    range.CharacterFormat.ForegroundColor = ColorString;
                }
                else if (match.Groups["labelDef"].Success)
                {
                    range.CharacterFormat.ForegroundColor = ColorLabel;
                    range.CharacterFormat.Bold = FormatEffect.On;
                }
                else if (match.Groups["labelRef"].Success)
                {
                    range.CharacterFormat.ForegroundColor = ColorLiteral;
                    range.CharacterFormat.Bold = FormatEffect.On;
                }
                else if (match.Groups["method"].Success)
                {
                    string m = match.Value;
                    if (m.StartsWith(".flow"))
                    {
                        range.CharacterFormat.ForegroundColor = ColorMethod;
                        range.CharacterFormat.Bold = FormatEffect.On;
                    }
                    else if (m == ".execute" || m == ".rodata" || m == ".data" || m == ".bss")
                    {
                        range.CharacterFormat.ForegroundColor = ColorSection;
                    }
                    else
                    {
                        range.CharacterFormat.ForegroundColor = ColorMethod;
                    }
                }
                else if (match.Groups["register"].Success)
                {
                    range.CharacterFormat.ForegroundColor = ColorRegister;
                }
                else if (match.Groups["generic"].Success || match.Groups["hex"].Success || match.Groups["num"].Success)
                {
                    range.CharacterFormat.ForegroundColor = ColorLiteral;
                }
                else if (match.Groups["word"].Success)
                {
                    string word = match.Value;
                    if (StructuralKeywords.Contains(word))
                    {
                        range.CharacterFormat.ForegroundColor = ColorKeyword;
                        if (word == "program" || word == "routine" || word == "end")
                        {
                            range.CharacterFormat.Bold = FormatEffect.On;
                        }
                    }
                    else if (NativeTypes.Contains(word))
                    {
                        range.CharacterFormat.ForegroundColor = ColorType;
                        range.CharacterFormat.Bold = FormatEffect.On;
                    }
                }
            }
        }
        finally
        {
            document.Selection.SetRange(selStart, selEnd);
            document.ApplyDisplayUpdates();
        }
    }
}
