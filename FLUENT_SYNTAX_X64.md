# Referencia de sintaxis de FluentASM — x64 (Fase 1: *Bare Metal*)

> **Documento específico de arquitectura x64.** Define los registros físicos, modos de direccionamiento, convención de llamadas, reglas de codificación y el **Catálogo Mínimo Viable** para programar en x64 *bare metal* durante la Fase 1.
> Para la sintaxis común, estructura `Proyecto.data(...).execute(...).initialize()`, modelo `Bit`/`Byte` y reglas de flujo, véase [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md).


## 1. Alcance en Fase 1

Este documento cubre **únicamente** la base implementable de la **Fase 1** sobre x64 de 64 bits sin sistema operativo previo (*bare metal*, imagen plana `.x64`).

Las extensiones avanzadas del procesador (SIMD/AVX, coma flotante x87, registros de segmento/control/depuración/MSR, virtualización y ejecutables PE/ELF) quedan explícitamente diferidas a la §9 para fases posteriores.


## 2. Registros x64 (Fase 1)

### 2.1 Registros de Propósito General (`Gpr0`–`Gpr15`)

Cada `GprN` es una celda física de **64 bits**. Las vistas (`.low32`, `.low16`, `.low8`, `.high8`) son **ventanas sobre esa misma celda**, no registros independientes.

| Canónico | Ref. hardware (solo verificación) | Vistas válidas | Rol en Fase 1 |
|---|---|---|---|
| `Gpr0` | `RAX` | `Gpr0`, `.low32`, `.low16`, `.low8`, `.high8` | Retorno principal; acumulador en `multiplyWide`/`divide`; volátil |
| `Gpr1` | `RCX` | `Gpr1`, `.low32`, `.low16`, `.low8`, `.high8` | Entrada 1; contador en `flowLoop` / `copyBlock` / shift variable (`.low8`); volátil |
| `Gpr2` | `RDX` | `Gpr2`, `.low32`, `.low16`, `.low8`, `.high8` | Entrada 2; parte alta en `multiplyWide` y resto en `divide`; puerto en `Port[Gpr2.low16]`; volátil |
| `Gpr3` | `RBX` | `Gpr3`, `.low32`, `.low16`, `.low8`, `.high8` | Preservado por el llamado (*callee-saved*) |
| `Gpr4` | `RSP` | `Gpr4`, `.low32`, `.low16`, `.low8` | Puntero de pila (`Stack`); **no** usable como índice SIB |
| `Gpr5` | `RBP` | `Gpr5`, `.low32`, `.low16`, `.low8` | Puntero de marco; preservado (*callee-saved*) |
| `Gpr6` | `RSI` | `Gpr6`, `.low32`, `.low16`, `.low8` | Puntero origen en `copyBlock`; preservado (*callee-saved*) |
| `Gpr7` | `RDI` | `Gpr7`, `.low32`, `.low16`, `.low8` | Puntero destino en `copyBlock`/`fillBlock`; preservado (*callee-saved*) |
| `Gpr8` | `R8` | `Gpr8`, `.low32`, `.low16`, `.low8` | Entrada 3; volátil |
| `Gpr9` | `R9` | `Gpr9`, `.low32`, `.low16`, `.low8` | Entrada 4; volátil |
| `Gpr10` | `R10` | `Gpr10`, `.low32`, `.low16`, `.low8` | Volátil |
| `Gpr11` | `R11` | `Gpr11`, `.low32`, `.low16`, `.low8` | Volátil |
| `Gpr12`–`Gpr15` | `R12`–`R15` | `GprN`, `.low32`, `.low16`, `.low8` | Preservados (*callee-saved*) |

> **Nota:** Usar nombres históricos (`Rax`, `Eax`, `Rcx`, `R8d`, etc.) en código `.fasm` emite el error `FASM3012` con la sugerencia al nombre canónico.

### 2.2 Reglas físicas de las vistas

| Vista | Bits | Efecto al escribir sobre `GprN` |
|---|---|---|
| `GprN` | 0–63 | Reemplaza los 64 bits |
| `.low32` | 0–31 | **Zero-extend:** pone a cero los bits 32–63 |
| `.low16` | 0–15 | **Merge:** conserva los bits 16–63 |
| `.low8` | 0–7 | **Merge:** conserva los bits 8–63 |
| `.high8` | 8–15 | **Merge:** solo existe en `Gpr0`–`Gpr3` |

