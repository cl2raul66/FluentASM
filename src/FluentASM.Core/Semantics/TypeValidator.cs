using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>
/// Valida el modelo físico ATM de Fase 1 dentro de <c>.execute(...)</c>:
/// magnitudes <c>Bit</c>/<c>Byte</c>, registros <c>GprN</c> y sus vistas,
/// nombres legacy prohibidos, acceso a memoria y política de literales.
/// </summary>
internal sealed class TypeValidator
{
    private static readonly string[] FlagVerbs =
    [
        "carry", "parity", "adjust", "zero", "sign", "interrupt", "direction", "overflow",
    ];

    private static readonly string[] BitMagnitudeVerbs =
    [
        "shiftLeft", "shiftRightLogical", "shiftRightArithmetic",
        "rotateLeft", "rotateRight", "testBit", "setBit", "clearBit", "toggleBit",
    ];

    private static readonly string[] ByteMagnitudeVerbs = ["reserve", "release"];

    private static readonly string[] SameWidthVerbs =
    [
        "copy", "exchange", "add", "sub", "addWithCarry", "subWithBorrow",
        "and", "or", "xor", "compare", "test", "multiply", "conditionalCopy",
    ];

    private readonly DiagnosticBag diagnostics;
    private readonly SymbolTable symbols;
    private readonly RoutineSymbol routine;

    /// <summary>Crea un validador ligado a la rutina que se está analizando.</summary>
    public TypeValidator(DiagnosticBag diagnostics, SymbolTable symbols, RoutineSymbol routine)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(routine);

