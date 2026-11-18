using System.Reflection;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Encoding;
using FluentASM.Core.Execution;
using FluentASM.Core.Projects;

namespace FluentASM.Compiler;

/// <summary>
/// Interfaz de línea de comandos <c>fluentasm</c>.
/// </summary>
internal static class Cli
{
    private const string BuildCommand = "build";
    private const string RunCommand = "run";
    private const string BootImageCommand = "boot-image";
    private const string InfoCommand = "info";
    private const string MapOption = "--map";
    private const string OutputOption = "--output";
    private const string ShortOutputOption = "-o";
    private const string QemuOption = "--qemu";

    private const string UsageText = """
        fluentasm — compilador de FluentASM (Fase 1, x64 bare metal)

        Uso:
          fluentasm build <ruta.fproj> [--map]  Compila el proyecto y escribe su imagen .x64.
          fluentasm run <ruta.fproj>            Compila y ejecuta la imagen en memoria; imprime Gpr0.
          fluentasm boot-image <ruta.fproj> [-o <ruta.img>] [--qemu]
                                                Empaqueta la imagen en una .img arrancable por QEMU.
          fluentasm info  <ruta.fproj>          Muestra la configuración resuelta del proyecto.
          fluentasm --help | -h                 Muestra esta ayuda.
          fluentasm --version | -v              Muestra la versión del compilador.

        Opciones de build:
          --map    Imprime el mapa de la imagen: secciones, símbolos e instrucciones.

        Opciones de boot-image:
          -o, --output <ruta.img>  Destino de la imagen de arranque (por defecto, junto a la .x64).
          --qemu                   Imprime el comando QEMU que arranca la .img generada.

        Estado actual (M4): léxico, sintaxis, validación Tipo 1/ATM, emisión de la
        imagen plana .x64, ejecución en memoria y arranque bare metal en QEMU.
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
            BuildCommand => RunBuild(args, output, error),
            RunCommand => RunImage(args, output, error),
            BootImageCommand => RunBootImage(args, output, error),
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

    private static int RunBuild(string[] args, TextWriter output, TextWriter error)
    {
        var showMap = false;
        string? path = null;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, MapOption, StringComparison.Ordinal))
            {
                showMap = true;
                continue;
            }

            if (path is not null)
            {
                return UsageError($"El comando '{BuildCommand}' admite una sola ruta a un archivo .fproj.", error);
            }

            path = argument;
        }

        if (path is null)
        {
            return UsageError($"El comando '{BuildCommand}' requiere la ruta a un archivo .fproj.", error);
        }

        var result = FprojReader.Read(path);
        WriteDiagnostics(result.Diagnostics, error);

        if (!result.Success || result.Project is not { } project)
        {
            return CliExitCodes.Diagnostics;
        }

        WriteProjectSummary(project, output);
        output.WriteLine();

        var compilation = ProjectCompiler.Compile(project);
        if (!compilation.Success || compilation.Image is not { } image)
        {
            WriteDiagnostics(compilation.Diagnostics, error);
            output.WriteLine(
                $"Compilación fallida: {compilation.Diagnostics.Count} diagnóstico(s). "
                + "No se escribió ninguna imagen.");
            return CliExitCodes.Diagnostics;
        }

        try
        {
            File.WriteAllBytes(project.OutputPath, image.Image);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"No se pudo escribir '{project.OutputPath}': {exception.Message}");
            return CliExitCodes.Diagnostics;
        }

        output.WriteLine(
            $"Imagen .x64 escrita: {project.OutputPath} ({image.Image.Length} byte(s)).");
        output.WriteLine(
            $"Entrada '{image.Map.EntryPoint}' en el offset 0x{image.Map.EntryOffset:X8}; "
            + $"{image.Map.Symbols.Count} símbolo(s), {image.Map.Instructions.Count} instrucción(es).");

        foreach (var section in image.Map.Sections)
        {
            output.WriteLine(
                $"  {section.Name,-9} 0x{section.Offset:X8}  {section.Size} byte(s)");
        }

        if (showMap)
        {
            output.WriteLine();
            output.Write(image.Map.Format());
        }

        return CliExitCodes.Success;
    }

    private static int RunImage(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2)
        {
            return UsageError(
                $"El comando '{RunCommand}' requiere exactamente una ruta a un archivo .fproj.", error);
        }

        var result = FprojReader.Read(args[1]);
        WriteDiagnostics(result.Diagnostics, error);

        if (!result.Success || result.Project is not { } project)
        {
            return CliExitCodes.Diagnostics;
        }

        if (CompileForExecution(project, error) is not { } image)
        {
            return CliExitCodes.Diagnostics;
        }

        var execution = InMemoryRunner.Run(image.Image, image.Map);
        if (!execution.Success)
        {
            error.WriteLine(execution.Error);
            return CliExitCodes.Diagnostics;
        }

        output.WriteLine(
            $"Ejecutado '{project.Name}' en memoria: {image.Image.Length} byte(s), "
            + $"entrada en el offset 0x{image.Map.EntryOffset:X8}.");
        output.WriteLine($"Gpr0 (RAX) = {execution.ReturnValue} (0x{execution.ReturnValue:X16})");
        return CliExitCodes.Success;
    }

    private static int RunBootImage(string[] args, TextWriter output, TextWriter error)
    {
        string? path = null;
        string? outputPath = null;
        var showQemu = false;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, QemuOption, StringComparison.Ordinal))
            {
                showQemu = true;
                continue;
            }

            if (string.Equals(argument, OutputOption, StringComparison.Ordinal)
                || string.Equals(argument, ShortOutputOption, StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return UsageError($"La opción '{argument}' requiere una ruta de destino.", error);
                }

                if (outputPath is not null)
                {
                    return UsageError("La ruta de destino de la imagen .img se indicó más de una vez.", error);
                }

                outputPath = args[++index];
                continue;
            }

            if (path is not null)
            {
                return UsageError(
                    $"El comando '{BootImageCommand}' admite una sola ruta a un archivo .fproj.", error);
            }

            path = argument;
        }

        if (path is null)
        {
            return UsageError(
                $"El comando '{BootImageCommand}' requiere la ruta a un archivo .fproj.", error);
        }

        var result = FprojReader.Read(path);
        WriteDiagnostics(result.Diagnostics, error);

        if (!result.Success || result.Project is not { } project)
        {
            return CliExitCodes.Diagnostics;
        }

        if (CompileForExecution(project, error) is not { } image)
        {
            return CliExitCodes.Diagnostics;
        }

        var boot = BareMetalBootImageBuilder.Build(image.Image);
        if (!boot.Success)
        {
            error.WriteLine(boot.Error);
            return CliExitCodes.Diagnostics;
        }

        var target = outputPath is null
            ? Path.ChangeExtension(project.OutputPath, ".img")
            : Path.GetFullPath(outputPath);

        try
        {
            File.WriteAllBytes(target, boot.Image);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"No se pudo escribir '{target}': {exception.Message}");
            return CliExitCodes.Diagnostics;
        }

        var stubAddress = boot.Layout.KernelAddress - boot.Layout.StubSize;
        output.WriteLine(
            $"Imagen de arranque escrita: {target} ({boot.Image.Length} byte(s), "
            + $"firma 0x{boot.Image[510]:X2}{boot.Image[511]:X2}).");
        output.WriteLine(
            $"  MBR: {boot.Layout.BootSectorSize} byte(s); stub: {boot.Layout.StubSize} byte(s) en "
            + $"0x{stubAddress:X}; imagen .x64: {boot.Layout.KernelLength} byte(s) en "
            + $"0x{boot.Layout.KernelAddress:X} ({boot.Layout.ChunkCount} bloque(s) de 32 KiB).");

        if (showQemu)
        {
            output.WriteLine();
            output.WriteLine("Ejecuta esta orden para arrancar la imagen en QEMU:");
            output.WriteLine($"  {FormatQemuCommand(target)}");
        }

        return CliExitCodes.Success;
    }

    /// <summary>
    /// Compila el proyecto para ejecutarlo o empaquetarlo. A diferencia de
    /// <c>build</c> no escribe la imagen en disco salvo que el comando lo pida.
    /// </summary>
    private static ImageBuildResult? CompileForExecution(FluentProject project, TextWriter error)
    {
        var compilation = ProjectCompiler.Compile(project);
        WriteDiagnostics(compilation.Diagnostics, error);

        if (!compilation.Success || compilation.Image is not { } image)
        {
            error.WriteLine(
                $"Compilación fallida: {compilation.Diagnostics.Count} diagnóstico(s). "
                + "No se generó ninguna imagen.");
            return null;
        }

        return image;
    }

    private static string FormatQemuCommand(string imagePath)
    {
        var file = imagePath.Contains(',') || imagePath.Contains(' ')
            ? $"\"{imagePath}\""
            : imagePath;
        return $"qemu-system-x86_64 -drive format=raw,file={file},if=ide -serial stdio -display none -no-reboot";
    }

    private static int UsageError(string message, TextWriter error)
    {
        error.WriteLine(message);
        error.WriteLine();
        error.WriteLine(UsageText);
        return CliExitCodes.Usage;
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
