using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax;

/// <summary>
/// Token emitido por el lexer, con su texto original, ubicación y span.
/// </summary>
public sealed record Token
{
    /// <summary>Categoría léxica del token.</summary>
    public required TokenKind Kind { get; init; }

    /// <summary>Texto exacto del token tal como aparece en el archivo.</summary>
    public required string Text { get; init; }

    /// <summary>Intervalo de caracteres que ocupa en el archivo.</summary>
    public required TextSpan Span { get; init; }

    /// <summary>Ubicación del primer carácter: archivo, línea y columna (base 1).</summary>
    public required SourceLocation Location { get; init; }

    /// <summary>Valor del literal cuando <see cref="Kind"/> es <see cref="TokenKind.Integer"/>.</summary>
    public long IntegerValue { get; init; }

    /// <summary>Contenido ya decodificado cuando <see cref="Kind"/> es <see cref="TokenKind.String"/>.</summary>
    public string? StringValue { get; init; }

    /// <summary>Unidad física cuando <see cref="Kind"/> es <see cref="TokenKind.Magnitude"/>.</summary>
    public MagnitudeKind Magnitude { get; init; } = MagnitudeKind.None;

    /// <inheritdoc />
    public override string ToString() => $"{Location}: {Kind} '{Text}'";
}


