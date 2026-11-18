using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>
/// Nodo del árbol sintáctico abstracto de FluentASM.
/// </summary>
/// <param name="Span">Intervalo de caracteres que abarca el nodo dentro del archivo fuente.</param>
public abstract record SyntaxNode(TextSpan Span);
