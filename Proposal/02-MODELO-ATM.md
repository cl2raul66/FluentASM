# 02 — Modelo ATM (Assembly Type Model)

> **Normativo.** Este documento define las únicas magnitudes de tipo del lenguaje, sus contextos, sus reglas de composición y los diagnósticos asociados. Todo lo que el checker debe aceptar o rechazar está aquí.

---

## 1. Qué es el ATM y qué no es

El **ATM** (Assembly Type Model) responde a una sola pregunta:

> ¿Qué **forma** tiene este contenedor?

No responde a "¿qué significa este valor?", ni a "¿es un entero o un flotante?", ni a "¿es un puntero?". Esas preguntas pertenecen al **verbo** (D-003) y al **programa**, no al tipo.

| Pregunta | La responde | Ejemplo |
|---|---|---|
| ¿Cuántos bits hay? | ATM | `Bit:32`, `Byte:8` |
| ¿Cómo se interpretan? | Verbo | `addInt` vs `addFloat` |
| ¿Dónde están? | Receptor / fuente | `Gpr0`, `Byte:8 [x]` |
| ¿De dónde vienen? | Programa | parámetro, memoria, hardware |

El ATM es deliberadamente **pobre**: dos magnitudes y una regla de contextos. Toda la riqueza del lenguaje está en los verbos y en los archivos de registros.

---

## 2. Las dos magnitudes primitivas

| Magnitud | Notación | Significado | Contexto natural |
|---|---|---|---|
| **Bit** | `Bit:N` | Ancho de `N` bits | Registros, flags, particiones, desplazamientos, anchos de operación |
| **Byte** | `Byte:N` | Extensión de `N` bytes | Memoria, offsets, capacidades, alineación, pila |

Relación exacta y única:

```
1 Byte = 8 Bit
```

No hay una tercera magnitud. No hay `Word`, `DWord`, `QWord`, `TByte`, `OWord`, `YWord`, `ZWord`. Cuando se necesita un ancho en bytes se escribe `Byte:2`, `Byte:4`, `Byte:8`, `Byte:10`, `Byte:16`, `Byte:32`, `Byte:64`.

---

## 3. `N` es forma, no dato

En `Byte:8`, el `8` **no es un dato del programa**: es la forma del contenedor. Es estructural, igual que el índice de un registro.

Esto es lo que permite tener literales numéricos en el lenguaje sin violar D-001. El compilador distingue por **posición sintáctica**:

| Posición | Naturaleza | Ejemplo |
|---|---|---|
| Sufijo de magnitud | Forma | `Byte:8`, `Bit:64` |
| Índice de registro | Forma | `Gpr0`, `Simd3`, `Xrf2` |
| Parámetro de partición | Forma | `part{32}` |
| Cuenta de desplazamiento | Forma | `shiftLeft(3)`, `rotateRight(8)` |
| Índice de bit | Forma | `testBit(7)` |
| Estado de flag | Forma | `Frf.carry(1)` |
| Alineación | Forma | `align(Byte:16)` |
| Desplazamiento de dirección | Forma (offset) | `Byte:8 [Gpr5 - 16]` |
| Argumento de verbo aritmético | **Dato** | `Gpr0.add(42)` ✗ |
| Payload de sección de datos | **Dato** (bootstrap) | `data x: Byte:8 = 42` ⚠ D-1 |

La tabla completa de posiciones está en [06-LITERALES](06-LITERALES.md). El ATM solo se ocupa de que `N` sea un número **entero positivo válido para su contexto**.

---

## 4. Múltiplos binarios

Los múltiplos son potencias de 2, no potencias de 10. `Kilo` significa 1024, siempre.

| Prefijo | Factor | En Bit | En Byte |
|---|---|---|---|
| *(ninguno)* | 2⁰ | `Bit` | `Byte` |
| `Kilo` | 2¹⁰ = 1 024 | `KiloBit` | `KiloByte` |
| `Mega` | 2²⁰ = 1 048 576 | `MegaBit` | `MegaByte` |
| `Giga` | 2³⁰ = 1 073 741 824 | `GigaBit` | `GigaByte` |
| `Tera` | 2⁴⁰ = 1 099 511 627 776 | `TeraBit` | `TeraByte` |

Formas válidas:

```fasm
KiloByte:4          // 4 KiB = 4 096 Byte
MegaByte:2          // 2 MiB = 2 097 152 Byte
KiloBit:512         // 512 Kib = 524 288 Bit
```

Los múltiplos existen para **capacidades** (tamaños de región, buffers, frames, cuentas de elementos de gran escala), no para anchos de operación. `Byte:KiloByte` no es válido: un ancho de acceso debe ser un valor pequeño y direccionable.

