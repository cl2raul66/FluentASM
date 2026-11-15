# 18 — IntelliSense

> **Normativo para el editor.** El completado, el hover y los diagnósticos en vivo se derivan de la misma tabla de verbos y del mismo checker que usa el compilador. El editor nunca inventa una sugerencia que el compilador vaya a rechazar.

---

## 1. Principio

El receptor determina el universo de verbos:

```fasm
Gpr0.            → operaciones de registro general
Frf.             → bits de flags
Ipr.             → transferencias de control
Simd0.r512.part{32}.  → operaciones por elemento de 32 bits
Byte:8 [x].      → operaciones de memoria de 8 bytes
```

No hay una lista global de "todas las instrucciones". Hay **una lista por tipo de receptor**, calculada del catálogo. Esto es lo que hace que el IntelliSense sea útil y no un volcado de 400 nombres.

La fuente única es `VerbCatalog` (doc 17 §3.4). El LSP, el checker y el hover leen la misma tabla.

---

## 2. Protocolo

### 2.1 LSP sobre stdio

`FluentASM.IDE` (WinUI 3) habla LSP con `FluentASM.Core.LanguageServer`. Alternativamente, el IDE puede hospedar `Core` en proceso; el comportamiento es idéntico porque el servidor es una capa fina sobre la misma API.

```
Content-Type: application/vscode-jsonrpc; charset=utf-8
```

### 2.2 Capacidades anunciadas

| Capacidad | Método | Uso |
|---|---|---|
| `textDocument/completion` | completado | tras `.`, `{`, `(`, `[`, `:` |
| `textDocument/hover` | hover | sobre registro, verbo, capacidad, símbolo |
| `textDocument/signatureHelp` | ayuda de firma | dentro de `( … )` |
| `textDocument/publishDiagnostics` | diagnósticos | al guardar y al escribir (debounce 300 ms) |
| `textDocument/semanticTokens/full` | resaltado | colores por clase semántica |
| `textDocument/definition` | ir a definición | sobre `@label`, rutina, `data` |
| `textDocument/references` | referencias | símbolos y labels |
| `textDocument/documentSymbol` | esquema | programa → secciones → rutinas |
| `textDocument/codeAction` | correcciones rápidas | desde `Diagnostic.Help` |
| `textDocument/formatting` | formato | reglas de §8 |
| `textDocument/inlayHint` | pistas | capacidades y offsets |

### 2.3 Sincronización

- `textDocument/didOpen` → parse completo, publicación de diagnósticos.
- `textDocument/didChange` → re-parse incremental del bloque afectado; re-checker del programa si cambia un símbolo.
- `textDocument/didSave` → re-checker completo y, si el usuario lo pide, compilación.

El servidor mantiene un `DocumentSnapshot` inmutable; cada request lee un snapshot consistente.

---

## 3. Completado por receptor

### 3.1 `GprN` y vistas

```
Gpr0.|
```

Sugerencias (ordenadas por frecuencia de uso, luego alfabético):

| Verbo | Firma | Nota |
|---|---|---|
| `copy(fuente)` | `rm64 → Gpr` | transferencia |
| `add(fuente)` | `rm64` | suma |
| `sub(fuente)` | `rm64` | resta |
| `compare(fuente)` | `rm64` | solo flags |
| `test(fuente)` | `rm64` | AND sin destino |
| `and` / `or` / `xor` / `not` | `rm64` / — | lógica |
| `shiftLeft` / `shiftRightLogical` / `shiftRightArithmetic` | `Bit:N` | desplazamiento |
| `increment` / `decrement` / `negate` / `clear` | — | forma |
| `multiplyLow` / `divide` / `divideSigned` | según firma | aritmética |
| `loadAddress(símbolo)` | `label` | LEA |
| `push` / `pop` | — | pila |

Tras `Gpr0.low32.` la lista se reduce a los verbos cuyo ancho admite 32 bits. Tras `Gpr0.low8.` no aparecen `divide`, `multiply` ni `loadAddress`.

### 3.2 SIMD

```
Simd0.r512.part{32}.|
```

| Verbo | Firma | Instrucción |
|---|---|---|
| `addInt` / `subInt` / `multiplyInt` | `Simd` | `VPADDD`, … |
| `addFloat` / `subFloat` / `multiplyFloat` / `divideFloat` | `Simd` | `VADDPS`, … |
| `compareEqualInt` | `Simd` → `Krf` | `VPCMPEQD` |
| `shiftLeftLogical` / `shiftRightArithmetic` | `Simd` o `Bit:N` | `VPSLLD`, … |
| `copy` / `and` / `or` / `xor` | `Simd` | bit a bit |
| `broadcast` | `Gpr.low32` | `VPBROADCASTD` |
| `permute` / `shuffle` | `Simd`, `Bit:N` | — |

Al escribir `part{` el editor ofrece solo `8`, `16`, `32`, `64` (doc 10 §3). Si el verbo ya está escrito y la partición no aplica, el diagnóstico aparece al cerrar `}` (`FASM3087`).

### 3.3 `Frf`

```
Frf.|
```

