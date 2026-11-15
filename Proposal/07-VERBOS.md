# 07 — Catálogo de verbos

> **Normativo.** Cada verbo del lenguaje, su instrucción x64, su encoding, los flags que toca y sus restricciones. El receptor es siempre el destino real. La columna *Flags* usa: `●` modificado, `—` intacto, `?` indefinido.

---

## 1. Reglas del catálogo

1. **Un verbo, una instrucción.** Si un verbo puede mapear a dos opcodes, el compilador elige el más corto (D-013) y lo documenta.
2. **El sufijo declara interpretación solo cuando hay ambigüedad.** En GPR solo hay enteros: `add`, `sub`, `multiply`. En SIMD hay dos: `addInt`, `addFloat`. Si existe una variante con signo distinta, se explicita: `multiplySigned`, `divideSigned`.
3. **Los flags son los del opcode real.** No se normalizan ni se "mejoran".
4. **Los registros implícitos se declaran** en la columna *Notas* (tabla completa en [03-REGISTROS](03-REGISTROS.md) §12).
5. **La forma es obligatoria.** Un operando de memoria sin `Byte:N` no compila (`FASM3023`).

### 1.1 Orden de las tuplas de destino

El orden lo fija el verbo, no la convención general:

| Verbo | Tupla | Orden |
|---|---|---|
| `multiply` | `(Gpr2:Gpr0)` | `alto:bajo` |
| `divide` | `(Gpr0:Gpr2)` | `cociente:resto` |
| `signExtend` | `(Gpr2:Gpr0)` | `alto:bajo` |
| `readTimestamp` | `(Gpr2:Gpr0)` | `alto:bajo` |
| `identify` | `(Gpr0:Gpr3:Gpr1:Gpr2)` | `eax:ebx:ecx:edx` |

Un verbo que no figura aquí no acepta tupla (`FASM3016`).

---

## 2. Transferencia (GPR)

| Verbo | Sintaxis | x64 | Flags | Notas |
|---|---|---|---|---|
| `copy` | `Gpr0.copy(Gpr1)` | `MOV r64, r/m64` — `REX.W 8B /r` o `89 /r` | — | Elige la dirección más corta |
| `copy` | `Gpr0.copy(Byte:8 [x])` | `MOV r64, m64` — `REX.W 8B /r` | — | Carga |
| `copy` | `Gpr0.low8.copy(Frf.zero)` | `SETZ r8` — `0F 94 /0` | — | Flag → 0/1 en receptor `.low8` |
| `copy` | `Gpr0.low8.copy(not Frf.zero)` | `SETNZ r8` — `0F 95 /0` | — | Flag negado → 0/1 en `.low8` |
| `copy` | `Byte:8 [x].copy(Gpr0)` | `MOV m64, r64` — `REX.W 89 /r` | — | Almacena |
| `copy` | `Gpr0.low32.copy(Byte:4 [x])` | `MOV r32, m32` — `8B /r` | — | Zero-extiende a 64 |
| `loadAddress` | `Gpr0.loadAddress(@x)` | `LEA r64, [rip + x]` — `REX.W 8D /r` | — | Dirección, no contenido |
| `loadAddress` | `Gpr0.loadAddress(absolute(x))` | `MOV r64, imm64` — `REX.W B8+rd` | — | Solo imagen con base conocida |
| `loadSigned` | `Gpr0.loadSigned(Byte:1 [x])` | `MOVSX r64, r/m8` — `REX.W 0F BE /r` | — | Extiende con signo |
| `loadUnsigned` | `Gpr0.loadUnsigned(Byte:1 [x])` | `MOVZX r64, r/m8` — `REX.W 0F B6 /r` | — | Extiende con ceros |
| `exchange` | `Gpr0.exchange(Gpr1)` | `XCHG r/m64, r64` — `REX.W 87 /r` | — | Implícitamente atómico con memoria |
| `exchange` | `Gpr0.exchange(Byte:8 [x])` | `XCHG r64, m64` — `REX.W 87 /r` | — | `LOCK` implícito |
| `conditionalCopy` | `Gpr0.conditionalCopy(Gpr1 if Frf.zero)` | `CMOVZ r64, r/m64` — `REX.W 0F 44 /r` | — | El destino conserva su valor si no se cumple |
| `compareExchange` | `Byte:8 [x].compareExchange(Gpr0, Gpr1)` | `CMPXCHG m64, r64` — `REX.W 0F B1 /r` | ● | Compara con `Gpr0`; si igual, escribe `Gpr1` |