**Regla:** un múltiplo solo se admite en posiciones de **capacidad**. En un ancho de acceso o de registro se usa la forma base (`Byte:64`, no `KiloByte:0.0625`).

---

## 5. Anchos válidos

No todo `N` es físico. El hardware solo sabe acceder a ciertos anchos, y el ATM los hace explícitos.

### 5.1 Anchos de operación (`Bit:N`)

| N | Uso |
|---|---|
| 1 | Flags, bits individuales (`testBit`, `setBit`) |
| 8 | Byte de registro, GPR `.low8` |
| 16 | `.low16`, selectores `Srf` |
| 32 | `.low32`, máscaras `Krf.low32` |
| 64 | GPR completo, direcciones, `Krf` completo |
| 80 | Contenedor x87 (`XrfN`) |
| 128 / 256 / 512 | Vistas SIMD (`.r128`, `.r256`, `.r512`) |

Cualquier otro `Bit:N` en un verbo de operación → `FASM3022`.

### 5.2 Anchos de acceso a memoria (`Byte:N`)

| N | Instrucción base | Notas |
|---|---|---|
| 1 | `MOV r8, m8` | |
| 2 | `MOV r16, m16` | |
| 4 | `MOV r32, m32` | |
| 8 | `MOV r64, m64` | |
| 10 | `FLD m80` / `FSTP m80` | Solo x87 |
| 16 | `MOVDQA` / `VMOVDQU` | SIMD 128 |
| 32 | `VMOVDQU ymm` | SIMD 256 |
| 64 | `VMOVDQU64 zmm` | SIMD 512 |

Un acceso de otro tamaño → `FASM3023`. Para mover regiones mayores se usan verbos de bloque (`copyBlock`, `fillBlock`) con capacidad en `Byte:N` y punteros en GPR.

### 5.3 Capacidades (`Byte:N` / múltiplos)

Una **capacidad** puede ser casi cualquier entero positivo (hasta 2⁶³−1). Se usa en:

- `.frame(Byte:N)`, `.shadow(1)`, `.redzone(1)`
- `local x: Byte:N`
- `data x: Byte:N`
- `Stack.reserve(Byte:N)`
- `Byte:N [símbolo]` cuando `N` es un ancho de acceso (aquí sí aplica la tabla 5.2)

La capacidad de una declaración no tiene que ser potencia de 2; la alineación sí (véase [09-MEMORIA](09-MEMORIA.md)).

---

## 6. Contextos: Bit vs Byte

El error más común de un ensamblador casero es mezclar bits y bytes. El ATM lo prohíbe con dos diagnósticos simétricos.

| Contexto | Magnitud | Ejemplos |
|---|---|---|
| Registro | `Bit` | `Gpr0`, `.low32`, `Simd0.r512` |
| ALU / verbo | `Bit` | `add`, `shiftLeft`, `part{32}` |
| Memoria | `Byte` | `Byte:8 [x]`, `Byte:64 [Gpr1]` |
| Offset | `Byte` | `[x + 16]` |
| Capacidad | `Byte` | `.frame(Byte:32)` |
| Alineación | `Byte` | `align(Byte:16)` |
| Pila | `Byte` | `Stack.reserve(Byte:8)` |
| Puerto | — | `Port[0x60]` (identificador, no magnitud) |

Reglas:

- `Byte:N` en un contexto de registro → `FASM3020` (`Byte` usado donde se espera `Bit`).
- `Bit:N` en un contexto de memoria → `FASM3021` (`Bit` usado donde se espera `Byte`).
- Un desplazamiento de dirección es siempre `Byte`; un desplazamiento de bits es siempre `Bit` (cuenta).

```fasm
Gpr0.shiftLeft(Bit:3)                  // ✓ cuenta de bits
Byte:8 [x + 16].copy(Gpr0)            // ✓ offset en bytes
Gpr0.copy(Byte:8 [x])                 // ✓ acceso de 8 bytes
Gpr0.add(Bit:8 [x])                   // ✗ FASM3021
Gpr0.shiftLeft(Byte:3)                 // ✗ FASM3020
```

---

## 7. Interpretación: la decide el verbo

El ATM no sabe si `Bit:32` es un entero, un flotante o una máscara. Lo sabe el nombre del verbo.

| Sufijo del verbo | Interpretación | Opcode típico |
|---|---|---|
| `Int` | Entero, sin signo por defecto | `ADD`, `VPADDD` |
| `Signed` / `Unsigned` | Fuerza la variante con signo | `IDIV`, `VPMULLD` |
| `Float` | IEEE 754 | `VADDPS`, `FADD` |
| `Decimal` | BCD empaquetado | `DAA`, `FBLD` |
| *(ninguno)* | Operación de bits, sin interpretación | `AND`, `SHL`, `MOV` |
| `ToInt` / `ToFloat` | Conversión explícita | `VCVTPS2DQ` |

