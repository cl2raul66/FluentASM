namespace FluentASM.Core.Encoding;

/// <summary>
/// Naturaleza de una referencia pendiente de resolver en una instrucción.
/// </summary>
public enum InstructionFixupKind
{
    /// <summary>Desplazamiento relativo al final de la instrucción (<c>rel8</c> o <c>rel32</c>).</summary>
    Relative,

    /// <summary>Desplazamiento de una dirección RIP-relativa (<c>disp32</c>).</summary>
    RipRelative,
}

/// <summary>Tipo de símbolo al que apunta una referencia pendiente.</summary>
public enum InstructionFixupTargetKind
{
    /// <summary>Etiqueta local de la rutina (<c>@nombre</c>).</summary>
    LocalLabel,

    /// <summary>Rutina de <c>.execute(...)</c>.</summary>
    Routine,

    /// <summary>Símbolo declarado en <c>.data(...)</c>.</summary>
    DataSymbol,
}

/// <summary>Símbolo destino de una referencia pendiente.</summary>
/// <param name="Kind">Espacio de nombres del símbolo.</param>
/// <param name="Name">Nombre exacto tal como aparece en el AST.</param>
public sealed record InstructionFixupTarget(InstructionFixupTargetKind Kind, string Name);

/// <summary>
/// Referencia pendiente dentro de los bytes de una instrucción. El layout
/// rellena el hueco cuando conoce los offsets definitivos.
/// </summary>
/// <param name="ByteOffset">Índice del primer byte del hueco dentro de la instrucción.</param>
/// <param name="Size">Tamaño del hueco en bytes (1 o 4).</param>
/// <param name="Kind">Naturaleza de la referencia.</param>
/// <param name="Target">Símbolo destino.</param>
/// <param name="Addend">Desplazamiento sumado al destino antes de calcular la referencia.</param>
public sealed record InstructionFixup(
    int ByteOffset,
    int Size,
    InstructionFixupKind Kind,
    InstructionFixupTarget Target,
    long Addend);

/// <summary>Forma de un salto relativo, necesaria para la relajación iterativa.</summary>
public enum BranchForm
{
    /// <summary>La instrucción no es un salto relajable.</summary>
    None,

    /// <summary>Salto corto (<c>rel8</c>) que puede promoverse a cercano.</summary>
    Short,

    /// <summary>Salto cercano (<c>rel32</c>).</summary>
    Near,

    /// <summary><c>LOOP rel8</c>, que no admite forma cercana.</summary>
    Loop,
}

/// <summary>
/// Instrucción x64 ya codificada, con sus referencias pendientes y un texto
/// equivalente legible para el mapa de compilación.
/// </summary>
/// <param name="Bytes">Bytes exactos en orden <em>little-endian</em>.</param>
/// <param name="Text">Instrucción equivalente legible.</param>
/// <param name="Fixups">Referencias pendientes de resolver.</param>
/// <param name="Branch">Forma del salto, si la instrucción lo es.</param>
public sealed record EncodedInstruction(
    byte[] Bytes,
    string Text,
    IReadOnlyList<InstructionFixup> Fixups,
    BranchForm Branch = BranchForm.None);
