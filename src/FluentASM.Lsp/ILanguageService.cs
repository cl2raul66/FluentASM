using FluentASM.Lsp.Protocol;
using FluentASM.Lsp.Server;

namespace FluentASM.Lsp;

/// <summary>
/// Contrato del servicio de lenguaje que consume el editor. Desacopla la interfaz
/// gráfica de la implementación concreta y garantiza que todas las operaciones
/// reutilicen el mismo snapshot versionado del documento.
/// </summary>
public interface ILanguageService
{
    /// <summary>Abre un documento con su texto inicial.</summary>
    void OpenDocument(string uri, string text, int version = 1);

    /// <summary>Actualiza el texto de un documento abierto.</summary>
    void UpdateDocument(string uri, string text, int version = 1);

    /// <summary>Cierra un documento y libera sus recursos de análisis.</summary>
    void CloseDocument(string uri);

    /// <summary>Devuelve el texto vigente del documento, o <c>null</c> si no está abierto.</summary>
    string? GetDocumentText(string uri);

    /// <summary>
    /// Devuelve el snapshot de análisis de la versión vigente, calculándolo si
    /// es necesario. El mismo snapshot alimenta diagnósticos, resaltado,
    /// completado, hover y símbolos.
    /// </summary>
    DocumentSnapshot? GetSnapshot(string uri, CancellationToken cancellationToken = default);

    /// <summary>Valida el documento y devuelve sus diagnósticos LSP.</summary>
    IReadOnlyList<LspDiagnostic> ValidateDocument(string uri, CancellationToken cancellationToken = default);

    /// <summary>Genera las sugerencias de autocompletado en la posición indicada.</summary>
    CompletionList GetCompletions(string uri, Position position, CancellationToken cancellationToken = default);

    /// <summary>Devuelve la documentación emergente del elemento situado en la posición.</summary>
    Hover? GetHover(string uri, Position position);

    /// <summary>Devuelve el árbol de símbolos del documento.</summary>
    IReadOnlyList<DocumentSymbol> GetDocumentSymbols(string uri);

    /// <summary>Devuelve los tokens clasificados para el resaltado sintáctico.</summary>
    IReadOnlyList<HighlightToken> GetHighlightTokens(string uri, CancellationToken cancellationToken = default);
}
