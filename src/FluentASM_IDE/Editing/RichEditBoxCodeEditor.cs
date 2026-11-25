using FluentASM_IDE.Common.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace FluentASM_IDE.Editing;

/// <summary>
/// Implementación de <see cref="ICodeEditor"/> sobre el <see cref="RichEditBox"/>
/// del IDE. Traduce offsets normalizados a párrafos nativos y viceversa con
/// <see cref="DocumentText"/>, la única normalización permitida.
/// </summary>
/// <remarks>
/// Los reemplazos se hacen con <c>ITextRange.SetText</c> sobre rangos mínimos,
/// nunca con <c>SetText</c> del documento completo, para conservar el scroll y
/// el historial propio de deshacer del IDE.
/// </remarks>
internal sealed class RichEditBoxCodeEditor : ICodeEditor
{
    private readonly RichEditBox editor;
    private int applyDepth;

    public RichEditBoxCodeEditor(RichEditBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        this.editor = editor;
    }

    /// <inheritdoc />
    public string IndentUnit { get; set; } = "    ";

    /// <inheritdoc />
    public bool IsApplying => applyDepth > 0;

    /// <inheritdoc />
    public string Text => DocumentText.Normalize(ReadEditorText());

    /// <inheritdoc />
    public int CaretOffset => ToNormalized(editor.Document.Selection.StartPosition);

    /// <inheritdoc />
    public int SelectionStart
    {
        get
        {
            string editorText = ReadEditorText();
            string normalized = DocumentText.Normalize(editorText);
            int start = DocumentText.ToNormalizedOffset(editorText, editor.Document.Selection.StartPosition);
            return Math.Clamp(start, 0, normalized.Length);
        }
    }

    /// <inheritdoc />
    public int SelectionLength
    {
        get
        {
            string editorText = ReadEditorText();
            string normalized = DocumentText.Normalize(editorText);
            int start = DocumentText.ToNormalizedOffset(editorText, editor.Document.Selection.StartPosition);
            int end = DocumentText.ToNormalizedOffset(editorText, editor.Document.Selection.EndPosition);
            return Math.Clamp(end - start, 0, normalized.Length - Math.Clamp(start, 0, normalized.Length));
        }
    }

    /// <inheritdoc />
    public void Replace(int start, int length, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string editorText = ReadEditorText();
        string normalized = DocumentText.Normalize(editorText);
        start = Math.Clamp(start, 0, normalized.Length);
        length = Math.Clamp(length, 0, normalized.Length - start);

        int[] map = DocumentText.BuildNormalizedToEditorMap(editorText);
        int editorStart = map[start];
        int editorEnd = map[start + length];

        editor.Document
            .GetRange(editorStart, editorEnd)
            .SetText(TextSetOptions.None, text.Replace("\n", "\r", StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public void SetCaret(int offset)
    {
        string editorText = ReadEditorText();
        string normalized = DocumentText.Normalize(editorText);
        int[] map = DocumentText.BuildNormalizedToEditorMap(editorText);
        int caret = map[Math.Clamp(offset, 0, normalized.Length)];
        editor.Document.Selection.SetRange(caret, caret);
    }

    /// <inheritdoc />
    public void BeginApply() => applyDepth++;

    /// <inheritdoc />
    public void EndApply() => applyDepth = Math.Max(0, applyDepth - 1);

    /// <inheritdoc />
    public bool IsInsideStringOrComment(int offset) =>
        !FasmTextContext.GetContext(Text, offset).IsCode;

    /// <inheritdoc />
    public FasmSectionKind GetSection(int offset) => FasmTextContext.GetSection(Text, offset);

    private string ReadEditorText()
    {
        editor.Document.GetText(TextGetOptions.None, out string text);
        return text ?? string.Empty;
    }

    private int ToNormalized(int editorOffset)
    {
        string editorText = ReadEditorText();
        string normalized = DocumentText.Normalize(editorText);
        return Math.Clamp(
            DocumentText.ToNormalizedOffset(editorText, editorOffset),
            0,
            normalized.Length);
    }
}
