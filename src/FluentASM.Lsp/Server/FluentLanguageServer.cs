using FluentASM.Core.Diagnostics;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;
using FluentASM.Lsp.Catalog;
using FluentASM.Lsp.Completion;
using FluentASM.Lsp.Protocol;
using Microsoft.Extensions.Logging;

namespace FluentASM.Lsp.Server;

/// <summary>
/// Servidor de lenguaje en memoria para FluentASM según el estándar LSP.
/// Implementa validación semántica, autocompletado contextual con metadatos,
/// inspección hover, resaltado y extracción de símbolos de documento.
/// </summary>
/// <remarks>
/// Todos los servicios se apoyan en el mismo <see cref="DocumentStore"/>: el
/// pipeline de análisis se ejecuta una sola vez por versión y se reutiliza en
/// diagnósticos, resaltado, completado, hover y símbolos.
/// </remarks>
public sealed class FluentLanguageServer : ILanguageService
{
    private readonly DocumentStore store;

    /// <summary>Crea el servidor sin registro estructurado.</summary>
    public FluentLanguageServer()
        : this(null)
    {
    }

    /// <summary>
    /// Crea el servidor con registro estructurado opcional. Los fallos internos
    /// del pipeline y los snapshots descartados se escriben con
    /// <see cref="ILoggerFactory"/>.
    /// </summary>
    /// <param name="loggerFactory">Fábrica de registro; puede ser <c>null</c>.</param>
    public FluentLanguageServer(ILoggerFactory? loggerFactory)
    {
        store = new DocumentStore(loggerFactory);
    }

    /// <inheritdoc />
    public void OpenDocument(string uri, string text, int version = 1) => store.Open(uri, text, version);

    /// <inheritdoc />
    public void UpdateDocument(string uri, string text, int version = 1) => store.Update(uri, text, version);

    /// <inheritdoc />
    public void CloseDocument(string uri) => store.Close(uri);

    /// <inheritdoc />
    public string? GetDocumentText(string uri) => store.GetText(uri);

