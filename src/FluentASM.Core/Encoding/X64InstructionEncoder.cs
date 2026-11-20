using FluentASM.Core.Diagnostics;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Encoding;

/// <summary>
/// Registro x64 codificable: número físico (0–15), tamaño en bytes de la vista
/// y marca de acceso a los registros altos de 8 bits (<c>AH</c>–<c>BH</c>).
/// </summary>
internal readonly record struct X64Register(int Number, int Size, bool High8 = false)
{
    /// <summary>Campo de 3 bits del registro dentro de ModR/M o SIB.</summary>
    public int Code => High8 ? Number + 4 : Number & 7;

    /// <summary>Bit de extensión REX (R/X/B) del registro.</summary>
    public int Extension => High8 ? 0 : Number >> 3;

    /// <summary>Indica si la vista de 8 bits exige prefijo REX (<c>Gpr4</c>–<c>Gpr7</c>).</summary>
    public bool RequiresRex => !High8 && Size == 1 && Number is >= 4 and <= 7;

    /// <summary>Convierte una referencia semántica a un registro codificable.</summary>
    public static bool TryCreate(RegisterReference reference, out X64Register register)
    {
        var size = reference.View switch
        {
            RegisterView.Full => 8,
            RegisterView.Low32 => 4,
            RegisterView.Low16 => 2,
            RegisterView.Low8 => 1,
            RegisterView.High8 => 1,
            _ => 0,
        };

        if (!reference.IsGpr || reference.Index is < 0 or > 15 || size == 0)
        {
            register = default;
            return false;
        }

        register = new X64Register(reference.Index, size, reference.View == RegisterView.High8);
        return true;
    }
}

/// <summary>Dirección efectiva ya resuelta para una instrucción.</summary>
/// <param name="RipRelative">Indica direccionamiento <c>[rip+disp32]</c>.</param>
/// <param name="Symbol">Símbolo de <c>.data</c> cuando la dirección es RIP-relativa.</param>
/// <param name="Base">Registro base, si existe.</param>
/// <param name="Index">Registro índice, si existe.</param>
/// <param name="Scale">Escala del índice (1, 2, 4 u 8).</param>
/// <param name="Displacement">Desplazamiento sumado.</param>
internal readonly record struct X64Address(
    bool RipRelative,
    string? Symbol,
    X64Register? Base,
    X64Register? Index,
    int Scale,
    long Displacement);

/// <summary>Operando <c>r/m</c> resuelto: registro o memoria.</summary>
internal readonly record struct X64RmOperand(X64Register? Register, X64Address? Memory);

/// <summary>Estado de las banderas aritméticas para resolver condiciones.</summary>
internal readonly record struct X64FlagState(
    bool Carry,
    bool Parity,
    bool Zero,
    bool Sign,
    bool Overflow);

/// <summary>
/// Codificador determinista de instrucciones x64 para el Catálogo Mínimo
/// Viable de Fase 1 (<c>FLUENT_SYNTAX_X64.md</c> §5).
/// </summary>
/// <remarks>
/// El codificador no conoce todavía los offsets definitivos: los saltos y las
/// referencias RIP-relativas se emiten como <see cref="InstructionFixup"/> que
/// el layout resuelve después. Cada acción puede producir varias instrucciones
/// máquina (por ejemplo <c>copyBlock</c> genera <c>MOV ECX</c> + <c>REP MOVSB</c>).
/// </remarks>
internal sealed class X64InstructionEncoder
{
    private readonly SymbolTable symbols;
    private readonly DiagnosticBag diagnostics;

    /// <summary>Crea un codificador que reporta problemas en la bolsa indicada.</summary>
    public X64InstructionEncoder(SymbolTable symbols, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.symbols = symbols;
        this.diagnostics = diagnostics;
    }

    /// <summary>Codifica una acción completa del cuerpo de una rutina.</summary>
    /// <param name="action">Acción del AST.</param>
    /// <param name="routine">Rutina que la contiene (parámetros y etiquetas).</param>
    /// <param name="forceNearBranch">Fuerza la forma cercana de los saltos relajables.</param>
    public IReadOnlyList<EncodedInstruction> Encode(
        ActionStatementSyntax action,
        EncodingRoutine routine,
        bool forceNearBranch)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(routine);

