using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Resultado de la fase semántica: tabla de símbolos y diagnósticos acumulados
/// (<c>FASM2xxx</c>–<c>FASM5xxx</c>).
/// </summary>
public sealed class SemanticResult
{
    internal SemanticResult(SymbolTable symbols, DiagnosticBag diagnostics)
    {
        Symbols = symbols;
        Diagnostics = diagnostics;
    }

    /// <summary>Tabla de símbolos construida por el binder.</summary>
    public SymbolTable Symbols { get; }

    /// <summary>Diagnósticos léxicos, sintácticos y semánticos acumulados.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>Indica si la fase semántica no emitió ningún error.</summary>
    public bool Success => !Diagnostics.HasErrors;
}
