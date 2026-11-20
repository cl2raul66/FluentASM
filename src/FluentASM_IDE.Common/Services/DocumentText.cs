using System;
using System.Text;

namespace FluentASM_IDE.Common.Services;

/// <summary>
/// Utilidades de texto compartidas por el editor. <c>RichEditBox</c> devuelve los
/// saltos de línea como <c>\r</c> y añade un párrafo vacío al final; el LSP y el
/// editor trabajan con texto normalizado.
/// </summary>
public static class DocumentText
{
    /// <summary>
    /// Normaliza los saltos de línea a <c>\n</c> y descarta el párrafo final que
    /// <c>RichEditBox</c> agrega siempre al documento.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                builder.Append('\n');
            }
            else if (current == '\n')
            {
                builder.Append('\n');
            }
            else
            {
                builder.Append(current);
            }
        }

        if (builder.Length > 0 && builder[^1] == '\n')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Convierte texto normalizado al formato nativo de <c>RichEditBox</c>, que
    /// separa párrafos con <c>\r</c>.
    /// </summary>
    public static string ToEditorText(string? text) => Normalize(text).Replace('\n', '\r');

    /// <summary>Cuenta las líneas visibles de un texto ya normalizado.</summary>
    public static int CountLines(string? normalizedText)
    {
        if (string.IsNullOrEmpty(normalizedText))
        {
            return 1;
        }

        int lines = 1;
        foreach (char current in normalizedText)
        {
            if (current == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    /// <summary>
    /// Convierte una posición base 0 del LSP a un offset absoluto del texto que
    /// devuelve <c>RichEditBox</c> (con saltos <c>\r</c>).
    /// </summary>
    public static int ToEditorOffset(string? editorText, int line, int character)
    {
        if (string.IsNullOrEmpty(editorText))
        {
            return 0;
        }

        int offset = 0;
        int currentLine = 0;

        while (offset < editorText.Length && currentLine < line)
        {
            char current = editorText[offset];
            if (current == '\r' || current == '\n')
            {
                if (current == '\r' && offset + 1 < editorText.Length && editorText[offset + 1] == '\n')
                {
                    offset++;
                }

                currentLine++;
            }

            offset++;
        }

        int currentCharacter = 0;
        while (offset < editorText.Length && currentCharacter < character)
        {
            char current = editorText[offset];
            if (current == '\r' || current == '\n')
            {
                break;
            }

            offset++;
            currentCharacter++;
        }

        return offset;
    }

    /// <summary>
    /// Convierte un offset del texto nativo del editor en el offset equivalente
    /// del texto normalizado que consume el LSP.
    /// </summary>
    public static int ToNormalizedOffset(string? editorText, int offset)
    {
        if (string.IsNullOrEmpty(editorText))
        {
            return 0;
        }

        offset = Math.Clamp(offset, 0, editorText.Length);
        int normalizedOffset = 0;

        for (int i = 0; i < offset; i++)
        {
            char current = editorText[i];
            if (current == '\r')
            {
                if (i + 1 < offset && editorText[i + 1] == '\n')
                {
                    i++;
                }

                normalizedOffset++;
            }
            else if (current == '\n')
            {
                normalizedOffset++;
            }
            else
            {
                normalizedOffset++;
            }
        }

        return normalizedOffset;
    }

    /// <summary>
    /// Construye el mapa de offset normalizado a offset del editor. La entrada
    /// <c>i</c> contiene la posición del carácter normalizado <c>i</c> en el
    /// texto nativo del <c>RichEditBox</c>; la última entrada apunta al final.
    /// </summary>
    public static int[] BuildNormalizedToEditorMap(string? editorText)
    {
        string normalized = Normalize(editorText);
        var map = new int[normalized.Length + 1];
        if (normalized.Length == 0)
        {
            return map;
        }

        int editor = 0;
        int editorLength = editorText?.Length ?? 0;

        for (int index = 0; index < normalized.Length; index++)
        {
            map[index] = editor;

            if (normalized[index] == '\n')
            {
                if (editor < editorLength && editorText![editor] == '\r')
                {
                    editor++;
                }

                if (editor < editorLength && editorText![editor] == '\n')
                {
                    editor++;
                }
            }
            else
            {
                editor++;
            }
        }

        map[normalized.Length] = Math.Min(editor, editorLength);
        return map;
    }

    /// <summary>
    /// Convierte un offset del texto del editor en coordenadas base 0.
    /// </summary>
    public static (int Line, int Character) ToLineCharacter(string? editorText, int offset)
    {
        if (string.IsNullOrEmpty(editorText))
        {
            return (0, 0);
        }

        offset = Math.Clamp(offset, 0, editorText.Length);
        int line = 0;
        int character = 0;

        for (int i = 0; i < offset; i++)
        {
            char current = editorText[i];
            if (current == '\r' || current == '\n')
            {
                if (current == '\r' && i + 1 < offset && editorText[i + 1] == '\n')
                {
                    i++;
                }

                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return (line, character);
    }
}










