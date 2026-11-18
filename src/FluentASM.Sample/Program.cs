using System.Text;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;

namespace FluentASM.Sample;

/// <summary>
/// Banco de pruebas manual de FluentASM. Ejecuta el lexer y el parser de M1 sobre
/// los tres ejemplos completos de <c>FLUENT_SYNTAX_X64.md</c> §7, sobre un ejemplo
/// con error sintáctico intencional y, opcionalmente, sobre un archivo indicado
/// por ruta.
/// </summary>
internal static class Program
{
    private const string ErrorSample = "errorSintactico.fasm";

    private static readonly (string Section, string File)[] ValidSamples =
    [
        ("7.1", "miCalculadoraCli.fasm"),
        ("7.2", "bucleDemo.fasm"),
        ("7.3", "serialBareMetal.fasm"),
    ];

    private static int Main(string[] args)
    {
        if (args.Length > 1)
        {
            Console.Error.WriteLine("Uso: FluentASM.Sample [ruta.fasm]");
            Console.Error.WriteLine("  Sin argumentos valida los ejemplos de FLUENT_SYNTAX_X64.md §7 y el error intencional.");
            return 2;
        }

        return args.Length == 1
            ? AnalyzeFile(Path.GetFullPath(args[0]), showTokens: true)
            : RunSuite();
    }

    private static int RunSuite()
    {
        var directory = ResolveSamplesDirectory();
        if (directory is null)
        {
            Console.Error.WriteLine(
                "No se encontró el directorio 'samples' junto al ejecutable ni en el árbol del repositorio.");
            return 2;
        }

        Console.WriteLine("FluentASM.Sample — validación de M1 (lexer + parser)");
        Console.WriteLine($"Ejemplos: {directory}");

        var validFailures = 0;
        foreach (var (section, file) in ValidSamples)
        {
            WriteSeparator($"{section} válido", file);
            if (AnalyzeFile(Path.Combine(directory, file), showTokens: false) != 0)
            {
                validFailures++;
            }
        }

        WriteSeparator("error esperado", ErrorSample);
        var errorFailures = CheckIntentionalError(Path.Combine(directory, ErrorSample));

        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        if (validFailures == 0 && errorFailures == 0)
        {
            Console.WriteLine(
                $"RESUMEN: {ValidSamples.Length}/{ValidSamples.Length} ejemplos válidos con 0 diagnósticos; "
                + "1/1 ejemplo con error detectado y posición exacta.");
            return 0;
        }

        Console.WriteLine(
            $"RESUMEN: {validFailures} ejemplo(s) válido(s) con diagnósticos inesperados y "
            + $"{errorFailures} fallo(s) en el ejemplo con error intencional.");
        return 1;
    }