`Gpr0.copy(Gpr1)` y `Gpr1.copy(Gpr0)` producen los mismos bytes salvo que uno sea más corto. El emisor elige `89` cuando el destino es `reg` y la fuente `r/m`, y `8B` en el caso contrario.

---

## 3. Aritmética entera (GPR)

| Verbo | Sintaxis | x64 | Flags | Notas |
|---|---|---|---|---|
| `add` | `Gpr0.add(Gpr1)` | `ADD r/m64, r64` — `REX.W 01 /r` | ● CF OF ZF SF PF AF | |
| `add` | `Gpr0.add(Byte:8 [x])` | `ADD r64, r/m64` — `REX.W 03 /r` | ● | |
| `add` | `Byte:8 [x].add(Gpr0)` | `ADD m64, r64` — `REX.W 01 /r` | ● | |
| `addWithCarry` | `Gpr0.addWithCarry(Gpr1)` | `ADC r/m64, r64` — `REX.W 11 /r` | ● | Suma multi-palabra |
| `sub` | `Gpr0.sub(Gpr1)` | `SUB r/m64, r64` — `REX.W 29 /r` | ● | |
| `sub` | `Gpr0.sub(Byte:8 [x])` | `SUB r64, r/m64` — `REX.W 2B /r` | ● | |
| `subWithBorrow` | `Gpr0.subWithBorrow(Gpr1)` | `SBB r/m64, r64` — `REX.W 19 /r` | ● | Resta multi-palabra |
| `increment` | `Gpr0.increment()` | `INC r/m64` — `REX.W FF /0` | ● CF — | **No** modifica CF |
| `decrement` | `Gpr0.decrement()` | `DEC r/m64` — `REX.W FF /1` | ● CF — | **No** modifica CF |
| `negate` | `Gpr0.negate()` | `NEG r/m64` — `REX.W F7 /3` | ● | CF=1 si el operando no era 0 |
| `compare` | `Gpr0.compare(Gpr1)` | `CMP r/m64, r64` — `REX.W 39 /r` | ● | No escribe resultado |
| `compare` | `Gpr0.compare(Byte:8 [x])` | `CMP r64, r/m64` — `REX.W 3B /r` | ● | |
| `test` | `Gpr0.test(Gpr1)` | `TEST r/m64, r64` — `REX.W 85 /r` | ● | `AND` sin destino |
| `testBit` | `Gpr0.testBit(Bit:7)` | `BT r/m64, imm8` — `0F BA /4 ib` | ● CF | No toca los demás |
| `compareSigned` | `Gpr0.compareSigned(Gpr1)` | `CMP` + condición con signo | ● | Documentado: no cambia el opcode, cambia la condición posterior |

### 3.1 Multiplicación y división

| Verbo | Sintaxis | x64 | Flags | Notas |
|---|---|---|---|---|
| `multiply` | `(Gpr2:Gpr0).multiply(Gpr3)` | `MUL r/m64` — `REX.W F7 /4` | ● CF OF, `?` resto | Sin signo; `RDX:RAX` |
| `multiplySigned` | `(Gpr2:Gpr0).multiplySigned(Gpr3)` | `IMUL r/m64` — `REX.W F7 /5` | ● CF OF | Con signo; `RDX:RAX` |
| `multiplyLow` | `Gpr0.multiplyLow(Gpr1)` | `IMUL r64, r/m64` — `REX.W 0F AF /r` | ● CF OF | Producto truncado a 64; un destino |
| `divide` | `(Gpr0:Gpr2).divide(Gpr3)` | `DIV r/m64` — `REX.W F7 /6` | `?` todos | Sin signo; cociente `RAX`, resto `RDX` |
| `divideSigned` | `(Gpr0:Gpr2).divideSigned(Gpr3)` | `IDIV r/m64` — `REX.W F7 /7` | `?` | Divisor 0 → `#DE` (`FASM4032`) |
| `signExtend` | `(Gpr2:Gpr0).signExtend()` | `CQO` — `REX.W 99` | — | `RDX` = signo de `RAX` |

> **Desbordamiento de división.** `divide` con cociente fuera de rango produce `#DE`. El lenguaje no lo comprueba: es una condición de ejecución. Se documenta y se emite `FASM4033` (info) si el divisor es una constante estructural.

### 3.2 Lógica

