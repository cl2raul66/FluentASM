using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Nombres de bandera de <c>Frf</c> y sus alias legibles, según
/// <c>FLUENT_SYNTAX_X64.md</c> §2.3 y §5.5.
/// </summary>
internal static class FlagCatalog
{
    private static readonly Dictionary<string, FlagSet> Aliases = new(StringComparer.Ordinal)
    {
        ["carry"] = FlagSet.Carry,
        ["below"] = FlagSet.Carry,
        ["aboveOrEqual"] = FlagSet.Carry,
        ["parity"] = FlagSet.Parity,
        ["adjust"] = FlagSet.Adjust,
        ["zero"] = FlagSet.Zero,
        ["equal"] = FlagSet.Zero,
        ["notEqual"] = FlagSet.Zero,
        ["sign"] = FlagSet.Sign,
        ["interrupt"] = FlagSet.Interrupt,
        ["direction"] = FlagSet.Direction,
        ["overflow"] = FlagSet.Overflow,
        ["belowOrEqual"] = FlagSet.Carry | FlagSet.Zero,
        ["above"] = FlagSet.Carry | FlagSet.Zero,
        ["less"] = FlagSet.Sign | FlagSet.Overflow,
        ["greaterOrEqual"] = FlagSet.Sign | FlagSet.Overflow,
        ["lessOrEqual"] = FlagSet.Zero | FlagSet.Sign | FlagSet.Overflow,
        ["greater"] = FlagSet.Zero | FlagSet.Sign | FlagSet.Overflow,
    };

    private static readonly (FlagSet Flag, string Name)[] Names =
    [
        (FlagSet.Carry, "carry"),
        (FlagSet.Parity, "parity"),
        (FlagSet.Adjust, "adjust"),
        (FlagSet.Zero, "zero"),
        (FlagSet.Sign, "sign"),
        (FlagSet.Interrupt, "interrupt"),
        (FlagSet.Direction, "direction"),
        (FlagSet.Overflow, "overflow"),
    ];

    /// <summary>Resuelve un miembro de <c>Frf</c> a las banderas físicas que evalúa.</summary>
    public static bool TryGetFlags(string member, out FlagSet flags) =>
        Aliases.TryGetValue(member, out flags);

    /// <summary>Describe un conjunto de banderas con sus nombres canónicos.</summary>
    public static string Describe(FlagSet flags)
    {
        var names = new List<string>();
        foreach (var (flag, name) in Names)
        {
            if ((flags & flag) != 0)
            {
                names.Add($"Frf.{name}");
            }
        }

        return names.Count == 0 ? "Frf" : string.Join(", ", names);
    }
}
