using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>
/// Parámetro de una lambda: un identificador simple (<c>dt</c>, <c>_</c>) o
/// uno tipado con magnitud (<c>arg1&lt;Byte:8&gt;</c>).
/// </summary>
public sealed record LambdaParameterSyntax(
    Token Name,
    MagnitudeSyntax? Magnitude,
    TextSpan Span) : SyntaxNode(Span);

/// <summary>Cuerpo de una lambda.</summary>
public abstract record LambdaBodySyntax(TextSpan Span) : SyntaxNode(Span);

/// <summary>Cuerpo de declaraciones o sentencias entre llaves.</summary>
public sealed record BlockBodySyntax(
    IReadOnlyList<StatementSyntax> Statements,
    TextSpan Span) : LambdaBodySyntax(Span);

/// <summary>
/// Cuerpo de configuración fluida encadenada, por ejemplo
/// <c>d.id(numA).value(10)</c> o <c>c.target(sumar).inputs(Gpr1).output(Gpr0)</c>.
/// </summary>
public sealed record FluentChainBodySyntax(
    ExpressionSyntax Receiver,
    IReadOnlyList<FluentCallSyntax> Calls,
    TextSpan Span) : LambdaBodySyntax(Span);

/// <summary>Lambda <c>() -&gt; {...}</c>, <c>dt -&gt; {...}</c> o <c>d -&gt; d.id(...)...</c>.</summary>
public sealed record LambdaExpressionSyntax(
    IReadOnlyList<LambdaParameterSyntax> Parameters,
    LambdaBodySyntax Body,
    TextSpan Span) : ExpressionSyntax(Span);

/// <summary>Llamada fluida <c>.nombre(argumentos)</c> dentro de una cadena de configuración.</summary>
public sealed record FluentCallSyntax(
    Token Name,
    IReadOnlyList<ExpressionSyntax> Arguments,
    TextSpan Span) : SyntaxNode(Span);
