namespace FluentASM.Core.Syntax;

/// <summary>
/// Nombres canónicos de las magnitudes físicas. Sensible a mayúsculas y minúsculas.
/// </summary>
public static class MagnitudeNames
{
    /// <summary>
    /// Intenta interpretar una palabra como unidad de medida física válida.
    /// </summary>
    public static bool TryParse(string? text, out MagnitudeKind magnitude)
    {
        switch (text)
        {
            case "Bit":
                magnitude = MagnitudeKind.Bit;
                return true;
            case "Byte":
                magnitude = MagnitudeKind.Byte;
                return true;
            case "KiloBit":
                magnitude = MagnitudeKind.KiloBit;
                return true;
            case "MegaBit":
                magnitude = MagnitudeKind.MegaBit;
                return true;
            case "GigaBit":
                magnitude = MagnitudeKind.GigaBit;
                return true;
            case "TeraBit":
                magnitude = MagnitudeKind.TeraBit;
                return true;
            case "KiloByte":
                magnitude = MagnitudeKind.KiloByte;
                return true;
            case "MegaByte":
                magnitude = MagnitudeKind.MegaByte;
                return true;
            case "GigaByte":
                magnitude = MagnitudeKind.GigaByte;
                return true;
            case "TeraByte":
                magnitude = MagnitudeKind.TeraByte;
                return true;
            default:
                magnitude = MagnitudeKind.None;
                return false;
        }
    }
}


