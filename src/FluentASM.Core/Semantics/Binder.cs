using System.Text;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Construye la tabla de símbolos de un programa: datos de <c>.data(...)</c>,
/// rutinas de <c>.execute(...)</c> y etiquetas locales por rutina, verificando
/// duplicados, capacidad del payload y contratos básicos de firma.
/// </summary>
internal sealed class Binder
{
    private static readonly string[] ReservedIdentifiers =
    [
        "Bit", "Byte", "KiloBit", "MegaBit", "GigaBit", "TeraBit",
        "KiloByte", "MegaByte", "GigaByte", "TeraByte",
        "defaultValue", "Port", "Frf", "Ipr", "Stack", "Cpu",
    ];

    private readonly DiagnosticBag diagnostics;
    private readonly Dictionary<string, DataSymbol> data = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoutineSymbol> routines = new(StringComparer.Ordinal);
    private readonly Dictionary<RoutineSymbol, IReadOnlyDictionary<string, LabelSymbol>> labels = [];

    /// <summary>Crea un binder que acumula diagnósticos en la bolsa indicada.</summary>
    public Binder(DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.diagnostics = diagnostics;
    }

    /// <summary>Recorre el árbol y devuelve la tabla de símbolos resultante.</summary>
    public SymbolTable Bind(CompilationUnitSyntax root)
    {
        ArgumentNullException.ThrowIfNull(root);

        BindData(root.Data);
        BindRoutines(root.Execute);
        return new SymbolTable(data, routines, labels);
    }

    private void BindData(DataSectionSyntax? section)
    {
        if (section is null)
        {
            return;
        }

        foreach (var definition in section.Definitions)
        {
            if (definition.Name is not { } nameToken)
            {
                continue;
            }

            var name = nameToken.Text;
            if (RegisterModel.TryGetLegacySuggestion(name, out var legacySuggestion))
            {
                diagnostics.Error(
                    DiagnosticIds.LegacyName,
                    $"'{name}' es un nombre legacy prohibido; usa '{legacySuggestion}'.",
                    nameToken.Location,
                    nameToken.Span);
                continue;
            }

            if (IsReserved(name))
            {
                diagnostics.Error(
                    DiagnosticIds.ReservedIdentifier,
                    $"El identificador '{name}' está reservado por el modelo físico y no puede usarse como símbolo de datos.",
                    nameToken.Location,
                    nameToken.Span);
                continue;
            }

            if (data.ContainsKey(name) || routines.ContainsKey(name))
            {
                diagnostics.Error(
                    DiagnosticIds.DuplicateSymbol,
                    $"El símbolo '{name}' ya está declarado en este programa.",
                    nameToken.Location,
                    nameToken.Span);
                continue;
            }

            if (!MagnitudeInfo.TryCreate(definition.Capacity, out var capacity))
            {
                diagnostics.Error(
                    DiagnosticIds.InvalidMagnitude,
                    $"La capacidad de '{name}' no es una magnitud válida.",
                    null,
                    definition.Capacity.Span);
                continue;
            }

            if (!capacity.IsByte)
            {
                diagnostics.Error(
                    DiagnosticIds.ByteContextExpected,
                    $"La capacidad de '{name}' debe expresarse en Byte:N (se encontró {capacity.Display}).",
                    definition.Capacity.Unit.Location,
                    definition.Capacity.Span);
                continue;
            }

            ValidatePayload(definition, name, capacity);
            data[name] = new DataSymbol(name, capacity, nameToken);
        }
    }

    private void BindRoutines(ExecuteSectionSyntax? section)
    {
        if (section is null)
        {
            return;
        }

        foreach (var definition in section.Routines)
        {
            var symbol = BindRoutine(definition);
            if (symbol is null)
            {
                continue;
            }

            if (symbol.Name is not null)
            {
                routines[symbol.Name] = symbol;
            }

            labels[symbol] = BindLabels(symbol);
        }
    }

