namespace FluentASM.Lsp.Server;

/// <summary>
/// Índice de líneas de un documento LSP. Traduce entre offsets absolutos base 0
/// y posiciones <c>(línea, carácter)</c> base 0 sin volver a recorrer el texto.
/// </summary>
internal sealed class DocumentLines
{
    private readonly string text;
    private readonly int[] lineStarts;

    public DocumentLines(string text)
    {
        this.text = text ?? string.Empty;

        var starts = new List<int> { 0 };
        for (int i = 0; i < this.text.Length; i++)
        {
            char current = this.text[i];
            if (current == '\r')
            {
                if (i + 1 < this.text.Length && this.text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
            else if (current == '\n')
            {
                starts.Add(i + 1);
            }
        }

        lineStarts = starts.ToArray();
    }

    /// <summary>Número de líneas del documento (siempre al menos una).</summary>
    public int LineCount => lineStarts.Length;

    /// <summary>Offset absoluto del primer carácter de la línea indicada.</summary>
    public int GetLineStart(int line)
    {
        int index = Math.Clamp(line, 0, lineStarts.Length - 1);
        return lineStarts[index];
    }

    /// <summary>Offset absoluto del primer carácter posterior al contenido de la línea.</summary>
    public int GetLineEnd(int line)
    {
        int index = Math.Clamp(line, 0, lineStarts.Length - 1);
        int end = index + 1 < lineStarts.Length ? lineStarts[index + 1] : text.Length;

        // El salto de línea pertenece al separador, no al contenido de la línea.
        if (end > lineStarts[index] && text[end - 1] == '\n')
        {
            end--;
        }

        if (end > lineStarts[index] && text[end - 1] == '\r')
        {
            end--;
        }

        return end;
    }

    /// <summary>Convierte un offset absoluto en una posición LSP base 0.</summary>
    public Protocol.Position ToPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);

        int index = Array.BinarySearch(lineStarts, offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        index = Math.Clamp(index, 0, lineStarts.Length - 1);
        return new Protocol.Position(index, offset - lineStarts[index]);
    }

    /// <summary>Convierte una posición LSP base 0 en un offset absoluto.</summary>
    public int ToOffset(Protocol.Position position)
    {
        int line = Math.Clamp(position.Line, 0, lineStarts.Length - 1);
        int start = lineStarts[line];
        int end = GetLineEnd(line);
        return Math.Clamp(start + Math.Max(0, position.Character), start, end);
    }
}


