namespace FluentASM_IDE.Editing;

/// <summary>
/// <c>Enter</c> contextual de la Capa A: expansión de bloques vacíos, cadena
/// <em>type-state</em>, etiquetas, comentarios de bloque y continuación con la
/// indentación efectiva del documento.
/// </summary>
/// <remarks>
/// Es el único responsable de los saltos de línea: la Capa B (formateo) nunca
/// procesa <c>\n</c>. La indentación base se recibe resuelta en
/// <see cref="ICodeEditor.IndentUnit"/>.
/// </remarks>
internal static class EnterAssist
{
    /// <summary>
    /// Inserta el salto de línea y la indentación adecuados. Devuelve
    /// <c>true</c> cuando consumió la pulsación.
    /// </summary>
    public static bool TryHandle(ICodeEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (editor.IsApplying)
        {
            return false;
        }

        string text = editor.Text;
        int caret = Math.Clamp(editor.CaretOffset, 0, text.Length);

        if (editor.SelectionLength > 0)
        {
            // Un Enter sobre una selección la reemplaza, como el editor nativo.
            int start = Math.Clamp(editor.SelectionStart, 0, text.Length);
            editor.Replace(start, editor.SelectionLength, string.Empty);
            text = editor.Text;
            caret = Math.Clamp(start, 0, text.Length);
        }

        string unit = string.IsNullOrEmpty(editor.IndentUnit) ? "    " : editor.IndentUnit;
        int lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        int lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        string linePrefix = text[lineStart..Math.Min(caret, lineEnd)];
        string lineIndent = ReadIndent(linePrefix);
        LexicalContext context = FasmTextContext.GetContext(text, caret);

        if (context.InsideBlockComment)
        {
            string continuation = lineIndent + " * ";
            Insert(editor, caret, "\n" + continuation, caret + 1 + continuation.Length);
            return true;
        }

        if (context.InsideLineComment || context.InsideString)
        {
            // Sin expansión sintáctica dentro de comentarios de línea ni cadenas.
            Insert(editor, caret, "\n" + lineIndent, caret + 1 + lineIndent.Length);
            return true;
        }

        if (TryExpandEmptyBlock(editor, text, caret, lineIndent, unit))
        {
            return true;
        }

        int level = ComputeNextLevel(text, caret, lineStart, lineIndent, unit);
        string indent = Repeat(unit, level);
        Insert(editor, caret, "\n" + indent, caret + 1 + indent.Length);
        return true;
    }

    /// <summary>
    /// Si el caret está entre <c>{</c> y <c>}</c> separados solo por espacios,
    /// expande a tres líneas con el contenido un nivel dentro.
    /// </summary>
    private static bool TryExpandEmptyBlock(
        ICodeEditor editor,
        string text,
        int caret,
        string lineIndent,
        string unit)
    {
        int openIndex = caret - 1;
        while (openIndex >= 0 && (text[openIndex] == ' ' || text[openIndex] == '\t'))
        {
            openIndex--;
        }

        int closeIndex = caret;
        while (closeIndex < text.Length && (text[closeIndex] == ' ' || text[closeIndex] == '\t'))
        {
            closeIndex++;
        }

        if (openIndex < 0 || text[openIndex] != '{' ||
            closeIndex >= text.Length || text[closeIndex] != '}')
        {
            return false;
        }

        string inner = lineIndent + unit;
        string replacement = "\n" + inner + "\n" + lineIndent;
        editor.Replace(caret, closeIndex - caret, replacement);
        editor.SetCaret(caret + 1 + inner.Length);
        return true;
    }

    /// <summary>
    /// Nivel de la nueva línea. Se deduce de la indentación real de la línea
    /// actual (no de un contador global, para respetar la cadena type-state) y
    /// del bloque abierto en el caret.
    /// </summary>
    private static int ComputeNextLevel(
        string text,
        int caret,
        int lineStart,
        string lineIndent,
        string unit)
    {
        int lineLevel = IndentLevel(lineIndent, unit);
        string before = text[lineStart..caret];
        string trimmedEnd = before.TrimEnd();
        string trimmedStart = before.TrimStart();

        bool isChainOrComment =
            trimmedStart.StartsWith('.') ||
            trimmedStart.StartsWith("//", StringComparison.Ordinal) ||
            trimmedStart.StartsWith("/*", StringComparison.Ordinal);

        int depth = FasmTextContext.GetBraceDepth(text, caret);
        if (depth == 0 && trimmedEnd.Length > 0 && !isChainOrComment)
        {
            // Nivel raíz type-state: tras el nombre del proyecto (o tras `})`)
            // el siguiente encadenamiento vive un nivel dentro.
            return 1;
        }

        if (trimmedEnd.EndsWith('{'))
        {
            return lineLevel + 1;
        }

        return lineLevel;
    }

    private static void Insert(ICodeEditor editor, int caret, string text, int caretOffset)
    {
        editor.Replace(caret, 0, text);
        editor.SetCaret(caretOffset);
    }

    private static string ReadIndent(string line)
    {
        int length = 0;
        while (length < line.Length && (line[length] == ' ' || line[length] == '\t'))
        {
            length++;
        }

        return line[..length];
    }

    private static int IndentLevel(string indent, string unit)
    {
        if (indent.Length == 0 || unit.Length == 0)
        {
            return 0;
        }

        int level = 0;
        int index = 0;
        while (index + unit.Length <= indent.Length &&
               string.CompareOrdinal(indent, index, unit, 0, unit.Length) == 0)
        {
            level++;
            index += unit.Length;
        }

        return level;
    }

    private static string Repeat(string unit, int level) =>
        level <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(unit, level));
}
