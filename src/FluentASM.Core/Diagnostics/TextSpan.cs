namespace FluentASM.Core.Diagnostics;

/// <summary>
/// Intervalo de caracteres dentro de un archivo fuente.
/// </summary>
/// <param name="Start">Índice absoluto del primer carácter, base 0.</param>
/// <param name="Length">Número de caracteres del intervalo.</param>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>Índice absoluto del primer carácter posterior al intervalo.</summary>
    public int End => Start + Length;
}
