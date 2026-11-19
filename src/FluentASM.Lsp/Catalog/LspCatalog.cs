using FluentASM.Lsp.Protocol;

namespace FluentASM.Lsp.Catalog;

/// <summary>
/// Catálogo centralizado de metadatos, firmas y documentación técnica de FluentASM
/// para autocompletado inteligente y tarjetas Stitch en el IDE.
/// </summary>
public static class LspCatalog
{
    private static readonly List<LspCompletionItem> AllItems = [];
    private static readonly Dictionary<string, LspCompletionItem> ItemsByLookup = new(StringComparer.OrdinalIgnoreCase);

    static LspCatalog()
    {
        InitializeRegisters();
        InitializeVerbs();
        InitializeFlowDirectives();
        InitializeDirectives();
    }

    public static IReadOnlyList<LspCompletionItem> GetAllItems() => AllItems;

    public static bool TryGetItem(string name, out LspCompletionItem item) =>
        ItemsByLookup.TryGetValue(name, out item!);

    private static void AddItem(LspCompletionItem item)
    {
        AllItems.Add(item);
        ItemsByLookup[item.Label] = item;
        if (!string.IsNullOrEmpty(item.InsertText) && !ItemsByLookup.ContainsKey(item.InsertText))
        {
            ItemsByLookup[item.InsertText] = item;
        }
    }

    private static void InitializeRegisters()
    {
        // Gpr0 - Gpr15
        for (int i = 0; i <= 15; i++)
        {
            string name = $"Gpr{i}";
            string role = i switch
            {
                0 => "Acumulador principal (rax / retorno de 64 bits)",
                1 => "Primer argumento / registro de propósito general (rcx)",
                2 => "Segundo argumento / registro de datos (rdx)",
                3 => "Registro base / propósito general (rbx)",
                4 => "Puntero de pila (rsp - uso protegido)",
                5 => "Puntero de marco base (rbp)",
                6 => "Índice fuente (rsi)",
                7 => "Índice destino (rdi)",
                _ => $"Registro extendido de 64 bits r{i}"
            };

            AddItem(new LspCompletionItem(
                Label: name,
                Kind: CompletionItemKind.Variable,
                Detail: $"Registro x64 de 64 bits [Gpr{i}]",
                Documentation: MarkupContent.Markdown($"### `{name}` (Registro General x64)\n\n{role}.\n\n- **Tamaño:** 64 bits (QWord)\n- **Sub-vistas:** `{name}.low32`, `{name}.low16`, `{name}.low8`"),
                InsertText: name,
                FilterText: name,
                SortText: $"01_{name}",
                Metadata: new FluentItemMetadata(
                    Category: "Registers",
                    Signature: $"{name}: QWord",
                    Description: role,
                    Inputs: "Ninguno",
                    Modifies: $"{name} (64 bits)",
                    Returns: "Celda de registro",
                    FlagsAffected: "Ninguna")));

            // Subviews for Gpr
            AddItem(new LspCompletionItem(
                Label: $"{name}.low32",
                Kind: CompletionItemKind.Field,
                Detail: $"Vista de 32 bits bajos de {name}",
                Documentation: MarkupContent.Markdown($"Vista de 32 bits inferiores de `{name}` (equivale a `eax`, `ecx`, etc.).\n\nAl escribir en esta vista se pone a cero la mitad superior de 64 bits."),
                InsertText: $"{name}.low32",
                FilterText: $"{name}.low32",
                SortText: $"02_{name}_low32",
                Metadata: new FluentItemMetadata(
                    Category: "Registers",
                    Signature: $"{name}.low32: DWord",
                    Description: $"Ventana de 32 bits bajos de {name}. La escritura limpia los bits 32-63.",
                    Inputs: $"{name}",
                    Modifies: $"{name} (32 bits bajos)",
                    Returns: "Vista DWord",
                    FlagsAffected: "Ninguna")));

            AddItem(new LspCompletionItem(
                Label: $"{name}.low8",
                Kind: CompletionItemKind.Field,
                Detail: $"Vista de 8 bits bajos de {name}",
                Documentation: MarkupContent.Markdown($"Vista del byte inferior de `{name}` (equivale a `al`, `cl`, `dl`, `bl`, `r8b`, etc.)."),
                InsertText: $"{name}.low8",
                FilterText: $"{name}.low8",
                SortText: $"03_{name}_low8",
                Metadata: new FluentItemMetadata(
                    Category: "Registers",
                    Signature: $"{name}.low8: Byte",
                    Description: $"Ventana de 8 bits inferiores (byte bajo) de {name}.",
                    Inputs: $"{name}",
                    Modifies: $"{name}[0..7]",
                    Returns: "Vista Byte",
                    FlagsAffected: "Ninguna")));
        }

        // Flags & Special
        AddItem(new LspCompletionItem(
            Label: "rFlags.zero",
            Kind: CompletionItemKind.Field,
            Detail: "Bandera Zero (ZF) en registro de banderas",
            Documentation: MarkupContent.Markdown("Indica si el resultado de la última operación de ALU fue cero."),
            InsertText: "rFlags.zero",
            SortText: "04_rflags_zero",
            Metadata: new FluentItemMetadata(
                Category: "Registers",
                Signature: "rFlags.zero: Bit",
                Description: "Bandera Zero (ZF). Evaluada en saltos condicionales (`if rFlags.zero` / `if not rFlags.zero`).",
                Inputs: "ALU status",
                Modifies: "Lectura",
                Returns: "Bit (0 o 1)",
                FlagsAffected: "Lectura")));

        AddItem(new LspCompletionItem(
            Label: "rFlags.carry",
            Kind: CompletionItemKind.Field,
            Detail: "Bandera Carry (CF)",
            Documentation: MarkupContent.Markdown("Indica acarreo sin signo en la última operación aritmética."),
            InsertText: "rFlags.carry",
            SortText: "04_rflags_carry",
            Metadata: new FluentItemMetadata(
                Category: "Registers",
                Signature: "rFlags.carry: Bit",
                Description: "Bandera Carry (CF). Acarreo en operaciones aritméticas sin signo.",
                Inputs: "ALU status",
                Modifies: "Lectura",
                Returns: "Bit (0 o 1)",
                FlagsAffected: "Lectura")));
    }

