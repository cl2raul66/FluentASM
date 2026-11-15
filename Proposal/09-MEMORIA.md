# 09 — Memoria

> **Normativo.** Direccionamiento, SIB, desplazamientos, símbolos, alineación, frame de pila y ordenamiento de memoria.

---

## 1. El espacio de direcciones

En x64 una dirección es un valor de 64 bits, pero el hardware solo implementa los bits que el SO habilita:

| Modelo | Bits efectivos | Canónico |
|---|---|---|
| 4 niveles | 48 | bits 63–48 = copia del bit 47 |
| 5 niveles (LA57) | 57 | bits 63–57 = copia del bit 56 |

El lenguaje no asume un modelo: `.canonical(...)` es una comprobación de ejecución, no de compilación. Un desplazamiento siempre es de 32 bits con signo (doc 16).

---

## 2. Modos de direccionamiento

| Modo | Sintaxis | Encoding | Bytes |
|---|---|---|---|
| Registro base | `Byte:8 [Gpr3]` | ModRM `mod=00 rm=base` | 0 disp |
| Base + disp8 | `Byte:8 [Gpr5 - 16]` | `mod=01` | 1 |
| Base + disp32 | `Byte:8 [Gpr5 + 1024]` | `mod=10` | 4 |
| Base + índice×escala | `Byte:4 [Gpr3 + Gpr1*4]` | SIB, `mod=00` | 0 |
| Base + índice×escala + disp8 | `Byte:8 [Gpr5 + Gpr1*8 - 8]` | SIB, `mod=01` | 1 |
| Índice×escala + disp32 | `Byte:1 [Gpr1*2 + 64]` | SIB, `base=101` | 4 |
| RIP-relativo | `Byte:8 [contador]` | `mod=00 rm=101` + disp32 | 4 |
| Absoluto (SIB sin base) | `Byte:8 [absolute(x)]` | SIB `base=101, index=100` + disp32 | 4 |

### 2.1 Reglas de SIB

| Regla | Diagnóstico |
|---|---|
| Escala ∈ {1, 2, 4, 8} | `FASM4004` |
| Índice ≠ `Gpr4` (RSP) | `FASM4003` |
| Como máximo una base y un índice | `FASM4005` |
| `Gpr5` (RBP) o `Gpr13` como base sin disp → SIB con `disp8=0` | automático |
| `.high8` no puede ser base ni índice | `FASM4007` |
| `.low16`/`.low8` como base o índice | `FASM4008` (solo 64 bits) |

El encoder emite siempre la forma más corta (D-013): `disp0` si no hay desplazamiento, `disp8` si cabe con signo, `disp32` en otro caso.

### 2.2 Direccionamiento RIP-relativo

Un **símbolo de datos** (identificador desnudo) dentro de corchetes es **siempre** RIP-relativo. Un `@label` de código también lo es cuando se usa como dirección:

```fasm
Gpr0.copy(Byte:8 [contador])         // REX.W 8B 05 disp32
Byte:8 [contador].copy(Gpr0)         // REX.W 89 05 disp32
Byte:16 [tabla + Gpr1*16]            // SIB con base=0, índice, disp32 RIP-rel
```

El desplazamiento se calcula como `destino - (dirección_de_la_siguiente_instrucción)`. El linker lo rellena.

### 2.3 Absoluto

`absolute(símbolo)` fuerza una dirección absoluta de 32 bits sin base:

```fasm
Gpr0.copy(Byte:8 [absolute(contador)])   // SIB base=101, index=100 + disp32
```

- Emite SIB `base=101, index=100` + `disp32` con la dirección virtual del símbolo (doc 16 §6.2). No usa `moffs`: es la misma forma que cualquier operando con SIB, así que funciona con cualquier registro destino.
- Exige que la imagen se cargue por debajo de 4 GiB.
- En imagen PIC o con base desconocida → `FASM5008`.
- Solo válido con `.image(flat)` y base conocida (bare metal, kernel).

