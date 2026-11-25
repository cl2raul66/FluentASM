using FluentASM.Core.Catalog;

namespace FluentASM_IDE.Editing;

/// <summary>
/// Cambio mínimo planificado por la Capa A: reemplaza <see cref="Length"/>
/// caracteres en <see cref="Start"/> por <see cref="Text"/> y deja el caret en
/// <see cref="CaretOffset"/>. Todo en offsets del texto normalizado.
/// </summary>
internal readonly record struct AssistEdit(int Start, int Length, string Text, int CaretOffset);

/// <summary>
/// Cierre insertado automáticamente cuya pulsación posterior lo sustituye en
/// lugar de duplicarlo. Solo ocurre cuando el caret queda <em>fuera</em> del
/// par (constructores y verbos sin argumentos: <c>flowStackJump()|</c>).
/// </summary>
/// <param name="Closer">Carácter de cierre insertado.</param>
/// <param name="Offset">Offset del cierre en el texto normalizado.</param>
internal readonly record struct PendingCloser(char Closer, int Offset);

/// <summary>
/// Autocierre contextual, salto sobre el cierre y borrado en pareja.
/// </summary>
/// <remarks>
/// Las reglas se calculan <em>después</em> de que el editor haya insertado el
/// carácter (o procesado el <c>Backspace</c>) y antes de registrar el paso de
/// deshacer, de modo que la corrección sea síncrona (0 ms), funcione con
/// cualquier distribución de teclado y no dependa de mapear <c>VirtualKey</c>.
/// Ante cualquier ambigüedad se devuelve <c>null</c> y el texto del usuario
/// queda intacto.
/// </remarks>
internal static class AutoPairAssist
{
    private static readonly Dictionary<string, VerbDescriptor> VerbsByName =
        LanguageCatalog.Verbs
            .GroupBy(verb => verb.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    /// <summary>
    /// Directivas cuyo argumento es una lambda: sus paréntesis abren la lista
    /// de parámetros donde sí se admiten tipos genéricos <c><Byte:N></c>.
    /// </summary>
    private static readonly HashSet<string> LambdaCapableNames =
        LanguageCatalog.Directives
            .Where(directive => directive.Syntax.Contains("->", StringComparison.Ordinal))
            .Select(directive => directive.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Planifica una reacción a la inserción de un único carácter. Devuelve
    /// <c>null</c> si no corresponde autocierre ni salto sobre el cierre.
    /// </summary>
    /// <param name="previous">Texto normalizado antes de la pulsación.</param>
    /// <param name="current">Texto normalizado después de la pulsación.</param>
    /// <param name="caret">Caret actual en offsets normalizados.</param>
    public static AssistEdit? PlanAfterTyping(
        string previous,
        string current,
        int caret,
        PendingCloser pending = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        // La pulsación debe ser exactamente una inserción de un carácter en la
        // posición del caret. No se usa un diff porque al teclear el mismo
        // carácter que ya sigue (saltar sobre el cierre) el prefijo común es
        // ambiguo.
        if (current.Length != previous.Length + 1 || caret <= 0 || caret > current.Length)
        {
            // Pegado, IME o reemplazo de selección: no se adivina nada.
            return null;
        }

        int typedOffset = caret - 1;
        if (!previous.AsSpan(0, typedOffset).SequenceEqual(current.AsSpan(0, typedOffset)) ||
            !previous.AsSpan(typedOffset).SequenceEqual(current.AsSpan(typedOffset + 1)))
        {
            return null;
        }

        int afterTyped = typedOffset + 1;
        char typed = current[typedOffset];

        // El cierre autoinsertado con el caret fuera del par se sustituye en
        // lugar de duplicarse: "flowStackJump()" + "|" + ")" -> "flowStackJump()|".
        if (pending.Closer != '\0' &&
            typed == pending.Closer &&
            pending.Offset + 1 == typedOffset &&
            pending.Offset < current.Length &&
            current[pending.Offset] == pending.Closer)
        {
            return new AssistEdit(pending.Offset, 1, string.Empty, pending.Offset + 1);
        }

        LexicalContext context = FasmTextContext.GetContext(current, typedOffset);

        switch (typed)
        {
            case ')':
            case ']':
            case '}':
            case '>':
                if (!context.IsCode)
                {
                    return null;
                }

                return afterTyped < current.Length && current[afterTyped] == typed
                    ? new AssistEdit(typedOffset, 1, string.Empty, typedOffset + 1)
                    : null;

            case '"':
                if (context.InsideLineComment || context.InsideBlockComment)
                {
                    return null;
                }

                if (afterTyped < current.Length && current[afterTyped] == '"')
                {
                    // El cierre ya estaba: saltar sobre él en lugar de duplicarlo.
                    return new AssistEdit(typedOffset, 1, string.Empty, typedOffset + 1);
                }

                if (context.InsideString ||
                    FasmTextContext.GetSection(previous, typedOffset) != FasmSectionKind.Data)
                {
                    // Solo se autocierran cadenas dentro de .data(...).
                    return null;
                }

                return new AssistEdit(afterTyped, 0, "\"", afterTyped);

            case '(':
                if (!context.IsCode)
                {
                    return null;
                }

                string identifier = ReadIdentifierBefore(previous, typedOffset);
                bool caretOutside = identifier.Length > 0 && ResolvesToZeroArguments(identifier);
                return new AssistEdit(afterTyped, 0, ")", caretOutside ? afterTyped + 1 : afterTyped);

            case '[':
                return context.IsCode
                    ? new AssistEdit(afterTyped, 0, "]", afterTyped)
                    : null;

            case '{':
                return context.IsCode
                    ? new AssistEdit(afterTyped, 0, "}", afterTyped)
                    : null;

            case '<':
                return context.IsCode && IsGenericOpenContext(previous, typedOffset)
                    ? new AssistEdit(afterTyped, 0, ">", afterTyped)
                    : null;

            case '*':
                if (!context.IsCode ||
                    typedOffset == 0 ||
                    previous[typedOffset - 1] != '/' ||
                    (afterTyped < current.Length && current[afterTyped] == '/'))
                {
                    return null;
                }

                // Se acaba de escribir "/*": completar " */" con el caret dentro.
                return new AssistEdit(afterTyped, 0, " */", afterTyped);

            default:
                return null;
        }
    }

    /// <summary>
    /// Planifica el borrado en pareja cuando el par inmediatamente alrededor
    /// del caret está vacío (<c>(|)</c>, <c>[|]</c>, <c>{|}</c>, <c><|></c>,
    /// <c>"|"</c>).
    /// </summary>
    public static AssistEdit? PlanAfterBackspace(string previous, string current, int caret)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        // El borrado es exacto: una sola eliminación y el resto del texto
        // intacto. No se usa un diff porque con pares de comillas ("") el
        // prefijo común es ambiguo.
        if (previous.Length != current.Length + 1)
        {
            return null;
        }

        // Tras el borrado el caret queda sobre la posición que ocupaba el
        // carácter eliminado: ese es el opener y el closer ahora está en esa
        // misma posición del texto actual.
        int removedAt = caret;
        if (removedAt < 0 || removedAt >= current.Length || removedAt >= previous.Length)
        {
            return null;
        }

        if (!previous.AsSpan(0, removedAt).SequenceEqual(current.AsSpan(0, removedAt)) ||
            !previous.AsSpan(removedAt + 1).SequenceEqual(current.AsSpan(removedAt)))
        {
            return null;
        }

        // El carácter borrado era el opener; el que ahora ocupa su lugar es el
        // closer del par vacío.
        char opener = previous[removedAt];
        char closer = current[removedAt];
        if (!IsMatchingPair(opener, closer))
        {
            return null;
        }

        // Los pares de comillas viven justamente dentro de una cadena; el resto
        // solo se borra en código.
        if (opener != '"' && !FasmTextContext.GetContext(current, removedAt).IsCode)
        {
            return null;
        }

        return new AssistEdit(removedAt, 1, string.Empty, removedAt);
    }

    /// <summary>
    /// Calcula el cierre pendiente que deja una edición aplicada, para que una
    /// pulsación posterior lo sustituya en lugar de duplicarlo.
    /// </summary>
    public static PendingCloser TrackPendingCloser(AssistEdit edit)
    {
        if (edit.Text.Length == 0)
        {
            return default;
        }

        char closer = edit.Text[^1];
        if (closer is not (')' or ']' or '}' or '>' or '"'))
        {
            return default;
        }

        int offset = edit.Start + edit.Text.Length - 1;

        // Solo cuando el caret quedó después del cierre insertado; si quedó
        // dentro, el salto sobre el cierre ya cubre el caso.
        return edit.CaretOffset == offset + 1
            ? new PendingCloser(closer, offset)
            : default;
    }

    private static bool IsMatchingPair(char opener, char closer) => (opener, closer) switch
    {
        ('(', ')') => true,
        ('[', ']') => true,
        ('{', '}') => true,
        ('<', '>') => true,
        ('\"', '\"') => true,
        _ => false,
    };

    /// <summary>
    /// Solo autocierra <c><</c> tras <c>define</c> o dentro de una lista de
    /// parámetros de lambda; nunca tras un operador ni un espacio aislado.
    /// </summary>
    private static bool IsGenericOpenContext(string previous, int typedOffset)
    {
        if (typedOffset == 0 || !IsIdentifierPart(previous[typedOffset - 1]))
        {
            return false;
        }

        string identifier = ReadIdentifierBefore(previous, typedOffset);
        if (identifier.Length == 0)
        {
            return false;
        }

        if (string.Equals(identifier, "define", StringComparison.Ordinal))
        {
            return true;
        }

        return IsInsideLambdaParameterList(previous, typedOffset);
    }

    /// <summary>
    /// Indica si el texto está dentro de la lista de parámetros de una lambda
    /// (los paréntesis de <c>d.run(...)</c> o <c>rt.define(...)</c>). Solo ahí
    /// tiene sentido un tipo genérico como <c>arg1<Byte:N></c>.
    /// </summary>
    private static bool IsInsideLambdaParameterList(string text, int offset)
    {
        var openParens = new Stack<int>();
        int limit = Math.Clamp(offset, 0, text.Length);
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

            switch (current)
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

                case '(':
                    openParens.Push(index);
                    break;

                case ')':
                    openParens.Pop();
                    break;
            }
        }

        while (openParens.Count > 0)
        {
            int open = openParens.Pop();
            string callee = ReadIdentifierBefore(text, open);
            if (!LambdaCapableNames.Contains(callee))
            {
                continue;
            }

            int next = open + 1;
            while (next < limit && (text[next] == ' ' || text[next] == '\t'))
            {
                next++;
            }

            // La lista de parámetros es el propio paréntesis de la llamada
            // ("run(arg1<...>)") o uno anidado ("run((arg1<...>) -> ...)").
            if (next < limit && (text[next] == '(' || char.IsLetterOrDigit(text[next]) || text[next] == '_'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ResolvesToZeroArguments(string identifier)
    {
        if (LanguageCatalog.FlowConstructorsByName.TryGetValue("." + identifier, out var flow))
        {
            return flow.MaxArguments == 0;
        }

        return VerbsByName.TryGetValue(identifier, out var verb) && verb.MaxArguments == 0;
    }

    private static string ReadIdentifierBefore(string text, int offset)
    {
        int end = Math.Clamp(offset, 0, text.Length);
        int start = end;
        while (start > 0 && IsIdentifierPart(text[start - 1]))
        {
            start--;
        }

        return text[start..end];
    }

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value == '_';
}
