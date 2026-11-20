using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;
using FluentASM.Lsp.Protocol;

namespace FluentASM.Lsp.Server;

/// <summary>
/// Fotografía inmutable del análisis de una versión concreta de un documento.
/// El lexer, el parser y el analizador semántico se ejecutan una sola vez por
/// versión y todos los servicios (diagnósticos, resaltado, completado, hover y
/// símbolos) reutilizan este mismo resultado.
/// </summary>
public sealed class DocumentSnapshot
{
    /// <summary>URI del documento analizado.</summary>
    public required string Uri { get; init; }

    /// <summary>Versión del documento a la que corresponde el análisis.</summary>
    public required int Version { get; init; }

    /// <summary>Texto exacto que se analizó.</summary>
    public required string Text { get; init; }

    /// <summary>Índice de líneas del texto, reutilizado para traducir rangos.</summary>
    public required DocumentLines Lines { get; init; }

    /// <summary>Resultado del análisis léxico.</summary>
    public required LexResult LexResult { get; init; }

    /// <summary>Resultado del análisis sintáctico (siempre con raíz parcial).</summary>
    public required ParseResult ParseResult { get; init; }

    /// <summary>
    /// Resultado del análisis semántico; <c>null</c> solo si la fase semántica
    /// falló de forma catastrófica y se conserva el árbol parcial como respaldo.
    /// </summary>
    public SemanticResult? SemanticResult { get; init; }

    /// <summary>Diagnósticos LSP ya traducidos y ordenados.</summary>
    public required IReadOnlyList<LspDiagnostic> Diagnostics { get; init; }

    /// <summary>Tokens clasificados para el resaltado sintáctico.</summary>
    public required IReadOnlyList<HighlightToken> HighlightTokens { get; init; }

    /// <summary>Mensaje de la excepción interna, si la hubo.</summary>
    public string? AnalysisError { get; init; }

    /// <summary>Indica si hay una tabla de símbolos utilizable.</summary>
    public bool HasSymbols => SemanticResult is not null;
}
