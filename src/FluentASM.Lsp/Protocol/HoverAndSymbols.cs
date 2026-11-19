namespace FluentASM.Lsp.Protocol;

/// <summary>
/// Contenido de texto enriquecido (Markdown o texto plano) según LSP.
/// </summary>
public sealed record MarkupContent(string Kind, string Value)
{
    public static MarkupContent Markdown(string markdown) => new("markdown", markdown);
    public static MarkupContent PlainText(string text) => new("plaintext", text);
}

/// <summary>
/// Información de ayuda emergente (Hover) al situar el cursor sobre un elemento.
/// </summary>
public sealed record Hover(MarkupContent Contents, LspRange? Range = null);

/// <summary>
/// Tipo de símbolo de documento según LSP.
/// </summary>
public enum SymbolKind
{
    File = 1,
    Module = 2,
    Namespace = 3,
    Package = 4,
    Class = 5,
    Method = 6,
    Property = 7,
    Field = 8,
    Constructor = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    String = 15,
    Number = 16,
    Boolean = 17,
    Array = 18,
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    Struct = 23,
    Event = 24,
    Operator = 25,
    TypeParameter = 26
}

/// <summary>
/// Representa un símbolo (rutina, etiqueta, sección) en el árbol del documento.
/// </summary>
public sealed record DocumentSymbol(
    string Name,
    SymbolKind Kind,
    LspRange Range,
    LspRange SelectionRange,
    string? Detail = null,
    IReadOnlyList<DocumentSymbol>? Children = null);

/// <summary>
/// Representa un documento de texto gestionado en el servidor LSP.
/// </summary>
public sealed record TextDocumentItem(
    string Uri,
    string LanguageId,
    int Version,
    string Text);

/// <summary>
/// Identificador de documento.
/// </summary>
public sealed record TextDocumentIdentifier(string Uri);

/// <summary>
/// Parámetros de posición en un documento.
/// </summary>
public sealed record TextDocumentPositionParams(
    TextDocumentIdentifier TextDocument,
    Position Position);