```fasm
// Mismo contenedor, interpretaciones distintas:
Simd0.r512.part{32}.addInt(Simd1.r512.part{32})     // VPADDD  — enteros
Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})   // VADDPS  — IEEE 754

// Mismo ancho, distinta semántica:
Gpr0.add(Gpr1)                 // ADD
Gpr0.addFloat(Gpr1)            // ✗ no existe: los GPR no hacen FP
```

**Consecuencia:** no hay promoción ni coerción implícita. Cambiar el ancho exige un verbo explícito:

| Verbo | Efecto |
|---|---|
| `extendUnsigned()` | Rellena con ceros hasta el ancho del destino |
| `extendSigned()` | Rellena con el bit de signo |
| `truncate()` | Conserva los bits bajos |
| `convertToInt()` | Reinterpreta/redondea a entero |
| `convertToFloat()` | Reinterpreta/redondea a IEEE 754 |

No existe `Gpr0.copy(Gpr0.low32)` como "conversión": es una copia con zero-extend, y se documenta como tal.

---

## 8. Vistas: la misma celda, distinta ventana

Una vista no crea tipo nuevo: restringe el acceso a una parte de la celda.

| Vista | Sobre | Ancho | Escritura |
|---|---|---|---|
| `.low8` | GPR | 8 | merge (conserva bits 8–63) |
| `.high8` | GPR 0–3 | 8 | merge (conserva el resto) |
| `.low16` | GPR, `Frf` | 16 | merge |
| `.low32` | GPR, `Frf`, `Krf` | 32 | zero-extend en GPR |
| `.low64` | SIMD, `Krf` | 64 | reemplaza la parte baja |
| `.r128` `.r256` `.r512` | SIMD | 128/256/512 | reemplaza la vista; VEX zero-extiende |
| `.base` | `Srf3`, `Srf4` | 64 | MSR |
| `.part{8,16,32,64}` | SIMD | n/a (declara elemento) | no escribe |

Regla de composición: **una vista sobre una vista no existe**. `Gpr0.low32.low16` → `FASM3024`; se escribe directamente `Gpr0.low16`.

Las vistas de ancho incompatible con el verbo → `FASM3025`. Por ejemplo, `Gpr0.low16.addFloat(...)` es inválido porque no hay opcode FP para 16 bits de GPR.

---

## 9. Composición de formas

El ATM permite dos operaciones aritméticas sobre formas, y solo dos:

| Operación | Regla | Ejemplo |
|---|---|---|
| **Bytes → bits** | `Byte:N` = `Bit:8N` | `Byte:8` = `Bit:64` |
| **Bits → bytes** | `Bit:8N` = `Byte:N` (solo si `N` divisible por 8) | `Bit:128` = `Byte:16` |

Y una derivación específica de SIMD:

| Derivación | Regla | Ejemplo |
|---|---|---|
| Elementos | `ancho / partición` | `.r512.part{32}` → 16 elementos |
| Ancho de máscara | `elementos` bits | 16 elementos → `Krf.low16` |

Ninguna otra operación está permitida. No hay multiplicación de formas (`Byte:8 * 4` es inválido como tipo; se escribe `Byte:32`), ni suma de formas (`Byte:8 + Byte:4` → `FASM3026`). Si el programador necesita expresar "cuatro elementos de 8 bytes", escribe la capacidad total (`Byte:32`) o usa `repeat` en datos, que es una construcción del programa, no un tipo.

---

## 10. Lo que el ATM no tiene (y su equivalente)

| Concepto ausente | Por qué | Cómo se expresa |
|---|---|---|
| `int`, `uint`, `long` | La interpretación va en el verbo | `Bit:N` + `add` |
| `float`, `double` | Ídem | `Bit:N` + `addFloat` |
| `bool` | Un flag es un bit con nombre | `Frf.zero`, `testBit` |
| `char` | Un carácter es un byte | `Byte:1` |
| `string` | Texto es una región de bytes | `Byte:N [símbolo]` + capacidad |
| `pointer` | Una dirección cabe en 64 bits | `Gpr0` + `loadAddress` |
| `array` | Dirección + capacidad | `Byte:N` + `Gpr` desplazado |
| `struct` | Offsets sobre una base | `Byte:N [base + Byte:offset]` |
| `enum` | Un selector es un valor de datos | entra por memoria/parámetro |
| `union` | Reinterpretación | el mismo `Byte:N` con otro verbo |
| `void` | Una rutina sin valor de retorno | `.returns()` omitido |
| `size_t` | Un tamaño es una capacidad | `Byte:N` |

La ausencia no es una limitación: es la garantía de que ningún tipo oculta bits.

