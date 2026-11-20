using System.Collections.Immutable;
using FluentASM.Core.Catalog;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;
using FluentASM.Lsp.Protocol;

namespace FluentASM.Lsp.Server;

/// <summary>
/// Clasifica los tokens del lexer y los comentarios contra el catálogo único de
/// <c>FluentASM.Core</c>, enriqueciendo los identificadores con la tabla de
/// símbolos cuando está disponible. El editor solo tiene que pintar los spans.
/// </summary>
internal static class TokenClassifier
{
    private static readonly ImmutableHashSet<string> Registers = BuildRegisters();
    private static readonly ImmutableHashSet<string> Flags = BuildFlags();
    private static readonly ImmutableHashSet<string> Verbs = BuildVerbs();
    private static readonly ImmutableHashSet<string> Magnitudes = BuildMagnitudes();
    private static readonly ImmutableHashSet<string> Directives = BuildDirectives();
    private static readonly ImmutableHashSet<string> FlowConstructors = BuildFlowConstructors();

    /// <summary>
    /// Clasifica todos los tokens y comentarios del documento, devolviendo spans
    /// absolutos base 0 sobre el texto normalizado.
    /// </summary>
    public static IReadOnlyList<HighlightToken> Classify(
        string text,
        IReadOnlyList<Token> tokens,
        IReadOnlyList<CommentTrivia> comments,
        DocumentLines lines,
        SemanticResult? semantic)
    {
        var dataNames = new HashSet<string>(StringComparer.Ordinal);
        var routineNames = new HashSet<string>(StringComparer.Ordinal);

        if (semantic is not null)
        {
            foreach (var data in semantic.Symbols.DataSymbols)
            {
                dataNames.Add(data.Name);
            }

            foreach (var routine in semantic.Symbols.Routines)
            {
                if (routine.Name is { Length: > 0 } name)
                {
                    routineNames.Add(name);
                }
            }
        }

        var result = new List<HighlightToken>(tokens.Count + comments.Count);

        for (int index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.EndOfFile)
            {
                continue;
            }

            var kind = ClassifyToken(tokens, index, dataNames, routineNames);
            if (kind is null)
            {
                continue;
            }

            result.Add(CreateToken(token.Span, kind.Value, token.Text, lines));
        }

        foreach (var comment in comments)
        {
            if (comment.Span.Length <= 0)
            {
                continue;
            }

            int start = Math.Clamp(comment.Span.Start, 0, text.Length);
            int end = Math.Clamp(comment.Span.End, start, text.Length);
            if (end == start)
            {
                continue;
            }

            result.Add(new HighlightToken(
                start,
                end - start,
                HighlightTokenKind.Comment,
                text[start..end],
                new LspRange(lines.ToPosition(start), lines.ToPosition(end))));
        }

