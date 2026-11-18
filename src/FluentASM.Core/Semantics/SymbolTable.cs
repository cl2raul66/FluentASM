namespace FluentASM.Core.Semantics;

/// <summary>
/// Tabla de símbolos del programa: datos de <c>.data(...)</c>, rutinas de
/// <c>.execute(...)</c> y etiquetas locales por rutina.
/// </summary>
public sealed class SymbolTable
{
    private readonly IReadOnlyDictionary<string, DataSymbol> data;
    private readonly IReadOnlyDictionary<string, RoutineSymbol> routines;
    private readonly IReadOnlyDictionary<RoutineSymbol, IReadOnlyDictionary<string, LabelSymbol>> labels;

    internal SymbolTable(
        IReadOnlyDictionary<string, DataSymbol> data,
        IReadOnlyDictionary<string, RoutineSymbol> routines,
        IReadOnlyDictionary<RoutineSymbol, IReadOnlyDictionary<string, LabelSymbol>> labels)
    {
        this.data = data;
        this.routines = routines;
        this.labels = labels;
    }

    /// <summary>Símbolos de <c>.data(...)</c> en orden de declaración.</summary>
    public IReadOnlyList<DataSymbol> DataSymbols => [.. data.Values];

    /// <summary>Rutinas de <c>.execute(...)</c> en orden de declaración.</summary>
    public IReadOnlyList<RoutineSymbol> Routines => [.. routines.Values];

    /// <summary>Total de etiquetas locales registradas en todas las rutinas.</summary>
    public int LabelCount => labels.Values.Sum(scope => scope.Count);

    /// <summary>Busca un símbolo de datos por nombre.</summary>
    public bool TryGetData(string? name, out DataSymbol symbol)
    {
        if (name is null)
        {
            symbol = null!;
            return false;
        }

        return data.TryGetValue(name, out symbol!);
    }

    /// <summary>Busca una rutina por nombre.</summary>
    public bool TryGetRoutine(string? name, out RoutineSymbol symbol)
    {
        if (name is null)
        {
            symbol = null!;
            return false;
        }

        return routines.TryGetValue(name, out symbol!);
    }

    /// <summary>Busca una etiqueta local dentro del ámbito de una rutina.</summary>
    public bool TryGetLabel(RoutineSymbol routine, string name, out LabelSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(routine);

        if (labels.TryGetValue(routine, out var scope) && scope.TryGetValue(name, out symbol!))
        {
            return true;
        }

        symbol = null!;
        return false;
    }

    /// <summary>Indica si existe una etiqueta local en el ámbito de la rutina.</summary>
    public bool LabelExists(RoutineSymbol routine, string name)
    {
        ArgumentNullException.ThrowIfNull(routine);
        return labels.TryGetValue(routine, out var scope) && scope.ContainsKey(name);
    }

    /// <summary>Etiquetas locales de una rutina.</summary>
    public IReadOnlyDictionary<string, LabelSymbol> LabelsOf(RoutineSymbol routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        return labels.TryGetValue(routine, out var scope)
            ? scope
            : new Dictionary<string, LabelSymbol>(StringComparer.Ordinal);
    }
}