---

## 11. Reglas del checker ATM

El checker recibe el AST tipado y aplica, en este orden:

1. **Resolución de nombres**: cada receptor y fuente tiene un archivo y un índice válidos.
2. **Contexto**: cada literal está en una posición estructural o de dato (doc 06).
3. **Ancho**: el ancho del verbo coincide con el ancho de receptor y fuentes, o existe un verbo de extensión explícito.
4. **Vista**: la vista existe en ese archivo y es compatible con el verbo.
5. **Partición**: si hay `part{N}`, `N ∈ {8,16,32,64}` y el ancho es divisible por `N`.
6. **Capacidad**: si hay `Byte:N`, `N` está en la tabla de anchos accesibles o es una capacidad declarativa.
7. **Privilegio y target**: se delegan a los checkers de ABI y de perfil (docs 12 y 13).

Si los pasos 3–6 fallan, el mensaje **debe** incluir la forma esperada y la encontrada:

```
FASM3025: la vista 'Gpr0.low16' no admite 'addFloat'.
  esperado: Bit:32 o Bit:64 (GPR completo)
  encontrado: Bit:16
  sugerencia: Gpr0.addFloat(Gpr1) o use SimdN.r128.part{16}
```

---

## 12. Diagnósticos del ATM

| Código | Severidad | Situación | Corrección |
|---|---|---|---|
| `FASM3020` | error | `Byte:N` en contexto de registro/ALU | use `Bit:8N` |
| `FASM3021` | error | `Bit:N` en contexto de memoria/offset | use `Byte:N/8` |
| `FASM3022` | error | `Bit:N` con `N` no operable | use 1/8/16/32/64/80/128/256/512 |
| `FASM3023` | error | `Byte:N` no accesible en una instrucción | use 1/2/4/8/10/16/32/64 o un verbo de bloque |
| `FASM3024` | error | Vista sobre vista | escriba la vista final directamente |
| `FASM3025` | error | Vista incompatible con el verbo | cambie de vista o de verbo |
| `FASM3026` | error | Operación aritmética entre formas | calcule la capacidad resultante |
| `FASM3027` | error | Múltiplo usado como ancho de operación | use la forma base |
| `FASM3028` | error | `part{N}` con `N` no admitido | use 8/16/32/64 |
| `FASM3029` | error | Ancho de máscara incompatible con la partición | ajuste `Krf.lowN` |

Todos son errores: el ATM no emite advertencias. Una forma ambigua es siempre un error, nunca una inferencia.

---

## 13. Ejemplos resueltos

### 13.1 Un registro de 64 bits, cuatro vistas

```fasm
Gpr0.copy(Gpr1)                  // Bit:64 — reemplaza los 64 bits
Gpr0.low32.copy(Gpr1.low32)      // Bit:32 — zero-extiende a 64
Gpr0.low16.copy(Gpr1.low16)      // Bit:16 — conserva 16..63
Gpr0.low8.copy(Gpr1.low8)        // Bit:8  — conserva 8..63
Gpr0.high8.copy(Gpr1.high8)      // Bit:8  — solo Gpr0..Gpr3
```

### 13.2 Memoria con capacidad explícita

```fasm
Gpr0.copy(Byte:8 [contador])            // lee 8 bytes
Gpr0.low32.copy(Byte:4 [contador])      // lee 4 bytes
Gpr0.low8.copy(Byte:1 [contador])       // lee 1 byte
Byte:8 [contador].copy(Gpr0)            // escribe 8 bytes
```

### 13.3 Misma celda, dos interpretaciones

```fasm
// 16 enteros de 32 bits
Simd0.r512.part{32}.addInt(Simd1.r512.part{32})

// 16 flotantes de 32 bits
Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})

// 8 enteros de 64 bits
Simd0.r512.part{64}.addInt(Simd1.r512.part{64})
```

### 13.4 Error típico de contexto

```fasm
Gpr0.copy(Bit:8 [x])     // FASM3021 — la memoria se mide en Byte
Byte [x].copy(Gpr0)      // FASM3023 — falta la capacidad en bytes
```

---

## 14. Resumen

- Dos magnitudes: `Bit:N`, `Byte:N`.
- `N` es forma; el dato nunca se escribe en una instrucción.
- Múltiplos binarios solo para capacidades.
- Anchos legales: 1/8/16/32/64/80 y 128/256/512 bits; 1/2/4/8/10/16/32/64 bytes para acceso.
- `Bit` para registros, `Byte` para memoria; mezclar es error.
- La interpretación está en el verbo, nunca en el tipo.
- Las vistas son ventanas, no tipos nuevos, y no se anidan.
- Toda infracción tiene un `FASM30xx` con corrección sugerida.
