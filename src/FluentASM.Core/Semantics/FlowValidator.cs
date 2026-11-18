using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Valida los contratos de <c>Ipr.flowCall(...)</c>, la invalidación lógica de
/// las entradas, el balance de pila y el cierre explícito de cada rutina
/// (<c>FLUENT_SYNTAX.md</c> §6 y <c>FLUENT_SYNTAX_X64.md</c> §4).
/// </summary>
internal sealed class FlowValidator
{
    private static readonly HashSet<string> WriteOnlyReceivers = new(StringComparer.Ordinal)
    {
        "copy", "copyUnsigned", "copySigned", "loadAddress", "pop",
    };

    private static readonly HashSet<string> WritesReceiver = new(StringComparer.Ordinal)
    {
        "copy", "copyUnsigned", "copySigned", "loadAddress", "exchange", "conditionalCopy",
        "add", "sub", "addWithCarry", "subWithBorrow", "increment", "decrement", "negate",
        "multiply", "and", "or", "xor", "not",
        "shiftLeft", "shiftRightLogical", "shiftRightArithmetic",
        "rotateLeft", "rotateRight", "testBit", "setBit", "clearBit", "toggleBit",
        "pop",
    };

    private readonly DiagnosticBag diagnostics;
    private readonly SymbolTable symbols;
    private readonly RoutineSymbol routine;
    private readonly HashSet<int> invalidated = [];
    private readonly HashSet<int> loaded = [];

    private int stackDepth;
    private bool reportedImbalance;
    private int verifiedContracts;

    /// <summary>Crea el validador de flujo de una rutina.</summary>
    public FlowValidator(DiagnosticBag diagnostics, SymbolTable symbols, RoutineSymbol routine)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(routine);

        this.diagnostics = diagnostics;
        this.symbols = symbols;
        this.routine = routine;

