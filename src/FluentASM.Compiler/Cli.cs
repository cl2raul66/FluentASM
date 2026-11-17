using System.Reflection;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Projects;

namespace FluentASM.Compiler;

/// <summary>
/// Interfaz de línea de comandos <c>fluentasm</c>.
/// </summary>
internal static class Cli
{
    private const string BuildCommand = "build";
    private const string InfoCommand = "info";

    private const string UsageText = """
        fluentasm — compilador de FluentASM (Fase 1, x64 bare metal)

        Uso:
          fluentasm build <ruta.fproj>    Valida el proyecto y prepara la compilación.
          fluentasm info  <ruta.fproj>    Muestra la configuración resuelta del proyecto.
          fluentasm --help | -h           Muestra esta ayuda.
          fluentasm --version | -v        Muestra la versión del compilador.

        Estado actual (M0): lectura y validación del archivo .fproj en texto simple.
        El análisis de sourcesCode.fasm llega en M1 y la imagen .x64 en M3.
        """;

    /// <summary>Ejecuta la CLI y devuelve el código de salida del proceso.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            error.WriteLine(UsageText);
            return CliExitCodes.Usage;
        }

        return args[0] switch
        {
            "--help" or "-h" => ShowHelp(output),
            "--version" or "-v" => ShowVersion(output),
            BuildCommand => RunProjectCommand(args, output, error, Build),
            InfoCommand => RunProjectCommand(args, output, error, Info),
            _ => UnknownCommand(args[0], error),
        };
    }

    private static int ShowHelp(TextWriter output)
    {
        output.WriteLine(UsageText);
        return CliExitCodes.Success;
    }

    private static int ShowVersion(TextWriter output)
    {
        output.WriteLine($"fluentasm {GetVersion()}");
        return CliExitCodes.Success;
    }

    private static int UnknownCommand(string command, TextWriter error)
    {
        error.WriteLine($"Comando desconocido '{command}'.");
        error.WriteLine();
        error.WriteLine(UsageText);
        return CliExitCodes.Usage;
    }

    private static int RunProjectCommand(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<FluentProject, TextWriter, int> handler)
    {
        if (args.Length != 2)
        {
            error.WriteLine($"El comando '{args[0]}' requiere exactamente una ruta a un archivo .fproj.");
            error.WriteLine();
            error.WriteLine(UsageText);
            return CliExitCodes.Usage;
        }

        var result = FprojReader.Read(args[1]);
        WriteDiagnostics(result.Diagnostics, error);

        if (!result.Success || result.Project is not { } project)
        {
            return CliExitCodes.Diagnostics;
        }

        return handler(project, output);
    }

    private static int Build(FluentProject project, TextWriter output)
    {
        WriteProjectSummary(project, output);
        output.WriteLine();
        output.WriteLine(
            "M0: el archivo .fproj es válido y sus fuentes existen. "
            + "El análisis sintáctico comienza en M1 y la imagen .x64 en M3.");
        return CliExitCodes.Success;
    }

    private static int Info(FluentProject project, TextWriter output)
    {
        WriteProjectSummary(project, output);
        return CliExitCodes.Success;
    }

    private static void WriteProjectSummary(FluentProject project, TextWriter output)
    {
        output.WriteLine($"name:         {project.Name}");
        output.WriteLine($"architecture: {project.Architecture.ToCanonicalName()}");
        output.WriteLine($"entryPoint:   {project.EntryPoint}");
        output.WriteLine($"output:       {project.OutputPath}");

        foreach (var source in project.Sources)
        {
            var state = source.Exists ? "ok" : "falta";
            output.WriteLine($"source:       {source.RelativePath} -> {source.FullPath} [{state}]");
        }
    }

    private static void WriteDiagnostics(DiagnosticBag diagnostics, TextWriter error)
    {
        foreach (var diagnostic in diagnostics)
        {
            error.WriteLine(diagnostic);
        }
    }

    private static string GetVersion()
    {
        var informationalVersion = typeof(Cli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "0.0.0-local";
        }

        var metadataIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex < 0
            ? informationalVersion
            : informationalVersion[..metadataIndex];
    }
}