### 2.4 Índice sin base

`Byte:1 [Gpr1*2 + 64]` es válido: el campo `base=101` de SIB con `mod=00` significa "sin base, disp32". El compilador lo usa cuando el programador escribe un índice sin base.

---

## 3. Capacidad del operando

La capacidad es obligatoria y determina el ancho del acceso:

| Sintaxis | Ancho | Registro compatible |
|---|---|---|
| `Byte:1 [x]` | 8 | `Gpr.low8`, `Gpr.high8` |
| `Byte:2 [x]` | 16 | `Gpr.low16` |
| `Byte:4 [x]` | 32 | `Gpr.low32` |
| `Byte:8 [x]` | 64 | `Gpr` |
| `Byte:10 [x]` | 80 | `Xrf` |
| `Byte:16 [x]` | 128 | `Simd.r128` |
| `Byte:32 [x]` | 256 | `Simd.r256` |
| `Byte:64 [x]` | 512 | `Simd.r512` |

Errores:

- `Byte:N` con `N` no accesible → `FASM3023`.
- Capacidad distinta al ancho del registro → `FASM4009`.
- `Byte:8 [x]` con `Gpr0.low32` → `FASM4009`.

---

## 4. Alineación

### 4.1 Declarativa

| Nivel | Atributo | Por defecto |
|---|---|---|
| Sección | `section .rodata.align(Byte:16)` | 16 |
| Rutina | `routine f.align(Byte:16)` | ninguna |
| Dato | `data x: Byte:8` | alineado a su capacidad, hasta 16 |
| Local | `local x: Byte:8` | alineado a su capacidad, hasta 16 |

### 4.2 Imperativa

```fasm
.align(Byte:16)
```

Emite NOPs (en `.execute`) o ceros (en datos) hasta el siguiente múltiplo. Solo potencias de 2 → `FASM4014`.

### 4.3 Acceso no alineado

x64 permite accesos no alineados salvo en:

- operaciones atómicas (`LOCK`) → `FASM4034` (advertencia; puede fallar en ejecución),
- instrucciones SIMD alineadas (`VMOVDQA`) → `FASM4015` (error): use `VMOVDQU`.

El lenguaje distingue los verbos: `copy` elige la variante alineada solo si la alineación es demostrable; en otro caso usa la no alineada (`VMOVDQU64`).

---

## 5. Secciones e imagen

### 5.1 Orden en el binario

```
+-----------------------------+  offset 0
| .execute   (R+X)            |
+-----------------------------+  align 16
| .rodata    (R)              |
+-----------------------------+  align 16
| .data      (R+W)            |
+-----------------------------+  align 16
| .bss       (R+W, zero)      |
+-----------------------------+
```

- `.execute` empieza en el offset 0.
- Cada sección se alinea a 16 bytes como mínimo.
- `.bss` no ocupa bytes en el archivo; el cargador la reserva y la pone a cero.

### 5.2 Entry

- Si `.entry` es una rutina y es la primera de `.execute`, no hay salto inicial.
- Si no, el linker emite `JMP rel32` (5 bytes) al inicio de `.execute` y coloca la entrada después.
- Un programa sin `.entry` usa la primera rutina.

### 5.3 Símbolos

| Símbolo | Sección | Valor |
|---|---|---|
| Rutina | `.execute` | offset dentro de `.execute` |
| Etiqueta | `.execute` | offset dentro de `.execute` |
| Dato | `.rodata` / `.data` / `.bss` | dirección virtual asignada por el linker |
| Recurso | `.rodata` | dirección virtual |

El compilador emite un **mapa** (`--map`) con nombre, sección, offset, tamaño y alineación de cada símbolo.

---

## 6. Frame de pila

### 6.1 Layout (Win64)

