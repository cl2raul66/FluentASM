using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Syntax;

/// <summary>
/// Resultado del análisis sintáctico: árbol parcial y diagnósticos acumulados.
/// </summary>
public sealed class ParseResult
{
    /// <summary>Crea un resultado sintáctico.</summary>
    public ParseResult(CompilationUnitSyntax root, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Root = root;
        Diagnostics = diagnostics;
    }

    /// <summary>Raíz del árbol sintáctico; siempre existe aunque haya errores.</summary>
    public CompilationUnitSyntax Root { get; }

    /// <summary>Diagnósticos léxicos y sintácticos acumulados.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>Indica si no se emitió ningún error.</summary>
    public bool Success => !Diagnostics.HasErrors;
}


