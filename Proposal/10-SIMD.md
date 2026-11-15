# 10 — SIMD

> **Normativo.** `Simd0`–`Simd31` son contenedores físicos de 512 bits. Este documento define las vistas, las particiones, las máscaras, los verbos y las reglas de encoding.

---

## 1. Modelo físico

### 1.1 El contenedor

x64 tiene **32** registros vectoriales de **512 bits** cada uno (con AVX-512; 16 si solo hay AVX2; 8 si solo SSE).

```
Simd0  ┌──────────────────────────────────────────────────────────────┐
       │                    512 bits (ZMM)                            │
       └──────────────────────────────────────────────────────────────┘
         ├──────────── 256 bits (YMM) ────────────┤
         ├───── 128 bits (XMM) ─────┤
```

`Simd0.r128` **no es un registro distinto** de `Simd0.r512`: es una ventana sobre los 128 bits bajos de la misma celda. Escribirla con VEX/EVEX pone a cero los bits 128–511.

### 1.2 Disponibilidad

| Registros | Requiere | Perfil mínimo |
|---|---|---|
| `Simd0`–`Simd15`, `.r128` | SSE2 | `x64-sse2` |
| `Simd0`–`Simd15`, `.r256` | AVX2 | `x64-avx2` |
| `Simd0`–`Simd31`, `.r512` | AVX-512F | `x64-avx512` |

`Simd16`–`Simd31` → `FASM3081` fuera de `x64-avx512`.
`.r512` → `FASM3080` fuera de `x64-avx512`.

---

## 2. Sintaxis canónica

```
SimdN [ "." "r" (128|256|512) ] [ "." "part" "{" (8|16|32|64) "}" ]
```

| Forma | Significado |
|---|---|
| `Simd0` | Contenedor completo (512 bits) |
| `Simd0.r512` | Vista de 512 bits (equivalente a `Simd0`) |
| `Simd0.r256` | Vista de 256 bits |
| `Simd0.r128` | Vista de 128 bits |
| `Simd0.r512.part{32}` | 512 bits divididos en elementos de 32 bits |
| `Simd0.low32` | Los 32 bits bajos (puente con `Gpr`) |
| `Simd0.low64` | Los 64 bits bajos |

### 2.1 Ancho por defecto

Si se omite `.rN`, se asume **`.r512`** (decisión D-3, pendiente de confirmación). El target debe soportar AVX-512; si no, el compilador emite `FASM3080` y sugiere `.r256` o `.r128`.

```fasm
Simd0.addFloat(Simd1)                 // ≡ Simd0.r512.addFloat(Simd1.r512)
Simd0.r256.addFloat(Simd1.r256)       // explícito
```

### 2.2 Partición

`part{N}` declara el tamaño del elemento. **Solo se admiten 8, 16, 32 y 64**, porque son los únicos anchos de elemento que el hardware implementa.

El número de elementos **se deriva**; no se declara:

| Ancho | `part{8}` | `part{16}` | `part{32}` | `part{64}` |
|---|---|---|---|---|
| `.r128` | 16 | 8 | 4 | 2 |
| `.r256` | 32 | 16 | 8 | 4 |
| `.r512` | 64 | 32 | 16 | 8 |

`part{10}` → `FASM3087`. El ancho debe ser divisible por la partición → `FASM3089`.

### 2.3 Partición y verbo

La partición no cambia el opcode por sí sola: el verbo elige la instrucción y la partición debe coincidir con el sufijo del nombre.

| Verbo | Partición implícita | Opcode (r512) |
|---|---|---|
| `addInt` | 8/16/32/64 | `VPADDB/W/D/Q` |
| `addFloat` | 32/64 | `VADDPS/PD` |
| `multiplyInt` | 16/32/64 | `VPMULLW/D/Q` |
| `multiplyFloat` | 32/64 | `VMULPS/PD` |
| `compareEqualInt` | 8/16/32/64 | `VPCMPEQB/W/D/Q` |
| `compareEqualFloat` | 32/64 | `VCMPPS/PD` |
| `shiftLeftInt` | 16/32/64 | `VPSLLW/D/Q` |
| `convertToInt` | 32 (origen FP) | `VCVTTPS2DQ` |