```
 dirección alta
+--------------------+
| argumento N        |  [RBP + 16 + 8(N-5)]
| ...                |
| argumento 5        |  [RBP + 16]
| dirección retorno  |  [RBP + 8]
+--------------------+  ← RBP
| saved Gpr3         |  [RBP - 8]
| saved Gpr12        |  [RBP - 16]
| local 1            |  [RBP - 24]
| local 2            |  [RBP - 32]
+--------------------+  ← RSP (16-aligned)
| shadow space 32 B  |  [RSP + 0 .. 31]
+--------------------+
 dirección baja
```

### 6.2 Layout (System V)

```
+--------------------+
| argumento 7        |  [RBP + 16]
| dirección retorno  |  [RBP + 8]
+--------------------+  ← RBP
| locals             |
+--------------------+  ← RSP (16-aligned)
| red zone 128 B     |  [RSP - 128 .. RSP - 1]
+--------------------+
```

### 6.3 Orden del prólogo declarado

Cuando una rutina declara `.frame`, `.preserves` y `.shadow`, el compilador emite en este orden:

1. `PUSH Gpr5` (si hay frame)
2. `MOV Gpr5, Gpr4`
3. `PUSH` de cada registro de `.preserves`, en el orden declarado
4. `SUB Gpr4, total` (una sola instrucción; `total` según §6.4)

Y el epílogo, en orden inverso exacto:

1. `ADD Gpr4, total`
2. `POP` de cada registro preservado, en orden inverso
3. `POP Gpr5`
4. `flowStackJump`

`total` incluye el frame, la cuña de alineación y el shadow space en una sola resta: es la codificación más corta (D-025) y la que el desensamblado muestra como un único `SUB RSP, N`. Con `.naked(1)` no se emite nada.

### 6.4 Cálculo del frame y alineación

`.frame(Byte:N)` es una **capacidad declarada por el programador**, no un valor que el compilador inventa:

```
N            = .frame(Byte:N)          // múltiplo de 16 (FASM2015)
suma(locales) ≤ N                       // FASM2017
cuña         = 8 si |preserves| es impar, 0 si es par
total        = N + cuña + (32 si .shadow(1))
```

Los registros de `.preserves` se empujan **fuera** de `total`; el frame solo cubre locales y la cuña. La prueba de alineación, partiendo de `RSP ≡ 8 (mod 16)` en la entrada (lo dejó el `CALL`):

| Paso | `RSP` mod 16 |
|---|---|
| entrada | 8 |
| `PUSH Gpr5` | 0 |
| `k` pushes de `preserves` | `8·k` |
| `SUB total` (`total ≡ 8·k`) | 0 |

Antes de un `flowCall` la pila queda alineada a 16 y el llamado entra con 8, como exige la ABI. La cuña no es un temporal de datos: son 8 bytes de relleno deterministas, visibles en el desensamblado.

---

## 7. Ordenación de memoria

### 7.1 Modelo

x64 tiene un modelo **TSO** (Total Store Order) débil:

| Reordenamiento | ¿Permitido? |
|---|---|
| Load → Load | No (en la misma dirección) |
| Load → Store | Sí |
| Store → Load | Sí |
| Store → Store | No (en la misma dirección) |

### 7.2 Barreras

| Verbo | Instrucción | Qué garantiza |
|---|---|---|
| `Fence.full()` | `MFENCE` | Todas las cargas y almacenes anteriores son visibles antes de los siguientes |
| `Fence.read()` | `LFENCE` | Ordena cargas |
| `Fence.write()` | `SFENCE` | Ordena almacenes |
| `Byte:8 [x].addLocked(Gpr0)` | `LOCK ADD` | Operación atómica + barrera completa |

`Cpu.serialize()` (`SERIALIZE`) ordena todo el pipeline y es la barrera más fuerte, disponible desde AVX-512.

### 7.3 Reglas

- Una sección `.data` compartida entre hilos requiere `Fence.*` explícito en cada transición.
- El compilador **no** inserta barreras (D-025).
- Las operaciones de bloque (`copyBlock`, `fillBlock`) no son atómicas: si se comparten, el programa debe sincronizar.

