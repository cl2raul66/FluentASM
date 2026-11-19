namespace FluentASM.Lsp.Protocol;

/// <summary>
/// Posición dentro de un documento de texto según la especificación de LSP (base 0).
/// </summary>
/// <param name="Line">Línea base 0.</param>
/// <param name="Character">Carácter / columna base 0.</param>
public readonly record struct Position(int Line, int Character)
{
    public static Position Zero => new(0, 0);

    public override string ToString() => $"({Line},{Character})";
}

/// <summary>
/// Rango de texto comprendido entre dos posiciones en un documento según LSP.
/// </summary>
/// <param name="Start">Posición inicial inclusiva.</param>
/// <param name="End">Posición final exclusiva.</param>
public readonly record struct LspRange(Position Start, Position End)
{
    public static LspRange Zero => new(Position.Zero, Position.Zero);

    public bool Contains(Position position)
    {
        if (position.Line < Start.Line || position.Line > End.Line)
        {
            return false;
        }

        if (position.Line == Start.Line && position.Character < Start.Character)
        {
            return false;
        }

        if (position.Line == End.Line && position.Character > End.Character)
        {
            return false;
        }

        return true;
    }

    public override string ToString() => $"[{Start}..{End}]";
}

/// <summary>
/// Ubicación de un rango de código en un archivo URI específico.
/// </summary>
/// <param name="Uri">URI del documento (ej. file:///...).</param>
/// <param name="Range">Rango dentro del documento.</param>
public readonly record struct Location(string Uri, LspRange Range);
