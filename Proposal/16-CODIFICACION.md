# 16 — Codificación

> **Normativo.** Cómo una acción FluentASM se convierte en bytes x64. Prefijos, REX, ModRM, SIB, VEX, EVEX, selección de desplazamiento, relocaciones, imagen plana y el mapa de enlazado. El encoder es determinista: la misma acción y el mismo target producen siempre la misma secuencia.

---

## 1. Principios del encoder

| # | Principio | Consecuencia |
|---|---|---|
| 1 | **Forma canónica única** | Para cada acción existe exactamente una instrucción elegida. No hay "variantes equivalentes" que el programador elija por preferencia. |
| 2 | **Lo más corto que sea legal** | Se prefiere `disp8` a `disp32`, `rel8` a `rel32`, VEX a EVEX cuando ambos codifican lo mismo, `83 /5 ib` a `81 /5 id`. |
| 3 | **Sin optimización posterior** | El encoder no reordena, no fusiona ni elimina instrucciones. Lo único que se relaja es el tamaño de los saltos (§10.3), porque depende del layout. |
| 4 | **Determinismo** | Dos compilaciones del mismo fuente con el mismo target dan archivos idénticos byte a byte. |
| 5 | **Sin bytes implícitos ocultos** | Todo byte emitido corresponde a una acción o a una regla documentada aquí. El listado `--map` lo expone. |
| 6 | **Fallo explícito** | Si una acción no tiene encoding para el target, se emite un error (`FASM6001`) y **no se emite ninguna instrucción**. Nunca se inventa una secuencia. |

`little-endian` en todos los campos multibyte. Los inmediatos y desplazamientos son complemento a dos.

---

## 2. Anatomía de una instrucción legacy

```
[prefijos] [REX] [opcode 1-3 B] [ModRM] [SIB] [disp] [imm]
```

| Campo | Bytes | Cuándo |
|---|---:|---|
| Prefijos legacy | 0–4 | `LOCK` (`F0`), `REP` (`F3`), `REPE` (`F3`), `REPNE` (`F2`), segmento (`2E/36/3E/26/64/65`), tamaño de operando (`66`), tamaño de dirección (`67`) |
| REX | 0–1 | Siempre que se use un registro `Gpr8`–`Gpr15`, o un ancho de 64 bits, o `.low8` extendido |
| Opcode | 1–3 | `0F`, `0F 38`, `0F 3A` añaden bytes |
| ModRM | 0–1 | Casi todas |
| SIB | 0–1 | Solo si `ModRM.rm = 100` y `mod ≠ 11` |
| Desplazamiento | 0, 1 o 4 | Según `mod` y base |
| Inmediato | 0, 1, 2, 4 u 8 | Según el opcode |

**Orden obligatorio:** los prefijos legacy van antes que REX, y REX debe ser el byte inmediatamente anterior al opcode. Una instrucción con `REX` y un prefijo mal ordenado es inválida aunque el resto sea correcto.

---

## 3. REX

```
0100 W R X B
```

| Bit | Extiende | Efecto |
|---|---|---|
| `W` | ancho | `1` = operando de 64 bits |
| `R` | campo `reg` de ModRM | `+8` al número de registro |
| `X` | campo `index` de SIB | `+8` |
| `B` | campo `rm` de ModRM, o `reg` del opcode, o `base` de SIB | `+8` |

### 3.1 Cuándo se emite

| Situación | REX |
|---|---|
| Cualquier operando de 64 bits | `W=1` (mínimo `48`) |
| Registro `Gpr8`–`Gpr15`, `Simd8`–`Simd15` | bit `R`, `X` o `B` correspondiente |
| `.low8` de `Gpr4`–`Gpr7` (SPL, BPL, SIL, DIL) | REX **sin** `W`, para distinguirlo de AH/CH/DH/BH |
| Operandos de 8/16/32 bits con registros 0–7 | **no** se emite |

### 3.2 La trampa de los bytes altos