    /// <inheritdoc />
    public DocumentSnapshot? GetSnapshot(string uri, CancellationToken cancellationToken = default) =>
        store.GetSnapshot(uri, cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<LspDiagnostic> ValidateDocument(string uri, CancellationToken cancellationToken = default) =>
        store.GetDiagnostics(uri, cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<HighlightToken> GetHighlightTokens(string uri, CancellationToken cancellationToken = default) =>
        store.GetHighlightTokens(uri, cancellationToken);

    /// <inheritdoc />
    public CompletionList GetCompletions(string uri, Position position, CancellationToken cancellationToken = default)
    {
        var snapshot = store.GetSnapshot(uri, cancellationToken);
        if (snapshot is null)
        {
            return new CompletionList(
                IsIncomplete: false,
                Items: Array.Empty<LspCompletionItem>(),
                ReplaceRange: null,
                Context: "Raíz",
                Caret: position,
                Version: null);
        }

        // Un documento vacío sigue siendo un contexto raíz válido: Ctrl+Espacio
        // debe ofrecer los constructores canónicos aun antes de escribir código.
        return ContextualCompletionProvider.GetCompletions(snapshot, position, cancellationToken);
    }

    /// <inheritdoc />
    public Hover? GetHover(string uri, Position position)
    {
        var snapshot = store.GetSnapshot(uri);
        if (snapshot is null || string.IsNullOrEmpty(snapshot.Text))
        {
            return null;
        }

        int offset = snapshot.Lines.ToOffset(position);
        var tokens = snapshot.LexResult.Tokens;
        int index = FindTokenIndex(tokens, offset);
        if (index < 0)
        {
            return null;
        }

        string word = GetHoverWord(tokens, index);
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        if (LspCatalog.TryGetItem(word, out var item))
        {
            return CreateHover(item);
        }

        if (!string.Equals(word, tokens[index].Text, StringComparison.Ordinal) &&
            LspCatalog.TryGetItem(tokens[index].Text, out item))
        {
            return CreateHover(item);
        }

        return GetSymbolHover(snapshot, word) ?? GetSymbolHover(snapshot, tokens[index].Text);
    }

    /// <inheritdoc />
    public IReadOnlyList<DocumentSymbol> GetDocumentSymbols(string uri)
    {
        var snapshot = store.GetSnapshot(uri);
        if (snapshot is null || string.IsNullOrEmpty(snapshot.Text))
        {
            return Array.Empty<DocumentSymbol>();
        }

        var symbols = new List<DocumentSymbol>();
        var dataNames = new HashSet<string>(StringComparer.Ordinal);
        var routineNames = new HashSet<string>(StringComparer.Ordinal);

        if (snapshot.SemanticResult is { } semantic)
        {
            foreach (var data in semantic.Symbols.DataSymbols)
            {
                if (!dataNames.Add(data.Name))
                {
                    continue;
                }

                symbols.Add(new DocumentSymbol(
                    Name: data.Name,
                    Kind: SymbolKind.Field,
                    Range: ToRange(snapshot.Lines, data.Declaration.Span),
                    SelectionRange: ToRange(snapshot.Lines, data.Declaration.Span),
                    Detail: $"Datos · {data.ByteSize} bytes"));
            }

            foreach (var routine in semantic.Symbols.Routines)
            {
                string name = routine.Name ?? "<implícita>";
                if (!routineNames.Add(name))
                {
                    continue;
                }

                var children = new List<DocumentSymbol>();
                foreach (var label in semantic.Symbols.LabelsOf(routine).Values)
                {
                    children.Add(CreateLabelSymbol(snapshot, label.Name, label.Declaration.Span));
                }

                symbols.Add(CreateRoutineSymbol(
                    snapshot,
                    name,
                    routine.Definition.Span,
                    routine.Definition.Name?.Span ?? routine.Definition.Span,
                    routine.Parameters.Count,
                    children));
            }
        }

        // Respaldo tolerante: completa huecos con el árbol parcial cuando la fase
        // semántica no pudo ligar todos los símbolos.
        if (snapshot.ParseResult.Root.Data is { } dataSection)
        {
            foreach (var definition in dataSection.Definitions)
            {
                if (definition.Name is not { } nameToken || !dataNames.Add(nameToken.Text))
                {
                    continue;
                }

                symbols.Add(new DocumentSymbol(
                    Name: nameToken.Text,
                    Kind: SymbolKind.Field,
                    Range: ToRange(snapshot.Lines, definition.Span),
                    SelectionRange: ToRange(snapshot.Lines, nameToken.Span),
                    Detail: "Datos"));
            }
        }

        if (snapshot.ParseResult.Root.Execute is { } executeSection)
        {
            foreach (var definition in executeSection.Routines)
            {
                if (definition.Name is not { } nameToken || !routineNames.Add(nameToken.Text))
                {
                    continue;
                }

                var children = new List<DocumentSymbol>();
                foreach (var statement in definition.Body)
                {
                    if (statement is LabelStatementSyntax label)
                    {
                        children.Add(CreateLabelSymbol(snapshot, label.Label.Text, label.Label.Span));
                    }
                }

                symbols.Add(CreateRoutineSymbol(
                    snapshot,
                    nameToken.Text,
                    definition.Span,
                    nameToken.Span,
                    definition.Run?.Parameters.Count ?? 0,
                    children));
            }
        }

        symbols.Sort(static (left, right) =>
        {
            int byLine = left.Range.Start.Line.CompareTo(right.Range.Start.Line);
            return byLine != 0
                ? byLine
                : left.Range.Start.Character.CompareTo(right.Range.Start.Character);
        });

        return symbols;
    }

    /// <inheritdoc />
    public IReadOnlyList<LspCodeAction> GetCodeActions(
        string uri,
        CancellationToken cancellationToken = default)
    {
        var snapshot = store.GetSnapshot(uri, cancellationToken);
        if (snapshot is null)
        {
            return Array.Empty<LspCodeAction>();
        }

        var actions = new List<LspCodeAction>();
        foreach (var diagnostic in snapshot.Diagnostics)
        {
            if (!string.Equals(
                diagnostic.Code,
                DiagnosticIds.LegacyName,
                StringComparison.Ordinal))
            {
                continue;
            }

            int diagnosticStart = snapshot.Lines.ToOffset(diagnostic.Range.Start);
            int diagnosticEnd = snapshot.Lines.ToOffset(diagnostic.Range.End);
            Token? token = FindTokenForRange(snapshot, diagnosticStart, diagnosticEnd);
            if (token is null
                || !RegisterModel.TryGetLegacySuggestion(token.Text, out string suggestion))
            {
                continue;
            }

            var edit = new LspTextEdit(
                ToRange(snapshot.Lines, token.Span),
                suggestion);
            actions.Add(new LspCodeAction(
                $"Reemplazar '{token.Text}' por '{suggestion}'",
                new[] { edit },
                diagnostic.Code));
        }

        return actions;
    }

    private static Token? FindTokenForRange(
        DocumentSnapshot snapshot,
        int rangeStart,
        int rangeEnd)
    {
        Token? containingToken = null;
        foreach (var token in snapshot.LexResult.Tokens)
        {
            if (token.Span.Start <= rangeStart && token.Span.End >= rangeEnd)
            {
                if (token.Span.Start == rangeStart && token.Span.End == rangeEnd)
                {
                    return token;
                }

                containingToken ??= token;
            }
            else if (token.Span.Start <= rangeStart && rangeStart < token.Span.End)
            {
                containingToken ??= token;
            }
        }

        return containingToken;
    }

    private static List<LocalRoutine> CollectRoutines(DocumentSnapshot snapshot)
    {
        var routines = new List<LocalRoutine>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (snapshot.SemanticResult is { } semantic)
        {
            foreach (var routine in semantic.Symbols.Routines)
            {
                if (routine.Name is not { Length: > 0 } name || !seen.Add(name))
                {
                    continue;
                }

                routines.Add(new LocalRoutine(name, routine.Parameters.Count));
            }
        }

        if (snapshot.ParseResult.Root.Execute is { } execute)
        {
            foreach (var definition in execute.Routines)
            {
                if (definition.Name is not { } nameToken || !seen.Add(nameToken.Text))
                {
                    continue;
                }

                routines.Add(new LocalRoutine(nameToken.Text, definition.Run?.Parameters.Count ?? 0));
            }
        }

        return routines;
    }

    private static List<LocalLabel> CollectLabels(DocumentSnapshot snapshot)
    {
        var labels = new List<LocalLabel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (snapshot.SemanticResult is { } semantic)
        {
            foreach (var routine in semantic.Symbols.Routines)
            {
                foreach (var label in semantic.Symbols.LabelsOf(routine).Values)
                {
                    if (seen.Add(label.Name))
                    {
                        labels.Add(new LocalLabel(label.Name));
                    }
                }
            }
        }

        if (snapshot.ParseResult.Root.Execute is { } execute)
        {
            foreach (var definition in execute.Routines)
            {
                foreach (var statement in definition.Body)
                {
                    if (statement is LabelStatementSyntax label && seen.Add(label.Label.Text))
                    {
                        labels.Add(new LocalLabel(label.Label.Text));
                    }
                }
            }
        }

        return labels;
    }

    private static string GetLinePrefix(DocumentSnapshot snapshot, Position position)
    {
        int line = Math.Clamp(position.Line, 0, snapshot.Lines.LineCount - 1);
        int lineStart = snapshot.Lines.GetLineStart(line);
        int lineEnd = snapshot.Lines.GetLineEnd(line);
        int offset = Math.Clamp(lineStart + Math.Max(0, position.Character), lineStart, lineEnd);
        return snapshot.Text.Substring(lineStart, offset - lineStart).TrimStart();
    }

    private static int FindTokenIndex(IReadOnlyList<Token> tokens, int offset)
    {
        int fallback = -1;

        for (int index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.EndOfFile)
            {
                break;
            }

            if (token.Span.Start <= offset && offset < token.Span.End)
            {
                return index;
            }

            if (token.Span.End == offset)
            {
                fallback = index;
            }
        }

        return fallback;
    }

    private static string GetHoverWord(IReadOnlyList<Token> tokens, int index)
    {
        var token = tokens[index];

        if (token.Kind == TokenKind.Identifier &&
            index >= 2 &&
            tokens[index - 1].Kind == TokenKind.Dot &&
            tokens[index - 2].Kind == TokenKind.Identifier)
        {
            return tokens[index - 2].Text + "." + token.Text;
        }

        return token.Text;
    }

    private static Hover CreateHover(LspCompletionItem item)
    {
        string markdown = item.Documentation?.Value ?? $"**{item.Label}**\n\n{item.Detail}";
        if (item.Metadata is { } metadata)
        {
            markdown += $"\n\n---\n- **Categoría:** {metadata.Category}\n- **Firma:** `{metadata.Signature}`\n- **Modifica:** {metadata.Modifies}\n- **Banderas:** {metadata.FlagsAffected ?? "Ninguna"}";
        }

        return new Hover(MarkupContent.Markdown(markdown));
    }

    private static Hover? GetSymbolHover(DocumentSnapshot snapshot, string name)
    {
        if (snapshot.SemanticResult is not { } semantic)
        {
            return null;
        }

        if (semantic.Symbols.TryGetData(name, out var data))
        {
            string markdown = $"**{data.Name}**\n\n---\n- **Categoría:** Datos\n- **Capacidad:** {data.ByteSize} bytes ({data.BitSize} bits)";
            return new Hover(MarkupContent.Markdown(markdown));
        }

        if (semantic.Symbols.TryGetRoutine(name, out var routine))
        {
            string markdown = $"**{routine.DisplayName}**\n\n---\n- **Categoría:** Rutina\n- **Parámetros:** {routine.Parameters.Count}";
            return new Hover(MarkupContent.Markdown(markdown));
        }

        foreach (var scope in semantic.Symbols.Routines)
        {
            if (semantic.Symbols.LabelsOf(scope).TryGetValue(name, out var label))
            {
                string markdown = $"**{label.Name}**\n\n---\n- **Categoría:** Etiqueta local\n- **Rutina:** {scope.DisplayName}";
                return new Hover(MarkupContent.Markdown(markdown));
            }
        }

        return null;
    }

    private static LspCompletionItem CreateRoutineItem(LocalRoutine routine, bool callContext)
    {
        string detail = callContext
            ? $"Rutina de usuario: {routine.Name}"
            : $"Rutina: {routine.Name}";

        string documentation = callContext
            ? $"Invocación a la rutina definida `{routine.Name}`."
            : $"Rutina definida en el código actual: `{routine.Name}`.";

        string inputs = routine.ParameterCount switch
        {
            0 => "Ninguno",
            1 => "1 parámetro",
            _ => $"{routine.ParameterCount} parámetros",
        };

        return new LspCompletionItem(
            Label: routine.Name,
            Kind: CompletionItemKind.Function,
            Detail: detail,
            Documentation: MarkupContent.Markdown(documentation),
            InsertText: routine.Name,
            SortText: $"00_{routine.Name}",
            Metadata: new FluentItemMetadata(
                Category: "Directivas",
                Signature: $"routine {routine.Name}",
                Description: "Subrutina de usuario declarada en el programa.",
                Inputs: inputs,
                Modifies: "Stack, Gpr0 (retorno)",
                Returns: "Gpr0"));
    }

    private static LspCompletionItem CreateLabelItem(LocalLabel label) =>
        new(
            Label: label.Name,
            Kind: CompletionItemKind.Reference,
            Detail: $"Etiqueta local de salto: {label.Name}",
            Documentation: MarkupContent.Markdown($"Etiqueta local `{label.Name}` definida en el código."),
            InsertText: label.Name,
            SortText: $"00_{label.Name}",
            Metadata: new FluentItemMetadata(
                Category: "Flujo",
                Signature: $"{label.Name}:",
                Description: "Punto de anclaje de flujo local.",
                Inputs: "Ninguno",
                Modifies: "Ipr",
                Returns: "Destino de salto"));

    private static DocumentSymbol CreateRoutineSymbol(
        DocumentSnapshot snapshot,
        string name,
        TextSpan span,
        TextSpan selectionSpan,
        int parameterCount,
        IReadOnlyList<DocumentSymbol> children) =>
        new(
            Name: name,
            Kind: SymbolKind.Function,
            Range: ToRange(snapshot.Lines, span),
            SelectionRange: ToRange(snapshot.Lines, selectionSpan),
            Detail: $"Rutina · {parameterCount} parámetros",
            Children: children.Count == 0 ? null : children);

    private static DocumentSymbol CreateLabelSymbol(DocumentSnapshot snapshot, string name, TextSpan span) =>
        new(
            Name: name,
            Kind: SymbolKind.Field,
            Range: ToRange(snapshot.Lines, span),
            SelectionRange: ToRange(snapshot.Lines, span),
            Detail: "Etiqueta local");

    private static LspRange ToRange(DocumentLines lines, TextSpan span) =>
        new(lines.ToPosition(span.Start), lines.ToPosition(span.End));

    private sealed record LocalRoutine(string Name, int ParameterCount);

    private sealed record LocalLabel(string Name);
}
