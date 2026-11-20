using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using CoreDiagnostics = FluentASM.Core.Diagnostics;
using CoreSemantics = FluentASM.Core.Semantics;
using CoreSyntax = FluentASM.Core.Syntax;

namespace FluentASM.Core.Catalog;

public static class X64LanguageCatalogProvider
{
    private sealed class VerbMeta
    {
        public string Description { get; init; } = string.Empty;
        public string Syntax { get; init; } = string.Empty;
        public string UsageContext { get; init; } = string.Empty;
    }

    private sealed class MagnitudeMeta
    {
        public string Description { get; init; } = string.Empty;
        public string Syntax { get; init; } = string.Empty;
        public string UsageContext { get; init; } = string.Empty;
    }

    private sealed class ContextualKeywordMeta
    {
        public string Description { get; init; } = string.Empty;
        public string Syntax { get; init; } = string.Empty;
        public string UsageContext { get; init; } = string.Empty;
    }

    private static readonly ImmutableDictionary<string, VerbMeta> VerbMetadata = new Dictionary<string, VerbMeta>
    {
        { "copy", new VerbMeta { Description = "Transferencia de datos / carga de valor (MOV)", Syntax = "target.copy(source)", UsageContext = "Registro, memoria o puerto" } },
        { "copyUnsigned", new VerbMeta { Description = "Extension con ceros (MOVZX)", Syntax = "target.copyUnsigned(source)", UsageContext = "Registro" } },
        { "copySigned", new VerbMeta { Description = "Extension con signo (MOVSX/MOVSXD)", Syntax = "target.copySigned(source)", UsageContext = "Registro" } },
        { "loadAddress", new VerbMeta { Description = "Calculo de direccion efectiva (LEA)", Syntax = "target.loadAddress(label)", UsageContext = "Registro" } },
        { "exchange", new VerbMeta { Description = "Intercambio de valores (XCHG)", Syntax = "target.exchange(source)", UsageContext = "Registro o memoria" } },
        { "conditionalCopy", new VerbMeta { Description = "Copia condicional (CMOVcc)", Syntax = "target.conditionalCopy(source if Frf.cond)", UsageContext = "Registro" } },
        { "copyBlock", new VerbMeta { Description = "Copia de bloque de memoria (REP MOVSB)", Syntax = "Byte:N [Gpr7].copyBlock(Byte:N [Gpr6])", UsageContext = "Memoria" } },
        { "fillBlock", new VerbMeta { Description = "Relleno de bloque de memoria (REP STOSB)", Syntax = "Byte:N [Gpr7].fillBlock(Gpr0.low8)", UsageContext = "Memoria" } },
        { "add", new VerbMeta { Description = "Suma aritmetica (ADD)", Syntax = "target.add(source)", UsageContext = "Registro o memoria" } },
        { "sub", new VerbMeta { Description = "Resta aritmetica (SUB)", Syntax = "target.sub(source)", UsageContext = "Registro o memoria" } },
        { "addWithCarry", new VerbMeta { Description = "Suma con acarreo (ADC)", Syntax = "target.addWithCarry(source)", UsageContext = "Registro o memoria" } },
        { "subWithBorrow", new VerbMeta { Description = "Resta con prestamo (SBB)", Syntax = "target.subWithBorrow(source)", UsageContext = "Registro o memoria" } },
        { "compare", new VerbMeta { Description = "Comparacion aritmetica (CMP)", Syntax = "target.compare(source)", UsageContext = "Registro o memoria" } },
        { "increment", new VerbMeta { Description = "Incremento (INC)", Syntax = "target.increment()", UsageContext = "Registro o memoria" } },
        { "decrement", new VerbMeta { Description = "Decremento (DEC)", Syntax = "target.decrement()", UsageContext = "Registro o memoria" } },
        { "negate", new VerbMeta { Description = "Negacion / complemento a dos (NEG)", Syntax = "target.negate()", UsageContext = "Registro o memoria" } },
        { "multiply", new VerbMeta { Description = "Multiplicacion de 64 bits (IMUL)", Syntax = "target.multiply(source)", UsageContext = "Registro o memoria" } },
        { "multiplyWide", new VerbMeta { Description = "Multiplicacion ancha 128 bits (MUL)", Syntax = "(Gpr2:Gpr0).multiplyWide(source)", UsageContext = "Par de registros" } },
        { "divide", new VerbMeta { Description = "Division sin signo (DIV)", Syntax = "(Gpr0:Gpr2).divide(source)", UsageContext = "Par de registros" } },
        { "divideSigned", new VerbMeta { Description = "Division con signo (IDIV)", Syntax = "(Gpr0:Gpr2).divideSigned(source)", UsageContext = "Par de registros" } },
        { "signExtend", new VerbMeta { Description = "Extension de signo para division (CQO)", Syntax = "(Gpr2:Gpr0).signExtend()", UsageContext = "Par de registros" } },
        { "and", new VerbMeta { Description = "AND bit a bit (AND)", Syntax = "target.and(source)", UsageContext = "Registro o memoria" } },
        { "or", new VerbMeta { Description = "OR bit a bit (OR)", Syntax = "target.or(source)", UsageContext = "Registro o memoria" } },
        { "xor", new VerbMeta { Description = "XOR bit a bit (XOR)", Syntax = "target.xor(source)", UsageContext = "Registro o memoria" } },
        { "test", new VerbMeta { Description = "Test bit a bit (TEST)", Syntax = "target.test(source)", UsageContext = "Registro o memoria" } },
        { "not", new VerbMeta { Description = "Inversion bit a bit (NOT)", Syntax = "target.not()", UsageContext = "Registro o memoria" } },
        { "shiftLeft", new VerbMeta { Description = "Desplazamiento izquierdo (SHL/SAL)", Syntax = "target.shiftLeft(Bit:N)", UsageContext = "Registro o memoria" } },
        { "shiftRightLogical", new VerbMeta { Description = "Desplazamiento derecho logico (SHR)", Syntax = "target.shiftRightLogical(Bit:N)", UsageContext = "Registro o memoria" } },
        { "shiftRightArithmetic", new VerbMeta { Description = "Desplazamiento derecho aritmetico (SAR)", Syntax = "target.shiftRightArithmetic(Bit:N)", UsageContext = "Registro o memoria" } },
        { "rotateLeft", new VerbMeta { Description = "Rotacion izquierda (ROL)", Syntax = "target.rotateLeft(Bit:N)", UsageContext = "Registro o memoria" } },
        { "rotateRight", new VerbMeta { Description = "Rotacion derecha (ROR)", Syntax = "target.rotateRight(Bit:N)", UsageContext = "Registro o memoria" } },
        { "testBit", new VerbMeta { Description = "Test de bit (BT)", Syntax = "target.testBit(Bit:N)", UsageContext = "Registro o memoria" } },
        { "setBit", new VerbMeta { Description = "Puesta de bit (BTS)", Syntax = "target.setBit(Bit:N)", UsageContext = "Registro o memoria" } },
        { "clearBit", new VerbMeta { Description = "Limpieza de bit (BTR)", Syntax = "target.clearBit(Bit:N)", UsageContext = "Registro o memoria" } },
        { "toggleBit", new VerbMeta { Description = "Inversion de bit (BTC)", Syntax = "target.toggleBit(Bit:N)", UsageContext = "Registro o memoria" } },
        { "push", new VerbMeta { Description = "Push a pila (PUSH)", Syntax = "Stack.push(operand)", UsageContext = "Pila" } },
        { "pop", new VerbMeta { Description = "Pop de pila (POP)", Syntax = "target.pop(Stack) / Frf.pop(Stack)", UsageContext = "Registro o Frf" } },
        { "reserve", new VerbMeta { Description = "Reserva espacio en pila (SUB RSP)", Syntax = "Stack.reserve(Byte:N)", UsageContext = "Pila" } },
        { "release", new VerbMeta { Description = "Libera espacio en pila (ADD RSP)", Syntax = "Stack.release(Byte:N)", UsageContext = "Pila" } },
        { "flowJump", new VerbMeta { Description = "Salto de flujo (JMP/Jcc)", Syntax = "Ipr.flowJump(@label [if Frf.cond])", UsageContext = "Ipr" } },
        { "flowCall", new VerbMeta { Description = "Llamada a rutina (CALL)", Syntax = "Ipr.flowCall(c -> c.target(...).inputs(...).output(...)) / Ipr.flowCall(routine)", UsageContext = "Ipr" } },
        { "flowStackJump", new VerbMeta { Description = "Retorno de rutina (RET)", Syntax = "Ipr.flowStackJump()", UsageContext = "Ipr" } },
        { "flowLoop", new VerbMeta { Description = "Bucle con contador (LOOP)", Syntax = "Ipr.flowLoop(@label)", UsageContext = "Ipr" } },
        { "noOperation", new VerbMeta { Description = "Operacion nula (NOP)", Syntax = "Cpu.noOperation()", UsageContext = "Cpu" } },
        { "pause", new VerbMeta { Description = "Pausa eficiente (PAUSE/F3 90)", Syntax = "Cpu.pause()", UsageContext = "Cpu" } },
        { "halt", new VerbMeta { Description = "Detencion de CPU (HLT)", Syntax = "Cpu.halt()", UsageContext = "Cpu" } },
        { "carry", new VerbMeta { Description = "Manipulacion de Frf.carry (STC/CLC/CMC)", Syntax = "Frf.carry(1 | 0 | not)", UsageContext = "Frf" } },
        { "parity", new VerbMeta { Description = "Manipulacion de Frf.parity", Syntax = "Frf.parity(1 | 0 | not)", UsageContext = "Frf" } },
        { "adjust", new VerbMeta { Description = "Manipulacion de Frf.adjust", Syntax = "Frf.adjust(1 | 0 | not)", UsageContext = "Frf" } },
        { "zero", new VerbMeta { Description = "Manipulacion de Frf.zero", Syntax = "Frf.zero(1 | 0 | not)", UsageContext = "Frf" } },
        { "sign", new VerbMeta { Description = "Manipulacion de Frf.sign", Syntax = "Frf.sign(1 | 0 | not)", UsageContext = "Frf" } },
        { "interrupt", new VerbMeta { Description = "Manipulacion de Frf.interrupt (CLI/STI)", Syntax = "Frf.interrupt(1 | 0)", UsageContext = "Frf" } },
        { "direction", new VerbMeta { Description = "Manipulacion de Frf.direction (CLD/STD)", Syntax = "Frf.direction(1 | 0)", UsageContext = "Frf" } },
        { "overflow", new VerbMeta { Description = "Manipulacion de Frf.overflow", Syntax = "Frf.overflow(1 | 0 | not)", UsageContext = "Frf" } },
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, MagnitudeMeta> MagnitudeMetadata = new Dictionary<string, MagnitudeMeta>
    {
        { "Bit", new MagnitudeMeta { Description = "Unidad de medida en bits (1 Bit = 1 bit)", Syntax = "Bit:N", UsageContext = "Anchos de registros, flags, desplazamientos" } },
        { "Byte", new MagnitudeMeta { Description = "Unidad de medida en bytes (1 Byte = 8 Bit)", Syntax = "Byte:N", UsageContext = "Memoria, reservas de pila, alineaciones" } },
        { "KiloBit", new MagnitudeMeta { Description = "1024 Bit", Syntax = "KiloBit:N", UsageContext = "Capacidades de memoria" } },
        { "MegaBit", new MagnitudeMeta { Description = "1024 KiloBit", Syntax = "MegaBit:N", UsageContext = "Capacidades de memoria" } },
        { "GigaBit", new MagnitudeMeta { Description = "1024 MegaBit", Syntax = "GigaBit:N", UsageContext = "Capacidades de memoria" } },
        { "TeraBit", new MagnitudeMeta { Description = "1024 GigaBit", Syntax = "TeraBit:N", UsageContext = "Capacidades de memoria" } },
        { "KiloByte", new MagnitudeMeta { Description = "1024 Byte", Syntax = "KiloByte:N", UsageContext = "Capacidades de memoria" } },
        { "MegaByte", new MagnitudeMeta { Description = "1024 KiloByte", Syntax = "MegaByte:N", UsageContext = "Capacidades de memoria" } },
        { "GigaByte", new MagnitudeMeta { Description = "1024 MegaByte", Syntax = "GigaByte:N", UsageContext = "Capacidades de memoria" } },
        { "TeraByte", new MagnitudeMeta { Description = "1024 GigaByte", Syntax = "TeraByte:N", UsageContext = "Capacidades de memoria" } },
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, ContextualKeywordMeta> ContextualKeywordMetadata = new Dictionary<string, ContextualKeywordMeta>
    {
        { "defaultValue", new ContextualKeywordMeta { Description = "Estado base limpio (todos los bits en cero)", Syntax = "Gpr0.copy(defaultValue)", UsageContext = "Emite la forma fisica mas corta para poner el contenedor en cero sin literal duro." } },
        { "if", new ContextualKeywordMeta { Description = "Condiciona un salto al estado de una bandera", Syntax = "Ipr.flowJump(@etiqueta if Frf.condicion)", UsageContext = "Usado dentro de flowJump para saltos condicionales." } },
        { "not", new ContextualKeywordMeta { Description = "Invierte una condicion de bandera o ejecuta NOT bit a bit", Syntax = "Ipr.flowJump(@etiqueta if not Frf.carry) / receptor.not()", UsageContext = "Prefijo en condiciones; verbo sin receptor para NOT bit a bit." } },
        { "and", new ContextualKeywordMeta { Description = "AND entre banderas (condicion) o bit a bit (verbo)", Syntax = "Frf.cond1 and Frf.cond2 / receptor.and(fuente)", UsageContext = "Operador binario en condiciones; verbo logico en instrucciones." } },
        { "or", new ContextualKeywordMeta { Description = "OR entre banderas (condicion) o bit a bit (verbo)", Syntax = "Frf.cond1 or Frf.cond2 / receptor.or(fuente)", UsageContext = "Operador binario en condiciones; verbo logico en instrucciones." } },
        { "equal", new ContextualKeywordMeta { Description = "Alias legible para Frf.zero (ZF = 1)", Syntax = "Ipr.flowJump(@etiqueta if Frf.equal)", UsageContext = "Equivalente a Frf.zero." } },
        { "unequal", new ContextualKeywordMeta { Description = "Alias legible para not Frf.zero (ZF = 0)", Syntax = "Ipr.flowJump(@etiqueta if Frf.unequal)", UsageContext = "Equivalente a not Frf.zero." } },
        { "Port", new ContextualKeywordMeta { Description = "Unidad de E/S hardware de 16 bits", Syntax = "Port[0x03F8] / Port[Gpr2.low16]", UsageContext = "Acceso a puertos de hardware; puertos > 0xFF usan Gpr2.low16 (DX)." } },
        { "Bit", new ContextualKeywordMeta { Description = "Calificador de magnitud en bits", Syntax = "Bit:3 / Bit:64", UsageContext = "Ancho de bits para inmediatos, desplazamientos, indices de bit." } },
        { "Byte", new ContextualKeywordMeta { Description = "Calificador de magnitud en bytes", Syntax = "Byte:8 / Byte:16", UsageContext = "Extension en bytes para memoria, pila, alineaciones." } },
    }.ToImmutableDictionary(StringComparer.Ordinal);

    public static ImmutableArray<GprDescriptor> GetGprDescriptors()
    {
        var array = ImmutableArray.CreateBuilder<GprDescriptor>(16);
        for (int i = 0; i <= 15; i++)
        {
            var views = ImmutableArray.CreateBuilder<RegisterViewDescriptor>();
            views.Add(RegisterViewDescriptor.Full);
            views.Add(RegisterViewDescriptor.Low32);
            views.Add(RegisterViewDescriptor.Low16);
            views.Add(RegisterViewDescriptor.Low8);
            if (i <= 3) { views.Add(RegisterViewDescriptor.High8); }
            array.Add(new GprDescriptor($"Gpr{i}", i, views.ToImmutable(),
                i == 0 || i == 1 || i == 2 || (i >= 8 && i <= 11),
                GetGprRole(i), GetGprHardwareRef(i)));
        }
        return array.ToImmutable();
    }

    public static ImmutableArray<UnitDescriptor> GetUnitDescriptors() => ImmutableArray.Create<UnitDescriptor>(
        new("Frf", "Registro de banderas (RFLAGS)", CoreSemantics.RegisterUnit.Frf, ImmutableArray<RegisterViewDescriptor>.Empty,
            ImmutableArray.Create<string>("carry", "parity", "adjust", "zero", "sign", "interrupt", "direction", "overflow")),
        new("Ipr", "Puntero de instruccion (RIP)", CoreSemantics.RegisterUnit.Ipr, ImmutableArray<RegisterViewDescriptor>.Empty,
            ImmutableArray.Create<string>("current")),
        new("Stack", "Pila sobre Gpr4 (RSP)", CoreSemantics.RegisterUnit.Stack, ImmutableArray<RegisterViewDescriptor>.Empty,
            ImmutableArray.Create<string>("push", "pop", "reserve", "release")),
        new("Cpu", "Control de maquina", CoreSemantics.RegisterUnit.Cpu, ImmutableArray<RegisterViewDescriptor>.Empty,
            ImmutableArray.Create<string>("noOperation", "pause", "halt")),
        new("Port", "Espacio de E/S de 16 bits", CoreSemantics.RegisterUnit.None, ImmutableArray<RegisterViewDescriptor>.Empty,
            ImmutableArray.Create<string>("copy")));

    public static ImmutableDictionary<string, string> GetLegacyNames()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in CoreSemantics.RegisterModel.LegacyNames) { dict[kvp.Key] = kvp.Value; }
        return dict.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public static ImmutableArray<FlagDescriptor> GetFlagDescriptors() => CoreSemantics.FlagCatalog.Names
        .Select(f => new FlagDescriptor(f.Name, f.Flag, GetFlagBit(f.Flag), GetPhysicalMeaning(f.Name), GetWhoUpdates(f.Name)))
        .ToImmutableArray();

    public static ImmutableArray<FlagAliasDescriptor> GetFlagAliasDescriptors() => CoreSemantics.FlagCatalog.Aliases
        .Where(a => a.Key != "carry" && a.Key != "zero" && a.Key != "sign" && a.Key != "overflow" && a.Key != "parity")
        .Select(a => new FlagAliasDescriptor(a.Key, GetCanonicalForm(a.Key, a.Value), GetBitsEvaluated(a.Value)))
        .ToImmutableArray();

    public static ImmutableArray<VerbDescriptor> GetVerbDescriptors() => CoreSemantics.VerbCatalog.Definitions
        .Select(kvp =>
        {
            var verb = kvp.Value;
            var meta = VerbMetadata.TryGetValue(kvp.Key, out var m) ? m : new VerbMeta { Description = string.Empty, Syntax = string.Empty, UsageContext = string.Empty };
            var receivers = GetReceiverNames(verb.Receivers);
            return new VerbDescriptor(verb.Name, meta.Description, receivers.ToImmutableArray(),
                verb.MinArguments, verb.MaxArguments, meta.Description, ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty, "void", GetFlagsAffected(verb.Flags));
        }).OrderBy(v => v.Name).ToImmutableArray();

    public static ImmutableArray<MagnitudeDescriptor> GetMagnitudeDescriptors() => Enum.GetValues<CoreSyntax.MagnitudeKind>()
        .Cast<CoreSyntax.MagnitudeKind>().Where(k => k != CoreSyntax.MagnitudeKind.None)
        .Select(k => { var name = k.ToString(); var meta = MagnitudeMetadata.TryGetValue(name, out var m) ? m : new MagnitudeMeta { Description = string.Empty, Syntax = string.Empty, UsageContext = string.Empty }; return new MagnitudeDescriptor(name, meta.Description, $"{name}:N", meta.UsageContext); })
        .ToImmutableArray();

    public static ImmutableArray<FlowConstructorDescriptor> GetFlowConstructorDescriptors()
    {
        return new[]
        {
            new FlowConstructorDescriptor(".data", "Declaracion de datos opcional", "Proyecto.data(dt -> { dt.define<Byte:N>(d -> d.id(nombre).value(...)) })", "Declara simbolos de datos con capacidad Byte:N y valor inicial (entero, cadena o defaultValue)."),
            new FlowConstructorDescriptor(".execute", "Seccion ejecutable obligatoria", "Proyecto.execute(ex -> { ex.routines(rt -> { rt.define(d -> { d.id(nombre).returns(...).run(...) }) }) })", "Contiene las rutinas ejecutables del programa. Debe contener al menos una rutina con d.id(main) o _."),
            new FlowConstructorDescriptor(".initialize", "Terminal obligatorio", "Proyecto.data(...).execute(...).initialize()", "Cierra el contrato del programa. Despues de .initialize() no se puede encadenar mas."),
        }.ToImmutableArray();
    }

    public static ImmutableArray<DirectiveDescriptor> GetDirectiveDescriptors()
    {
        return new[]
        {
            new DirectiveDescriptor("define", "define", ".data", "dt.define<Byte:N>(d -> d.id(nombre).value(valor))", "Declara un simbolo de datos con capacidad Byte:N y valor inicial."),
            new DirectiveDescriptor("id", "id", ".execute -> routines -> define", "d.id(nombreRutina)", "Identificador unico de la rutina."),
            new DirectiveDescriptor("returns", "returns", ".execute -> routines -> define", "d.returns(GprN)", "Declara el registro de retorno de la rutina (o se omite si no devuelve valor)."),
            new DirectiveDescriptor("run", "run", ".execute -> routines -> define", "d.run(() -> { ... }) o d.run((arg1<Byte:N>, ...) -> { ... })", "Bloque de instrucciones. Argumentos son alias directos sobre registros fisicos de entrada."),
            new DirectiveDescriptor("value", "value", ".data -> define", "d.value(10) / d.value(\"texto\") / d.value(defaultValue)", "Valor inicial del simbolo (entero, cadena o defaultValue)."),
            new DirectiveDescriptor("target", "target", ".execute -> flowCall", "c.target(nombreRutina)", "Identifica la rutina destino de la llamada."),
            new DirectiveDescriptor("inputs", "inputs", ".execute -> flowCall", "c.inputs(Gpr1, Gpr2, ...)", "Lista homogenea de registros de entrada para la llamada."),
            new DirectiveDescriptor("output", "output", ".execute -> flowCall", "c.output(Gpr0)", "Registro de salida que debe coincidir con d.returns de la rutina destino."),
            new DirectiveDescriptor("if", "if", "flowJump", "Ipr.flowJump(@etiqueta if Frf.condicion)", "Condiciona el salto al estado de una bandera de Frf (con prefijo opcional 'not')."),
            new DirectiveDescriptor("not", "not", "Condicion / verbo not", "not Frf.bandera / not operando", "Invierte la condicion de bandera o ejecuta el verbo NOT bit a bit."),
            new DirectiveDescriptor("and", "and", "Condicion / verbo and", "Frf.cond1 and Frf.cond2 / receptor.and(fuente)", "Operacion AND entre banderas (condicion) o bit a bit (verbo)."),
            new DirectiveDescriptor("or", "or", "Condicion / verbo or", "Frf.cond1 or Frf.cond2 / receptor.or(fuente)", "Operacion OR entre banderas (condicion) o bit a bit (verbo)."),
            new DirectiveDescriptor("equal", "equal", "Condicion", "Frf.equal (alias de Frf.zero)", "Alias legible para Frf.zero (ZF = 1)."),
            new DirectiveDescriptor("unequal", "unequal", "Condicion", "Frf.unequal (alias de not Frf.zero)", "Alias legible para not Frf.zero (ZF = 0)."),
        }.ToImmutableArray();
    }

    public static ImmutableArray<ContextualKeywordDescriptor> GetContextualKeywordDescriptors()
    {
        return new Dictionary<string, ContextualKeywordMeta>
        {
            { "defaultValue", new ContextualKeywordMeta { Description = "Estado base limpio (todos los bits en cero)", Syntax = "Gpr0.copy(defaultValue)", UsageContext = "Emite la forma fisica mas corta para poner el contenedor en cero sin literal duro." } },
            { "if", new ContextualKeywordMeta { Description = "Condiciona un salto al estado de una bandera", Syntax = "Ipr.flowJump(@etiqueta if Frf.condicion)", UsageContext = "Usado dentro de flowJump para saltos condicionales." } },
            { "not", new ContextualKeywordMeta { Description = "Invierte una condicion de bandera o ejecuta NOT bit a bit", Syntax = "Ipr.flowJump(@etiqueta if not Frf.carry) / receptor.not()", UsageContext = "Prefijo en condiciones; verbo sin receptor para NOT bit a bit." } },
            { "and", new ContextualKeywordMeta { Description = "AND entre banderas (condicion) o bit a bit (verbo)", Syntax = "Frf.cond1 and Frf.cond2 / receptor.and(fuente)", UsageContext = "Operador binario en condiciones; verbo logico en instrucciones." } },
            { "or", new ContextualKeywordMeta { Description = "OR entre banderas (condicion) o bit a bit (verbo)", Syntax = "Frf.cond1 or Frf.cond2 / receptor.or(fuente)", UsageContext = "Operador binario en condiciones; verbo logico en instrucciones." } },
            { "equal", new ContextualKeywordMeta { Description = "Alias legible para Frf.zero (ZF = 1)", Syntax = "Ipr.flowJump(@etiqueta if Frf.equal)", UsageContext = "Equivalente a Frf.zero." } },
            { "unequal", new ContextualKeywordMeta { Description = "Alias legible para not Frf.zero (ZF = 0)", Syntax = "Ipr.flowJump(@etiqueta if Frf.unequal)", UsageContext = "Equivalente a not Frf.zero." } },
            { "Port", new ContextualKeywordMeta { Description = "Unidad de E/S hardware de 16 bits", Syntax = "Port[0x03F8] / Port[Gpr2.low16]", UsageContext = "Acceso a puertos de hardware; puertos > 0xFF usan Gpr2.low16 (DX)." } },
            { "Bit", new ContextualKeywordMeta { Description = "Calificador de magnitud en bits", Syntax = "Bit:3 / Bit:64", UsageContext = "Ancho de bits para inmediatos, desplazamientos, indices de bit." } },
            { "Byte", new ContextualKeywordMeta { Description = "Calificador de magnitud en bytes", Syntax = "Byte:8 / Byte:16", UsageContext = "Extension en bytes para memoria, pila, alineaciones." } },
        }.Select(kvp => new ContextualKeywordDescriptor(kvp.Key, kvp.Value.Description, kvp.Value.Syntax, kvp.Value.UsageContext))
            .ToImmutableArray();
    }

    public static ImmutableArray<DiagnosticDescriptor> GetDiagnosticDescriptors()
    {
        var fields = typeof(FluentASM.Core.Diagnostics.DiagnosticIds).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => f.GetValue(null) as string)
            .Where(s => !string.IsNullOrEmpty(s))
            .OrderBy(s => s).ToArray();
        return fields.Select(code => { var c = code ?? string.Empty; var (id, message, severity, category) = GetDiagnosticMetadata(c); return new DiagnosticDescriptor(c, id, message, CoreDiagnostics.DiagnosticSeverity.Error, category); }).ToImmutableArray();
    }

