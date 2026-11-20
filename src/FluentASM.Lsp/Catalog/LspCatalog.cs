using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentASM.Core.Catalog;
using FluentASM.Lsp.Protocol;

namespace FluentASM.Lsp.Catalog;

/// <summary>
/// Catálogo LSP construido dinámicamente desde <see cref="LanguageCatalog"/> de FluentASM.Core.
/// Elimina todos los ítems legacy hardcodeados y proyecta directamente desde la fuente única de verdad.
/// </summary>
public static class LspCatalog
{
    private static readonly ImmutableArray<LspCompletionItem> AllItems;
    private static readonly ImmutableDictionary<string, LspCompletionItem> ItemsByLookup;

    static LspCatalog()
    {
        var items = ImmutableArray.CreateBuilder<LspCompletionItem>();

        foreach (var gpr in LanguageCatalog.Gprs) { AddRegister(items, gpr); }
        foreach (var unit in LanguageCatalog.Units) { AddUnit(items, unit); }
        foreach (var flag in LanguageCatalog.Flags) { AddFlagMember(items, flag); }
        foreach (var alias in LanguageCatalog.FlagAliases) { AddFlagAlias(items, alias); }
        foreach (var verb in LanguageCatalog.Verbs) { AddVerb(items, verb); }
        foreach (var mag in LanguageCatalog.Magnitudes) { AddMagnitude(items, mag); }
        foreach (var fc in LanguageCatalog.FlowConstructors) { AddFlowConstructor(items, fc); }
        foreach (var dir in LanguageCatalog.Directives) { AddDirective(items, dir); }
        foreach (var ck in LanguageCatalog.ContextualKeywords) { AddContextualKeyword(items, ck); }
        foreach (var diag in LanguageCatalog.Diagnostics) { AddDiagnostic(items, diag); }
        foreach (var legacy in LanguageCatalog.LegacyNames) { AddLegacyName(items, legacy.Key, legacy.Value); }

        AllItems = items.ToImmutable();

        var lookup = ImmutableDictionary.CreateBuilder<string, LspCompletionItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in AllItems)
        {
            lookup[item.Label] = item;
            if (!string.IsNullOrEmpty(item.InsertText) && !lookup.ContainsKey(item.InsertText))
            {
                lookup[item.InsertText] = item;
            }

            if (!string.IsNullOrEmpty(item.FilterText) && !lookup.ContainsKey(item.FilterText))
            {
                lookup[item.FilterText] = item;
            }
        }
        ItemsByLookup = lookup.ToImmutable();
    }

    public static IReadOnlyList<LspCompletionItem> GetAllItems() => AllItems;

    public static bool TryGetItem(string name, out LspCompletionItem item) =>
        ItemsByLookup.TryGetValue(name, out item!);

    private static string GetViewSortText(string suffix) => suffix switch
    {
        ".low32" => "02",
        ".low16" => "03",
        ".low8" => "03",
        ".high8" => "04",
        _ => "05"
    };

    private static void AddRegister(ImmutableArray<LspCompletionItem>.Builder items, GprDescriptor gpr)
    {
        var name = gpr.Name;
        var role = gpr.Role;

        var detailBase = "Registro x64 de 64 bits [Gpr" + gpr.Index + "]";
        var docBase = "### `" + gpr.Name + "` (Registro General x64)\n\n" + role + ".\n\n- **Tamaño:** 64 bits (QWord)\n- **Sub-vistas:** `" + string.Join("`, `", gpr.ViewNames) + "`";

        var baseMeta = new FluentItemMetadata(
            Category: "Registros",
            Signature: gpr.Name + ": QWord",
            Description: role,
            Inputs: "Ninguno",
            Modifies: gpr.Name + " (64 bits)",
            Returns: "Celda de registro",
            FlagsAffected: "Ninguna");

        items.Add(new LspCompletionItem(
            Label: gpr.Name,
            Kind: CompletionItemKind.Variable,
            Detail: "Registro x64 de 64 bits [Gpr" + gpr.Index + "]",
            Documentation: MarkupContent.Markdown(docBase),
            InsertText: gpr.Name,
            FilterText: gpr.Name,
            SortText: "01_" + gpr.Name,
            Metadata: baseMeta));

        foreach (var view in LanguageCatalog.Gprs.First(g => g.Name == gpr.Name).ValidViews)
        {
            if (view.Suffix != "")
            {
                var viewDetail = view.Name;
                var viewSig = gpr.Name + view.Suffix + ": " + view.Bits + " bits";

                var meta = new FluentItemMetadata(
                    Category: "Registros",
                    Signature: gpr.Name + view.Suffix + ": " + view.Bits + " bits",
                    Description: view.Effect,
                    Inputs: gpr.Name,
                    Modifies: gpr.Name + " (" + view.Bits + " bits)",
                    Returns: "Vista " + view.Bits + " bits",
                    FlagsAffected: "Ninguna");

                items.Add(new LspCompletionItem(
                    Label: gpr.Name + view.Suffix,
                    Kind: CompletionItemKind.Field,
                    Detail: viewDetail,
                    Documentation: MarkupContent.Markdown(view.Effect),
                    InsertText: gpr.Name + view.Suffix,
                    FilterText: gpr.Name + view.Suffix,
                    SortText: GetViewSortText(view.Suffix) + "_" + gpr.Name,
                    Metadata: meta));
            }
        }
    }

    private static void AddUnit(ImmutableArray<LspCompletionItem>.Builder items, UnitDescriptor unit)
    {
        var kind = unit.IsGpr ? CompletionItemKind.Variable : CompletionItemKind.Field;
        var detail = unit.Description;

        var doc = unit.Description;
        var meta = new FluentItemMetadata(
            Category: "Registros",
            Signature: unit.Name + ": " + detail,
            Description: detail,
            Inputs: string.Join(", ", unit.Members),
            Modifies: unit.Name,
            Returns: unit.Name,
            FlagsAffected: "Ninguna");

        var item = new LspCompletionItem(
            Label: unit.Name,
            Kind: kind,
            Detail: detail,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: unit.Name,
            FilterText: unit.Name,
            SortText: "02_" + unit.Name,
            Metadata: meta);

        items.Add(item);

        foreach (var member in unit.Members)
        {
            var memMeta = new FluentItemMetadata(
                Category: "Registros",
                Signature: unit.Name + "." + member,
                Description: "Acceso al miembro `" + member + "` de `" + unit.Name + "`.",
                Inputs: unit.Name,
                Modifies: "Lectura",
                Returns: member,
                FlagsAffected: "Ninguna");

            items.Add(new LspCompletionItem(
                Label: unit.Name + "." + member,
                Kind: CompletionItemKind.Field,
                Detail: "Miembro " + member + " de " + unit.Name,
                Documentation: MarkupContent.Markdown("Miembro `" + member + "` de la unidad `" + unit.Name + "`."),
                InsertText: unit.Name + "." + member,
                FilterText: unit.Name + "." + member,
                SortText: "03_" + unit.Name + "_" + member,
                Metadata: memMeta));
        }
    }

    private static void AddFlagMember(ImmutableArray<LspCompletionItem>.Builder items, FlagDescriptor flag)
    {
        var doc = flag.PhysicalMeaning;
        var meta = new FluentItemMetadata(
            Category: "Registros",
            Signature: "Frf." + flag.Name + ": Bit",
            Description: flag.PhysicalMeaning,
            Inputs: "ALU status",
            Modifies: "Lectura",
            Returns: "Bit (0 o 1)",
            FlagsAffected: "Lectura");

        items.Add(new LspCompletionItem(
            Label: "Frf." + flag.Name,
            Kind: CompletionItemKind.Field,
            Detail: flag.PhysicalMeaning,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: "Frf." + flag.Name,
            FilterText: "Frf." + flag.Name,
            SortText: "04_Frf." + flag.Name,
            Metadata: meta));
    }

    private static void AddFlagAlias(ImmutableArray<LspCompletionItem>.Builder items, FlagAliasDescriptor alias)
    {
        var doc = "Alias legible: `" + alias.CanonicalForm + "`\n\nBits evaluados: " + alias.BitsEvaluated;
        var meta = new FluentItemMetadata(
            Category: "Registros",
            Signature: "Frf." + alias.Alias + " (" + alias.CanonicalForm + ")",
            Description: "Alias legible para `" + alias.CanonicalForm + "`." + "\n\nBits evaluados: " + alias.BitsEvaluated,
            Inputs: "ALU status",
            Modifies: "Lectura",
            Returns: "Bit (0 o 1)",
            FlagsAffected: "Lectura");

        items.Add(new LspCompletionItem(
            Label: "Frf." + alias.Alias,
            Kind: CompletionItemKind.Field,
            Detail: "Alias legible para " + alias.CanonicalForm,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: "Frf." + alias.Alias,
            FilterText: "Frf." + alias.Alias,
            SortText: "05_" + alias.Alias,
            Metadata: meta));
    }

    private static void AddVerb(ImmutableArray<LspCompletionItem>.Builder items, VerbDescriptor verb)
    {
        var receivers = string.Join(", ", verb.Receivers);
        var flagsAffected = verb.FlagsAffected == "Ninguna" ? "Ninguna" : verb.FlagsAffected;

        var doc = verb.SemanticDescription + "\n\n**Firma:** `" + verb.Signature + "`\n\n**Receptores:** " + receivers + "\n\n**Argumentos:** " + verb.MinArguments + ".." + verb.MaxArguments + "\n\n**Banderas afectadas:** " + flagsAffected;

        var meta = new FluentItemMetadata(
            Category: "Verbos",
            Signature: verb.Signature,
            Description: verb.SemanticDescription,
            Inputs: string.Join(", ", verb.Inputs),
            Modifies: string.Join(", ", verb.Modifies),
            Returns: verb.Returns,
            FlagsAffected: flagsAffected);

        items.Add(new LspCompletionItem(
            Label: verb.Name + "($0)",
            Kind: CompletionItemKind.Method,
            Detail: verb.Description,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: verb.Name + "($0)",
            FilterText: verb.Name,
            SortText: "10_" + verb.Name,
            Metadata: meta));
    }

    private static void AddMagnitude(ImmutableArray<LspCompletionItem>.Builder items, MagnitudeDescriptor mag)
    {
        var doc = mag.Description + "\n\nSintaxis: `" + mag.Syntax + "`\n\nContexto: " + mag.UsageContext;
        var meta = new FluentItemMetadata(
            Category: "Magnitudes",
            Signature: mag.Syntax,
            Description: mag.Description,
            Inputs: "Valor numérico N",
            Modifies: "Tipo de operando",
            Returns: "Literal tipado",
            FlagsAffected: "Ninguna");

        items.Add(new LspCompletionItem(
            Label: mag.Name,
            Kind: CompletionItemKind.Keyword,
            Detail: mag.Description,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: mag.Name + ":N",
            FilterText: mag.Name,
            SortText: "30_" + mag.Name,
            Metadata: meta));
    }

    private static void AddFlowConstructor(ImmutableArray<LspCompletionItem>.Builder items, FlowConstructorDescriptor fc)
    {
        var doc = fc.Description + "\n\nSintaxis: `" + fc.Syntax + "`\n\n" + fc.SemanticDescription;
        var meta = new FluentItemMetadata(
            Category: "Flujo",
            Signature: fc.Syntax,
            Description: fc.SemanticDescription,
            Inputs: "Constructor fluido",
            Modifies: "Estructura del programa",
            Returns: "Programa completo",
            FlagsAffected: "Ninguna");

        items.Add(new LspCompletionItem(
            Label: fc.Name,
            Kind: CompletionItemKind.Keyword,
            Detail: fc.Description,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: fc.Name,
            FilterText: fc.Name,
            SortText: "40_" + fc.Name,
            Metadata: meta));
    }

    private static void AddDirective(ImmutableArray<LspCompletionItem>.Builder items, DirectiveDescriptor dir)
    {
        var doc = dir.Description + "\n\nContexto: " + dir.Context + "\n\nSintaxis: `" + dir.Syntax + "`";
        var meta = new FluentItemMetadata(
            Category: "Directivas",
            Signature: dir.Context + " -> " + dir.CanonicalName,
            Description: dir.Description,
            Inputs: "Según sintaxis",
            Modifies: "Estructura del programa",
            Returns: "Símbolo configurado",
            FlagsAffected: "Ninguna");

        items.Add(new LspCompletionItem(
            Label: dir.Name,
            Kind: CompletionItemKind.Keyword,
            Detail: dir.Description,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: dir.Name,
            FilterText: dir.Name,
            SortText: "45_" + dir.Name,
            Metadata: meta));
    }

    private static void AddContextualKeyword(ImmutableArray<LspCompletionItem>.Builder items, ContextualKeywordDescriptor ck)
    {
        var doc = ck.Description + "\n\nSintaxis: `" + ck.Syntax + "`\n\n" + ck.Usage;
        var meta = new FluentItemMetadata(
            Category: "Contexto",
            Signature: ck.Syntax,
            Description: ck.Description,
            Inputs: "Según contexto",
            Modifies: "Según uso",
            Returns: "Según uso",
            FlagsAffected: "Ninguna");

        items.Add(new LspCompletionItem(
            Label: ck.Name,
            Kind: CompletionItemKind.Keyword,
            Detail: ck.Description,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: ck.Name,
            FilterText: ck.Name,
            SortText: "50_" + ck.Name,
            Metadata: meta));
    }

    private static void AddDiagnostic(ImmutableArray<LspCompletionItem>.Builder items, DiagnosticDescriptor diag)
    {
        var doc = "**Código:** `" + diag.Code + "`\n\n**Mensaje:** " + diag.Message + "\n\n**Severidad:** " + diag.Severity + "\n\n**Categoría:** " + diag.Category;
        var meta = new FluentItemMetadata(
            Category: "Diagnósticos",
            Signature: diag.Code + ": " + diag.Message,
            Description: diag.Message,
            Inputs: "N/A",
            Modifies: "N/A",
            Returns: "Código de diagnóstico",
            FlagsAffected: "Ninguna");

        items.Add(new LspCompletionItem(
            Label: diag.Code,
            Kind: CompletionItemKind.Keyword,
            Detail: diag.Message,
            Documentation: MarkupContent.Markdown(doc),
            InsertText: diag.Code,
            FilterText: diag.Code,
            SortText: "60_" + diag.Code,
            Metadata: meta));
    }

    private static void AddLegacyName(ImmutableArray<LspCompletionItem>.Builder items, string legacy, string canonical)
    {
        var doc = "`" + legacy + "` es un nombre legacy **prohibido** (FASM3012).\n\nUse el nombre canónico: `" + canonical + "`.";
        var meta = new FluentItemMetadata(
            Category: "Legacy (prohibido)",
            Signature: legacy + " -> " + canonical,
            Description: "Nombre legacy prohibido. Use `" + canonical + "`.",
            Inputs: "N/A",
            Modifies: "N/A",
            Returns: "Sugerencia de corrección",
            FlagsAffected: "Ninguna");

        items.Add(new LspCompletionItem(
            Label: legacy,
            Kind: CompletionItemKind.Keyword,
            Detail: "Nombre legacy -> use `" + canonical + "`",
            Documentation: MarkupContent.Markdown(doc),
            InsertText: canonical,
            FilterText: legacy,
            SortText: "99_" + legacy,
            Metadata: meta));
    }
}