`AH`, `CH`, `DH` y `BH` son los bytes altos de `Gpr0`–`Gpr3`. En FluentASM se escriben `.high8`. Un `REX` presente convierte `100`–`111` en SPL/BPL/SIL/DIL, así que:

- `.high8` **prohíbe** REX: no puede combinarse con `Gpr8`–`Gpr15`, ni con `W=1`, ni con `.low8` de `Gpr4`–`Gpr7`.
- Violación → `FASM3002`.
- `Gpr4`–`Gpr7` **no tienen** `.high8` (`FASM3015`).

### 3.3 Ejemplos

| Acción | Bytes | REX |
|---|---|---|
| `Gpr0.copy(Gpr1)` | `48 89 C8` | `48` (`W=1`) |
| `Gpr8.copy(Gpr9)` | `4D 89 C8` | `4D` (`W=1,R=1,B=1`) |
| `Gpr0.low8.copy(Gpr4.low8)` | `40 88 E0` | `40` (REX vacío, para SPL) |
| `Gpr0.high8.copy(Gpr1.low8)` | `88 C8` | ninguno |

---

## 4. ModRM

```
mod reg rm
 2   3   3
```

| `mod` | Significado | Desplazamiento |
|---|---|---|
| `00` | `[rm]` | ninguno; `rm=101` ⇒ `disp32`; `rm=100` ⇒ SIB |
| `01` | `[rm + disp8]` | 1 byte con signo |
| `10` | `[rm + disp32]` | 4 bytes con signo |
| `11` | registro | ninguno |

`reg` es el registro destino en la mayoría de opcodes; en algunos (`8B`, `89`, `03`, `2B`) es el otro operando. La tabla de [07-VERBOS](07-VERBOS.md) fija cuál es cuál por verbo; el encoder no decide.

### 4.1 Casos especiales en 64 bits

| `mod` | `rm` | Interpretación |
|---|---|---|
| `00` | `101` | `[RIP + disp32]` — **RIP-relativo** |
| `00` | `100` | SIB obligatorio |
| `00`/`01`/`10` | `100` + SIB `base=101`, `index=100` | `disp32` absoluto (sin base) |

---

## 5. SIB

```
scale index base
  2     3     3
```

| `scale` | Factor |
|---|---|
| `00` | 1 |
| `01` | 2 |
| `10` | 4 |
| `11` | 8 |

Reglas:

- `index = 100` significa **sin índice** (si `REX.X = 0`). Es la forma de codificar `[base + disp]` cuando `base` es `Gpr4` (RSP).
- `base = 101` con `mod = 00` significa **sin base** y exige `disp32`. Es la forma de una dirección absoluta.
- `Gpr4` (RSP) **no puede ser índice**; el checker lo rechaza con `FASM4003`. Puede ser base.
- El índice y la base son siempre `GprN` completos (`FASM4008`). Un `.low32` como índice no es representable.

### 5.1 Algoritmo de direccionamiento

El checker ya normalizó la dirección a `base + index*scale + disp` (doc 09 §3):

```
si base = ∅ y index = ∅:
    emitir ModRM(mod=00, rm=101), disp32        // RIP-relativo si el símbolo lo pide
si base ≠ ∅ y index = ∅ y base ≠ Gpr4:
    si disp = 0 y base ≠ Gpr5:  mod=00, rm=base
    si disp ∈ [-128,127]:       mod=01, rm=base, disp8
    si no:                      mod=10, rm=base, disp32
si base ≠ ∅ y index = ∅ y base = Gpr4:
    SIB(scale=0, index=100, base=100)
    si disp = 0:  mod=00
    si disp8:     mod=01
    si no:        mod=10
si index ≠ ∅:
    SIB(scale, index, base si existe)
    mod=00 si base ≠ ∅ y disp = 0 y base ≠ Gpr5
    mod=01 si disp8
    mod=10 si disp32
    mod=00 con base=101 si no hay base
```

> `base = Gpr5` (RBP) con `disp = 0` **no** se puede codificar como `mod=00` (se interpretaría como RIP-relativo). El encoder fuerza `mod=01` con `disp8 = 0`.

