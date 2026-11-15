# 03 — Registros

> **Normativo.** Este documento define todos los archivos de registros, sus miembros, sus vistas, sus roles implícitos y sus restricciones físicas. El catálogo de verbos está en [07-VERBOS](07-VERBOS.md); aquí solo se describe **el estado** sobre el que operan.

---

## 1. Principio: archivos, no nombres históricos

Un registro no es "el acumulador" ni "el contador": es **la celda número N de un archivo físico**. Por eso todos los nombres siguen la misma forma:

```
<Archivo><índice>
```

| Archivo | Nombre | Miembros | Ancho físico |
|---|---|---|---|
| General Purpose Register File | `Gpr` | `Gpr0`–`Gpr15` | 64 |
| Segment Register File | `Srf` | `Srf0`–`Srf5` | 16 |
| Control Register File | `Crf` | `Crf0`–`Crf4`, `Crf8` | 64 |
| Debug Register File | `Drf` | `Drf0`–`Drf7` | 64 |
| Flags Register File | `Frf` | `Frf` | 64 |
| Instruction Pointer Register | `Ipr` | `Ipr` | 64 |
| SIMD Register File | `Simd` | `Simd0`–`Simd31` | 512 |
| K Mask Register File | `Krf` | `Krf0`–`Krf7` | 64 |
| x87 Register File | `Xrf` | `Xrf0`–`Xrf7` | 80 |
| Extended Control Register | `Xcr` | `Xcr0` | 64 |
| Protection Key Register | `Pkru` | `Pkru` | 32 |
| Shadow Stack Pointer | `Ssp` | `Ssp` | 64 |

Además existen **recursos con nombre**, que no son registros pero se comportan como receptores o fuentes:

| Recurso | Forma | Qué es |
|---|---|---|
| `Msr.<nombre>` | identificador | Registro específico de modelo, indexado por número |
| `Port[<addr>]` | dirección de E/S | Puerto de entrada/salida |
| `Stack` | archivo virtual | La pila principal (`Gpr4` + memoria) |
| `Cpu` | unidad | Operaciones sin destino de registro (NOP, PAUSE, HLT) |
| `Fence` | unidad | Barreras de memoria |

---

## 2. GPR — `Gpr0`–`Gpr15`

### 2.1 Vistas

`GprN` es una celda de **64 bits**. Las vistas son ventanas reales sobre esa celda:

| Vista | Bits | Efecto al escribir | Codificación |
|---|---|---|---|
| `GprN` | 0–63 | Reemplaza los 64 bits | REX.W |
| `.low32` | 0–31 | **Zero-extend**: bits 32–63 = 0 | sin REX.W |
| `.low16` | 0–15 | **Merge**: conserva 16–63 | prefijo `0x66` |
| `.low8` | 0–7 | **Merge**: conserva 8–63 | sin prefijo |
| `.high8` | 8–15 | **Merge**; solo `Gpr0`–`Gpr3` | sin prefijo, sin REX |

```fasm
Gpr0.copy(Gpr1)                // 64 bits
Gpr0.low32.copy(Gpr1.low32)    // 32 bits; borra 32–63 de Gpr0
Gpr0.low16.copy(Gpr1.low16)    // 16 bits; conserva 16–63 de Gpr0
Gpr0.low8.copy(Gpr1.low8)      // 8 bits; conserva 8–63 de Gpr0
Gpr0.high8.copy(Gpr1.high8)    // 8 bits en 8–15; solo Gpr0–Gpr3
```

### 2.2 Índice y encoding

El índice coincide con el número físico del registro:

| Índice | `reg` | REX.R | Notas |
|---|---|---|---|
| 0–7 | 0–7 | 0 | Codificación clásica |
| 8–15 | 0–7 | 1 | Requieren REX |

- `.high8` solo existe para `Gpr0`–`Gpr3`; en los demás → `FASM3015`.
- `.low8` de `Gpr4`–`Gpr7` obliga a REX (el encoder lo emite): `FASM3002` si además se usa `.high8`.
- `.low8` de `Gpr8`–`Gpr15` ya requiere REX por el índice.

### 2.3 Roles implícitos

