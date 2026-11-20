using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>
/// Definición de datos <c>dt.define&lt;Byte:8&gt;(d -&gt; d.id(numA).value(10))</c>.
/// </summary>
/// <param name="Name">Símbolo declarado con <c>d.id(...)</c>; <c>null</c> si falta.</param>
/// <param name="Capacity">Capacidad física declarada entre <c>&lt; &gt;</c>.</param>
/// <param name="Values">Valores declarados con <c>d.value(...)</c> (uno, lista homogénea o <c>defaultValue</c>).</param>
/// <param name="Descriptor">Lambda completa del descriptor de datos.</param>
/// <param name="Span">Intervalo de la definición.</param>
public sealed record DataDefinitionSyntax(
    Token? Name,
    MagnitudeSyntax Capacity,
    IReadOnlyList<ExpressionSyntax> Values,
    LambdaExpressionSyntax Descriptor,
    TextSpan Span) : SyntaxNode(Span);

/// <summary>
/// Definición de rutina <c>rt.define(d -&gt; { d.id(...); d.returns(...); d.run(...) })</c>.
/// </summary>
/// <param name="Name">Identificador declarado con <c>d.id(...)</c>; <c>null</c> si falta.</param>
/// <param name="Returns">Receptor declarado con <c>d.returns(...)</c>, si existe.</param>
/// <param name="Run">Lambda declarada con <c>d.run(...)</c>; <c>null</c> si falta.</param>
/// <param name="Body">Sentencias del cuerpo de <c>d.run(...)</c>.</param>
/// <param name="IsImplicit">Indica la forma implícita <c>.routines(_ -&gt; { ... })</c>.</param>
/// <param name="Span">Intervalo de la definición.</param>
public sealed record RoutineDefinitionSyntax(
    Token? Name,
    ExpressionSyntax? Returns,
    LambdaExpressionSyntax? Run,
    IReadOnlyList<StatementSyntax> Body,
    bool IsImplicit,
    TextSpan Span) : SyntaxNode(Span);


