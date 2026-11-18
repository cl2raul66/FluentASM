using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Símbolo declarado en <c>.data(...)</c> con su capacidad física resuelta.
/// </summary>
/// <param name="Name">Identificador declarado con <c>d.id(...)</c>.</param>
/// <param name="Capacity">Capacidad declarada (siempre en bytes).</param>
/// <param name="Declaration">Token del identificador.</param>
public sealed record DataSymbol(string Name, MagnitudeInfo Capacity, Token Declaration)
{
    /// <summary>Capacidad del símbolo en bits.</summary>
    public long BitSize => Capacity.Bits;

    /// <summary>Capacidad del símbolo en bytes.</summary>
    public long ByteSize => Capacity.Bytes;
}

/// <summary>
/// Parámetro de entrada de una rutina, mapeado a su registro físico según la
/// convención de Fase 1.
/// </summary>
/// <param name="Name">Alias declarado en <c>d.run((alias&lt;Magnitud&gt;, ...) -&gt; ...)</c>.</param>
/// <param name="Magnitude">Capacidad física declarada para el alias.</param>
/// <param name="RegisterIndex">Índice del <c>Gpr</c> asignado.</param>
public sealed record RoutineParameter(string Name, MagnitudeInfo Magnitude, int RegisterIndex);

/// <summary>Rutina declarada en <c>.execute(...)</c> con su contrato resuelto.</summary>
public sealed class RoutineSymbol
{
    /// <summary>Crea el símbolo de una rutina.</summary>
    public RoutineSymbol(
        RoutineDefinitionSyntax definition,
        string? name,
        IReadOnlyList<RoutineParameter> parameters,
        ExpressionSyntax? returnsExpression,
        RegisterReference? returns)
    {
        Definition = definition;
        Name = name;
        Parameters = parameters;
        ReturnsExpression = returnsExpression;
        Returns = returns;
    }

    /// <summary>Definición sintáctica original.</summary>
    public RoutineDefinitionSyntax Definition { get; }

    /// <summary>Identificador de la rutina; <c>null</c> en la forma implícita <c>_ -&gt; ...</c>.</summary>
    public string? Name { get; }

    /// <summary>Parámetros de entrada en orden de declaración.</summary>
    public IReadOnlyList<RoutineParameter> Parameters { get; }

    /// <summary>Expresión declarada en <c>d.returns(...)</c>, si existe.</summary>
    public ExpressionSyntax? ReturnsExpression { get; }

    /// <summary>Registro de retorno resuelto, si el contrato es válido.</summary>
    public RegisterReference? Returns { get; }

    /// <summary>Indica si la rutina usa la forma implícita <c>.routines(_ -&gt; { ... })</c>.</summary>
    public bool IsImplicit => Definition.IsImplicit;

    /// <summary>Nombre legible para diagnósticos.</summary>
    public string DisplayName => Name ?? "<implícita>";
}

/// <summary>Etiqueta local visible solo dentro de su rutina.</summary>
/// <param name="Name">Nombre con prefijo <c>@</c>.</param>
/// <param name="Declaration">Token de la declaración.</param>
public sealed record LabelSymbol(string Name, Token Declaration);