### 5.2 Selección del desplazamiento

| Condición | Forma | Bytes de disp |
|---|---|---:|
| `disp = 0`, base legal | sin disp | 0 |
| `−128 ≤ disp ≤ 127` | `disp8` | 1 |
| `−2³¹ ≤ disp ≤ 2³¹−1` | `disp32` | 4 |
| fuera de rango | error `FASM4006` | — |

Para EVEX con `b = 1`, el `disp8` se **escala** por el tamaño de tupla `N` (§8.4) y puede representar hasta `127·N`.

### 5.3 Ejemplos

| Dirección | Bytes de ModRM/SIB/disp | Nota |
|---|---|---|
| `[Gpr6]` | `30` | `mod=00, rm=110` |
| `[Gpr6 + 8]` | `70 08` | `mod=01, rm=110` |
| `[Gpr6 + 0x1234]` | `B0 34 12 00 00` | `mod=10` |
| `[Gpr4]` | `24 24` | SIB, `base=100` |
| `[Gpr4 + Gpr3*4 + 16]` | `A4 9C 10` | SIB `scale=10, index=011, base=100` |
| `[Gpr3*8]` | `04 DD 00 00 00 00` | sin base, `base=101` |
| `[RIP + d]` | `05 d32` | `mod=00, rm=101` |

---

## 6. Direccionamiento simbólico

### 6.1 RIP-relativo por defecto

Toda referencia a un símbolo de datos o a una rutina se emite **RIP-relativa**:

```fasm
Gpr0.copy(Byte:8 [tabla])      // 48 8B 05 d32
Ipr.flowCall(procesar)         // E8 r32
Gpr0.loadAddress(@fin)         // 48 8D 05 d32
```

El desplazamiento se calcula como:

```
disp32 = dirección_del_símbolo − (offset_de_la_instrucción + longitud_total)
```

Es decir, relativo a la **siguiente** instrucción. Rango: `±2 GiB`. Fuera de rango → `FASM4020` para flujo, `FASM4006` para datos.

### 6.2 Absoluto

`absolute(símbolo)` fuerza una dirección de 32 bits sin base:

```fasm
Gpr0.copy(Byte:8 [absolute(tabla)])
```

Emite SIB `base=101, index=100` + `disp32`, con la dirección virtual del símbolo. Requiere que la imagen esté cargada por debajo de 4 GiB. En imagen PIC → `FASM5008`. Este modo existe para kernels y *bootloaders*; el código de usuario no lo necesita.

### 6.3 El punto de entrada

Si la rutina de entrada no está en el offset 0 de `.execute`, el enlazador inserta un **stub**:

```
0:  E9 r32       JMP <entrada>
```

cinco bytes al inicio de `.execute`, y desplaza todo lo demás. Esto mantiene la regla de que el cargador salta a la dirección base de la imagen. El stub aparece en `--map` como `__entry_stub`.

---

## 7. VEX

Se usa para AVX/AVX2, BMI1/BMI2, FMA y todo lo que no requiera AVX-512.

### 7.1 VEX de 2 bytes (`C5`)

```
C5  R̄ v̄v̄v̄v̄ L pp
```

| Campo | Bits | Significado |
|---|---|---|
| `R̄` | 7 | inverso de `R` (registro `reg` ≥ 8) |
| `v̄v̄v̄v̄` | 6–3 | inverso del registro fuente 1 |
| `L` | 2 | 0 = 128, 1 = 256 |
| `pp` | 1–0 | 00 = ninguno, 01 = `66`, 10 = `F3`, 11 = `F2` |

Solo se puede usar si `X = 0`, `B = 0` y `W` no importa (mapa `0F`). Es la forma más corta.

### 7.2 VEX de 3 bytes (`C4`)

```
C4  R̄ X̄ B̄ mmmmm  W v̄v̄v̄v̄ L pp
```

`mmmmm`: `00001` = `0F`, `00010` = `0F38`, `00011` = `0F3A`.

### 7.3 Regla de elección