    private static ImmutableArray<string> GetReceiverNames(CoreSemantics.ReceiverShape shape)
    {
        var names = new List<string>();
        if ((shape & CoreSemantics.ReceiverShape.Register) != 0) { names.Add("Register"); }
        if ((shape & CoreSemantics.ReceiverShape.Memory) != 0) { names.Add("Memory"); }
        if ((shape & CoreSemantics.ReceiverShape.Port) != 0) { names.Add("Port"); }
        if ((shape & CoreSemantics.ReceiverShape.Stack) != 0) { names.Add("Stack"); }
        if ((shape & CoreSemantics.ReceiverShape.Frf) != 0) { names.Add("Frf"); }
        if ((shape & CoreSemantics.ReceiverShape.Ipr) != 0) { names.Add("Ipr"); }
        if ((shape & CoreSemantics.ReceiverShape.Cpu) != 0) { names.Add("Cpu"); }
        if ((shape & CoreSemantics.ReceiverShape.Pair) != 0) { names.Add("Pair"); }
        return names.Count == 0 ? ImmutableArray<string>.Empty : names.ToImmutableArray();
    }

    private static string GetFlagsAffected(CoreSemantics.FlagEffect effect)
    {
        if (effect.Defined == CoreSemantics.FlagSet.None && effect.Undefined == CoreSemantics.FlagSet.None) { return "Ninguna"; }
        var parts = new List<string>();
        foreach (var (flag, name) in CoreSemantics.FlagCatalog.Names)
        {
            if ((effect.Defined & flag) != 0) { parts.Add($"Frf.{name} (definida)"); }
            if ((effect.Undefined & flag) != 0) { parts.Add($"Frf.{name} (indefinida)"); }
        }
        return parts.Count == 0 ? "Ninguna" : string.Join(", ", parts);
    }

