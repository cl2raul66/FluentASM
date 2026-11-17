namespace FluentASM.Core.Projects;

/// <summary>
/// Nombres canónicos de las arquitecturas soportadas.
/// El lenguaje es sensible a mayúsculas: el nombre válido es exactamente <c>x64</c>.
/// </summary>
public static class ArchitectureNames
{
    /// <summary>Nombre canónico de la arquitectura x86-64.</summary>
    public const string X64 = "x64";

    /// <summary>Arquitecturas admitidas en Fase 1, para mensajes de diagnóstico.</summary>
    public const string SupportedList = X64;

    /// <summary>Intenta interpretar un nombre de arquitectura respetando mayúsculas y minúsculas.</summary>
    public static bool TryParse(string? text, out Architecture architecture)
    {
        if (string.Equals(text, X64, StringComparison.Ordinal))
        {
            architecture = Architecture.X64;
            return true;
        }

        architecture = default;
        return false;
    }

    /// <summary>Devuelve el nombre canónico de una arquitectura.</summary>
    public static string ToCanonicalName(this Architecture architecture) => architecture switch
    {
        Architecture.X64 => X64,
        _ => throw new ArgumentOutOfRangeException(
            nameof(architecture),
            architecture,
            "Arquitectura desconocida."),
    };
}
