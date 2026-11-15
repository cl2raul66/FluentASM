# 20 — Glosario

> **Informativo.** Vocabulario, nombres canónicos, alias prohibidos y tablas de consulta rápida. Si una palabra de este glosario contradice un documento normativo, manda el documento normativo.

---

## 1. Términos del modelo

| Término | Definición | Documento |
|---|---|---|
| **ATM** | *Abstract Type Model*: el modelo de contenedores y formas del lenguaje. Solo `Bit:N` y `Byte:N`. | 02 |
| **Acción** | Una sentencia de la forma `receptor.verbo(fuente)`. Es la unidad ejecutable. | 04 |
| **Bootstrap** | Isla de datos iniciales en `.rodata`/`.data`, permitida pero marcada (`FASM8101`). | 06 |
| **Capacidad** | Tamaño de un acceso a memoria: `Byte:8`, `KiloByte:4`, … Es **forma**, no dato. | 02 |
| **Clobber** | Registro que una instrucción destructivamente modifica sin que sea su destino declarado (`copyBlock`, `flowSyscall`). | 07 |
| **Contenedor** | Estado físico donde viven bits: registro, memoria, flags, pila, MSR, puerto. | 02 |
| **Dato** | Contenido que circula. Prohibido en `.execute` como literal. | 06 |
| **Destino** | El receptor de una acción; el contenedor que se escribe. | 04 |
| **Flujo** | Cambio de `Ipr`; solo ocurre por verbos `flow*`. | 08 |
| **Forma** | Número que describe estructura (capacidad, partición, cuenta, alineación, estado de flag), no contenido. | 06 |
| **Fuente** | El argumento de una acción; de donde vienen los bits. | 04 |
| **Imagen plana** | Salida binaria sin cabecera por defecto, cargada en una dirección base. | 16 |
| **Máscara** | Registro `Krf` que selecciona elementos de una operación SIMD. | 10 |
| **Partición** | Tamaño de elemento SIMD: `part{8}`, `part{16}`, `part{32}`, `part{64}`. | 10 |
| **Payload** | Bytes iniciales de un `data` en `.rodata`/`.data`. | 06 |
| **Receptor** | El contenedor que precede al verbo y que recibe la operación. | 04 |
| **Relajación** | Reducción de un salto de `rel32` a `rel8` cuando cabe. | 16 |
| **Tupla** | Destino múltiple `(A:B)` para instrucciones que escriben dos o más registros. | 04 |
| **Verbo** | La operación física que se aplica al receptor. | 07 |
| **Vista** | Ventana sobre un contenedor: `.low32`, `.r256`, `.part{32}`, `.base`. | 03 |
| **Cuña** | 8 bytes de relleno que alinean `RSP` cuando `|preserves|` es impar. | 09 |

---

## 2. Nombres canónicos de registro

### 2.1 GPR — `Gpr0`–`Gpr15`

| Canónico | Legacy | Rol principal |
|---|---|---|
| `Gpr0` | RAX | retorno de función; acumulador |
| `Gpr1` | RCX | parámetro 1 (Win64); contador de `flowLoop`; cuenta de desplazamiento |
| `Gpr2` | RDX | parámetro 2 (Win64); mitad alta de multiplicación/división |
| `Gpr3` | RBX | callee-saved |
| `Gpr4` | RSP | puntero de pila; **no** puede ser índice SIB |
| `Gpr5` | RBP | puntero de marco |
| `Gpr6` | RSI | origen de bloques |
| `Gpr7` | RDI | destino de bloques |
| `Gpr8` | R8 | parámetro 3 (Win64) |
| `Gpr9` | R9 | parámetro 4 (Win64) |
| `Gpr10` | R10 | volátil |
| `Gpr11` | R11 | volátil; `syscall` lo destruye |
| `Gpr12` | R12 | callee-saved |
| `Gpr13` | R13 | callee-saved |
| `Gpr14` | R14 | callee-saved |
| `Gpr15` | R15 | callee-saved |

Vistas: `.low8`, `.low16`, `.low32`, `.low64`, `.high8` (solo `Gpr0`–`Gpr3`).

### 2.2 Segmentos — `Srf0`–`Srf5`

| Canónico | Legacy | Campo `sreg` |
|---|---|---|
| `Srf0` | CS | 1 |
| `Srf1` | DS | 3 |
| `Srf2` | ES | 0 |
| `Srf3` | FS | 4 |
| `Srf4` | GS | 5 |
| `Srf5` | SS | 2 |

Vista: `.base` (requiere `FSGSBASE` para `Srf3`/`Srf4`).

### 2.3 Control — `Crf0`–`Crf4`, `Crf8`

| Canónico | Legacy |
|---|---|
| `Crf0` | CR0 |
| `Crf1` | CR1 (reservado) |
| `Crf2` | CR2 |
| `Crf3` | CR3 |
| `Crf4` | CR4 |
| `Crf8` | CR8 |

