using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>Expresión u operando de FluentASM.</summary>
public abstract record ExpressionSyntax(TextSpan Span) : SyntaxNode(Span);

/// <summary>Identificador de usuario, registro, unidad o miembro.</summary>
public sealed record IdentifierExpressionSyntax(
    Token Identifier) : ExpressionSyntax(Identifier.Span);

/// <summary>Literal entero decimal, hexadecimal o binario.</summary>
public sealed record IntegerLiteralSyntax(
    Token Literal) : ExpressionSyntax(Literal.Span)
{
    /// <summary>Valor entero ya interpretado por el lexer.</summary>
    public long Value => Literal.IntegerValue;
}

/// <summary>Literal de cadena ya decodificado por el lexer.</summary>
public sealed record StringLiteralSyntax(
    Token Literal) : ExpressionSyntax(Literal.Span)
{
    /// <summary>Contenido de la cadena sin comillas y con los escapes resueltos.</summary>
    public string Value => Literal.StringValue ?? string.Empty;
}

/// <summary>Estado base limpio <c>defaultValue</c>.</summary>
public sealed record DefaultValueSyntax(
    Token Keyword) : ExpressionSyntax(Keyword.Span);

/// <summary>Palabra contextual <c>not</c> usada como argumento (por ejemplo <c>Frf.carry(not)</c>).</summary>
public sealed record NotValueSyntax(
    Token Keyword) : ExpressionSyntax(Keyword.Span);

/// <summary>Referencia a una etiqueta local <c>@nombre</c>.</summary>
public sealed record LabelReferenceSyntax(
    Token Label) : ExpressionSyntax(Label.Span);

/// <summary>Magnitud física <c>Bit:N</c> o <c>Byte:N</c> (y sus múltiplos).</summary>
public sealed record MagnitudeSyntax(
    Token Unit,
    Token Size,
    TextSpan Span) : ExpressionSyntax(Span);

/// <summary>Acceso a miembro o vista, por ejemplo <c>Gpr0.low32</c> o <c>Frf.zero</c>.</summary>
public sealed record MemberAccessSyntax(
    ExpressionSyntax Receiver,
    Token Member,
    TextSpan Span) : ExpressionSyntax(Span);

/// <summary>Operando de memoria <c>[simbolo]</c> o <c>Byte:N [base + indice*escala +/- disp]</c>.</summary>
public sealed record MemoryOperandSyntax(
    MagnitudeSyntax? Capacity,
    AddressExpressionSyntax Address,
    TextSpan Span) : ExpressionSyntax(Span);

/// <summary>Espacio de puertos hardware <c>Port[...]</c>.</summary>
public sealed record PortOperandSyntax(
    Token PortKeyword,
    ExpressionSyntax Address,
    TextSpan Span) : ExpressionSyntax(Span);

/// <summary>Tupla de destino <c>(Gpr2:Gpr0)</c>.</summary>
public sealed record RegisterPairSyntax(
    ExpressionSyntax High,
    ExpressionSyntax Low,
    TextSpan Span) : ExpressionSyntax(Span);

/// <summary>Argumento condicionado <c>fuente if condicion</c>.</summary>
public sealed record ConditionalExpressionSyntax(
    ExpressionSyntax Value,
    ConditionSyntax Condition,
    TextSpan Span) : ExpressionSyntax(Span);

/// <summary>
/// Expresión de relleno que conserva el árbol parcial cuando el texto no es recuperable.
/// </summary>
public sealed record ErrorExpressionSyntax(
    string Message,
    TextSpan Span) : ExpressionSyntax(Span);