    private static string GetGprRole(int index) => index switch
    {
        0 => "Retorno principal; acumulador en multiplyWide/divide; volatil",
        1 => "Entrada 1; contador en flowLoop/copyBlock/shift variable (.low8); volatil",
        2 => "Entrada 2; parte alta en multiplyWide y resto en divide; puerto en Port[Gpr2.low16]; volatil",
        3 => "Preservado por el llamado (callee-saved)",
        4 => "Puntero de pila (Stack); no usable como indice SIB",
        5 => "Puntero de marco; preservado (callee-saved)",
        6 => "Puntero origen en copyBlock; preservado (callee-saved)",
        7 => "Puntero destino en copyBlock/fillBlock; preservado (callee-saved)",
        8 => "Entrada 3; volatil",
        9 => "Entrada 4; volatil",
        10 => "Volatil",
        11 => "Volatil",
        _ => "Preservado (callee-saved)",
    };

    private static string GetGprHardwareRef(int index) => index switch
    {
        0 => "RAX", 1 => "RCX", 2 => "RDX", 3 => "RBX", 4 => "RSP", 5 => "RBP",
        6 => "RSI", 7 => "RDI", 8 => "R8", 9 => "R9", 10 => "R10", 11 => "R11",
        12 => "R12", 13 => "R13", 14 => "R14", 15 => "R15", _ => "",
    };

