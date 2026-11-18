using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Semantics;

/// <summary>Unidad física referida por un receptor o una fuente.</summary>
public enum RegisterUnit
{
    /// <summary>El receptor no es una unidad física conocida.</summary>
    None,

    /// <summary>Registro de propósito general <c>GprN</c>.</summary>
    Gpr,

    /// <summary>Registro de banderas <c>Frf</c>.</summary>
    Frf,

    /// <summary>Puntero de instrucción <c>Ipr</c>.</summary>
    Ipr,

    /// <summary>Pila <c>Stack</c> (sobre <c>Gpr4</c>).</summary>
    Stack,

    /// <summary>Unidad de control de la máquina <c>Cpu</c>.</summary>
    Cpu,
}

/// <summary>Vista o ventana aplicada sobre una unidad física.</summary>
public enum RegisterView
{
    /// <summary>La celda completa (64 bits en <c>Gpr</c>).</summary>
    Full,

    /// <summary>Ventana de 32 bits bajos.</summary>
    Low32,

    /// <summary>Ventana de 16 bits bajos.</summary>
    Low16,

    /// <summary>Ventana de 8 bits bajos.</summary>
    Low8,

    /// <summary>Ventana de bits 8–15 (solo <c>Gpr0</c>–<c>Gpr3</c>).</summary>
    High8,

    /// <summary>Miembro semántico de <c>Frf</c> o <c>Ipr</c> (por ejemplo <c>Frf.zero</c>).</summary>
    Member,

    /// <summary>Vista desconocida; el validador la reporta como <c>FASM3014</c>.</summary>
    Invalid,
}

/// <summary>Referencia resuelta a una unidad física, su índice y su vista.</summary>
/// <param name="Unit">Unidad referida.</param>
/// <param name="Index">Índice de <c>Gpr</c>; <c>-1</c> para las demás unidades.</param>
/// <param name="View">Vista o ventana aplicada.</param>
/// <param name="Member">Nombre del miembro cuando la vista es <see cref="RegisterView.Member"/>.</param>
/// <param name="Token">Token que describe la referencia, si se conoce.</param>
public readonly record struct RegisterReference(
    RegisterUnit Unit,
    int Index,
    RegisterView View,
    string? Member,
    Token? Token)
{
    /// <summary>Indica si la referencia corresponde a una unidad física.</summary>
    public bool IsValid => Unit != RegisterUnit.None;

    /// <summary>Indica si la referencia es un registro de propósito general.</summary>
    public bool IsGpr => Unit == RegisterUnit.Gpr;

    /// <summary>Ancho en bits de la vista referida.</summary>
    public int Bits => View switch
    {
        RegisterView.Low32 => 32,
        RegisterView.Low16 => 16,
        RegisterView.Low8 or RegisterView.High8 => 8,
        _ => 64,
    };

    /// <summary>Representación legible de la referencia.</summary>
    public string Display => Unit switch
    {
        RegisterUnit.Gpr => Member is null ? $"Gpr{Index}" : $"Gpr{Index}.{Member}",
        RegisterUnit.Frf => Member is null ? "Frf" : $"Frf.{Member}",
        RegisterUnit.Ipr => Member is null ? "Ipr" : $"Ipr.{Member}",
        RegisterUnit.Stack => "Stack",
        RegisterUnit.Cpu => "Cpu",
        _ => "<desconocido>",
    };
}

/// <summary>
/// Modelo físico de registros de Fase 1: resuelve nombres canónicos, vistas y
/// nombres legacy prohibidos según <c>FLUENT_SYNTAX_X64.md</c> §2.
/// </summary>
public static class RegisterModel
{
    private static readonly int[] InputRegisters = [1, 2, 8, 9];

    private static readonly Dictionary<string, string> LegacyNames = CreateLegacyNames();

    /// <summary>Número máximo de parámetros de entrada admitidos en Fase 1.</summary>
    public static int MaxParameters => InputRegisters.Length;

    /// <summary>
    /// Registro físico asignado a la posición de parámetro indicada
    /// (<c>Gpr1</c>, <c>Gpr2</c>, <c>Gpr8</c>, <c>Gpr9</c>).
    /// </summary>
    /// <returns>Índice del registro o <c>-1</c> si se excede el máximo.</returns>
    public static int InputRegister(int parameterIndex) =>
        parameterIndex >= 0 && parameterIndex < InputRegisters.Length
            ? InputRegisters[parameterIndex]
            : -1;

    /// <summary>Intenta resolver una expresión como unidad física con vista.</summary>
    public static bool TryResolve(ExpressionSyntax expression, out RegisterReference reference)
    {
        ArgumentNullException.ThrowIfNull(expression);

        switch (expression)
        {
            case IdentifierExpressionSyntax identifier:
                return TryResolveName(identifier.Identifier, out reference);

            case MemberAccessSyntax member when member.Receiver is IdentifierExpressionSyntax baseIdentifier:
                if (!TryResolveName(baseIdentifier.Identifier, out var baseReference))
                {
                    reference = default;
                    return false;
                }

                if (baseReference.Unit == RegisterUnit.Gpr)
                {
                    var view = TryParseView(member.Member.Text, out var parsed)
                        ? parsed
                        : RegisterView.Invalid;
                    reference = baseReference with { View = view, Member = member.Member.Text, Token = member.Member };
                    return true;
                }

                if (baseReference.Unit is RegisterUnit.Frf or RegisterUnit.Ipr)
                {
                    reference = baseReference with
                    {
                        View = RegisterView.Member,
                        Member = member.Member.Text,
                        Token = member.Member,
                    };
                    return true;
                }

                reference = default;
                return false;

            default:
                reference = default;
                return false;
        }
    }

