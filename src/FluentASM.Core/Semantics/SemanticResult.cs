using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Resultado de la fase semántica: tabla de símbolos y diagnósticos acumulados
/// (<c>FASM2xxx</c>–<c>FASM5xxx</c>).
/// </summary>
public sealed class SemanticResult
{
    internal SemanticResult(SymbolTable symbols, DiagnosticBag diagnostics, int contractsVerified)
    {
        Symbols = symbols;
        Diagnostics = diagnostics;
        ContractsVerified = contractsVerified;
    }

    /// <summary>Tabla de símbolos construida por el binder.</summary>
    public SymbolTable Symbols { get; }

    /// <summary>Diagnósticos léxicos, sintácticos y semánticos acumulados.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>Contratos de <c>Ipr.flowCall(...)</c> verificados correctamente.</summary>
    public int ContractsVerified { get; }

    /// <summary>Indica si la fase semántica no emitió ningún error.</summary>
    public bool Success => !Diagnostics.HasErrors;
}


