# 00 — Registro de decisiones

> **Normativo.** Este documento fija las decisiones que el resto de la especificación asume. Cada decisión tiene un identificador estable (`D-NNN`), el contexto que la motivó, la decisión concreta, la justificación física o cognitiva, y sus consecuencias.
>
> Si una decisión cambia, se actualiza aquí primero y luego se propagan las referencias. Las decisiones marcadas **⚠ revisar** son las únicas que aún esperan confirmación humana (D-1…D-5 del [README](README.md#7-decisiones-pendientes-de-confirmación)).

---

## D-001 — El código describe flujos, no valores

**Contexto.** Un ensamblador tradicional mezcla instrucción y dato: `mov rax, 100`. El dato `100` queda incrustado en el flujo de instrucciones, y el programa sabe de antemano qué valor va a circular.

**Decisión.** La superficie de FluentASM **no admite datos incrustados en instrucciones**. Una instrucción expresa únicamente:

- **fuente** (de dónde vienen los bits),
- **transformación** (qué verbo físico se aplica),
- **destino** (dónde quedan los bits),
- **forma** (capacidad, partición, desplazamiento, alineación, índice, estado).

Los datos concretos viven en memoria, en registros, en hardware o en recursos externos; el código solo describe la topología por la que circulan.

**Justificación.** Es la diferencia entre un programa que *contiene* información y un programa que *transporta y transforma* información. Elimina la categoría de "constante mágica" y hace visible la procedencia de cada bit.

**Consecuencias.**
- `Gpr0.copy(Bit:42)` es inválido (dato `42`). Se sustituye por `Gpr0.copy(Byte:8 [origen])` o por un parámetro.
- Los verbos cubren los casos estructurales que antes se resolvían con literales: `clear()`, `increment()`, `decrement()`.
- El compilador clasifica cada literal por su **posición semántica**: forma (permitida) o dato (prohibido). Véase [06-LITERALES](06-LITERALES.md).
- El *bootstrap* se resuelve con secciones de datos o recursos externos (D-022).

---

## D-002 — Solo dos magnitudes primitivas: `Bit:N` y `Byte:N`

**Contexto.** Los lenguajes de alto nivel introducen `int`, `float`, `bool`, `char`, `string`, `struct`, `array`, `pointer`. Cada una es una abstracción que oculta el hecho físico de que todo son bits en contenedores.

**Decisión.** El ATM (Assembly Type Model) tiene exactamente dos magnitudes primitivas:

| Magnitud | Significado | Contexto natural |
|---|---|---|
| `Bit:N` | Ancho de `N` bits | Registros, flags, shifts, particiones SIMD, campos |
| `Byte:N` | Extensión de `N` bytes | Memoria, offsets, capacidades, alineación, pila |

Con múltiplos binarios: `KiloBit`, `MegaBit`, `GigaBit`, `TeraBit`, `KiloByte`, `MegaByte`, `GigaByte`, `TeraByte`. No existen tipos compuestos, punteros ni colecciones en el lenguaje.

**Justificación.** Un puntero no es un tipo: es una dirección que cabe en un registro. Un array no es un tipo: es una dirección más una capacidad. Un `struct` no es un tipo: es un conjunto de offsets. Todo eso se expresa con `Bit:N`/`Byte:N` y aritmética de direcciones.

**Consecuencias.**
- No hay `Int32`, `Float64`, `Bool`, `Char`, `String`, `Vector`, `Mask`, `Pointer`, `Array`, `Struct`.
- La aritmética de capacidades usa `Bit`/`Byte`; la de direcciones usa `Byte`.
- Los alias históricos (`QWord`, `DWord`, `TByte`) no existen; se usan `Byte:8`, `Byte:4`, `Byte:10`.
- Los contextos están separados: `Bit` en registro/ALU/partición; `Byte` en memoria/offset/capacidad. Mezclarlos produce `FASM3020`/`FASM3021`.

---

## D-003 — La interpretación es propiedad del verbo, no del tipo

**Contexto.** Los mismos 32 bits pueden ser un entero con signo, un entero sin signo, un flotante IEEE 754 o cuatro bytes. El hardware tiene opcodes distintos para cada interpretación (`paddd` vs `addps`, `fadd` vs `fiadd`). Un tipo abstracto obligaría al compilador a adivinar o a inventar una conversión.

**Decisión.** El tipo solo describe **forma** (ancho/extensión). La **interpretación** la declara el verbo:

```fasm
Gpr0.add(Gpr1)                     // entero sin signo (ADD)
Gpr0.addWithCarry(Gpr1)            // entero con acarreo (ADC)
Simd0.r512.part{32}.addInt(Simd1.r512.part{32})    // vpaddd
Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})  // vaddps
Xrf.loadFloat(Byte:8 [x])         // FLD qword (IEEE 754)
Xrf.loadInt(Byte:8 [x])           // FILD qword (entero)
```

**Justificación.** Es la única forma de ser fiel al hardware sin introducir tipos que el silicio no tiene. Además, hace que el IntelliSense sea inequívoco: tras `Simd0.r512.part{32}.` el editor ofrece `addInt`, `addFloat`, `multiplyInt`, `multiplyFloat`, etc., cada uno con su opcode.

**Consecuencias.**
- El sufijo `Int`/`Float`/`Signed`/`Unsigned` forma parte del nombre del verbo y se documenta en el catálogo ([07-VERBOS](07-VERBOS.md), [10-SIMD](10-SIMD.md), [11-X87](11-X87.md)).
- No hay conversiones implícitas. Convertir es un verbo explícito (`convertToInt`, `convertToFloat`).
- Los flags que produce cada verbo son los del opcode real; no se normalizan ni se "mejoran".

---

## D-004 — Honestidad física

**Contexto.** Una sintaxis cómoda puede mentir: `mov` sugiere "mover" pero en realidad copia; `rFlags` sugiere un registro único pero el hardware tiene bits con efectos distintos; `XMM` sugiere un registro de 128 bits pero físicamente es la mitad baja de uno de 512.

**Decisión.** Todo constructo del lenguaje debe poder responder estas cinco preguntas:

1. ¿Qué estado físico lee?
2. ¿Qué estado físico escribe?
3. ¿Qué encoding produce?
4. ¿Qué registros, flags o memoria toca de forma implícita?
5. ¿Qué ocurre si el hardware objetivo no lo soporta?

Si alguna respuesta es "algo oculto", el constructo se rechaza o se documenta como explícito.

**Justificación.** El programador de bajo nivel necesita un modelo mental que coincida con el silicio. Una mentira cómoda se paga en depuración, en *performance* impredecible y en bugs de encoding.

**Consecuencias.**
- `Gpr0` es 64 bits; `.low32/.low16/.low8/.high8` son vistas reales, con sus efectos reales (zero-extend o merge).
- `Ipr` no se copia con `copy`; solo cambia con verbos de flujo (`FASM3060`).
- `SimdN` es un contenedor de 512 bits; `.r128/.r256` son vistas, no registros distintos.
- `CrfN`, `SrfN`, `DrfN`, `Msr` y `Pkru` tienen verbos y privilegios propios; no se disfrazan de GPR.
- Los operandos implícitos (`Gpr1` en `flowLoop`, `Gpr4` en `Stack`, `Gpr6/Gpr7` en copia de bloques) se declaran en la tabla del verbo, nunca se esconden.

---

## D-005 — Un solo patrón: `destino.verbo(fuente)`

**Contexto.** Las propuestas anteriores usaban varias formas: `.copy(origen to destino)`, `.alu(Rax add Rbx)`, `.call(sym)`, `.ret()`, `.jump(label)`. Cada forma era una excepción gramatical y una carga cognitiva distinta.

**Decisión.** Toda acción es una llamada sobre un receptor que es el destino real:

```fasm
destino.verbo(fuente1, fuente2, ...)
```

El receptor puede ser un registro, una vista, una ubicación de memoria con capacidad, un archivo de registros (`Frf`, `Ipr`, `Stack`) o una tupla de destinos `(D1:D2)` cuando el hardware escribe varios.

**Justificación.** Un solo patrón reduce la gramática, facilita el IntelliSense (el receptor determina las sugerencias) y hace explícito el flujo: lo que está a la izquierda muta; lo que está entre paréntesis alimenta.

**Consecuencias.**
- No existen instrucciones "solas" (`.nop()` se convierte en `Cpu.noOperation()`; véase D-005a).
- No existe `.commit()`, `.build()`, `.apply()` ni ningún cierre de contexto.
- El orden `origen → destino` de propuestas antiguas se invierte: `Gpr1.copy(Gpr0)`, no `.copy(Gpr0 to Gpr1)`.
- Las condiciones de salto son parte de la fuente: `Ipr.flowJump(@fin if Frf.zero)`.

### D-005a — Verbos sin receptor físico

Algunas operaciones no mutan un registro concreto: `NOP`, `PAUSE`, `HLT`, `LFENCE`. Para ellas se usa un **recurso físico** como receptor:

```fasm
Cpu.noOperation()        // NOP
Cpu.pause()              // PAUSE
Cpu.halt()               // HLT
Fence.full()             // MFENCE
```

El receptor sigue siendo un objeto real (la CPU, el pipeline, la unidad de memoria), no un contexto inventado.

---

## D-006 — Archivos de registros con prefijo de tres letras e índice

**Contexto.** Los nombres legacy (`RAX`, `CS`, `CR0`, `RFLAGS`, `XMM0`, `K0`) son inconsistentes, dependen de la historia de Intel y no revelan a qué archivo físico pertenecen.

**Decisión.** Cada archivo de registros se nombra con **tres letras + índice decimal**, empezando en 0, en el orden del encoding:

| Archivo | Miembros | Ancho físico | Notas |
|---|---|---|---|
| `Gpr` | `Gpr0`–`Gpr15` | 64 | Propósito general |
| `Srf` | `Srf0`–`Srf5` | 16 | Selectores de segmento |
| `Crf` | `Crf0`–`Crf4`, `Crf8` | 64 | Control |
| `Drf` | `Drf0`–`Drf7` | 64 | Depuración |
| `Frf` | `Frf` | 64 | Flags (RFLAGS) |
| `Ipr` | `Ipr` | 64 | Instruction pointer |
| `Simd` | `Simd0`–`Simd31` | 512 | Vectorial (excepción de 4 letras, D-006a) |
| `Krf` | `Krf0`–`Krf7` | 64 | Máscaras AVX-512 |
| `Xrf` | `Xrf0`–`Xrf7` | 80 | Pila x87 |
| `Xcr` | `Xcr0` | 64 | Control extendido |
| `Pkru` | `Pkru` | 32 | Protection keys |
| `Ssp` | `Ssp` | 64 | Shadow stack pointer (CET) |

**Justificación.** El nombre revela el archivo, el índice coincide con el número físico del registro y el conjunto es predecible. `Gpr7` siempre es el octavo GPR; `Srf3` siempre es FS; `Crf4` siempre es CR4.

**Consecuencias.**
- `Rax`, `Eax`, `Ax`, `Al`, `Ah`, `Rcx`… `R15`, `CS`, `DS`, `ES`, `FS`, `GS`, `SS`, `CR0`… `CR8`, `RFLAGS`, `RIP`, `XMM0`, `YMM0`, `ZMM0`, `K0`, `ST(0)` son **alias prohibidos** → `FASM3012`.
- El índice nunca se escribe con llaves en el código: `Simd0`, no `Simd{0}`.
- Los índices fuera de rango producen `FASM3013` (GPR) o el diagnóstico del archivo correspondiente.

### D-006a — La excepción `Simd`

`Simd` tiene cuatro letras. Se acepta porque:

1. Es el término hardware universal para la unidad vectorial.
2. `Vrf` (Vector Register File) ocultaría que x64 usa SSE/AVX/AVX-512, no un "vector file" genérico.
3. La alternativa `Sim` es ambigua; `Smd` es ilegible.

La regla de tres letras se mantiene para todos los demás archivos. `Simd` es la única excepción documentada.

---

## D-007 — GPR de 64 bits con vistas reales

**Decisión.** `Gpr0`–`Gpr15` son celdas de 64 bits. Las vistas son ventanas sobre la misma celda:

| Vista | Bits | Efecto al escribir |
|---|---|---|
| `GprN` | 0–63 | Reemplaza los 64 bits |
| `.low32` | 0–31 | **Zero-extend**: bits 32–63 = 0 |
| `.low16` | 0–15 | **Merge**: conserva bits 16–63 |
| `.low8` | 0–7 | **Merge**: conserva bits 8–63 |
| `.high8` | 8–15 | **Merge**; solo `Gpr0`–`Gpr3` |

**Justificación.** Es el comportamiento exacto del hardware (Intel SDM Vol. 1 §3.4.1.1). Ocultarlo produciría bugs sutiles al mezclar anchos.

**Consecuencias.**
- `.high8` en `Gpr4`–`Gpr15` → `FASM3015`.
- `.low8` en `Gpr4`–`Gpr7` requiere REX; el encoder lo emite automáticamente.
- Una instrucción no puede mezclar `high8` (`Gpr0`–`Gpr3`) con `low8` de `Gpr4`–`Gpr7` → `FASM3002`.
- `Gpr4` (RSP) no puede ser índice SIB → `FASM4003`; `Gpr5`/`Gpr13` como base requieren `disp8=0` (el encoder lo emite).

---

## D-008 — `Frf` unificado y flags semánticos

**Contexto.** `RFLAGS` es un registro de 64 bits con bits reservados, bits de sistema y bits de condición. Tratarlo como un entero (`rFlags`) permite escribir combinaciones inválidas y oculta qué flag cambia cada instrucción.

**Decisión.** `Frf` es el **Flags Register File**:

- `Frf` (64 bits) para lecturas/escrituras completas (`Stack.push(Frf)`, `Frf.pop(Stack)`).
- Flags individuales con nombre semántico: `.carry`, `.parity`, `.adjust`, `.zero`, `.sign`, `.trap`, `.interrupt`, `.direction`, `.overflow`, `.iopl`, `.nestedTask`, `.resume`, `.virtual8086`, `.alignmentCheck`, `.virtualInterrupt`, `.virtualInterruptPending`, `.cpuid`.
- Estados: `Frf.carry(1)` = set, `Frf.carry(0)` = clear, `Frf.carry(not)` = toggle (solo CF tiene `CMC`).
- Lectura de flag individual: **solo** a receptor `.low8` → `Gpr0.low8.copy(Frf.zero)` produce 0/1 en `AL`; `Gpr0.low8.copy(not Frf.zero)` produce el negado. Para anchos mayores: paso explícito `Gpr0.low8.copy(Frf.zero); Gpr0.loadUnsigned(Gpr0.low8)` (zero-extend).
- Condiciones: `if Frf.zero`, `if not Frf.carry`, `if Frf.sign unequal Frf.overflow`.

**Justificación.** El programador piensa en flags, no en máscaras. `Frf.zero` es más claro que `rFlags & 0x40`, y el compilador puede verificar qué flags están definidos.

**Consecuencias.**
- `rFlags` es alias prohibido → `FASM3012`.
- Escribir flags de sistema en ring 3 → `FASM3061`.
- Leer un flag no definido por la instrucción anterior → `FASM4012` (análisis de flujo).
- `Frf.low32`/`Frf.low16` en x64 nativo → `FASM3064` (el hardware usa RFLAGS de 64 bits; las formas `popfd`/`popf` no son válidas en modo largo).

---

## D-009 — `Ipr` solo cambia por flujo

**Decisión.** `Ipr` es el puntero de instrucción. **No** se lee ni se escribe con `copy` (`FASM3060`). Solo cambia mediante:

- `Ipr.flowCall(destino)`
- `Ipr.flowJump(destino [if condición])`
- `Ipr.flowStackJump([Byte:N])`
- `Ipr.flowLoop(destino)`
- `(Ipr:SrfN).flow*` para llamadas/saltos lejanos
- `Ipr.flowInterrupt(vector)` / `Ipr.flowSyscall()` / `Ipr.flowBreakpoint()`

Para leer el valor actual: `Gpr0.copy(Ipr.current)` (expande a `LEA reg, [RIP]`).

**Justificación.** `RIP` no es un GPR; el hardware no permite `mov rip, rax`. Permitir `copy` sería mentir y produciría un encoding inexistente.

**Consecuencias.**
- El par lejano es `(Ipr:SrfN)`, no una palabra clave `Far`.
- `Ipr.current` es la única lectura; su uso en código PIC es idiomático.
- Los targets de flujo son símbolos (`@label`, rutina) o registros/memoria de 64 bits.

---

## D-010 — Tuplas de destino para instrucciones multirresultado

**Contexto.** `MUL`, `DIV`, `CQO`, `CPUID`, `RDTSC` escriben más de un registro. El patrón `destino.verbo(fuente)` tiene un solo receptor.

**Decisión.** Se usa una **tupla de destinos** entre paréntesis, en el orden físico documentado:

```fasm
(Gpr2:Gpr0).multiply(Gpr3)        // RDX:RAX = RAX * RBX
(Gpr2:Gpr0).signExtend()           // CQO: RDX:RAX = signExtend(RAX)
(Gpr0:Gpr2).divide(Gpr3)           // RAX = cociente, RDX = resto
(Gpr2:Gpr0).readTimestamp()        // EDX:EAX = TSC
(Gpr0:Gpr3:Gpr1:Gpr2).identify()   // EAX:EBX:ECX:EDX = CPUID
```

**Justificación.** La tupla es una lista de destinos reales, no un objeto. Es la forma más honesta de representar una instrucción que escribe varios registros sin inventar un `struct` de resultado.

**Consecuencias.**
- La tupla solo se permite en verbos que el catálogo marca como multirresultado; en otro caso → `FASM3016`.
- El orden de la tupla es el orden de la tabla del verbo, no el alfabético.
- Las tuplas no se encadenan (no tiene sentido "seguir" una operación sobre dos destinos); el formateador lo impone.
- Los destinos de una tupla deben ser registros del mismo archivo o del archivo que el verbo especifique.

---

## D-011 — Fluent para configuración, bloque para procedimiento

**Contexto.** El usuario quiere las dos cosas: encadenar para expresar configuración y declaraciones (como una maqueta o una consulta), y escribir bloque para procesos, una acción por línea, sin perder el hilo mental.

**Decisión.** La gramática es la misma (`receptor.verbo(fuente)`); cambia el **contexto**:

| Contexto | Estilo | Regla |
|---|---|---|
| Cabecera de programa/rutina | Fluent encadenado | Se encadenan atributos del mismo receptor |
| Declaraciones de datos/secciones | Selectores y bloques | Una declaración por línea |
| Cuerpo de rutina | Bloque | Una acción física por línea; se permite encadenar mutadores del mismo destino, pero el formateador lo expande |
| Condiciones | Expresión | `if` + expresión de flags |

**Justificación.** Encadenar configuración no cuesta nada cognitivamente: es un objeto con propiedades. Encadenar acciones oculta pasos y dificulta el *debug*. La misma sintaxis evita tener que aprender dos lenguajes.

**Consecuencias.**
- `routine main.abi(win64).frame(Byte:32).preserves(Gpr3) { ... }` es válido.
- `Gpr0.copy(x).add(y).shiftLeft(3)` es **válido pero no recomendado** en cuerpos; el formateador lo expande a tres líneas. Se emite `FASM8001` (info) si el usuario lo escribe en un cuerpo.
- No hay "modo fluent" y "modo bloque" separados: hay un solo parser.

---

## D-012 — Sin temporales ocultos

**Decisión.** El compilador **no** crea registros, memoria ni valores temporales que el programador no haya declarado.

- Memoria → memoria: `FASM4026` ("use un registro explícito").
- Operación que requiere scratch: `FASM4028` ("declare un registro de trabajo").
- Dirección demasiado compleja para un solo SIB: `FASM4029` ("calcule la dirección en un GPR").
- El compilador no hace *register allocation*, ni *spilling*, ni *inlining*.

**Justificación.** Un temporal oculto cambia el estado físico sin que el programador lo vea y rompe la promesa de honestidad. En un ensamblador, el programador controla los registros.

**Consecuencias.**
- La ABI sí puede generar prólogo/epílogo **declarado** (`.frame`, `.preserves`, `.shadow`); eso no es un temporal, es un contrato.
- Las expansiones de verbos compuestos (p. ej. `reduceAddFloat`) se documentan paso a paso y usan solo los registros que el verbo declara.
- Los diagnósticos de scratch son errores, no advertencias.

---

## D-013 — Codificación determinista y más corta

**Decisión.** Para una misma fuente y un mismo target, el compilador emite **siempre los mismos bytes**, eligiendo la forma más corta y segura:

1. `rel8` si el destino cabe; si no, `rel32`.
2. `disp8` si cabe con signo; si no, `disp32`.
3. REX solo cuando es necesario (64 bits, registros 8–15, `low8` de `Gpr4`–`Gpr7`).
4. VEX de 2 bytes cuando sea posible; VEX de 3 bytes o EVEX cuando el ancho/máscara lo exija.
5. Sin prefijos redundantes ni NOPs de relleno salvo `align`.

**Justificación.** La reproducibilidad es una propiedad de seguridad y de pruebas: dos compilaciones deben producir el mismo binario. La forma más corta es también la más rápida de decodificar.

**Consecuencias.**
- El encoder tiene una tabla de decisión cerrada; no hay "elige la que prefieras".
- Los tests comparan bytes exactos, no solo comportamiento.
- Cualquier optimización posterior (alinear bucles, reordenar) es un paso explícito y opcional, con mapa de transformación.

---

## D-014 — Estructura de programa con llaves y cabecera encadenada

**Decisión.** Un archivo `.fasm` contiene un programa:

```fasm
program <nombre> [.atributo(...)]* {
    section .execute [.atributo(...)]* {
        routine <nombre> [.atributo(...)]* {
            parameter ...
            local ...
            <sentencias>
        }
    }
}
```

- Llaves `{}` para todos los bloques.
- Atributos de programa/rutina en la cabecera, encadenados con `.`.
- `section` declara `.execute`, `.rodata`, `.data` o `.bss`.
- `routine` declara código; `data` declara bytes.
- `parameter` y `local` van al inicio del cuerpo de la rutina.

**Justificación.** Las llaves son inequívocas, se anidan bien y permiten que el editor colapse bloques. La cabecera encadenada expresa configuración sin ensuciar el cuerpo. Es la forma que el usuario describió como "fluent hasta cierto punto y bloque hasta cierto punto".

**Consecuencias.**
- `program ... end`, `routine ... end` y `section .execute` sin llaves quedan como sintaxis legacy (migración).
- El `entry` es un atributo del programa: `.entry(main)`.
- La ABI y el target son atributos del programa: `.abi(win64).target(x64-avx2)`.
- Los atributos de rutina son `.abi`, `.frame`, `.preserves`, `.shadow`, `.redzone`, `.naked`, `.align`, `.ring`, `.visibility`, `.returns`.

---

## D-015 — Cuatro secciones canónicas

**Decisión.**

| Sección | Permisos | Contenido | Payload |
|---|---|---|---|
| `.execute` | lectura + ejecución | Instrucciones | **Prohibido** |
| `.rodata` | lectura | Datos constantes, recursos | Permitido (bootstrap) |
| `.data` | lectura + escritura | Datos inicializados | Permitido (bootstrap) |
| `.bss` | lectura + escritura | Datos reservados, zero-init | Prohibido (siempre cero) |

**Justificación.** Separa el flujo de instrucciones del almacenamiento de datos, y hace explícito dónde puede vivir un dato. `.bss` no necesita payload porque el cargador la zero-inicializa.

**Consecuencias.**
- `data` en `.execute` → `FASM2007`.
- `data` con payload en `.bss` → `FASM2008`.
- Las secciones personalizadas quedan para fase 2; en v1 solo estas cuatro.
- El linker ordena `.execute`, `.rodata`, `.data`, `.bss`, con alineación mínima de 16 bytes.

---

## D-016 — Símbolos: nombres, etiquetas y referencias

**Decisión.**

| Símbolo | Declaración | Referencia | Significado |
|---|---|---|---|
| Rutina | `routine nombre { ... }` | `nombre` en contexto de dirección | Dirección de código |
| Etiqueta local | `@nombre:` | `@nombre` | Dirección de código local |
| Dato | `data nombre: Byte:N` | `nombre` = dirección; `Byte:N [nombre]` = contenido | Dirección de memoria |
| Parámetro de registro | `parameter nombre: Gpr` | `nombre` | El registro (valor/lvalue) |
| Parámetro de pila | `parameter nombre: Stack` | `Byte:N [nombre]` | Dirección del slot |
| Local | `local nombre: Byte:N` | `Byte:N [nombre]` | Dirección del slot |

**Regla:** un identificador **desnudo** es una dirección o un alias de registro; el **contenido** de memoria siempre se escribe con capacidad y corchetes: `Byte:N [símbolo]`. Así no hay ambigüedad entre "la dirección" y "lo que hay en la dirección".

**Justificación.** Es la convención física: una etiqueta es una dirección; un dato es una dirección con una extensión; un parámetro de registro es un registro.

**Consecuencias.**
- `Gpr0.loadAddress(dato)` carga la dirección.
- `Gpr0.copy(Byte:8 [dato])` carga el contenido.
- `Ipr.flowCall(rutina)` usa la dirección de la rutina.
- `@etiqueta` solo para etiquetas locales; las rutinas y datos usan su nombre desnudo.
- Los nombres de usuario son `camelCase`; los nombres de registros son `PascalCase` + índice.

---

## D-017 — SIMD: contenedor de 512 bits, vistas por ancho, particiones por elemento

**Decisión.**

- `Simd0`–`Simd31` son contenedores físicos de 512 bits.
- Vistas de ancho: `.r128`, `.r256`, `.r512` (por defecto `.r512`).
- Vistas escalares: `.low32`, `.low64` (para transferencias con GPR).
- Partición: `.part{8|16|32|64}` declara el tamaño de elemento. **Solo esos cuatro valores** (tabla fija del hardware).
- El número de elementos es **derivado** (`ancho / partición`); no se declara.
- La interpretación va en el verbo: `.addInt`, `.addFloat`, etc.
- Las máscaras AVX-512 son `Krf0`–`Krf7`; se aplican al verbo: `.addFloat{Krf1}(...)`, `.addFloat{Krf1.z}(...)`.

**Justificación.** Es la forma más corta y física de decir "trata este registro de 512 bits como 16 trozos de 32 bits y suma flotantes". Separa las tres decisiones reales (ancho, tamaño de trozo, interpretación) sin tipos vectoriales abstractos.

**Consecuencias.**
- `part{10}`, `part{24}`, `part{Bit:10}` → `FASM3087`.
- `.r512` en un target sin AVX-512 → `FASM3080`.
- `Simd16`–`Simd31` requieren AVX-512 → `FASM3081`.
- Escribir `.r128`/`.r256` con VEX zero-extiende los bits altos; el compilador elige VEX/EVEX y documenta el efecto (D-004).
- `Krf0` como máscara de escritura → `FASM3083` (K0 significa "sin máscara" en la mayoría de opcodes).

---

## D-018 — Máscaras: `Krf0`–`Krf7`

**Decisión.** El archivo de máscaras AVX-512 se llama `Krf` (K Register File). Cada registro es de 64 bits, con vistas `.low8`, `.low16`, `.low32`, `.low64` según el ancho de máscara que use el opcode.

```fasm
Krf0.copy(Krf1)                         // kmovq
Krf0.low16.copy(Gpr0.low16)             // kmovw
Simd0.r512.part{32}.addFloat{Krf1}(Simd1.r512.part{32})
```

**Justificación.** `Krf` sigue la regla de tres letras y revela que es un archivo de registros, no un tipo. `Mrf` (Mask Register File) es igual de válido, pero `Krf` conserva la K histórica del hardware y evita confundirlo con "memory".

**Consecuencias.**
- `K0`–`K7` son alias prohibidos → `FASM3012`.
- `Krf0` como predicado → `FASM3083`.
- El ancho de la vista debe coincidir con el ancho de máscara del verbo → `FASM3088`.

---

## D-019 — x87: `Xrf0`–`Xrf7`

**Decisión.** La pila x87 se modela como `Xrf0`–`Xrf7` (X Register File), donde `Xrf0` es el tope lógico (`ST(0)`) y `Xrf7` el fondo (`ST(7)`). El puntero `TOP` es implícito y se consulta en `Xrf.status`.

- `Xrf.loadFloat(Byte:N [mem])` / `Xrf.loadInt(...)` hacen push.
- `Xrf0.storeFloatPop(Byte:N [mem])` / `.storeIntPop(...)` hacen store + pop.
- `Xrf0.addFloat(Xrf1)`, `.multiplyFloat`, `.divideFloat`, `.compareFloat`, `.squareRoot`, etc.
- `Xrf.control` (FCW), `Xrf.status` (FSW), `Xrf.tag` (FTW).
- `Xrf0.exchange(Xrf1)` (FXCH).
- `Xrf0.free()` (FFREE); `Xrf.rotateUp()` / `.rotateDown()` (FINCSTP/FDECSTP).
- MMX (`MM0`–`MM7`) está prohibido → `FASM3073`.

**Justificación.** x87 es una pila, no un archivo plano. Modelarlo como pila evita el error clásico de tratar `ST(i)` como un registro fijo. Los índices `XrfN` son **posiciones desde el tope**, que es exactamente lo que codifica el hardware.

**Consecuencias.**
- `ST(0)`…`ST(7)` son alias prohibidos → `FASM3012`.
- La profundidad de pila se verifica estáticamente cuando el flujo es recto; si no, se emite `FASM3071`/`FASM3072` como advertencia.
- `XrfN` no tiene vista `.low64` utilizable como entero; para eso está SSE/AVX.

---

## D-020 — ABI: Win64 por defecto, System V opt-in

**Decisión.** La ABI se declara siempre (`program.abi(...)` o `routine.abi(...)`); nunca se adivina. El valor por defecto es **Win64**.

- Parámetros enteros/punteros: `Gpr1`, `Gpr2`, `Gpr8`, `Gpr9` (Win64); `Gpr7`, `Gpr6`, `Gpr2`, `Gpr1`, `Gpr8`, `Gpr9` (SysV).
- Parámetros SIMD: `Simd0`–`Simd3` (Win64); `Simd0`–`Simd7` (SysV).
- Retorno: `Gpr0` (entero) o `Simd0` (vector), declarado con `.returns(Gpr|Simd)`.
- Shadow space: 32 bytes en Win64, declarado con `.shadow(1)`.
- Red zone: 128 bytes en SysV, declarada con `.redzone(1)`.
- Alineación de pila: 16 bytes antes de `flowCall`.
- Callee-saved: se declaran con `.preserves(...)`; el compilador no adivina qué registros usa la rutina.

**Justificación.** La ABI es un contrato físico. Declararla evita que el compilador "adivine" y permite verificar llamadas y retornos.

**Consecuencias.**
- `parameter a: Gpr` / `parameter v: Simd` / `parameter s: Stack` declaran la clase de paso.
- Una rutina con `.returns(Gpr)` debe escribir `Gpr0` antes de cada `flowStackJump` (lint `FASM5007`).
- `.naked(1)` desactiva prólogo/epílogo automático; el programador es responsable de todo.
- `.preserves(...)` genera push/pop alrededor del cuerpo, en el orden declarado.

---

## D-021 — Prólogo/epílogo declarado, no inferido

**Decisión.** El compilador genera prólogo/epílogo **solo** a partir de atributos explícitos:

```fasm
routine foo.abi(win64).frame(Byte:32).preserves(Gpr3, Gpr12).shadow(1) {
    ...
}
```

- `.frame(Byte:N)` reserva `N` bytes de locales y alinea la pila.
- `.preserves(GprA, GprB, ...)` guarda/restaura esos registros.
- `.shadow(1)` añade los 32 bytes de shadow space de Win64.
- `.naked(1)` no genera nada.
- Sin atributos, la rutina no tiene prólogo (leaf mínima).

**Justificación.** El programador sabe qué registros usa y cuánta pila necesita. Adivinarlo exigiría análisis de liveness y haría el binario dependiente de optimizaciones.

**Consecuencias.**
- El orden exacto del prólogo se documenta en [12-ABI](12-ABI.md).
- `Stack.reserve`/`Stack.release` permiten ajustes manuales dentro del cuerpo; el compilador rastrea el delta de RSP por bloque básico.
- Un `flowCall` con RSP desalineada → `FASM5003`.
- Un `flowStackJump` con frame sin restaurar → `FASM5004`.

---

## D-022 — Política de literales: forma permitida, dato prohibido

**Decisión.** Un literal solo es legal si el **contexto semántico** lo declara como **forma**:

| Forma (permitida) | Dato (prohibido en `.execute`) |
|---|---|
| Índice de registro (`Gpr0`, `Simd3`) | Valor de payload (`42`, `3.14`, `'A'`) |
| Capacidad (`Byte:8`, `KiloByte:4`) | Máscara aritmética (`0xFF00`) |
| Desplazamiento/cuenta (`Bit:3`, `shiftLeft(2)`) | Código de retorno (`42`) |
| Índice de bit (`testBit(7)`) | Texto (`"hola"`) |
| Alineación (`align Byte:16`) | Número de servicio inventado |
| Partición (`part{32}`) | |
| Estado de flag (`Frf.carry(1)`) | |
| Dirección/símbolo (`@tabla`, `Msr.fsBase`, `Port[0x60]`) | |
| Selector de sección/vista (`r512`, `.low32`) | |

El detalle y la tabla de posiciones están en [06-LITERALES](06-LITERALES.md).

**Bootstrap.** Los datos pueden existir en `.rodata`/`.data` (D-015) o entrar desde hardware/memoria/I/O/recurso externo. Esta es la única válvula de escape y se marca para revisión (D-1).

**Consecuencias.**
- `Gpr0.copy(Bit:42)` → `FASM2007` (dato en `.execute`).
- `data x: Byte:8 = 42` → permitido en `.rodata`/`.data`, con aviso `FASM8101` (bootstrap).
- `data x: Byte:8 = 42` en `.execute` o `.bss` → error.
- Los verbos `clear`, `increment`, `decrement` cubren los casos estructurales sin literales.

---

## D-023 — Sin nombres legacy; migración automática

**Decisión.** La superficie no acepta nombres legacy. El compilador emite `FASM3012` con el nombre canónico sugerido. La herramienta `fasm migrate` reescribe archivos antiguos.

**Justificación.** Un lenguaje que acepta dos nombres para el mismo registro duplica el vocabulario, confunde al IntelliSense y perpetúa el modelo mental antiguo.

**Consecuencias.**
- Tabla completa en [19-MIGRACIÓN](19-MIGRACION.md).
- El analizador léxico reconoce los alias solo para poder emitir el diagnóstico y la corrección; no los acepta como código válido.
- `mov`, `ret`, `call`, `jmp`, `push`, `pop`, `cmp` también son alias prohibidos: los verbos canónicos son `copy`, `Ipr.flowStackJump`, `Ipr.flowCall`, `Ipr.flowJump`, `Stack.push`, `Gpr0.pop(Stack)`, `compare`.

---

## D-024 — Perfiles de target

**Decisión.** El atributo `.target(...)` declara el conjunto de instrucciones disponibles:

| Perfil | Baseline | SIMD máximo | Notas |
|---|---|---|---|
| `x64-sse2` | x86-64 + SSE2 | `.r128` | Cualquier x64 moderno |
| `x64-avx2` | + AVX2, BMI1/2, FMA | `.r256` | Consumo típico 2013+ |
| `x64-avx512` | + AVX-512F/BW/DQ/VL | `.r512` | Servidores y CPUs recientes |
| `x64-avx512full` | + AVX-512 completo, AMX (futuro) | `.r512` | Fase 4 |

**Justificación.** El mismo fuente puede apuntar a un CPU antiguo o a uno con AVX-512. Declarar el perfil permite diagnósticos tempranos y evita instrucciones ilegales en tiempo de ejecución.

**Consecuencias.**
- Usar una instrucción fuera del perfil → `FASM6001`.
- `Simd16`–`Simd31` y `.r512` requieren `x64-avx512` o superior.
- El perfil por defecto es `x64-avx2` (AVX2), el mínimo práctico para SIMD moderno sin exigir AVX-512.

---

## D-025 — Sin optimización en v1

**Decisión.** El compilador v1 no optimiza:

- No reordena instrucciones.
- No elimina código muerto.
- No asigna registros.
- No alinea bucles automáticamente.
- No convierte secuencias en instrucciones más complejas.

Solo elige la **codificación más corta** de cada verbo (D-013).

**Justificación.** La optimización oculta cambios de estado y dificulta el aprendizaje y la depuración. Un ensamblador debe ser predecible. Las optimizaciones podrán añadirse después como pases explícitos (`--optimize=scheduling`) con mapa de transformación.

**Consecuencias.**
- El pipeline termina en `encode`; no hay pases posteriores.
- Los tests de bytes son estables.
- La documentación de cada verbo muestra la secuencia exacta que se emite.

---

## D-026 — Diagnósticos estables y accionables

**Decisión.** Cada restricción tiene un código `FASMxxxx` estable, una severidad y una corrección sugerida. Los códigos no se reutilizan ni se renumeran.

**Justificación.** Los códigos son parte de la interfaz del compilador: aparecen en el editor, en CI y en la documentación. Un código estable permite buscar, suprimir y testear.

**Consecuencias.**
- Registro completo en [13-DIAGNÓSTICOS](13-DIAGNOSTICOS.md).
- Los códigos existentes (`FASM3002`, `FASM3012`, `FASM4002`, etc.) se conservan.
- Los nuevos se asignan por rango (1xxx léxico, 2xxx símbolos, 3xxx ATM/registros, 4xxx operandos, 5xxx ABI, 6xxx target, 7xxx FP/SIMD, 8xxx estilo, 9xxx interno).

---

## D-027 — IntelliSense guiado por el receptor

**Decisión.** El completado se calcula a partir del **tipo del receptor** y del contexto sintáctico:

| Receptor | Sugerencias |
|---|---|
| `GprN` | `.copy`, `.add`, `.sub`, `.multiply`, `.and`, `.or`, `.xor`, `.shiftLeft`, `.compare`, `.test`, `.loadAddress`, `.clear`, `.increment`, `.decrement`, vistas `.low32`… |
| `GprN.low32` | Operaciones de 32 bits + `.zeroExtend`? (no: la vista ya zero-extiende) |
| `Frf` | `.carry(1|0|not)`, `.direction`, `.interrupt`, `.zero`, `.sign`, … |
| `Ipr` | `.flowCall`, `.flowJump`, `.flowStackJump`, `.flowLoop`, `.current` |
| `SimdN.r512.part{32}` | `.addInt`, `.addFloat`, `.multiplyInt`, `.multiplyFloat`, `.compareEqualInt`, `.convertToInt`, … |
| `KrfN` | `.copy`, `.and`, `.or`, `.xor`, `.not`, `.test` |
| `XrfN` | `.addFloat`, `.multiplyFloat`, `.compareFloat`, `.squareRoot`, `.exchange`, `.storeFloatPop` |
| `Stack` | `.push`, `.reserve`, `.release`, `.align`, `.pointer` (el `pop` va en el destino: `Gpr0.pop(Stack)`) |
| `Routine` (cabecera) | `.abi`, `.frame`, `.preserves`, `.shadow`, `.redzone`, `.naked`, `.align`, `.returns` |

**Consecuencias.**
- El LSP necesita el AST tipado, no solo tokens.
- Los verbos inválidos para un receptor se muestran deshabilitados con el motivo (`FASM3001`).
- El hover muestra la operación física, los flags afectados y el encoding esperado.
- Detalle en [18-INTELLISENSE](18-INTELLISENSE.md).

---

## D-028 — Identificadores en inglés, prosa en español

**Decisión.** Los nombres canónicos del lenguaje (registros, verbos, atributos, tipos) son **ingleses**, en `camelCase` o `PascalCase` según corresponda. La documentación, los ejemplos explicativos y los mensajes de diagnóstico pueden estar en español.

**Justificación.** Los términos físicos (`carry`, `overflow`, `shift`, `broadcast`) son universales y coinciden con los manuales. Traducirlos rompería la búsqueda y la familiaridad. La prosa en español mantiene el proyecto accesible para su autor.

**Consecuencias.**
- `Gpr0.shiftLeft`, no `Gpr0.desplazarIzquierda`.
- `Frf.overflow`, no `Frf.desbordamiento`.
- Los nombres de usuario pueden ser español (`factorial`, `sumar`) y se conservan.
- El compilador puede localizar mensajes sin cambiar los identificadores.

---

## D-029 — Sin llamadas externas en v1; syscalls sí

**Decisión.** En v1, `Ipr.flowCall` solo puede apuntar a rutinas definidas en el mismo programa. Las llamadas a bibliotecas externas (`FASM5001`) quedan para el futuro. Las llamadas al sistema se hacen con verbos explícitos (`Ipr.flowSyscall()`, `Ipr.flowInterrupt(...)`), no con `flowCall`.

**Justificación.** Un binario plano sin linker dinámico no puede resolver símbolos externos. Las syscalls son instrucciones, no enlaces.

**Consecuencias.**
- El linker resuelve todas las referencias internamente.
- `flowCall` a un símbolo no definido → `FASM2003`.
- Las syscalls requieren declarar el sistema operativo y la ABI en el programa.

---

## D-030 — Imagen plana, posición independiente

**Decisión.** La salida por defecto es un binario plano `.x64` sin cabecera:

- Secciones en orden `.execute`, `.rodata`, `.data`, `.bss`.
- La rutina de entrada se coloca al inicio de `.execute` (o se emite un salto de 5 bytes).
- Todo direccionamiento de código y datos es RIP-relativo (PIC).
- No hay relocaciones dinámicas; las referencias se resuelven en tiempo de ensamblado.
- El cargador reserva memoria ejecutable, copia los bytes y salta a la entrada.

**Justificación.** Es el formato más simple para empezar el compilador y probar el código en un entorno controlado. Los formatos ELF/PE quedan para fases posteriores.

**Consecuencias.**
- Los punteros absolutos almacenados en datos requieren una base conocida → `FASM5008`.
- El compilador puede emitir un mapa de símbolos (`--map`) para depuración.
- La entrada se documenta en [05-PROGRAMA](05-PROGRAMA.md) y [16-CODIFICACIÓN](16-CODIFICACION.md).

---

## Decisiones rechazadas

| Idea | Por qué se rechaza |
|---|---|
| Tipos `Int32`, `Float64`, `Bool`, `String`, `struct` | Ocultarían el hecho físico de que todo son bits; la interpretación va en el verbo (D-003). |
| Registros virtuales / SSA expuestos | El programador controla registros físicos; no hay allocator (D-012). |
| *Builder* / contexto / `.commit()` | El receptor debe ser el destino real, no un objeto temporal (D-005). |
| `mov` como verbo canónico | "Move" sugiere mover, pero el hardware copia; `copy` es honesto (D-004). |
| Aceptar nombres legacy en modo compatibilidad | Duplica el vocabulario y confunde al IntelliSense (D-023). |
| Tamaños SIMD arbitrarios (`part{10}`) | No existen opcodes; el hardware solo tiene 8/16/32/64 (D-017). |
| `XMM/YMM/ZMM` como nombres canónicos | Ocultan que son vistas de un contenedor de 512 bits (D-006, D-017). |
| `rFlags` como entero | Permite combinaciones inválidas y oculta qué flag cambia cada instrucción (D-008). |
| x86 de 32 bits | El proyecto es x64 nativo; x86 añadiría un backend y un modelo de direccionamiento distintos. |
| Optimización automática en v1 | Rompe la reproducibilidad y la honestidad (D-025). |
| Datos literales en instrucciones | Es la negación del dataflow (D-001, D-022). |

---

## Cómo cambiar una decisión

1. Abrir un issue con el identificador (`D-0xx`) y el caso de uso que la motiva.
2. Actualizar este documento con la nueva decisión y la fecha.
3. Buscar referencias en los demás documentos (`grep "D-0xx"`) y actualizarlas.
4. Añadir o ajustar los diagnósticos afectados.
5. Añadir un test de aceptación en [15-EJEMPLOS](15-EJEMPLOS.md) que cubra el cambio.

Las decisiones **D-1…D-5** del [README](README.md#7-decisiones-pendientes-de-confirmación) son las únicas que aún no están cerradas por el autor; el resto se consideran normativas para la versión 1.0.
