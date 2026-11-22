using System;
using System.Collections.Immutable;
using CoreDiagnostics = FluentASM.Core.Diagnostics;
using CoreSemantics = FluentASM.Core.Semantics;
using CoreSyntax = FluentASM.Core.Syntax;

namespace FluentASM.Core.Catalog;

/// <summary>Vista o ventana aplicada sobre un registro.</summary>
public sealed record RegisterViewDescriptor(
    string Suffix,
    string Name,
    int Bits,
    string Effect)
{
    public static readonly RegisterViewDescriptor Full = new("", "Completo (64 bits)", 64, "Reemplaza los 64 bits");
    public static readonly RegisterViewDescriptor Low32 = new(".low32", "32 bits bajos", 32, "Zero-extend: pone a cero bits 32-63");
    public static readonly RegisterViewDescriptor Low16 = new(".low16", "16 bits bajos", 16, "Merge: conserva bits 16-63");
    public static readonly RegisterViewDescriptor Low8 = new(".low8", "8 bits bajos", 8, "Merge: conserva bits 8-63");
    public static readonly RegisterViewDescriptor High8 = new(".high8", "Bits 8-15", 8, "Merge: solo existe en Gpr0-Gpr3");
    public static readonly RegisterViewDescriptor Member = new(".member", "Miembro semantico", 1, "Miembro de Frf/Ipr");
}

/// <summary>Descriptor de un registro de proposito general <c>GprN</c>.</summary>
public sealed record GprDescriptor(
    string Name,
    int Index,
    ImmutableArray<RegisterViewDescriptor> ValidViews,
    bool IsVolatile,
    string Role,
    string HardwareRef)
{
    public string CanonicalName => Name;
    public ImmutableArray<string> ViewNames => ValidViews.Select(v => v.Suffix).ToImmutableArray();
}

/// <summary>Descriptor de una unidad fisica no-GPR (Frf, Ipr, Stack, Cpu, Port).</summary>
public sealed record UnitDescriptor(
    string Name,
    string Description,
    CoreSemantics.RegisterUnit Unit,
    ImmutableArray<RegisterViewDescriptor> ValidViews,
    ImmutableArray<string> Members)
{
    public bool IsGpr => Unit == CoreSemantics.RegisterUnit.Gpr;
}

/// <summary>Descriptor de una bandera fisica de <c>Frf</c>.</summary>
public sealed record FlagDescriptor(
    string Name,
    CoreSemantics.FlagSet Flag,
    int Bit,
    string PhysicalMeaning,
    string WhoUpdates)
{
    public string CanonicalName => $"Frf.{Name}";
}

/// <summary>Alias legible para una condicion de salto.</summary>
public sealed record FlagAliasDescriptor(
    string Alias,
    string CanonicalForm,
    string BitsEvaluated)
{
    public string DisplayName => $"{Alias} ({CanonicalForm})";
}

/// <summary>Descriptor de un verbo canonico.</summary>
public sealed record VerbDescriptor(
    string Name,
    string Description,
    ImmutableArray<string> Receivers,
    int MinArguments,
    int MaxArguments,
    string SemanticDescription,
    ImmutableArray<string> Inputs,
    ImmutableArray<string> Modifies,
    string Returns,
    string FlagsAffected)
{
    public string CanonicalName => Name;
    public string Signature => $"{Name}({string.Join(", ", Receivers)}) args:{MinArguments}..{MaxArguments}";
}

/// <summary>Descriptor de una magnitud fisica <c>Bit:N</c> o <c>Byte:N</c>.</summary>
public sealed record MagnitudeDescriptor(
    string Name,
    string Description,
    string Syntax,
    string UsageContext)
{
    public string CanonicalName => Name;
}

/// <summary>Constructor fluido de nivel superior (<c>.data</c>, <c>.execute</c>, <c>.initialize</c>).</summary>
public sealed record FlowConstructorDescriptor(
    string Name,
    string Description,
    string Syntax,
    string SemanticDescription)
{
    public string CanonicalName => Name;
}

/// <summary>Directiva dentro de <c>.data</c> o <c>.execute</c>.</summary>
public sealed class DirectiveDescriptor
{
    public string Name { get; init; }
    public string CanonicalName { get; init; }
    public string Context { get; init; }
    public string Syntax { get; init; }
    public string Description { get; init; }
    public string FullSyntax => $"{Context} -> {CanonicalName}";

    public DirectiveDescriptor(string name, string canonicalName, string context, string syntax, string description)
    {
        Name = name;
        CanonicalName = canonicalName;
        Context = context;
        Syntax = syntax;
        Description = description;
    }
}

/// <summary>Palabra contextual (<c>defaultValue</c>, <c>if</c>, <c>not</c>, etc.).</summary>
public sealed record ContextualKeywordDescriptor(
    string Name,
    string Description,
    string Syntax,
    string Usage)
{
    public string CanonicalName => Name;
}

/// <summary>Descriptor de un codigo de diagnostico <c>FASMxxxx</c>.</summary>
public sealed record DiagnosticDescriptor(
    string Code,
    string Id,
    string Message,
    CoreDiagnostics.DiagnosticSeverity Severity,
    string Category)
{
    public string FullCode => Code;
}