| Registro | Rol físico | Dónde aparece implícito |
|---|---|---|
| `Gpr0` | Acumulador; retorno de función | `multiply`, `divide`, `readTimestamp`, `readMsr`, ABI |
| `Gpr1` | Contador de desplazamiento; índice de MSR; 4.º arg SysV | `shiftLeft`/`shiftRight` con máscara variable, `readMsr`, `flowLoop` |
| `Gpr2` | Retorno alto; 3.er arg SysV | `multiply`, `divide`, `readTimestamp`, `readMsr` |
| `Gpr4` | **Puntero de pila (RSP)** | `Stack.*`, `flowCall`, `flowStackJump`, `push`/`pop` |
| `Gpr5` | **Puntero de marco (RBP)** | `Byte:N [Gpr5 ± disp]` |
| `Gpr6` | Origen de bloque; 2.º arg SysV | `copyBlock`, `compareBlock`, `scanBlock` |
| `Gpr7` | Destino de bloque; 1.er arg SysV | `copyBlock`, `scanBlock` |
| `Gpr4`/`Gpr5` | Base/índice de pila | ABI, frame |

Ninguna de estas apariciones es "magia": la tabla de cada verbo en [07-VERBOS](07-VERBOS.md) declara qué registros lee y escribe.

### 2.4 Roles ABI

| Registro | Win64 | System V |
|---|---|---|
| `Gpr0` | retorno | retorno |
| `Gpr1` | arg 0 | arg 3 |
| `Gpr2` | arg 1; retorno alto | arg 2; retorno alto |
| `Gpr3` | callee-saved | callee-saved |
| `Gpr4` | pila | pila |
| `Gpr5` | callee-saved / frame | callee-saved / frame |
| `Gpr6` | callee-saved | arg 1 |
| `Gpr7` | callee-saved | arg 0 |
| `Gpr8` | arg 2 | arg 4 |
| `Gpr9` | arg 3 | arg 5 |
| `Gpr10` | scratch | scratch |
| `Gpr11` | scratch | scratch |
| `Gpr12`–`Gpr15` | callee-saved | callee-saved |

Volátiles: `Gpr0`, `Gpr1`, `Gpr2`, `Gpr8`, `Gpr9`, `Gpr10`, `Gpr11`. El resto (salvo `Gpr4`) es callee-saved. El detalle está en [12-ABI](12-ABI.md).

---

## 3. SRF — `Srf0`–`Srf5`

Seis selectores de 16 bits. En x64 las bases de `CS/DS/ES/SS` son 0 y sus límites 4 GiB; solo `FS` y `GS` tienen base configurable.

| Miembro | Selector | `sreg` (3 bits) | Escritura |
|---|---|---|---|
| `Srf0` | CS | `1` | **No** por `copy`: solo transferencia de control lejana |
| `Srf1` | DS | `3` | `Srf1.copy(Gpr.low16)` — `MOV DS, r/m16` |
| `Srf2` | ES | `0` | `Srf2.copy(Gpr.low16)` |
| `Srf3` | FS | `4` | `Srf3.copy(Gpr.low16)`; base MSR con `.base` |
| `Srf4` | GS | `5` | `Srf4.copy(Gpr.low16)`; base MSR con `.base` |
| `Srf5` | SS | `2` | `Srf5.copy(Gpr.low16)`; inhibe interrupciones una instrucción |

> **Nota de encoding.** El índice `Srf` **no** es el campo `sreg` de la instrucción. El encoder traduce con esta tabla. Es la única desviación de "índice = número físico" del lenguaje, y existe porque el orden de los selectores en el manual (ES, CS, SS, DS, FS, GS) no coincide con el orden de uso (CS, DS, ES, FS, GS, SS). La traducción es fija y verificada por test.

### 3.1 Base de FS/GS

| Operación | Sintaxis | Instrucción |
|---|---|---|
| Leer base | `Gpr0.copy(Srf3.base)` | `RDFSBASE RAX` |
| Leer base | `Gpr0.copy(Srf4.base)` | `RDGSBASE RAX` |
| Escribir base | `Srf3.base.copy(Gpr0)` | `WRFSBASE RAX` |
| Escribir base | `Srf4.base.copy(Gpr0)` | `WRGSBASE RAX` |

Requieren `Crf4` bit 16 (`FSGSBASE`). Sin él → `FASM3031`.

