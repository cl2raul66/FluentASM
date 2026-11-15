# 01 — Filosofía

> **Normativo en sus reglas; informativo en su explicación.** Este documento responde *por qué* el lenguaje es como es. Las reglas concretas están en los documentos 02–14.

---

## 1. El problema del ensamblador clásico

Un ensamblador clásico mezcla dos cosas en la misma línea:

```asm
mov rax, 100
mov rbx, 50
add rax, rbx
```

La primera línea contiene **tres decisiones distintas** comprimidas en un mnemónico:

1. **Contenedor:** `rax` (un registro de 64 bits).
2. **Operación:** copiar bits.
3. **Dato:** `100` (información semántica incrustada en el flujo de instrucciones).

El mnemónico `mov` no dice "copia"; dice "mueve", que en inglés sugiere trasladar (y dejar el origen vacío), algo que el hardware no hace. `rax` no dice "registro de propósito general número 0"; dice "accumulator", un nombre histórico. `100` no dice de dónde viene ni por qué está ahí.

El resultado es un programa que **contiene** información, en lugar de un programa que **transporta** información. Cada constante es una decisión invisible: ¿quién la eligió?, ¿qué significa?, ¿puede cambiar sin recompilar?, ¿está en el lugar correcto del binario?

FluentASM separa esas tres decisiones y las hace explícitas.

---

## 2. La tesis: el programa es una topología de flujo

FluentASM describe **tuberías** por donde circulan bits. El código no dice qué datos hay; dice:

- **de dónde entran** (fuente),
- **qué se hace con ellos** (verbo físico),
- **a dónde van** (destino),
- **con qué forma** (capacidad, partición, alineación, desplazamiento).

```fasm
Gpr0.copy(Byte:8 [entrada])        // entra un valor de 8 bytes desde una dirección
Gpr0.add(Byte:8 [incremento])      // se transforma con otro valor de 8 bytes
Byte:8 [salida].copy(Gpr0)         // sale hacia una dirección
```

Ninguna línea sabe si el valor es `100`, `-3` o una dirección de red. El programa solo sabe que hay **8 bytes** en un lugar, que se suman a otros **8 bytes**, y que el resultado va a otro lugar.

Esto es *dataflow assembly*: el grafo de flujo es el programa; los valores son la carga que lo recorre.

---

## 3. Las dos categorías de información

Todo literal del lenguaje pertenece a una de dos categorías:

| Categoría | Qué es | Ejemplos | ¿Permitido? |
|---|---|---|---|
| **Estructura** | Forma, topología, capacidad, dirección, identificador | `Byte:8`, `Bit:64`, `KiloByte:4`, `part{32}`, `@tabla`, `Gpr0`, `Frf.carry(1)`, `Msr.fsBase` | **Sí** |
| **Dato** | Información semántica que el programa procesa | `42`, `3.14`, `'A'`, `"hola"`, `0xFF00` como máscara aritmética | **No** en instrucciones |

La diferencia no siempre es obvia. `Gpr4.sub(Byte:24)` usa `24` como **capacidad** (tamaño del frame); es estructura. `Gpr0.add(Byte:8 [x])` usa una **dirección**; es estructura. `Gpr0.copy(Bit:42)` usa `42` como **dato**; está prohibido.

El compilador no adivina: la gramática y la firma del verbo declaran qué posiciones son estructurales. La tabla completa está en [06-LITERALES](06-LITERALES.md).

---

## 4. El problema del *bootstrap*: ¿de dónde sale el primer dato?

Si el código no puede escribir datos, ¿cómo arranca un programa?

La respuesta es que **el universo ya contiene información**; el programa solo la mueve. Un dato puede entrar por:

| Origen | Verbo / mecanismo |
|---|---|
| Memoria preexistente (ROM, firmware, `.rodata`) | `Byte:N [dirección]` |
| Registros de la CPU | `GprN`, `SimdN`, `XrfN` |
| Hardware (tiempo, aleatoriedad, identidad) | `Cpu.readTimestamp`, `Cpu.readRandom`, `Cpu.identify` |
| Entrada/salida | `Port[n]`, MMIO, `Ipr.flowSyscall` |
| Argumentos de la función | `parameter` |
| Recursos externos | `resource nombre: file("ruta")` |

El programa no *crea* el primer dato; lo **recibe**. Esa es la diferencia entre un programa que fabrica valores y un programa que procesa el mundo.

La única concesión práctica es la **isla de datos**: las secciones `.rodata` y `.data` pueden contener payload para que un programa sea autocontenido y testeable. Esa concesión está marcada como decisión pendiente (D-1) y separada del flujo de instrucciones: el compilador sabe exactamente dónde hay datos y dónde hay código.