**Restricciones de hardware (verificadas por el compilador):**
- `Gpr4`–`Gpr15` **no** tienen `.high8` (`FASM3015`).
- `.low8` de `Gpr4`–`Gpr7` (`SPL`, `BPL`, `SIL`, `DIL`) exige prefijo `REX` (`0x40`). Una misma instrucción no puede mezclar `.high8` (`Gpr0`–`Gpr3`) con `.low8` de `Gpr4`–`Gpr7` (`FASM3002`).
- `Gpr4` (RSP) no puede usarse como índice en direccionamiento `[base + índice*escala]` (`FASM4003`).
- `Gpr5` (RBP) y `Gpr13` usados como base sin desplazamiento emiten automáticamente `disp8 = 0` en el codificador.


### 2.3 Registro de Banderas (`Frf`)

`Frf` representa el registro de banderas de 64 bits (`RFLAGS`) con acceso semántico a cada bit:

| Miembro | Bit | Significado físico | Quién lo actualiza |
|---|---|---|---|
| `Frf.carry` | 0 | Acarreo / préstamo sin signo (`CF`) | `add`, `sub`, `compare`, `shift*`, `rotate*`, `*Bit`, `Frf.carry(...)` |
| `Frf.parity` | 2 | Paridad par del byte bajo (`PF`) | Operaciones ALU estándar |
| `Frf.adjust` | 4 | Acarreo auxiliar BCD (`AF`) | `add`, `sub`, `compare` |
| `Frf.zero` | 6 | Resultado igual a cero (`ZF`) | Casi toda la ALU, `compare`, `test` |
| `Frf.sign` | 7 | Bit más significativo = 1 (`SF`) | Casi toda la ALU, `compare`, `test` |
| `Frf.interrupt` | 9 | Habilitación de interrupciones (`IF`) | `Frf.interrupt(1 \| 0)` |
| `Frf.direction` | 10 | Dirección de bloques (`DF`: 0=adelante, 1=atrás) | `Frf.direction(1 \| 0)` |
| `Frf.overflow` | 11 | Desbordamiento con signo (`OF`) | `add`, `sub`, `compare`, `multiply*` |

**Manipulación directa de banderas:**

```fasm
Frf.carry(1)        // STC — CF = 1
Frf.carry(0)        // CLC — CF = 0
Frf.carry(not)      // CMC — CF = !CF
Frf.direction(0)    // CLD — DF = 0 (ascendente)
Frf.direction(1)    // STD — DF = 1 (descendente)
Frf.interrupt(0)    // CLI — IF = 0 (deshabilita interrupciones en bare metal)
Frf.interrupt(1)    // STI — IF = 1 (habilita interrupciones)
```


### 2.4 Puntero de Instrucción (`Ipr`), Pila (`Stack`) y Máquina (`Cpu`, `Port`)

- **`Ipr`:** Puntero de instrucción (`RIP`). No se lee ni escribe con `.copy(...)` (`FASM3060`). Solo cambia mediante `Ipr.flow*` y se puede obtener su dirección actual con `Gpr0.copy(Ipr.current)` (`LEA RAX, [RIP]`).
- **`Stack`:** Unidad virtual sobre `Gpr4` (RSP) y la pila de memoria. Crece hacia abajo. Debe estar alineada a **16 bytes** en el momento de ejecutar `Ipr.flowCall(...)`.
- **`Cpu`:** Unidad física para instrucciones sin receptor de registro (`Cpu.noOperation()`, `Cpu.pause()`, `Cpu.halt()`).
- **`Port[...]`:** Espacio de direcciones de E/S hardware de 16 bits (`Port[0x03F8]` o `Port[Gpr2.low16]`), esencial para periféricos y consola serie en *bare metal*.


## 3. Modos de Direccionamiento x64

