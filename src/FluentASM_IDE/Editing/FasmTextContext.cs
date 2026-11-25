namespace FluentASM_IDE.Editing;

/// <summary>
/// Sección del flujo <em>type-state</em> a la que pertenece una posición.
/// </summary>
internal enum FasmSectionKind
{
    /// <summary>Nivel raíz: nombre del proyecto y encadenamientos.</summary>
    Root,

    /// <summary>Dentro de <c>.data(...)</c>.</summary>
    Data,

    /// <summary>Dentro de <c>.execute(...)</c>.</summary>
    Execute,

    /// <summary>La posición no se pudo clasificar con certeza.</summary>
    Unknown,
}

/// <summary>
/// Estado léxico de una posición del documento.
/// </summary>
/// <param name="InsideString">Dentro de una cadena <c>"..."</c>.</param>
/// <param name="InsideLineComment">Dentro de un comentario <c>// ...</c>.</param>
/// <param name="InsideBlockComment">Dentro de un comentario <c>/* ... */</c>.</param>
internal readonly record struct LexicalContext(
    bool InsideString,
    bool InsideLineComment,
    bool InsideBlockComment)
{
    /// <summary>Indica si la posición está en código (fuera de cadenas y comentarios).</summary>
    public bool IsCode => !InsideString && !InsideLineComment && !InsideBlockComment;

    /// <summary>Indica si la posición está dentro de cualquier comentario.</summary>
    public bool IsComment => InsideLineComment || InsideBlockComment;
}

/// <summary>
/// Escáner léxico mínimo y síncrono para la Capa A del editor. No depende del
/// LSP ni de WinUI: permite decidir autocierres y <c>Enter</c> con latencia cero
/// incluso cuando el análisis todavía no se ha ejecutado.
/// </summary>
/// <remarks>
/// El escáner es deliberadamente conservador: ante una cadena sin cerrar
/// reinicia el estado en el salto de línea para no contaminar el resto del
/// documento. Las decisiones ambiguas las resuelve el consumidor no actuando.
/// </remarks>
internal static class FasmTextContext
{
    /// <summary>Devuelve el estado léxico de <paramref name="offset"/>.</summary>
    public static LexicalContext GetContext(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);

        int limit = Math.Clamp(offset, 0, text.Length);
        bool inString = false;
        bool inBlock = false;

        for (int index = 0; index < limit; index++)
        {
            char current = text[index];

            if (inBlock)
            {
                if (current == '*' && index + 1 < limit && text[index + 1] == '/')
                {
                    inBlock = false;
                    index++;
                }

                continue;
            }

            if (inString)
            {
                if (current == '\\' && index + 1 < limit)
                {
                    index++;
                }
                else if (current == '"' || current == '\n')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '/' && index + 1 < limit)
            {
                char next = text[index + 1];
                if (next == '/')
                {
                    // Todo el resto de la línea es comentario.
                    return new LexicalContext(false, true, false);
                }

                if (next == '*')
                {
                    inBlock = true;
                    index++;
                }
            }
        }

        return new LexicalContext(inString, false, inBlock);
    }

    /// <summary>Profundidad de llaves <c>{ }</c> en <paramref name="offset"/>.</summary>
    public static int GetBraceDepth(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);

        int limit = Math.Clamp(offset, 0, text.Length);
        int depth = 0;
        bool inString = false;
        bool inBlock = false;
        bool inLine = false;

        for (int index = 0; index < limit; index++)
        {
            char current = text[index];

            if (inLine)
            {
                if (current == '\n')
                {
                    inLine = false;
                }

                continue;
            }

            if (inBlock)
            {
                if (current == '*' && index + 1 < limit && text[index + 1] == '/')
                {
                    inBlock = false;
                    index++;
                }

                continue;
            }

            if (inString)
            {
                if (current == '\\' && index + 1 < limit)
                {
                    index++;
                }
                else if (current == '"' || current == '\n')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '/' && index + 1 < limit)
            {
                if (text[index + 1] == '/')
                {
                    inLine = true;
                    index++;
                    continue;
                }

                if (text[index + 1] == '*')
                {
                    inBlock = true;
                    index++;
                    continue;
                }
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth = Math.Max(0, depth - 1);
            }
        }

        return depth;
    }

    /// <summary>
    /// Sección efectiva en <paramref name="offset"/>. Se apoya en los encadenamientos
    /// <c>.data(</c> / <c>.execute(</c> y en las llaves de los bloques.
    /// </summary>
    /// <remarks>
    /// La pila mezcla paréntesis y llaves: una llamada <c>.data(...)</c> abre la
    /// sección <c>Data</c> hasta su <c>)</c> de cierre, y las llaves internas de
    /// la lambda conservan esa sección. Con código mal formado la pila se
    /// desborda con seguridad y el resultado degrada a <see cref="FasmSectionKind.Root"/>.
    /// </remarks>
    public static FasmSectionKind GetSection(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);

        int limit = Math.Clamp(offset, 0, text.Length);
        var stack = new Stack<FasmSectionKind>();
        FasmSectionKind current = FasmSectionKind.Root;
        FasmSectionKind pending = FasmSectionKind.Unknown;
        bool inString = false;
        bool inBlock = false;
        bool inLine = false;

        for (int index = 0; index < limit; index++)
        {
            char character = text[index];

            if (inLine)
            {
                if (character == '\n')
                {
                    inLine = false;
                }

                continue;
            }

            if (inBlock)
            {
                if (character == '*' && index + 1 < limit && text[index + 1] == '/')
                {
                    inBlock = false;
                    index++;
                }

                continue;
            }

            if (inString)
            {
                if (character == '\\' && index + 1 < limit)
                {
                    index++;
                }
                else if (character == '"' || character == '\n')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;

                case '/':
                    if (index + 1 < limit)
                    {
                        if (text[index + 1] == '/')
                        {
                            inLine = true;
                            index++;
                        }
                        else if (text[index + 1] == '*')
                        {
                            inBlock = true;
                            index++;
                        }
                    }

                    break;

                case '\n':
                    // Un encadenamiento nunca atraviesa el fin de línea.
                    pending = FasmSectionKind.Unknown;
                    break;

                case '.':
                    if (MatchesSection(text, index, ".data", limit))
                    {
                        pending = FasmSectionKind.Data;
                        index += ".data".Length - 1;
                    }
                    else if (MatchesSection(text, index, ".execute", limit))
                    {
                        pending = FasmSectionKind.Execute;
                        index += ".execute".Length - 1;
                    }

                    break;

                case '(':
                    stack.Push(current);
                    current = pending == FasmSectionKind.Unknown ? current : pending;
                    pending = FasmSectionKind.Unknown;
                    break;

                case ')':
                    current = stack.Count > 0 ? stack.Pop() : FasmSectionKind.Root;
                    break;

                case '{':
                    stack.Push(current);
                    current = pending == FasmSectionKind.Unknown ? current : pending;
                    pending = FasmSectionKind.Unknown;
                    break;

                case '}':
                    current = stack.Count > 0 ? stack.Pop() : FasmSectionKind.Root;
                    break;
            }
        }

        return current;
    }

    private static bool MatchesSection(string text, int start, string candidate, int limit)
    {
        if (!Matches(text, start, candidate, limit))
        {
            return false;
        }

        int next = start + candidate.Length;
        while (next < limit && (text[next] == ' ' || text[next] == '\t'))
        {
            next++;
        }

        return next < limit && text[next] == '(';
    }

    private static bool Matches(string text, int start, string candidate, int limit) =>
        start + candidate.Length <= limit &&
        text.AsSpan(start, candidate.Length).SequenceEqual(candidate);
}
