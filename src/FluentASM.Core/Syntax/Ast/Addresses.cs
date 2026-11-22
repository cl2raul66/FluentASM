using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>
/// Expresión de dirección dentro de <c>[ ... ]</c>: base, índice opcional y
/// desplazamiento con signo.
/// </summary>
/// <param name="Base">Símbolo de <c>.data</c> o registro base.</param>
/// <param name="Index">Registro índice y escala, si existen.</param>
/// <param name="Displacement">Desplazamiento sumado (puede ser negativo).</param>
/// <param name="Span">Intervalo de la expresión entre corchetes.</param>
public sealed record AddressExpressionSyntax(
    ExpressionSyntax Base,
    AddressIndexSyntax? Index,
    long Displacement,
    TextSpan Span) : SyntaxNode(Span);

/// <summary>Registro índice con escala opcional, por ejemplo <c>Gpr1*4</c>.</summary>
public sealed record AddressIndexSyntax(
    ExpressionSyntax Register,
    ExpressionSyntax? Scale,
    TextSpan Span) : SyntaxNode(Span);


