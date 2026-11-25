using FluentASM.Lsp.Protocol;

namespace FluentASM.Lsp.Formatting;

/// <summary>
/// Acumula ediciones de formateo y las devuelve ordenadas por posición, sin
/// solapamientos y descartando las que no cambian nada.
/// </summary>
/// <remarks>
/// El motor genera como máximo una edición por línea más las de fin de línea;
/// aun así el constructor valida el orden porque el IDE aplica el lote de mayor
/// a menor offset y un solapamiento corrompería el texto.
/// </remarks>
public sealed class TextEditBuilder
{
    private readonly List<TextEdit> edits = [];

    /// <summary>Número de ediciones acumuladas antes de normalizar.</summary>
    public int Count => edits.Count;

    /// <summary>Agrega una edición; las ediciones vacías sobre rangos vacíos se ignoran.</summary>
    public void Add(LspRange range, string newText)
    {
        if (string.IsNullOrEmpty(newText) && range.Start == range.End)
        {
            return;
        }

        edits.Add(new TextEdit(range, newText));
    }

    /// <summary>Devuelve las ediciones ordenadas y sin solapamientos.</summary>
    public IReadOnlyList<TextEdit> Build()
    {
        if (edits.Count == 0)
        {
            return Array.Empty<TextEdit>();
        }

        edits.Sort(CompareEdits);

        var result = new List<TextEdit>(edits.Count);
        foreach (TextEdit edit in edits)
        {
            if (result.Count > 0 &&
                ComparePositions(edit.Range.Start, result[^1].Range.End) < 0)
            {
                // Defensivo: nunca se devuelve un lote solapado. Como el motor
                // trabaja por líneas disjuntas, esta rama no debería alcanzarse.
                continue;
            }

            result.Add(edit);
        }

        return result;
    }

    private static int CompareEdits(TextEdit left, TextEdit right)
    {
        int byStart = ComparePositions(left.Range.Start, right.Range.Start);
        return byStart != 0
            ? byStart
            : ComparePositions(left.Range.End, right.Range.End);
    }

    private static int ComparePositions(Position left, Position right)
    {
        int byLine = left.Line.CompareTo(right.Line);
        return byLine != 0 ? byLine : left.Character.CompareTo(right.Character);
    }
}