### 2.4 Depuración — `Drf0`–`Drf7`

`Drf0`–`Drf3`: breakpoints. `Drf4`/`Drf5`: obsoletos. `Drf6`: breakpoint extendido. `Drf7`: dirección de depuración.

### 2.5 SIMD — `Simd0`–`Simd31`

Contenedor físico de 512 bits. Vistas: `.r128`, `.r256`, `.r512`, `.part{8|16|32|64}`, `.element(k)`.

### 2.6 Máscaras — `Krf0`–`Krf7`

`Krf0` no puede ser máscara de escritura (`FASM3083`). Vistas: `.low8`, `.low16`, `.low32`, `.low64`.

### 2.7 x87 — `Xrf0`–`Xrf7`

Pila de 8 posiciones. Vistas de estado: `Xrf.status`, `Xrf.control`, `Xrf.tag`. `Xrf0` es el tope (`ST(0)`).

### 2.8 Otros

| Canónico | Qué es |
|---|---|
| `Frf` | registro de flags unificado (`CF`, `ZF`, `SF`, `OF`, `PF`, `AF`, `DF`, `IF`, `IOPL`, …) |
| `Ipr` | puntero de instrucción (`RIP`); solo cambia por `flow*` |
| `Xcr0` | registro de control extendido (AVX/AVX-512 state) |
| `Pkru` | claves de protección de memoria |
| `Ssp` | shadow stack pointer (CET) |
| `Msr.<nombre>` | modelo específico de registro (`Msr.efer`, `Msr.gsBase`, …) |
| `Port[addr]` | puerto de E/S |
| `Stack` | unidad de pila: `Stack.push`, `Stack.reserve`, `Stack.release`, `Stack.pointer`, `Stack.framePointer` |
| `Cpu` | unidad de CPU: `Cpu.halt`, `Cpu.noOperation`, `Cpu.identify`, `Cpu.readTimestamp`, … |
| `Fence` | unidad de ordenación de memoria: `Fence.full`, `Fence.load`, `Fence.store` |

---

## 3. Alias prohibidos

Cualquier aparición produce `FASM3012` con el nombre canónico en la corrección.

| Prohibido | Canónico |
|---|---|
| `Rax`, `Eax`, `Ax`, `Al`, `Ah` | `Gpr0`, `.low32`, `.low16`, `.low8`, `.high8` |
| `Rbx` … `R15`, `Rsp`, `Rbp`, `Rsi`, `Rdi` | `Gpr3` … `Gpr15` |
| `Rflags`, `rFlags`, `EFLAGS` | `Frf` |
| `Rip`, `EIP` | `Ipr` |
| `CS`, `DS`, `ES`, `FS`, `GS`, `SS` | `Srf0` … `Srf5` |
| `CR0` … `CR8` | `Crf0` … `Crf8` |
| `DR0` … `DR7` | `Drf0` … `Drf7` |
| `XMM0` … `XMM31` | `Simd0.r128` … |
| `YMM0` … `YMM31` | `Simd0.r256` … |
| `ZMM0` … `ZMM31` | `Simd0.r512` … |
| `MM0` … `MM7` | no existen (`FASM3073`) |
| `K0` … `K7` | `Krf0` … `Krf7` |
| `ST(0)`, `ST0`, `ST(7)` | `Xrf0`, `Xrf7` |
| `mov` | `copy` |
| `add` / `sub` / `and` / `or` / `xor` | `add` / `sub` / `and` / `or` / `xor` (mismo nombre, distinta forma de llamada) |
| `cmp` | `compare` |
| `call` | `Ipr.flowCall` |
| `ret` | `Ipr.flowStackJump` |
| `jmp` | `Ipr.flowJump` |
| `jz` / `je` / `jnz` / `jne` / `jl` / `jg` / … | `Ipr.flowJump(... if …)` |
| `int` | `Ipr.flowInterrupt` |
| `syscall` | `Ipr.flowSyscall` |
| `lea` | `loadAddress` |
| `movzx` / `movsx` | `loadUnsigned` / `loadSigned` |
| `imul` | `multiplyLow` (truncado) o `multiplySigned` (128) |
| `idiv` | `divideSigned` |
| `shl` / `shr` / `sar` | `shiftLeft` / `shiftRightLogical` / `shiftRightArithmetic` |
| `nop` | `Cpu.noOperation()` |
| `hlt` | `Cpu.halt()` |

---

## 4. Verbos por familia

### 4.1 Transferencia

`copy`, `exchange`, `loadAddress`, `loadUnsigned`, `loadSigned`, `loadFloat`, `loadInt`, `storeFloat`, `storeFloatPop`, `storeInt`, `storeIntPop`, `conditionalCopy`, `broadcast`, `extract`, `insert`.

