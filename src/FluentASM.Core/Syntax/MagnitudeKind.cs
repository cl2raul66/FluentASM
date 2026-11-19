namespace FluentASM.Core.Syntax;

/// <summary>
/// Magnitudes físicas admitidas por el modelo ATM (ver <c>FLUENT_SYNTAX.md</c> §4).
/// </summary>
public enum MagnitudeKind
{
    /// <summary>El token no es una magnitud.</summary>
    None,

    /// <summary><c>Bit</c></summary>
    Bit,

    /// <summary><c>Byte</c></summary>
    Byte,

    /// <summary><c>KiloBit</c></summary>
    KiloBit,

    /// <summary><c>MegaBit</c></summary>
    MegaBit,

    /// <summary><c>GigaBit</c></summary>
    GigaBit,

    /// <summary><c>TeraBit</c></summary>
    TeraBit,

    /// <summary><c>KiloByte</c></summary>
    KiloByte,

    /// <summary><c>MegaByte</c></summary>
    MegaByte,

    /// <summary><c>GigaByte</c></summary>
    GigaByte,

    /// <summary><c>TeraByte</c></summary>
    TeraByte,
}