| Modo | Sintaxis FluentASM | Significado |
|---|---|---|
| Símbolo de `.data` (tamaño conocido) | `[numA]` | Contenido de `numA` (RIP-relativo) |
| Indirecto por registro | `Byte:8 [Gpr3]` | 8 bytes en la dirección que contiene `Gpr3` |
| Base + desplazamiento | `Byte:8 [Gpr5 - 16]` | 8 bytes en `Gpr5 - 16` |
| Base + índice × escala | `Byte:4 [Gpr3 + Gpr1*4]` | Elemento de 4 bytes en arreglo |
| Base + índice × escala + desp. | `Byte:8 [Gpr3 + Gpr1*8 + 16]` | Campo en arreglo de estructuras |
| Símbolo + índice | `Byte:8 [tabla + Gpr1*8]` | Tabla RIP-relativa indexada |
| Dirección efectiva | `Gpr0.loadAddress(tabla)` | Carga la dirección del símbolo (`LEA`), no su contenido |

**Reglas:**
1. La escala solo puede ser `1`, `2`, `4` u `8` (`FASM4004`).
2. Todo acceso por registro o expresión exige capacidad explícita (`Byte:1`, `Byte:2`, `Byte:4`, `Byte:8`) (`FASM3023`). Un símbolo declarado en `.data` ya conoce su capacidad (`[numA]`).
3. No existe copia directa memoria → memoria con `.copy(...)` (`FASM4026`); debe pasar por un `Gpr` o usar `copyBlock`.
4. Todo direccionamiento a símbolos es **relativo a `Ipr`** (código independiente de posición, PIC).


## 4. Convención de Llamadas x64 *Bare Metal* (Fase 1)

### 4.1 Mapeo de registros y contratos

| Rol | Registro | Notas |
|---|---|---|
| Entrada 1 (`arg1`) | `Gpr1` | Alias directo en `d.run((arg1<Byte:8>, ...) -> ...)` |
| Entrada 2 (`arg2`) | `Gpr2` | Alias directo |
| Entrada 3 (`arg3`) | `Gpr8` | Alias directo |
| Entrada 4 (`arg4`) | `Gpr9` | Alias directo |
| Salida (`d.returns`) | `Gpr0` | Valor devuelto |
| Volátiles | `Gpr0`, `Gpr1`, `Gpr2`, `Gpr8`–`Gpr11` | Pueden cambiar dentro de una rutina llamada |
| Preservados | `Gpr3`, `Gpr5`–`Gpr7`, `Gpr12`–`Gpr15` | Si una rutina los usa, debe guardarlos en `Stack` y restaurarlos |
| Pila (`Stack`) | `Gpr4` | Alineada a 16 bytes antes de `Ipr.flowCall(...)` |

### 4.2 Seguridad Tipo 1 en llamadas

- En `Ipr.flowCall(c -> c.target(sumar).inputs(Gpr1, Gpr2).output(Gpr0))`:
  1. El compilador verifica que `Gpr1` y `Gpr2` tengan datos válidos de ancho compatible antes de la llamada.
  2. El compilador verifica que `.output(Gpr0)` coincida con `d.returns(Gpr0)` de `sumar`.
  3. Al regresar de `flowCall`, `Gpr1` y `Gpr2` quedan **invalidados lógicamente** en el análisis del compilador (no se genera ninguna instrucción extra); intentar leerlos sin volver a cargarlos produce error `FASM5xxx`.
- Toda rutina termina explícitamente con `Ipr.flowStackJump()` (`RET`) o `Cpu.halt()` (`HLT`).


## 5. Catálogo Mínimo Viable x64 (Fase 1)

La columna **Flags** usa: `●` actualizado por la instrucción, `—` intacto, `?` indefinido.

### 5.1 Transferencia y Memoria

