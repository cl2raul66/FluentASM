using System.Text;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Sample;

/// <summary>
/// Volcado legible del AST para validar manualmente el parser de M1.
/// </summary>
internal static class AstDumper
{
    public static string Dump(CompilationUnitSyntax unit)
    {
        var builder = new StringBuilder();
        WriteUnit(builder, unit, 0);
        return builder.ToString();
    }

    private static void WriteUnit(StringBuilder builder, CompilationUnitSyntax unit, int indent)
    {
        var name = unit.ProjectName?.Text ?? "<sin nombre>";
        WriteLine(builder, indent, $"CompilationUnit {name} {Span(unit.Span)}");

        if (unit.Data is not null)
        {
            WriteLine(builder, indent + 1, $"DataSection ({unit.Data.Definitions.Count} definiciones)");
            foreach (var definition in unit.Data.Definitions)
            {
                WriteDataDefinition(builder, definition, indent + 2);
            }
        }
        else
        {
            WriteLine(builder, indent + 1, "DataSection <omitida>");
        }

        if (unit.Execute is not null)
        {
            WriteLine(builder, indent + 1, $"ExecuteSection ({unit.Execute.Routines.Count} rutinas)");
            foreach (var routine in unit.Execute.Routines)
            {
                WriteRoutine(builder, routine, indent + 2);
            }
        }
        else
        {
            WriteLine(builder, indent + 1, "ExecuteSection <ausente>");
        }

        WriteLine(builder, indent + 1, unit.Initialize is null ? "Initialize <ausente>" : "Initialize .initialize()");
    }

    private static void WriteRoutine(
        StringBuilder builder,
        RoutineDefinitionSyntax routine,
        int indent)
    {
        var header = routine.IsImplicit
            ? "Routine <implícita>"
            : $"Routine {routine.Name?.Text ?? "<sin id>"}";
        var returns = routine.Returns is null ? "sin retorno" : $"devuelve {Describe(routine.Returns)}";
        WriteLine(builder, indent, $"{header} ({returns}) {Span(routine.Span)}");

        if (routine.Body.Count == 0)
        {
            WriteLine(builder, indent + 1, "<cuerpo vacío>");
            return;
        }

        foreach (var statement in routine.Body)
        {
            WriteStatement(builder, statement, indent + 1);
        }
    }

    private static void WriteDataDefinition(
        StringBuilder builder,
        DataDefinitionSyntax definition,
        int indent)
    {
        var name = definition.Name?.Text ?? "<sin id>";
        var values = definition.Values.Count == 0
            ? "<sin valor>"
            : string.Join(", ", definition.Values.Select(Describe));
        WriteLine(
            builder,
            indent,
            $"DataDefinition {name} : {Describe(definition.Capacity)} = {values}");
    }

    private static void WriteStatement(
        StringBuilder builder,
        StatementSyntax statement,
        int indent)
    {
        switch (statement)
        {
            case LabelStatementSyntax label:
                WriteLine(builder, indent, $"Label {label.Label.Text}");
                break;

            case ActionStatementSyntax action:
                var arguments = action.Arguments.Count == 0
                    ? string.Empty
                    : string.Join(", ", action.Arguments.Select(Describe));
                WriteLine(
                    builder,
                    indent,
                    $"Action {Describe(action.Receiver)}.{action.Verb.Text}({arguments})");
                break;

            case DataDefinitionStatementSyntax definition:
                WriteDataDefinition(builder, definition.Definition, indent);
                break;

            default:
                WriteLine(builder, indent, statement.GetType().Name);
                break;
        }
    }

    private static string Describe(ExpressionSyntax expression) => expression switch
    {
        IdentifierExpressionSyntax identifier => identifier.Identifier.Text,
        IntegerLiteralSyntax integer => integer.Literal.Text,
        StringLiteralSyntax text => $"\"{Escape(text.Value)}\"",
        DefaultValueSyntax => "defaultValue",
        NotValueSyntax => "not",
        LabelReferenceSyntax label => label.Label.Text,
        MagnitudeSyntax magnitude => $"{magnitude.Unit.Text}:{magnitude.Size.Text}",
        MemberAccessSyntax member => $"{Describe(member.Receiver)}.{member.Member.Text}",
        MemoryOperandSyntax memory => DescribeMemory(memory),
        PortOperandSyntax port => $"Port[{Describe(port.Address)}]",
        RegisterPairSyntax pair => $"({Describe(pair.High)}:{Describe(pair.Low)})",
        ConditionalExpressionSyntax conditional =>
            $"{Describe(conditional.Value)} if {Describe(conditional.Condition)}",
        LambdaExpressionSyntax lambda => DescribeLambda(lambda),
        ErrorExpressionSyntax error => $"<error: {error.Message}>",
        _ => expression.GetType().Name,
    };

    private static string DescribeMemory(MemoryOperandSyntax memory)
    {
        var capacity = memory.Capacity is null ? string.Empty : $"{Describe(memory.Capacity)} ";
        return $"{capacity}[{DescribeAddress(memory.Address)}]";
    }

    private static string DescribeAddress(AddressExpressionSyntax address)
    {
        var text = Describe(address.Base);

        if (address.Index is not null)
        {
            text += $" + {Describe(address.Index.Register)}";
            if (address.Index.Scale is not null)
            {
                text += $"*{Describe(address.Index.Scale)}";
            }
        }

        if (address.Displacement > 0)
        {
            text += $" + {address.Displacement}";
        }
        else if (address.Displacement < 0)
        {
            text += $" - {Math.Abs(address.Displacement)}";
        }

        return text;
    }

    private static string DescribeLambda(LambdaExpressionSyntax lambda)
    {
        var parameters = lambda.Parameters.Count == 0
            ? "()"
            : string.Join(", ", lambda.Parameters.Select(DescribeParameter));

        return lambda.Body switch
        {
            FluentChainBodySyntax chain => $"{parameters} -> {DescribeChain(chain)}",
            BlockBodySyntax block => $"{parameters} -> {{ {block.Statements.Count} sentencias }}",
            _ => parameters,
        };
    }

    private static string DescribeChain(FluentChainBodySyntax chain)
    {
        var builder = new StringBuilder(Describe(chain.Receiver));
        foreach (var call in chain.Calls)
        {
            builder.Append('.').Append(call.Name.Text).Append('(');
            builder.AppendJoin(", ", call.Arguments.Select(Describe));
            builder.Append(')');
        }

        return builder.ToString();
    }

    private static string DescribeParameter(LambdaParameterSyntax parameter) =>
        parameter.Magnitude is null
            ? parameter.Name.Text
            : $"{parameter.Name.Text}<{Describe(parameter.Magnitude)}>";

    private static string Describe(ConditionSyntax condition) => condition switch
    {
        FlagConditionSyntax flag => Describe(flag.Flag),
        NotConditionSyntax not => $"not {Describe(not.Operand)}",
        BinaryConditionSyntax binary =>
            $"{Describe(binary.Left)} {binary.Operator.Text} {Describe(binary.Right)}",
        _ => condition.GetType().Name,
    };

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\0':
                    builder.Append("\\0");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string Span(FluentASM.Core.Diagnostics.TextSpan span) =>
        $"[{span.Start}..{span.End}]";

    private static void WriteLine(StringBuilder builder, int indent, string text) =>
        builder.Append(' ', indent * 2).AppendLine(text);
}