    private static void InitializeVerbs()
    {
        AddItem(new LspCompletionItem(
            Label: "imult<2,0>(op)",
            Kind: CompletionItemKind.Method,
            Detail: "Multiplicación entera con receptor ampliado (imul)",
            Documentation: MarkupContent.Markdown("Multiplica el registro destino por el operando, ampliando el resultado en el par de registros especificado (ej. `Gpr2:Gpr0`)."),
            InsertText: "imult<2,0>($0)",
            FilterText: "imult",
            SortText: "10_imult",
            Metadata: new FluentItemMetadata(
                Category: "Verbs",
                Signature: "Gpr0.imult<2,0>(Gpr1)",
                Description: "Multiplica el valor del registro destino por el operando, calculando producto firmado completo.",
                Inputs: "Gpr0 (acumulador), Gpr1 (multiplicando)",
                Modifies: "Gpr0 (resultado bajo), Gpr2 (resultado alto truncado)",
                Returns: "void",
                FlagsAffected: "Frf.Carry, Frf.Overflow")));

        AddItem(new LspCompletionItem(
            Label: "copy(src)",
            Kind: CompletionItemKind.Method,
            Detail: "Transferencia de datos / carga de valor (mov)",
            Documentation: MarkupContent.Markdown("Copia el valor del operando fuente en la ubicación receptora sin alterar banderas."),
            InsertText: "copy($0)",
            FilterText: "copy",
            SortText: "10_copy",
            Metadata: new FluentItemMetadata(
                Category: "Verbs",
                Signature: "target.copy(source)",
                Description: "Transfiere datos de la fuente al destino sin modificar banderas del procesador.",
                Inputs: "source (inmediato, registro o memoria)",
                Modifies: "target",
                Returns: "void",
                FlagsAffected: "Ninguna")));

        AddItem(new LspCompletionItem(
            Label: "sub(src)",
            Kind: CompletionItemKind.Method,
            Detail: "Resta aritmética (sub)",
            Documentation: MarkupContent.Markdown("Resta el operando de la fuente del receptor y actualiza las banderas de estado (ZF, SF, CF, OF)."),
            InsertText: "sub($0)",
            FilterText: "sub",
            SortText: "10_sub",
            Metadata: new FluentItemMetadata(
                Category: "Verbs",
                Signature: "target.sub(value)",
                Description: "Resta aritmética de enteros. Actualiza banderas según el resultado.",
                Inputs: "value (inmediato o registro)",
                Modifies: "target = target - value",
                Returns: "void",
                FlagsAffected: "Frf.Zero, Frf.Sign, Frf.Carry, Frf.Overflow")));

        AddItem(new LspCompletionItem(
            Label: "add(src)",
            Kind: CompletionItemKind.Method,
            Detail: "Suma aritmética (add)",
            Documentation: MarkupContent.Markdown("Suma el operando de la fuente al receptor y actualiza las banderas de la ALU."),
            InsertText: "add($0)",
            FilterText: "add",
            SortText: "10_add",
            Metadata: new FluentItemMetadata(
                Category: "Verbs",
                Signature: "target.add(value)",
                Description: "Suma aritmética de enteros.",
                Inputs: "value (inmediato o registro)",
                Modifies: "target = target + value",
                Returns: "void",
                FlagsAffected: "Frf.Zero, Frf.Sign, Frf.Carry, Frf.Overflow")));

        AddItem(new LspCompletionItem(
            Label: "cmp(src)",
            Kind: CompletionItemKind.Method,
            Detail: "Comparación aritmética (cmp)",
            Documentation: MarkupContent.Markdown("Resta conceptualmente el operando del receptor para actualizar las banderas sin modificar el valor de los registros."),
            InsertText: "cmp($0)",
            FilterText: "cmp",
            SortText: "10_cmp",
            Metadata: new FluentItemMetadata(
                Category: "Verbs",
                Signature: "target.cmp(value)",
                Description: "Compara el valor de target con value afectando banderas para saltos condicionales posteriores.",
                Inputs: "target, value",
                Modifies: "Solo banderas",
                Returns: "void",
                FlagsAffected: "Frf.Zero, Frf.Sign, Frf.Carry, Frf.Overflow")));

        AddItem(new LspCompletionItem(
            Label: "xor(src)",
            Kind: CompletionItemKind.Method,
            Detail: "Operación lógica XOR bit a bit (xor)",
            Documentation: MarkupContent.Markdown("Realiza una operación OR exclusiva entre receptor y operando. Frecuentemente usado para poner un registro en cero de forma eficiente."),
            InsertText: "xor($0)",
            FilterText: "xor",
            SortText: "10_xor",
            Metadata: new FluentItemMetadata(
                Category: "Verbs",
                Signature: "target.xor(value)",
                Description: "Operación XOR a nivel de bits. Pone a cero Carry y Overflow.",
                Inputs: "target, value",
                Modifies: "target = target ^ value",
                Returns: "void",
                FlagsAffected: "Frf.Zero, Frf.Sign, Frf.Parity (CF=0, OF=0)")));
    }