    private static int GetFlagBit(CoreSemantics.FlagSet flag) => flag switch
    {
        CoreSemantics.FlagSet.Carry => 0, CoreSemantics.FlagSet.Parity => 2, CoreSemantics.FlagSet.Adjust => 4,
        CoreSemantics.FlagSet.Zero => 6, CoreSemantics.FlagSet.Sign => 7, CoreSemantics.FlagSet.Interrupt => 9,
        CoreSemantics.FlagSet.Direction => 10, CoreSemantics.FlagSet.Overflow => 11, _ => -1,
    };

    private static string GetPhysicalMeaning(string name) => name switch
    {
        "carry" => "Acarreo / prestamo sin signo (CF)", "parity" => "Paridad par del byte bajo (PF)",
        "adjust" => "Acarreo auxiliar BCD (AF)", "zero" => "Resultado igual a cero (ZF)",
        "sign" => "Bit mas significativo = 1 (SF)", "interrupt" => "Habilitacion de interrupciones (IF)",
        "direction" => "Direccion de bloques (DF: 0=adelante, 1=atras)", "overflow" => "Desbordamiento con signo (OF)",
        _ => "",
    };

    private static string GetWhoUpdates(string name) => name switch
    {
        "carry" => "add, sub, compare, shift*, rotate*, *Bit, Frf.carry(...)",
        "parity" => "Operaciones ALU estandar", "adjust" => "add, sub, compare",
        "zero" => "Casi toda la ALU, compare, test", "sign" => "Casi toda la ALU, compare, test",
        "interrupt" => "Frf.interrupt(1 | 0)", "direction" => "Frf.direction(1 | 0)",
        "overflow" => "add, sub, compare, multiply*", _ => "",
    };

