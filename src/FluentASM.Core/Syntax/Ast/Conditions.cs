using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>Condición de un argumento <c>if</c>.</summary>
public abstract record ConditionSyntax(TextSpan Span) : SyntaxNode(Span);

/// <summary>Bandera o alias de <c>Frf</c>, por ejemplo <c>Frf.zero</c>.</summary>
public sealed record FlagConditionSyntax(
    ExpressionSyntax Flag) : ConditionSyntax(Flag.Span);

/// <summary>Negación lógica <c>not condicion</c>.</summary>
public sealed record NotConditionSyntax(
    Token Not,
    ConditionSyntax Operand,
    TextSpan Span) : ConditionSyntax(Span);

/// <summary>Composición <c>and</c>, <c>or</c>, <c>equal</c> o <c>unequal</c> de dos condiciones.</summary>
public sealed record BinaryConditionSyntax(
    ConditionSyntax Left,
    Token Operator,
    ConditionSyntax Right,
    TextSpan Span) : ConditionSyntax(Span);


