using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FluentASM.Lsp.Formatting;

/// <summary>
/// Resuelve las opciones de formateo leyendo los archivos <c>.editorconfig</c>
/// desde el directorio del archivo hacia arriba, respetando <c>root = true</c>,
/// el orden de las secciones y la precedencia del archivo más cercano.
/// </summary>
/// <remarks>
/// La implementación no depende de WinUI ni de <c>Microsoft.UI.*</c>: puede
/// ejecutarse en el host JSON-RPC de la Fase 3. Los archivos se cachean por
/// ruta y se invalidan cuando cambian su fecha de modificación o su tamaño.
/// Cualquier error de lectura se traduce en opciones por defecto más un aviso
/// <c>FASM1D01</c>; nunca se lanza una excepción hacia el llamador.
/// </remarks>
public sealed class EditorConfigResolver
{
    private readonly ConcurrentDictionary<string, ConfigFile> cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resuelve las opciones para el archivo indicado. Si <paramref name="filePath"/>
    /// es <c>null</c>, se parte del directorio de trabajo actual.
    /// </summary>
    public ResolvedFormattingOptions Resolve(string? filePath)
    {
        try
        {
            return ResolveCore(filePath);
        }
        catch (Exception ex)
        {
            return new ResolvedFormattingOptions(
                FormattingOptions.Default,
                "FASM1D01",
                $"No se pudo resolver .editorconfig: {ex.Message}");
        }
    }

    private ResolvedFormattingOptions ResolveCore(string? filePath)
    {
        (string fullPath, string directory) = NormalizeTarget(filePath);

        // Se recorre del directorio más cercano hacia la raíz; luego se aplican
        // los archivos de fuera hacia dentro para que el más cercano gane.
        var chain = new List<(string Directory, ConfigFile Config)>();
        string? current = directory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, ".editorconfig");
            if (File.Exists(candidate))
            {
                ConfigFile config = Load(candidate);
                chain.Add((current, config));
                if (config.Root)
                {
                    break;
                }
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        var options = FormattingOptions.Default;
        string? warningCode = null;
        string? warningMessage = null;

        foreach ((string configDirectory, ConfigFile config) in chain.AsEnumerable().Reverse())
        {
            if (config.Problem is { Length: > 0 } problem && warningCode is null)
            {
                warningCode = "FASM1D01";
                warningMessage = problem;
            }

            string relativePath = Path.GetRelativePath(configDirectory, fullPath)
                .Replace('\\', '/');
            string fileName = Path.GetFileName(fullPath);

            foreach (ConfigSection section in config.Sections)
            {
                string subject = section.MatchPath ? relativePath : fileName;
                if (!section.Pattern.IsMatch(subject))
                {
                    continue;
                }

                foreach ((string key, string value) in section.Properties)
                {
                    options = Apply(options, key, value);
                }
            }
        }

        return new ResolvedFormattingOptions(options, warningCode, warningMessage);
    }

    private static (string FullPath, string Directory) NormalizeTarget(string? filePath)
    {
        string fallbackName = "document.fasm";
        string currentDirectory = Directory.GetCurrentDirectory();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return (Path.Combine(currentDirectory, fallbackName), currentDirectory);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception)
        {
            fullPath = Path.Combine(currentDirectory, Path.GetFileName(filePath) ?? fallbackName);
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            // URIs como file:///nombre.fasm producen raíces inexistentes en
            // entornos de prueba; se usa el directorio de trabajo como ancla.
            directory = currentDirectory;
            fullPath = Path.Combine(directory, Path.GetFileName(filePath) ?? fallbackName);
        }

