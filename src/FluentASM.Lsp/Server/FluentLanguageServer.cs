using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;
using FluentASM.Lsp.Catalog;
using FluentASM.Lsp.Protocol;

namespace FluentASM.Lsp.Server;

/// <summary>
/// Servidor de lenguaje en memoria para FluentASM según el estándar LSP.
/// Implementa validación semántica, autocompletado inteligente con metadatos de Stitch,
/// inspección hover y extracción de símbolos de documento.
/// </summary>
public sealed class FluentLanguageServer
{
    private readonly ConcurrentDictionary<string, TextDocumentItem> _documents = new(StringComparer.OrdinalIgnoreCase);

    public void OpenDocument(string uri, string text, int version = 1)
    {
        _documents[uri] = new TextDocumentItem(uri, "fluentasm", version, text);
    }

    public void UpdateDocument(string uri, string text, int version = 1)
    {
        _documents[uri] = new TextDocumentItem(uri, "fluentasm", version, text);
    }

    public void CloseDocument(string uri)
    {
        _documents.TryRemove(uri, out _);
    }

    public string? GetDocumentText(string uri)
    {
        return _documents.TryGetValue(uri, out var doc) ? doc.Text : null;
    }

    /// <summary>
    /// Valida el documento ejecutando el Lexer, Parser y SemanticAnalyzer de FluentASM.Core
    /// y emite diagnósticos LSP con rangos exactos.
    /// </summary>
    public IReadOnlyList<LspDiagnostic> ValidateDocument(string uri)
    {
        if (!_documents.TryGetValue(uri, out var doc) || string.IsNullOrEmpty(doc.Text))
        {
            return Array.Empty<LspDiagnostic>();
        }

        var diagnostics = new List<LspDiagnostic>();
        var bag = new DiagnosticBag();

        try
        {
            var lexResult = Lexer.Tokenize(doc.Text, uri, bag);
            var parseResult = Parser.Parse(lexResult.Tokens, bag);

            if (parseResult.Root != null)
            {
                try
                {
                    SemanticAnalyzer.Analyze(parseResult.Root, bag);
                }
                catch
                {
                    // La recuperación semántica conserva diagnósticos anteriores
                }
            }

            foreach (var diag in bag)
            {
                int line = Math.Max(0, (diag.Location?.Line ?? 1) - 1);
                int col = Math.Max(0, (diag.Location?.Column ?? 1) - 1);
                int length = diag.Span?.Length ?? 1;

                var range = new LspRange(new Position(line, col), new Position(line, col + length));
                var severity = diag.Severity switch
                {
                    Core.Diagnostics.DiagnosticSeverity.Error => Protocol.DiagnosticSeverity.Error,
                    Core.Diagnostics.DiagnosticSeverity.Warning => Protocol.DiagnosticSeverity.Warning,
                    _ => Protocol.DiagnosticSeverity.Information
                };

                diagnostics.Add(new LspDiagnostic(range, severity, diag.Message, diag.Id));
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new LspDiagnostic(
                new LspRange(Position.Zero, Position.Zero),
                Protocol.DiagnosticSeverity.Error,
                $"Error interno en análisis LSP: {ex.Message}"));
        }

        return diagnostics;
    }

    /// <summary>
    /// Genera la lista de sugerencias de autocompletado en el punto dado del documento.
    /// </summary>
    public CompletionList GetCompletions(string uri, Position position)
    {
        if (!_documents.TryGetValue(uri, out var doc) || string.IsNullOrEmpty(doc.Text))
        {
            return new CompletionList(false, LspCatalog.GetAllItems());
        }

        string text = doc.Text;
        string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string currentLine = position.Line < lines.Length ? lines[position.Line] : "";
        string prefix = position.Character <= currentLine.Length
            ? currentLine.Substring(0, position.Character).TrimStart()
            : currentLine.TrimStart();

        var result = new List<LspCompletionItem>();

        // 1. Extraer símbolos locales definidos por el usuario en el archivo (rutinas y etiquetas)
        var localRoutines = ExtractRoutines(lines);
        var localLabels = ExtractLabels(lines);

        // 2. Contexto de llamada a rutina (.flowCall(...))
        if (prefix.Contains(".flowCall") || prefix.EndsWith("flowCall("))
        {
            foreach (var routine in localRoutines)
            {
                result.Add(new LspCompletionItem(
                    Label: routine,
                    Kind: CompletionItemKind.Function,
                    Detail: $"Rutina de usuario: {routine}",
                    Documentation: MarkupContent.Markdown($"Invocación a la rutina definida `{routine}`."),
                    InsertText: routine,
                    SortText: $"00_{routine}",
                    Metadata: new FluentItemMetadata(
                        Category: "Directives",
                        Signature: $"routine {routine}",
                        Description: "Subrutina de usuario declarada en el programa.",
                        Inputs: "Argumentos según parámetros",
                        Modifies: "Stack, Gpr0 (retorno)",
                        Returns: "Gpr0")));
            }
            return new CompletionList(false, result);
        }

        // 3. Contexto de salto a etiqueta (@loop, etc.)
        if (prefix.EndsWith("@") || prefix.Contains(".flowJump") || prefix.Contains("@"))
        {
            foreach (var label in localLabels)
            {
                result.Add(new LspCompletionItem(
                    Label: label,
                    Kind: CompletionItemKind.Reference,
                    Detail: $"Etiqueta local de salto: {label}",
                    Documentation: MarkupContent.Markdown($"Etiqueta local `{label}` definida en el código."),
                    InsertText: label,
                    SortText: $"00_{label}",
                    Metadata: new FluentItemMetadata(
                        Category: "Flow",
                        Signature: $"{label}:",
                        Description: "Punto de anclaje de flujo local.",
                        Inputs: "Ninguno",
                        Modifies: "Ipr",
                        Returns: "Destino de salto")));
            }

            if (prefix.EndsWith("@"))
            {
                return new CompletionList(false, result);
            }
        }

        // 4. Contexto de miembro o verbo con punto (ej. Gpr0., .flowJump, etc.)
        if (prefix.EndsWith(".") || prefix.Contains("."))
        {
            foreach (var item in LspCatalog.GetAllItems())
            {
                if (item.Metadata?.Category == "Verbs" ||
                    item.Metadata?.Category == "Flow" ||
                    item.Label.Contains(".low"))
                {
                    result.Add(item);
                }
            }
            return new CompletionList(false, result);
        }

        // 5. Sugerencias generales: catálogo completo + rutinas + etiquetas locales
        result.AddRange(LspCatalog.GetAllItems());

        foreach (var routine in localRoutines)
        {
            result.Add(new LspCompletionItem(
                Label: routine,
                Kind: CompletionItemKind.Function,
                Detail: $"Rutina: {routine}",
                Documentation: MarkupContent.Markdown($"Rutina definida en el código actual: `{routine}`."),
                InsertText: routine,
                SortText: $"00_{routine}",
                Metadata: new FluentItemMetadata(
                    Category: "Directives",
                    Signature: $"routine {routine}",
                    Description: "Subrutina definida en el módulo.",
                    Inputs: "Argumentos de usuario",
                    Modifies: "Gpr0, Stack",
                    Returns: "Gpr0")));
        }

        foreach (var label in localLabels)
        {
            result.Add(new LspCompletionItem(
                Label: label,
                Kind: CompletionItemKind.Reference,
                Detail: $"Etiqueta: {label}",
                Documentation: MarkupContent.Markdown($"Etiqueta de flujo `{label}`."),
                InsertText: label,
                SortText: $"00_{label}",
                Metadata: new FluentItemMetadata(
                    Category: "Flow",
                    Signature: $"{label}:",
                    Description: "Etiqueta local de salto.",
                    Inputs: "Ninguno",
                    Modifies: "Ipr",
                    Returns: "Destino de salto")));
        }

        return new CompletionList(false, result);
    }