        result.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return result;
    }

    private static HighlightToken CreateToken(TextSpan span, HighlightTokenKind kind, string text, DocumentLines lines) =>
        new(
            span.Start,
            span.Length,
            kind,
            text,
            new LspRange(lines.ToPosition(span.Start), lines.ToPosition(span.End)));

    private static HighlightTokenKind? ClassifyToken(
        IReadOnlyList<Token> tokens,
        int index,
        HashSet<string> dataNames,
        HashSet<string> routineNames)
    {
        var token = tokens[index];

        return token.Kind switch
        {
            TokenKind.Identifier => ClassifyIdentifier(tokens, index, dataNames, routineNames),
            TokenKind.Label => HighlightTokenKind.Label,
            TokenKind.Integer => HighlightTokenKind.Number,
            TokenKind.String => HighlightTokenKind.String,
            TokenKind.Magnitude => HighlightTokenKind.Magnitude,
            TokenKind.Port => HighlightTokenKind.Register,
            TokenKind.DefaultValue or TokenKind.If or TokenKind.Not or TokenKind.And
                or TokenKind.Or or TokenKind.Equal or TokenKind.Unequal => HighlightTokenKind.Keyword,
            TokenKind.Dot or TokenKind.Colon or TokenKind.Comma or TokenKind.OpenParen
                or TokenKind.CloseParen or TokenKind.OpenBracket or TokenKind.CloseBracket
                or TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.OpenAngle
                or TokenKind.CloseAngle => HighlightTokenKind.Punctuation,
            TokenKind.Arrow or TokenKind.Underscore or TokenKind.Plus
                or TokenKind.Minus or TokenKind.Star => HighlightTokenKind.Operator,
            _ => null,
        };
    }

    private static HighlightTokenKind ClassifyIdentifier(
        IReadOnlyList<Token> tokens,
        int index,
        HashSet<string> dataNames,
        HashSet<string> routineNames)
    {
        var token = tokens[index];

        if (index > 0 && tokens[index - 1].Kind == TokenKind.Dot)
        {
            string? receiver = index > 1 && tokens[index - 2].Kind == TokenKind.Identifier
                ? tokens[index - 2].Text
                : null;

            if (receiver is not null)
            {
                string qualified = receiver + "." + token.Text;
                if (Flags.Contains(qualified))
                {
                    return HighlightTokenKind.Flag;
                }

                if (Registers.Contains(qualified))
                {
                    // Algunos miembros de unidad son verbos (Stack.push, Cpu.halt, Port.copy).
                    return Verbs.Contains(token.Text)
                        ? HighlightTokenKind.Verb
                        : HighlightTokenKind.Register;
                }
            }

            if (FlowConstructors.Contains(token.Text))
            {
                return HighlightTokenKind.FlowConstructor;
            }

            if (Directives.Contains(token.Text))
            {
                return HighlightTokenKind.Directive;
            }

            if (Verbs.Contains(token.Text))
            {
                return HighlightTokenKind.Verb;
            }

            if (Magnitudes.Contains(token.Text))
            {
                return HighlightTokenKind.Magnitude;
            }

            return HighlightTokenKind.Identifier;
        }

        if (dataNames.Contains(token.Text))
        {
            return HighlightTokenKind.Data;
        }

        if (routineNames.Contains(token.Text))
        {
            return HighlightTokenKind.Routine;
        }

        if (Registers.Contains(token.Text))
        {
            return HighlightTokenKind.Register;
        }

        if (Flags.Contains(token.Text))
        {
            return HighlightTokenKind.Flag;
        }

        if (Verbs.Contains(token.Text))
        {
            return HighlightTokenKind.Verb;
        }

        if (Magnitudes.Contains(token.Text))
        {
            return HighlightTokenKind.Magnitude;
        }

        return HighlightTokenKind.Identifier;
    }

    private static ImmutableHashSet<string> BuildRegisters()
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var gpr in LanguageCatalog.Gprs)
        {
            builder.Add(gpr.Name);
            foreach (var view in gpr.ValidViews)
            {
                if (view.Suffix.Length > 0)
                {
                    builder.Add(gpr.Name + view.Suffix);
                }
            }
        }

        foreach (var unit in LanguageCatalog.Units)
        {
            builder.Add(unit.Name);
            foreach (var member in unit.Members)
            {
                builder.Add(unit.Name + "." + member);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> BuildFlags()
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var flag in LanguageCatalog.Flags)
        {
            builder.Add("Frf." + flag.Name);
        }

        foreach (var alias in LanguageCatalog.FlagAliases)
        {
            builder.Add("Frf." + alias.Alias);
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> BuildVerbs()
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var verb in LanguageCatalog.Verbs)
        {
            builder.Add(verb.Name);
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> BuildMagnitudes()
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var magnitude in LanguageCatalog.Magnitudes)
        {
            builder.Add(magnitude.Name);
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> BuildDirectives()
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var directive in LanguageCatalog.Directives)
        {
            builder.Add(directive.Name);
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> BuildFlowConstructors()
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var constructor in LanguageCatalog.FlowConstructors)
        {
            builder.Add(constructor.Name.TrimStart('.'));
        }

        return builder.ToImmutable();
    }
}