---

## 5. Honestidad física

Un lenguaje puede ser cómodo y mentiroso, o incómodo y honesto. FluentASM elige lo segundo, porque su usuario necesita predecir el hardware.

### 5.1 Los nombres dicen lo que el hardware hace

| Nombre clásico | Lo que sugiere | Lo que hace | Nombre FluentASM |
|---|---|---|---|
| `mov` | mover | copiar bits | `copy` |
| `ret` | retornar | leer `[RSP]` y saltar | `Ipr.flowStackJump()` |
| `push` | empujar | restar 8 a RSP y escribir | `Stack.push(...)` |
| `cmp` | comparar | restar sin escribir y actualizar flags | `compare` |
| `jz` | saltar si cero | saltar si ZF=1 | `flowJump(@x if Frf.zero)` |
| `xmm0` | registro de 128 bits | mitad baja de un contenedor de 512 | `Simd0.r128` |

### 5.2 Los efectos secundarios se declaran

Cada verbo documenta:

- registros que lee y escribe,
- flags que define, deja indefinidos o preserva,
- memoria que toca,
- privilegio requerido,
- feature de CPU requerida.

Si una instrucción toca `Gpr1` de forma implícita (`flowLoop`), la tabla lo dice. No hay "magia".

### 5.3 Las vistas son ventanas, no registros nuevos

`Gpr0.low32` no es un registro de 32 bits; es una ventana sobre `Gpr0`. Escribirla **borra** los bits 32–63 (zero-extend). `Gpr0.low16` **conserva** los bits 16–63 (merge). `Simd0.r128` no es un registro distinto de `Simd0.r512`; es la mitad baja, y escribirla con VEX **borra** el resto. El lenguaje no oculta esas reglas: las convierte en parte del modelo.

### 5.4 Sin temporales ocultos

El compilador no inventa registros. Si una operación necesita un scratch, el programador lo declara. Si una copia es memoria→memoria, el programador elige el registro intermedio. El binario resultante se puede leer y predecir línea por línea.

---

## 6. Sin tipos abstractos: la interpretación va en el verbo

Un tipo abstracto responde a la pregunta "¿qué *es* este valor?". FluentASM no hace esa pregunta. Solo responde "¿qué *forma* tiene este contenedor?" y "¿qué *operación* se aplica?".

```fasm
// Los mismos 32 bits, dos interpretaciones distintas:
Gpr0.add(Gpr1)                                     // suma entera (ADD)
Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})  // suma IEEE 754 (VADDPS)
```

No hay `Int32` ni `Float32`; hay un contenedor de 32 bits y un verbo que decide cómo tratarlo. Esto tiene tres ventajas:

1. **Fidelidad al hardware:** cada interpretación tiene su opcode; el lenguaje no las confunde.
2. **Menos vocabulario:** no hay que aprender `int`, `uint`, `float`, `double`, `bool`, `char`, `byte`, `short`, `long`, `size_t`, `ssize_t`…
3. **IntelliSense inequívoco:** tras `Simd0.r512.part{32}.` el editor ofrece `addInt` y `addFloat` como operaciones distintas, no como una sola "suma" que el compilador debe adivinar.

La interpretación es una **decisión del programador**, no una inferencia del compilador.

---

## 7. Los dos ritmos: fluent y bloque

El usuario lo describió así: encadenar es natural para configurar una estructura (una maqueta, un diseño, una consulta); el bloque es natural para un proceso, donde cada paso debe verse.

FluentASM no elige uno: usa **la misma sintaxis** en dos contextos.

### 7.1 Cabecera: configuración encadenada

```fasm
routine procesar
    .abi(win64)
    .frame(Byte:32)
    .preserves(Gpr3, Gpr12)
    .shadow(1)
{
    ...
}
```

Cada eslabón configura el mismo objeto (la rutina). No hay datos, no hay efectos físicos, no hay ambigüedad. Es una declaración, no una secuencia.

### 7.2 Cuerpo: acciones en bloque

```fasm
Gpr0.copy(origen)
Gpr0.add(Byte:8 [destino])
Byte:8 [temporal].copy(Gpr0)
Ipr.flowCall(@normalizar)
Ipr.flowStackJump()
```

Una acción por línea. El ojo sigue el flujo de arriba a abajo. Si se encadena, el formateador lo expande:

```fasm
// Válido, pero el formateador lo convierte en tres líneas:
Gpr0.copy(origen).add(Byte:8 [destino])
```

### 7.3 La regla

> **Se encadena lo que configura un objeto; se bloquea lo que ejecuta un proceso.**

La gramática acepta ambas formas; el estilo y el formateador imponen el ritmo.