    /// <summary>
    /// Devuelve información de documentación Hover sobre el elemento situado en la posición dada.
    /// </summary>
    public Hover? GetHover(string uri, Position position)
    {
        if (!_documents.TryGetValue(uri, out var doc) || string.IsNullOrEmpty(doc.Text))
        {
            return null;
        }

        string[] lines = doc.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (position.Line >= lines.Length)
        {
            return null;
        }

        string line = lines[position.Line];
        string word = ExtractWordAt(line, position.Character);
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        if (LspCatalog.TryGetItem(word, out var item))
        {
            string md = item.Documentation?.Value ?? $"**{item.Label}**\n\n{item.Detail}";
            if (item.Metadata != null)
            {
                md += $"\n\n---\n- **Categoría:** {item.Metadata.Category}\n- **Firma:** `{item.Metadata.Signature}`\n- **Modifica:** {item.Metadata.Modifies}\n- **Banderas:** {item.Metadata.FlagsAffected ?? "Ninguna"}";
            }
            return new Hover(MarkupContent.Markdown(md));
        }

        return null;
    }

    /// <summary>
    /// Devuelve el árbol de símbolos de documento estructurado según LSP.
    /// </summary>
    public IReadOnlyList<DocumentSymbol> GetDocumentSymbols(string uri)
    {
        if (!_documents.TryGetValue(uri, out var doc) || string.IsNullOrEmpty(doc.Text))
        {
            return Array.Empty<DocumentSymbol>();
        }

        var symbols = new List<DocumentSymbol>();
        string[] lines = doc.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("routine ", StringComparison.OrdinalIgnoreCase))
            {
                string routineName = line.Substring(8).Trim();
                var range = new LspRange(new Position(i, 0), new Position(i, line.Length));
                symbols.Add(new DocumentSymbol(
                    Name: routineName,
                    Kind: SymbolKind.Function,
                    Range: range,
                    SelectionRange: range,
                    Detail: "Subrutina FluentASM"));
            }
            else if (line.StartsWith("@") && line.Contains(':'))
            {
                string label = line.Substring(0, line.IndexOf(':')).Trim();
                var range = new LspRange(new Position(i, 0), new Position(i, line.Length));
                symbols.Add(new DocumentSymbol(
                    Name: label,
                    Kind: SymbolKind.Field,
                    Range: range,
                    SelectionRange: range,
                    Detail: "Etiqueta de flujo"));
            }
        }

        return symbols;
    }

    private static List<string> ExtractRoutines(string[] lines)
    {
        var routines = new List<string>();
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"\broutine\s+([a-zA-Z0-9_]+)");
            if (match.Success)
            {
                routines.Add(match.Groups[1].Value);
            }
        }
        return routines;
    }

    private static List<string> ExtractLabels(string[] lines)
    {
        var labels = new List<string>();
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"(@[a-zA-Z0-9_]+):?");
            if (match.Success)
            {
                labels.Add(match.Groups[1].Value);
            }
        }
        return labels;
    }

    private static string ExtractWordAt(string line, int col)
    {
        if (string.IsNullOrEmpty(line) || col < 0 || col > line.Length)
        {
            return "";
        }

        int start = Math.Min(col, line.Length - 1);
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '.' || line[start - 1] == '@' || line[start - 1] == '_'))
        {
            start--;
        }

        int end = Math.Min(col, line.Length);
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '.' || line[end] == '@' || line[end] == '_'))
        {
            end++;
        }

        if (start < end)
        {
            return line.Substring(start, end - start);
        }

        return "";
    }
}
