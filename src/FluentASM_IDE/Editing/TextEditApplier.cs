using FluentASM.Lsp.Protocol;
using FluentASM_IDE.Common.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace FluentASM_IDE.Editing;

/// <summary>Resultado de aplicar un lote de ediciones de formateo.</summary>
internal enum TextEditApplyStatus
{
    /// <summary>El lote se aplicó correctamente.</summary>
    Applied,

    /// <summary>No había ediciones que aplicar.</summary>
    NoChanges,

    /// <summary>El documento cambió desde que se calcularon las ediciones.</summary>
    Stale,

    /// <summary>El lote no se pudo aplicar; el texto queda intacto.</summary>
    Failed,
}

/// <summary>
/// Aplica un lote de <see cref="TextEdit"/> de la Capa B sobre el
/// <see cref="RichEditBox"/> respetando los párrafos nativos <c>\r</c>, el
/// caret y el scroll.
/// </summary>
/// <remarks>
/// Las ediciones se validan contra el texto exacto sobre el que se calcularon;
/// si el documento cambió entre la petición y la aplicación se descarta el lote
/// completo. Nunca se usa <c>SetText</c> del documento: los rangos son mínimos.
/// </remarks>
internal static class TextEditApplier
{
    /// <summary>
    /// Aplica <paramref name="edits"/> sobre <paramref name="snapshotText"/> y
    /// devuelve el caret resultante en offsets normalizados.
    /// </summary>
    /// <param name="editor">Editor nativo.</param>
    /// <param name="snapshotText">Texto normalizado sobre el que se calcularon las ediciones.</param>
    /// <param name="edits">Ediciones ordenadas o desordenadas; deben ser disjuntas.</param>
    /// <param name="caret">Caret actual en offsets normalizados.</param>
    /// <param name="newCaret">Caret resultante en offsets normalizados.</param>
    /// <param name="error">Motivo del descarte, si lo hubo.</param>
    public static TextEditApplyStatus TryApply(
        RichEditBox editor,
        string snapshotText,
        IReadOnlyList<TextEdit> edits,
        int caret,
        out int newCaret,
        out string? error)
    {
        newCaret = Math.Clamp(caret, 0, snapshotText?.Length ?? 0);
        error = null;

        if (editor is null)
        {
            error = "Editor nulo.";
            return TextEditApplyStatus.Failed;
        }

        if (snapshotText is null || edits is null)
        {
            error = "Lote de ediciones nulo.";
            return TextEditApplyStatus.Failed;
        }

        try
        {
            editor.Document.GetText(TextGetOptions.None, out string editorText);
            string current = DocumentText.Normalize(editorText);
            if (!string.Equals(current, snapshotText, StringComparison.Ordinal))
            {
                error = "El documento cambió desde que se calculó el formateo.";
                return TextEditApplyStatus.Stale;
            }

            if (edits.Count == 0)
            {
                return TextEditApplyStatus.NoChanges;
            }

            var resolved = new List<(int Start, int End, string Text)>(edits.Count);
            foreach (TextEdit edit in edits)
            {
                if (!TryGetOffset(snapshotText, edit.Range.Start, out int start) ||
                    !TryGetOffset(snapshotText, edit.Range.End, out int end) ||
                    end < start)
                {
                    error = "Rango de edición inválido.";
                    return TextEditApplyStatus.Failed;
                }

                resolved.Add((start, end, edit.NewText ?? string.Empty));
            }

            resolved.Sort((left, right) => left.Start.CompareTo(right.Start));
            for (int index = 1; index < resolved.Count; index++)
            {
                if (resolved[index].Start < resolved[index - 1].End)
                {
                    error = "Ediciones solapadas: se descarta el lote completo.";
                    return TextEditApplyStatus.Failed;
                }
            }

            newCaret = AdjustCaret(resolved, Math.Clamp(caret, 0, snapshotText.Length));

            // De mayor a menor offset para no invalidar las posiciones previas.
            for (int index = resolved.Count - 1; index >= 0; index--)
            {
                (int start, int end, string text) = resolved[index];

                editor.Document.GetText(TextGetOptions.None, out string liveText);
                int[] map = DocumentText.BuildNormalizedToEditorMap(liveText);
                int editorStart = map[Math.Clamp(start, 0, map.Length - 1)];
                int editorEnd = map[Math.Clamp(end, 0, map.Length - 1)];

                editor.Document
                    .GetRange(editorStart, editorEnd)
                    .SetText(TextSetOptions.None, text.Replace("\n", "\r", StringComparison.Ordinal));
            }

            editor.Document.GetText(TextGetOptions.None, out string updatedText);
            string updated = DocumentText.Normalize(updatedText);
            int[] updatedMap = DocumentText.BuildNormalizedToEditorMap(updatedText);
            int editorCaret = updatedMap[Math.Clamp(newCaret, 0, updated.Length)];
            editor.Document.Selection.SetRange(editorCaret, editorCaret);
            return TextEditApplyStatus.Applied;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return TextEditApplyStatus.Failed;
        }
    }

    private static int AdjustCaret(List<(int Start, int End, string Text)> ascending, int caret)
    {
        int adjusted = caret;
        foreach ((int start, int end, string text) in ascending)
        {
            int delta = text.Length - (end - start);
            if (adjusted >= end)
            {
                adjusted += delta;
            }
            else if (adjusted > start)
            {
                adjusted = start + text.Length;
            }
        }

        return Math.Max(0, adjusted);
    }

    private static bool TryGetOffset(string text, Position position, out int offset) =>
        DocumentText.TryGetNormalizedOffset(text, position.Line, position.Character, out offset);
}
