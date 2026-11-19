using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>Sentencia dentro de un bloque <c>{ ... }</c>.</summary>
public abstract record StatementSyntax(TextSpan Span) : SyntaxNode(Span);

/// <summary>Etiqueta local <c>@nombre:</c>.</summary>
public sealed record LabelStatementSyntax(
    Token Label,
    TextSpan Span) : StatementSyntax(Span);

/// <summary>
/// Acción fluida <c>receptor.verbo([argumento])</c>, con o sin argumentos.
/// </summary>
public sealed record ActionStatementSyntax(
    ExpressionSyntax Receiver,
    Token Verb,
    IReadOnlyList<ExpressionSyntax> Arguments,
    TextSpan Span) : StatementSyntax(Span);

/// <summary>
/// Declaración <c>dt.define&lt;Magnitud&gt;(...)</c> dentro de un bloque <c>.data(...)</c>.
/// </summary>
public sealed record DataDefinitionStatementSyntax(
    DataDefinitionSyntax Definition,
    TextSpan Span) : StatementSyntax(Span);


