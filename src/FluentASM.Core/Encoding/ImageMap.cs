using System.Globalization;
using System.Text;

namespace FluentASM.Core.Encoding;

/// <summary>Sección de la imagen plana <c>.x64</c>.</summary>
/// <param name="Name">Nombre de la sección (<c>.execute</c> o <c>.data</c>).</param>
/// <param name="Offset">Offset absoluto dentro de la imagen.</param>
/// <param name="Size">Tamaño en bytes.</param>
/// <param name="Fill">Byte de relleno usado al alinear la sección.</param>
public sealed record ImageSection(string Name, int Offset, int Size, byte Fill);

/// <summary>Tipo de símbolo presente en el mapa de la imagen.</summary>
public enum ImageSymbolKind
{
    /// <summary>Rutina de <c>.execute(...)</c>.</summary>
    Routine,

    /// <summary>Etiqueta local de una rutina.</summary>
    Label,

    /// <summary>Símbolo de <c>.data(...)</c>.</summary>
    Data,
}

/// <summary>Símbolo con su ubicación definitiva en la imagen.</summary>
/// <param name="Kind">Tipo de símbolo.</param>
/// <param name="Name">Nombre tal como aparece en el programa.</param>
/// <param name="Section">Sección que lo contiene.</param>
/// <param name="Offset">Offset absoluto dentro de la imagen.</param>
/// <param name="Size">Tamaño reservado en bytes (0 para etiquetas).</param>
public sealed record ImageSymbol(ImageSymbolKind Kind, string Name, string Section, int Offset, int Size);

/// <summary>Instrucción máquina con su offset definitivo dentro de la imagen.</summary>
/// <param name="Offset">Offset absoluto dentro de la imagen.</param>
/// <param name="Bytes">Bytes finales, ya relocalizados.</param>
/// <param name="Text">Instrucción equivalente legible.</param>
/// <param name="Routine">Rutina que la contiene.</param>
/// <param name="Label">Última etiqueta local anterior, si existe.</param>
public sealed record ImageInstruction(
    int Offset,
    byte[] Bytes,
    string Text,
    string Routine,
    string? Label);

/// <summary>
/// Mapa legible de la imagen plana: secciones, símbolos e instrucciones con
/// offsets y bytes exactos.
/// </summary>
public sealed class ImageMap
{
    internal ImageMap(
        string entryPoint,
        int entryOffset,
        IReadOnlyList<ImageSection> sections,
        IReadOnlyList<ImageSymbol> symbols,
        IReadOnlyList<ImageInstruction> instructions)
    {
        EntryPoint = entryPoint;
        EntryOffset = entryOffset;
        Sections = sections;
        Symbols = symbols;
        Instructions = instructions;
    }

    /// <summary>Mapa vacío usado cuando la imagen no pudo construirse.</summary>
    public static ImageMap Empty { get; } = new(string.Empty, 0, [], [], []);

    /// <summary>Rutina de entrada configurada.</summary>
    public string EntryPoint { get; }

    /// <summary>Offset absoluto de la rutina de entrada.</summary>
    public int EntryOffset { get; }

    /// <summary>Secciones de la imagen en orden de aparición.</summary>
    public IReadOnlyList<ImageSection> Sections { get; }

    /// <summary>Símbolos en orden de declaración (rutinas, etiquetas y datos).</summary>
    public IReadOnlyList<ImageSymbol> Symbols { get; }

    /// <summary>Instrucciones en orden de aparición.</summary>
    public IReadOnlyList<ImageInstruction> Instructions { get; }

    /// <summary>Genera el reporte legible que imprime la CLI y el Sample.</summary>
    public string Format()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Entrada: {EntryPoint} @ 0x{EntryOffset:X8}");
        builder.AppendLine();
        builder.AppendLine("Secciones:");
        foreach (var section in Sections)
        {
            builder.AppendLine(
                $"  {section.Name,-9} offset 0x{section.Offset:X8}  tamaño {section.Size,6} byte(s)  relleno 0x{section.Fill:X2}");
        }

        builder.AppendLine();
        builder.AppendLine("Símbolos:");
        foreach (var symbol in Symbols)
        {
            builder.AppendLine(
                $"  {DescribeKind(symbol.Kind),-9} {symbol.Name,-24} {symbol.Section,-9} 0x{symbol.Offset:X8}  {symbol.Size} byte(s)");
        }

        builder.AppendLine();
        builder.AppendLine("Instrucciones:");
        foreach (var instruction in Instructions)
        {
            var bytes = string.Join(' ', instruction.Bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
            var label = instruction.Label is null ? string.Empty : $"  ; {instruction.Label}";
            builder.AppendLine(
                $"  {instruction.Offset:X8}  {bytes,-26} {instruction.Text}{label}");
        }

        return builder.ToString();
    }

    private static string DescribeKind(ImageSymbolKind kind) => kind switch
    {
        ImageSymbolKind.Routine => "rutina",
        ImageSymbolKind.Label => "etiqueta",
        ImageSymbolKind.Data => "data",
        _ => "símbolo",
    };
}