### 3.2 Transferencia lejana

`Srf0` no se escribe con `copy` (`FASM3030`). Se cambia con un par lejano:

```fasm
(Ipr:Srf0).flowJump(Gpr0)         // JMP FAR
(Ipr:Srf0).flowCall(Gpr0)         // CALL FAR
(Ipr:Srf0).flowStackJump()        // RETF
(Ipr:Srf0).flowInterrupt(vector)  // IRETQ / puerta
```

---

## 4. CRF — `Crf0`–`Crf4`, `Crf8`

| Miembro | Legacy | Bits relevantes | Privilegio |
|---|---|---|---|
| `Crf0` | CR0 | PE 0, MP 1, EM 2, TS 3, ET 4, NE 5, WP 16, AM 18, NW 29, CD 30, PG 31 | ring 0 |
| `Crf1` | CR1 | **Reservado**; lectura → #UD en algunas CPUs, escritura → #GP | — |
| `Crf2` | CR2 | Dirección de fallo de página (64 bits) | ring 0 |
| `Crf3` | CR3 | Base de tablas de página; PCID en bits 0–11 | ring 0 |
| `Crf4` | CR4 | VME 0, PVI 1, TSD 2, DE 3, PSE 4, PAE 5, MCE 6, PGE 7, PCE 8, OSFXSR 9, OSXMMEXCPT 10, UMIP 11, LA57 12, VMXE 13, SMXE 14, **FSGSBASE 16**, PCIDE 17, OSXSAVE 18, SMEP 20, SMAP 21, PKE 22, CET 23, PKS 24 | ring 0 |
| `Crf8` | CR8 | Task Priority Register (TPR, bits 3–0) | ring 0 |

Verbos:

```fasm
Crf0.copy(Gpr0)              // MOV CR0, RAX
Gpr0.copy(Crf0)              // MOV RAX, CR0
Crf4.bit(16, 1)              // setea FSGSBASE (lee-modifica-escribe)
Crf4.fsgsbase(1)             // azúcar tipada equivalente a .bit(16, 1)
```

- `Crf1` → `FASM3040` (reservado).
- Escribir un `Crf` desde ring 3 → `FASM3041`.
- `Crf0.paging(1)` sin `Crf4.pae(1)` en x64 → `FASM3042` (PAE es obligatorio en long mode).
- `Crf0.cacheDisable(1)` con caché activa → advertencia `FASM3043`.
- `Crf4.osfxsr(0)` con `Simd` en uso → `FASM3044` (el SO debe habilitar SSE).

Las vistas con nombre (`Crf4.fsgsbase`, `Crf0.paging`) son **azúcar de bit**: el compilador emite siempre el `MOV` de 64 bits completo, porque no existen instrucciones de bit sobre `CRn`.

---

## 5. DRF — `Drf0`–`Drf7`

Registros de depuración. Solo útiles a ring 0 o con `Drf7` configurado.

| Miembro | Legacy | Uso |
|---|---|---|
| `Drf0`–`Drf3` | DR0–DR3 | Direcciones de watchpoint |
| `Drf4`, `Drf5` | DR4/DR5 | Alias de `Drf6`/`Drf7` si `Crf4.DE = 0`; reservados si `DE = 1` |
| `Drf6` | DR6 | Estado de la última excepción de depuración (B0–B3, BD, BS, BT) |
| `Drf7` | DR7 | Control: L0–L3 (local), G0–G3 (global), RW0–3, LEN0–3, GD, LE, GE |

```fasm
Drf0.copy(Gpr0)              // MOV DR0, RAX
Gpr0.copy(Drf6)              // MOV RAX, DR6
Drf6.clear()                 // MOV DR6, 0 — limpia el estado
```

- `Drf4`/`Drf5` con `Crf4.DE = 1` → `FASM3050`.
- Acceso desde ring 3 → `FASM3051`.

---

## 6. FRF — `Frf` (flags)

### 6.1 Modelo

`Frf` es **un** registro de 64 bits (`RFLAGS`) con bits de distinta naturaleza. No es un entero: es un conjunto de bits con nombre.