### 4.2 Aritmética

`add`, `sub`, `addWithCarry`, `subWithBorrow`, `increment`, `decrement`, `negate`, `multiply`, `multiplySigned`, `multiplyLow`, `divide`, `divideSigned`, `signExtend`, `extendSigned`, `extendUnsigned`, `truncate`, `compare`, `compareSigned`, `addInt`, `addFloat`, `subInt`, `subFloat`, `multiplyInt`, `multiplyFloat`, `divideFloat`.

### 4.3 Lógica y bits

`and`, `or`, `xor`, `not`, `andNot`, `test`, `testBit`, `setBit`, `clearBit`, `complementBit`, `setBits`, `extractBits`, `insertBits`, `reverseBits`, `populationCount`, `countLeadingZeros`, `countTrailingZeros`, `scanForward`, `scanReverse`.

### 4.4 Desplazamientos y rotaciones

`shiftLeft`, `shiftRightLogical`, `shiftRightArithmetic`, `shiftLeftDouble`, `shiftRightDouble`, `shiftLeftBytes`, `rotateLeft`, `rotateRight`, `rotateLeftThroughCarry`, `rotateRightThroughCarry`.

### 4.5 Pila

`Stack.push`, `Stack.reserve`, `Stack.release`, `Stack.pointer`, `Stack.framePointer`, `enter`, `leave`. El `pop` va en el destino: `Gpr0.pop(Stack)`, `Xrf0.storeFloatPop(...)`.

### 4.6 Flags

`Frf.carry(state)`, `Frf.zero(state)`, `Frf.sign(state)`, `Frf.overflow(state)`, `Frf.parity(state)`, `Frf.direction(state)`, `Frf.interrupt(state)`, `Frf.trap(state)`, `Frf.iopl`. Para manipular la pila de flags: `Stack.push(Frf)`, `Frf.pop(Stack)`.

### 4.7 Flujo

`Ipr.flowJump`, `Ipr.flowCall`, `Ipr.flowStackJump`, `Ipr.flowLoop`, `Ipr.flowInterrupt`, `Ipr.flowSyscall`, `Ipr.flowSysret`, `Ipr.flowBreakpoint`, `Ipr.flowOverflow`, `Ipr.current`.

### 4.8 Memoria

`copyBlock`, `fillBlock`, `compareBlock`, `scanBlock`, `loadBlock`, `storeBlock`, `copyNonTemporal`, `flushCacheLine`, `prefetch`, `prefetchnta`, `serialize`, `fence`.

### 4.9 Átomos

`addLocked`, `exchange`, `compareExchange`, `cmpxchg16b`.

### 4.10 SIMD / x87

Ver [10-SIMD](10-SIMD.md) §5–§9 y [11-X87](11-X87.md) §4–§7.

### 4.11 CPU, MSR y E/S

`Cpu.halt`, `Cpu.noOperation`, `Cpu.pause`, `Cpu.identify`, `Cpu.readTimestamp`, `Cpu.readTimestampSerialized`, `Cpu.readRandom`, `Cpu.readRandomSeeded`, `Cpu.alignmentCheck`, `Msr.read`, `Msr.write`, `Port.read`, `Port.write`, `Xcr0.read`, `Xcr0.write`.

---

## 5. Condiciones y flags

| Alias | Expansión |
|---|---|
| `Frf.equal` | `Frf.zero` |
| `Frf.notEqual` | `not Frf.zero` |
| `Frf.less` | `Frf.sign unequal Frf.overflow` |
| `Frf.greaterOrEqual` | `Frf.sign equal Frf.overflow` |
| `Frf.lessOrEqual` | `Frf.zero or Frf.sign unequal Frf.overflow` |
| `Frf.greater` | `not Frf.zero and Frf.sign equal Frf.overflow` |
| `Frf.below` | `Frf.carry` |
| `Frf.aboveOrEqual` | `not Frf.carry` |
| `Frf.belowOrEqual` | `Frf.carry or Frf.zero` |
| `Frf.above` | `not Frf.carry and not Frf.zero` |

---

## 6. Capacidades

| Unidad | Bits | Bytes |
|---|---:|---:|
| `Bit:1` | 1 | — |
| `Bit:8` | 8 | 1 |
| `Bit:16` | 16 | 2 |
| `Bit:32` | 32 | 4 |
| `Bit:64` | 64 | 8 |
| `Bit:80` | 80 | 10 |
| `Bit:128` | 128 | 16 |
| `Bit:256` | 256 | 32 |
| `Bit:512` | 512 | 64 |
| `KiloBit:1` | 1024 | 128 |
| `KiloByte:1` | 8192 | 1024 |
| `MegaByte:1` | 8 388 608 | 1 048 576 |
| `GigaByte:1` | 2³³ | 2³⁰ |
| `TeraByte:1` | 2⁴³ | 2⁴⁰ |