| Verbo | Sintaxis | x64 | Flags | Notas |
|---|---|---|---|---|
| `and` | `Gpr0.and(Gpr1)` | `AND r/m64, r64` — `REX.W 21 /r` | ● CF=0 OF=0, ZF SF PF, `?` AF | |
| `or` | `Gpr0.or(Gpr1)` | `OR r/m64, r64` — `REX.W 09 /r` | ● CF=0 OF=0 | |
| `xor` | `Gpr0.xor(Gpr1)` | `XOR r/m64, r64` — `REX.W 31 /r` | ● CF=0 OF=0 | |
| `not` | `Gpr0.not()` | `NOT r/m64` — `REX.W F7 /2` | — | **No** toca flags |
| `clear` | `Gpr0.clear()` | `XOR r32, r32` — `31 C0+rd` | ● CF=0 OF=0 ZF=1 | 2 bytes (`Gpr0`–`Gpr7`), 3 con REX (`Gpr8`–`Gpr15`); cero sin literal |
| `clear` | `Gpr0.low32.clear()` | `XOR r32, r32` | ● | |
| `andNot` | `Gpr0.andNot(Gpr1)` | `ANDN r64, r64, r/m64` — `VEX.LZ.F2.0F38.W1 F2 /r` | ● | BMI1; requiere `x64-avx2` |

`clear()` sobre un GPR completo es `XOR eax, eax` (zero-extiende). Sobre `.low32` es la misma instrucción. Sobre `.low16`/`.low8` no existe `XOR` sin REX de 16/8 bits que limpie solo esa parte; el compilador emite `XOR r16, r16` / `XOR r8, r8` y **conserva** el resto (merge).

### 3.3 Desplazamientos y rotaciones

| Verbo | Sintaxis | x64 | Flags | Notas |
|---|---|---|---|---|
| `shiftLeft` | `Gpr0.shiftLeft(Bit:3)` | `SHL r/m64, imm8` — `REX.W C1 /4 ib` | ● CF OF, ZF SF PF | Cuenta 1 ⇒ `D1 /4` |
| `shiftLeft` | `Gpr0.shiftLeft(Gpr1.low8)` | `SHL r/m64, CL` — `REX.W D3 /4` | ● | Cuenta variable: solo `Gpr1.low8` → `FASM4030` |
| `shiftRightLogical` | `Gpr0.shiftRightLogical(Bit:1)` | `SHR r/m64, 1` — `REX.W D1 /5` | ● | Rellena con 0 |
| `shiftRightArithmetic` | `Gpr0.shiftRightArithmetic(Bit:1)` | `SAR r/m64, 1` — `REX.W D1 /7` | ● | Rellena con signo |
| `rotateLeft` | `Gpr0.rotateLeft(Bit:1)` | `ROL r/m64, 1` — `REX.W D1 /0` | ● CF OF | |
| `rotateRight` | `Gpr0.rotateRight(Bit:1)` | `ROR r/m64, 1` — `REX.W D1 /1` | ● | |
| `rotateLeftThroughCarry` | `Gpr0.rotateLeftThroughCarry(Bit:1)` | `RCL r/m64, 1` — `REX.W D1 /2` | ● | Usa y define CF |
| `rotateRightThroughCarry` | `Gpr0.rotateRightThroughCarry(Bit:1)` | `RCR r/m64, 1` — `REX.W D1 /3` | ● | |
| `shiftLeftDouble` | `Gpr0.shiftLeftDouble(Gpr1, Bit:3)` | `SHLD r/m64, r64, imm8` — `REX.W 0F A4 /r ib` | ● | |
| `shiftRightDouble` | `Gpr0.shiftRightDouble(Gpr1, Bit:3)` | `SHRD r/m64, r64, imm8` — `REX.W 0F AC /r ib` | ● | |
| `shiftLeftBytes` | `Gpr0.shiftLeftBytes(Bit:1)` | `SHL` con cuenta ×8 | ● | Azúcar sobre `shiftLeft` |

> **Cuentas mayores que el ancho.** El hardware enmascara la cuenta a 6 bits (64) o 5 (32). El lenguaje lo documenta pero no lo corrige: `shiftLeft(Bit:64)` es legal y equivale a no desplazar.

### 3.4 Otras operaciones de bits