| Verbo | Sintaxis | Instrucción x64 | Flags | Notas |
|---|---|---|---|---|
| `copy` | `Gpr0.copy(Gpr1)` | `MOV r64, r/m64` (`48 89 /r` / `48 8B /r`) | — | Copia 64b (o vista `.low32`/`.low16`/`.low8`/`.high8`) |
| `copy` | `Gpr0.copy([numA])` | `MOV r64, [rip+d32]` (`48 8B 05 ...`) | — | Carga desde símbolo de `.data` |
| `copy` | `Byte:8 [Gpr3].copy(Gpr0)` | `MOV [r64], r64` (`48 89 /r`) | — | Almacena en memoria |
| `copy(defaultValue)` | `Gpr0.copy(defaultValue)` | `XOR r32, r32` (`31 C0+rd`) | ● `ZF=1,CF=0,OF=0` | Estado limpio a cero sin literal duro |
| `copyUnsigned` | `Gpr0.copyUnsigned(Byte:1 [Gpr3])` | `MOVZX r64, m8/m16` (`0F B6`/`B7`) | — | Extiende con ceros a 64 bits (para `Byte:4`, usar `.low32.copy(...)`) |
| `copySigned` | `Gpr0.copySigned(Byte:1 [Gpr3])` | `MOVSX` / `MOVSXD` (`48 0F BE`/`BF`/`63`) | — | Extiende conservando el bit de signo |
| `loadAddress` | `Gpr0.loadAddress(tabla)` | `LEA r64, [rip+d32]` (`48 8D 05 ...`) | — | Calcula la dirección efectiva |
| `exchange` | `Gpr0.exchange(Gpr1)` | `XCHG r/m64, r64` (`48 87 /r`) | — | Intercambia valores (con memoria es atómico) |
| `conditionalCopy` | `Gpr0.conditionalCopy(Gpr1 if Frf.equal)` | `CMOVcc r64, r/m64` (`48 0F 4x /r`) | — | Copia solo si la condición es cierta |
| `copyBlock` | `Byte:N [Gpr7].copyBlock(Byte:N [Gpr6])` | `MOV ECX, N; REP MOVSB` (`F3 A4`) | — | Copia `N` bytes; usa implícitamente `Gpr1`, `Gpr6`, `Gpr7` y `Frf.direction` |
| `fillBlock` | `Byte:N [Gpr7].fillBlock(Gpr0.low8)` | `MOV ECX, N; REP STOSB` (`F3 AA`) | — | Rellena `N` bytes; usa `Gpr1`, `Gpr7`, `Gpr0.low8` y `Frf.direction` |

### 5.2 Aritmética Entera

| Verbo | Sintaxis | Instrucción x64 | Flags | Notas |
|---|---|---|---|---|
| `add` | `Gpr0.add(Gpr1)` | `ADD r/m64, r64` (`48 01 /r`) | ● `CF,OF,SF,ZF,AF,PF` | Suma |
| `sub` | `Gpr0.sub(Gpr1)` | `SUB r/m64, r64` (`48 29 /r`) | ● `CF,OF,SF,ZF,AF,PF` | Resta |
| `addWithCarry` | `Gpr0.addWithCarry(Gpr1)` | `ADC r/m64, r64` (`48 11 /r`) | ● `CF,OF,SF,ZF,AF,PF` | Suma incluyendo `Frf.carry` |
| `subWithBorrow` | `Gpr0.subWithBorrow(Gpr1)` | `SBB r/m64, r64` (`48 19 /r`) | ● `CF,OF,SF,ZF,AF,PF` | Resta incluyendo préstamo `Frf.carry` |
| `increment` | `Gpr0.increment()` | `INC r/m64` (`48 FF /0`) | ● `OF,SF,ZF,AF,PF` (`CF` —) | Suma 1; **no** altera `Frf.carry` |
| `decrement` | `Gpr0.decrement()` | `DEC r/m64` (`48 FF /1`) | ● `OF,SF,ZF,AF,PF` (`CF` —) | Resta 1; **no** altera `Frf.carry` |
| `negate` | `Gpr0.negate()` | `NEG r/m64` (`48 F7 /3`) | ● `CF,OF,SF,ZF,AF,PF` | Complemento a dos (`-x`) |
| `multiply` | `Gpr0.multiply(Gpr1)` | `IMUL r64, r/m64` (`48 0F AF /r`) | ● `CF,OF` (`?` resto) | Producto de 64 bits |
| `multiplyWide` | `(Gpr2:Gpr0).multiplyWide(Gpr3)` | `MUL r/m64` (`48 F7 /4`) | ● `CF,OF` (`?` resto) | `Gpr2:Gpr0 = Gpr0 × Gpr3` (128 bits sin signo) |
| `divide` | `(Gpr0:Gpr2).divide(Gpr3)` | `DIV r/m64` (`48 F7 /6`) | `?` todos | Divide `Gpr2:Gpr0 / Gpr3` → `Gpr0`=cociente, `Gpr2`=resto |
| `divideSigned` | `(Gpr0:Gpr2).divideSigned(Gpr3)` | `IDIV r/m64` (`48 F7 /7`) | `?` todos | División con signo |
| `signExtend` | `(Gpr2:Gpr0).signExtend()` | `CQO` (`48 99`) | — | Extiende signo de `Gpr0` hacia `Gpr2` antes de `divideSigned` |
| `compare` | `Gpr0.compare(Gpr1)` | `CMP r/m64, r64` (`48 39 /r`) | ● `CF,OF,SF,ZF,AF,PF` | Resta interna sin escribir destino |