    private RoutineSymbol? BindRoutine(RoutineDefinitionSyntax definition)
    {
        if (definition.Name is { } nameToken)
        {
            var name = nameToken.Text;
            if (RegisterModel.TryGetLegacySuggestion(name, out var legacySuggestion))
            {
                diagnostics.Error(
                    DiagnosticIds.LegacyName,
                    $"'{name}' es un nombre legacy prohibido; usa '{legacySuggestion}'.",
                    nameToken.Location,
                    nameToken.Span);
                return null;
            }

            if (IsReserved(name))
            {
                diagnostics.Error(
                    DiagnosticIds.ReservedIdentifier,
                    $"El identificador '{name}' está reservado por el modelo físico y no puede usarse como rutina.",
                    nameToken.Location,
                    nameToken.Span);
                return null;
            }

            if (routines.ContainsKey(name) || data.ContainsKey(name))
            {
                diagnostics.Error(
                    DiagnosticIds.DuplicateSymbol,
                    $"El símbolo '{name}' ya está declarado en este programa.",
                    nameToken.Location,
                    nameToken.Span);
                return null;
            }
        }

        var parameters = BindParameters(definition);
        RegisterReference? returns = null;
        if (definition.Returns is { } returnsExpression)
        {
            if (RegisterModel.TryResolve(returnsExpression, out var reference)
                && reference.IsGpr
                && reference.View != RegisterView.Invalid)
            {
                returns = reference;
            }
            else if (FindLegacyToken(returnsExpression) is { } legacyToken
                && RegisterModel.TryGetLegacySuggestion(legacyToken.Text, out var suggestion))
            {
                diagnostics.Error(
                    DiagnosticIds.LegacyName,
                    $"'{legacyToken.Text}' es un nombre legacy prohibido; usa '{suggestion}'.",
                    legacyToken.Location,
                    legacyToken.Span);
            }
            else
            {
                diagnostics.Error(
                    DiagnosticIds.InvalidReturnRegister,
                    "El retorno declarado en d.returns(...) debe ser un registro GprN (o una de sus vistas).",
                    null,
                    returnsExpression.Span);
            }
        }

        return new RoutineSymbol(definition, definition.Name?.Text, parameters, definition.Returns, returns);
    }

    private IReadOnlyList<RoutineParameter> BindParameters(RoutineDefinitionSyntax definition)
    {
        var run = definition.Run;
        if (run is null || run.Parameters.Count == 0)
        {
            return [];
        }

        if (run.Parameters.Count > RegisterModel.MaxParameters)
        {
            diagnostics.Error(
                DiagnosticIds.TooManyParameters,
                $"La rutina declara {run.Parameters.Count} parámetros y Fase 1 admite como máximo {RegisterModel.MaxParameters}.",
                null,
                run.Span);
        }

        var parameters = new List<RoutineParameter>(run.Parameters.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < run.Parameters.Count; index++)
        {
            var parameter = run.Parameters[index];
            var parameterName = parameter.Name.Text;

            if (RegisterModel.TryGetLegacySuggestion(parameterName, out var legacySuggestion))
            {
                diagnostics.Error(
                    DiagnosticIds.LegacyName,
                    $"'{parameterName}' es un nombre legacy prohibido; usa '{legacySuggestion}'.",
                    parameter.Name.Location,
                    parameter.Name.Span);
                continue;
            }

            if (!names.Add(parameterName))
            {
                diagnostics.Error(
                    DiagnosticIds.DuplicateSymbol,
                    $"El parámetro '{parameterName}' está declarado más de una vez en la misma rutina.",
                    parameter.Name.Location,
                    parameter.Name.Span);
                continue;
            }

            if (parameter.Magnitude is not { } magnitudeSyntax)
            {
                diagnostics.Error(
                    DiagnosticIds.ParameterWithoutMagnitude,
                    $"El parámetro '{parameterName}' debe declarar su capacidad física, por ejemplo '{parameterName}<Byte:8>'.",
                    null,
                    parameter.Span);
                continue;
            }

            if (!MagnitudeInfo.TryCreate(magnitudeSyntax, out var magnitude))
            {
                diagnostics.Error(
                    DiagnosticIds.InvalidMagnitude,
                    $"La capacidad del parámetro '{parameterName}' no es una magnitud válida.",
                    null,
                    magnitudeSyntax.Span);
                continue;
            }

            if (magnitude.Bits > 64)
            {
                diagnostics.Error(
                    DiagnosticIds.WidthMismatch,
                    $"El parámetro '{parameterName}' ocupa {magnitude.Bits} bits y los registros de entrada de Fase 1 son de 64 bits.",
                    parameter.Name.Location,
                    parameter.Span);
                continue;
            }

            var register = RegisterModel.InputRegister(index);
            if (register < 0)
            {
                continue;
            }

            parameters.Add(new RoutineParameter(parameterName, magnitude, register));
        }

        return parameters;
    }