`Bit` se usa en registros y máscaras; `Byte` en memoria y capacidades. Mezclarlos en el contexto equivocado es `FASM3020`/`FASM3021`.

---

## 7. Siglas

| Sigla | Significado |
|---|---|
| **ABI** | *Application Binary Interface*: convención de llamada (Win64, System V) |
| **ATM** | *Abstract Type Model* (doc 02) |
| **CFG** | *Control Flow Graph*: grafo de bloques que construye el checker |
| **EVEX** | prefijo de 4 bytes de AVX-512 |
| **GPR** | *General Purpose Register* |
| **IR** | *Intermediate Representation*: instrucción ya seleccionada |
| **LSP** | *Language Server Protocol* |
| **MSR** | *Model-Specific Register* |
| **PIC** | *Position-Independent Code* |
| **REX** | prefijo de 1 byte que extiende registros y ancho en x64 |
| **SIB** | *Scale-Index-Base*: byte de direccionamiento |
| **SIMD** | *Single Instruction, Multiple Data* |
| **TSC** | *Time Stamp Counter* |
| **VEX** | prefijo de 2 o 3 bytes de AVX/AVX2 |

---

## 8. Convenciones de escritura

| Elemento | Convención | Ejemplo |
|---|---|---|
| Registro | `PascalCase` + índice | `Gpr0`, `Simd12`, `Krf3` |
| Archivo de registro | `PascalCase` + `.` + `camelCase` | `Msr.efer`, `Msr.gsBase` |
| Verbo | `camelCase` | `copy`, `shiftRightArithmetic`, `flowStackJump` |
| Vista | `camelCase` o `{n}` | `.low32`, `.part{32}`, `.base` |
| Sección | `.` + minúsculas | `.execute`, `.rodata`, `.bss` |
| Atributo | `.` + `camelCase` | `.abi(win64)`, `.frame(Byte:32)` |
| Rutina | `camelCase` | `suma`, `factorialDemo` |
| Label | `@` + `camelCase` | `@loop`, `@fin` |
| Símbolo de datos | `camelCase` | `mensaje`, `resultado` |
| Capacidad | `Unidad:N` | `Byte:16`, `Bit:3`, `KiloByte:4` |
| Constante estructural | forma explícita | `Bit:1`, `part{32}`, `Frf.carry(1)` |
| Comentario | `//` hasta fin de línea; `/* */` en bloque | — |

**Prohibido:** `snake_case` y `SCREAMING_CASE` en identificadores de usuario; solo `camelCase`; prefijos húngaros (`pBuffer`, `dwSize`); abreviaturas no canónicas (`reg`, `tmp`, `aux` como nombre de registro).

---

## 9. Diagnósticos por familia

| Rango | Familia | Documento |
|---|---|---|
| `FASM1001`–`FASM1006` | léxico | 13 §3 |
| `FASM1101`–`FASM1111` | sintaxis | 13 §4 |
| `FASM2001`–`FASM2030` | programa, símbolos, datos | 13 §5 |
| `FASM3001`–`FASM3089` | ATM, registros, SIMD, x87 | 13 §6 |
| `FASM4001`–`FASM4036` | operandos, verbos, flujo | 13 §7 |
| `FASM5001`–`FASM5013` | ABI | 13 §8 |
| `FASM6001`–`FASM6005` | target | 13 §9 |
| `FASM8001`–`FASM8107` | estilo y bootstrap | 13 §10 |
| `FASM9001`–`FASM9003` | interno | 13 §11 |

---

## 10. Frases que resumen el lenguaje

| Frase | Significado |
|---|---|
| «El receptor es el destino» | No hay `origen → destino`; se escribe `destino.verbo(origen)`. |
| «La interpretación es del verbo» | `add` vs `addFloat` vs `addInt`; el tipo no la lleva. |
| «Forma, no dato» | Lo que se escribe en `.execute` describe estructura, no contenido. |
| «Sin temporales ocultos» | Si hace falta un registro, lo declara el programador. |
| «Misma sintaxis, distinto contexto» | Cabecera fluent, cuerpo en bloque; ambas son `receptor.verbo(...)`. |
| «El IntelliSense es el receptor» | Tras `Gpr0.` solo aparecen verbos de GPR. |
| «Todo byte tiene un diagnóstico» | Cada restricción tiene código estable. |
| «No hay `mov`, hay `copy`» | El nombre dice qué hace: mover bits de un contenedor a otro. |

---

## 11. Resumen

- Un solo vocabulario, sin sinónimos: cada concepto tiene un nombre canónico y un documento.
- Los nombres legacy están listados para poder **rechazarlos** con precisión.
- Las tablas de registros, capacidades, flags y verbos son la referencia de consulta diaria.
- El glosario no introduce reglas: las resume y apunta a la fuente normativa.
