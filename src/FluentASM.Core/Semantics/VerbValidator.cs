using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Valida verbos, número de argumentos, tuplas de receptor, direccionamiento
/// SIB y copias memoria → memoria, y calcula el efecto de cada instrucción
/// sobre <c>Frf</c>.
/// </summary>
internal sealed class VerbValidator
{
    private readonly DiagnosticBag diagnostics;
    private readonly SymbolTable symbols;
    private readonly RoutineSymbol routine;

    /// <summary>Crea un validador de verbos para una rutina.</summary>
    public VerbValidator(DiagnosticBag diagnostics, SymbolTable symbols, RoutineSymbol routine)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(routine);

        this.diagnostics = diagnostics;
        this.symbols = symbols;
        this.routine = routine;
    }

    /// <summary>Valida la forma del verbo, sus argumentos y el direccionamiento.</summary>
    public void ValidateAction(ActionStatementSyntax action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (RegisterModel.TryGetLegacySuggestion(action.Verb.Text, out _)
            || FindLegacyReceiver(action.Receiver) is not null)
        {
            return;
        }

        if (!VerbCatalog.TryGet(action.Verb.Text, out var definition))
        {
            diagnostics.Error(
                DiagnosticIds.InvalidVerb,
                $"El verbo '{action.Verb.Text}' no pertenece al catálogo de Fase 1.",
                action.Verb.Location,
                action.Verb.Span);
            return;
        }

        var receiverShape = VerbCatalog.ShapeOf(action.Receiver, symbols, routine);
        if ((definition.Receivers & receiverShape) == 0)
        {
            diagnostics.Error(
                DiagnosticIds.InvalidVerb,
                $"El verbo '{action.Verb.Text}' no se aplica a este receptor en Fase 1.",
                action.Verb.Location,
                action.Verb.Span);
            return;
        }

        if (action.Arguments.Count < definition.MinArguments
            || action.Arguments.Count > definition.MaxArguments)
        {
            diagnostics.Error(
                DiagnosticIds.InvalidArgumentCount,
                $"El verbo '{action.Verb.Text}' espera {DescribeArguments(definition)} y recibió {action.Arguments.Count}.",
                action.Verb.Location,
                action.Span);
            return;
        }

        ValidateSpecialForms(action);
        ValidateAddressing(action.Receiver);
        ValidateConditionNames(action);
    }

    /// <summary>Verifica las banderas leídas por saltos condicionales y copias condicionales.</summary>
    public static void ValidateFlagReads(ActionStatementSyntax action, FlagTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(tracker);

        if (action.Verb.Text is not ("flowJump" or "conditionalCopy"))
        {
            return;
        }

        foreach (var argument in action.Arguments)
        {
            if (argument is ConditionalExpressionSyntax conditional)
            {
                tracker.RequireDefined(conditional.Condition);
            }
        }
    }

    /// <summary>Obtiene el efecto de una instrucción sobre <c>Frf</c>.</summary>
    public static FlagEffect EffectOf(ActionStatementSyntax action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action.Verb.Text == "copy"
            && action.Arguments.Count == 1
            && action.Arguments[0] is DefaultValueSyntax)
        {
            return new FlagEffect(
                FlagSet.Carry | FlagSet.Overflow | FlagSet.Zero | FlagSet.Sign | FlagSet.Parity,
                FlagSet.Adjust);
        }

        if (!VerbCatalog.TryGet(action.Verb.Text, out var definition))
        {
            return FlagEffect.Preserve;
        }

        if (action.Verb.Text == "pop"
            && action.Receiver is IdentifierExpressionSyntax { Identifier.Text: "Frf" })
        {
            return new FlagEffect(FlagSet.All, FlagSet.None);
        }

        return definition.Flags;
    }

    private void ValidateSpecialForms(ActionStatementSyntax action)
    {
        var verb = action.Verb.Text;
        switch (verb)
        {
            case "loadAddress":
                if (action.Arguments[0] is not IdentifierExpressionSyntax address
                    || !symbols.TryGetData(address.Identifier.Text, out _))
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        "loadAddress requiere el identificador de un símbolo de .data.",
                        action.Arguments[0]);
                }

                break;

            case "push":
                if (VerbCatalog.ShapeOf(action.Arguments[0], symbols, routine)
                    is not (ReceiverShape.Register or ReceiverShape.Frf))
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        "Stack.push solo admite un registro Gpr o Frf.",
                        action.Arguments[0]);
                }

                break;

            case "pop":
                if (VerbCatalog.ShapeOf(action.Arguments[0], symbols, routine) != ReceiverShape.Stack)
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        "pop requiere Stack como argumento: 'Gpr0.pop(Stack)'.",
                        action.Arguments[0]);
                }

                break;

            case "copy":
                if (VerbCatalog.ShapeOf(action.Receiver, symbols, routine) == ReceiverShape.Memory
                    && VerbCatalog.ShapeOf(action.Arguments[0], symbols, routine) == ReceiverShape.Memory)
                {
                    diagnostics.Error(
                        DiagnosticIds.MemoryToMemoryCopy,
                        "No existe copia directa memoria → memoria con '.copy'; usa un Gpr intermedio o copyBlock.",
                        action.Verb.Location,
                        action.Span);
                }

                break;

            case "conditionalCopy":
                if (action.Arguments[0] is not ConditionalExpressionSyntax)
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        "conditionalCopy requiere una fuente condicionada: 'Gpr0.conditionalCopy(Gpr1 if Frf.equal)'.",
                        action.Arguments[0]);
                }

                break;

            case "flowJump":
                if (!IsValidFlowTarget(action.Arguments[0]))
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        "flowJump solo admite una etiqueta, un registro de dirección o una forma condicionada.",
                        action.Arguments[0]);
                }

                break;

            case "flowLoop":
                if (!IsValidFlowTarget(action.Arguments[0]))
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        "flowLoop solo admite una etiqueta local o un registro de dirección.",
                        action.Arguments[0]);
                }

                break;

            case "multiplyWide":
            case "signExtend":
                if (!IsPair(action.Receiver, high: 2, low: 0))
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        $"'{verb}' exige la tupla de receptor (Gpr2:Gpr0).",
                        action.Receiver);
                }

                break;

            case "divide":
            case "divideSigned":
                if (!IsPair(action.Receiver, high: 0, low: 2))
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        $"'{verb}' exige la tupla de receptor (Gpr0:Gpr2).",
                        action.Receiver);
                }

                break;

            case "carry":
            case "parity":
            case "adjust":
            case "zero":
            case "sign":
            case "interrupt":
            case "direction":
            case "overflow":
                if (action.Arguments[0] is not (IntegerLiteralSyntax or NotValueSyntax))
                {
                    Error(
                        DiagnosticIds.InvalidVerb,
                        $"Frf.{verb} solo admite el estado 0, 1 o not.",
                        action.Arguments[0]);
                }

                break;

            default:
                break;
        }
    }

    private void ValidateAddressing(ExpressionSyntax receiver)
    {
        switch (receiver)
        {
            case MemoryOperandSyntax memory:
                ValidateAddress(memory.Address);
                break;

            case PortOperandSyntax port:
                ValidateAddressOperand(port.Address);
                break;

            case RegisterPairSyntax pair:
                ValidateAddressing(pair.High);
                ValidateAddressing(pair.Low);
                break;

            default:
                break;
        }
    }

    private void ValidateAddress(AddressExpressionSyntax address)
    {
        ValidateAddressOperand(address.Base);
        if (address.Index is { } index)
        {
            ValidateAddressOperand(index.Register);
        }
    }

    private void ValidateAddressOperand(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemoryOperandSyntax memory:
                ValidateAddress(memory.Address);
                break;

            case PortOperandSyntax port:
                ValidateAddressOperand(port.Address);
                break;

            case ConditionalExpressionSyntax conditional:
                ValidateAddressOperand(conditional.Value);
                break;

            default:
                break;
        }
    }

    private void ValidateConditionNames(ActionStatementSyntax action)
    {
        if (action.Verb.Text is not ("flowJump" or "conditionalCopy"))
        {
            return;
        }

        foreach (var argument in action.Arguments)
        {
            if (argument is ConditionalExpressionSyntax conditional)
            {
                ValidateCondition(conditional.Condition);
            }
        }
    }

    private void ValidateCondition(ConditionSyntax condition)
    {
        switch (condition)
        {
            case FlagConditionSyntax { Flag: MemberAccessSyntax { Receiver: IdentifierExpressionSyntax { Identifier.Text: "Frf" } } member }
                when !FlagCatalog.TryGetFlags(member.Member.Text, out _):
                diagnostics.Error(
                    DiagnosticIds.InvalidVerb,
                    $"'{member.Member.Text}' no es una bandera o alias válido de Frf.",
                    member.Member.Location,
                    member.Member.Span);
                break;

            case NotConditionSyntax not:
                ValidateCondition(not.Operand);
                break;

            case BinaryConditionSyntax binary:
                ValidateCondition(binary.Left);
                ValidateCondition(binary.Right);
                break;

            default:
                break;
        }
    }

    private static bool IsValidFlowTarget(ExpressionSyntax expression) =>
        expression is LabelReferenceSyntax
        || (expression is ConditionalExpressionSyntax conditional && IsValidFlowTarget(conditional.Value))
        || (expression is IdentifierExpressionSyntax identifier
            && RegisterModel.TryResolveName(identifier.Identifier, out var register)
            && register.IsGpr);

    private static bool IsPair(ExpressionSyntax expression, int high, int low) =>
        expression is RegisterPairSyntax pair
        && IsFullGpr(pair.High, high)
        && IsFullGpr(pair.Low, low);

    private static bool IsFullGpr(ExpressionSyntax expression, int index) =>
        RegisterModel.TryResolve(expression, out var register)
        && register.IsGpr
        && register.View == RegisterView.Full
        && register.Index == index;

    private static Token? FindLegacyReceiver(ExpressionSyntax expression) => expression switch
    {
        IdentifierExpressionSyntax identifier
            when RegisterModel.TryGetLegacySuggestion(identifier.Identifier.Text, out _) => identifier.Identifier,
        MemberAccessSyntax member => FindLegacyReceiver(member.Receiver),
        RegisterPairSyntax pair => FindLegacyReceiver(pair.High) ?? FindLegacyReceiver(pair.Low),
        _ => null,
    };

    private static string DescribeArguments(VerbDefinition definition) =>
        definition.MinArguments == definition.MaxArguments
            ? $"{definition.MinArguments} argumento(s)"
            : $"entre {definition.MinArguments} y {definition.MaxArguments} argumentos";

    private void Error(string id, string message, ExpressionSyntax expression)
    {
        var token = SyntaxTraversal.FirstToken(expression);
        diagnostics.Error(id, message, token?.Location, expression.Span);
    }
}