    private static void InitializeFlowDirectives()
    {
        AddItem(new LspCompletionItem(
            Label: ".flowJump(@target)",
            Kind: CompletionItemKind.Function,
            Detail: "Salto de flujo (jmp / jcc)",
            Documentation: MarkupContent.Markdown("Bifurca la ejecución del flujo a la etiqueta especificada, opcionalmente condicionado por el estado de una bandera."),
            InsertText: ".flowJump($0)",
            FilterText: ".flowJump",
            SortText: "20_flowJump",
            Metadata: new FluentItemMetadata(
                Category: "Flow",
                Signature: ".flowJump(@etiqueta [if [not] rFlags.cond])",
                Description: "Salto local a una etiqueta del flujo dentro de la rutina actual.",
                Inputs: "Etiqueta destino, condición opcional",
                Modifies: "Ipr (Puntero de instrucción)",
                Returns: "void",
                FlagsAffected: "Ninguna")));

        AddItem(new LspCompletionItem(
            Label: ".flowCall(Routine)",
            Kind: CompletionItemKind.Function,
            Detail: "Llamada a rutina externa o interna (call)",
            Documentation: MarkupContent.Markdown("Invoca la subrutina indicada empujando la dirección de retorno a la pila de ejecución según la ABI."),
            InsertText: ".flowCall($0)",
            FilterText: ".flowCall",
            SortText: "20_flowCall",
            Metadata: new FluentItemMetadata(
                Category: "Flow",
                Signature: ".flowCall(NombreRutina)",
                Description: "Llamada a subrutina siguiendo las convenciones de llamada del compilador.",
                Inputs: "Nombre de rutina declarada",
                Modifies: "Stack (dirección de retorno), Ipr",
                Returns: "Valor en Gpr0 según firma",
                FlagsAffected: "Indefinidas tras retorno")));

        AddItem(new LspCompletionItem(
            Label: ".flowStackJump()",
            Kind: CompletionItemKind.Function,
            Detail: "Retorno de rutina (ret)",
            Documentation: MarkupContent.Markdown("Extrae la dirección de retorno guardada en la pila y transfiere el control de vuelta al llamador."),
            InsertText: ".flowStackJump()",
            FilterText: ".flowStackJump",
            SortText: "20_flowStackJump",
            Metadata: new FluentItemMetadata(
                Category: "Flow",
                Signature: ".flowStackJump()",
                Description: "Finaliza la rutina actual y salta a la dirección de retorno de la pila.",
                Inputs: "Pila de llamada",
                Modifies: "Stack, Ipr",
                Returns: "Control al llamador",
                FlagsAffected: "Preservadas")));
    }