        foreach (var parameter in routine.Parameters)
        {
            loaded.Add(parameter.RegisterIndex);
        }
    }

    /// <summary>Contratos de llamada verificados correctamente en la rutina.</summary>
    public int VerifiedContracts => verifiedContracts;

    /// <summary>Procesa una acción del cuerpo respecto de lecturas, pila y llamadas.</summary>
    public void ValidateAction(ActionStatementSyntax action)
    {
        ArgumentNullException.ThrowIfNull(action);

        CheckInvalidatedReads(action);

        if (action.Verb.Text == "flowCall")
        {
            var outcome = ValidateFlowCall(action);
            if (outcome.Found)
            {
                foreach (var index in outcome.Inputs)
                {
                    invalidated.Add(index);
                    loaded.Remove(index);
                }

                if (outcome.OutputIndex >= 0)
                {
                    Load(outcome.OutputIndex);
                }

                if (outcome.Valid)
                {
                    verifiedContracts++;
                }
            }
        }

        TraceStack(action);
        TraceWrites(action);
    }

    /// <summary>Valida el cierre explícito y el balance final de la pila.</summary>
    public void ValidateClosure()
    {
        if (stackDepth != 0 && !reportedImbalance)
        {
            diagnostics.Error(
                DiagnosticIds.StackImbalance,
                $"La rutina '{routine.DisplayName}' termina con {stackDepth} byte(s) de desbalance en Stack.",
                routine.Definition.Name?.Location,
                routine.Definition.Span);
        }

        var last = routine.Definition.Body.OfType<ActionStatementSyntax>().LastOrDefault();
        if (last is null)
        {
            ReportNotClosed("La rutina no contiene instrucciones y no cierra ningún camino.");
            return;
        }

        if (IsClosingAction(last))
        {
            return;
        }

        ReportNotClosed(
            "La rutina debe cerrar cada camino con Ipr.flowStackJump() o Cpu.halt(); "
            + $"la última instrucción es '{last.Verb.Text}'.");
    }

    private void ReportNotClosed(string message) =>
        diagnostics.Error(
            DiagnosticIds.RoutineNotClosed,
            message,
            routine.Definition.Name?.Location,
            routine.Definition.Span);

    private static bool IsClosingAction(ActionStatementSyntax action)
    {
        if (action.Receiver is not IdentifierExpressionSyntax receiver)
        {
            return false;
        }

        return (receiver.Identifier.Text == "Ipr" && action.Verb.Text == "flowStackJump")
            || (receiver.Identifier.Text == "Cpu" && action.Verb.Text == "halt")
            || (receiver.Identifier.Text == "Ipr"
                && action.Verb.Text == "flowJump"
                && action.Arguments.Count == 1
                && action.Arguments[0] is LabelReferenceSyntax or IdentifierExpressionSyntax);
    }

    private CallOutcome ValidateFlowCall(ActionStatementSyntax action)
    {
        var inputs = new List<int>();

        if (!TryReadContract(action.Arguments[0], out var contract))
        {
            Report(
                DiagnosticIds.CallContractMismatch,
                "Ipr.flowCall exige 'c -> c.target(rutina).inputs(...).output(...)' o el nombre de una rutina.",
                action.Arguments[0]);
            return new CallOutcome(Found: false, Valid: false, inputs, OutputIndex: -1);
        }

        if (contract.Target is null)
        {
            Report(
                DiagnosticIds.CallContractMismatch,
                "El contrato de Ipr.flowCall debe declarar .target(rutina).",
                action.Arguments[0]);
            return new CallOutcome(Found: false, Valid: false, inputs, OutputIndex: -1);
        }

        if (!symbols.TryGetRoutine(contract.Target.Text, out var target))
        {
            ReportToken(
                DiagnosticIds.CallTargetNotFound,
                $"La rutina destino '{contract.Target.Text}' no existe.",
                contract.Target);
            return new CallOutcome(Found: false, Valid: false, inputs, OutputIndex: -1);
        }

        var valid = true;

        if (contract.ShortForm)
        {
            if (target.Parameters.Count > 0 || target.Returns is not null)
            {
                ReportToken(
                    DiagnosticIds.CallContractMismatch,
                    $"'{target.DisplayName}' recibe entradas o devuelve valor; usa la forma completa con "
                    + ".target(...).inputs(...).output(...).",
                    contract.Target);
                valid = false;
            }

            return new CallOutcome(Found: true, valid, inputs, OutputIndex: -1);
        }

        if (contract.Inputs is null)
        {
            if (target.Parameters.Count > 0)
            {
                ReportToken(
                    DiagnosticIds.CallContractMismatch,
                    $"'{target.DisplayName}' espera {target.Parameters.Count} entrada(s) y el contrato no declara .inputs(...).",
                    contract.Target);
                valid = false;
            }
        }
        else
        {
            if (contract.Inputs.Count != target.Parameters.Count)
            {
                ReportToken(
                    DiagnosticIds.CallContractMismatch,
                    $"'{target.DisplayName}' espera {target.Parameters.Count} entrada(s) y el contrato declara {contract.Inputs.Count}.",
                    contract.Target);
                valid = false;
            }

            var count = Math.Min(contract.Inputs.Count, target.Parameters.Count);
            for (var index = 0; index < count; index++)
            {
                if (!ValidateInput(target, index, contract.Inputs[index], inputs))
                {
                    valid = false;
                }
            }
        }

        var outputIndex = ValidateOutput(target, contract.Output, action.Arguments[0], ref valid);
        return new CallOutcome(Found: true, valid, inputs, outputIndex);
    }

    private bool ValidateInput(
        RoutineSymbol target,
        int index,
        ExpressionSyntax expression,
        List<int> inputs)
    {
        var parameter = target.Parameters[index];

        if (!RegisterModel.TryResolve(expression, out var reference) || !reference.IsGpr)
        {
            Report(
                DiagnosticIds.CallContractMismatch,
                $"La entrada {index + 1} de '{target.DisplayName}' debe ser un registro Gpr.",
                expression);
            return false;
        }

        inputs.Add(reference.Index);
        var valid = true;

        if (reference.Index != parameter.RegisterIndex)
        {
            Report(
                DiagnosticIds.CallContractMismatch,
                $"La entrada {index + 1} de '{target.DisplayName}' corresponde a Gpr{parameter.RegisterIndex}, "
                + $"no a {reference.Display}.",
                expression);
            valid = false;
        }

        if (reference.Bits != parameter.Magnitude.Bits)
        {
            Report(
                DiagnosticIds.CallContractMismatch,
                $"La entrada {index + 1} de '{target.DisplayName}' ocupa {parameter.Magnitude.Bits} bits y "
                + $"{reference.Display} ocupa {reference.Bits} bits.",
                expression);
            valid = false;
        }

        if (invalidated.Contains(reference.Index))
        {
            return false;
        }

        if (!loaded.Contains(reference.Index))
        {
            Report(
                DiagnosticIds.CallInputNotLoaded,
                $"La entrada '{reference.Display}' de '{target.DisplayName}' se usa antes de ser cargada.",
                expression);
            valid = false;
        }

        return valid;
    }

    private int ValidateOutput(
        RoutineSymbol target,
        ExpressionSyntax? output,
        ExpressionSyntax callExpression,
        ref bool valid)
    {
        if (target.Returns is not { } expected)
        {
            if (output is not null)
            {
                Report(
                    DiagnosticIds.CallContractMismatch,
                    $"'{target.DisplayName}' no declara d.returns(...) y el contrato sí declara .output(...).",
                    output);
                valid = false;
            }

            return -1;
        }

        if (output is null)
        {
            Report(
                DiagnosticIds.CallContractMismatch,
                $"'{target.DisplayName}' devuelve {expected.Display} y el contrato no declara .output(...).",
                callExpression);
            valid = false;
            return -1;
        }

        if (!RegisterModel.TryResolve(output, out var actual) || !SameRegister(actual, expected))
        {
            Report(
                DiagnosticIds.CallContractMismatch,
                $"El .output(...) debe coincidir con d.returns({expected.Display}) de '{target.DisplayName}'.",
                output);
            valid = false;
            return -1;
        }

        return actual.Index;
    }

    private static bool SameRegister(RegisterReference left, RegisterReference right) =>
        left.Unit == right.Unit
        && left.Index == right.Index
        && left.View == right.View
        && string.Equals(left.Member, right.Member, StringComparison.Ordinal);

    private void CheckInvalidatedReads(ActionStatementSyntax action)
    {
        var references = new List<RegisterReference>();

        if (action.Verb.Text == "flowCall")
        {
            if (TryReadContract(action.Arguments[0], out var contract) && contract.Inputs is { } inputs)
            {
                foreach (var input in inputs)
                {
                    if (RegisterModel.TryResolve(input, out var reference) && reference.IsGpr)
                    {
                        references.Add(reference);
                    }
                }
            }
        }
        else
        {
            var readsReceiver = !WriteOnlyReceivers.Contains(action.Verb.Text)
                || action.Receiver is MemoryOperandSyntax or PortOperandSyntax;

            if (readsReceiver)
            {
                CollectRegisters(action.Receiver, references);
            }

            foreach (var argument in action.Arguments)
            {
                CollectRegisters(argument, references);
            }
        }

        var reported = new HashSet<int>();
        foreach (var reference in references)
        {
            if (invalidated.Contains(reference.Index) && reported.Add(reference.Index))
            {
                diagnostics.Error(
                    DiagnosticIds.InvalidatedInputRead,
                    $"'{reference.Display}' quedó invalidado lógicamente tras Ipr.flowCall(...) y se lee antes de recargarlo.",
                    reference.Token?.Location,
                    reference.Token?.Span);
            }
        }
    }

    private void CollectRegisters(ExpressionSyntax expression, List<RegisterReference> references)
    {
        switch (expression)
        {
            case RegisterPairSyntax pair:
                CollectRegisters(pair.High, references);
                CollectRegisters(pair.Low, references);
                break;

            case ConditionalExpressionSyntax conditional:
                CollectRegisters(conditional.Value, references);
                break;

            case MemoryOperandSyntax memory:
                CollectRegisters(memory.Address.Base, references);
                if (memory.Address.Index is { } index)
                {
                    CollectRegisters(index.Register, references);
                }

                break;

            case PortOperandSyntax port:
                CollectRegisters(port.Address, references);
                break;

            case LambdaExpressionSyntax:
                break;

            default:
                if (RegisterModel.TryResolve(expression, out var reference) && reference.IsGpr)
                {
                    references.Add(reference);
                }

                break;
        }
    }

    private void TraceWrites(ActionStatementSyntax action)
    {
        var verb = action.Verb.Text;

        if (WritesReceiver.Contains(verb)
            && RegisterModel.TryResolve(action.Receiver, out var receiver)
            && receiver.IsGpr)
        {
            Load(receiver.Index);
        }

        switch (verb)
        {
            case "multiplyWide":
            case "signExtend":
            case "divide":
            case "divideSigned":
                Load(0);
                Load(2);
                break;

            case "flowLoop":
                Load(1);
                break;

            case "copyBlock":
            case "fillBlock":
                Load(1);
                Load(6);
                Load(7);
                break;

            default:
                break;
        }
    }

    private void Load(int registerIndex)
    {
        loaded.Add(registerIndex);
        invalidated.Remove(registerIndex);
    }

    private void TraceStack(ActionStatementSyntax action)
    {
        switch (action.Verb.Text)
        {
            case "push":
                stackDepth += 8;
                break;

            case "pop":
                stackDepth -= 8;
                CheckStackDepth(action);
                break;

            case "reserve":
                stackDepth += StackBytes(action.Arguments[0]);
                break;

            case "release":
                stackDepth -= StackBytes(action.Arguments[0]);
                CheckStackDepth(action);
                break;

            case "flowCall":
                if (Math.Abs(stackDepth) % 16 != 0)
                {
                    diagnostics.Error(
                        DiagnosticIds.StackNotAligned,
                        $"Stack debe estar alineada a 16 bytes antes de Ipr.flowCall(...); "
                        + $"el desplazamiento actual es {stackDepth} byte(s).",
                        action.Verb.Location,
                        action.Span);
                }

                break;

            case "flowStackJump":
                if (stackDepth != 0)
                {
                    diagnostics.Error(
                        DiagnosticIds.StackImbalance,
                        $"Ipr.flowStackJump() requiere Stack equilibrada; hay {stackDepth} byte(s) pendientes.",
                        action.Verb.Location,
                        action.Span);
                    reportedImbalance = true;
                }

                break;

            default:
                break;
        }
    }

    private void CheckStackDepth(ActionStatementSyntax action)
    {
        if (stackDepth >= 0)
        {
            return;
        }

        diagnostics.Error(
            DiagnosticIds.StackImbalance,
            $"La pila retrocede {Math.Abs(stackDepth)} byte(s) por debajo del nivel de entrada de la rutina.",
            action.Verb.Location,
            action.Span);
        reportedImbalance = true;
        stackDepth = 0;
    }

    private static int StackBytes(ExpressionSyntax expression) =>
        expression is MagnitudeSyntax magnitude && MagnitudeInfo.TryCreate(magnitude, out var info)
            ? (int)Math.Min(info.Bytes, int.MaxValue)
            : 0;

    private void Report(string id, string message, ExpressionSyntax? expression)
    {
        var token = expression is null ? null : SyntaxTraversal.FirstToken(expression);
        var location = token?.Location ?? routine.Definition.Name?.Location;
        var span = expression?.Span ?? routine.Definition.Span;
        diagnostics.Error(id, message, location, span);
    }

    private void ReportToken(string id, string message, Token? token) =>
        diagnostics.Error(
            id,
            message,
            token?.Location ?? routine.Definition.Name?.Location,
            token?.Span ?? routine.Definition.Span);

    private static bool TryReadContract(ExpressionSyntax expression, out CallContract contract)
    {
        switch (expression)
        {
            case IdentifierExpressionSyntax identifier:
                contract = new CallContract(identifier.Identifier, null, null, ShortForm: true);
                return true;

            case LambdaExpressionSyntax { Body: FluentChainBodySyntax chain }:
                Token? target = null;
                IReadOnlyList<ExpressionSyntax>? inputs = null;
                ExpressionSyntax? output = null;

                foreach (var call in chain.Calls)
                {
                    switch (call.Name.Text)
                    {
                        case "target" when call.Arguments.Count == 1
                            && call.Arguments[0] is IdentifierExpressionSyntax name:
                            target = name.Identifier;
                            break;

                        case "inputs":
                            inputs = call.Arguments;
                            break;

                        case "output" when call.Arguments.Count == 1:
                            output = call.Arguments[0];
                            break;

                        default:
                            break;
                    }
                }

                contract = new CallContract(target, inputs, output, ShortForm: false);
                return target is not null;

            default:
                contract = default;
                return false;
        }
    }

    private readonly record struct CallOutcome(
        bool Found,
        bool Valid,
        IReadOnlyList<int> Inputs,
        int OutputIndex);

    private readonly record struct CallContract(
        Token? Target,
        IReadOnlyList<ExpressionSyntax>? Inputs,
        ExpressionSyntax? Output,
        bool ShortForm);
}