    /// <summary>Intenta resolver un token como unidad física canónica.</summary>
    public static bool TryResolveName(Token token, out RegisterReference reference)
    {
        ArgumentNullException.ThrowIfNull(token);

        var text = token.Text;
        if (TryGetGprIndex(text, out var index))
        {
            reference = new RegisterReference(RegisterUnit.Gpr, index, RegisterView.Full, null, token);
            return true;
        }

        var unit = text switch
        {
            "Frf" => RegisterUnit.Frf,
            "Ipr" => RegisterUnit.Ipr,
            "Stack" => RegisterUnit.Stack,
            "Cpu" => RegisterUnit.Cpu,
            _ => RegisterUnit.None,
        };

        reference = unit == RegisterUnit.None
            ? default
            : new RegisterReference(unit, -1, RegisterView.Full, null, token);
        return unit != RegisterUnit.None;
    }

    /// <summary>Interpreta el sufijo de una vista de <c>Gpr</c>.</summary>
    public static bool TryParseView(string? text, out RegisterView view)
    {
        view = text switch
        {
            "low32" => RegisterView.Low32,
            "low16" => RegisterView.Low16,
            "low8" => RegisterView.Low8,
            "high8" => RegisterView.High8,
            _ => RegisterView.Invalid,
        };

        return view != RegisterView.Invalid;
    }

    /// <summary>Interpreta un nombre <c>GprN</c> y obtiene su índice.</summary>
    public static bool TryGetGprIndex(string text, out int index)
    {
        index = -1;
        if (text.Length <= 3 || !text.StartsWith("Gpr", StringComparison.Ordinal))
        {
            return false;
        }

        var digits = text.AsSpan(3);
        foreach (var character in digits)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index);
    }

    /// <summary>
    /// Obtiene el nombre canónico sugerido para un nombre legacy prohibido
    /// (<c>Rax</c>, <c>Eax</c>, <c>RIP</c>, <c>Mov</c>, <c>Ret</c>, ...).
    /// </summary>
    public static bool TryGetLegacySuggestion(string text, out string suggestion)
    {
        if (LegacyNames.TryGetValue(text, out var canonical))
        {
            suggestion = canonical;
            return true;
        }

        suggestion = string.Empty;
        return false;
    }

    private static Dictionary<string, string> CreateLegacyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Rax"] = "Gpr0",
            ["Eax"] = "Gpr0.low32",
            ["Ax"] = "Gpr0.low16",
            ["Al"] = "Gpr0.low8",
            ["Ah"] = "Gpr0.high8",
            ["Rbx"] = "Gpr3",
            ["Ebx"] = "Gpr3.low32",
            ["Bx"] = "Gpr3.low16",
            ["Bl"] = "Gpr3.low8",
            ["Bh"] = "Gpr3.high8",
            ["Rcx"] = "Gpr1",
            ["Ecx"] = "Gpr1.low32",
            ["Cx"] = "Gpr1.low16",
            ["Cl"] = "Gpr1.low8",
            ["Ch"] = "Gpr1.high8",
            ["Rdx"] = "Gpr2",
            ["Edx"] = "Gpr2.low32",
            ["Dx"] = "Gpr2.low16",
            ["Dl"] = "Gpr2.low8",
            ["Dh"] = "Gpr2.high8",
            ["Rsi"] = "Gpr6",
            ["Esi"] = "Gpr6.low32",
            ["Si"] = "Gpr6.low16",
            ["Sil"] = "Gpr6.low8",
            ["Rdi"] = "Gpr7",
            ["Edi"] = "Gpr7.low32",
            ["Di"] = "Gpr7.low16",
            ["Dil"] = "Gpr7.low8",
            ["Rbp"] = "Gpr5",
            ["Ebp"] = "Gpr5.low32",
            ["Bp"] = "Gpr5.low16",
            ["Bpl"] = "Gpr5.low8",
            ["Rsp"] = "Gpr4",
            ["Esp"] = "Gpr4.low32",
            ["Sp"] = "Gpr4.low16",
            ["Spl"] = "Gpr4.low8",
            ["RIP"] = "Ipr.current",
            ["Rip"] = "Ipr.current",
            ["RFLAGS"] = "Frf",
            ["rFlags"] = "Frf",
            ["rflags"] = "Frf",
            ["EFLAGS"] = "Frf",
            ["Eflags"] = "Frf",
            ["Mov"] = "copy",
            ["Movzx"] = "copyUnsigned",
            ["Movsx"] = "copySigned",
            ["Lea"] = "loadAddress",
            ["Xchg"] = "exchange",
            ["Jmp"] = "flowJump",
            ["Call"] = "flowCall",
            ["Ret"] = "flowStackJump",
            ["Push"] = "Stack.push",
            ["Pop"] = "pop(Stack)",
            ["Nop"] = "Cpu.noOperation",
            ["Hlt"] = "Cpu.halt",
            ["Alu"] = "destino.verbo(fuente)",
            [".alu"] = "destino.verbo(fuente)",
        };

        for (var index = 8; index <= 15; index++)
        {
            names[$"R{index}"] = $"Gpr{index}";
            names[$"R{index}d"] = $"Gpr{index}.low32";
            names[$"R{index}w"] = $"Gpr{index}.low16";
            names[$"R{index}b"] = $"Gpr{index}.low8";
        }

        foreach (var condition in new[]
                 {
                     "Je", "Jz", "Jne", "Jnz", "Jc", "Jb", "Jnc", "Jae", "Js", "Jns",
                     "Jo", "Jno", "Jp", "Jnp", "Jbe", "Ja", "Jl", "Jge", "Jle", "Jg",
                 })
        {
            names[condition] = "flowJump";
        }

        return names;
    }
}
