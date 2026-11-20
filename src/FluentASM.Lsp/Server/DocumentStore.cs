using System.Collections.Concurrent;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;
using FluentASM.Lsp.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LspDiagnosticSeverity = FluentASM.Lsp.Protocol.DiagnosticSeverity;

namespace FluentASM.Lsp.Server;

/// <summary>
/// Almacén versionado de documentos LSP. Mantiene un snapshot por URI y ejecuta
/// el pipeline <c>Lexer → Parser → SemanticAnalyzer</c> una sola vez por versión,
/// descartando cualquier análisis que quede obsoleto mientras se calcula.
/// </summary>
/// <remarks>
/// El registro estructurado es opcional: si no se inyecta un
/// <see cref="ILoggerFactory"/>, el almacén funciona con
/// <see cref="NullLogger"/> y no escribe en ningún sumidero.
/// </remarks>
public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<string, DocumentState> documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<DocumentStore> logger;

    /// <summary>Crea un almacén sin registro estructurado.</summary>
    public DocumentStore()
        : this(null)
    {
    }

    /// <summary>
    /// Crea un almacén que registra los fallos internos del pipeline y los
    /// análisis descartados por obsolescencia.
    /// </summary>
    /// <param name="loggerFactory">Fábrica de registro; puede ser <c>null</c>.</param>
    public DocumentStore(ILoggerFactory? loggerFactory)
    {
        logger = loggerFactory?.CreateLogger<DocumentStore>()
            ?? NullLogger<DocumentStore>.Instance;
    }

    /// <summary>Abre o reemplaza un documento.</summary>
    public void Open(string uri, string text, int version = 1) => SetDocument(uri, text, version);

    /// <summary>Actualiza el contenido de un documento abierto.</summary>
    public void Update(string uri, string text, int version = 1) => SetDocument(uri, text, version);

    /// <summary>Cierra un documento y libera su snapshot.</summary>
    public bool Close(string uri)
    {
        if (!documents.TryRemove(uri, out var state))
        {
            logger.LogDebug("Cierre ignorado: {Uri} no estaba abierto.", uri);
            return false;
        }

        lock (state.Gate)
        {
            state.Document = null;
            state.Snapshot = null;
        }

        logger.LogDebug("Documento cerrado: {Uri}.", uri);
        return true;
    }

    /// <summary>Devuelve el texto vigente del documento, o <c>null</c> si no está abierto.</summary>
    public string? GetText(string uri)
    {
        if (!documents.TryGetValue(uri, out var state))
        {
            return null;
        }

        lock (state.Gate)
        {
            return state.Document?.Text;
        }
    }

    /// <summary>
    /// Devuelve el snapshot vigente del documento, calculándolo si aún no existe
    /// para la versión actual. Si aparece una versión más reciente durante el
    /// análisis, el resultado obsoleto se descarta y se reintenta.
    /// </summary>
    public DocumentSnapshot? GetSnapshot(string uri, CancellationToken cancellationToken = default)
    {
        if (!documents.TryGetValue(uri, out var state))
        {
            return null;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TextDocumentItem document;
            lock (state.Gate)
            {
                if (state.Document is null)
                {
                    return null;
                }

                document = state.Document;
                if (state.Snapshot is { } cached && cached.Version == document.Version)
                {
                    return cached;
                }
            }

            lock (state.AnalysisGate)
            {
                // Otra llamada pudo completar el análisis mientras se esperaba el turno.
                lock (state.Gate)
                {
                    if (state.Document is null)
                    {
                        return null;
                    }

                    if (state.Snapshot is { } completed && completed.Version == state.Document.Version)
                    {
                        return completed;
                    }

                    document = state.Document;
                }

                var snapshot = Build(document, cancellationToken);

                lock (state.Gate)
                {
                    if (state.Document is null)
                    {
                        return null;
                    }

                    if (state.Document.Version == document.Version &&
                        string.Equals(state.Document.Text, document.Text, StringComparison.Ordinal))
                    {
                        state.Snapshot = snapshot;
                        return snapshot;
                    }

                    logger.LogDebug(
                        "Snapshot descartado por obsolescencia en {Uri}: analizada v{Analizada}, vigente v{Vigente}.",
                        uri,
                        document.Version,
                        state.Document.Version);
                }
            }
        }
    }

    /// <summary>Diagnósticos LSP de la versión vigente.</summary>
    public IReadOnlyList<LspDiagnostic> GetDiagnostics(string uri, CancellationToken cancellationToken = default)
    {
        var snapshot = GetSnapshot(uri, cancellationToken);
        return snapshot is null ? Array.Empty<LspDiagnostic>() : snapshot.Diagnostics;
    }

    /// <summary>Tokens clasificados para el resaltado de la versión vigente.</summary>
    public IReadOnlyList<HighlightToken> GetHighlightTokens(string uri, CancellationToken cancellationToken = default)
    {
        var snapshot = GetSnapshot(uri, cancellationToken);
        return snapshot is null ? Array.Empty<HighlightToken>() : snapshot.HighlightTokens;
    }

    private void SetDocument(string uri, string text, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(text);

        var state = documents.GetOrAdd(uri, static _ => new DocumentState());

        lock (state.Gate)
        {
            if (state.Document is { } current && string.Equals(current.Text, text, StringComparison.Ordinal))
            {
                return;
            }

            int effectiveVersion = version;
            if (state.Document is { } previous && effectiveVersion <= previous.Version)
            {
                // El IDE puede reenviar la misma versión al sincronizar; nunca se
                // reutiliza un snapshot de un texto distinto.
                effectiveVersion = previous.Version + 1;
            }

            state.Document = new TextDocumentItem(uri, "fluentasm", effectiveVersion, text);
            state.Snapshot = null;

            logger.LogDebug(
                "Documento sincronizado: {Uri} v{Version} ({Length} caracteres).",
                uri,
                effectiveVersion,
                text.Length);
        }
    }

    private DocumentSnapshot Build(TextDocumentItem document, CancellationToken cancellationToken)
    {
        var lines = new DocumentLines(document.Text);
        var bag = new DiagnosticBag();
        string? analysisError = null;

        logger.LogDebug(
            "Analizando {Uri} v{Version} ({Length} caracteres).",
            document.Uri,
            document.Version,
            document.Text.Length);

        LexResult lexResult;
        try
        {
            lexResult = Lexer.Tokenize(document.Text, document.Uri, bag);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            analysisError = ex.Message;
            lexResult = new LexResult(Array.Empty<Token>(), bag);
            logger.LogError(
                ex,
                "Fallo en la fase {Fase} para {Uri} v{Version}.",
                "Lexer",
                document.Uri,
                document.Version);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ParseResult parseResult;
        try
        {
            parseResult = Parser.Parse(lexResult.Tokens, bag);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            analysisError ??= ex.Message;
            parseResult = new ParseResult(
                new CompilationUnitSyntax(null, null, null, null, new TextSpan(0, document.Text.Length)),
                bag);
            logger.LogError(
                ex,
                "Fallo en la fase {Fase} para {Uri} v{Version}.",
                "Parser",
                document.Uri,
                document.Version);
        }

        cancellationToken.ThrowIfCancellationRequested();

        SemanticResult? semanticResult = null;
        try
        {
            semanticResult = SemanticAnalyzer.Analyze(parseResult.Root, bag);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            analysisError ??= ex.Message;
            logger.LogError(
                ex,
                "Fallo en la fase {Fase} para {Uri} v{Version}.",
                "SemanticAnalyzer",
                document.Uri,
                document.Version);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = DiagnosticTranslator.Translate(bag, lines, document.Text.Length);
        if (analysisError is not null)
        {
            var internalDiagnostics = new List<LspDiagnostic>
            {
                new(
                    new LspRange(Position.Zero, Position.Zero),
                    LspDiagnosticSeverity.Error,
                    $"Error interno en análisis LSP: {analysisError}"),
            };
            internalDiagnostics.AddRange(diagnostics);
            diagnostics = internalDiagnostics;
        }

        var highlightTokens = TokenClassifier.Classify(
            document.Text,
            lexResult.Tokens,
            lexResult.Comments,
            lines,
            semanticResult);

        logger.LogDebug(
            "Análisis completado para {Uri} v{Version}: {Diagnosticos} diagnóstico(s), {Tokens} token(s) resaltados.",
            document.Uri,
            document.Version,
            diagnostics.Count,
            highlightTokens.Count);

        return new DocumentSnapshot
        {
            Uri = document.Uri,
            Version = document.Version,
            Text = document.Text,
            Lines = lines,
            LexResult = lexResult,
            ParseResult = parseResult,
            SemanticResult = semanticResult,
            Diagnostics = diagnostics,
            HighlightTokens = highlightTokens,
            AnalysisError = analysisError,
        };
    }

    private sealed class DocumentState
    {
        public object Gate { get; } = new();

        /// <summary>Serializa el pipeline de análisis: como máximo uno en vuelo por documento.</summary>
        public object AnalysisGate { get; } = new();

        public TextDocumentItem? Document { get; set; }

        public DocumentSnapshot? Snapshot { get; set; }
    }
}