El encoder emite VEX de 2 bytes cuando `mmmmm = 00001`, `X = 0`, `B = 0`; si no, VEX de 3 bytes. Ejemplo:

| Acción | Bytes |
|---|---|
| `Simd0.r256.part{32}.addFloat(Simd1.r256.part{32})` | `C5 FC 58 C1` |
| `Gpr0.andNot(Gpr1)` (BMI1) | `C4 E2 F8 F2 C1` |

---

## 8. EVEX

Obligatorio si: se usa `.r512`, una máscara `Krf`, `Simd16`–`Simd31`, o *broadcast*/*rounding* embebido. Prohibido si VEX puede codificar lo mismo (regla 2).

```
62  P0  P1  P2  opcode  ModRM  [SIB]  [disp]  [imm]
```

### 8.1 P0

```
R̄ X̄ B̄ R̄' 0 0 mm
```

`mm`: `01` = `0F`, `10` = `0F38`, `11` = `0F3A`.

### 8.2 P1

```
W v̄v̄v̄v̄ 1 pp
```

`pp`: `00` = ninguno, `01` = `66`, `10` = `F3`, `11` = `F2`. El bit 2 es siempre `1`.

### 8.3 P2

```
z L'L b V̄' aaa
```

| Campo | Bits | Significado |
|---|---|---|
| `z` | 7 | 1 = *zero-masking*, 0 = *merge-masking* |
| `L'L` | 6–5 | `00` = 128, `01` = 256, `10` = 512 |
| `b` | 4 | *broadcast* o *rounding*; además activa `disp8*N` |
| `V̄'` | 3 | bit alto inverso de `vvvv` |
| `aaa` | 2–0 | registro `Krf` de máscara (0 = sin máscara) |

`Krf0` **no** puede ser máscara de escritura (`FASM3083`): `aaa=000` significa "sin máscara", no "Krf0".

### 8.4 `disp8*N`

Cuando `b = 1` y `mod = 01`, el `disp8` se multiplica por `N` (tamaño de tupla):

| Forma | `N` |
|---|---:|
| `.r512` completo | 64 |
| `.r256` completo | 32 |
| `.r128` completo | 16 |
| `.part{32}`/`.part{64}` escalar | 4 / 8 |
| `.part{8}`/`.part{16}` escalar | 1 / 2 |

El encoder usa `disp8*N` si `disp` es múltiplo de `N` y `disp / N ∈ [−128, 127]`; si no, `disp32`.

### 8.5 Ejemplo desglosado

`Simd0.r512.part{32}.addFloat(Byte:64 [b])`:

| Byte | Valor | Campos |
|---|---|---|
| firma | `62` | EVEX |
| P0 | `F1` | `R̄ X̄ B̄ R̄'` = `1111`, `mm = 01` |
| P1 | `7C` | `W = 0`, `v̄v̄v̄v̄ = 1111` (zmm0), `pp = 00` |
| P2 | `48` | `z = 0`, `L'L = 10` (512), `b = 0`, `V̄' = 1`, `aaa = 000` |
| opcode | `58` | `VADDPS` |
| ModRM | `05` | `mod=00, reg=000, rm=101` (RIP) |
| disp32 | `d32` | — |

Total: 10 bytes.

---

## 9. Inmediatos

FluentASM no emite inmediatos de **dato** (`FASM2007`). Emite inmediatos **estructurales**, y siempre con la forma más corta:

| Acción | Inmediato | Forma |
|---|---|---|
| `Gpr0.shiftLeft(Bit:3)` | cuenta | `C1 /4 ib` (3 B) |
| `Gpr0.shiftLeft(Bit:1)` | cuenta 1 | `D1 /4` (2 B) |
| `Gpr0.testBit(Bit:7)` | índice | `0F BA /4 ib` |
| `Simd0...insert(Gpr0.low32, Bit:3)` | índice | `imm8` |
| `Ipr.flowInterrupt(0x80)` | vector | `CD ib` |
| `Port[0x60].copy(Gpr0.low8)` | puerto | `EE` o `E6 ib` |
| `SUB Gpr4, N` (frame ≤ 127) | tamaño | `83 /5 ib` |
| `SUB Gpr4, N` (frame > 127) | tamaño | `81 /5 id` |
| `Stack.reserve(Byte:32)` | capacidad | `83 /5 ib` |
| `copyBlock(Byte:64 [...])` | cuenta | `B9 id` + `REP MOVSB` |

### 9.1 Materialización de la cuenta de bloque

`REP MOVSB` lee la cuenta de `RCX`. La capacidad `Byte:64` es forma, pero el hardware necesita el número. El encoder emite:

```
B9 40 00 00 00     MOV ECX, 64
F3 A4              REP MOVSB
```

El `MOV ECX` es la única instrucción que materializa una forma en un registro, y se marca con `FASM8106` (info). Si el programador pasa la cuenta como registro (`copyBlock(..., Gpr1)`), no se materializa nada.

### 9.2 Lo que nunca se materializa

| Forma | Por qué no se materializa |
|---|---|
| `part{32}` | va en el opcode (prefijo/EVEX), no en un inmediato |
| `.r512` | va en `L'L` |
| Máscara `Krf1` | va en `aaa` |
| Capacidad `Byte:8` de un acceso | va en el opcode (`MOV r64, m64`) |
| Alineación `Byte:16` | produce relleno, no instrucción |
| Estado de flag `1`/`0` | selecciona `STC`/`CLC`, no `MOV` |

---

## 10. Relocaciones y resolución

### 10.1 Modelo

La imagen es **plana y autocontenida**: no hay enlazador dinámico, no hay GOT/PLT, no hay símbolos externos (`FASM5001`). Todas las referencias se resuelven en tiempo de compilación.

| Referencia | Relocación | Campo |
|---|---|---|
| Datos RIP-relativos | `R_X64_PC32` | `disp32` |
| Llamada o salto cercano | `R_X64_PC32` | `rel32` |
| Salto corto | `R_X64_PC8` | `rel8` |
| Dirección absoluta | `R_X64_32` | `disp32` |
| `.entry` | `R_X64_32` | stub |

### 10.2 Dos pasadas

1. **Layout.** Asignar offset y tamaño a cada sección, rutina, instrucción, dato y símbolo. Los tamaños de instrucción que dependen de un salto se estiman primero en su forma **larga**.
2. **Relajación.** Sustituir saltos largos por cortos mientras quepan, hasta punto fijo (§10.3).
3. **Emisión.** Escribir bytes con los desplazamientos definitivos.

### 10.3 Relajación de saltos

Solo cambian de tamaño `flowJump` y las condiciones:

| Forma | Largo | Corto |
|---|---:|---:|
| `JMP` | `E9 r32` (5) | `EB r8` (2) |
| `Jcc` | `0F 8x r32` (6) | `7x r8` (2) |
| `CALL` | `E8 r32` (5) | no existe |
| `LOOP` | `E2 r8` (2) | único (sin forma larga) |

Algoritmo:

```
repetir:
    cambiar = falso
    para cada salto en orden de offset:
        si forma_actual = larga y cabe en corta:
            acortar; cambiar = verdadero
    hasta que no cambiar
```

Acortar un salto mueve los offsets siguientes y puede hacer que **otro** salto quepa. El proceso converge porque cada paso reduce el tamaño total. Si en una pasada un salto oscila (no puede ocurrir con este algoritmo, que solo acorta), se fija en largo y se emite `FASM4011`.

`LOOP` no tiene forma larga: si el cuerpo mide más de 127 bytes, `FASM4022` y el programador debe reescribirlo con `decrement` + `flowJump`.

### 10.4 Saltos fuera de rango

| Caso | Resultado |
|---|---|
| `rel8` no cabe | se promueve a `rel32` (`FASM4011` info) |
| `rel32` no cabe (±2 GiB) | `FASM4020` error |
| Destino en otra sección ejecutable | imposible: solo hay `.execute` |
| Destino externo | `FASM5001` |

---

## 11. Imagen plana

### 11.1 Orden de secciones

```
[ .execute ] [ .rodata ] [ .data ] [ .bss ]
```

Es fijo (D-015), independientemente del orden de declaración en el fuente. El linker ordena y avisa si una sección está vacía (`FASM8106` informativo solo para datos).

### 11.2 Alineación y relleno

- Cada sección empieza en un múltiplo de su `.align(Byte:N)` (por defecto 16).
- El relleno entre secciones es `0x00` en datos y `0x90` (NOP) en `.execute`.
- `.bss` no ocupa bytes en el archivo; su tamaño se declara en el encabezado de carga (en una imagen plana sin cabecera, el cargador debe saberlo; el `--map` lo expone).

### 11.3 Encabezado

La imagen plana **no** lleva cabecera PE/ELF por defecto. El programador puede pedir `.image(elf64)` o `.image(pe64)` en la cabecera del programa; entonces el linker añade la cabecera mínima y ajusta las direcciones virtuales. Sin `.image`, el archivo es un volcado directo de las secciones.

### 11.4 Ejemplo de mapa

```
$ fasm build --map
Sections
  .execute   off=0x00000000  size=0x00000030  align=16  flags=R+X
  .rodata    off=0x00000030  size=0x00000008  align=16  flags=R
  .data      off=0x00000040  size=0x00000008  align=16  flags=R+W
Symbols
  main        .execute  0x00000000  size=33  GLOBAL  (entry)
  factorial   .execute  0x00000021  size=15  LOCAL
  n           .rodata   0x00000030  size=8   LOCAL
  resultado   .data     0x00000040  size=8   LOCAL
```

---

## 12. Diagnósticos del encoder

| Código | Situación |
|---|---|
| `FASM4003` | `Gpr4` como índice SIB |
| `FASM4004` | escala distinta de 1/2/4/8 |
| `FASM4006` | desplazamiento o dirección fuera de 32 bits |
| `FASM4011` | salto `rel8` promovido a `rel32` (info) |
| `FASM4020` | salto fuera de `±2 GiB` |
| `FASM4021` | `flowStackJump(Byte:N)` con `N` no múltiplo de 8 |
| `FASM4022` | cuerpo de `loop` mayor de 127 bytes |
| `FASM4026` | memoria → memoria sin registro intermedio |
| `FASM4028` | operación que requiere scratch no declarado |
| `FASM4029` | dirección no representable en un SIB |
| `FASM5008` | dirección absoluta en imagen PIC |
| `FASM6001` | instrucción no disponible en el target |
| `FASM9002` | verbo sin plantilla de encoding (bug interno) |

---

## 13. Determinismo y verificación

1. **Orden estable.** Las instrucciones se emiten en el orden del AST; los símbolos se resuelven en orden de declaración.
2. **Sin aleatoriedad.** No hay *hash seeds*, ni orden de diccionario, ni concurrencia en la emisión.
3. **Prueba de oro.** `tests/golden` compara hex; una diferencia es un fallo, no un "cambio aceptable".
4. **Doble compilación.** `fasm build && fasm build` debe producir `sha256` idénticos.
5. **Ida y vuelta.** `fasm disasm` reconstruye acciones equivalentes, no necesariamente idénticas en texto; la equivalencia se comprueba por bytes.

---

## 14. Resumen

- Legacy: prefijos → REX → opcode → ModRM → SIB → disp → imm.
- REX es obligatorio para 64 bits y para registros ≥ 8; incompatible con `.high8`.
- ModRM/SIB tienen un algoritmo único y determinista; `Gpr5` con `disp=0` fuerza `disp8=0`; `Gpr4` no puede ser índice.
- VEX si cabe; EVEX solo cuando aporta algo (512, máscara, registro alto, broadcast).
- Los símbolos son RIP-relativos por defecto; absoluto es una excepción explícita y PIC-hostil.
- Los únicos inmediatos son estructurales; `copyBlock` materializa su capacidad en `RCX`, y se marca.
- La imagen es plana, ordenada, alineada y sin cabecera salvo petición explícita.
- Todo byte tiene una regla y un diagnóstico.
