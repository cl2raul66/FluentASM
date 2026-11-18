using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Fase semántica de M2: liga los símbolos del programa y valida el modelo
/// físico de Fase 1 (ATM, registros, verbos, direccionamiento, banderas y
/// contratos de llamada) sin emitir bytes.
/// </summary>
public static class SemanticAnalyzer
{
    /// <summary>
    /// Analiza un árbol sintáctico completo y devuelve su tabla de símbolos y
    /// los diagnósticos semánticos.
    /// </summary>
    /// <param name="root">Raíz producida por el parser de M1.</param>
    /// <param name="diagnostics">Bolsa compartida opcional; si se omite se crea una nueva.</param>
    public static SemanticResult Analyze(CompilationUnitSyntax root, DiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var bag = diagnostics ?? new DiagnosticBag();
        var binder = new Binder(bag);
        var symbols = binder.Bind(root);
        var validator = new RoutineValidator(bag, symbols);
        validator.Validate(root);
        return new SemanticResult(symbols, bag);
    }
}