### 5.3 Lógica y Manipulación de Bits

| Verbo | Sintaxis | Instrucción x64 | Flags | Notas |
|---|---|---|---|---|
| `and` | `Gpr0.and(Gpr1)` | `AND r/m64, r64` (`48 21 /r`) | ● `CF=0,OF=0,SF,ZF,PF` (`? AF`) | AND bit a bit |
| `or` | `Gpr0.or(Gpr1)` | `OR r/m64, r64` (`48 09 /r`) | ● `CF=0,OF=0,SF,ZF,PF` (`? AF`) | OR bit a bit |
| `xor` | `Gpr0.xor(Gpr1)` | `XOR r/m64, r64` (`48 31 /r`) | ● `CF=0,OF=0,SF,ZF,PF` (`? AF`) | XOR bit a bit |
| `not` | `Gpr0.not()` | `NOT r/m64` (`48 F7 /2`) | — | Invierte todos los bits (`~x`) |
| `test` | `Gpr0.test(Gpr1)` | `TEST r/m64, r64` (`48 85 /r`) | ● `CF=0,OF=0,SF,ZF,PF` (`? AF`) | AND sin escribir destino |
| `shiftLeft` | `Gpr0.shiftLeft(Bit:3)` | `SHL r/m64, imm8` (`48 C1 /4 ib`) | ● `CF,OF,SF,ZF,PF` | Acepta `Bit:N` o `Gpr1.low8` (`D3 /4`) |
| `shiftRightLogical` | `Gpr0.shiftRightLogical(Bit:3)` | `SHR r/m64, imm8` (`48 C1 /5 ib`) | ● `CF,OF,SF,ZF,PF` | Rellena con ceros (acepta `Gpr1.low8`) |
| `shiftRightArithmetic` | `Gpr0.shiftRightArithmetic(Bit:3)` | `SAR r/m64, imm8` (`48 C1 /7 ib`) | ● `CF,OF,SF,ZF,PF` | Conserva bit de signo (acepta `Gpr1.low8`) |
| `rotateLeft` | `Gpr0.rotateLeft(Bit:1)` | `ROL r/m64, 1/imm8` (`48 D1`/`C1 /0`) | ● `CF,OF` | Rotación izquierda |
| `rotateRight` | `Gpr0.rotateRight(Bit:1)` | `ROR r/m64, 1/imm8` (`48 D1`/`C1 /1`) | ● `CF,OF` | Rotación derecha |
| `testBit` | `Gpr0.testBit(Bit:7)` | `BT r/m64, imm8` (`48 0F BA /4 ib`) | ● `CF` | Copia el bit a `Frf.carry` |
| `setBit` | `Gpr0.setBit(Bit:7)` | `BTS r/m64, imm8` (`48 0F BA /5 ib`) | ● `CF` | Lee el bit a `CF` y lo pone en `1` |
| `clearBit` | `Gpr0.clearBit(Bit:7)` | `BTR r/m64, imm8` (`48 0F BA /6 ib`) | ● `CF` | Lee el bit a `CF` y lo pone en `0` |
| `toggleBit` | `Gpr0.toggleBit(Bit:7)` | `BTC r/m64, imm8` (`48 0F BA /7 ib`) | ● `CF` | Lee el bit a `CF` y lo invierte |

### 5.4 Pila (`Stack`)