| Miembro | Bit | Nombre | Quién lo escribe |
|---|---|---|---|
| `Frf.carry` | 0 | CF | `add`, `sub`, `compare`, `shift*`, `rotate*`, `testBit`, `Frf.carry(...)` |
| `Frf.parity` | 2 | PF | operaciones de 8 bits |
| `Frf.adjust` | 4 | AF | `add`, `sub`, BCD |
| `Frf.zero` | 6 | ZF | casi toda la ALU |
| `Frf.sign` | 7 | SF | casi toda la ALU |
| `Frf.trap` | 8 | TF | `Frf.trap(...)` (ring 0) |
| `Frf.interrupt` | 9 | IF | `Frf.interrupt(...)` (ring 0) |
| `Frf.direction` | 10 | DF | `Frf.direction(...)` |
| `Frf.overflow` | 11 | OF | `add`, `sub`, `compare`, `multiply`, shifts |
| `Frf.iopl` | 12–13 | IOPL | `Frf.iopl(...)` (ring 0) |
| `Frf.nestedTask` | 14 | NT | `flowInterrupt`/`IRET` |
| `Frf.resume` | 16 | RF | control de depuración |
| `Frf.virtual8086` | 17 | VM | ring 0 |
| `Frf.alignmentCheck` | 18 | AC | `Frf.alignmentCheck(...)` (ring 0/Cpl3 con Cr0.AM) |
| `Frf.virtualInterrupt` | 19 | VIF | `Frf.virtualInterrupt(...)` |
| `Frf.virtualInterruptPending` | 20 | VIP | — |
| `Frf.cpuid` | 21 | ID | `Cpu.identify` |

### 6.2 Verbos

| Verbo | Instrucción | Efecto |
|---|---|---|
| `Frf.carry(1)` | `STC` | CF = 1 |
| `Frf.carry(0)` | `CLC` | CF = 0 |
| `Frf.carry(not)` | `CMC` | CF = ¬CF |
| `Frf.direction(1)` | `STD` | DF = 1 |
| `Frf.direction(0)` | `CLD` | DF = 0 |
| `Frf.interrupt(1)` | `STI` | IF = 1 |
| `Frf.interrupt(0)` | `CLI` | IF = 0 |
| `Stack.push(Frf)` | `PUSHFQ` | guarda RFLAGS |
| `Frf.pop(Stack)` | `POPFQ` | restaura RFLAGS |
| `Gpr0.low8.copy(Frf.status8)` | `LAHF` | AH = SF:ZF:0:AF:0:PF:1:CF |
| `Frf.status8.copy(Gpr0.low8)` | `SAHF` | carga SF:ZF:AF:PF:CF desde AH |
| `Frf.clear()` | `XOR` sobre las condiciones | pone CF/PF/AF/ZF/SF/OF a 0 |

Reglas:

- Solo `carry` admite `not` (el hardware solo tiene `CMC`).
- Flags de sistema (`trap`, `interrupt`, `iopl`, `nestedTask`, `virtual8086`, `virtualInterrupt`, `virtualInterruptPending`, `cpuid`, `alignmentCheck`) en ring 3 → `FASM3061`.
- `Frf.low32`/`Frf.low16` → `FASM3064` (no existen `POPFD`/`POPF` en modo largo).
- Leer un flag que la instrucción anterior deja **indefinido** → `FASM4012`.
- Copia de flag individual (`Frf.<flag>`) solo permitida a receptor `.low8` (p.ej. `Gpr0.low8.copy(Frf.zero)` → `SETZ AL`). Para anchos mayores: `Gpr0.low8.copy(Frf.zero); Gpr0.loadUnsigned(Gpr0.low8)`.

### 6.3 Condiciones

`Frf.<flag>` es un predicado booleano; `not Frf.<flag>` lo niega. Las condiciones compuestas se construyen con `and` / `or` y se documentan en [08-FLUJO](08-FLUJO.md).

```fasm
Ipr.flowJump(@reintentar if Frf.zero)
Ipr.flowJump(@fin if not Frf.carry)
Ipr.flowJump(@ajustar if Frf.sign unequal Frf.overflow)   // JL
```

---

## 7. IPR — `Ipr`

Puntero de instrucción. **No** es un GPR.