    private static int AnalyzeFile(string path, bool showTokens)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No se encontró el archivo '{path}'.");
            return 2;
        }

        var text = File.ReadAllText(path);
        var lexResult = Lexer.Tokenize(text, path);
        var parseResult = Parser.Parse(lexResult);
        var semanticResult = SemanticAnalyzer.Analyze(parseResult.Root, parseResult.Diagnostics);
        var errors = CountErrors(parseResult);

        Console.WriteLine($"Archivo: {path}");
        Console.WriteLine();

        if (showTokens)
        {
            foreach (var token in lexResult.Tokens)
            {
                Console.WriteLine(
                    $"{token.Location.Line,4}:{token.Location.Column,-4} {token.Kind,-13} {Describe(token)}");
            }

            Console.WriteLine();
        }

        Console.WriteLine(
            $"Resumen AST: proyecto '{parseResult.Root.ProjectName?.Text ?? "<sin nombre>"}', "
            + $"{parseResult.Root.Data?.Definitions.Count ?? 0} dato(s), "
            + $"{parseResult.Root.Execute?.Routines.Count ?? 0} rutina(s), "
            + (parseResult.Root.Initialize is null ? "sin .initialize()." : "'.initialize()' presente."));
        Console.WriteLine();
        Console.WriteLine("AST:");
        Console.WriteLine(AstDumper.Dump(parseResult.Root));
        Console.WriteLine();
        WriteSemanticSummary(semanticResult);
        Console.WriteLine();
        Console.WriteLine(
            $"{lexResult.Tokens.Count} tokens, {parseResult.Diagnostics.Count} diagnósticos ({errors} errores).");

        foreach (var diagnostic in parseResult.Diagnostics)
        {
            Console.Error.WriteLine(Format(diagnostic));
        }

        if (errors == 0)
        {
            Console.WriteLine("RESULTADO: OK — AST generado sin errores.");
            return 0;
        }

        Console.WriteLine($"RESULTADO: FALLO — {errors} error(es) de sintaxis o léxico.");
        return 1;
    }

    private static int CheckIntentionalError(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No se encontró el archivo '{path}'.");
            return 1;
        }

        var text = File.ReadAllText(path);
        var parseResult = Parser.Parse(Lexer.Tokenize(text, path));
        _ = SemanticAnalyzer.Analyze(parseResult.Root, parseResult.Diagnostics);
        var errors = parseResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Console.WriteLine($"Archivo: {path}");
        Console.WriteLine();
        Console.WriteLine("AST parcial:");
        Console.WriteLine(AstDumper.Dump(parseResult.Root));
        Console.WriteLine();
        Console.WriteLine($"{parseResult.Diagnostics.Count} diagnósticos ({errors.Count} errores).");

        foreach (var diagnostic in parseResult.Diagnostics)
        {
            Console.WriteLine(Format(diagnostic));
        }

        var syntaxError = errors.FirstOrDefault(
            diagnostic => diagnostic.Id.StartsWith("FASM1", StringComparison.Ordinal));

        if (syntaxError is null)
        {
            Console.WriteLine("RESULTADO: FALLO — no se emitió ningún diagnóstico FASM1xxx.");
            return 1;
        }

        if (syntaxError.Span is not { Length: > 0 } span)
        {
            Console.WriteLine("RESULTADO: FALLO — el diagnóstico no tiene un span válido.");
            return 1;
        }

        if (syntaxError.Location is not { } location)
        {
            Console.WriteLine("RESULTADO: FALLO — el diagnóstico no tiene posición.");
            return 1;
        }

        var expected = ComputePosition(text, span.Start);
        Console.WriteLine();
        WriteCaret(text, location, span);
        Console.WriteLine(
            $"Diagnóstico: {syntaxError.Id} en línea {location.Line}, columna {location.Column}, "
            + $"span {span.Start}..{span.End} (longitud {span.Length}).");

        if (expected != (location.Line, location.Column))
        {
            Console.WriteLine(
                $"RESULTADO: FALLO — el span empieza en línea {expected.Line}, columna {expected.Column}.");
            return 1;
        }

        if (parseResult.Root.ProjectName is null
            || parseResult.Root.Execute is null
            || parseResult.Root.Initialize is null)
        {
            Console.WriteLine("RESULTADO: FALLO — el AST parcial perdió el nombre, .execute(...) o .initialize().");
            return 1;
        }

        Console.WriteLine(
            "RESULTADO: OK — error detectado con posición exacta y AST parcial utilizable.");
        return 0;
    }

    private static void WriteSemanticSummary(SemanticResult result)
    {
        var symbols = result.Symbols;
        Console.WriteLine("Resumen semántico:");
        Console.WriteLine(
            $"  Símbolos .data: {symbols.DataSymbols.Count}, rutinas: {symbols.Routines.Count}, "
            + $"etiquetas locales: {symbols.LabelCount}.");

        foreach (var data in symbols.DataSymbols)
        {
            Console.WriteLine(
                $"  data {data.Name}: capacidad {data.Capacity.Display} ({data.BitSize} bits, {data.ByteSize} bytes).");
        }

        foreach (var routine in symbols.Routines)
        {
            var parameters = routine.Parameters.Count == 0
                ? "sin parámetros"
                : string.Join(
                    ", ",
                    routine.Parameters.Select(parameter => $"{parameter.Name} -> Gpr{parameter.RegisterIndex}"));
            var returns = routine.Returns is { } returnsReference
                ? returnsReference.Display
                : "sin retorno declarado";
            Console.WriteLine($"  rutina {routine.DisplayName}: {parameters}; retorno {returns}.");
        }

        Console.WriteLine(
            $"  Diagnósticos semánticos: {result.Diagnostics.Count} "
            + $"({(result.Success ? "0 errores" : "con errores")}).");
    }

    private static string? ResolveSamplesDirectory()
    {
        var besideExecutable = Path.Combine(AppContext.BaseDirectory, "samples");
        if (Directory.Exists(besideExecutable))
        {
            return besideExecutable;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "FluentASM.Sample", "samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void WriteSeparator(string title, string file)
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"[{title}] {file}");
        Console.WriteLine();
    }

    private static void WriteCaret(string text, SourceLocation location, TextSpan span)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lineText = location.Line - 1 < lines.Length ? lines[location.Line - 1] : string.Empty;
        var gutter = location.Line.ToString();
        var padding = new string(' ', Math.Max(location.Column - 1, 0));
        var carets = new string('^', Math.Max(span.Length, 1));

        Console.WriteLine($"  {gutter} | {lineText}");
        Console.WriteLine($"  {new string(' ', gutter.Length)} | {padding}{carets}");
        Console.WriteLine();
    }

    /// <summary>
    /// Recalcula la línea y columna (base 1) de un desplazamiento con las mismas
    /// reglas que el lexer: <c>\r\n</c> cuenta como un solo salto de línea y las
    /// columnas avanzan por carácter, incluidas las tabulaciones.
    /// </summary>
    private static (int Line, int Column) ComputePosition(string text, int offset)
    {
        var line = 1;
        var column = 1;
        var end = Math.Clamp(offset, 0, text.Length);

        for (var index = 0; index < end; index++)
        {
            var current = text[index];
            if (current == '\r')
            {
                if (index + 1 >= text.Length || text[index + 1] != '\n')
                {
                    line++;
                    column = 1;
                }

                continue;
            }

            if (current == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return (line, column);
    }

    private static int CountErrors(ParseResult result) =>
        result.Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static string Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Span is { } value
            ? $" [span {value.Start}..{value.End}, longitud {value.Length}]"
            : string.Empty;
        return $"{diagnostic}{span}";
    }

    private static string Describe(Token token) => token.Kind switch
    {
        TokenKind.Integer => $"{token.Text} (valor {token.IntegerValue})",
        TokenKind.String => $"{token.Text} (texto \"{Escape(token.StringValue)}\")",
        TokenKind.Magnitude => $"{token.Text} ({token.Magnitude})",
        TokenKind.EndOfFile => "<fin de archivo>",
        _ => token.Text,
    };

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\0':
                    builder.Append("\\0");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
