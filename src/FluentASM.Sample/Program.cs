using System.Text;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Encoding;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;

namespace FluentASM.Sample;

/// <summary>
/// Banco de pruebas manual de FluentASM. Ejecuta el lexer, el parser de M1, el
/// análisis semántico de M2 y el emisor de imagen plana de M3 sobre los tres
/// ejemplos completos de <c>FLUENT_SYNTAX_X64.md</c> §7, sobre un ejemplo con
/// error sintáctico intencional, sobre cuatro casos negativos de M2, sobre dos
/// casos de layout de M3 y, opcionalmente, sobre un archivo indicado por ruta.
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

    /// <summary>
    /// Imágenes esperadas byte a byte de los ejemplos de §7 y del caso de
    /// entrada tardía; fijan la forma más corta determinista de §6.
    /// </summary>
    private static readonly (string Title, string File, int EntryOffset, string Hex)[] ImageSamples =
    [
        (
            "imagen 7.1",
            "miCalculadoraCli.fasm",
            0x00,
            "48 8B 0D 19 00 00 00 48 8B 15 1A 00 00 00 E8 01 00 00 00 C3 "
            + "48 8B C1 48 01 D0 C3 90 90 90 90 90 "
            + "0A 00 00 00 00 00 00 00 14 00 00 00 00 00 00 00"),
        (
            "imagen 7.2",
            "bucleDemo.fasm",
            0x00,
            "31 C0 31 C9 48 01 C8 48 FF C1 48 3B 0D 0F 00 00 00 72 F1 C3 "
            + "90 90 90 90 90 90 90 90 90 90 90 90 "
            + "05 00 00 00 00 00 00 00"),
        (
            "imagen 7.3",
            "serialBareMetal.fasm",
            0x00,
            "FA 66 8B 15 08 00 00 00 8A 05 04 00 00 00 EE F4 F8 03 41"),
        (
            "imagen con salto inicial",
            "entradaTardia.fasm",
            0x08,
            "E9 03 00 00 00 31 C0 C3 31 C0 C3"),
    ];

    /// <summary>Caso negativo de layout: <c>LOOP</c> no admite forma cercana.</summary>
    private const string LoopOverflowSample = "bucleLargo.fasm";

    private static readonly (string Title, string File, string DiagnosticId, int Line, int Column)[] NegativeSamples =
    [
        ("negativo 1: dato duro", "datoDuro.fasm", "FASM2007", 7, 31),
        ("negativo 2: nombre legacy", "nombreLegacy.fasm", "FASM3012", 7, 21),
        ("negativo 3: capacidad ausente", "memoriaSinCapacidad.fasm", "FASM3023", 7, 22),
        ("negativo 4: entrada invalidada", "entradaInvalidada.fasm", "FASM5003", 9, 21),
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

        Console.WriteLine("FluentASM.Sample — validación de M1 (lexer + parser) + M2 (semántica) + M3 (imagen .x64)");
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

        var negativeFailures = 0;
        foreach (var (title, file, id, line, column) in NegativeSamples)
        {
            WriteSeparator(title, file);
            if (CheckExpectedDiagnostic(Path.Combine(directory, file), id, line, column) != 0)
            {
                negativeFailures++;
            }
        }

        var imageFailures = CheckImages(directory);

        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        if (validFailures == 0 && errorFailures == 0 && negativeFailures == 0 && imageFailures == 0)
        {
            Console.WriteLine(
                $"RESUMEN: {ValidSamples.Length}/{ValidSamples.Length} ejemplos válidos con 0 diagnósticos; "
                + "1/1 error sintáctico con posición exacta; "
                + $"{NegativeSamples.Length}/{NegativeSamples.Length} casos negativos M2 con diagnóstico y posición exactos; "
                + $"{ImageSamples.Length}/{ImageSamples.Length} imágenes byte a byte deterministas y "
                + "1/1 desbordamiento de LOOP detectado.");
            return 0;
        }

        Console.WriteLine(
            $"RESUMEN: {validFailures} ejemplo(s) válido(s) con diagnósticos inesperados, "
            + $"{errorFailures} fallo(s) en el error sintáctico, "
            + $"{negativeFailures} fallo(s) en los casos negativos M2 y "
            + $"{imageFailures} fallo(s) en la emisión de la imagen .x64.");
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
            Console.WriteLine("RESULTADO: OK — AST generado y análisis semántico sin errores.");
            return 0;
        }

        Console.WriteLine($"RESULTADO: FALLO — {errors} error(es) léxico(s), sintáctico(s) o semántico(s).");
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
                $"  data {data.Name}: capacidad {data.Capacity.Display} ({data.BitSize} bits, {data.ByteSize} byte(s)).");
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
            + $"({(result.Success ? "0 errores" : "con errores")}), "
            + $"contratos flowCall verificados: {result.ContractsVerified}.");
    }

    private static int CheckExpectedDiagnostic(string path, string expectedId, int expectedLine, int expectedColumn)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No se encontró el archivo '{path}'.");
            return 1;
        }

        var text = File.ReadAllText(path);
        var lexResult = Lexer.Tokenize(text, path);
        var parseResult = Parser.Parse(lexResult);
        var semanticResult = SemanticAnalyzer.Analyze(parseResult.Root, parseResult.Diagnostics);

        Console.WriteLine($"Archivo: {path}");
        Console.WriteLine(
            $"{parseResult.Diagnostics.Count} diagnósticos "
            + $"({CountErrors(parseResult)} errores), {semanticResult.ContractsVerified} contrato(s) verificado(s).");
        Console.WriteLine();

        foreach (var diagnostic in parseResult.Diagnostics)
        {
            Console.WriteLine(Format(diagnostic));
        }

        var expected = parseResult.Diagnostics.FirstOrDefault(
            diagnostic => diagnostic.Id == expectedId
                && diagnostic.Location is { } location
                && location.Line == expectedLine
                && location.Column == expectedColumn);

        if (expected is null)
        {
            Console.WriteLine(
                $"RESULTADO: FALLO — no se emitió {expectedId} en línea {expectedLine}, columna {expectedColumn}.");
            return 1;
        }

        if (expected.Span is not { Length: > 0 } span || expected.Location is not { } exact)
        {
            Console.WriteLine($"RESULTADO: FALLO — {expectedId} no tiene span o posición válidos.");
            return 1;
        }

        var computed = ComputePosition(text, span.Start);
        WriteCaret(text, exact, span);

        var locationMatches = exact.Line == expectedLine && exact.Column == expectedColumn;
        var spanOnLine = computed.Line == expectedLine;
        if (!locationMatches || !spanOnLine)
        {
            Console.WriteLine(
                $"RESULTADO: FALLO — {expectedId} declara línea {exact.Line}, columna {exact.Column}, "
                + $"pero se esperaba línea {expectedLine}, columna {expectedColumn} "
                + $"(span en línea {computed.Line}, columna {computed.Column}).");
            return 1;
        }

        Console.WriteLine(
            $"RESULTADO: OK — {expectedId} detectado en línea {exact.Line}, columna {exact.Column}, "
            + $"span {span.Start}..{span.End}.");
        return 0;
    }

    /// <summary>
    /// Compila a imagen plana los ejemplos de §7 y el caso de entrada tardía, y
    /// comprueba offsets, bytes exactos y determinismo; después valida el caso
    /// negativo de <c>LOOP</c> fuera de rango.
    /// </summary>
    private static int CheckImages(string directory)
    {
        var failures = 0;

        foreach (var (title, file, entryOffset, expectedHex) in ImageSamples)
        {
            WriteSeparator(title, file);
            if (CheckImage(Path.Combine(directory, file), entryOffset, expectedHex) != 0)
            {
                failures++;
            }
        }

        WriteSeparator("negativo de layout: LOOP fuera de rango", LoopOverflowSample);
        if (CheckLoopOverflow(Path.Combine(directory, LoopOverflowSample)) != 0)
        {
            failures++;
        }

        return failures;
    }

    private static int CheckImage(string path, int entryOffset, string expectedHex)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No se encontró el archivo '{path}'.");
            return 1;
        }

        var first = BuildImage(path, out var diagnostics);
        if (!first.Success)
        {
            WriteDiagnostics(diagnostics);
            Console.WriteLine(
                $"RESULTADO: FALLO — no se generó la imagen ({diagnostics.Count} diagnóstico(s)).");
            return 1;
        }

        Console.WriteLine(first.Map.Format());
        var hex = ToHex(first.Image);
        Console.WriteLine($"Hex ({first.Image.Length} byte(s)): {hex}");

        var second = BuildImage(path, out _);
        var deterministic = second.Success && first.Image.SequenceEqual(second.Image);
        Console.WriteLine();
        Console.WriteLine(
            $"Determinismo: {(deterministic ? "idéntico en dos compilaciones" : "DIFIERE entre compilaciones")}.");
        Console.WriteLine(
            $"Entrada: offset 0x{first.Map.EntryOffset:X8} (esperado 0x{entryOffset:X8}).");

        if (!deterministic)
        {
            Console.WriteLine("RESULTADO: FALLO — dos compilaciones del mismo fuente producen bytes distintos.");
            return 1;
        }

        if (first.Map.EntryOffset != entryOffset)
        {
            Console.WriteLine($"RESULTADO: FALLO — la entrada quedó en 0x{first.Map.EntryOffset:X8}.");
            return 1;
        }

        if (!string.Equals(hex, expectedHex, StringComparison.Ordinal))
        {
            Console.WriteLine($"RESULTADO: FALLO — bytes distintos de los esperados:{Environment.NewLine}  {expectedHex}");
            return 1;
        }

        Console.WriteLine("RESULTADO: OK — imagen determinista con los bytes esperados.");
        return 0;
    }

    private static int CheckLoopOverflow(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No se encontró el archivo '{path}'.");
            return 1;
        }

        var image = BuildImage(path, out var diagnostics);
        WriteDiagnostics(diagnostics);

        var overflow = diagnostics.FirstOrDefault(
            diagnostic => diagnostic.Id == DiagnosticIds.RelativeOutOfRange);
        if (image.Success || overflow is null)
        {
            Console.WriteLine("RESULTADO: FALLO — falta el diagnóstico FASM4022 de LOOP fuera de rango.");
            return 1;
        }

        if (overflow.Location is not { } location || location.Line != 150 || location.Column != 25)
        {
            Console.WriteLine(
                $"RESULTADO: FALLO — FASM4022 en línea {overflow.Location?.Line}, "
                + $"columna {overflow.Location?.Column}; se esperaba 150, 25.");
            return 1;
        }

        Console.WriteLine(
            $"RESULTADO: OK — {overflow.Id} en línea 150, columna 25; el layout rechaza LOOP fuera de rel8.");
        return 0;
    }

    private static ImageBuildResult BuildImage(string path, out DiagnosticBag diagnostics)
    {
        var text = File.ReadAllText(path);
        var parseResult = Parser.Parse(Lexer.Tokenize(text, path));
        var bag = parseResult.Diagnostics;
        var semanticResult = SemanticAnalyzer.Analyze(parseResult.Root, bag);
        diagnostics = bag;
        return X64ImageBuilder.Build(parseResult.Root, semanticResult, "main", bag);
    }

    private static string ToHex(IReadOnlyList<byte> bytes) =>
        string.Join(
            ' ',
            bytes.Select(value => value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

    private static void WriteDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Console.WriteLine(Format(diagnostic));
        }
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