Usar `part{32}` con un verbo que solo admite 64 → `FASM3087`.

---

## 3. Interpretación

El verbo decide:

```fasm
// 16 enteros de 32 bits
Simd0.r512.part{32}.addInt(Simd1.r512.part{32})        // VPADDD

// 16 flotantes de 32 bits
Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})      // VADDPS

// 8 enteros de 64 bits
Simd0.r512.part{64}.addInt(Simd1.r512.part{64})        // VPADDQ

// 8 flotantes de 64 bits
Simd0.r512.part{64}.addFloat(Simd1.r512.part{64})      // VADDPD
```

### 3.1 Sufijos

| Sufijo | Interpretación |
|---|---|
| `Int` | Entero sin signo por defecto |
| `Signed` | Entero con signo (donde el opcode difiere) |
| `Unsigned` | Entero sin signo explícito |
| `Float` | IEEE 754 |
| `Saturating` | Saturación (SSE2/AVX) |
| *(ninguno)* | Operación de bits o de movimiento |

### 3.2 Verbos que no dependen de la interpretación

`copy`, `and`, `or`, `xor`, `not`, `shuffle`, `permute`, `blend`, `extract`, `insert`, `broadcast` operan sobre bits y no llevan sufijo.

---

## 4. Máscaras (`Krf`)

AVX-512 permite enmascarar por elemento con `Krf0`–`Krf7`.

### 4.1 Aplicación

```fasm
Simd0.r512.part{32}.addFloat{Krf1}(Simd1.r512.part{32})     // merge-masking
Simd0.r512.part{32}.addFloat{Krf1.z}(Simd1.r512.part{32})   // zero-masking
```

| Forma | Elementos enmascarados |
|---|---|
| `{KrfN}` | Conservan el valor previo del destino |
| `{KrfN.z}` | Se ponen a 0 |
| sin máscara | Todos se escriben |

### 4.2 Máscaras como destino

Las comparaciones escriben una máscara, no un vector:

```fasm
Simd0.r512.part{32}.compareEqualInt{Krf2}(Simd1.r512.part{32})   // VPCMPEQD k2, zmm0, zmm1
```

El receptor sigue siendo `Simd0` (el operando que se compara); la máscara va entre llaves porque es el destino implícito del opcode.

### 4.3 Ancho de máscara

| Partición y ancho | Elementos | Vista de `Krf` |
|---|---|---|
| `.r128.part{8}` | 16 | `Krf.low16` |
| `.r256.part{32}` | 8 | `Krf.low8` |
| `.r512.part{64}` | 8 | `Krf.low8` |
| `.r512.part{32}` | 16 | `Krf.low16` |
| `.r512.part{8}` | 64 | `Krf` completo |

Una máscara de ancho distinto → `FASM3088`.

### 4.4 Operaciones sobre máscaras

```fasm
Krf0.copy(Krf1)                  // KMOVQ
Krf2.and(Krf1)                   // KANDQ
Krf2.or(Krf1)                    // KORQ
Krf2.xor(Krf1)                   // KXORQ
Krf2.not()                       // KNOTQ
Krf1.low16.copy(Gpr0.low16)      // KMOVW
```

Regla: `Krf0` no puede usarse como predicado (`FASM3083`).

---

## 5. Movimiento de datos

| Verbo | Sintaxis | Opcode |
|---|---|---|
| `copy` | `Simd0.r512.copy(Simd1.r512)` | `VMOVDQA64` |
| `copy` | `Simd0.r512.copy(Byte:64 [x])` | `VMOVDQU64` (no alineado) |
| `copy` | `Byte:64 [x].copy(Simd0.r512)` | `VMOVDQU64` |
| `copy` | `Simd0.r128.copy(Simd1.r128)` | `VMOVDQA` / `VMOVDQU` |
| `broadcast` | `Simd0.r512.part{32}.broadcast(Gpr0.low32)` | `VPBROADCASTD` |
| `broadcast` | `Simd0.r512.part{64}.broadcastFloat(Gpr0)` | `VBROADCASTSD` |
| `extract` | `Gpr0.low32.copy(Simd0.r512.part{32}.element(3))` | `VPEXTRD` |
| `insert` | `Simd0.r512.part{32}.insert(Gpr0.low32, Bit:3)` | `VPINSRD` |
| `permute` | `Simd0.r512.part{32}.permute(Simd1.r512.part{32}, Simd2.r512.part{32})` | `VPERMD` |
| `shuffle` | `Simd0.r512.part{32}.shuffle(Simd1.r512.part{32}, Bit:0x1B)` | `VSHUFPS` |
| `shuffleBytes` | `Simd0.r512.shuffleBytes(Simd1.r512, Simd2.r512)` | `VPSHUFB` |
| `blend` | `Simd0.r512.part{32}.blend(Simd1.r512.part{32}, Krf1)` | `VPBLENDMD` |
| `zipLow` / `zipHigh` | `Simd0.r512.part{32}.zipLow(Simd1.r512.part{32})` | `VPUNPCKLDQ` |
| `reverseElements` | `Simd0.r512.part{32}.reverseElements()` | `VPERMD` con índice invertido |

