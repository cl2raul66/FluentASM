using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>Forma física que puede ocupar el receptor de un verbo.</summary>
[Flags]
internal enum ReceiverShape
{
    None = 0,
    Register = 1 << 0,
    Memory = 1 << 1,
    Port = 1 << 2,
    Stack = 1 << 3,
    Frf = 1 << 4,
    Ipr = 1 << 5,
    Cpu = 1 << 6,
    Pair = 1 << 7,
}

/// <summary>Conjunto de banderas de <c>Frf</c>.</summary>
[Flags]
internal enum FlagSet
{
    None = 0,
    Carry = 1 << 0,
    Parity = 1 << 1,
    Adjust = 1 << 2,
    Zero = 1 << 3,
    Sign = 1 << 4,
    Interrupt = 1 << 5,
    Direction = 1 << 6,
    Overflow = 1 << 7,

    /// <summary>Todas las banderas semánticas de Fase 1.</summary>
    All = Carry | Parity | Adjust | Zero | Sign | Interrupt | Direction | Overflow,

    /// <summary>Banderas aritméticas estándar actualizadas por la ALU.</summary>
    Arithmetic = Carry | Parity | Adjust | Zero | Sign | Overflow,
}

/// <summary>Efecto de una instrucción sobre el registro de banderas.</summary>
/// <param name="Defined">Banderas que quedan definidas con un valor determinista.</param>
/// <param name="Undefined">Banderas que quedan indefinidas.</param>
internal readonly record struct FlagEffect(FlagSet Defined, FlagSet Undefined)
{
    /// <summary>La instrucción no modifica ninguna bandera.</summary>
    public static FlagEffect Preserve => default;

    /// <summary>Aplica el efecto sobre el conjunto de banderas definidas.</summary>
    public FlagSet Apply(FlagSet current) => (current | Defined) & ~Undefined;
}

/// <summary>Descripción declarativa de un verbo de Fase 1.</summary>
/// <param name="Name">Nombre canónico del verbo.</param>
/// <param name="Receivers">Formas de receptor admitidas.</param>
/// <param name="MinArguments">Número mínimo de argumentos.</param>
/// <param name="MaxArguments">Número máximo de argumentos.</param>
/// <param name="Flags">Efecto sobre <c>Frf</c>.</param>
internal sealed record VerbDefinition(
    string Name,
    ReceiverShape Receivers,
    int MinArguments,
    int MaxArguments,
    FlagEffect Flags);

/// <summary>
/// Catálogo Mínimo Viable de verbos x64 de Fase 1
/// (<c>FLUENT_SYNTAX_X64.md</c> §5), con su efecto sobre <c>Frf</c>.
/// </summary>
internal static class VerbCatalog
{
    private static readonly Dictionary<string, VerbDefinition> Definitions = CreateDefinitions();

    /// <summary>Busca la definición de un verbo.</summary>
    public static bool TryGet(string verb, out VerbDefinition definition) =>
        Definitions.TryGetValue(verb, out definition!);

    /// <summary>Traduce una expresión a la forma de receptor que representa.</summary>
    public static ReceiverShape ShapeOf(ExpressionSyntax expression, SymbolTable symbols, RoutineSymbol routine)
    {
        ArgumentNullException.ThrowIfNull(expression);

        switch (expression)
        {
            case RegisterPairSyntax:
                return ReceiverShape.Pair;

            case MemoryOperandSyntax:
                return ReceiverShape.Memory;

            case PortOperandSyntax:
                return ReceiverShape.Port;

            case IdentifierExpressionSyntax identifier:
                if (RegisterModel.TryResolveName(identifier.Identifier, out var register))
                {
                    return register.Unit switch
                    {
                        RegisterUnit.Gpr => ReceiverShape.Register,
                        RegisterUnit.Frf => ReceiverShape.Frf,
                        RegisterUnit.Ipr => ReceiverShape.Ipr,
                        RegisterUnit.Stack => ReceiverShape.Stack,
                        RegisterUnit.Cpu => ReceiverShape.Cpu,
                        _ => ReceiverShape.None,
                    };
                }

                if (routine.Parameters.Any(parameter => parameter.Name == identifier.Identifier.Text))
                {
                    return ReceiverShape.Register;
                }

                return ReceiverShape.None;

            case MemberAccessSyntax:
                if (RegisterModel.TryResolve(expression, out var member))
                {
                    return member.Unit switch
                    {
                        RegisterUnit.Gpr => ReceiverShape.Register,
                        RegisterUnit.Frf => ReceiverShape.Frf,
                        RegisterUnit.Ipr => ReceiverShape.Ipr,
                        _ => ReceiverShape.None,
                    };
                }

                return ReceiverShape.None;

            default:
                return ReceiverShape.None;
        }
    }