| Verbo | Sintaxis | x64 | Flags | Notas |
|---|---|---|---|---|
| `setBit` | `Gpr0.setBit(Bit:0)` | `BTS r/m64, imm8` — `0F BA /5 ib` | ● CF | |
| `clearBit` | `Gpr0.clearBit(Bit:0)` | `BTR r/m64, imm8` — `0F BA /6 ib` | ● CF | |
| `complementBit` | `Gpr0.complementBit(Bit:0)` | `BTC r/m64, imm8` — `0F BA /7 ib` | ● CF | |
| `setBits` | `Gpr0.setBits(Bit:8)` | `MOV` de máscara estructural | ● | Pone los `N` bits bajos a 1 |
| `scanForward` | `Gpr0.scanForward(Gpr1)` | `BSF r64, r/m64` — `REX.W 0F BC /r` | ● ZF | Índice del bit 1 más bajo |
| `scanReverse` | `Gpr0.scanReverse(Gpr1)` | `BSR r64, r/m64` — `REX.W 0F BD /r` | ● ZF | Índice del bit 1 más alto |
| `populationCount` | `Gpr0.populationCount(Gpr1)` | `POPCNT r64, r/m64` — `F3 REX.W 0F B8 /r` | ● ZF | `x64-avx2` |
| `countLeadingZeros` | `Gpr0.countLeadingZeros(Gpr1)` | `LZCNT r64, r/m64` — `F3 REX.W 0F BD /r` | ● | `x64-avx2` |
| `countTrailingZeros` | `Gpr0.countTrailingZeros(Gpr1)` | `TZCNT r64, r/m64` — `F3 REX.W 0F BC /r` | ● | `x64-avx2` |
| `byteSwap` | `Gpr0.byteSwap()` | `BSWAP r64` — `REX.W 0F C8+rd` | — | |
| `reverseBits` | `Gpr0.reverseBits()` | Expansión documentada | — | No hay opcode; el compilador emite la secuencia y la lista |
| `extractBits` | `Gpr0.extractBits(Gpr1, Bit:8, Bit:4)` | `BEXTR r64, r/m64, r64` — `VEX.LZ.0F38.W1 F7 /r` | ● | BMI1; máscara estructural |
| `insertBits` | `Gpr0.insertBits(Gpr1, Bit:8, Bit:4)` | `BEXTR`+`SHL`+`OR` o `BZHI` | ● | Documentado |

### 3.5 Extensión y truncamiento

| Verbo | Sintaxis | x64 | Flags |
|---|---|---|---|
| `extendUnsigned` | `Gpr0.low32.extendUnsigned()` | `MOV r32, r32` (zero-extend implícito) | — |
| `extendSigned` | `Gpr0.low32.extendSigned()` | `MOVSXD r64, r/m32` — `REX.W 63 /r` | — |
| `extendSigned` | `Gpr0.low8.extendSigned()` | `MOVSX r64, r/m8` | — |
| `extendUnsigned` | `Gpr0.low8.extendUnsigned()` | `MOVZX r64, r/m8` | — |
| `truncate` | `Gpr0.low32.truncate()` | `MOV r32, r32` | — |
| `signExtend` | `(Gpr2:Gpr0).signExtend()` | `CQO` | — |

---

## 4. Pila (`Stack`)

`Stack` es un archivo virtual: `Gpr4` (RSP) + memoria.

| Verbo | Sintaxis | x64 | Flags | Notas |
|---|---|---|---|---|
| `push` | `Stack.push(Gpr0)` | `PUSH r64` — `50+rd` | — | `RSP -= 8` |
| `push` | `Stack.push(Byte:8 [x])` | `PUSH m64` — `FF /6` | — | |
| `pop` | `Gpr0.pop(Stack)` | `POP r64` — `58+rd` | — | `RSP += 8` |
| `push` | `Stack.push(Frf)` | `PUSHFQ` — `9C` | — | |
| `pop` | `Frf.pop(Stack)` | `POPFQ` — `9D` | ● | |
| `enter` | `Stack.enter(Byte:32)` | `ENTER imm16, 0` — `C8 iw 00` | — | Reserva + frame |
| `leave` | `Stack.leave()` | `LEAVE` — `C9` | — | `RSP = RBP`; `POP RBP` |
| `reserve` | `Stack.reserve(Byte:32)` | `SUB rsp, imm32` — `REX.W 81 /5 id` | ● | Capacidad estructural |
| `release` | `Stack.release(Byte:32)` | `ADD rsp, imm32` — `REX.W 81 /0 id` | ● | |
| `align` | `Stack.align(Byte:16)` | `AND rsp, -16` — `REX.W 83 /4 ib` | ● | |
| `pointer` | `Gpr0.copy(Stack.pointer)` | `MOV r64, rsp` — `REX.W 89 /r` | — | Lectura del puntero |
| `framePointer` | `Gpr5.copy(Stack.framePointer)` | `MOV rbp, rsp` | — | |

