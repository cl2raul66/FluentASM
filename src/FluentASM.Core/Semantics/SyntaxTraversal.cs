using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Recorrido auxiliar del AST para inspeccionar expresiones, condiciones y
/// referencias a etiquetas sin duplicar la lógica en cada validador.
/// </summary>
internal static class SyntaxTraversal
{
    /// <summary>Enumera las expresiones contenidas en una sentencia (incluidas las anidadas).</summary>
    public static IEnumerable<ExpressionSyntax> Expressions(StatementSyntax statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        switch (statement)
        {
            case ActionStatementSyntax action:
                foreach (var expression in Descendants(action.Receiver))
                {
                    yield return expression;
                }

                foreach (var argument in action.Arguments)
                {
                    foreach (var expression in Descendants(argument))
                    {
                        yield return expression;
                    }
                }

                break;

            case DataDefinitionStatementSyntax definition:
                foreach (var value in definition.Definition.Values)
                {
                    foreach (var expression in Descendants(value))
                    {
                        yield return expression;
                    }
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Enumera las referencias a etiquetas <c>@nombre</c> de una sentencia.</summary>
    public static IEnumerable<LabelReferenceSyntax> LabelReferences(StatementSyntax statement) =>
        Expressions(statement).OfType<LabelReferenceSyntax>();

    /// <summary>Primer token significativo de una expresión, útil para ubicar diagnósticos.</summary>
    public static Token? FirstToken(ExpressionSyntax expression) => expression switch
    {
        IdentifierExpressionSyntax identifier => identifier.Identifier,
        IntegerLiteralSyntax integer => integer.Literal,
        StringLiteralSyntax text => text.Literal,
        DefaultValueSyntax keyword => keyword.Keyword,
        NotValueSyntax keyword => keyword.Keyword,
        LabelReferenceSyntax label => label.Label,
        MagnitudeSyntax magnitude => magnitude.Unit,
        MemberAccessSyntax member => FirstToken(member.Receiver) ?? member.Member,
        MemoryOperandSyntax memory => FirstToken(memory.Address.Base),
        PortOperandSyntax port => port.PortKeyword,
        RegisterPairSyntax pair => FirstToken(pair.High),
        ConditionalExpressionSyntax conditional => FirstToken(conditional.Value),
        _ => null,
    };

    /// <summary>Enumera una expresión y todos sus descendientes.</summary>
    public static IEnumerable<ExpressionSyntax> Descendants(ExpressionSyntax expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        yield return expression;
        foreach (var child in Children(expression))
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Enumera las expresiones de una condición <c>if</c>.</summary>
    public static IEnumerable<ExpressionSyntax> ConditionExpressions(ConditionSyntax condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        switch (condition)
        {
            case FlagConditionSyntax flag:
                foreach (var expression in Descendants(flag.Flag))
                {
                    yield return expression;
                }

                break;

            case NotConditionSyntax not:
                foreach (var expression in ConditionExpressions(not.Operand))
                {
                    yield return expression;
                }

                break;

            case BinaryConditionSyntax binary:
                foreach (var expression in ConditionExpressions(binary.Left))
                {
                    yield return expression;
                }

                foreach (var expression in ConditionExpressions(binary.Right))
                {
                    yield return expression;
                }

                break;

            default:
                break;
        }
    }

    private static IEnumerable<ExpressionSyntax> Children(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessSyntax member:
                yield return member.Receiver;
                break;

            case MemoryOperandSyntax memory:
                yield return memory.Address.Base;
                if (memory.Address.Index is { } index)
                {
                    yield return index.Register;
                    if (index.Scale is { } scale)
                    {
                        yield return scale;
                    }
                }

                break;

            case PortOperandSyntax port:
                yield return port.Address;
                break;

            case RegisterPairSyntax pair:
                yield return pair.High;
                yield return pair.Low;
                break;

            case ConditionalExpressionSyntax conditional:
                yield return conditional.Value;
                foreach (var conditionExpression in ConditionExpressions(conditional.Condition))
                {
                    yield return conditionExpression;
                }

                break;

            case LambdaExpressionSyntax lambda:
                foreach (var bodyExpression in LambdaBodyExpressions(lambda))
                {
                    yield return bodyExpression;
                }

                break;

            default:
                yield break;
        }
    }

    private static IEnumerable<ExpressionSyntax> LambdaBodyExpressions(LambdaExpressionSyntax lambda)
    {
        switch (lambda.Body)
        {
            case BlockBodySyntax block:
                foreach (var statement in block.Statements)
                {
                    foreach (var expression in Expressions(statement))
                    {
                        yield return expression;
                    }
                }

                break;

            case FluentChainBodySyntax chain:
                foreach (var expression in Descendants(chain.Receiver))
                {
                    yield return expression;
                }

                foreach (var call in chain.Calls)
                {
                    foreach (var argument in call.Arguments)
                    {
                        foreach (var expression in Descendants(argument))
                        {
                            yield return expression;
                        }
                    }
                }

                break;

            default:
                break;
        }
    }
}