    private static Dictionary<string, VerbDefinition> CreateDefinitions()
    {
        var definitions = new Dictionary<string, VerbDefinition>(StringComparer.Ordinal)
        {
            // 5.1 Transferencia y memoria.
            ["copy"] = new("copy", ReceiverShape.Register | ReceiverShape.Memory | ReceiverShape.Port, 1, 1, FlagEffect.Preserve),
            ["copyUnsigned"] = new("copyUnsigned", ReceiverShape.Register, 1, 1, FlagEffect.Preserve),
            ["copySigned"] = new("copySigned", ReceiverShape.Register, 1, 1, FlagEffect.Preserve),
            ["loadAddress"] = new("loadAddress", ReceiverShape.Register, 1, 1, FlagEffect.Preserve),
            ["exchange"] = new("exchange", ReceiverShape.Register | ReceiverShape.Memory, 1, 1, FlagEffect.Preserve),
            ["conditionalCopy"] = new("conditionalCopy", ReceiverShape.Register, 1, 1, FlagEffect.Preserve),
            ["copyBlock"] = new("copyBlock", ReceiverShape.Memory, 1, 1, FlagEffect.Preserve),
            ["fillBlock"] = new("fillBlock", ReceiverShape.Memory, 1, 1, FlagEffect.Preserve),

            // 5.2 Aritmética entera.
            ["add"] = Arithmetic("add", 1),
            ["sub"] = Arithmetic("sub", 1),
            ["addWithCarry"] = Arithmetic("addWithCarry", 1),
            ["subWithBorrow"] = Arithmetic("subWithBorrow", 1),
            ["compare"] = Arithmetic("compare", 1),
            ["increment"] = new(
                "increment",
                ReceiverShape.Register | ReceiverShape.Memory,
                0,
                0,
                new FlagEffect(
                    FlagSet.Overflow | FlagSet.Sign | FlagSet.Zero | FlagSet.Adjust | FlagSet.Parity,
                    FlagSet.None)),
            ["decrement"] = new(
                "decrement",
                ReceiverShape.Register | ReceiverShape.Memory,
                0,
                0,
                new FlagEffect(
                    FlagSet.Overflow | FlagSet.Sign | FlagSet.Zero | FlagSet.Adjust | FlagSet.Parity,
                    FlagSet.None)),
            ["negate"] = new("negate", ReceiverShape.Register | ReceiverShape.Memory, 0, 0, new FlagEffect(FlagSet.Arithmetic, FlagSet.None)),
            ["multiply"] = Multiply("multiply", ReceiverShape.Register | ReceiverShape.Memory, 1),
            ["multiplyWide"] = Multiply("multiplyWide", ReceiverShape.Pair, 1),
            ["divide"] = Divide("divide"),
            ["divideSigned"] = Divide("divideSigned"),
            ["signExtend"] = new("signExtend", ReceiverShape.Pair, 0, 0, FlagEffect.Preserve),

            // 5.3 Lógica y manipulación de bits.
            ["and"] = Logic("and"),
            ["or"] = Logic("or"),
            ["xor"] = Logic("xor"),
            ["test"] = Logic("test"),
            ["not"] = new("not", ReceiverShape.Register | ReceiverShape.Memory, 0, 0, FlagEffect.Preserve),
            ["shiftLeft"] = Shift("shiftLeft"),
            ["shiftRightLogical"] = Shift("shiftRightLogical"),
            ["shiftRightArithmetic"] = Shift("shiftRightArithmetic"),
            ["rotateLeft"] = Rotate("rotateLeft"),
            ["rotateRight"] = Rotate("rotateRight"),
            ["testBit"] = BitFlag("testBit"),
            ["setBit"] = BitFlag("setBit"),
            ["clearBit"] = BitFlag("clearBit"),
            ["toggleBit"] = BitFlag("toggleBit"),

            // 5.4 Pila.
            ["push"] = new("push", ReceiverShape.Stack, 1, 1, FlagEffect.Preserve),
            ["pop"] = new("pop", ReceiverShape.Register | ReceiverShape.Frf, 1, 1, FlagEffect.Preserve),
            ["reserve"] = new("reserve", ReceiverShape.Stack, 1, 1, FlagEffect.Preserve),
            ["release"] = new("release", ReceiverShape.Stack, 1, 1, FlagEffect.Preserve),

            // 5.5 Control de flujo.
            ["flowJump"] = new("flowJump", ReceiverShape.Ipr, 1, 1, FlagEffect.Preserve),
            ["flowCall"] = new("flowCall", ReceiverShape.Ipr, 1, 1, new FlagEffect(FlagSet.None, FlagSet.All)),
            ["flowStackJump"] = new("flowStackJump", ReceiverShape.Ipr, 0, 0, FlagEffect.Preserve),
            ["flowLoop"] = new("flowLoop", ReceiverShape.Ipr, 1, 1, FlagEffect.Preserve),

            // 5.6 Control bare metal y puertos.
            ["noOperation"] = new("noOperation", ReceiverShape.Cpu, 0, 0, FlagEffect.Preserve),
            ["pause"] = new("pause", ReceiverShape.Cpu, 0, 0, FlagEffect.Preserve),
            ["halt"] = new("halt", ReceiverShape.Cpu, 0, 0, FlagEffect.Preserve),

            // 2.3 Manipulación directa de banderas.
            ["carry"] = FlagState("carry", FlagSet.Carry),
            ["parity"] = FlagState("parity", FlagSet.Parity),
            ["adjust"] = FlagState("adjust", FlagSet.Adjust),
            ["zero"] = FlagState("zero", FlagSet.Zero),
            ["sign"] = FlagState("sign", FlagSet.Sign),
            ["interrupt"] = FlagState("interrupt", FlagSet.Interrupt),
            ["direction"] = FlagState("direction", FlagSet.Direction),
            ["overflow"] = FlagState("overflow", FlagSet.Overflow),
        };

        return definitions;
    }