`carry`, `zero`, `sign`, `overflow`, `parity`, `direction`, `interrupt`, `iopl`, `trap`, …

Con `(` después, el completado ofrece el estado: `0`, `1`, `not`. Con comparación, `equal`, `unequal`.

### 3.4 `Ipr`

```
Ipr.|
```

`flowJump`, `flowCall`, `flowStackJump`, `flowLoop`, `flowInterrupt`, `flowSyscall`, `flowSysret`, `flowBreakpoint`, `flowOverflow`, `current`.

`flowJump(` completado de primer argumento: labels `@` de la rutina + `GprN` + memoria. Con `if ` activa el completado de condiciones y alias (`Frf.zero`, `Frf.less`, …).

### 3.5 Memoria

```
Byte:8 [|              → GprN, @label, símbolo, absolute(...)
Byte:8 [Gpr6 + |       → GprN, número, símbolo
Byte:8 [x].|           → verbos de memoria de 8 bytes
```

La capacidad dicta el universo: `Byte:8` ofrece `copy`, `add`, `sub`, `compare`, `exchange`, `copyBlock`, …; `Byte:1` ofrece `copy`, `setBit`, `testBit`, …

### 3.6 Cabeceras

| Posición | Completado |
|---|---|
| `program x.\|` | `abi`, `target`, `entry`, `image`, `stack`, `ring` |
| `.abi(\|` | `win64`, `sysv` |
| `.target(\|` | `x64-sse2`…`x64-avx512full` |
| `section .\|` | `.execute`, `.rodata`, `.data`, `.bss` |
| `routine x.\|` | `frame`, `preserves`, `shadow`, `redzone`, `naked`, `returns`, `align`, … |
| `.\|` al inicio de línea | directivas |

### 3.7 Etiquetas

`Ipr.flowJump(@` lista solo los labels de la rutina actual, porque el ámbito es local (doc 05 §9). Un label de otra rutina no se sugiere y, si se escribe, da `FASM4010`.

---

## 4. Insertion y snippets

El completado inserta la **forma canónica con placeholders**, no un nombre suelto:

| Sugerencia | Texto insertado | Cursores |
|---|---|---|
| `copy` | `copy(${1:fuente})` | 1 |
| `add` | `add(${1:fuente})` | 1 |
| `shiftLeft` | `shiftLeft(Bit:${1:1})` | 1 |
| `flowJump` | `flowJump(@${1:destino}${2: if Frf.zero})` | 1, 2 |
| `part` | `.part{${1:32}}` | 1 |
| `frame` | `.frame(Byte:${1:16})` | 1 |

Regla: **el snippet nunca inserta un dato**. `copy` no propone `copy(42)` ni `add(1)`; propone una fuente, y si el usuario escribe un número, el checker lo rechaza con `FASM2007`.

---

## 5. Hover

El hover muestra la verdad física, no una descripción de alto nivel.

### 5.1 Sobre un registro

```
Gpr0.low32
──────────
Contenedor : Gpr0 (RAX)
Vista      : .low32 (32 bits)
Encoding   : EAX
Tipo ATM   : Bit:32
ABI        : caller-saved en win64 y sysv
Notas      : escribir EAX pone a cero los 32 bits altos de RAX
```

### 5.2 Sobre un verbo

```
add
───
Firma      : GprN.add(fuente: r/m64)
Efecto     : destino ← destino + fuente
x64        : ADD r/m64, r64  (REX.W 01 /r)  ó  ADD r64, r/m64 (03 /r)
Flags      : escribe CF OF SF ZF AF PF
Tamaño     : 3–7 bytes según ModRM
Target     : x64-sse2
Diagnóstico: FASM2007 si la fuente es un dato literal
```

### 5.3 Sobre una capacidad

```
Byte:8
──────
Tipo       : capacidad de memoria (no de registro)
Bits       : 64
Accesos    : 8, 4, 2, 1 bytes
Uso válido : Byte:8 [dirección], local, data
Uso inválido: Byte:8 como ancho de registro (FASM3020)
```

### 5.4 Sobre un flag

```
Frf.sign unequal Frf.overflow
─────────────────────────────
Alias      : Frf.less
Jcc        : JL  (0F 8C rel32 / 7C rel8)
Semántica  : SF ≠ OF, comparación con signo
```

### 5.5 Sobre un símbolo

Muestra sección, offset (si ya hay layout), tamaño, visibilidad y si es rutina o dato. Sobre `@label`, la rutina dueña.

---

## 6. Ayuda de firma

Dentro de `(`:

```
Gpr0.add(▮)
─────────────────────────
add(fuente: r/m64)
fuente: GprN | Byte:N [dirección] | parámetro
✗ 42  → dato literal (FASM2007)
```

Para verbos de varios argumentos, resalta el activo:

```
Gpr0.shiftLeftDouble(Gpr1, ▮)
──────────────────────────────
shiftLeftDouble(relleno: GprN, cuenta: Bit:N)
```

Para tuplas, la ayuda muestra el orden:

