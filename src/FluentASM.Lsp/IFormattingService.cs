using FluentASM.Lsp.Protocol;

namespace FluentASM.Lsp;

/// <summary>
/// Contrato del motor de formateo de FluentASM. Todas las operaciones parten
/// del mismo documento versionado que el resto del servicio de lenguaje y
/// devuelven ediciones mínimas, ordenadas y sin solapamientos.
/// </summary>
/// <remarks>
/// La interfaz es independiente de WinUI para que la Fase 3 pueda exponerla
/// mediante JSON-RPC (<c>textDocument/formatting</c>,
/// <c>textDocument/rangeFormatting</c> y <c>textDocument/onTypeFormatting</c>)
/// sin duplicar reglas.
/// </remarks>
public interface IFormattingService
{
    /// <summary>Formatea el documento completo.</summary>
    /// <param name="uri">URI del documento abierto en el servicio.</param>
    IReadOnlyList<TextEdit> FormatDocument(string uri);

    /// <summary>
    /// Formatea solo las líneas tocadas por <paramref name="range"/>. La
    /// indentación se calcula desde el inicio del documento para mantener la
    /// coherencia de los bloques.
    /// </summary>
    IReadOnlyList<TextEdit> FormatRange(string uri, LspRange range);

    /// <summary>
    /// Formateo <em>on-type</em> acotado. Solo se admiten <c>}</c> y el
    /// <c>&gt;</c> que completa <c>-&gt;</c>; cualquier otro disparador
    /// devuelve una lista vacía.
    /// </summary>
    IReadOnlyList<TextEdit> FormatOnType(string uri, Position position, char trigger);

    /// <summary>
    /// Unidad de indentación efectiva para el documento (un tabulador o
    /// <c>indent_size</c> espacios). La Capa A la usa para <c>Enter</c> sin
    /// leer <c>.editorconfig</c> por su cuenta; si el documento no está
    /// abierto, devuelve el valor del repositorio.
    /// </summary>
    string GetIndentUnit(string uri);
}
