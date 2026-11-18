using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Recorre las rutinas de <c>.execute(...)</c> y aplica los validadores de
/// Fase 1 sobre cada acción del cuerpo.
/// </summary>
internal sealed class RoutineValidator
{
    private readonly DiagnosticBag diagnostics;
    private readonly SymbolTable symbols;

    /// <summary>Crea el validador de rutinas.</summary>
    public RoutineValidator(DiagnosticBag diagnostics, SymbolTable symbols)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(symbols);

        this.diagnostics = diagnostics;
        this.symbols = symbols;
    }

    /// <summary>Valida todas las rutinas registradas en la tabla de símbolos.</summary>
    public void Validate(CompilationUnitSyntax root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var routine in symbols.Routines)
        {
            ValidateRoutine(routine);
        }
    }

    private void ValidateRoutine(RoutineSymbol routine)
    {
        var typeValidator = new TypeValidator(diagnostics, symbols, routine);
        var verbValidator = new VerbValidator(diagnostics, symbols, routine);
        var flags = new FlagTracker(diagnostics);

        foreach (var statement in routine.Definition.Body)
        {
            switch (statement)
            {
                case LabelStatementSyntax:
                    flags.Reset();
                    break;

                case ActionStatementSyntax action:
                    typeValidator.ValidateAction(action);
                    verbValidator.ValidateAction(action);
                    VerbValidator.ValidateFlagReads(action, flags);
                    flags.Apply(VerbValidator.EffectOf(action));
                    break;

                default:
                    break;
            }
        }
    }
}