`Stack.pop` no existe como receptor: el destino del `pop` es el registro, y por eso el verbo va en él (`Gpr0.pop(Stack)`).

---

## 5. Flags (`Frf`)

| Verbo | Sintaxis | x64 |
|---|---|---|
| `carry` | `Frf.carry(1)` | `STC` — `F9` |
| `carry` | `Frf.carry(0)` | `CLC` — `F8` |
| `carry` | `Frf.carry(not)` | `CMC` — `F5` |
| `direction` | `Frf.direction(1)` | `STD` — `FD` |
| `direction` | `Frf.direction(0)` | `CLD` — `FC` |
| `interrupt` | `Frf.interrupt(1)` | `STI` — `FB` |
| `interrupt` | `Frf.interrupt(0)` | `CLI` — `FA` |
| `alignmentCheck` | `Frf.alignmentCheck(1)` | `STAC` — `0F 01 CB` |
| `alignmentCheck` | `Frf.alignmentCheck(0)` | `CLAC` — `0F 01 CA` |
| `status8` | `Gpr0.low8.copy(Frf.status8)` | `LAHF` — `9F` |
| `status8` | `Frf.status8.copy(Gpr0.low8)` | `SAHF` — `9E` |
| `pop` | `Frf.pop(Stack)` | `POPFQ` — `9D` |
| `clear` | `Frf.clear()` | `XOR` de condiciones (secuencia) |

`Frf.carry(not)` es el único estado negado: el hardware solo tiene `CMC`.

---

## 6. Flujo (`Ipr`)

| Verbo | Sintaxis | x64 | Notas |
|---|---|---|---|
| `flowJump` | `Ipr.flowJump(@x)` | `JMP rel32` — `E9 cd` o `EB cb` | `rel8` si cabe |
| `flowJump` | `Ipr.flowJump(@x if Frf.zero)` | `JE rel32` — `0F 84 cd` | Condición → `Jcc` |
| `flowJump` | `Ipr.flowJump(Gpr0)` | `JMP r/m64` — `FF /4` | Indirecto |
| `flowJump` | `Ipr.flowJump(Byte:8 [tabla + Gpr1*8])` | `JMP m64` — `FF /4` | Tabla de saltos |
| `flowCall` | `Ipr.flowCall(rutina)` | `CALL rel32` — `E8 cd` | Relativo |
| `flowCall` | `Ipr.flowCall(Gpr0)` | `CALL r/m64` — `FF /2` | Indirecto |
| `flowStackJump` | `Ipr.flowStackJump()` | `RET` — `C3` | |
| `flowStackJump` | `Ipr.flowStackJump(Byte:16)` | `RET imm16` — `C2 iw` | Libera argumentos |
| `flowLoop` | `Ipr.flowLoop(@x)` | `LOOP rel8` — `E2 cb` | Decrementa `Gpr1`, salta si ≠ 0 |
| `flowLoop` | `Ipr.flowLoop(@x if Frf.zero)` | `LOOPE` — `E1 cb` | |
| `flowLoop` | `Ipr.flowLoop(@x if not Frf.zero)` | `LOOPNE` — `E0 cb` | |
| `flowInterrupt` | `Ipr.flowInterrupt(0x80)` | `INT imm8` — `CD ib` | Vector estructural |
| `flowInterrupt` | `Ipr.flowInterrupt(Byte:1 [v])` | `INT m8` — `CD /3` | |
| `flowSyscall` | `Ipr.flowSyscall()` | `SYSCALL` — `0F 05` | |
| `flowSysret` | `Ipr.flowSysret()` | `SYSRET` — `0F 07` | |
| `flowBreakpoint` | `Ipr.flowBreakpoint()` | `INT3` — `CC` | |
| `flowOverflow` | `Ipr.flowOverflow()` | `INTO` — `CE` | Solo si `Frf.overflow` |
| `current` | `Gpr0.copy(Ipr.current)` | `LEA r64, [rip]` — `REX.W 8D /r` | Lectura |

### 6.1 Condiciones