    private static void InitializeDirectives()
    {
        AddItem(new LspCompletionItem(
            Label: "program",
            Kind: CompletionItemKind.Keyword,
            Detail: "Declaración de programa FluentASM",
            Documentation: MarkupContent.Markdown("Define el contenedor principal de la unidad de compilación."),
            InsertText: "program $0\nend",
            SortText: "30_program",
            Metadata: new FluentItemMetadata(
                Category: "Directives",
                Signature: "program Identificador ... end",
                Description: "Bloque raíz del programa de ensamblado fluido.",
                Inputs: "Identificador del programa",
                Modifies: "Estructura de unidad",
                Returns: "Contenedor de programa",
                FlagsAffected: "Ninguna")));

        AddItem(new LspCompletionItem(
            Label: "section .execute",
            Kind: CompletionItemKind.Keyword,
            Detail: "Sección de código (.text)",
            Documentation: MarkupContent.Markdown("Apertura del segmento de instrucciones ejecutables del programa."),
            InsertText: "section .execute",
            SortText: "30_section_execute",
            Metadata: new FluentItemMetadata(
                Category: "Directives",
                Signature: "section .execute",
                Description: "Segmento de código con permisos de lectura y ejecución (RX).",
                Inputs: "Ninguno",
                Modifies: "Segmentación",
                Returns: "Bloque de código",
                FlagsAffected: "Ninguna")));

        AddItem(new LspCompletionItem(
            Label: "routine",
            Kind: CompletionItemKind.Keyword,
            Detail: "Declaración de subrutina",
            Documentation: MarkupContent.Markdown("Define una función o rutina autónoma de bajo nivel."),
            InsertText: "routine $0\n    .flowStackJump()\nend",
            SortText: "30_routine",
            Metadata: new FluentItemMetadata(
                Category: "Directives",
                Signature: "routine Nombre\n    ...\nend",
                Description: "Declaración de subrutina con alcance de etiquetas locales y parámetros.",
                Inputs: "Nombre de rutina",
                Modifies: "Tabla de símbolos",
                Returns: "Símbolo de rutina",
                FlagsAffected: "Ninguna")));

        AddItem(new LspCompletionItem(
            Label: "parameter",
            Kind: CompletionItemKind.Keyword,
            Detail: "Parámetro formal de rutina",
            Documentation: MarkupContent.Markdown("Asigna un nombre formal al siguiente registro libre de paso de argumentos según la convención de llamadas."),
            InsertText: "parameter $0",
            SortText: "30_parameter",
            Metadata: new FluentItemMetadata(
                Category: "Directives",
                Signature: "parameter nombre",
                Description: "Mapea un nombre local a un registro de argumentos (ej. `parameter n` -> Gpr1).",
                Inputs: "Identificador del parámetro",
                Modifies: "Variables locales",
                Returns: "Alias de registro",
                FlagsAffected: "Ninguna")));

        AddItem(new LspCompletionItem(
            Label: "Bit",
            Kind: CompletionItemKind.Keyword,
            Detail: "Especificador de literal o tipo Bit",
            Documentation: MarkupContent.Markdown("Crea un valor de tipo Bit (1 bit de amplitud) o califica un inmediato."),
            InsertText: "Bit $0",
            SortText: "30_bit",
            Metadata: new FluentItemMetadata(
                Category: "Directives",
                Signature: "Bit <valor>",
                Description: "Calificador de amplitud inmediata de 1 bit.",
                Inputs: "Valor numérico",
                Modifies: "Tipo de operando",
                Returns: "Literal tipado",
                FlagsAffected: "Ninguna")));
    }
}