| Operación | Sintaxis |
|---|---|
| Leer | `Gpr0.copy(Ipr.current)` → `LEA Gpr0, [RIP]` |
| Llamar | `Ipr.flowCall(destino)` |
| Saltar | `Ipr.flowJump(destino [if condición])` |
| Retornar | `Ipr.flowStackJump([Byte:N])` |
| Bucle | `Ipr.flowLoop(destino)` |
| Interrumpir | `Ipr.flowInterrupt(vector)` |
| Syscall | `Ipr.flowSyscall()` |
| Retorno de syscall | `Ipr.flowSysret()` |
| Breakpoint | `Ipr.flowBreakpoint()` |

`Gpr0.copy(Ipr)` y `Ipr.copy(Gpr0)` → `FASM3060`. No existe `mov rip, reg`; fingirlo sería mentir.

---

## 8. SIMD — `Simd0`–`Simd31`

32 contenedores físicos de **512 bits**. El detalle completo está en [10-SIMD](10-SIMD.md); aquí solo el resumen:

| Vista | Bits | Notas |
|---|---|---|
| `SimdN.r128` | 128 | SSE / VEX-128 |
| `SimdN.r256` | 256 | AVX / VEX-256 |
| `SimdN.r512` | 512 | AVX-512 (por defecto, D-3) |
| `SimdN.r*.part{8,16,32,64}` | n/a | Declara el tamaño de elemento |
| `SimdN.low32`, `.low64` | 32/64 | Puente con `Gpr` |

`Simd16`–`Simd31` requieren AVX-512 → `FASM3081`.

---

## 9. KRF — `Krf0`–`Krf7`

Registros de máscara AVX-512. 64 bits cada uno, con vistas según el ancho del opcode.

| Vista | Ancho de máscara | Instrucción |
|---|---|---|
| `.low8` | 8 | `KMOVB` |
| `.low16` | 16 | `KMOVW` |
| `.low32` | 32 | `KMOVD` |
| *(completo)* | 64 | `KMOVQ` |

```fasm
Krf1.low16.copy(Gpr0.low16)                 // KMOVW k1, eax
Krf2.copy(Krf1)                             // KMOVQ k2, k1
Simd0.r512.part{32}.addFloat{Krf1}(Simd1.r512.part{32})
```

`Krf0` como máscara de escritura → `FASM3083` (K0 significa "sin máscara").
Ancho de máscara incompatible con la partición → `FASM3088`.

---

## 10. XRF — `Xrf0`–`Xrf7`

Pila x87 de 8 posiciones de 80 bits. `Xrf0` es el tope lógico (`ST(0)`), `Xrf7` el fondo (`ST(7)`); el puntero `TOP` es implícito. El detalle está en [11-X87](11-X87.md).

`MM0`–`MM7` (MMX) están prohibidos → `FASM3073`.

---

## 11. Recursos especiales

### 11.1 `Xcr0`

| Bit | Nombre | Controla |
|---|---|---|
| 0 | `x87` | Estado x87 |
| 1 | `sse` | XMM |
| 2 | `avx` | YMM |
| 5–7 | `opmask`, `zmmHi256`, `hi16Zmm` | AVX-512 |
| 9 | `pkru` | Protection keys |
| 17–18 | `amxTile`, `amxFp` | AMX (futuro) |

```fasm
Gpr0.copy(Xcr0)              // XGETBV
Xcr0.copy(Gpr10)             // XSETBV
Xcr0.avx(1)                  // azúcar: setea el bit 2
```

`XSETBV` requiere ring 0 → `FASM3070`.

### 11.2 `Pkru`

Registro de 32 bits con 16 claves de 2 bits (AD, WD).

```fasm
Gpr0.low32.copy(Pkru)        // RDPKRU
Pkru.copy(Gpr0.low32)        // WRPKRU
```

### 11.3 `Ssp`

Shadow stack pointer (CET).

```fasm
Gpr0.copy(Ssp)               // RDSSPQ
Ssp.copy(Gpr0)               // WRSSQ
```

### 11.4 `Msr.<nombre>`

Los MSR son registros indexados por número. El lenguaje los expone por **nombre estructural**; el número se materializa en el encoder, no en el código.

