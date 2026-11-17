namespace FluentASM.Core.Diagnostics;

/// <summary>
/// Ubicación física de un diagnóstico dentro de un archivo fuente.
/// </summary>
/// <param name="FilePath">Ruta del archivo, tal como se abrió.</param>
/// <param name="Line">Número de línea, base 1.</param>
/// <param name="Column">Número de columna, base 1.</param>
public sealed record SourceLocation(string FilePath, int Line, int Column)
{
    /// <summary>Representa la ubicación como <c>ruta(linea,columna)</c>.</summary>
    public override string ToString() => $"{FilePath}({Line},{Column})";
}