### 5.1 Alineación

- `Simd.r*.copy(Byte:N [x])` elige `VMOVDQU` salvo que la alineación sea demostrable (dato con `.align(Byte:64)`), en cuyo caso usa `VMOVDQA`.
- Un acceso alineado sobre dirección no alineada → `FASM4015`.

### 5.2 Elemento individual

`element(k)` es una **vista de lectura** de un elemento; `k` es un índice estructural (0..N−1).

```fasm
Gpr0.low32.copy(Simd0.r512.part{32}.element(3))
```

Fuera de rango → `FASM3089`.

---

## 6. Aritmética entera

| Verbo | Particiones | Opcode (r512) |
|---|---|---|
| `addInt` | 8/16/32/64 | `VPADDB/W/D/Q` |
| `subInt` | 8/16/32/64 | `VPSUBB/W/D/Q` |
| `multiplyInt` | 16/32/64 | `VPMULLW/D/Q` |
| `multiplyAddInt` | 16/32 | `VPMADDWD` |
| `addSaturatingInt` | 8/16 | `VPADDSB/W` |
| `subSaturatingInt` | 8/16 | `VPSUBSB/W` |
| `averageInt` | 8/16 | `VPAVGB/W` |
| `minInt` / `maxInt` | 8/16/32/64 | `VPMINS*/VPMaxS*` |
| `minSignedInt` / `maxSignedInt` | 8/16/32 | `VPMINSD` |
| `absoluteInt` | 8/16/32 | `VPABSB/W/D` |
| `negateInt` | 8/16/32/64 | `VPSUBB/W/D/Q` desde cero |
| `sumAbsoluteDifferences` | 8 | `VPSADBW` |

`multiplyInt` con `part{8}` no existe → `FASM3087`.

---

## 7. Aritmética de coma flotante

| Verbo | Particiones | Opcode (r512) |
|---|---|---|
| `addFloat` | 32/64 | `VADDPS/PD` |
| `subFloat` | 32/64 | `VSUBPS/PD` |
| `multiplyFloat` | 32/64 | `VMULPS/PD` |
| `divideFloat` | 32/64 | `VDIVPS/PD` |
| `squareRootFloat` | 32/64 | `VSQRTPS/PD` |
| `multiplyAddFloat` | 32/64 | `VFMADD132PS/PD` |
| `multiplySubFloat` | 32/64 | `VFMSUB132PS/PD` |
| `minFloat` / `maxFloat` | 32/64 | `VMINPS/PD` |
| `roundFloat` | 32/64 | `VRNDSCALEPS/PD` |
| `reciprocalFloat` | 32/64 | `VRCP14PS/PD` |
| `reciprocalSqrtFloat` | 32/64 | `VRSQRT14PS/PD` |
| `horizontalAddFloat` | 32/64 | `VHADDPS/PD` |

### 7.1 Modo de redondeo

```fasm
Simd0.r512.part{32}.roundFloat(Simd1.r512.part{32}, .nearest)
Simd0.r512.part{32}.roundFloat(Simd1.r512.part{32}, .down)
Simd0.r512.part{32}.roundFloat(Simd1.r512.part{32}, .up)
Simd0.r512.part{32}.roundFloat(Simd1.r512.part{32}, .towardZero)
```

Son cuatro identificadores cerrados, no números.