---

## 8. Memoria no temporal y caché

| Verbo | Instrucción | Uso |
|---|---|---|
| `Byte:8 [x].copyNonTemporal(Gpr0)` | `MOVNTI` | Escritura que evita la caché |
| `Byte:64 [x].prefetch(.t0)` | `PREFETCHT0` | Trae a L1 |
| `Byte:64 [x].prefetch(.nta)` | `PREFETCHNTA` | Trae a L1 sin contaminar |
| `Byte:64 [x].flushCacheLine()` | `CLFLUSH` | Invalida la línea |

Reglas:

- `copyNonTemporal` requiere alineación a 8 → `FASM4035`.
- `prefetch` no falla con direcciones inválidas; no genera excepción.
- `flushCacheLine` requiere alineación a 64 → `FASM4036` (advertencia).

---

## 9. Memoria y sistemas operativos

| Concepto | Registro | Notas |
|---|---|---|
| Base de tablas | `Crf3` | PCID en bits 0–11 |
| Fallo de página | `Crf2` | Dirección que falló |
| Paging | `Crf0.PG` | Debe estar activo en x64 |
| PAE | `Crf4.PAE` | Obligatorio en long mode |
| LA57 | `Crf4.LA57` | Habilita 5 niveles |
| NX | Bit 63 de la entrada | Requiere `Msr.efer.NXE` |
| Claves de protección | `Pkru` | 16 claves de 2 bits |

El lenguaje no modela tablas de página como tipo: son bytes en memoria con un formato que el programador conoce.

---

## 10. Ejemplos

### 10.1 Acceso a un array

```fasm
Gpr1.copy(Byte:8 [indice])
Gpr0.copy(Byte:8 [tabla + Gpr1*8])
```

### 10.2 Campo de una estructura por offset

```fasm
// struct { Byte:8 id; Byte:8 valor; }  → offset 0 y 8
Gpr0.copy(Byte:8 [Gpr3 + 8])               // campo 'valor'
```

### 10.3 Pila como almacenamiento temporal explícito

```fasm
Stack.push(Gpr0)
Stack.push(Gpr1)
// ...
Gpr1.pop(Stack)
Gpr0.pop(Stack)
```

### 10.4 Copia de bloque

```fasm
Byte:4096 [Gpr7].copyBlock(Byte:4096 [Gpr6])
```

`Gpr6` = origen, `Gpr7` = destino, `Gpr1` = cuenta (clobber declarado).

---

## 11. Diagnósticos de memoria

| Código | Situación |
|---|---|
| `FASM4003` | `Gpr4` como índice SIB |
| `FASM4004` | Escala inválida |
| `FASM4005` | Más de una base o índice |
| `FASM4006` | Desplazamiento fuera de 32 bits |
| `FASM4007` | `.high8` como base o índice |
| `FASM4008` | Vista distinta de 64 bits como base o índice |
| `FASM4009` | Capacidad incompatible con el ancho del registro |
| `FASM4014` | Alineación no potencia de 2 |
| `FASM4015` | Acceso alineado sobre dirección no alineada |
| `FASM4029` | Dirección no representable en un solo SIB |
| `FASM4034` | Atómico sobre dirección no alineada |
| `FASM4035` | `copyNonTemporal` mal alineado |
| `FASM4036` | `flushCacheLine` mal alineado |
| `FASM5008` | Absoluto en imagen PIC |

---

## 12. Resumen

- La capacidad (`Byte:N`) es obligatoria y determina el ancho.
- Un símbolo de datos dentro de corchetes es RIP-relativo; `absolute(símbolo)` es absoluto y PIC-hostil.
- SIB admite una base, un índice y escala 1/2/4/8.
- El frame es explícito y su layout se documenta.
- La pila se alinea a 16 antes de cada llamada.
- Las barreras son explícitas; el compilador no las inventa.
- La imagen es plana y las secciones se ordenan de forma determinista.