| Nombre | MSR | Contenido |
|---|---|---|
| `Msr.fsBase` | `0xC0000100` | Base de FS |
| `Msr.gsBase` | `0xC0000101` | Base de GS |
| `Msr.kernelGsBase` | `0xC0000102` | Base de GS del kernel |
| `Msr.efer` | `0xC0000080` | Extended Feature Enable |
| `Msr.star` | `0xC0000081` | Segmentos de `SYSCALL` |
| `Msr.lstar` | `0xC0000082` | Entrada de `SYSCALL` |
| `Msr.cstar` | `0xC0000083` | Entrada de compatibilidad |
| `Msr.sfmask` | `0xC0000084` | Máscara de flags en `SYSCALL` |
| `Msr.sysenterCs` | `0x174` | `SYSCALL`/`SYSEXIT` (legacy) |
| `Msr.sysenterEsp` | `0x175` | Pila de kernel |
| `Msr.sysenterEip` | `0x176` | Entrada de kernel |
| `Msr.apicBase` | `0x1B` | Base del APIC |
| `Msr.mtrrCap` | `0xFE` | Capacidades MTRR |

```fasm
(Gpr0:Gpr2).readMsr(Msr.efer)      // RDMSR  → EAX:EDX
Msr.efer.write(Gpr0, Gpr2)         // WRMSR  ← EAX:EDX
```

> **Contrato declarado:** `readMsr` y `writeMsr` usan `Gpr1` (ECX) para el índice. El encoder emite el `MOV` del número, que es un **constante de hardware**, no un dato del programa. El clobber de `Gpr1` está declarado en la tabla del verbo, no oculto.

### 11.5 `Port[<addr>]`

Direcciones de E/S (16 bits). La dirección es estructural, como una dirección de memoria.

```fasm
Gpr0.low8.copy(Port[0x60])         // IN AL, 0x60
Port[0x60].copy(Gpr0.low8)         // OUT 0x60, AL
Gpr0.low32.copy(Port[Gpr2.low16])  // IN EAX, DX
```

Requiere `Frf.iopl` suficiente → `FASM3062`.

### 11.6 `Cpu` y `Fence`

| Verbo | Instrucción |
|---|---|
| `Cpu.noOperation()` | `NOP` |
| `Cpu.pause()` | `PAUSE` |
| `Cpu.halt()` | `HLT` (ring 0) |
| `Cpu.serialize()` | `SERIALIZE` (AVX-512) |
| `Cpu.identify()` | `CPUID` (tuple) |
| `Fence.full()` | `MFENCE` |
| `Fence.read()` | `LFENCE` |
| `Fence.write()` | `SFENCE` |
| `Fence.instruction()` | `LFENCE` + `SERIALIZE` donde aplique |

---

## 12. Registros implícitos por verbo

Esta tabla existe para que ningún efecto sea invisible. Cada verbo de [07-VERBOS](07-VERBOS.md) referencia su fila.

| Verbo | Lee | Escribe | Notas |
|---|---|---|---|
| `GprN.shiftLeft(Gpr1.low8)` | `Gpr1.low8` | `GprN` | cuenta variable solo en `Gpr1.low8` |
| `(Gpr2:Gpr0).multiply(x)` | `Gpr0`, `x` | `Gpr0`, `Gpr2` | `RDX:RAX` |
| `(Gpr2:Gpr0).divide(x)` | `Gpr0`, `Gpr2`, `x` | `Gpr0`, `Gpr2` | |
| `Stack.push(x)` | `Gpr4`, `x` | `Gpr4` | `RSP -= 8` |
| `GprN.pop(Stack)` | `Gpr4` | `GprN`, `Gpr4` | `RSP += 8` |
| `Ipr.flowCall(x)` | `Gpr4`, `x` | `Gpr4`, `Ipr` | |
| `Ipr.flowStackJump()` | `Gpr4`, `[Gpr4]` | `Gpr4`, `Ipr` | |
| `Ipr.flowLoop(@x)` | `Gpr1`, `Frf.zero` | `Gpr1`, `Ipr` | `LOOP` |
| `(Gpr0:Gpr2).readTimestamp()` | — | `Gpr0`, `Gpr2` | `TSC` |
| `(Gpr0:Gpr3:Gpr1:Gpr2).identify()` | `Gpr0` | `Gpr0`, `Gpr1`, `Gpr2`, `Gpr3` | `CPUID` |
| `(Gpr0:Gpr2).readMsr(...)` | `Gpr1` | `Gpr0`, `Gpr2` | `RDMSR` |
| `Msr.x.write(...)` | `Gpr1`, args | — | `WRMSR` |
| `(Gpr7:Gpr6).copyBlock(...)` | `Gpr6`, `Gpr7`, `Gpr1` | `Gpr6`, `Gpr7`, `Gpr1` | `REP MOVSB` |
| `Ipr.flowSyscall()` | `Gpr0`, `Gpr1`, `Gpr2`, `Gpr4`, `Gpr11` | `Ipr`, `Gpr11` | depende del SO |
| `Xrf.loadFloat(...)` | — | `Xrf0`, `Xrf.status` | push |
| `Xrf0.storeFloatPop(...)` | `Xrf0` | `Xrf0`, `Xrf.status` | pop |