### 7.2 Denormales y excepciones

El estado FP vive en `Mxcsr` (ver §11). El lenguaje no cambia `MXCSR` implícitamente.

---

## 8. Comparaciones

| Verbo | Resultado | Opcode |
|---|---|---|
| `compareEqualInt{KrfN}(x)` | máscara | `VPCMPEQ*` |
| `compareGreaterInt{KrfN}(x)` | máscara | `VPCMPGT*` (con signo) |
| `compareEqualFloat{KrfN}(x)` | máscara | `VCMPPS` predicado 0 |
| `compareLessFloat{KrfN}(x)` | máscara | predicado 1 |
| `compareUnorderedFloat{KrfN}(x)` | máscara | predicado 3 |
| `compareOrderedFloat{KrfN}(x)` | máscara | predicado 7 |

Predicados disponibles como identificadores:

```
.equal .less .lessOrEqual .greater .greaterOrEqual
.unordered .notEqual .ordered .notLess .notLessOrEqual
.notGreater .notGreaterOrEqual
```

En AVX2/x64-avx2, sin máscaras: el resultado es un vector de 0/−1 y se consume con `blend`, `and`, `andNot`.

---

## 9. Conversiones

| Verbo | De → a | Opcode |
|---|---|---|
| `convertToInt` | float → int (truncado) | `VCVTTPS2DQ` |
| `convertToIntRounded` | float → int (redondeo) | `VCVTPS2DQ` |
| `convertToFloat` | int → float | `VCVTDQ2PS` |
| `convertToDouble` | float32 → float64 | `VCVTPS2PD` |
| `convertToSingle` | float64 → float32 | `VCVTPD2PS` |
| `convertUnsignedToFloat` | uint → float | `VCVTUDQ2PS` |
| `convertFloatToUnsigned` | float → uint | `VCVTTPS2UDQ` |
| `widen` / `narrow` | ancho de elemento | `VPMOVZX*` / `VPMOVSX*` |
| `packSaturating` | reducir con saturación | `VPACKSS*`/`VPACKUS*` |

Reglas:

- `convertToInt` satura a `INT_MIN`/`INT_MAX` y no genera excepción (comportamiento `VCVTT*`).
- La conversión es explícita: no hay coerción implícita en `addInt`/`addFloat`.
- Un `part{8}` en `convertToInt` → `FASM3087`.

---

## 10. Desplazamientos y lógica

| Verbo | Sintaxis | Opcode |
|---|---|---|
| `shiftLeftInt` | `Simd0.r512.part{32}.shiftLeftInt(Bit:2)` | `VPSLLD` |
| `shiftRightLogicalInt` | `...shiftRightLogicalInt(Bit:2)` | `VPSRLD` |
| `shiftRightArithmeticInt` | `...shiftRightArithmeticInt(Bit:2)` | `VPSRAD` |
| `shiftLeftInt` | `...shiftLeftInt(Simd1.r128.low64)` | `VPSLLD xmm, xmm, xmm` |
| `and` | `Simd0.r512.and(Simd1.r512)` | `VPANDQ` |
| `or` | `Simd0.r512.or(Simd1.r512)` | `VPORQ` |
| `xor` | `Simd0.r512.xor(Simd1.r512)` | `VPXORQ` |
| `not` | `Simd0.r512.not()` | `VPTERNLOGQ`/`VPANDN` |
| `andNot` | `Simd0.r512.andNot(Simd1.r512)` | `VPANDNQ` |
| `bitwiseTernary` | `Simd0.r512.bitwiseTernary(Simd1.r512, Simd2.r512, Bit:0x96)` | `VPTERNLOGQ` |
| `populationCount` | `Simd0.r512.part{64}.populationCount(Simd1.r512.part{64})` | `VPOPCNTQ` |
| `countLeadingZeros` | `...countLeadingZeros(...)` | `VPLZCNTQ` |

La cuenta de desplazamiento es una **forma** (inmediato) o un registro (cuenta variable por elemento).

---

## 11. Estado MXCSR

`MXCSR` no es un registro del archivo `Simd`; es un registro de control de 32 bits.

```fasm
Gpr0.low32.copy(Mxcsr)            // STMXCSR [mem] + load
Mxcsr.copy(Gpr0.low32)            // LDMXCSR [mem]
```