        return (fullPath, directory);
    }

    private ConfigFile Load(string path)
    {
        DateTime lastWrite;
        long length;
        try
        {
            var info = new FileInfo(path);
            lastWrite = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch (Exception ex)
        {
            return new ConfigFile(
                DateTime.MinValue,
                0,
                Root: false,
                Array.Empty<ConfigSection>(),
                $"No se pudo leer '{path}': {ex.Message}");
        }

        if (cache.TryGetValue(path, out ConfigFile? cached) &&
            cached.LastWriteTimeUtc == lastWrite &&
            cached.Length == length)
        {
            return cached;
        }

        ConfigFile parsed;
        try
        {
            string? problem = null;
            string content = File.ReadAllText(path);
            parsed = Parse(path, content, lastWrite, length, ref problem);
        }
        catch (Exception ex)
        {
            parsed = new ConfigFile(
                lastWrite,
                length,
                Root: false,
                Array.Empty<ConfigSection>(),
                $"No se pudo leer '{path}': {ex.Message}");
        }

        cache[path] = parsed;
        return parsed;
    }

    private static ConfigFile Parse(
        string path,
        string content,
        DateTime lastWrite,
        long length,
        ref string? problem)
    {
        bool root = false;
        var sections = new List<ConfigSection>();
        string? currentPattern = null;
        Dictionary<string, string>? currentProperties = null;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']') && line.Length >= 2)
            {
                if (currentPattern is not null && currentProperties is not null)
                {
                    sections.Add(CreateSection(currentPattern, currentProperties));
                }

                currentPattern = line[1..^1].Trim();
                currentProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                problem ??= $"Línea no reconocida en '{path}': {line}";
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (currentPattern is null || currentProperties is null)
            {
                if (key.Equals("root", StringComparison.OrdinalIgnoreCase) &&
                    bool.TryParse(value, out bool isRoot))
                {
                    root = isRoot;
                }

                continue;
            }

            currentProperties[key] = value;
        }

        if (currentPattern is not null && currentProperties is not null)
        {
            sections.Add(CreateSection(currentPattern, currentProperties));
        }

        return new ConfigFile(lastWrite, length, root, sections, problem);
    }

    private static ConfigSection CreateSection(string pattern, Dictionary<string, string> properties)
    {
        bool matchPath = pattern.Contains('/');
        string normalized = pattern.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return new ConfigSection(
            BuildRegex(normalized),
            matchPath,
            properties);
    }

    private static Regex BuildRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        for (int index = 0; index < pattern.Length; index++)
        {
            char current = pattern[index];
            switch (current)
            {
                case '*':
                    if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                    {
                        builder.Append(".*");
                        index++;
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }

                    break;

                case '?':
                    builder.Append("[^/]");
                    break;

                case '{':
                    int braceEnd = pattern.IndexOf('}', index + 1);
                    if (braceEnd > index)
                    {
                        string[] alternatives = pattern[(index + 1)..braceEnd].Split(',');
                        builder.Append("(?:");
                        builder.Append(string.Join("|", alternatives.Select(Regex.Escape)));
                        builder.Append(')');
                        index = braceEnd;
                    }
                    else
                    {
                        builder.Append("\\{");
                    }

                    break;

                case '[':
                    int bracketEnd = pattern.IndexOf(']', index + 1);
                    if (bracketEnd > index)
                    {
                        builder.Append('[');
                        builder.Append(pattern[(index + 1)..bracketEnd]);
                        builder.Append(']');
                        index = bracketEnd;
                    }
                    else
                    {
                        builder.Append("\\[");
                    }

                    break;

                case ']':
                    builder.Append("\\]");
                    break;

                default:
                    builder.Append(Regex.Escape(current.ToString()));
                    break;
            }
        }

        builder.Append('$');

        RegexOptions options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows())
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(builder.ToString(), options);
    }

    private static FormattingOptions Apply(FormattingOptions options, string key, string value)
    {
        if (value.Length == 0 || value.Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            return options;
        }

        switch (key.ToLowerInvariant())
        {
            case "indent_style":
                if (value.Equals("tab", StringComparison.OrdinalIgnoreCase))
                {
                    return options with { IndentStyle = IndentStyleKind.Tab };
                }

                if (value.Equals("space", StringComparison.OrdinalIgnoreCase))
                {
                    return options with { IndentStyle = IndentStyleKind.Space };
                }

                return options;

            case "indent_size":
                if (value.Equals("tab", StringComparison.OrdinalIgnoreCase))
                {
                    return options;
                }

                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) &&
                    size is > 0 and <= 16)
                {
                    return options with { IndentSize = size };
                }

                return options;

            case "insert_final_newline":
                return bool.TryParse(value, out bool insertFinal)
                    ? options with { InsertFinalNewline = insertFinal }
                    : options;

            case "trim_trailing_whitespace":
                return bool.TryParse(value, out bool trim)
                    ? options with { TrimTrailingWhitespace = trim }
                    : options;

            case "end_of_line":
                if (value.Equals("lf", StringComparison.OrdinalIgnoreCase))
                {
                    return options with { EndOfLine = EndOfLineKind.Lf };
                }

                if (value.Equals("crlf", StringComparison.OrdinalIgnoreCase))
                {
                    return options with { EndOfLine = EndOfLineKind.CrLf };
                }

                if (value.Equals("cr", StringComparison.OrdinalIgnoreCase))
                {
                    return options with { EndOfLine = EndOfLineKind.Cr };
                }

                return options;

            default:
                return options;
        }
    }

    private sealed record ConfigFile(
        DateTime LastWriteTimeUtc,
        long Length,
        bool Root,
        IReadOnlyList<ConfigSection> Sections,
        string? Problem);

    private sealed record ConfigSection(
        Regex Pattern,
        bool MatchPath,
        IReadOnlyDictionary<string, string> Properties);
}
