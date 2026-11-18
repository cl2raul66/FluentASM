using FluentASM.Core.Diagnostics;
using FluentASM.Core.Encoding;
using FluentASM.Core.Projects;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Compiler;

/// <summary>
/// Compila un proyecto FluentASM completo: léxico, sintaxis, análisis semántico
/// y emisión de la imagen plana <c>.x64</c> (<c>CONTRIBUTING.md</c> §«Pipeline»).
/// </summary>
internal static class ProjectCompiler
{
    /// <summary>
    /// Encadena todas las fases sobre los archivos declarados en el
    /// <c>.fproj</c> y devuelve la imagen resultante con sus diagnósticos.
    /// </summary>
    /// <remarks>
    /// En Fase 1 los archivos fuente comparten una única tabla de símbolos: sus
    /// definiciones de datos y rutinas se combinan en un solo programa antes de
    /// analizar y emitir, en el orden en que aparecen en el <c>.fproj</c>.
    /// </remarks>
    public static CompileResult Compile(FluentProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new DiagnosticBag();
        var parsed = new List<ParsedSource>(project.Sources.Count);

        foreach (var source in project.Sources)
        {
            var text = File.ReadAllText(source.FullPath);
            var parseResult = Parser.Parse(Lexer.Tokenize(text, source.FullPath));
            foreach (var diagnostic in parseResult.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            parsed.Add(new ParsedSource(source, parseResult.Root));
        }

        if (parsed.Count == 0)
        {
            diagnostics.Error(
                DiagnosticIds.ProjectFieldMissing,
                "El proyecto no declara ningún archivo fuente en 'source'.");
            return new CompileResult(null, diagnostics);
        }

        if (diagnostics.HasErrors)
        {
            return new CompileResult(null, diagnostics);
        }

        foreach (var (source, root) in parsed)
        {
            ValidateProjectName(project, source, root, diagnostics);
        }

        if (diagnostics.HasErrors)
        {
            return new CompileResult(null, diagnostics);
        }

        var unit = Combine(parsed);
        var semantics = SemanticAnalyzer.Analyze(unit, diagnostics);
        if (!semantics.Success)
        {
            return new CompileResult(null, diagnostics);
        }

        var image = X64ImageBuilder.Build(unit, semantics, project.EntryPoint, diagnostics);
        return new CompileResult(image.Success ? image : null, diagnostics);
    }

    private static void ValidateProjectName(
        FluentProject project,
        SourceFile source,
        CompilationUnitSyntax root,
        DiagnosticBag diagnostics)
    {
        if (root.ProjectName is not { } name)
        {
            return;
        }

        if (string.Equals(name.Text, project.Name, StringComparison.Ordinal))
        {
            return;
        }

        diagnostics.Error(
            DiagnosticIds.ProjectNameMismatch,
            $"La raíz '{name.Text}' de '{source.RelativePath}' no coincide con el nombre del proyecto '{project.Name}'.",
            name.Location,
            name.Span);
    }

    private static CompilationUnitSyntax Combine(IReadOnlyList<ParsedSource> sources)
    {
        if (sources.Count == 1)
        {
            return sources[0].Root;
        }

        var definitions = sources
            .Where(source => source.Root.Data is not null)
            .SelectMany(source => source.Root.Data!.Definitions)
            .ToList();
        var routines = sources
            .Where(source => source.Root.Execute is not null)
            .SelectMany(source => source.Root.Execute!.Routines)
            .ToList();
        var initialize = sources
            .Select(source => source.Root.Initialize)
            .FirstOrDefault(token => token is not null);
        var span = new TextSpan(
            0,
            sources.Max(source => source.Root.Span.End));

        return new CompilationUnitSyntax(
            sources[0].Root.ProjectName,
            definitions.Count == 0 ? null : new DataSectionSyntax(null, definitions, span),
            new ExecuteSectionSyntax(null, routines, span),
            initialize,
            span);
    }

    private sealed record ParsedSource(SourceFile Source, CompilationUnitSyntax Root);
}

/// <summary>
/// Resultado de compilar un proyecto: imagen plana (si se generó) y todos los
/// diagnósticos emitidos por las fases.
/// </summary>
/// <param name="Image">Imagen plana construida, o <see langword="null"/> si hubo errores.</param>
/// <param name="Diagnostics">Diagnósticos de léxico, sintaxis, semántica y layout.</param>
internal sealed record CompileResult(ImageBuildResult? Image, DiagnosticBag Diagnostics)
{
    /// <summary>Indica si la imagen se generó sin diagnósticos de error.</summary>
    public bool Success => Image is not null && !Diagnostics.HasErrors;
}