| Expresión | `Jcc` | Opcode |
|---|---|---|
| `Frf.zero` | `JE` / `JZ` | `74` / `0F 84` |
| `not Frf.zero` | `JNE` / `JNZ` | `75` / `0F 85` |
| `Frf.carry` | `JB` / `JC` | `72` / `0F 82` |
| `not Frf.carry` | `JAE` / `JNC` | `73` / `0F 83` |
| `Frf.sign` | `JS` | `78` / `0F 88` |
| `not Frf.sign` | `JNS` | `79` / `0F 89` |
| `Frf.overflow` | `JO` | `70` / `0F 80` |
| `not Frf.overflow` | `JNO` | `71` / `0F 81` |
| `Frf.parity` | `JP` | `7A` / `0F 8A` |
| `not Frf.parity` | `JNP` | `7B` / `0F 8B` |
| `Frf.sign unequal Frf.overflow` | `JL` | `7C` / `0F 8C` |
| `Frf.sign equal Frf.overflow` | `JGE` | `7D` / `0F 8D` |
| `Frf.zero or Frf.sign unequal Frf.overflow` | `JLE` | `7E` / `0F 8E` |
| `not Frf.zero and Frf.sign equal Frf.overflow` | `JG` | `7F` / `0F 8F` |
| `Frf.carry or Frf.zero` | `JBE` | `76` / `0F 86` |
| `not Frf.carry and not Frf.zero` | `JA` | `77` / `0F 87` |

Cualquier otra combinación se expande en varios `Jcc` y se marca con `FASM8104` (info).

### 6.2 Lejos

| Sintaxis | x64 |
|---|---|
| `(Ipr:Srf0).flowJump(Gpr0)` | `JMP FAR` |
| `(Ipr:Srf0).flowCall(Gpr0)` | `CALL FAR` |
| `(Ipr:Srf0).flowStackJump()` | `RETF` |
| `(Ipr:Srf0).flowInterrupt(Byte:1 [puerta])` | `IRETQ` / puerta |

---

## 7. Memoria

| Verbo | Sintaxis | x64 | Notas |
|---|---|---|---|
| `copy` | `Byte:8 [a].copy(Gpr0)` | `MOV m64, r64` | |
| `copyBlock` | `Byte:N [Gpr7].copyBlock(Byte:N [Gpr6])` | `REP MOVSB` — `F3 A4` | Cuenta = `N`; clobber declarado `Gpr1`, `Gpr6`, `Gpr7` |
| `copyBlock` | `Byte:N [Gpr7].copyBlock(Byte:N [Gpr6], Gpr1)` | `REP MOVSB` | Cuenta en `Gpr1` |
| `fillBlock` | `Byte:N [Gpr7].fillBlock(Gpr0.low8)` | `REP STOSB` — `F3 AA` | |
| `fillBlock` | `Byte:N [Gpr7].fillBlock(Gpr0.low32)` | `REP STOSD` — `F3 AB` | |
| `compareBlock` | `(Gpr7:Gpr6).compareBlock(Byte:N)` | `REPE CMPSB` — `F3 A6` | ● CF ZF |
| `scanBlock` | `Gpr7.scanBlock(Gpr0.low8)` | `REPNE SCASB` — `F3 AE` | ● ZF |
| `loadBlock` | `Byte:N [Gpr7].loadBlock(Port[0x1F0])` | `REP INSB` — `F3 6C` | E/S |
| `storeBlock` | `Port[0x1F0].storeBlock(Byte:N [Gpr6])` | `REP OUTSB` — `F3 6E` | E/S |
| `prefetch` | `Byte:64 [x].prefetch(.t0)` | `PREFETCHT0` — `0F 18 /1` | `.t0`, `.t1`, `.t2`, `.nta` |
| `addLocked` | `Byte:8 [x].addLocked(Gpr0)` | `LOCK ADD m64, r64` — `F0 REX.W 01 /r` | ● |
| `copyNonTemporal` | `Byte:8 [x].copyNonTemporal(Gpr0)` | `MOVNTI m64, r64` — `0F C3 /r` | Sin caché |
| `flushCacheLine` | `Byte:64 [x].flushCacheLine()` | `CLFLUSH` — `0F AE /7` | |
| `fence` | `Byte:8 [x].fence()` | `MFENCE` implícito en `XCHG` | Ver §9 |

Reglas:

- Memoria→memoria con `copy` → `FASM4026`. Solo los verbos de bloque mueven memoria a memoria, y usan `Gpr6`/`Gpr7`/`Gpr1` declarados.
- Un desplazamiento que no cabe en 32 bits → `FASM4006`; calcule la dirección en un `Gpr` (`FASM4029`).
- `addLocked` sobre una dirección no alineada → `FASM4034` (advertencia).

---

## 8. SIMD y x87

Resumen; el detalle está en [10-SIMD](10-SIMD.md) y [11-X87](11-X87.md).

