namespace FluentASM.Lsp.Protocol;

/// <summary>
/// Tipos de ítems de autocompletado según LSP.
/// </summary>
public enum CompletionItemKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25
}

/// <summary>
/// Metadatos enriquecidos de FluentASM para el control IntelliSense / IDE.
/// </summary>
public sealed record FluentItemMetadata(
    string Category,
    string Signature,
    string Description,
    string Inputs,
    string Modifies,
    string Returns,
    string? FlagsAffected = null);

/// <summary>
/// Formato del texto insertado por un completado.
/// </summary>
public enum InsertTextFormat
{
    /// <summary>Texto plano.</summary>
    PlainText = 1,

    /// <summary>Fragmento con marcadores como <c>$0</c>.</summary>
    Snippet = 2,
}

/// <summary>
/// Ítem de autocompletado propuesto por el Language Server.
/// </summary>
/// <param name="Label">Texto visible del suggestion.</param>
/// <param name="Kind">Clase LSP del suggestion.</param>
/// <param name="Detail">Resumen breve.</param>
/// <param name="Documentation">Documentación enrichida.</param>
/// <param name="InsertText">Texto a insertar; puede contener el marcador <c>$0</c>.</param>
/// <param name="FilterText">Texto usado para filtrar y ordenar.</param>
/// <param name="SortText">Clave de ordenación del suggestion.</param>
/// <param name="Metadata">Metadatos enriquecidos para la IDE.</param>
/// <param name="TextEdit">Edición exacta que reemplaza el prefijo parcial.</param>
/// <param name="Category">Categoría canónica mostrada por la IDE.</param>
/// <param name="CaretOffset">Posición del caret dentro del texto insertado, relativa a su inicio.</param>
public sealed record LspCompletionItem(
    string Label,
    CompletionItemKind Kind,
    string? Detail = null,
    MarkupContent? Documentation = null,
    string? InsertText = null,
    string? FilterText = null,
    string? SortText = null,
    FluentItemMetadata? Metadata = null,
    LspTextEdit? TextEdit = null,
    string? Category = null,
    int? CaretOffset = null,
    InsertTextFormat InsertTextFormat = InsertTextFormat.PlainText);

/// <summary>
/// Lista de autocompletados devueltos por el Language Server.
/// </summary>
/// <param name="IsIncomplete">Indica si deben recalcularse al continuar escribiendo.</param>
/// <param name="Items">Suggestions filtrados y ordenados.</param>
/// <param name="ReplaceRange">Rango compartido que reemplazan las ediciones de los ítems.</param>
/// <param name="Context">Estado contextual o <c>type-state</c> detectado.</param>
/// <param name="Caret">Posición solicitada del completado.</param>
/// <param name="Version">Versión efectiva del documento analizado.</param>
public sealed record CompletionList(
    bool IsIncomplete,
    IReadOnlyList<LspCompletionItem> Items,
    LspRange? ReplaceRange = null,
    string? Context = null,
    Position? Caret = null,
    int? Version = null);


