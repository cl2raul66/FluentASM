using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Medida física resuelta del modelo ATM (<c>Bit:N</c>, <c>Byte:N</c> y sus
/// múltiplos binarios) con el total de bits ya calculado.
/// </summary>
/// <param name="Kind">Unidad declarada (<c>Bit</c>, <c>KiloByte</c>, ...).</param>
/// <param name="Size">Cantidad declarada antes del prefijo binario.</param>
/// <param name="Bits">Total de bits que representa la medida.</param>
public readonly record struct MagnitudeInfo(MagnitudeKind Kind, long Size, long Bits)
{
    /// <summary>Total de bytes; solo tiene sentido si <see cref="IsByteAligned"/> es cierto.</summary>
    public long Bytes => Bits / 8;

    /// <summary>Indica si la medida ocupa bytes completos.</summary>
    public bool IsByteAligned => Bits % 8 == 0;

    /// <summary>Indica si la unidad mide bits.</summary>
    public bool IsBit => IsBitUnit(Kind);

    /// <summary>Indica si la unidad mide bytes.</summary>
    public bool IsByte => Kind is
        MagnitudeKind.Byte
        or MagnitudeKind.KiloByte
        or MagnitudeKind.MegaByte
        or MagnitudeKind.GigaByte
        or MagnitudeKind.TeraByte;

    /// <summary>Potencia de 1024 aplicada por el prefijo binario.</summary>
    public int BinaryPower => Kind switch
    {
        MagnitudeKind.KiloBit or MagnitudeKind.KiloByte => 1,
        MagnitudeKind.MegaBit or MagnitudeKind.MegaByte => 2,
        MagnitudeKind.GigaBit or MagnitudeKind.GigaByte => 3,
        MagnitudeKind.TeraBit or MagnitudeKind.TeraByte => 4,
        _ => 0,
    };

    /// <summary>Representación canónica de la medida.</summary>
    public string Display => $"{Kind}:{Size}";

    /// <summary>
    /// Resuelve una magnitud sintáctica a su medida física, rechazando tamaños
    /// no positivos o que desborden la representación.
    /// </summary>
    public static bool TryCreate(MagnitudeSyntax syntax, out MagnitudeInfo info)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        info = default;
        var kind = syntax.Unit.Magnitude;
        if (kind == MagnitudeKind.None || syntax.Size.IntegerValue <= 0)
        {
            return false;
        }

        var multiplier = 1L << (10 * BinaryPowerOf(kind));
        var size = syntax.Size.IntegerValue;
        if (size > long.MaxValue / multiplier)
        {
            return false;
        }

        var units = size * multiplier;
        var bitsPerUnit = IsBitUnit(kind) ? 1 : 8;
        if (units > long.MaxValue / bitsPerUnit)
        {
            return false;
        }

        info = new MagnitudeInfo(kind, size, units * bitsPerUnit);
        return true;
    }

    private static bool IsBitUnit(MagnitudeKind kind) => kind is
        MagnitudeKind.Bit
        or MagnitudeKind.KiloBit
        or MagnitudeKind.MegaBit
        or MagnitudeKind.GigaBit
        or MagnitudeKind.TeraBit;

    private static int BinaryPowerOf(MagnitudeKind kind) => kind switch
    {
        MagnitudeKind.KiloBit or MagnitudeKind.KiloByte => 1,
        MagnitudeKind.MegaBit or MagnitudeKind.MegaByte => 2,
        MagnitudeKind.GigaBit or MagnitudeKind.GigaByte => 3,
        MagnitudeKind.TeraBit or MagnitudeKind.TeraByte => 4,
        _ => 0,
    };
}
