using System.Collections.Immutable;
using CoreDiagnostics = FluentASM.Core.Diagnostics;
using CoreSemantics = FluentASM.Core.Semantics;
using CoreSyntax = FluentASM.Core.Syntax;

namespace FluentASM.Core.Catalog;

/// <summary>
/// Catalogo publico de solo lectura que expone la verdad del lenguaje FluentASM
/// para arquitectura x64 (Fase 1), construida proyectando directamente desde
/// las estructuras internas de <c>FluentASM.Core</c>.
/// </summary>
/// <remarks>
/// Este catalogo es la **fuente unica de verdad** para el LSP, la IDE y cualquier consumidor externo.
/// Se construye proyectando directamente desde:
/// <list type="bullet">
///   <item><see cref="CoreSemantics.VerbCatalog.Definitions"/> -> verbos</item>
///   <item><see cref="CoreSemantics.FlagCatalog"/> -> banderas y alias</item>
///   <item><see cref="CoreSemantics.RegisterModel"/> -> GPRs, unidades, nombres legacy</item>
///   <item><see cref="CoreSyntax.MagnitudeNames"/> -> magnitudes</item>
///   <item><see cref="CoreDiagnostics.DiagnosticIds"/> -> diagnosticos</item>
/// </list>
/// Solo anade metadatos de presentacion (descripciones, sintaxis, ejemplos) que no existen en Core,
/// vinculados por clave canonica para que cualquier añadido en Core aparezca automaticamente.
/// </remarks>
public static class LanguageCatalog
{
    /// <summary>Arquitecturas soportadas en el catalogo.</summary>
    public static IReadOnlyList<string> Architectures { get; } = ImmutableList.Create("x64");

    // ---------------------------------------------------------------------
    // REGISTROS
    // ---------------------------------------------------------------------

    /// <summary>Registros de proposito general con sus vistas validas.</summary>
    public static IReadOnlyList<GprDescriptor> Gprs { get; } = X64LanguageCatalogProvider.GetGprDescriptors().ToImmutableList();

    /// <summary>Unidades fisicas no-GPR (Frf, Ipr, Stack, Cpu, Port).</summary>
    public static IReadOnlyList<UnitDescriptor> Units { get; } = X64LanguageCatalogProvider.GetUnitDescriptors().ToImmutableList();

    /// <summary>Nombres legacy prohibidos con su sugerencia canonica.</summary>
    public static IReadOnlyDictionary<string, string> LegacyNames { get; } = X64LanguageCatalogProvider.GetLegacyNames();

    // ---------------------------------------------------------------------
    // BANDERAS (Frf)
    // ---------------------------------------------------------------------

    /// <summary>Banderas fisicas del registro <c>Frf</c>.</summary>
    public static IReadOnlyList<FlagDescriptor> Flags { get; } = X64LanguageCatalogProvider.GetFlagDescriptors().ToImmutableList();

    /// <summary>Alias legibles para condiciones de salto.</summary>
    public static IReadOnlyList<FlagAliasDescriptor> FlagAliases { get; } = X64LanguageCatalogProvider.GetFlagAliasDescriptors().ToImmutableList();

    // ---------------------------------------------------------------------
    // VERBOS
    // ---------------------------------------------------------------------

    /// <summary>Verbos canonicos de Fase 1 con su descripcion completa.</summary>
    public static IReadOnlyList<VerbDescriptor> Verbs { get; } = X64LanguageCatalogProvider.GetVerbDescriptors().ToImmutableList();

    // ---------------------------------------------------------------------
    // MAGNITUDES
    // ---------------------------------------------------------------------

    /// <summary>Magnitudes fisicas <c>Bit:N</c> y <c>Byte:N</c> y sus multiplos.</summary>
    public static IReadOnlyList<MagnitudeDescriptor> Magnitudes { get; } = X64LanguageCatalogProvider.GetMagnitudeDescriptors().ToImmutableList();

    // ---------------------------------------------------------------------
    // PALABRAS CLAVE DEL FLUJO TYPE-STATE
    // ---------------------------------------------------------------------

    /// <summary>Constructores fluidos de nivel superior (<c>.data</c>, <c>.execute</c>, <c>.initialize</c>).</summary>
    public static IReadOnlyList<FlowConstructorDescriptor> FlowConstructors { get; } = X64LanguageCatalogProvider.GetFlowConstructorDescriptors().ToImmutableList();

    /// <summary>
    /// Constructores de flujo indexados por nombre canonico (incluido el punto
    /// inicial) para que la IDE y el LSP resuelvan su aridad sin recorrer la lista.
    /// </summary>
    public static IReadOnlyDictionary<string, FlowConstructorDescriptor> FlowConstructorsByName { get; } =
        FlowConstructors.ToImmutableDictionary(descriptor => descriptor.CanonicalName, StringComparer.Ordinal);

    /// <summary>Directivas dentro de <c>.data</c> y <c>.execute</c>.</summary>
    public static IReadOnlyList<DirectiveDescriptor> Directives { get; } = X64LanguageCatalogProvider.GetDirectiveDescriptors().ToImmutableList();

    /// <summary>Palabras contextuales (<c>defaultValue</c>, <c>if</c>, <c>not</c>, etc.).</summary>
    public static IReadOnlyList<ContextualKeywordDescriptor> ContextualKeywords { get; } = X64LanguageCatalogProvider.GetContextualKeywordDescriptors().ToImmutableList();

    // ---------------------------------------------------------------------
    // DIAGNOSTICOS
    // ---------------------------------------------------------------------

    /// <summary>Codigos de diagnostico estables <c>FASMxxxx</c>.</summary>
    public static IReadOnlyList<DiagnosticDescriptor> Diagnostics { get; } = X64LanguageCatalogProvider.GetDiagnosticDescriptors().ToImmutableList();
}