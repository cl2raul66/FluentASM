namespace FluentASM.Core.Projects;

/// <summary>
/// Reglas de forma del archivo <c>.fproj</c> en texto simple (Fase 1).
/// </summary>
/// <remarks>
/// Cada línea no vacía es una asignación <c>clave = valor</c>. Los comentarios
/// empiezan con <c>//</c> y llegan hasta el final de la línea. Las claves son
/// sensibles a mayúsculas y minúsculas.
/// </remarks>
public static class FprojSyntax
{
    /// <summary>Clave del nombre del proyecto (obligatoria).</summary>
    public const string NameKey = "name";

    /// <summary>Clave de la arquitectura destino (opcional).</summary>
    public const string ArchitectureKey = "architecture";

    /// <summary>Clave de la rutina de entrada (opcional).</summary>
    public const string EntryPointKey = "entryPoint";

    /// <summary>Clave de un archivo fuente (obligatoria, repetible).</summary>
    public const string SourceKey = "source";

    /// <summary>Clave del nombre de la imagen generada (opcional).</summary>
    public const string OutputKey = "output";

    /// <summary>Inicio de comentario de línea.</summary>
    public const string CommentToken = "//";

    /// <summary>Lista de claves válidas, para mensajes de diagnóstico.</summary>
    public const string ValidKeys = "name, architecture, entryPoint, source, output";

    /// <summary>Rutina de entrada usada cuando el <c>.fproj</c> no la declara.</summary>
    public const string DefaultEntryPoint = "main";

    /// <summary>Extensión de la imagen plana de Fase 1.</summary>
    public const string OutputExtension = ".x64";

    /// <summary>Longitud máxima admitida para un identificador de usuario.</summary>
    public const int MaxIdentifierLength = 64;

    /// <summary>
    /// Verifica que un nombre de usuario sea un identificador camelCase válido:
    /// empieza con letra minúscula y continúa con letras, dígitos o guion bajo.
    /// </summary>
    public static bool IsValidIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxIdentifierLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(value[0]))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}


