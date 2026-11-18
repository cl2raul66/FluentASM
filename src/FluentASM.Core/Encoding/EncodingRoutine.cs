using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Encoding;

/// <summary>
/// Vista mínima de una rutina lista para codificar: parámetros resueltos y
/// etiquetas locales, sin depender de símbolos privados del analizador.
/// </summary>
internal sealed class EncodingRoutine
{
    private readonly Dictionary<string, LabelSymbol> labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoutineParameter> parameters = new(StringComparer.Ordinal);

    private EncodingRoutine(RoutineDefinitionSyntax definition, string? name)
    {
        Definition = definition;
        Name = name;
    }

    /// <summary>Definición sintáctica original.</summary>
    public RoutineDefinitionSyntax Definition { get; }

    /// <summary>Nombre de la rutina, o <see langword="null"/> en la forma implícita.</summary>
    public string? Name { get; }

    /// <summary>Parámetros de entrada en orden de declaración.</summary>
    public IReadOnlyCollection<RoutineParameter> Parameters => parameters.Values;

    /// <summary>Etiquetas locales declaradas en el cuerpo.</summary>
    public IReadOnlyCollection<LabelSymbol> Labels => labels.Values;

    /// <summary>Construye la vista de codificación de una rutina del AST.</summary>
    public static EncodingRoutine Create(RoutineDefinitionSyntax definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var routine = new EncodingRoutine(definition, definition.Name?.Text);
        routine.BindParameters();
        routine.BindLabels();
        return routine;
    }

    /// <summary>Busca una etiqueta local por su nombre con prefijo <c>@</c>.</summary>
    public bool TryGetLabel(string name, out LabelSymbol symbol) =>
        labels.TryGetValue(name, out symbol!);

    /// <summary>Busca el parámetro de entrada por su alias.</summary>
    public bool TryGetParameter(string name, out RoutineParameter parameter) =>
        parameters.TryGetValue(name, out parameter!);

    private void BindParameters()
    {
        if (Definition.Run is not { } run)
        {
            return;
        }

        for (var index = 0; index < run.Parameters.Count; index++)
        {
            var parameter = run.Parameters[index];
            var register = RegisterModel.InputRegister(index);
            if (register < 0 || parameter.Magnitude is not { } magnitudeSyntax)
            {
                continue;
            }

            if (!MagnitudeInfo.TryCreate(magnitudeSyntax, out var magnitude))
            {
                continue;
            }

            parameters.TryAdd(
                parameter.Name.Text,
                new RoutineParameter(parameter.Name.Text, magnitude, register));
        }
    }

    private void BindLabels()
    {
        foreach (var statement in Definition.Body)
        {
            if (statement is LabelStatementSyntax label)
            {
                labels.TryAdd(label.Label.Text, new LabelSymbol(label.Label.Text, label.Label));
            }
        }
    }
}

/// <summary>Símbolo de <c>.data(...)</c> con su definición sintáctica original.</summary>
internal sealed record EncodingDataSymbol(string Name, MagnitudeInfo Capacity, DataDefinitionSyntax Definition);