    private IReadOnlyDictionary<string, LabelSymbol> BindLabels(RoutineSymbol routine)
    {
        var scope = new Dictionary<string, LabelSymbol>(StringComparer.Ordinal);

        foreach (var statement in routine.Definition.Body)
        {
            if (statement is not LabelStatementSyntax label)
            {
                continue;
            }

            var name = label.Label.Text;
            if (!scope.TryAdd(name, new LabelSymbol(name, label.Label)))
            {
                diagnostics.Error(
                    DiagnosticIds.DuplicateSymbol,
                    $"La etiqueta '{name}' ya existe en la rutina '{routine.DisplayName}'.",
                    label.Label.Location,
                    label.Label.Span);
            }
        }

        foreach (var statement in routine.Definition.Body)
        {
            foreach (var reference in SyntaxTraversal.LabelReferences(statement))
            {
                var name = reference.Label.Text;
                if (!scope.ContainsKey(name))
                {
                    diagnostics.Error(
                        DiagnosticIds.UnknownSymbol,
                        $"La etiqueta '{name}' no existe en la rutina '{routine.DisplayName}'.",
                        reference.Label.Location,
                        reference.Label.Span);
                }
            }
        }

        return scope;
    }

    private void ValidatePayload(DataDefinitionSyntax definition, string symbolName, MagnitudeInfo capacity)
    {
        var values = definition.Values;
        if (values.Count == 0)
        {
            return;
        }

        var capacityBytes = capacity.Bytes;
        if (values.Count == 1)
        {
            ValidateSingleValue(symbolName, values[0], capacityBytes);
            return;
        }

        if (capacityBytes % values.Count != 0)
        {
            diagnostics.Error(
                DiagnosticIds.DataPayloadOverflow,
                $"La lista de {values.Count} valores no divide exactamente la capacidad {capacity.Display} de '{symbolName}'.",
                null,
                definition.Span);
            return;
        }

        var elementBytes = capacityBytes / values.Count;
        foreach (var value in values)
        {
            if (value is not IntegerLiteralSyntax integer)
            {
                diagnostics.Error(
                    DiagnosticIds.InvalidDataValue,
                    $"La lista de '{symbolName}' solo admite enteros homogéneos.",
                    null,
                    value.Span);
                continue;
            }

            if (!FitsUnsigned(integer.Value, elementBytes))
            {
                diagnostics.Error(
                    DiagnosticIds.DataPayloadOverflow,
                    $"El valor '{integer.Literal.Text}' no cabe en {elementBytes} byte(s) del elemento de '{symbolName}'.",
                    integer.Literal.Location,
                    integer.Span);
            }
        }
    }

    private void ValidateSingleValue(string symbolName, ExpressionSyntax value, long capacityBytes)
    {
        switch (value)
        {
            case DefaultValueSyntax:
                return;

            case IntegerLiteralSyntax integer when FitsUnsigned(integer.Value, capacityBytes):
                return;

            case IntegerLiteralSyntax integer:
                diagnostics.Error(
                    DiagnosticIds.DataPayloadOverflow,
                    $"El valor '{integer.Literal.Text}' no cabe en {capacityBytes} byte(s) de '{symbolName}'.",
                    integer.Literal.Location,
                    integer.Span);
                return;

            case StringLiteralSyntax text:
                var byteCount = System.Text.Encoding.UTF8.GetByteCount(text.Value);
                if (byteCount > capacityBytes)
                {
                    diagnostics.Error(
                        DiagnosticIds.DataPayloadOverflow,
                        $"La cadena de '{symbolName}' ocupa {byteCount} byte(s) UTF-8 y la capacidad es {capacityBytes}.",
                        text.Literal.Location,
                        text.Span);
                }

                return;

            default:
                diagnostics.Error(
                    DiagnosticIds.InvalidDataValue,
                    $"El valor de '{symbolName}' debe ser un entero, una cadena o defaultValue.",
                    null,
                    value.Span);
                return;
        }
    }

    private bool IsReserved(string name) =>
        ReservedIdentifiers.Contains(name, StringComparer.Ordinal)
        || RegisterModel.TryGetGprIndex(name, out _);

    private static Token? FindLegacyToken(ExpressionSyntax expression) => expression switch
    {
        IdentifierExpressionSyntax identifier
            when RegisterModel.TryGetLegacySuggestion(identifier.Identifier.Text, out _) => identifier.Identifier,
        MemberAccessSyntax member => FindLegacyToken(member.Receiver),
        _ => null,
    };

    private static bool FitsUnsigned(long value, long byteCount)
    {
        if (value < 0)
        {
            return false;
        }

        if (byteCount >= sizeof(long))
        {
            return true;
        }

        return value <= (1L << (int)(byteCount * 8)) - 1;
    }
}
