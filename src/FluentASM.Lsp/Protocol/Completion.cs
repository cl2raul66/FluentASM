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
/// Ítem de autocompletado propuesto por el Language Server.
/// </summary>
public sealed record LspCompletionItem(
    string Label,
    CompletionItemKind Kind,
    string? Detail = null,
    MarkupContent? Documentation = null,
    string? InsertText = null,
    string? FilterText = null,
    string? SortText = null,
    FluentItemMetadata? Metadata = null);

/// <summary>
/// Lista de autocompletados devueltos por el Language Server.
/// </summary>
public sealed record CompletionList(
    bool IsIncomplete,
    IReadOnlyList<LspCompletionItem> Items);


