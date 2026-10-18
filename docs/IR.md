# IR — Modelo intermedio de FluentASM

Este documento define **qué estructuras de datos existen entre el análisis y la emisión de bytes**, y
qué invariantes se cumplen en cada paso. Es la pieza que une la superficie de entrada (el DSL `.fasm`
o la API fluent de C#) con la salida (una imagen plana `.x64`).

Sin esta pieza, cada superficie tendría su propia lógica de codificación y ambas divergirían. Con
ella, **toda la codificación se escribe una sola vez**.

- Ámbito: Fase 1 (x64, imagen plana). Las demás arquitecturas añaden encoders, no cambian el modelo.
- Implementación prevista: `src/FluentASM.Core/`.
- Decisiones relacionadas: [ADR 0003](adr/0003-salidas-imagen-plana.md), [ADR 0004](adr/0004-superficie-canonica.md), [ADR 0005](adr/0005-modelo-de-ejecucion.md).

---

## 1. Etapas del proceso

```
1. Entrada      .fasm (texto)  ─┐
                               ├─►  2. IR de origen  ──►  3. Layout  ──►  4. Codificación ──►  5. Emisión
1'. Entrada     API C# (objetos)┘                                                                  │
                                                                                    imagen .x64 (bytes)
```

| Etapa | Entrada | Salida | Invariante al terminar |
|---|---|---|---|
| 1. Entrada | Texto `.fasm` o llamadas de la API de C# | IR de origen | Toda instrucción usa mnemónicos y operandos del catálogo; ningún símbolo desconocido queda sin diagnosticar como posible error de escritura |
| 2. IR de origen | IR de origen | IR de origen validado | Cada símbolo tiene un dueño (sección o externo), cada sección tiene nombre, flags y alineación |
| 3. Layout | IR de origen | IR con direcciones asignadas | Cada símbolo tiene `Offset` dentro de su sección y cada sección tiene `VirtualAddress` relativa a la imagen |
| 4. Codificación | IR con direcciones | Bytes + lista de relocalizaciones | Cada instrucción tiene codificación seleccionada y tamaño conocido |
| 5. Emisión | Bytes + relocalizaciones | Imagen plana | Todas las relocalizaciones están resueltas; ninguna queda pendiente |

El **layout** y la **codificación** interactúan: el tamaño de un salto corto (`eb` + 1 byte) y el de
uno cercano (`e9` + 4 bytes) es distinto, y elegir uno u otro puede mover etiquetas posteriores. La
solución se detalla en la sección 6.

---

## 2. Enumeraciones base

| Tipo | Valores | Notas |
|---|---|---|
| `Architecture` | `X64`, `X86`, `Arm` | `X86RealMode16` se añadirá en la Fase 7, no es Fase 1 |
| `Abi` | `Win64`, `SystemV`, `None` | `None` solo para código sin rutinas. Ver [ABI.md](ABI.md) |
| `TargetSystem` | `BareMetal`, `Windows`, `Linux`, `MacOs` | Determina qué es válido: con `BareMetal` no hay API del sistema |
| `Endianness` | `Little` (único valor soportado) | Se declara explícitamente para no asumirlo |

---

## 3. Registros

Un registro es un nombre con un número de codificación, un tamaño y una familia. **No** es una
variable: es una ranura del procesador.

```csharp
// Ilustrativo, no es la implementación final.
public sealed record Register(string Name, int Number, int SizeBits, RegisterClass Class, bool RequiresRex);
```

| Familia | Tamaño | Números | Ejemplos |
|---|---|---|---|
| `GeneralPurpose64` | 64 | 0–15 | `Rax`, `Rcx`, `Rdx`, `Rbx`, `Rsp`, `Rbp`, `Rsi`, `Rdi`, `R8`–`R15` |
| `GeneralPurpose32` | 32 | 0–15 | `Eax`, `Ecx`, `Edx`, `Ebx`, `Esp`, `Ebp`, `Esi`, `Edi`, `R8d`–`R15d` |
| `GeneralPurpose16` | 16 | 0–15 | `Ax`, `Cx`, `Dx`, `Bx`, `Sp`, `Bp`, `Si`, `Di`, `R8w`–`R15w` |
| `GeneralPurpose8` | 8 | 0–15 | `Al`, `Cl`, `Dl`, `Bl`, `Spl`, `Bpl`, `Sil`, `Dil`, `R8b`–`R15b` |
| `GeneralPurpose8High` | 8 | 4–7 | `Ah`, `Ch`, `Dh`, `Bh` (**prohibido** con prefijo REX) |
| `InstructionPointer` | 64 | — | `Rip` (solo como base de un operando de memoria) |
| `Flags` | 64 | — | `Rflags` (Fase 2) |

Reglas obligatorias:

1. `Spl`, `Bpl`, `Sil`, `Dil` **exigen** prefijo REX, aunque el número sea menor que 8.
2. `Ah`, `Ch`, `Dh`, `Bh` **prohiben** el prefijo REX. Un operando que mezcle un registro de esta
   familia con uno de `R8`–`R15` es un error semántico, no una codificación creativa.
3. Mezclar tamaños distintos en una misma instrucción solo se admite en las formas que lo permiten
   explícitamente (`Movzx`, `Movsx`). `Add(Rax, Ebx)` es un error de diagnóstico.
4. El nombre canónico es `PascalCase` (`Rax`); el lexer acepta cualquier combinación de mayúsculas y
   minúsculas porque los registros son palabras reservadas del lenguaje.

El catálogo completo con números y nombres vive en `FLUENT_SYNTAX.md`; aquí se define **cómo se
representan**, no cuáles existen.

---

## 4. Operandos

```csharp
public abstract record Operand(int SizeBits);                       // -1 = tamaño inferido por la instrucción
public sealed record ImmediateOperand(long Value, int SizeBits) : Operand(SizeBits);
public sealed record RegisterOperand(Register Register) : Operand(Register.SizeBits);
public sealed record MemoryOperand(
    Register? Base,          // null si es rip-relativo o absoluto
    Register? Index,         // null si no hay índice
    int Scale,               // 1, 2, 4 u 8 (siempre 1 si no hay índice)
    long Displacement,
    bool RipRelative,
    int SizeBits,            // tamaño del dato accedido, no de los registros
    bool ExplicitSize) : Operand(SizeBits);
public sealed record SymbolOperand(Symbol Symbol, Operand Inner);   // marca un símbolo dentro de un operando
```

Reglas de los operandos de memoria:

| Regla | Detalle |
|---|---|
| Forma general | `[base + index*scale + desplazamiento]` |
| Escala | Solo 1, 2, 4 u 8. Cualquier otro valor es un error de validación |
| `Rsp` como índice | Prohibido (no existe codificación). `R12` como índice tampoco, porque codifica como `Rsp` y necesita SIB |
| Base `Rip` | Solo sin índice, forma `[Rip + desplazamiento]` |
| Tamaño | Se infiere del otro operando; si es ambiguo (`Mov([Rbx], 1)`), **es obligatorio** declararlo y su ausencia es un error que lo explica |
| Desplazamiento | Se emite como `disp8` si cabe en un byte con signo; si no, como `disp32` |

La decisión de `disp8` frente a `disp32` **no** la elige el usuario: la elige el codificador, buscando
la forma más corta, y el resultado queda registrado (ver sección 7).

---

## 5. Instrucciones, secciones, símbolos y relocalizaciones

### 5.1 Instrucción

```csharp
public sealed record Instruction(string Mnemonic, IReadOnlyList<Operand> Operands, int SizeHint = 0);
```

Una instrucción en el IR es **solo** mnemónico y operandos. El codificador es quien decide la forma
concreta de la tabla de codificación, y **debe** ser determinista: para los mismos operandos siempre
la misma secuencia de bytes, de la forma más corta posible.

### 5.2 Sección

```csharp
public sealed record Section(
    string Name,                 // ".text", ".data", ".bss", o nombre propio
    SectionKind Kind,            // Code, Data, Uninitialized
    int Alignment,               // potencia de dos; por defecto 16 para .text
    bool Read, bool Write, bool Execute,
    int VirtualAddress,          // asignado en el layout, relativo a la imagen
    int FileOffset,              // asignado en la emisión; -1 para Uninitialized
    byte[] Content);             // bytes reales; vacío en Uninitialized
```

Reglas:

1. `.bss` es `Uninitialized`: se reserva espacio en memoria pero **no ocupa bytes** en el archivo.
2. El orden de las secciones en la imagen plana es el orden de declaración, con `.text` primero.
3. La alineación nunca es un supuesto: siempre es un valor declarado, con valor por defecto.

### 5.3 Símbolo

```csharp
public sealed record Symbol(
    string Name,
    SymbolKind Kind,             // CodeLabel, DataLabel, External, Constant
    SymbolScope Scope,           // Local, Global
    Section? Section,            // null si es External o Constant
    int Offset,                  // dentro de la sección, asignado en el layout
    int Size,                    // en bytes; 0 si no se conoce
    ConstantValue? Value);       // solo para Constant
```

| Tipo | Significado | Ejemplo |
|---|---|---|
| `CodeLabel` | Punto del código al que se puede saltar | `main:` |
| `DataLabel` | Punto de la sección de datos | `mensaje:` |
| `External` | Símbolo definido fuera de la imagen | `extern ExitProcess` |
| `Constant` | Valor calculado en tiempo de ensamblado | `longitud equ 12` |

Los símbolos `External` **no** tienen cabida en una imagen plana de Fase 1: usarlos produce un error
de diagnóstico que explica que hacen falta imports y en qué fase llegan (Fase 5).

### 5.4 Relocalización

```csharp
public sealed record Relocation(
    RelocationKind Kind,         // Rel32, Rel8, Abs32, Abs64
    string SymbolName,
    int SectionIndex,
    int OffsetInSection,         // dónde comienzan los bytes del parche
    int Width,                   // 1, 4 u 8 bytes
    long Addend);                // valor sumado al símbolo (normalmente -4 en un Rel32)
```

En la Fase 1, con la regla de independencia de posición (ver [ADR 0003](adr/0003-salidas-imagen-plana.md)),
las únicas relocalizaciones son **internas** a la imagen y se resuelven antes de escribir el archivo:
`Rel8` y `Rel32` entre símbolos de la misma imagen. `Abs32` y `Abs64` se rechazan con un error.

**Criterio de aceptación:** si al terminar la emisión queda una sola relocalización pendiente, la
compilación falla. No se escribe un binario con parches por resolver.

---

## 6. Layout: asignación de direcciones y ajuste de saltos

El layout es la etapa que hace que los saltos cortos y cercanos convivan. Algoritmo obligatorio:

1. **Supuesto inicial:** todos los saltos condicionales e incondicionales se codifican en su forma
   **más corta** (`Rel8`).
2. **Asignación de direcciones:** se calculan los offsets de todos los símbolos con esos tamaños.
3. **Verificación:** si un desplazamiento relativo no cabe en el rango de su forma corta
   (`-128..127`), se **promociona** ese salto a la forma cercana (`Rel32`).
4. **Repetición:** al cambiar un tamaño, los offsets posteriores se mueven, lo que puede provocar
   nuevas promociones. Se repite desde el paso 2 hasta que una pasada completa no cambie ningún
   tamaño.
5. **Convergencia:** el proceso es monótono (los tamaños solo crecen) y por tanto termina. Si tras
   32 iteraciones no converge, es un error interno (`FASM9001`), nunca un bucle infinito.

Este comportamiento es el mismo que el de otros ensambladores y es **verificable** comparando bytes
con NASM, que aplica la misma política de formas más cortas.

---

## 7. Registro de decisiones de codificación (trazabilidad)

Cada instrucción codificada debe conservar la decisión tomada, porque es lo que permite explicar el
resultado y compararlo con el oráculo:

| Campo | Para qué sirve |
|---|---|
| Forma elegida (p. ej. `Add r/m64, imm8`) | Explicar por qué `add rax, 5` son 4 bytes y `add rax, 1000` son 6 |
| Tamaño en bytes | Base del layout y de las pruebas de bytes dorados |
| Prefijos usados (`REX`, `0F`) | Depurar diferencias con NASM |
| Desplazamiento elegido (`disp8`/`disp32`) | Lo mismo |

Esto no es documentación opcional: alimenta el futuro `fasm_admin dump --asm` y los mensajes de
diagnóstico. Sin esta traza, cualquier diferencia de bytes con NASM se vuelve indepurable.

---

## 8. Diagnósticos

Modelo único para ambas superficies. Cada diagnóstico tiene: código, severidad, ubicación
(archivo, línea, columna; o el punto de la cadena fluent en C#), mensaje en español y, cuando es
posible, una sugerencia de corrección.

| Rango | Etapa | Ejemplo |
|---|---|---|
| `FASM1xxx` | Léxico | `FASM1002` carácter inesperado |
| `FASM2xxx` | Sintáctico | `FASM2001` falta el paréntesis de cierre |
| `FASM3xxx` | Semántico y ABI | `FASM3001` más de 4 parámetros en Win64; `FASM3002` registro de 8 bits alto con REX |
| `FASM4xxx` | Codificación | `FASM4001` no existe forma de la instrucción para esos operandos |
| `FASM5xxx` | Layout y relocalizaciones | `FASM5001` símbolo externo en imagen plana; `FASM5002` dirección absoluta no permitida |
| `FASM9xxx` | Errores internos | `FASM9001` el layout no converge |

Regla: el compilador **acumula** diagnósticos y los informa todos juntos; no se detiene en el primero.

---

## 9. Validaciones obligatorias

| Validación | Cuándo | Si falla |
|---|---|---|
| Un símbolo no se define dos veces | Después del análisis | `FASM3003`, indicando ambas ubicaciones |
| Todo símbolo referenciado existe o es `extern` | Antes del layout | `FASM3004` con sugerencia del símbolo más parecido |
| `size` de un operando de memoria ambiguo está declarado | Antes de codificar | `FASM3005` |
| La escala es 1, 2, 4 u 8 | Antes de codificar | `FASM4002` |
| No hay `Rsp`/`R12` como índice | Antes de codificar | `FASM4003` |
| No hay direcciones absolutas a símbolos | Antes de codificar | `FASM5002`, sugiriendo `[rel simbolo]` |
| No hay símbolos externos en imagen plana | Antes del layout | `FASM5001` con la fase en la que llegan |
| La pila está alineada a 16 en cada llamada generada | Después del layout | `FASM3006` (la generación de prólogo/epílogo debe hacerlo imposible, pero se verifica) |
| Ninguna relocalización queda pendiente | Al emitir | `FASM5003` |

---

## 10. Criterios de aceptación de la Fase 1

Una implementación del IR cumple la Fase 1 cuando:

1. El ejemplo mínimo (`Mov(Eax, 42)` + `Ret()`) produce `b8 2a 00 00 00 c3`, **idéntico a NASM**.
2. `routine AddTwoNumbers` con `parameter a, b` produce prólogo `55 48 89 e5 48 83 ec 20`, cuerpo
   `48 89 c8 48 01 d0`, epílogo `c9 c3`, y al ejecutarse en memoria con `AddTwoNumbers(10, 32)`
   devuelve **42**.
3. La misma imagen cargada en dos direcciones de memoria distintas produce el mismo resultado
   (prueba de independencia de posición).
4. Un salto a una etiqueta lejana se promociona a `Rel32` y el archivo final sigue siendo válido.
5. Cualquier ejemplo que use `extern` falla con `FASM5001`, no con una excepción.
6. La API fluent de C# y el DSL `.fasm` (Fase 2) producen **los mismos bytes** para el mismo programa.