        this.diagnostics = diagnostics;
        this.symbols = symbols;
        this.routine = routine;
    }

    /// <summary>Valida una acción fluida completa del cuerpo de la rutina.</summary>
    public void ValidateAction(ActionStatementSyntax action)
    {
        ArgumentNullException.ThrowIfNull(action);

        ValidateLiteralPolicy(action);
        ValidateVerbName(action.Verb);
        ValidateReceiver(action.Receiver);
        ValidateMagnitudeContexts(action);

        foreach (var argument in action.Arguments)
        {
            ValidateValue(argument);
        }

        ValidateHigh8Low8Conflict(action);
        ValidateIprCopy(action);
        ValidateSameWidthOperands(action);
    }

    /// <summary>Comprueba que un identificador no use un nombre legacy prohibido.</summary>
    public void ValidateIdentifierName(Token token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (RegisterModel.TryGetLegacySuggestion(token.Text, out var suggestion))
        {
            diagnostics.Error(
                DiagnosticIds.LegacyName,
                $"'{token.Text}' es un nombre legacy prohibido; usa '{suggestion}'.",
                token.Location,
                token.Span);
            return;
        }

        if (RegisterModel.TryGetGprIndex(token.Text, out var index) && index > 15)
        {
            diagnostics.Error(
                DiagnosticIds.GprIndexOutOfRange,
                $"El registro '{token.Text}' está fuera del rango Gpr0–Gpr15 de Fase 1.",
                token.Location,
                token.Span);
        }
    }

    private void ValidateVerbName(Token verb)
    {
        if (RegisterModel.TryGetLegacySuggestion(verb.Text, out var suggestion))
        {
            diagnostics.Error(
                DiagnosticIds.LegacyName,
                $"'{verb.Text}' es un nombre legacy prohibido; usa '{suggestion}'.",
                verb.Location,
                verb.Span);
        }
    }

    private void ValidateReceiver(ExpressionSyntax receiver)
    {
        switch (receiver)
        {
            case IdentifierExpressionSyntax identifier:
                ValidateIdentifierName(identifier.Identifier);
                break;

            case MemberAccessSyntax member:
                ValidateMemberAccess(member);
                break;

            case MemoryOperandSyntax memory:
                ValidateMemoryOperand(memory);
                break;

            case PortOperandSyntax port:
                ValidatePortOperand(port);
                break;

            case RegisterPairSyntax pair:
                ValidateReceiver(pair.High);
                ValidateReceiver(pair.Low);
                break;

            case MagnitudeSyntax magnitude:
                RequireByte(magnitude, "la capacidad de un operando de memoria");
                break;

            default:
                break;
        }
    }

    private void ValidateValue(ExpressionSyntax value)
    {
        switch (value)
        {
            case IdentifierExpressionSyntax identifier:
                ValidateIdentifierName(identifier.Identifier);
                ValidateValueIdentifier(identifier);
                break;

            case MemberAccessSyntax member:
                ValidateMemberReference(member);
                break;

            case MemoryOperandSyntax memory:
                ValidateMemoryOperand(memory);
                break;

            case PortOperandSyntax port:
                ValidatePortOperand(port);
                break;

            case RegisterPairSyntax pair:
                ValidateValue(pair.High);
                ValidateValue(pair.Low);
                break;

            case MagnitudeSyntax magnitude:
                ValidateMagnitude(magnitude);
                break;

            case ConditionalExpressionSyntax conditional:
                ValidateValue(conditional.Value);
                break;

            case LambdaExpressionSyntax:
            case IntegerLiteralSyntax:
            case StringLiteralSyntax:
            case DefaultValueSyntax:
            case NotValueSyntax:
            case LabelReferenceSyntax:
            case ErrorExpressionSyntax:
            default:
                break;
        }
    }

    private void ValidateMemberAccess(MemberAccessSyntax member)
    {
        if (member.Receiver is not IdentifierExpressionSyntax baseIdentifier)
        {
            ValidateReceiver(member.Receiver);
            return;
        }

        ValidateIdentifierName(baseIdentifier.Identifier);

        if (RegisterModel.TryGetGprIndex(baseIdentifier.Identifier.Text, out var index))
        {
            ValidateGprView(baseIdentifier.Identifier, index, member.Member);
            return;
        }

        var unit = baseIdentifier.Identifier.Text;
        if (unit is "Frf" or "Ipr")
        {
            return;
        }

        if (unit is "Stack" or "Cpu")
        {
            diagnostics.Error(
                DiagnosticIds.InvalidRegisterView,
                $"La unidad '{unit}' no admite la vista '.{member.Member.Text}'.",
                member.Member.Location,
                member.Member.Span);
        }
    }

    private void ValidateMemberReference(MemberAccessSyntax member)
    {
        if (member.Receiver is not IdentifierExpressionSyntax baseIdentifier)
        {
            ValidateValue(member.Receiver);
            return;
        }

        ValidateIdentifierName(baseIdentifier.Identifier);

        if (RegisterModel.TryGetGprIndex(baseIdentifier.Identifier.Text, out var index))
        {
            ValidateGprView(baseIdentifier.Identifier, index, member.Member);
            return;
        }

        var unit = baseIdentifier.Identifier.Text;
        if (unit is not ("Frf" or "Ipr" or "Stack" or "Cpu"))
        {
            ValidateValueIdentifier(baseIdentifier);
        }
    }

    private void ValidateGprView(Token register, int index, Token viewToken)
    {
        if (index > 15)
        {
            return;
        }

        if (!RegisterModel.TryParseView(viewToken.Text, out var view))
        {
            diagnostics.Error(
                DiagnosticIds.InvalidRegisterView,
                $"'{viewToken.Text}' no es una vista válida de Gpr{index}; usa low32, low16, low8 o high8.",
                viewToken.Location,
                viewToken.Span);
            return;
        }

        if (view == RegisterView.High8 && index > 3)
        {
            diagnostics.Error(
                DiagnosticIds.High8NotAvailable,
                $".high8 solo existe en Gpr0–Gpr3; 'Gpr{index}.high8' no es accesible.",
                viewToken.Location,
                viewToken.Span);
        }
    }

    private void ValidateValueIdentifier(IdentifierExpressionSyntax identifier)
    {
        var name = identifier.Identifier.Text;
        if (IsKnownUserName(name))
        {
            return;
        }

        if (RegisterModel.TryResolveName(identifier.Identifier, out _))
        {
            return;
        }

        diagnostics.Error(
            DiagnosticIds.UnknownSymbol,
            $"El símbolo '{name}' no existe en el programa ni es un registro de Fase 1.",
            identifier.Identifier.Location,
            identifier.Identifier.Span);
    }

    private bool IsKnownUserName(string name) =>
        symbols.TryGetData(name, out _)
        || symbols.TryGetRoutine(name, out _)
        || routine.Parameters.Any(parameter => parameter.Name == name)
        || symbols.LabelExists(routine, name);

    private void ValidateMemoryOperand(MemoryOperandSyntax memory)
    {
        if (memory.Capacity is { } capacity)
        {
            RequireByte(capacity, "la capacidad de un operando de memoria");
            ValidateAddress(memory.Address);
            return;
        }

        if (IsRegisterBase(memory.Address.Base))
        {
            var anchor = SyntaxTraversal.FirstToken(memory);
            diagnostics.Error(
                DiagnosticIds.MemoryCapacityRequired,
                "Todo acceso a memoria por registro exige capacidad explícita, por ejemplo 'Byte:8 [Gpr3]'.",
                anchor?.Location,
                memory.Span);
        }
        else if (memory.Address.Base is IdentifierExpressionSyntax identifier)
        {
            ValidateIdentifierName(identifier.Identifier);
            if (!symbols.TryGetData(identifier.Identifier.Text, out _)
                && !routine.Parameters.Any(parameter => parameter.Name == identifier.Identifier.Text))
            {
                diagnostics.Error(
                    DiagnosticIds.UnknownSymbol,
                    $"El símbolo de datos '{identifier.Identifier.Text}' no existe.",
                    identifier.Identifier.Location,
                    identifier.Identifier.Span);
            }
        }

        ValidateAddress(memory.Address);
    }

    private void ValidateAddress(AddressExpressionSyntax address)
    {
        ValidateValue(address.Base);

        if (address.Index is not { } index)
        {
            return;
        }

        ValidateValue(index.Register);

        if (RegisterModel.TryResolve(index.Register, out var indexRegister)
            && indexRegister.IsGpr
            && indexRegister.Index == 4)
        {
            var anchor = SyntaxTraversal.FirstToken(index.Register);
            diagnostics.Error(
                DiagnosticIds.Gpr4AsIndex,
                "Gpr4 (RSP) no puede usarse como índice SIB; usa otro registro como índice.",
                anchor?.Location,
                index.Register.Span);
        }

        if (index.Scale is not { } scale)
        {
            return;
        }

        if (scale is not IntegerLiteralSyntax integer || integer.Value is not (1 or 2 or 4 or 8))
        {
            var anchor = SyntaxTraversal.FirstToken(scale);
            diagnostics.Error(
                DiagnosticIds.InvalidScale,
                "La escala de direccionamiento solo puede ser 1, 2, 4 u 8.",
                anchor?.Location,
                scale.Span);
        }
    }

    private bool IsRegisterBase(ExpressionSyntax expression) =>
        RegisterModel.TryResolve(expression, out var reference) && reference.IsValid;

    private void ValidatePortOperand(PortOperandSyntax port)
    {
        switch (port.Address)
        {
            case IntegerLiteralSyntax integer when integer.Value is < 0 or > 0xFFFF:
                diagnostics.Error(
                    DiagnosticIds.InvalidVerb,
                    $"El puerto '{integer.Literal.Text}' debe caber en 16 bits (0x0000–0xFFFF).",
                    integer.Literal.Location,
                    integer.Span);
                break;

            case IntegerLiteralSyntax:
                break;

            default:
                ValidateValue(port.Address);
                break;
        }
    }

    private void ValidateMagnitude(MagnitudeSyntax magnitude)
    {
        if (!MagnitudeInfo.TryCreate(magnitude, out _))
        {
            diagnostics.Error(
                DiagnosticIds.InvalidMagnitude,
                $"La magnitud '{magnitude.Unit.Text}:{magnitude.Size.Text}' no es válida; el tamaño debe ser positivo.",
                magnitude.Unit.Location,
                magnitude.Span);
        }
    }

    private void ValidateMagnitudeContexts(ActionStatementSyntax action)
    {
        var verb = action.Verb.Text;

        if (action.Receiver is MemoryOperandSyntax { Capacity: { } receiverCapacity })
        {
            RequireByte(receiverCapacity, "la capacidad de un operando de memoria");
        }

        foreach (var argument in action.Arguments)
        {
            foreach (var expression in SyntaxTraversal.Descendants(argument))
            {
                switch (expression)
                {
                    case MemoryOperandSyntax { Capacity: { } capacity }:
                        RequireByte(capacity, "la capacidad de un operando de memoria");
                        break;

                    case MagnitudeSyntax magnitude when BitMagnitudeVerbs.Contains(verb, StringComparer.Ordinal):
                        RequireBit(magnitude, $"el operando de '{verb}'");
                        break;

                    case MagnitudeSyntax magnitude when ByteMagnitudeVerbs.Contains(verb, StringComparer.Ordinal):
                        RequireByte(magnitude, $"el operando de '{verb}'");
                        break;

                    default:
                        break;
                }
            }
        }
    }

    private void RequireBit(MagnitudeSyntax magnitude, string context)
    {
        ValidateMagnitude(magnitude);
        if (!MagnitudeInfo.TryCreate(magnitude, out var info) || info.IsBit)
        {
            return;
        }

        diagnostics.Error(
            DiagnosticIds.BitContextExpected,
            $"Se esperaba una cuenta en Bit:N para {context} y se encontró {info.Display}.",
            magnitude.Unit.Location,
            magnitude.Span);
    }

    private void RequireByte(MagnitudeSyntax magnitude, string context)
    {
        ValidateMagnitude(magnitude);
        if (!MagnitudeInfo.TryCreate(magnitude, out var info) || info.IsByte)
        {
            return;
        }

        diagnostics.Error(
            DiagnosticIds.ByteContextExpected,
            $"Se esperaba una capacidad en Byte:N para {context} y se encontró {info.Display}.",
            magnitude.Unit.Location,
            magnitude.Span);
    }

    private void ValidateHigh8Low8Conflict(ActionStatementSyntax action)
    {
        var references = new List<RegisterReference>();
        CollectRegisters(action.Receiver, references);
        foreach (var argument in action.Arguments)
        {
            CollectRegisters(argument, references);
        }

        var high8 = references.FirstOrDefault(reference => reference.View == RegisterView.High8);
        if (high8.Token is null)
        {
            return;
        }

        var conflicting = references.FirstOrDefault(
            reference => reference.View == RegisterView.Low8 && reference.Index is >= 4 and <= 7);
        if (conflicting.Token is null)
        {
            return;
        }

        diagnostics.Error(
            DiagnosticIds.High8Low8Conflict,
            $"'{high8.Display}' y '{conflicting.Display}' no pueden coexistir en la misma instrucción: "
            + $"'.low8' de Gpr4–Gpr7 exige prefijo REX, incompatible con '.high8'.",
            conflicting.Token.Location,
            conflicting.Token.Span);
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
                if (RegisterModel.TryResolve(expression, out var reference) && reference.IsValid)
                {
                    references.Add(reference);
                }

                break;
        }
    }

    private void ValidateIprCopy(ActionStatementSyntax action)
    {
        if (action.Verb.Text != "copy")
        {
            return;
        }

        if (action.Receiver is IdentifierExpressionSyntax receiver
            && receiver.Identifier.Text == "Ipr")
        {
            ReportIprCopy(receiver.Identifier);
            return;
        }

        foreach (var argument in action.Arguments)
        {
            if (argument is IdentifierExpressionSyntax identifier
                && identifier.Identifier.Text == "Ipr")
            {
                ReportIprCopy(identifier.Identifier);
            }
        }
    }

    private void ReportIprCopy(Token token)
    {
        diagnostics.Error(
            DiagnosticIds.IprDirectCopy,
            "Ipr no se lee ni se escribe con '.copy'; usa 'Gpr0.copy(Ipr.current)' o los verbos Ipr.flow*.",
            token.Location,
            token.Span);
    }

    private void ValidateSameWidthOperands(ActionStatementSyntax action)
    {
        if (!SameWidthVerbs.Contains(action.Verb.Text, StringComparer.Ordinal)
            || action.Arguments.Count != 1)
        {
            return;
        }

        if (!TryGetWidth(action.Receiver, out var receiverBits)
            || !TryGetWidth(action.Arguments[0], out var sourceBits))
        {
            return;
        }

        if (receiverBits == sourceBits)
        {
            return;
        }

        var anchor = SyntaxTraversal.FirstToken(action.Arguments[0]);
        diagnostics.Error(
            DiagnosticIds.WidthMismatch,
            $"El destino ocupa {receiverBits} bits y la fuente {sourceBits} bits; los anchos deben coincidir.",
            anchor?.Location,
            action.Arguments[0].Span);
    }

    private bool TryGetWidth(ExpressionSyntax expression, out long bits)
    {
        switch (expression)
        {
            case MemoryOperandSyntax { Capacity: { } capacity }:
                bits = MagnitudeInfo.TryCreate(capacity, out var capacityInfo) ? capacityInfo.Bits : 0;
                return bits > 0;

            case MemoryOperandSyntax { Address: { } address }:
                if (symbols.TryGetData(GetSimpleName(address.Base), out var symbol))
                {
                    bits = symbol.BitSize;
                    return true;
                }

                bits = 0;
                return false;

            case IdentifierExpressionSyntax identifier:
                if (RegisterModel.TryResolveName(identifier.Identifier, out var register))
                {
                    bits = register.Bits;
                    return true;
                }

                var parameter = routine.Parameters.FirstOrDefault(
                    candidate => candidate.Name == identifier.Identifier.Text);
                if (parameter is not null)
                {
                    bits = parameter.Magnitude.Bits;
                    return true;
                }

                bits = 0;
                return false;

            case MemberAccessSyntax:
                if (RegisterModel.TryResolve(expression, out var member))
                {
                    bits = member.Bits;
                    return true;
                }

                bits = 0;
                return false;

            case RegisterPairSyntax:
                bits = 128;
                return true;

            default:
                bits = 0;
                return false;
        }
    }

    private static string? GetSimpleName(ExpressionSyntax expression) =>
        expression is IdentifierExpressionSyntax identifier ? identifier.Identifier.Text : null;

    private void ValidateLiteralPolicy(ActionStatementSyntax action)
    {
        if (IsStructuralFlagState(action))
        {
            return;
        }

        CheckHardData(action.Receiver, structural: false);
        foreach (var argument in action.Arguments)
        {
            CheckHardData(argument, structural: false);
        }
    }

    private bool IsStructuralFlagState(ActionStatementSyntax action) =>
        action.Receiver is IdentifierExpressionSyntax { Identifier.Text: "Frf" }
        && FlagVerbs.Contains(action.Verb.Text, StringComparer.Ordinal)
        && action.Arguments.Count == 1
        && action.Arguments[0] is IntegerLiteralSyntax { Value: 0 or 1 } or NotValueSyntax;

    private void CheckHardData(ExpressionSyntax expression, bool structural)
    {
        switch (expression)
        {
            case IntegerLiteralSyntax integer:
                if (!structural)
                {
                    ReportHardData(integer.Literal);
                }

                break;

            case StringLiteralSyntax text:
                ReportHardData(text.Literal);
                break;

            case MemberAccessSyntax member:
                CheckHardData(member.Receiver, structural: false);
                break;

            case MemoryOperandSyntax memory:
                CheckHardData(memory.Address.Base, structural: false);
                if (memory.Address.Index is { } index)
                {
                    CheckHardData(index.Register, structural: false);
                    if (index.Scale is { } scale)
                    {
                        CheckHardData(scale, structural: true);
                    }
                }

                break;

            case PortOperandSyntax port:
                CheckHardData(port.Address, structural: true);
                break;

            case RegisterPairSyntax pair:
                CheckHardData(pair.High, structural: false);
                CheckHardData(pair.Low, structural: false);
                break;

            case ConditionalExpressionSyntax conditional:
                CheckHardData(conditional.Value, structural: false);
                break;

            case MagnitudeSyntax:
            case DefaultValueSyntax:
            case NotValueSyntax:
            case LabelReferenceSyntax:
            case LambdaExpressionSyntax:
            case ErrorExpressionSyntax:
            default:
                break;
        }
    }

    private void ReportHardData(Token token)
    {
        diagnostics.Error(
            DiagnosticIds.LiteralInExecute,
            $"No se permiten datos duros '{token.Text}' dentro de .execute(...); "
            + "declara el valor en .data(...) o usa defaultValue.",
            token.Location,
            token.Span);
    }
}