### 8.1 SIMD (ejemplos)

| Verbo | Sintaxis | x64 |
|---|---|---|
| `copy` | `Simd0.r512.copy(Simd1.r512)` | `VMOVDQA64 zmm0, zmm1` |
| `addInt` | `Simd0.r512.part{32}.addInt(Simd1.r512.part{32})` | `VPADDD zmm0, zmm0, zmm1` |
| `addFloat` | `Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})` | `VADDPS zmm0, zmm0, zmm1` |
| `multiplyFloat` | `Simd0.r512.part{64}.multiplyFloat(...)` | `VMULPD` |
| `compareEqualInt` | `Simd0.r512.part{32}.compareEqualInt{Krf1}(...)` | `VPCMPEQD k1, zmm0, zmm1` |
| `convertToInt` | `Simd0.r256.part{32}.convertToInt(Simd1.r256.part{32})` | `VCVTTPS2DQ` |
| `broadcast` | `Simd0.r512.part{32}.broadcast(Gpr0.low32)` | `VPBROADCASTD` |
| `shuffle` | `Simd0.r512.part{32}.shuffle(Simd1.r512.part{32}, Bit:0x1B)` | `VSHUFPS` |

### 8.2 x87 (ejemplos)

| Verbo | Sintaxis | x64 |
|---|---|---|
| `loadFloat` | `Xrf.loadFloat(Byte:8 [x])` | `FLD qword` — `DD /0` |
| `loadInt` | `Xrf.loadInt(Byte:8 [x])` | `FILD qword` — `DF /5` |
| `storeFloatPop` | `Xrf0.storeFloatPop(Byte:8 [x])` | `FSTP qword` — `DD /3` |
| `addFloat` | `Xrf0.addFloat(Xrf1)` | `FADD ST(0), ST(i)` — `D8 C0+i` |
| `squareRoot` | `Xrf0.squareRoot()` | `FSQRT` — `D9 FA` |

---

## 9. CPU, barreras y estado

| Verbo | Sintaxis | x64 | Notas |
|---|---|---|---|
| `noOperation` | `Cpu.noOperation()` | `NOP` — `90` | |
| `pause` | `Cpu.pause()` | `PAUSE` — `F3 90` | |
| `halt` | `Cpu.halt()` | `HLT` — `F4` | ring 0 |
| `serialize` | `Cpu.serialize()` | `SERIALIZE` — `0F 01 E8` | AVX-512 |
| `identify` | `(Gpr0:Gpr3:Gpr1:Gpr2).identify()` | `CPUID` — `0F A2` | Hoja en `Gpr0` |
| `readTimestamp` | `(Gpr2:Gpr0).readTimestamp()` | `RDTSC` — `0F 31` | |
| `readTimestampSerialized` | `(Gpr2:Gpr0:Gpr1).readTimestampSerialized()` | `RDTSCP` — `0F 01 F9` | |
| `readRandom` | `Gpr0.readRandom()` | `RDRAND r64` — `0F C7 /6` | CF=1 si éxito |
| `readRandomSeeded` | `Gpr0.readRandomSeeded()` | `RDSEED r64` — `0F C7 /7` | |
| `full` | `Fence.full()` | `MFENCE` — `0F AE F0` | |
| `read` | `Fence.read()` | `LFENCE` — `0F AE E8` | |
| `write` | `Fence.write()` | `SFENCE` — `0F AE F8` | |
| `bit` | `Crf4.bit(16, 1)` | Leer-modificar-escribir sobre el `Crf` | Dos `MOV` + lógica |
| `fsgsbase` | `Crf4.fsgsbase(1)` | Azúcar de `Crf4.bit(16, 1)` | |
| `avx` | `Xcr0.avx(1)` | Azúcar de `Xcr0.bit(2, 1)` | ring 0 |
| `readMsr` | `(Gpr0:Gpr2).readMsr(Msr.efer)` | `RDMSR` — `0F 32` | Clobber declarado `Gpr1` |
| `write` | `Msr.efer.write(Gpr0, Gpr2)` | `WRMSR` — `0F 30` | ring 0 |
| `copy` | `Gpr0.low8.copy(Port[0x60])` | `IN AL, imm8` — `E4 ib` | |
| `copy` | `Port[0x60].copy(Gpr0.low8)` | `OUT imm8, AL` — `E6 ib` | |
| `copy` | `Gpr0.low32.copy(Port[Gpr2.low16])` | `IN EAX, DX` — `ED` | |
| `copy` | `Gpr0.copy(Xcr0)` | `XGETBV` — `0F 01 D0` | |
| `copy` | `Xcr0.copy(Gpr0)` | `XSETBV` — `0F 01 D1` | ring 0 |
| `copy` | `Gpr0.low32.copy(Pkru)` | `RDPKRU` — `0F 01 EE` | |
| `copy` | `Pkru.copy(Gpr0.low32)` | `WRPKRU` — `0F 01 EF` | |
| `copy` | `Gpr0.copy(Ssp)` | `RDSSPQ` — `F3 0F 01 EE` | CET |
| `copy` | `Ssp.copy(Gpr0)` | `WRSSQ` — `F3 0F 01 F0` | |