        return new ActionEncoder(this, action, routine, forceNearBranch).Encode();
    }

    private bool TryGetDataSymbol(string? name, out DataSymbol symbol) =>
        symbols.TryGetData(name, out symbol);

    private static string FlagName(int code) => code switch
    {
        0 => "o",
        1 => "no",
        2 => "b",
        3 => "ae",
        4 => "e",
        5 => "ne",
        6 => "be",
        7 => "a",
        8 => "s",
        9 => "ns",
        10 => "p",
        11 => "np",
        12 => "l",
        13 => "ge",
        14 => "le",
        _ => "g",
    };

    /// <summary>Codifica una única acción y acumula sus instrucciones máquina.</summary>
    private sealed class ActionEncoder
    {
        private readonly X64InstructionEncoder host;
        private readonly ActionStatementSyntax action;
        private readonly EncodingRoutine routine;
        private readonly bool forceNearBranch;
        private readonly List<EncodedInstruction> output = [];

        public ActionEncoder(
            X64InstructionEncoder host,
            ActionStatementSyntax action,
            EncodingRoutine routine,
            bool forceNearBranch)
        {
            this.host = host;
            this.action = action;
            this.routine = routine;
            this.forceNearBranch = forceNearBranch;
        }

        public IReadOnlyList<EncodedInstruction> Encode()
        {
            switch (action.Verb.Text)
            {
                case "copy":
                    EncodeCopy();
                    break;

                case "copyUnsigned":
                    EncodeExtend(signed: false);
                    break;

                case "copySigned":
                    EncodeExtend(signed: true);
                    break;

                case "loadAddress":
                    EncodeLoadAddress();
                    break;

                case "exchange":
                    EncodeExchange();
                    break;

                case "conditionalCopy":
                    EncodeConditionalCopy();
                    break;

                case "copyBlock":
                    EncodeBlock(fill: false);
                    break;

                case "fillBlock":
                    EncodeBlock(fill: true);
                    break;

                case "add":
                    EncodeBinary(0x01, 0x03, "add");
                    break;

                case "addWithCarry":
                    EncodeBinary(0x11, 0x13, "adc");
                    break;

                case "sub":
                    EncodeBinary(0x29, 0x2B, "sub");
                    break;

                case "subWithBorrow":
                    EncodeBinary(0x19, 0x1B, "sbb");
                    break;

                case "compare":
                    EncodeBinary(0x39, 0x3B, "cmp");
                    break;

                case "and":
                    EncodeBinary(0x21, 0x23, "and");
                    break;

                case "or":
                    EncodeBinary(0x09, 0x0B, "or");
                    break;

                case "xor":
                    EncodeBinary(0x31, 0x33, "xor");
                    break;

                case "test":
                    EncodeBinary(0x85, 0x85, "test");
                    break;

                case "increment":
                    EncodeUnary(0xFF, 0, "inc");
                    break;

                case "decrement":
                    EncodeUnary(0xFF, 1, "dec");
                    break;

                case "negate":
                    EncodeUnary(0xF7, 3, "neg");
                    break;

                case "not":
                    EncodeUnary(0xF7, 2, "not");
                    break;

                case "multiply":
                    EncodeMultiply();
                    break;

                case "multiplyWide":
                    EncodeImplicitProduct(0xF7, 4, "mul");
                    break;

                case "divide":
                    EncodeImplicitProduct(0xF7, 6, "div");
                    break;

                case "divideSigned":
                    EncodeImplicitProduct(0xF7, 7, "idiv");
                    break;

                case "signExtend":
                    EncodeSignExtend();
                    break;

                case "shiftLeft":
                    EncodeShift(4, "shl");
                    break;

                case "shiftRightLogical":
                    EncodeShift(5, "shr");
                    break;

                case "shiftRightArithmetic":
                    EncodeShift(7, "sar");
                    break;

                case "rotateLeft":
                    EncodeShift(0, "rol");
                    break;

                case "rotateRight":
                    EncodeShift(1, "ror");
                    break;

                case "testBit":
                    EncodeBitOperation(4, "bt");
                    break;

                case "setBit":
                    EncodeBitOperation(5, "bts");
                    break;

                case "clearBit":
                    EncodeBitOperation(6, "btr");
                    break;

                case "toggleBit":
                    EncodeBitOperation(7, "btc");
                    break;

                case "push":
                    EncodePush();
                    break;

                case "pop":
                    EncodePop();
                    break;

                case "reserve":
                    EncodeStackAdjust(subtract: true);
                    break;

                case "release":
                    EncodeStackAdjust(subtract: false);
                    break;

                case "carry":
                    EncodeFlagState("stc", 0xF9, "clc", 0xF8, "cmc", 0xF5);
                    break;

                case "direction":
                    EncodeFlagState("std", 0xFD, "cld", 0xFC, null, 0);
                    break;

                case "interrupt":
                    EncodeFlagState("sti", 0xFB, "cli", 0xFA, null, 0);
                    break;

                case "flowJump":
                    EncodeFlowJump();
                    break;

                case "flowCall":
                    EncodeFlowCall();
                    break;

                case "flowStackJump":
                    AddSimple([0xC3], "ret");
                    break;

                case "flowLoop":
                    EncodeFlowLoop();
                    break;

                case "noOperation":
                    AddSimple([0x90], "nop");
                    break;

                case "pause":
                    AddSimple([0xF3, 0x90], "pause");
                    break;

                case "halt":
                    AddSimple([0xF4], "hlt");
                    break;

                default:
                    Report(
                        DiagnosticIds.InvalidVerb,
                        $"No hay codificación x64 para el verbo '{action.Verb.Text}'.");
                    break;
            }

            return output;
        }

        // ───────────────────────── Transferencia y memoria ─────────────────────────

        private void EncodeCopy()
        {
            if (action.Arguments.Count != 1)
            {
                Report(DiagnosticIds.InvalidArgumentCount, "'copy' espera exactamente un argumento.");
                return;
            }

            var argument = action.Arguments[0];

            if (argument is DefaultValueSyntax)
            {
                EncodeCopyDefault();
                return;
            }

            if (action.Receiver is PortOperandSyntax port)
            {
                EncodePortOut(port, argument);
                return;
            }

            if (argument is PortOperandSyntax inPort)
            {
                EncodePortIn(inPort);
                return;
            }

            if (argument is MemberAccessSyntax
                {
                    Receiver: IdentifierExpressionSyntax { Identifier.Text: "Ipr" },
                    Member.Text: "current",
                })
            {
                EncodeLoadCurrentIp();
                return;
            }

            if (TryResolveRegister(action.Receiver, out _, out var destination))
            {
                if (!TryMakeRmOperand(argument, out var source) || source.Memory is null && source.Register is null)
                {
                    Report(DiagnosticIds.InvalidVerb, "'copy' requiere un registro o un operando de memoria como fuente.");
                    return;
                }

                var size = destination.Size;
                var instruction = EmitRm(
                    [size == 1 ? (byte)0x8A : (byte)0x8B],
                    size,
                    w: size == 8,
                    destination,
                    source,
                    $"mov {Describe(action.Receiver)}, {Describe(argument)}");
                output.Add(instruction);
                return;
            }

            if (action.Receiver is MemoryOperandSyntax memory
                && TryResolveRegister(argument, out _, out var sourceRegister))
            {
                var address = ResolveAddress(memory);
                if (address is null)
                {
                    return;
                }

                var size = OperandSize(memory, sourceRegister.Size);
                var instruction = EmitRm(
                    [ByteOpcode(0x89, size)],
                    size,
                    w: size == 8,
                    sourceRegister,
                    new X64RmOperand(null, address),
                    $"mov {Describe(action.Receiver)}, {Describe(argument)}");
                output.Add(instruction);
                return;
            }

            Report(
                DiagnosticIds.InvalidVerb,
                "'copy' no admite esta combinación de operandos en Fase 1.");
        }

        private void EncodeCopyDefault()
        {
            if (!TryResolveRegister(action.Receiver, out _, out var destination))
            {
                Report(DiagnosticIds.InvalidVerb, "'copy(defaultValue)' exige un registro destino.");
                return;
            }

            var zero = new X64Register(destination.Number, 4);
            var text = $"xor {RegisterText(zero)}, {RegisterText(zero)}";
            output.Add(EmitRm([0x31], 4, w: false, zero, new X64RmOperand(zero, null), text));
        }

        private void EncodeExtend(bool signed)
        {
            if (action.Arguments.Count != 1
                || !TryResolveRegister(action.Receiver, out _, out var destination))
            {
                Report(
                    DiagnosticIds.InvalidVerb,
                    $"'{(signed ? "copySigned" : "copyUnsigned")}' exige un registro destino y un operando de origen.");
                return;
            }

            var argument = action.Arguments[0];
            if (!TryMakeRmOperand(argument, out var source))
            {
                Report(DiagnosticIds.InvalidVerb, "La fuente de extensión debe ser un registro o memoria.");
                return;
            }

            if (destination.Size == 1)
            {
                Report(DiagnosticIds.InvalidVerb, "La extensión con signo o sin signo exige un destino de 16, 32 o 64 bits.");
                return;
            }

            var sourceSize = source.Register?.Size ?? OperandSize((MemoryOperandSyntax)argument, 1);
            var destinationSize = destination.Size;
            var verb = signed ? (sourceSize == 4 ? "movsxd" : "movsx") : "movzx";

            if (!signed && sourceSize == 4)
            {
                var size = destinationSize == 8 ? 4 : destinationSize;
                output.Add(EmitRm(
                    [0x8B],
                    size,
                    w: false,
                    destination,
                    source,
                    $"mov {Describe(action.Receiver)}, {Describe(argument)}"));
                return;
            }

            if (signed && sourceSize == 4 && destinationSize == 4)
            {
                output.Add(EmitRm(
                    [0x8B],
                    4,
                    w: false,
                    destination,
                    source,
                    $"mov {Describe(action.Receiver)}, {Describe(argument)}"));
                return;
            }

            var opcode = sourceSize switch
            {
                1 => new byte[] { 0x0F, signed ? (byte)0xBE : (byte)0xB6 },
                2 => new byte[] { 0x0F, signed ? (byte)0xBF : (byte)0xB7 },
                4 when signed => [0x63],
                _ => [],
            };

            if (opcode.Length == 0)
            {
                Report(
                    DiagnosticIds.InvalidVerb,
                    $"'{verb}' no admite un origen de {sourceSize} byte(s).");
                return;
            }

            output.Add(EmitRm(
                opcode,
                destinationSize,
                w: destinationSize == 8,
                destination,
                source,
                $"{verb} {Describe(action.Receiver)}, {Describe(argument)}"));
        }

        private void EncodeLoadAddress()
        {
            if (action.Arguments.Count != 1
                || !TryResolveRegister(action.Receiver, out _, out var destination))
            {
                Report(DiagnosticIds.InvalidVerb, "'loadAddress' exige un registro destino y un símbolo de .data.");
                return;
            }

            if (action.Arguments[0] is not IdentifierExpressionSyntax symbol
                || !host.TryGetDataSymbol(symbol.Identifier.Text, out _))
            {
                Report(DiagnosticIds.InvalidVerb, "'loadAddress' requiere el identificador de un símbolo de .data.");
                return;
            }

            var address = new X64Address(
                RipRelative: true,
                Symbol: symbol.Identifier.Text,
                Base: null,
                Index: null,
                Scale: 1,
                Displacement: 0);

            var size = destination.Size;
            output.Add(EmitRm(
                [0x8D],
                size,
                w: size == 8,
                destination,
                new X64RmOperand(null, address),
                $"lea {Describe(action.Receiver)}, [rip+{symbol.Identifier.Text}]"));
        }

        private void EncodeExchange()
        {
            if (action.Arguments.Count != 1)
            {
                Report(DiagnosticIds.InvalidArgumentCount, "'exchange' espera exactamente un argumento.");
                return;
            }

            var argument = action.Arguments[0];
            if (TryResolveRegister(action.Receiver, out _, out var destination))
            {
                var size = destination.Size;
                if (TryResolveRegister(argument, out _, out var argumentRegister))
                {
                    output.Add(EmitRm(
                        [ByteOpcode(0x87, size)],
                        size,
                        w: size == 8,
                        argumentRegister,
                        new X64RmOperand(destination, null),
                        $"xchg {Describe(action.Receiver)}, {Describe(argument)}"));
                    return;
                }

                if (argument is MemoryOperandSyntax sourceMemory)
                {
                    var address = ResolveAddress(sourceMemory);
                    if (address is null)
                    {
                        return;
                    }

                    output.Add(EmitRm(
                        [ByteOpcode(0x87, size)],
                        size,
                        w: size == 8,
                        destination,
                        new X64RmOperand(null, address),
                        $"xchg {Describe(action.Receiver)}, {Describe(argument)}"));
                    return;
                }

                Report(DiagnosticIds.InvalidVerb, "'exchange' requiere un registro o memoria como fuente.");
                return;
            }

            if (action.Receiver is MemoryOperandSyntax memory
                && TryResolveRegister(argument, out _, out var sourceRegister))
            {
                var address = ResolveAddress(memory);
                if (address is null)
                {
                    return;
                }

                var size = OperandSize(memory, sourceRegister.Size);
                output.Add(EmitRm(
                    [ByteOpcode(0x87, size)],
                    size,
                    w: size == 8,
                    sourceRegister,
                    new X64RmOperand(null, address),
                    $"xchg {Describe(action.Receiver)}, {Describe(argument)}"));
                return;
            }

            Report(DiagnosticIds.InvalidVerb, "'exchange' no admite esta combinación de operandos.");
        }

        private void EncodeConditionalCopy()
        {
            if (action.Arguments.Count != 1
                || action.Arguments[0] is not ConditionalExpressionSyntax conditional
                || !TryResolveRegister(action.Receiver, out _, out var destination))
            {
                Report(
                    DiagnosticIds.InvalidVerb,
                    "'conditionalCopy' exige 'Gpr0.conditionalCopy(fuente if Frf.condicion)'.");
                return;
            }

            if (!TryGetConditionCode(conditional.Condition, out var code))
            {
                Report(DiagnosticIds.InvalidVerb, "La condición de 'conditionalCopy' no es una bandera válida de Frf.");
                return;
            }

            if (!TryMakeRmOperand(conditional.Value, out var source))
            {
                Report(DiagnosticIds.InvalidVerb, "'conditionalCopy' requiere un registro o memoria como fuente.");
                return;
            }

            var size = destination.Size;
            if (size == 1)
            {
                Report(DiagnosticIds.InvalidVerb, "'conditionalCopy' no admite operandos de 8 bits en x64.");
                return;
            }

            var opcode = new byte[] { 0x0F, (byte)(0x40 | code) };
            output.Add(EmitRm(
                opcode,
                size,
                w: size == 8,
                destination,
                source,
                $"cmov{FlagName(code)} {Describe(action.Receiver)}, {Describe(conditional.Value)}"));
        }

        private void EncodeBlock(bool fill)
        {
            if (action.Receiver is not MemoryOperandSyntax memory || memory.Capacity is null)
            {
                Report(
                    DiagnosticIds.InvalidVerb,
                    $"'{(fill ? "fillBlock" : "copyBlock")}' exige una capacidad Byte:N en el destino.");
                return;
            }

            if (!MagnitudeInfo.TryCreate(memory.Capacity, out var capacity) || !capacity.IsByte)
            {
                Report(DiagnosticIds.InvalidVerb, "La capacidad del bloque debe expresarse en Byte:N.");
                return;
            }

            if (capacity.Bytes is < 0 or > uint.MaxValue)
            {
                Report(DiagnosticIds.InvalidVerb, $"El bloque de {capacity.Bytes} byte(s) excede el contador de 32 bits.");
                return;
            }

            var counter = new InstructionBuilder();
            counter.Add(0xB9);
            counter.AddImmediate(capacity.Bytes, 4);
            output.Add(counter.Build($"mov ecx, {capacity.Bytes}"));

            var repeat = new InstructionBuilder();
            repeat.Add(0xF3);
            repeat.Add((byte)(fill ? 0xAA : 0xA4));
            output.Add(repeat.Build(fill ? "rep stosb" : "rep movsb"));
        }

        // ────────────────────────────── Aritmética ──────────────────────────────

        private void EncodeBinary(byte opcodeRmReg, byte opcodeRegRm, string mnemonic)
        {
            if (action.Arguments.Count != 1)
            {
                Report(DiagnosticIds.InvalidArgumentCount, $"'{action.Verb.Text}' espera exactamente un argumento.");
                return;
            }

            var argument = action.Arguments[0];

            if (TryResolveRegister(action.Receiver, out _, out var destination))
            {
                var size = destination.Size;
                if (TryResolveRegister(argument, out _, out var sourceRegister))
                {
                    output.Add(EmitRm(
                        [ByteOpcode(opcodeRmReg, size)],
                        size,
                        w: size == 8,
                        sourceRegister,
                        new X64RmOperand(destination, null),
                        $"{mnemonic} {Describe(action.Receiver)}, {Describe(argument)}"));
                    return;
                }

                if (argument is MemoryOperandSyntax sourceMemory)
                {
                    var address = ResolveAddress(sourceMemory);
                    if (address is null)
                    {
                        return;
                    }

                    output.Add(EmitRm(
                        [ByteOpcode(opcodeRegRm, size)],
                        size,
                        w: size == 8,
                        destination,
                        new X64RmOperand(null, address),
                        $"{mnemonic} {Describe(action.Receiver)}, {Describe(argument)}"));
                    return;
                }

                Report(DiagnosticIds.InvalidVerb, $"'{action.Verb.Text}' requiere un registro o memoria como fuente.");
                return;
            }

            if (action.Receiver is MemoryOperandSyntax destinationMemory
                && TryResolveRegister(argument, out _, out var source))
            {
                var address = ResolveAddress(destinationMemory);
                if (address is null)
                {
                    return;
                }

                var size = OperandSize(destinationMemory, source.Size);
                output.Add(EmitRm(
                    [ByteOpcode(opcodeRmReg, size)],
                    size,
                    w: size == 8,
                    source,
                    new X64RmOperand(null, address),
                    $"{mnemonic} {Describe(action.Receiver)}, {Describe(argument)}"));
                return;
            }

            Report(DiagnosticIds.InvalidVerb, $"'{action.Verb.Text}' no admite esta combinación de operandos.");
        }

        private void EncodeUnary(byte opcode, int extension, string mnemonic)
        {
            var operand = action.Receiver;
            if (!TryMakeRmOperand(operand, out var rm))
            {
                Report(DiagnosticIds.InvalidVerb, $"'{action.Verb.Text}' requiere un registro o memoria.");
                return;
            }

            var size = rm.Register?.Size ?? OperandSize((MemoryOperandSyntax)operand, 8);
            var byteOpcode = size == 1
                ? (byte)(opcode == 0xF7 ? 0xF6 : 0xFE)
                : opcode;
            var builder = new InstructionBuilder();
            WriteOperandSize(builder, size);
            WriteRex(builder, size == 8, null, extension, rm.Memory, rm.Register);
            builder.Add(byteOpcode);
            WriteModRm(builder, extension, rm);
            output.Add(builder.Build($"{mnemonic} {Describe(operand)}"));
        }

        private void EncodeMultiply()
        {
            if (action.Arguments.Count != 1
                || !TryResolveRegister(action.Receiver, out _, out var destination))
            {
                Report(DiagnosticIds.InvalidVerb, "'multiply' exige un registro destino y un operando de origen.");
                return;
            }

            if (!TryMakeRmOperand(action.Arguments[0], out var source))
            {
                Report(DiagnosticIds.InvalidVerb, "'multiply' requiere un registro o memoria como fuente.");
                return;
            }

            var size = destination.Size;
            if (size == 1)
            {
                Report(DiagnosticIds.InvalidVerb, "'multiply' no admite operandos de 8 bits en x64.");
                return;
            }

            output.Add(EmitRm(
                [0x0F, 0xAF],
                size,
                w: size == 8,
                destination,
                source,
                $"imul {Describe(action.Receiver)}, {Describe(action.Arguments[0])}"));
        }

        private void EncodeImplicitProduct(byte opcode, int extension, string mnemonic)
        {
            if (action.Arguments.Count != 1
                || !TryMakeRmOperand(action.Arguments[0], out var source))
            {
                Report(DiagnosticIds.InvalidVerb, $"'{action.Verb.Text}' requiere un registro o memoria como fuente.");
                return;
            }

            var builder = new InstructionBuilder();
            WriteRex(builder, w: true, null, extension, source.Memory, source.Register);
            builder.Add(opcode);
            WriteModRm(builder, extension, source);
            output.Add(builder.Build($"{mnemonic} {Describe(action.Arguments[0])}"));
        }

        private void EncodeSignExtend() => AddSimple([0x48, 0x99], "cqo");

        // ─────────────────────────── Lógica y bits ───────────────────────────

        private void EncodeShift(int extension, string mnemonic)
        {
            if (action.Arguments.Count != 1
                || !TryMakeRmOperand(action.Receiver, out var rm))
            {
                Report(DiagnosticIds.InvalidVerb, $"'{action.Verb.Text}' requiere un registro o memoria destino.");
                return;
            }

            var size = rm.Register?.Size ?? OperandSize((MemoryOperandSyntax)action.Receiver, 8);
            var argument = action.Arguments[0];

            if (argument is MagnitudeSyntax magnitude
                && MagnitudeInfo.TryCreate(magnitude, out var count))
            {
                var bits = count.Bits;
                var builder = new InstructionBuilder();
                WriteOperandSize(builder, size);
                WriteRex(builder, size == 8, null, extension, rm.Memory, rm.Register);

                if (bits == 1)
                {
                    builder.Add((byte)(size == 1 ? 0xD0 : 0xD1));
                    WriteModRm(builder, extension, rm);
                }
                else
                {
                    builder.Add((byte)(size == 1 ? 0xC0 : 0xC1));
                    WriteModRm(builder, extension, rm);
                    builder.Add((byte)bits);
                }

                output.Add(builder.Build($"{mnemonic} {Describe(action.Receiver)}, {bits}"));
                return;
            }

            if (IsRegister(argument, 1, RegisterView.Low8))
            {
                var builder = new InstructionBuilder();
                WriteOperandSize(builder, size);
                WriteRex(builder, size == 8, null, extension, rm.Memory, rm.Register);
                builder.Add((byte)(size == 1 ? 0xD2 : 0xD3));
                WriteModRm(builder, extension, rm);
                output.Add(builder.Build($"{mnemonic} {Describe(action.Receiver)}, cl"));
                return;
            }

            Report(
                DiagnosticIds.InvalidVerb,
                $"'{action.Verb.Text}' solo admite un conteo Bit:N o Gpr1.low8.");
        }

        private void EncodeBitOperation(int extension, string mnemonic)
        {
            if (action.Arguments.Count != 1
                || !TryMakeRmOperand(action.Receiver, out var rm)
                || action.Arguments[0] is not MagnitudeSyntax magnitude
                || !MagnitudeInfo.TryCreate(magnitude, out var index))
            {
                Report(DiagnosticIds.InvalidVerb, $"'{action.Verb.Text}' requiere un índice Bit:N.");
                return;
            }

            var size = rm.Register?.Size ?? OperandSize((MemoryOperandSyntax)action.Receiver, 8);
            if (size == 1)
            {
                Report(DiagnosticIds.InvalidVerb, $"'{action.Verb.Text}' no admite operandos de 8 bits.");
                return;
            }

            var builder = new InstructionBuilder();
            WriteOperandSize(builder, size);
            WriteRex(builder, size == 8, null, extension, rm.Memory, rm.Register);
            builder.Add(0x0F);
            builder.Add(0xBA);
            WriteModRm(builder, extension, rm);
            builder.Add((byte)index.Bits);
            output.Add(builder.Build($"{mnemonic} {Describe(action.Receiver)}, {index.Bits}"));
        }

        // ─────────────────────────────── Pila ───────────────────────────────

        private void EncodePush()
        {
            if (action.Arguments.Count != 1)
            {
                Report(DiagnosticIds.InvalidArgumentCount, "'Stack.push' espera exactamente un argumento.");
                return;
            }

            var argument = action.Arguments[0];

            if (argument is IdentifierExpressionSyntax { Identifier.Text: "Frf" })
            {
                AddSimple([0x9C], "pushfq");
                return;
            }

            if (TryResolveRegister(argument, out _, out var register))
            {
                var builder = new InstructionBuilder();
                if (register.Extension != 0)
                {
                    builder.Add(0x41);
                }

                builder.Add((byte)(0x50 + register.Code));
                output.Add(builder.Build($"push {Describe(argument)}"));
                return;
            }

            if (TryMakeRmOperand(argument, out var memory) && memory.Memory is { } address)
            {
                var builder = new InstructionBuilder();
                WriteRex(builder, w: false, null, 6, address, null);
                builder.Add(0xFF);
                WriteModRm(builder, 6, new X64RmOperand(null, address));
                output.Add(builder.Build($"push {Describe(argument)}"));
                return;
            }

            Report(DiagnosticIds.InvalidVerb, "'Stack.push' solo admite un registro Gpr, memoria o Frf.");
        }

        private void EncodePop()
        {
            if (action.Arguments.Count != 1)
            {
                Report(DiagnosticIds.InvalidArgumentCount, "'pop(Stack)' espera exactamente un argumento.");
                return;
            }

            if (action.Receiver is IdentifierExpressionSyntax { Identifier.Text: "Frf" })
            {
                AddSimple([0x9D], "popfq");
                return;
            }

            if (!TryResolveRegister(action.Receiver, out _, out var register))
            {
                Report(DiagnosticIds.InvalidVerb, "'pop(Stack)' exige un registro Gpr o Frf como receptor.");
                return;
            }

            var builder = new InstructionBuilder();
            if (register.Extension != 0)
            {
                builder.Add(0x41);
            }

            builder.Add((byte)(0x58 + register.Code));
            output.Add(builder.Build($"pop {Describe(action.Receiver)}"));
        }

        private void EncodeStackAdjust(bool subtract)
        {
            if (action.Arguments.Count != 1
                || action.Arguments[0] is not MagnitudeSyntax magnitude
                || !MagnitudeInfo.TryCreate(magnitude, out var amount))
            {
                Report(
                    DiagnosticIds.InvalidVerb,
                    $"'Stack.{(subtract ? "reserve" : "release")}' requiere una capacidad Byte:N.");
                return;
            }

            var bytes = amount.Bytes;
            var rsp = new X64Register(4, 8);
            var builder = new InstructionBuilder();
            builder.Add(0x48);

            if (bytes is >= sbyte.MinValue and <= sbyte.MaxValue)
            {
                builder.Add(0x83);
                WriteModRm(builder, subtract ? 5 : 0, new X64RmOperand(rsp, null));
                builder.Add((byte)(sbyte)bytes);
            }
            else
            {
                builder.Add(0x81);
                WriteModRm(builder, subtract ? 5 : 0, new X64RmOperand(rsp, null));
                builder.AddImmediate(bytes, 4);
            }

            output.Add(builder.Build($"{(subtract ? "sub" : "add")} rsp, {bytes}"));
        }

        // ───────────────────────────── Banderas ─────────────────────────────

        private void EncodeFlagState(
            string setText,
            byte setOpcode,
            string clearText,
            byte clearOpcode,
            string? complementText,
            byte complementOpcode)
        {
            if (action.Arguments.Count != 1)
            {
                Report(DiagnosticIds.InvalidArgumentCount, $"'Frf.{action.Verb.Text}' espera exactamente un argumento.");
                return;
            }

            switch (action.Arguments[0])
            {
                case IntegerLiteralSyntax { Value: 1 }:
                    AddSimple([setOpcode], setText);
                    break;

                case IntegerLiteralSyntax { Value: 0 }:
                    AddSimple([clearOpcode], clearText);
                    break;

                case NotValueSyntax when complementText is not null:
                    AddSimple([complementOpcode], complementText);
                    break;

                default:
                    Report(
                        DiagnosticIds.InvalidVerb,
                        $"'Frf.{action.Verb.Text}' solo admite 1, 0 o not, y solo carry/direction/interrupt tienen instrucción directa en Fase 1.");
                    break;
            }
        }

        // ─────────────────────────── Control de flujo ───────────────────────────

        private void EncodeFlowJump()
        {
            if (action.Arguments.Count != 1)
            {
                Report(DiagnosticIds.InvalidArgumentCount, "'Ipr.flowJump' espera exactamente un argumento.");
                return;
            }

            var argument = action.Arguments[0];

            if (argument is ConditionalExpressionSyntax conditional)
            {
                if (!TryGetConditionCode(conditional.Condition, out var code))
                {
                    Report(DiagnosticIds.InvalidVerb, "La condición de 'flowJump' no es una bandera válida de Frf.");
                    return;
                }

                if (conditional.Value is not LabelReferenceSyntax label)
                {
                    Report(DiagnosticIds.InvalidVerb, "'flowJump' condicional solo admite una etiqueta local.");
                    return;
                }

                output.Add(EmitConditionalBranch(label, code));
                return;
            }

            if (argument is LabelReferenceSyntax target)
            {
                output.Add(EmitUnconditionalBranch(target));
                return;
            }

            if (TryResolveRegister(argument, out _, out var register))
            {
                var builder = new InstructionBuilder();
                WriteRex(builder, w: false, register, 4, null, null);
                builder.Add(0xFF);
                WriteModRm(builder, 4, new X64RmOperand(register, null));
                output.Add(builder.Build($"jmp {Describe(argument)}"));
                return;
            }

            Report(
                DiagnosticIds.InvalidVerb,
                "'flowJump' solo admite una etiqueta, un registro de dirección o una forma condicionada.");
        }

        private EncodedInstruction EmitUnconditionalBranch(LabelReferenceSyntax target)
        {
            if (!routine.TryGetLabel(target.Label.Text, out _))
            {
                Report(DiagnosticIds.UnknownSymbol, $"La etiqueta '{target.Label.Text}' no existe en la rutina.");
            }

            var fixupTarget = new InstructionFixupTarget(
                InstructionFixupTargetKind.LocalLabel,
                target.Label.Text);
            var builder = new InstructionBuilder();

            if (forceNearBranch)
            {
                builder.Add(0xE9);
                builder.AddFixup(4, InstructionFixupKind.Relative, fixupTarget, 0);
                return builder.Build($"jmp near {target.Label.Text}", BranchForm.Near);
            }

            builder.Add(0xEB);
            builder.AddFixup(1, InstructionFixupKind.Relative, fixupTarget, 0);
            return builder.Build($"jmp short {target.Label.Text}", BranchForm.Short);
        }

        private EncodedInstruction EmitConditionalBranch(LabelReferenceSyntax target, int code)
        {
            if (!routine.TryGetLabel(target.Label.Text, out _))
            {
                Report(DiagnosticIds.UnknownSymbol, $"La etiqueta '{target.Label.Text}' no existe en la rutina.");
            }

            var fixupTarget = new InstructionFixupTarget(
                InstructionFixupTargetKind.LocalLabel,
                target.Label.Text);
            var builder = new InstructionBuilder();

            if (forceNearBranch)
            {
                builder.Add(0x0F);
                builder.Add((byte)(0x80 | code));
                builder.AddFixup(4, InstructionFixupKind.Relative, fixupTarget, 0);
                return builder.Build($"j{FlagName(code)} near {target.Label.Text}", BranchForm.Near);
            }

            builder.Add((byte)(0x70 | code));
            builder.AddFixup(1, InstructionFixupKind.Relative, fixupTarget, 0);
            return builder.Build($"j{FlagName(code)} short {target.Label.Text}", BranchForm.Short);
        }

        private void EncodeFlowCall()
        {
            if (action.Arguments.Count != 1)
            {
                Report(DiagnosticIds.InvalidArgumentCount, "'Ipr.flowCall' espera exactamente un argumento.");
                return;
            }

            var argument = action.Arguments[0];

            if (TryResolveRegister(argument, out _, out var register))
            {
                var builder = new InstructionBuilder();
                WriteRex(builder, w: false, register, 2, null, null);
                builder.Add(0xFF);
                WriteModRm(builder, 2, new X64RmOperand(register, null));
                output.Add(builder.Build($"call {Describe(argument)}"));
                return;
            }

            var targetName = ResolveCallTarget(argument);
            if (targetName is null || !host.symbols.TryGetRoutine(targetName, out _))
            {
                Report(
                    DiagnosticIds.CallTargetNotFound,
                    "El destino de 'flowCall' no existe o no es una rutina con nombre.");
                return;
            }

            var call = new InstructionBuilder();
            call.Add(0xE8);
            call.AddFixup(
                4,
                InstructionFixupKind.Relative,
                new InstructionFixupTarget(InstructionFixupTargetKind.Routine, targetName),
                0);
            output.Add(call.Build($"call rel32 {targetName}"));
        }

        private static string? ResolveCallTarget(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case IdentifierExpressionSyntax identifier:
                    return identifier.Identifier.Text;

                case LambdaExpressionSyntax { Body: FluentChainBodySyntax chain }:
                    foreach (var call in chain.Calls)
                    {
                        if (call.Name.Text == "target"
                            && call.Arguments.Count == 1
                            && call.Arguments[0] is IdentifierExpressionSyntax target)
                        {
                            return target.Identifier.Text;
                        }
                    }

                    return null;

                default:
                    return null;
            }
        }

        private void EncodeFlowLoop()
        {
            if (action.Arguments.Count != 1
                || action.Arguments[0] is not LabelReferenceSyntax target)
            {
                Report(DiagnosticIds.InvalidVerb, "'flowLoop' exige una etiqueta local.");
                return;
            }

            if (!routine.TryGetLabel(target.Label.Text, out _))
            {
                Report(DiagnosticIds.UnknownSymbol, $"La etiqueta '{target.Label.Text}' no existe en la rutina.");
            }

            var builder = new InstructionBuilder();
            builder.Add(0xE2);
            builder.AddFixup(
                1,
                InstructionFixupKind.Relative,
                new InstructionFixupTarget(InstructionFixupTargetKind.LocalLabel, target.Label.Text),
                0);
            output.Add(builder.Build($"loop {target.Label.Text}", BranchForm.Loop));
        }

        // ─────────────────────────── Puertos ───────────────────────────

        private void EncodePortOut(PortOperandSyntax port, ExpressionSyntax value)
        {
            if (!TryResolveRegister(value, out _, out var register))
            {
                Report(DiagnosticIds.InvalidVerb, "'Port[...].copy' requiere un registro de 8 bits como valor.");
                return;
            }

            var valueText = register.Size == 1 ? "al" : Describe(value);
            if (port.Address is IntegerLiteralSyntax integer && integer.Value <= byte.MaxValue)
            {
                var builder = new InstructionBuilder();
                builder.Add(0xE6);
                builder.Add((byte)integer.Value);
                output.Add(builder.Build($"out 0x{integer.Value:X2}, {valueText}"));
                return;
            }

            output.AddRange(EmitPortAddress(port.Address));
            AddSimple([0xEE], $"out dx, {valueText}");
        }

        private void EncodePortIn(PortOperandSyntax port)
        {
            if (!TryResolveRegister(action.Receiver, out _, out var destination))
            {
                Report(DiagnosticIds.InvalidVerb, "'copy(Port[...])' requiere un registro destino.");
                return;
            }

            var size = destination.Size == 8 ? 4 : destination.Size;
            var destinationText = size switch
            {
                1 => "al",
                2 => "ax",
                _ => "eax",
            };

            if (port.Address is IntegerLiteralSyntax integer && integer.Value <= byte.MaxValue)
            {
                var builder = new InstructionBuilder();
                WriteOperandSize(builder, size);
                builder.Add(size == 1 ? (byte)0xE4 : (byte)0xE5);
                builder.Add((byte)integer.Value);
                output.Add(builder.Build($"in {destinationText}, 0x{integer.Value:X2}"));
                return;
            }

            output.AddRange(EmitPortAddress(port.Address));
            var reader = new InstructionBuilder();
            WriteOperandSize(reader, size);
            reader.Add(size == 1 ? (byte)0xEC : (byte)0xED);
            output.Add(reader.Build($"in {destinationText}, dx"));
        }

        private IReadOnlyList<EncodedInstruction> EmitPortAddress(ExpressionSyntax address)
        {
            if (address is IntegerLiteralSyntax integer)
            {
                var builder = new InstructionBuilder();
                builder.Add(0x66);
                builder.Add(0xBA);
                builder.AddImmediate(integer.Value, 2);
                return [builder.Build($"mov dx, 0x{integer.Value:X4}")];
            }

            if (IsRegister(address, 2, RegisterView.Low16))
            {
                return [];
            }

            Report(DiagnosticIds.InvalidVerb, "El puerto debe ser un inmediato de 16 bits o Gpr2.low16 (DX).");
            return [];
        }

        // ───────────────────────────── Auxiliares ─────────────────────────────

        private void EncodeLoadCurrentIp()
        {
            if (!TryResolveRegister(action.Receiver, out _, out var destination))
            {
                Report(DiagnosticIds.InvalidVerb, "'copy(Ipr.current)' exige un registro destino.");
                return;
            }

            var address = new X64Address(true, null, null, null, 1, 0);
            var builder = new InstructionBuilder();
            WriteRex(builder, destination.Size == 8, destination, 0, address, null);
            builder.Add(0x8D);
            WriteModRm(builder, destination.Code, new X64RmOperand(null, address));
            output.Add(builder.Build($"lea {Describe(action.Receiver)}, [rip]"));
        }

        private EncodedInstruction EmitRm(
            byte[] opcode,
            int operandSize,
            bool w,
            X64Register reg,
            X64RmOperand rm,
            string text)
        {
            var builder = new InstructionBuilder();
            WriteOperandSize(builder, operandSize);
            WriteRex(builder, w, reg, reg.Code, rm.Memory, rm.Register);
            builder.Add(opcode);
            WriteModRm(builder, reg.Code, rm);
            return builder.Build(text);
        }

        private void WriteOperandSize(InstructionBuilder builder, int size)
        {
            if (size == 2)
            {
                builder.Add(0x66);
            }
        }

        private void WriteRex(
            InstructionBuilder builder,
            bool w,
            X64Register? reg,
            int regExtension,
            X64Address? memory,
            X64Register? rmRegister)
        {
            var regExt = reg?.Extension ?? (regExtension >> 3);
            var indexExt = memory?.Index?.Extension ?? 0;
            var baseExt = memory?.Base?.Extension ?? rmRegister?.Extension ?? 0;
            var force = (reg?.RequiresRex ?? false) || (rmRegister?.RequiresRex ?? false);
            var high8 = (reg?.High8 ?? false) || (rmRegister?.High8 ?? false);

            if (!w && regExt == 0 && indexExt == 0 && baseExt == 0 && !force)
            {
                return;
            }

            if (high8)
            {
                if (w || regExt != 0 || indexExt != 0 || baseExt != 0)
                {
                    Report(
                        DiagnosticIds.High8Low8Conflict,
                        "Un registro .high8 no puede combinarse con operandos que exigen prefijo REX.");
                }

                return;
            }

            builder.Add((byte)(0x40 | (w ? 0x08 : 0) | (regExt << 2) | (indexExt << 1) | baseExt));
        }

        private void WriteModRm(InstructionBuilder builder, int regCode, X64RmOperand rm)
        {
            if (rm.Register is { } register)
            {
                builder.Add((byte)(0xC0 | ((regCode & 7) << 3) | register.Code));
                return;
            }

            var address = rm.Memory!.Value;
            if (address.RipRelative)
            {
                builder.Add((byte)(((regCode & 7) << 3) | 0x05));
                if (address.Symbol is { } symbol)
                {
                    builder.AddFixup(
                        4,
                        InstructionFixupKind.RipRelative,
                        new InstructionFixupTarget(InstructionFixupTargetKind.DataSymbol, symbol),
                        address.Displacement);
                }
                else
                {
                    builder.AddImmediate(0, 4);
                }

                return;
            }

            var baseRegister = address.Base!.Value;
            var displacement = address.Displacement;
            var needsSib = address.Index is not null || baseRegister.Code == 4;
            int mod;

            if (displacement == 0 && baseRegister.Code != 5)
            {
                mod = 0;
            }
            else if (displacement is >= sbyte.MinValue and <= sbyte.MaxValue)
            {
                mod = 1;
            }
            else
            {
                mod = 2;
            }

            builder.Add((byte)((mod << 6) | ((regCode & 7) << 3) | (needsSib ? 4 : baseRegister.Code)));

            if (needsSib)
            {
                var index = address.Index;
                var indexCode = index?.Code ?? 4;
                var scale = index is null ? 0 : ScaleBits(address.Scale);
                builder.Add((byte)((scale << 6) | (indexCode << 3) | baseRegister.Code));
            }

            if (mod == 1)
            {
                builder.Add((byte)(sbyte)displacement);
            }
            else if (mod == 2)
            {
                builder.AddImmediate(displacement, 4);
            }
        }

        private bool TryMakeRmOperand(ExpressionSyntax expression, out X64RmOperand operand)
        {
            if (TryResolveRegister(expression, out _, out var register))
            {
                operand = new X64RmOperand(register, null);
                return true;
            }

            if (expression is MemoryOperandSyntax memory)
            {
                var address = ResolveAddress(memory);
                if (address is not null)
                {
                    operand = new X64RmOperand(null, address);
                    return true;
                }
            }

            operand = default;
            return false;
        }

        private X64Address? ResolveAddress(MemoryOperandSyntax memory)
        {
            if (!TryResolveAddress(memory.Address, out var address))
            {
                return null;
            }

            return address;
        }

        private bool TryResolveAddress(AddressExpressionSyntax address, out X64Address resolved)
        {
            resolved = default;

            if (address.Base is IdentifierExpressionSyntax symbol
                && host.TryGetDataSymbol(symbol.Identifier.Text, out _))
            {
                if (address.Index is not null)
                {
                    Report(
                        DiagnosticIds.InvalidScale,
                        "El direccionamiento RIP-relativo de un símbolo de .data no admite registro índice en x64; "
                        + "usa una base de registro o loadAddress.");
                    return false;
                }

                resolved = new X64Address(
                    RipRelative: true,
                    Symbol: symbol.Identifier.Text,
                    Base: null,
                    Index: null,
                    Scale: 1,
                    Displacement: address.Displacement);
                return true;
            }

            if (!TryResolveRegister(address.Base, out _, out var baseRegister)
                || baseRegister.Number is < 0 or > 15)
            {
                Report(DiagnosticIds.InvalidVerb, "La base de la dirección debe ser un registro Gpr o un símbolo de .data.");
                return false;
            }

            baseRegister = new X64Register(baseRegister.Number, 8);
            X64Register? indexRegister = null;
            var scale = 1;

            if (address.Index is { } index)
            {
                if (!TryResolveRegister(index.Register, out _, out var candidate)
                    || candidate.Number == 4)
                {
                    Report(
                        DiagnosticIds.Gpr4AsIndex,
                        "El índice de la dirección debe ser un registro Gpr distinto de Gpr4.");
                    return false;
                }

                indexRegister = new X64Register(candidate.Number, 8);
                scale = index.Scale is IntegerLiteralSyntax integer ? (int)integer.Value : 1;
                if (scale is not (1 or 2 or 4 or 8))
                {
                    Report(DiagnosticIds.InvalidScale, "La escala de direccionamiento solo puede ser 1, 2, 4 u 8.");
                    return false;
                }
            }

            resolved = new X64Address(
                RipRelative: false,
                Symbol: null,
                Base: baseRegister,
                Index: indexRegister,
                Scale: scale,
                Displacement: address.Displacement);
            return true;
        }

        private bool TryResolveRegister(
            ExpressionSyntax expression,
            out RegisterReference reference,
            out X64Register register)
        {
            if (RegisterModel.TryResolve(expression, out reference)
                && X64Register.TryCreate(reference, out register))
            {
                return true;
            }

            if (expression is IdentifierExpressionSyntax identifier
                && routine.TryGetParameter(identifier.Identifier.Text, out var parameter))
            {
                reference = new RegisterReference(
                    RegisterUnit.Gpr,
                    parameter.RegisterIndex,
                    RegisterView.Full,
                    null,
                    identifier.Identifier);
                register = new X64Register(parameter.RegisterIndex, 8);
                return true;
            }

            reference = default;
            register = default;
            return false;
        }

        private bool IsRegister(ExpressionSyntax expression, int index, RegisterView view) =>
            TryResolveRegister(expression, out var reference, out _)
            && reference.Index == index
            && reference.View == view;

        private int OperandSize(MemoryOperandSyntax memory, int fallback)
        {
            if (memory.Capacity is { } capacity
                && MagnitudeInfo.TryCreate(capacity, out var info))
            {
                return ScalarSize(info);
            }

            if (memory.Address.Base is IdentifierExpressionSyntax symbol
                && host.TryGetDataSymbol(symbol.Identifier.Text, out var data))
            {
                return ScalarSize(data.ByteSize);
            }

            return fallback;
        }

        private static int ScalarSize(MagnitudeInfo info) => ScalarSize(info.IsBit ? (info.Bits + 7) / 8 : info.Bytes);

        private static int ScalarSize(long bytes) => bytes switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 4 => 4,
            _ => 8,
        };

        private static int ScaleBits(int scale) => scale switch
        {
            2 => 1,
            4 => 2,
            8 => 3,
            _ => 0,
        };

        private static byte ByteOpcode(byte opcode, int size) =>
            size == 1 ? (byte)(opcode - 1) : opcode;

        private bool TryGetConditionCode(ConditionSyntax condition, out int code)
        {
            for (var candidate = 0; candidate < 16; candidate++)
            {
                var matches = true;
                for (var bits = 0; bits < 32 && matches; bits++)
                {
                    var flags = new X64FlagState(
                        Carry: (bits & 1) != 0,
                        Parity: (bits & 2) != 0,
                        Zero: (bits & 4) != 0,
                        Sign: (bits & 8) != 0,
                        Overflow: (bits & 16) != 0);
                    matches = Evaluate(condition, flags) == EvaluateConditionCode(candidate, flags);
                }

                if (matches)
                {
                    code = candidate;
                    return true;
                }
            }

            code = 0;
            return false;
        }

        private static bool Evaluate(ConditionSyntax condition, X64FlagState flags) => condition switch
        {
            FlagConditionSyntax flag => EvaluateFlag(flag.Flag, flags),
            NotConditionSyntax not => !Evaluate(not.Operand, flags),
            BinaryConditionSyntax binary => binary.Operator.Kind switch
            {
                TokenKind.Or => Evaluate(binary.Left, flags) | Evaluate(binary.Right, flags),
                TokenKind.And => Evaluate(binary.Left, flags) & Evaluate(binary.Right, flags),
                TokenKind.Equal => Evaluate(binary.Left, flags) == Evaluate(binary.Right, flags),
                TokenKind.Unequal => Evaluate(binary.Left, flags) != Evaluate(binary.Right, flags),
                _ => false,
            },
            _ => false,
        };

        private static bool EvaluateFlag(ExpressionSyntax expression, X64FlagState flags)
        {
            if (expression is not MemberAccessSyntax
                {
                    Receiver: IdentifierExpressionSyntax { Identifier.Text: "Frf" },
                } member)
            {
                return false;
            }

            return member.Member.Text switch
            {
                "carry" or "below" => flags.Carry,
                "aboveOrEqual" => !flags.Carry,
                "parity" => flags.Parity,
                "zero" or "equal" => flags.Zero,
                "notEqual" => !flags.Zero,
                "sign" => flags.Sign,
                "overflow" => flags.Overflow,
                "belowOrEqual" => flags.Carry || flags.Zero,
                "above" => !flags.Carry && !flags.Zero,
                "less" => flags.Sign != flags.Overflow,
                "greaterOrEqual" => flags.Sign == flags.Overflow,
                "lessOrEqual" => flags.Zero || flags.Sign != flags.Overflow,
                "greater" => !flags.Zero && flags.Sign == flags.Overflow,
                _ => false,
            };
        }

        private static bool EvaluateConditionCode(int code, X64FlagState flags) => code switch
        {
            0 => flags.Overflow,
            1 => !flags.Overflow,
            2 => flags.Carry,
            3 => !flags.Carry,
            4 => flags.Zero,
            5 => !flags.Zero,
            6 => flags.Carry || flags.Zero,
            7 => !flags.Carry && !flags.Zero,
            8 => flags.Sign,
            9 => !flags.Sign,
            10 => flags.Parity,
            11 => !flags.Parity,
            12 => flags.Sign != flags.Overflow,
            13 => flags.Sign == flags.Overflow,
            14 => flags.Zero || flags.Sign != flags.Overflow,
            _ => !flags.Zero && flags.Sign == flags.Overflow,
        };

        private string Describe(ExpressionSyntax expression)
        {
            if (expression is IdentifierExpressionSyntax identifier
                && routine.TryGetParameter(identifier.Identifier.Text, out _))
            {
                return identifier.Identifier.Text;
            }

            if (TryResolveRegister(expression, out var reference, out _))
            {
                return reference.Display;
            }

            return expression switch
            {
                MemoryOperandSyntax memory => DescribeMemory(memory),
                PortOperandSyntax port => $"Port[{Describe(port.Address)}]",
                MagnitudeSyntax magnitude => $"{magnitude.Unit.Text}:{magnitude.Size.Text}",
                IntegerLiteralSyntax integer => $"0x{integer.Value:X}",
                LabelReferenceSyntax label => label.Label.Text,
                DefaultValueSyntax => "defaultValue",
                NotValueSyntax => "not",
                _ => expression.GetType().Name,
            };
        }

        private string DescribeMemory(MemoryOperandSyntax memory)
        {
            var capacity = memory.Capacity is { } magnitude
                ? $"{magnitude.Unit.Text}:{magnitude.Size.Text} "
                : string.Empty;
            var address = memory.Address;
            var parts = new List<string>();

            if (address.Base is IdentifierExpressionSyntax symbol
                && host.TryGetDataSymbol(symbol.Identifier.Text, out _))
            {
                parts.Add($"rip+{symbol.Identifier.Text}");
            }
            else
            {
                parts.Add(Describe(address.Base));
            }

            if (address.Index is { } index)
            {
                var scale = index.Scale is IntegerLiteralSyntax integer ? integer.Value : 1;
                parts.Add($"{Describe(index.Register)}*{scale}");
            }

            var text = string.Join(" + ", parts);
            if (address.Displacement > 0)
            {
                text += $" + {Invariant(address.Displacement)}";
            }
            else if (address.Displacement < 0)
            {
                text += $" - {Invariant(-address.Displacement)}";
            }

            return $"{capacity}[{text}]";
        }

        private static string Invariant(long value) =>
            value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string RegisterText(X64Register register)
        {
            var name = $"Gpr{register.Number}";
            return register.Size switch
            {
                1 => register.High8 ? $"{name}.high8" : $"{name}.low8",
                2 => $"{name}.low16",
                4 => $"{name}.low32",
                _ => name,
            };
        }

        private void AddSimple(byte[] bytes, string text) =>
            output.Add(new EncodedInstruction(bytes, text, []));

        private void Report(string id, string message) =>
            host.diagnostics.Error(id, message, action.Verb.Location, action.Span);
    }

    /// <summary>Constructor incremental de bytes con referencias pendientes.</summary>
    private sealed class InstructionBuilder
    {
        private readonly List<byte> bytes = [];
        private readonly List<InstructionFixup> fixups = [];

        public void Add(byte value) => bytes.Add(value);

        public void Add(params byte[] values) => bytes.AddRange(values);

        public void AddImmediate(long value, int size)
        {
            var unsigned = unchecked((ulong)value);
            for (var index = 0; index < size; index++)
            {
                bytes.Add((byte)(unsigned >> (index * 8)));
            }
        }

        public void AddFixup(int size, InstructionFixupKind kind, InstructionFixupTarget target, long addend)
        {
            var offset = bytes.Count;
            for (var index = 0; index < size; index++)
            {
                bytes.Add(0);
            }

            fixups.Add(new InstructionFixup(offset, size, kind, target, addend));
        }

        public EncodedInstruction Build(string text, BranchForm branch = BranchForm.None) =>
            new([.. bytes], text, fixups, branch);
    }
}