| Bit | Nombre | Función |
|---|---|---|
| 0–5 | excepciones | IE, DE, ZE, OE, UE, PE |
| 6 | DAZ | Denormals Are Zero |
| 7–12 | máscaras | enmascarar excepciones |
| 13–14 | RC | modo de redondeo |
| 15 | FZ | Flush to Zero |

El lenguaje no modifica `MXCSR` en ninguna operación.

---

## 12. Encoding

### 12.1 Elección de prefijo

| Caso | Forma |
|---|---|
| `.r128`, sin AVX, target v1 | SSE (legacy, 2 operandos) |
| `.r128` o `.r256`, con AVX | VEX (2 o 3 bytes) |
| `.r512`, o máscara, o `Simd16`–`Simd31` | EVEX |

### 12.2 Semántica de la forma legacy

En `x64-sse2` (solo SSE2), `Simd0.r128.addFloat(Simd1.r128)` se codifica como `ADDPS xmm0, xmm1`:

- Dos operandos, destino = fuente 1.
- Los 128 bits altos del contenedor (inexistentes en el hardware de 128 bits) no se tocan.

En `x64-avx2+`, la misma línea se codifica como `VADDPS xmm0, xmm0, xmm1`, que **pone a cero** los bits 128–511. La diferencia se documenta y se advierte con `FASM3086` si el programa mezcla anchos.

### 12.3 Ejemplos de bytes

| Sintaxis | Bytes |
|---|---|
| `Simd0.r128.copy(Simd1.r128)` | `0F 10 C1` (`MOVUPS xmm0, xmm1`) |
| `Simd0.r128.copy(Simd1.r128)` (AVX) | `C5 F8 10 C1` (`VMOVUPS xmm0, xmm1`) |
| `Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})` | `62 F1 7C 48 58 C1` (`VADDPS zmm0, zmm0, zmm1`) |
| `Simd0.r512.part{32}.addFloat{Krf1}(...)` | `62 F1 7C 49 58 C1` (`{k1}`) |
| `Simd0.r512.part{32}.addFloat{Krf1.z}(...)` | `62 F1 7C C9 58 C1` (`{k1}{z}`) |

---

## 13. Diagnósticos SIMD

| Código | Situación |
|---|---|
| `FASM3080` | Vista de ancho no soportada por el target |
| `FASM3081` | `Simd16`–`Simd31` sin AVX-512 |
| `FASM3082` | Verbo no disponible para esa partición |
| `FASM3083` | `Krf0` como máscara de escritura |
| `FASM3084` | Máscara en target sin AVX-512 |
| `FASM3085` | `part{N}` no coincide con el ancho de la vista |
| `FASM3086` | Mezcla de anchos dentro de una secuencia (aviso) |
| `FASM3087` | `part{N}` no admitido para el verbo |
| `FASM3088` | Ancho de máscara incompatible |
| `FASM3089` | Índice de elemento fuera de rango |
| `FASM3090` | Acceso alineado sobre dirección no alineada |

---

## 14. Ejemplo completo

```fasm
routine sumarVectores.abi(win64).returns(Simd) {
    parameter a: Simd
    parameter b: Simd

    // Simd0 = a + b, 16 flotantes de 32 bits
    Simd0.r512.part{32}.copy(a)
    Simd0.r512.part{32}.addFloat(b)

    // Máscara leída de memoria: 16 bits con el patrón de selección
    Krf1.low16.copy(Byte:2 [mascara])
    Simd0.r512.part{32}.multiplyFloat{Krf1}(Simd2.r512.part{32})

    Ipr.flowStackJump()
}
```

---

## 15. Resumen

- 32 contenedores de 512 bits; `.r128`/`.r256` son ventanas, no registros.
- `part{8|16|32|64}` declara el elemento; el número se deriva.
- La interpretación va en el verbo (`addInt` vs `addFloat`).
- Las máscaras `Krf` son predicados o destinos según el verbo.
- `Krf0` no predica.
- `.r512` y `Simd16`–`Simd31` exigen `x64-avx512`.
- La diferencia VEX vs SSE (zero-extend vs merge) se documenta y se advierte.