---

## 8. El modelo mental del programador

Cuando escribes una línea, haces cinco preguntas en orden:

| # | Pregunta | Respuesta sintáctica |
|---|---|---|
| 1 | ¿Qué contenedor muta? | El receptor, antes del punto |
| 2 | ¿Qué operación física se aplica? | El verbo, después del punto |
| 3 | ¿De dónde vienen los bits? | Los argumentos entre paréntesis |
| 4 | ¿Qué forma tienen? | `Byte:N`, `Bit:N`, `part{N}`, vistas |
| 5 | ¿Bajo qué condición? | `if` + expresión de flags |

Ejemplo:

```fasm
Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})
// 1. Simd0 (contenedor de 512 bits)
// 2. addFloat (suma IEEE 754 por elemento)
// 3. Simd1 (otro contenedor)
// 4. .r512 (ancho) + .part{32} (elemento de 32 bits) → 16 elementos
// 5. sin condición
```

El programador nunca se pregunta "¿qué tipo tiene esto?". Se pregunta "¿qué forma tiene y qué opcode lo hace?".

---

## 9. Qué se prohíbe y por qué

| Anti-patrón | Ejemplo | Por qué está prohibido |
|---|---|---|
| Dato incrustado | `Gpr0.copy(Bit:42)` | El programa contiene información en vez de transportarla (D-001). |
| Tipo abstracto | `Gpr0.add(Int32:1, Int32:2)` | El hardware no tiene `Int32`; la interpretación va en el verbo (D-003). |
| Nombre legacy | `Rax`, `XMM0`, `rFlags`, `mov` | Oculta el archivo y el efecto físico (D-023). |
| Builder / contexto | `Flow.create().load(x).commit()` | El receptor no es el destino real; hay estado oculto (D-005). |
| Temporal invisible | `mem1.copy(mem2)` | El compilador tendría que inventar un registro (D-012). |
| Instrucción "sola" | `.ret()` | No se sabe qué receptor muta; se usa `Ipr.flowStackJump()` (D-005a). |
| Encadenar acciones distintas | `Gpr0.copy(x).add(y).mul(z)` | Válido, pero el formateador lo expande; oculta pasos (D-011). |
| Adivinar el ancho | `Gpr0.copy([x])` | El tamaño del dato debe ser explícito: `Byte:8 [x]` (D-004). |
| Optimización silenciosa | Reordenar instrucciones | Rompe la reproducibilidad y la lectura (D-025). |

---

## 10. La estética de la línea

Una línea FluentASM bien escrita tiene cuatro propiedades:

1. **Un destino visible.** Empieza por el contenedor que cambia.
2. **Un verbo físico.** Se puede nombrar el opcode que produce.
3. **Una fuente explícita.** Se sabe de dónde vienen los bits.
4. **Una forma explícita.** Se sabe cuántos bits y con qué partición.

```fasm
Byte:8 [Gpr5 - 16].copy(Gpr0.low32)
// destino: memoria de 8 bytes en [RBP-16]
// verbo: copy (MOV)
// fuente: Gpr0.low32 (32 bits, zero-extend)
// forma: Byte:8 (extensión de memoria)
```

Si una línea no cumple las cuatro, probablemente está escondiendo algo.

---

## 11. IntelliSense como parte del lenguaje

El lenguaje está diseñado para que un editor pueda sugerir **sin ambigüedad**:

- El receptor determina el conjunto de verbos.
- La vista determina el ancho.
- La partición determina los elementos.
- El contexto (cabecera vs cuerpo) determina si se encadena o se bloquea.
- Las posiciones estructurales se muestran con un tipo de dato distinto (gris/azul) para que el programador sepa que un número ahí es una forma, no un dato.

El IntelliSense no es un adorno: es la interfaz principal del lenguaje. Un lenguaje físico con un buen completado es más rápido de escribir que un lenguaje abstracto con un mal completado.

Véase [18-INTELLISENSE](18-INTELLISENSE.md).

---

## 12. Resumen en una página

- **El código describe flujos, no valores.**
- **Solo hay `Bit:N` y `Byte:N`; la interpretación va en el verbo.**
- **Todo constructo es físicamente honesto y verificable.**
- **Un solo patrón: `destino.verbo(fuente)`.**
- **Fluent para configurar; bloque para ejecutar.**
- **Sin temporales ocultos, sin optimización silenciosa.**
- **Los datos entran por memoria, hardware, E/S, parámetros o recursos.**
- **El IntelliSense se deriva del receptor, no de heurísticas.**
- **Cada error tiene un código y una corrección.**

> *Un ensamblador clásico manipula valores. FluentASM manipula contenedores y relaciones entre ellos.*
