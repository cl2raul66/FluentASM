using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Rastrea de forma lineal, dentro de una rutina, qué banderas de <c>Frf</c>
/// están definidas y emite <c>FASM4012</c> cuando una condición lee una bandera
/// que la instrucción previa dejó indefinida.
/// </summary>
internal sealed class FlagTracker
{
    private readonly DiagnosticBag diagnostics;
    private FlagSet defined;

    /// <summary>Crea un rastreador con todas las banderas indefinidas.</summary>
    public FlagTracker(DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.diagnostics = diagnostics;
    }

    /// <summary>Reinicia el estado en un punto de unión de caminos (etiqueta).</summary>
    public void Reset() => defined = FlagSet.None;

    /// <summary>Aplica el efecto de una instrucción ejecutada.</summary>
    public void Apply(FlagEffect effect) => defined = effect.Apply(defined);

    /// <summary>Verifica que una condición solo lea banderas definidas.</summary>
    public void RequireDefined(ConditionSyntax? condition)
    {
        if (condition is null)
        {
            return;
        }

        foreach (var expression in SyntaxTraversal.ConditionExpressions(condition))
        {
            if (expression is not MemberAccessSyntax
                {
                    Receiver: IdentifierExpressionSyntax { Identifier.Text: "Frf" },
                } member)
            {
                continue;
            }

            if (!FlagCatalog.TryGetFlags(member.Member.Text, out var flags))
            {
                continue;
            }

            var missing = flags & ~defined;
            if (missing == 0)
            {
                continue;
            }

            diagnostics.Error(
                DiagnosticIds.UndefinedFlag,
                $"La condición lee {FlagCatalog.Describe(missing)}, que la instrucción previa dejó indefinida.",
                member.Member.Location,
                member.Member.Span);
        }
    }
}