    private static string GetCanonicalForm(string alias, CoreSemantics.FlagSet flags)
    {
        var names = CoreSemantics.FlagCatalog.Names.Where(f => (flags & f.Flag) != 0).Select(f => $"Frf.{f.Name}");
        return string.Join(" / ", names);
    }

    private static string GetBitsEvaluated(CoreSemantics.FlagSet flags)
    {
        var parts = new List<string>();
        foreach (var (flag, name) in CoreSemantics.FlagCatalog.Names)
        {
            if ((flags & flag) != 0) { parts.Add(name switch { "carry" => "CF = 1", "zero" => "ZF = 1", "sign" => "SF = 1", "overflow" => "OF = 1", "parity" => "PF = 1", "direction" => "DF = 1", "interrupt" => "IF = 1", "adjust" => "AF = 1", _ => name }); }
        }
        return parts.Count == 0 ? "" : string.Join(" ", parts);
    }

    private static (string Id, string Message, CoreDiagnostics.DiagnosticSeverity Severity, string Category) GetDiagnosticMetadata(string code) => code switch
    {
        var c when c.StartsWith("FASM1") => (code, "", CoreDiagnostics.DiagnosticSeverity.Error, "Lexico"),
        var c when c.StartsWith("FASM2") => (code, "", CoreDiagnostics.DiagnosticSeverity.Error, "Proyecto/Datos"),
        var c when c.StartsWith("FASM3") => (code, "", CoreDiagnostics.DiagnosticSeverity.Error, "ATM"),
        var c when c.StartsWith("FASM4") => (code, "", CoreDiagnostics.DiagnosticSeverity.Error, "Operandos"),
        var c when c.StartsWith("FASM5") => (code, "", CoreDiagnostics.DiagnosticSeverity.Error, "Contratos"),
        _ => (code, "", CoreDiagnostics.DiagnosticSeverity.Error, "Desconocido"),
    };
}