    private static VerbDefinition Arithmetic(string name, int arguments) =>
        new(name, ReceiverShape.Register | ReceiverShape.Memory, arguments, arguments, new FlagEffect(FlagSet.Arithmetic, FlagSet.None));

    private static VerbDefinition Multiply(string name, ReceiverShape receivers, int arguments) =>
        new(
            name,
            receivers,
            arguments,
            arguments,
            new FlagEffect(FlagSet.Carry | FlagSet.Overflow, FlagSet.Sign | FlagSet.Zero | FlagSet.Adjust | FlagSet.Parity));

    private static VerbDefinition Divide(string name) =>
        new(name, ReceiverShape.Pair, 1, 1, new FlagEffect(FlagSet.None, FlagSet.All));

    private static VerbDefinition Logic(string name) =>
        new(
            name,
            ReceiverShape.Register | ReceiverShape.Memory,
            1,
            1,
            new FlagEffect(
                FlagSet.Carry | FlagSet.Overflow | FlagSet.Sign | FlagSet.Zero | FlagSet.Parity,
                FlagSet.Adjust));

    private static VerbDefinition Shift(string name) =>
        new(
            name,
            ReceiverShape.Register | ReceiverShape.Memory,
            1,
            1,
            new FlagEffect(
                FlagSet.Carry | FlagSet.Overflow | FlagSet.Sign | FlagSet.Zero | FlagSet.Parity,
                FlagSet.Adjust));

    private static VerbDefinition Rotate(string name) =>
        new(name, ReceiverShape.Register | ReceiverShape.Memory, 1, 1, new FlagEffect(FlagSet.Carry | FlagSet.Overflow, FlagSet.None));

    private static VerbDefinition BitFlag(string name) =>
        new(name, ReceiverShape.Register | ReceiverShape.Memory, 1, 1, new FlagEffect(FlagSet.Carry, FlagSet.None));

    private static VerbDefinition FlagState(string name, FlagSet flag) =>
        new(name, ReceiverShape.Frf, 1, 1, new FlagEffect(flag, FlagSet.None));
}


