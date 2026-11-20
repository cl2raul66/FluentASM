using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax;

/// <summary>
/// Resultado de tokenizar un archivo fuente.
/// </summary>
public sealed class LexResult
{
    /// <summary>Crea un resultado léxico.</summary>
    public LexResult(
        IReadOnlyList<Token> tokens,
        DiagnosticBag diagnostics,
        IReadOnlyList<CommentTrivia>? comments = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Tokens = tokens;
        Diagnostics = diagnostics;
        Comments = comments ?? Array.Empty<CommentTrivia>();
    }

    /// <summary>Tokens en orden de aparición, terminando siempre en <see cref="TokenKind.EndOfFile"/>.</summary>
    public IReadOnlyList<Token> Tokens { get; }

    /// <summary>Comentarios descartados, en orden de aparición.</summary>
    public IReadOnlyList<CommentTrivia> Comments { get; }

    /// <summary>Diagnósticos léxicos emitidos.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>Indica si no se emitió ningún error léxico.</summary>
    public bool Success => !Diagnostics.HasErrors;
}


