using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax;

/// <summary>
/// Comentario descartado por el lexer, conservado como trivia para que las
/// herramientas (LSP, IDE) puedan resaltarlo sin volver a analizar el texto.
/// </summary>
/// <param name="Span">Intervalo exacto que ocupa el comentario en el archivo.</param>
/// <param name="IsBlock">Indica si es un comentario de bloque <c>/* ... */</c>.</param>
public sealed record CommentTrivia(TextSpan Span, bool IsBlock);