```
(Gpr2:Gpr0).multiply(▮)
────────────────────────
multiply(divisor: r/m64)
Salida: RDX:RAX  →  (Gpr2:Gpr0)
```

---

## 7. Diagnósticos en vivo

- Se publican con `debounce` de 300 ms tras la última pulsación.
- Severidad y código son los mismos que en el CLI (doc 13).
- El subrayado usa el `Span` del diagnóstico.
- `Diagnostic.Help` alimenta las *quick fixes*:

| Código | Quick fix |
|---|---|
| `FASM2007` | "Usar un parámetro" / "Mover a `.rodata`" |
| `FASM3024` | "Escribir la vista final (`.low32`)" |
| `FASM3087` | "Cambiar a `part{32}`" |
| `FASM3017` | "Envolver en tupla: `(Gpr2:Gpr0)`" |
| `FASM5002` | "Añadir `.shadow(1)`" |
| `FASM5003` | "Ajustar `.frame` o `.preserves` para alinear" |
| `FASM4012` | "Insertar `compare` antes del salto" |
| `FASM8101` | "Añadir `--no-payload`" (no aplica dentro del fuente) |

- Los errores de una acción no invalidan el resto del archivo: se muestran todos los que el checker pudo calcular.

---

## 8. Formato

El formateador es determinista y no cambia la semántica:

| Regla | Ejemplo |
|---|---|
| Una acción por línea | `Gpr0.copy(Gpr1)` |
| Indentación 4 espacios | programa → sección → rutina → cuerpo |
| Cabecera encadenada en una línea si cabe en 100 columnas | `program x.abi(win64).target(x64-avx2).entry(main) {` |
| Si no cabe, un atributo por línea alineados bajo el primero | |
| Sin espacio antes de `.` ni de `(` | `Gpr0.add(Gpr1)` |
| Un espacio tras `,` | `preserves(Gpr3, Gpr12)` |
| Comentarios `//` conservados en su línea | |
| Línea máxima 100 columnas | `FASM8102` |

El formato se aplica completo al archivo, no por rangos, para que el resultado no dependa del historial de edición.

---

## 9. Resaltado semántico

`semanticTokens` con esta leyenda:

| Tipo | Token | Color sugerido |
|---|---|---|
| `keyword` | `program`, `section`, `routine`, `data` | azul |
| `type` | `Bit`, `Byte`, `KiloByte`, … | verde azulado |
| `variable` | registros (`Gpr0`, `Simd3`) | naranja |
| `property` | verbos (`copy`, `addFloat`) | violeta |
| `function` | rutinas | amarillo |
| `parameter` | `parameter` y sus alias | marrón |
| `number` | capacidades, índices estructurales | gris |
| `string` | payload | verde |
| `label` | `@nombre` | rojo suave |
| `operator` | `.`, `:`, `+`, `*` | gris claro |
| `comment` | `//`, `/* */` | gris |

El resaltado distingue `Frf.zero` (bit de flag) de una variable de usuario `zero`, y `@loop` (label) de `loop`.

---

## 10. Pistas en línea (*inlay hints*)

| Pista | Dónde | Ejemplo |
|---|---|---|
| Capacidad derivada | operandos de memoria sin capacidad explícita | `Byte:8` |
| Offset | junto a un label | `@loop · 0x26` |
| Tamaño codificado | al final de una acción | `3 B` |
| Registro ABI | junto a un `parameter` | `→ Gpr1 (RCX)` |
| Valor de retorno | junto a `flowCall` | `→ Gpr0` |
| Clobbers | junto a `copyBlock`, `flowSyscall` | `clobber Gpr1, Gpr6, Gpr7` |

Las pistas son opcionales y se activan por configuración; nunca se insertan en el texto.

---

## 11. Navegación

| Acción | Comportamiento |
|---|---|
| Ir a definición | `@label` → su declaración en la rutina; símbolo → su `data`/`routine` |
| Buscar referencias | todos los usos del símbolo o label |
| Esquema del documento | árbol programa → secciones → rutinas → labels |
| Renombrar | renombra símbolo o label y todas sus referencias (respeta el ámbito) |
| Plegar | programa, sección, rutina, payload `repeat` |

Renombrar un label nunca cruza de rutina: los ámbitos son disjuntos.

---

## 12. Rendimiento del editor

| Operación | Objetivo |
|---|---|
| Completado tras `.` | < 30 ms |
| Diagnósticos tras pausa | < 150 ms |
| Hover | < 50 ms |
| Formato de archivo de 10 000 líneas | < 300 ms |
| Memoria por documento abierto | < 20 MB |

El completado se calcula del `VerbCatalog` ya ordenado y filtrado por receptor; no se recorre el AST completo.

---

## 13. Resumen

- El receptor define el universo de verbos; el catálogo es la fuente única.
- El completado inserta la forma canónica con placeholders y nunca un dato.
- El hover explica la física: encoding, flags, tamaño, target y diagnóstico esperado.
- Los diagnósticos en vivo son los mismos del CLI y traen *quick fixes*.
- El formato y el resaltado son deterministas.
- El editor no puede sugerir algo que el compilador rechace: ambos leen la misma tabla.