---

## 10. Verbos que **no** existen

| Ausente | Por qué | Equivalente |
|---|---|---|
| `mov` | Miente sobre "mover" | `copy` |
| `lea` | Es un `copy` de dirección | `loadAddress` |
| `ret` / `call` / `jmp` | No tienen receptor; son flujo | `Ipr.flow*` |
| `pushf` / `popf` | Flags no son registro plano | `Stack.push(Frf)` / `Frf.pop(Stack)` |
| `int`/`syscall` como mnemónico | Son flujo | `Ipr.flow*` |
| `nop` | Sin receptor | `Cpu.noOperation()` |
| `imul` de tres operandos con inmediato | El inmediato es dato | `Gpr0.multiplyLow(Gpr1)` |
| `add eax, 1` | Literal como dato | `Gpr0.increment()` |
| `xchg` con inmediato | Ídem | `exchange` |
| `movabs` | Nombre histórico | `Gpr0.loadAddress(absolute(x))` |
| `prefetchnta` | Mnemónico opaco | `prefetch(.nta)` |
| `cmpxchg16b` con registro implícito | Requiere tupla y flags | fase 2 |

---

## 11. Matriz de flags

Resumen de qué deja definido cada familia. Una condición que lee un flag marcado `?` produce `FASM4012`.

| Familia | CF | PF | AF | ZF | SF | OF |
|---|---|---|---|---|---|---|
| `add`, `sub`, `compare`, `test` | ● | ● | ● | ● | ● | ● |
| `addWithCarry`, `subWithBorrow` | ● | ● | ● | ● | ● | ● |
| `increment`, `decrement` | — | ● | ● | ● | ● | ● |
| `negate` | ● | ● | ● | ● | ● | ● |
| `and`, `or`, `xor`, `test` | 0 | ● | ? | ● | ● | 0 |
| `not`, `copy`, `load*`, `exchange` | — | — | — | — | — | — |
| `shift*`, `rotate*` | ● | ● | ? | ● | ● | ● |
| `multiply`, `multiplySigned` | ● | ? | ? | ? | ? | ● |
| `multiplyLow` | ● | ? | ? | ? | ? | ● |
| `divide`, `divideSigned` | ? | ? | ? | ? | ? | ? |
| `testBit`, `setBit`, `clearBit`, `complementBit` | ● | — | — | — | — | — |
| `scanForward`, `scanReverse`, `populationCount` | — | — | — | ● | — | — |
| `shiftLeftDouble`, `shiftRightDouble` | ● | ● | ? | ● | ● | ● |

---

## 12. Diagnósticos

| Código | Situación |
|---|---|
| `FASM3001` | Verbo no disponible para el receptor |
| `FASM4001` | Número de argumentos incorrecto |
| `FASM4002` | Tipo de argumento incorrecto |
| `FASM4030` | Cuenta de desplazamiento variable fuera de `Gpr1.low8` |
| `FASM4031` | División por cero estructural |
| `FASM4032` | División con cociente fuera de rango (aviso de `#DE`) |
| `FASM4033` | Divisor estructural constante (info) |
| `FASM4034` | Operación atómica sobre dirección no alineada |
| `FASM4026` | Memoria → memoria con `copy` |
| `FASM4028` | Operación requiere scratch no declarado |
| `FASM4029` | Dirección no representable en un solo SIB |

---

## 13. Resumen

- Cada verbo es una instrucción física; la tabla da su opcode y sus flags.
- El sufijo de interpretación solo aparece cuando hay ambigüedad.
- Las tuplas siguen el orden documentado por verbo.
- Los registros implícitos están declarados.
- `copy`, `add`, `compare`, `shift*`, `flow*` cubren el 90 % del uso diario.
- Los verbos ausentes tienen equivalente estructural, nunca un literal.