---

## 13. Nombres prohibidos

Cualquier aparición produce `FASM3012` con la corrección sugerida.

| Prohibido | Canónico |
|---|---|
| `RAX`…`R15`, `EAX`…`R15D`, `AX`…`R15W`, `AL`…`R15B`, `AH`…`BH` | `GprN`, `.low32`, `.low16`, `.low8`, `.high8` |
| `RIP`, `EIP`, `IP` | `Ipr` |
| `RFLAGS`, `EFLAGS`, `FLAGS` | `Frf` |
| `CS`, `DS`, `ES`, `FS`, `GS`, `SS` | `Srf0`…`Srf5` |
| `CR0`…`CR8` | `Crf0`…`Crf8` |
| `DR0`…`DR7` | `Drf0`…`Drf7` |
| `XMM0`…`XMM31`, `YMM*`, `ZMM*` | `SimdN.r128` / `.r256` / `.r512` |
| `K0`…`K7` | `Krf0`…`Krf7` |
| `ST(0)`…`ST(7)`, `MM0`…`MM7` | `Xrf0`…`Xrf7` (MMX prohibido: `FASM3073`) |
| `XCR0` | `Xcr0` |
| `PKRU` | `Pkru` |
| `SSP` | `Ssp` |
| `RAX` como "acumulador" en prosa | `Gpr0` |

---

## 14. Diagnósticos de registros

| Código | Situación |
|---|---|
| `FASM3001` | Verbo no disponible para este archivo de registros |
| `FASM3002` | Mezcla de `.high8` con `.low8` que requiere REX |
| `FASM3012` | Nombre legacy |
| `FASM3013` | Índice de registro fuera de rango |
| `FASM3015` | `.high8` fuera de `Gpr0`–`Gpr3` |
| `FASM3016` | Tupla de destinos en verbo de un solo destino |
| `FASM3030` | Escritura directa a `Srf0` |
| `FASM3031` | `Srf3.base`/`Srf4.base` sin `FSGSBASE` |
| `FASM3040` | `Crf1` reservado |
| `FASM3041` | Escritura a `Crf` fuera de ring 0 |
| `FASM3050` | `Drf4`/`Drf5` con `DE = 1` |
| `FASM3060` | `copy` sobre `Ipr` |
| `FASM3061` | Flag de sistema escrito fuera de ring 0 |
| `FASM3062` | Puerto de E/S sin `IOPL` |
| `FASM3064` | `Frf.low32`/`.low16` en modo largo |
| `FASM3070` | `XSETBV` fuera de ring 0 |
| `FASM3073` | Uso de MMX |
| `FASM3080` | Vista SIMD no soportada por el target |
| `FASM3081` | `Simd16`–`Simd31` sin AVX-512 |
| `FASM3083` | `Krf0` como máscara de escritura |
| `FASM3088` | Ancho de máscara incompatible |

---

## 15. Resumen

- Doce archivos, nombres `<Archivo><índice>`, índice = número físico.
- GPR de 64 bits con cinco vistas; `.low32` zero-extiende, el resto hace merge.
- `Srf0` no se escribe; `Srf3.base`/`Srf4.base` son MSR.
- `Frf` es un registro de bits con nombre, no un entero.
- `Ipr` solo cambia por flujo.
- SIMD es un contenedor de 512 bits con vistas de ancho y partición.
- Todo efecto implícito está declarado en la tabla §12.
- Ningún nombre histórico sobrevive.
