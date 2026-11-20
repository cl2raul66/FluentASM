using System.Collections.Immutable;
using FluentASM.Core.Catalog;
using FluentASM.Core.Syntax;
using FluentASM.Lsp.Protocol;

namespace FluentASM.Lsp.Server;

/// <summary>
/// Clasifica los tokens del lexer contra el catálogo único de
/// <c>FluentASM.Core</c> para que el editor solo tenga que pintar rangos.
/// </summary>
internal static class TokenClassifier
{
    private static readonly ImmutableHashSet<string> Registers = BuildRegisters();
    private static readonly ImmutableHashSet<string> Flags = BuildFlags();
    private static readonly ImmutableHashSet<string> Verbs = BuildVerbs();
    private static readonly ImmutableHashSet<string> Magnitudes = BuildMagnitudes();
    private static readonly ImmutableHashSet<string> Directives = BuildDirectives();
    private static readonly ImmutableHashSet<string> FlowConstructors = BuildFlowConstructors();

    /// <summary>Clasifica todos los tokens significativos de un documento.</summary>
    public static IReadOnlyList<HighlightToken> Classify(IReadOnlyList<Token> tokens, DocumentLines lines)
    {
        var result = new List<HighlightToken>(tokens.Count);

        for (int index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.EndOfFile)
            {
                continue;
            }

            var kind = ClassifyToken(tokens, index);
            if (kind is null)
            {
                continue;
            }

            var range = new LspRange(
                lines.ToPosition(token.Span.Start),
                lines.ToPosition(token.Span.End));

            result.Add(new HighlightToken(range, kind.Value, token.Text));
        }

        return result;
    }

    private static HighlightTokenKind? ClassifyToken(IReadOnlyList<Token> tokens, int index)
    {
        var token = tokens[index];

        return token.Kind switch
        {
            TokenKind.Identifier => ClassifyIdentifier(tokens, index),
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

    private static HighlightTokenKind ClassifyIdentifier(IReadOnlyList<Token> tokens, int index)
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
                    return HighlightTokenKind.Register;
                }
            }

            if (FlowConstructors.Contains(token.Text))
            {
                return HighlightTokenKind.Keyword;
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
            builder.Add(constructor.Name);
        }

        return builder.ToImmutable();
    }
}