| Verbo | Sintaxis | Instrucción x64 | Notas |
|---|---|---|---|
| `Stack.push` | `Stack.push(Gpr0)` | `PUSH r64` (`50+rd`) | `Gpr4 -= 8; [Gpr4] = Gpr0` |
| `pop(Stack)` | `Gpr0.pop(Stack)` | `POP r64` (`58+rd`) | `Gpr0 = [Gpr4]; Gpr4 += 8` |
| `Stack.push(Frf)` | `Stack.push(Frf)` | `PUSHFQ` (`9C`) | Guarda banderas de 64 bits en la pila |
| `Frf.pop(Stack)` | `Frf.pop(Stack)` | `POPFQ` (`9D`) | Restaura banderas de 64 bits desde la pila |
| `Stack.reserve` | `Stack.reserve(Byte:16)` | `SUB RSP, imm` (`48 83 /5 ib`) | Reserva espacio alineado |
| `Stack.release` | `Stack.release(Byte:16)` | `ADD RSP, imm` (`48 83 /0 ib`) | Libera espacio reservado |

### 5.5 Control de Flujo (`Ipr`) y las 16 Condiciones (`Frf`)

| Verbo | Sintaxis | Instrucción x64 | Notas |
|---|---|---|---|
| `flowJump` | `Ipr.flowJump(@lbl)` | `JMP rel8/rel32` (`EB`/`E9`) | Salto incondicional relativo |
| `flowJump` | `Ipr.flowJump(Gpr0)` | `JMP r64` (`FF /4`) | Salto indirecto |
| `flowJump` | `Ipr.flowJump(@lbl if cond)` | `Jcc rel8/rel32` (`7x`/`0F 8x`) | Salto condicional (16 formas abajo) |
| `flowCall` | `Ipr.flowCall(c -> ...)` / `Ipr.flowCall(r)` | `CALL rel32` (`E8 cd`) | Llamada con contrato visible o simple |
| `flowCall` | `Ipr.flowCall(Gpr0)` | `CALL r64` (`FF /2`) | Llamada indirecta |
| `flowStackJump` | `Ipr.flowStackJump()` | `RET` (`C3`) | Extrae dirección de `Stack` y salta |
| `flowLoop` | `Ipr.flowLoop(@lbl)` | `LOOP rel8` (`E2 cb`) | `Gpr1--; if Gpr1 != 0: salta` (**no** toca flags) |

**Las 16 formas condicionales válidas (físicas y alias legibles equivalentes):**

| Forma física (`Frf`) | Alias legible | Instrucción x64 | Bits evaluados |
|---|---|---|---|
| `Frf.zero` | `Frf.equal` | `JE` / `JZ` (`74` / `0F 84`) | `ZF = 1` |
| `not Frf.zero` | `Frf.notEqual` | `JNE` / `JNZ` (`75` / `0F 85`) | `ZF = 0` |
| `Frf.carry` | `Frf.below` | `JC` / `JB` (`72` / `0F 82`) | `CF = 1` |
| `not Frf.carry` | `Frf.aboveOrEqual` | `JNC` / `JAE` (`73` / `0F 83`) | `CF = 0` |
| `Frf.sign` | — | `JS` (`78` / `0F 88`) | `SF = 1` |
| `not Frf.sign` | — | `JNS` (`79` / `0F 89`) | `SF = 0` |
| `Frf.overflow` | — | `JO` (`70` / `0F 80`) | `OF = 1` |
| `not Frf.overflow` | — | `JNO` (`71` / `0F 81`) | `OF = 0` |
| `Frf.parity` | — | `JP` (`7A` / `0F 8A`) | `PF = 1` |
| `not Frf.parity` | — | `JNP` (`7B` / `0F 8B`) | `PF = 0` |
| `Frf.carry or Frf.zero` | `Frf.belowOrEqual` | `JBE` (`76` / `0F 86`) | `CF = 1 o ZF = 1` |
| `not Frf.carry and not Frf.zero` | `Frf.above` | `JA` (`77` / `0F 87`) | `CF = 0 y ZF = 0` |
| `Frf.sign unequal Frf.overflow` | `Frf.less` | `JL` (`7C` / `0F 8C`) | `SF ≠ OF` |
| `Frf.sign equal Frf.overflow` | `Frf.greaterOrEqual` | `JGE` (`7D` / `0F 8D`) | `SF = OF` |
| `Frf.zero or Frf.sign unequal Frf.overflow` | `Frf.lessOrEqual` | `JLE` (`7E` / `0F 8E`) | `ZF = 1 o SF ≠ OF` |
| `not Frf.zero and Frf.sign equal Frf.overflow` | `Frf.greater` | `JG` (`7F` / `0F 8F`) | `ZF = 0 y SF = OF` |

