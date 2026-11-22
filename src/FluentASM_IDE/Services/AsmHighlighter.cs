using System;
using System.Collections.Generic;
using FluentASM.Lsp.Protocol;
using FluentASM_IDE.Common.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace FluentASM_IDE.Services;

/// <summary>
/// Resaltador de sintaxis para FluentASM en <see cref="RichEditBox"/>.
/// No conoce el lenguaje ni usa expresiones regulares: solo aplica los spans
/// clasificados que entrega <c>ILanguageService.GetHighlightTokens</c>.
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
    /// Aplica el resaltado de la versión analizada. Los offsets de los tokens
    /// son absolutos sobre el texto normalizado; se traducen a offsets del
    /// <c>RichEditBox</c> con un único recorrido.
    /// </summary>
    /// <param name="editor">Editor destino.</param>
    /// <param name="tokens">Tokens clasificados por el LSP.</param>
    /// <param name="normalizedText">Texto normalizado al que pertenecen los tokens.</param>
    public static void Apply(RichEditBox editor, IReadOnlyList<HighlightToken> tokens, string normalizedText)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(normalizedText);

        editor.Document.GetText(TextGetOptions.None, out string editorText);
        if (string.IsNullOrEmpty(editorText))
        {
            return;
        }

        // Los spans solo son válidos si el texto no cambió desde el análisis.
        if (!string.Equals(DocumentText.Normalize(editorText), normalizedText, StringComparison.Ordinal))
        {
            return;
        }

        int[] map = DocumentText.BuildNormalizedToEditorMap(editorText);
        int editorLength = editorText.Length;

        var document = editor.Document;
        int selectionStart = document.Selection.StartPosition;
        int selectionEnd = document.Selection.EndPosition;

        document.BatchDisplayUpdates();
        try
        {
            var all = document.GetRange(0, editorLength).CharacterFormat;
            all.ForegroundColor = ColorDefault;
            all.Bold = FormatEffect.Off;
            all.Italic = FormatEffect.Off;

            foreach (var token in tokens)
            {
                int start = MapOffset(map, token.Start);
                int end = MapOffset(map, token.Start + token.Length);
                if (end <= start)
                {
                    continue;
                }

                var range = document.GetRange(start, Math.Min(end, editorLength));
                var format = range.CharacterFormat;
                format.ForegroundColor = GetColor(token.Kind);

                if (token.Kind == HighlightTokenKind.Comment)
                {
                    format.Italic = FormatEffect.On;
                }

                if (token.Kind is HighlightTokenKind.Keyword
                    or HighlightTokenKind.FlowConstructor
                    or HighlightTokenKind.Label)
                {
                    format.Bold = FormatEffect.On;
                }
            }
        }
        finally
        {
            document.Selection.SetRange(selectionStart, selectionEnd);
            document.ApplyDisplayUpdates();
        }
    }

    /// <summary>Color del tema actual para una categoría de token.</summary>
    public static Color GetColor(HighlightTokenKind kind)
    {
        bool dark = IsCurrentDark;

        return kind switch
        {
            HighlightTokenKind.Keyword => dark ? DarkColorKeyword : LightColorKeyword,
            HighlightTokenKind.Verb => dark ? DarkColorKeyword : LightColorKeyword,
            HighlightTokenKind.Routine => dark ? DarkColorType : LightColorType,
            HighlightTokenKind.FlowConstructor => dark ? DarkColorSection : LightColorSection,
            HighlightTokenKind.Directive => dark ? DarkColorDirective : LightColorDirective,
            HighlightTokenKind.Register => dark ? DarkColorRegister : LightColorRegister,
            HighlightTokenKind.Flag => dark ? DarkColorRegister : LightColorRegister,
            HighlightTokenKind.Magnitude => dark ? DarkColorType : LightColorType,
            HighlightTokenKind.Data => dark ? DarkColorType : LightColorType,
            HighlightTokenKind.Number => dark ? DarkColorNumber : LightColorNumber,
            HighlightTokenKind.String => dark ? DarkColorString : LightColorString,
            HighlightTokenKind.Comment => dark ? DarkColorComment : LightColorComment,
            HighlightTokenKind.Label => dark ? DarkColorSection : LightColorSection,
            HighlightTokenKind.Operator => dark ? DarkColorOperator : LightColorOperator,
            HighlightTokenKind.Punctuation => dark ? DarkColorOperator : LightColorOperator,
            _ => ColorDefault,
        };
    }

    private static int MapOffset(int[] map, int normalizedOffset)
    {
        if (map.Length == 0)
        {
            return 0;
        }

        int index = Math.Clamp(normalizedOffset, 0, map.Length - 1);
        return map[index];
    }
}