### 5.6 Control *Bare Metal* y Puertos (`Cpu`, `Port`)

| Verbo | Sintaxis | Instrucción x64 | Notas |
|---|---|---|---|
| `Cpu.noOperation()` | `Cpu.noOperation()` | `NOP` (`90`) | No altera estado |
| `Cpu.pause()` | `Cpu.pause()` | `PAUSE` (`F3 90`) | Espera eficiente en bucles |
| `Cpu.halt()` | `Cpu.halt()` | `HLT` (`F4`) | Detiene la CPU hasta interrupción |
| `Port[...].copy` | `Port[0x03F8].copy(Gpr0.low8)` | `OUT imm8, AL` / `OUT DX, AL` (`E6`/`EE`) | Puertos `> 0xFF` usan `Gpr2.low16` (`DX`) |
| `copy(Port[...])` | `Gpr0.low8.copy(Port[Gpr2.low16])` | `IN AL, imm8` / `IN AL, DX` (`E4`/`EC`) | Lee 1, 2 o 4 bytes según vista (`.low8`/`.low16`/`.low32`) |


## 6. Reglas de Codificación y Salida `.x64` (Fase 1)

1. **Forma más corta determinista:** para los mismos operandos, el codificador produce siempre exactamente los mismos bytes, eligiendo `rel8` antes que `rel32` y `disp8` antes que `disp32`.
2. **Relajación de saltos:** todos los saltos inician estimados como cortos (`rel8`) y se promueven a cercanos (`rel32`) iterativamente hasta convergencia cuando el desplazamiento supera `-128..127`.
3. **Prefijo `REX`:** se emite solo cuando es obligatorio (operandos de 64 bits, registros `Gpr8`–`Gpr15`, o `.low8` de `Gpr4`–`Gpr7`).
4. **Imagen plana `.x64`:** bytes puros sin cabecera PE/ELF, orden de secciones `.execute` seguido de `.data`, código independiente de posición (PIC). Si la rutina de entrada no está en el offset `0`, se inserta un salto inicial hacia ella.
5. **Ejecución de prueba:** mediante arrancador de FluentASM en memoria o imagen cargada en QEMU *bare metal*.


## 7. Ejemplos Completos de Fase 1

### 7.1 Llamada entre rutinas (`miCalculadoraCli`)

```fasm
miCalculadoraCli
    .data(dt -> {
        dt.define<Byte:8>(d -> d.id(numA).value(10))
        dt.define<Byte:8>(d -> d.id(numB).value(20))
    })
    .execute(ex -> {
        ex.routines(rt -> {

            rt.define(d -> {
                d.id(main)
                d.returns(Gpr0)
                d.run(() -> {
                    Gpr1.copy([numA])
                    Gpr2.copy([numB])
                    Ipr.flowCall(c -> c.target(sumar).inputs(Gpr1, Gpr2).output(Gpr0))
                    Ipr.flowStackJump()
                })
            })

            rt.define(d -> {
                d.id(sumar)
                d.returns(Gpr0)
                d.run((arg1<Byte:8>, arg2<Byte:8>) -> {
                    Gpr0.copy(arg1)
                    Gpr0.add(arg2)
                    Ipr.flowStackJump()
                })
            })

        })
    })
    .initialize()
```

### 7.2 Bucle ascendente con `defaultValue` y salto condicional

```fasm
bucleDemo
    .data(dt -> {
        dt.define<Byte:8>(d -> d.id(limite).value(5))
    })
    .execute(ex -> {
        ex.routines(rt -> {

            rt.define(d -> {
                d.id(main)
                d.returns(Gpr0)
                d.run(() -> {
                    Gpr0.copy(defaultValue)       // acumulador = 0
                    Gpr1.copy(defaultValue)       // contador = 0
                @ciclo:
                    Gpr0.add(Gpr1)
                    Gpr1.increment()
                    Gpr1.compare([limite])
                    Ipr.flowJump(@ciclo if Frf.below)
                    Ipr.flowStackJump()           // devuelve 0+1+2+3+4 = 10 en Gpr0
                })
            })

        })
    })
    .initialize()
```

### 7.3 Salida directa *bare metal* por puerto serie (QEMU COM1)

```fasm
serialBareMetal
    .data(dt -> {
        dt.define<Byte:2>(d -> d.id(puertoCom1).value(0xF8, 0x03)) // 0x03F8 little-endian
        dt.define<Byte:1>(d -> d.id(letra).value("A"))
    })
    .execute(ex -> {
        ex.routines(rt -> {

            rt.define(d -> {
                d.id(main)
                d.run(() -> {
                    Frf.interrupt(0)                    // desactiva interrupciones al iniciar
                    Gpr2.low16.copy([puertoCom1])       // Gpr2.low16 (DX) = 0x03F8
                    Gpr0.low8.copy([letra])             // Gpr0.low8 (AL) = 'A'
                    Port[Gpr2.low16].copy(Gpr0.low8)    // OUT DX, AL
                    Cpu.halt()                          // detiene la CPU limpiamente
                })
            })

        })
    })
    .initialize()
```


## 8. Diagnósticos x64 (Fase 1)

| Código | Situación |
|---|---|
| `FASM2007` | Dato literal prohibido escrito dentro de `.execute(...)` |
| `FASM3002` | Conflicto entre `.high8` (`Gpr0`–`Gpr3`) y `.low8` que exige `REX` (`Gpr4`–`Gpr7`) |
| `FASM3012` | Uso de nombre legacy prohibido (`Rax`, `Eax`, `RIP`, `rFlags`, `Mov`, `Ret`, etc.) |
| `FASM3013` | Índice de `Gpr` fuera de rango (`0`–`15`) |
| `FASM3015` | `.high8` usado en `Gpr4`–`Gpr15` |
| `FASM3020` / `FASM3021` | Mezcla inválida de `Bit` y `Byte` según el contexto |
| `FASM3023` | Falta capacidad explícita `Byte:N` en acceso a memoria por registro |
| `FASM3060` | Acceso directo inválido a `Ipr` con `.copy` |
| `FASM4002` | Operando o escala de direccionamiento inválida (solo 1, 2, 4 u 8) |
| `FASM4003` | `Gpr4` usado como índice en direccionamiento SIB |
| `FASM4012` | Lectura de bandera en `Frf` dejada indefinida por la instrucción previa |
| `FASM4026` | Copia directa memoria → memoria con `.copy` |
| `FASM5002` / `FASM5003` | Contrato de `Ipr.flowCall` incompleto, lectura de entrada invalidada o pila desalineada |


## 9. Elementos Diferidos a Fases Posteriores (2+)

Para mantener la Fase 1 enfocada y viable, quedan explícitamente fuera hasta fases siguientes:

| Elemento | Fase prevista | Motivo |
|---|---|---|
| Vectores SIMD (`Simd0`–`Simd31`, SSE/AVX/AVX-512) y máscaras `Krf` | Fase 4 | Unidades vectoriales y reglas VEX/EVEX separadas del núcleo entero |
| Coma flotante x87 (`Xrf0`–`Xrf7`) | Fase 4 | Pila de 80 bits independiente de los `Gpr` |
| Registros de sistema y control (`Srf`, `Crf`, `Drf`, `Msr`, `Xcr0`, `Pkru`, `Ssp`) | Fase 2–4 | Paginación, tablas de interrupciones y privilegios de kernel completo |
| Virtualización (`VMX` / `SVM`) y APIC avanzado | Fase 4 | Hipervisores fuera del alcance básico |
| Comunicación entre binarios `.bin` ("Partes") y ejecutables PE/ELF | Fase 2 / 5 | Requiere primero estabilizar la generación de imagen plana `.x64` |
| Capa común multi-arquitectura (ARM64 / RISC-V) | Fase 3 | Se consolidará contrastando x64 real contra una segunda CPU real |
