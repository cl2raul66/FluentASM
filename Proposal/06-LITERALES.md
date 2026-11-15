# 06 — Literales: forma y dato

> **Normativo.** Este documento decide, para cada posición del lenguaje, si un número es **forma** (permitido) o **dato** (prohibido en `.execute`), y describe la única válvula de escape: el *bootstrap* de datos.

---

## 1. La pregunta

El lenguaje prohíbe datos incrustados en instrucciones (D-001). Pero el fuente contiene números: `Byte:8`, `part{32}`, `shiftLeft(Bit:3)`, `Byte:16 [Gpr5 - 32]`. ¿Por qué unos son legales y otros no?

La respuesta no está en el número, sino en **la posición**. Un número es legal si describe **la forma del flujo**; es ilegal si describe **el contenido del flujo**.

| Concepto | Descripción | Ejemplo |
|---|---|---|
| **Forma** | Cómo es el contenedor, cuántos bits, qué partición, dónde está | `Byte:8`, `part{16}`, `@tabla`, `Gpr3` |
| **Dato** | Qué información circula | `42`, `3.14`, `"hola"` |

---

## 2. Catálogo de posiciones estructurales

Estas posiciones son **forma** y están permitidas en cualquier sección, incluida `.execute`.

### 2.1 Identificadores de contenedor

| Posición | Ejemplo | Qué describe |
|---|---|---|
| Índice de registro | `Gpr0`, `Simd31`, `Xrf7` | Qué celda física |
| Vista | `.low32`, `.r512`, `.high8` | Qué ventana |
| Archivo | `Frf`, `Ipr`, `Stack` | Qué unidad |
| Miembro de recurso | `Msr.efer`, `Xcr0`, `Srf3.base` | Qué registro con nombre |

### 2.2 Capacidades y anchos

| Posición | Ejemplo | Qué describe |
|---|---|---|
| Magnitud de memoria | `Byte:8 [x]` | Cuántos bytes se acceden |
| Ancho de registro | `Bit:64` | Cuántos bits |
| Capacidad de frame | `.frame(Byte:32)` | Tamaño de la reserva |
| Capacidad de reserva | `Stack.reserve(Byte:16)` | Ajuste de pila |
| Capacidad de reserva de pila | `.stack(MegaByte:1)` | Pila inicial |
| Capacidad de dato | `data x: Byte:4096` | Tamaño del símbolo |
| Capacidad de recurso | `memory(KiloByte:4, ...)` | Tamaño del recurso |

### 2.3 Formas de operación

| Posición | Ejemplo | Qué describe |
|---|---|---|
| Partición SIMD | `part{32}` | Tamaño de elemento |
| Tabla de verdad ternaria | `bitwiseTernary(..., Bit:0x96)` | Qué función lógica ejecuta el hardware |
| Cuenta de desplazamiento | `shiftLeft(Bit:3)`, `rotateRight(Bit:8)` | Cuántos bits desplazar |
| Índice de bit | `testBit(Bit:7)`, `setBit(Bit:0)` | Qué bit |
| Ancho de máscara | `Krf1.low16` | Cuántos bits de máscara |
| Modo de redondeo | `.round(.nearest)` | Cómo redondear |
| Predicado de comparación | `.equal`, `.lessSigned` | Qué comparación |
| Pista de caché | `.t0`, `.nta` | Qué nivel de caché |
| Orden de bytes | `.little`, `.big` | Cómo interpretar |

### 2.4 Direcciones y estados

| Posición | Ejemplo | Qué describe |
|---|---|---|
| Símbolo | `@fin`, `rutina`, `dato` | Una dirección |
| Desplazamiento | `[x + 16]`, `[Gpr5 - 32]` | Un offset en bytes |
| Alineación | `.align(Byte:16)` | Dónde empieza un bloque |
| Estado de flag | `Frf.carry(1)`, `Frf.carry(0)`, `Frf.carry(not)` | Uno de tres estados |
| Estado de bit de control | `Crf4.fsgsbase(1)` | Un bit de configuración |
| Puerto | `Port[0x60]` | Una dirección de E/S |
| Vector de interrupción | `Ipr.flowInterrupt(0x80)` | Un número de vector |

> **Nota sobre `0x80`.** Un vector de interrupción es una **dirección de entrada** del sistema, no un dato del programa: identifica una puerta, igual que `@etiqueta` identifica código. Se admite como estructura. Lo mismo vale para un número de puerto.

### 2.5 Selectores de configuración

| Posición | Valores | Ejemplo |
|---|---|---|
| ABI | `win64`, `sysv` | `.abi(win64)` |
| Target | `x64-sse2`…`x64-avx512full` | `.target(x64-avx2)` |
| Imagen | `flat` | `.image(flat)` |
| Visibilidad | `public`, `private` | `.visibility(public)` |
| Sección | `.execute`, `.rodata`, `.data`, `.bss` | `section .data` |
| Clase de parámetro | `Gpr`, `Simd`, `Stack` | `parameter a: Gpr` |
| Permiso de memoria | `read`, `write`, `execute` | `memory(Byte:64, readWrite)` |

Todos son enumeraciones cerradas. Un valor fuera de la lista → `FASM2025`.

---

## 3. Posiciones de dato

Estas posiciones son **dato** y están prohibidas en `.execute` y en toda instrucción.

| Posición | Ejemplo inválido | Por qué |
|---|---|---|
| Primer argumento de un verbo aritmético | `Gpr0.add(42)` | El 42 es contenido |
| Argumento de `copy` | `Gpr0.copy(42)` | Ídem |
| Argumento de `compare` | `Gpr0.compare(100)` | Ídem |
| Máscara aritmética | `Gpr0.and(0xFF00)` | Ídem |
| Código de retorno | `Gpr0.copy(0)` | Use `Gpr0.clear()` |
| Constante de incremento | `Gpr0.add(1)` | Use `Gpr0.increment()` |
| Cadena en instrucción | `Gpr0.loadAddress("hola")` | La cadena es dato |
| Carácter | `Gpr0.low8.copy('A')` | Ídem |
| Flotante | `Simd0.r128.part{32}.addFloat(1.5)` | Ídem |

### 3.1 ¿Y `Frf.carry(1)`?

Es legal porque `1` no es un valor que circula: es **el estado** de un bit de control. Los estados son tres y están enumerados (`1`, `0`, `not`). No se puede escribir `Frf.carry(42)`.

La diferencia es semántica: `1` en `Frf.carry(1)` selecciona una instrucción (`STC`), no produce un operando inmediato. El compilador nunca emite `mov eax, 1` para ese `1`.

### 3.2 ¿Y `shiftLeft(Bit:3)`?

Es legal porque la cuenta de desplazamiento es una **forma** de la operación: define cuántos bits se mueven, igual que `part{32}` define el tamaño de elemento. La instrucción resultante (`SHL r64, 3`) tiene el 3 en el campo `imm8`, que es un parámetro de forma del encoding, no un dato del programa. La notación canónica exige la unidad: `shiftLeft(Bit:3)`, `testBit(Bit:7)`, `setBit(Bit:0)`.

Por la misma razón son legales los índices de bit (`testBit(Bit:7)`), las escalas de direccionamiento (`*8`) y las alineaciones (`Byte:16`).

**Copia de flag individual.** `Frf.<flag>` como fuente solo se permite con receptor `.low8` (p.ej. `Gpr0.low8.copy(Frf.zero)` → `SETZ AL`). Para anchos mayores: `Gpr0.low8.copy(Frf.zero); Gpr0.loadUnsigned(Gpr0.low8)`.

### 3.3 La prueba de fuego

Para decidir si un número es forma o dato, pregúntate:

> ¿Cambiar este número cambia **la forma del flujo** o **el contenido que circula**?

- Cambiar `Byte:8` a `Byte:4` cambia la forma (otra instrucción, otro ancho). **Forma.**
- Cambiar `42` a `43` en `Gpr0.add(42)` no cambia la forma: sigue siendo "sumar un valor". **Dato.**

---

## 4. Cómo se escribe un valor sin escribirlo

La prohibición no deja al programador sin recursos. Un valor entra por cinco vías.

| Vía | Ejemplo | Cuándo usarla |
|---|---|---|
| **Memoria** | `Gpr0.copy(Byte:8 [constante])` | El valor ya existe en el binario o en el entorno |
| **Parámetro** | `parameter n: Gpr` | El valor lo aporta quien llama |
| **Hardware** | `(Gpr0:Gpr2).readTimestamp()` | El valor lo produce el silicio |
| **E/S** | `Gpr0.low8.copy(Port[0x60])` | El valor viene de un dispositivo |
| **Forma** | `Gpr0.clear()`, `Gpr0.increment()` | El valor es estructural (0 o 1) |

### 4.1 Constantes estructurales sin literal

| Necesidad | Forma canónica | Instrucción |
|---|---|---|
| Cero | `Gpr0.clear()` | `XOR eax, eax` |
| Uno | `Gpr0.clear()` + `Gpr0.increment()` | `XOR` + `INC` |
| Dos | `Gpr0.clear()` + `Gpr0.increment()` + `Gpr0.shiftLeft(Bit:1)` | `XOR` + `INC` + `SHL` |
| −1 | `Gpr0.clear()` + `Gpr0.decrement()` | `XOR` + `DEC` |
| Máscara de bits bajos | `Gpr0.setBits(Bit:8)` | verbo dedicado |
| Dirección de un símbolo | `Gpr0.loadAddress(@x)` | `LEA` |

Estos verbos existen precisamente para que la ausencia de literales no obligue a escribir datos.

### 4.2 Lo que **no** se hace

```fasm
// Mal: el programador "fabrica" un dato
Gpr0.copy(42)
Gpr0.add(7)
Gpr0.and(0xFF)

// Bien
Gpr0.copy(Byte:8 [cuarentaYDos])     // si de verdad es una constante del programa
Gpr0.add(Byte:8 [siete])
Gpr0.and(Byte:8 [mascaraFF])
```

Pero incluso esa solución es sospechosa: si el valor es una constante del programa, ¿por qué no es un parámetro o un recurso? El *bootstrap* es para los datos iniciales, no para esconder constantes mágicas.

---

## 5. Bootstrap: la isla de datos

### 5.1 Regla

Los datos pueden existir en `.rodata` y `.data` como **payload declarativo**. Es la única concesión del lenguaje y está marcada como decisión pendiente **D-1**.

```fasm
section .rodata {
    data mensaje: Byte:10 = "FluentASM\n"
    data tabla: Byte:16 = 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
}
```

### 5.2 Por qué existe

Sin payload, un programa de ejemplo necesitaría:

- un cargador que inyectara bytes en memoria,
- o un dispositivo externo que los aportara,
- o un `resource file("...")` con un archivo aparte.

Nada de eso es razonable para el "hola mundo" ni para los tests. El payload permite que un programa sea autocontenido **sin** meter datos en el flujo de instrucciones: los datos viven en una sección separada, con permisos distintos, y el código solo los referencia por dirección.

### 5.3 Por qué es una concesión y no una traición

| Propiedad | Se cumple |
|---|---|
| El código no contiene datos | Sí: `.execute` sigue prohibiendo literales |
| El dato tiene procedencia visible | Sí: está en un símbolo con nombre y capacidad |
| El dato se puede auditar | Sí: `fasm map` lista todos los payloads con su origen |
| El dato se puede eliminar | Sí: `--no-payload` rechaza cualquier payload y exige recursos |

### 5.4 Modo estricto

```bash
fasm build programa.fasm --no-payload
```

En modo estricto:

- Cualquier `= <payload>` → `FASM2026`.
- Los datos deben venir de `resource file(...)` o quedar sin inicializar en `.bss`.
- Es el modo recomendado para producción.

### 5.5 Marcado

Un payload emite siempre `FASM8101` (info):

```
FASM8101: payload de 14 bytes en 'mensaje' (.rodata).
  Es bootstrap de datos; el flujo de instrucciones no contiene literales.
  Use --no-payload para prohibirlo.
```

---

## 6. Gramática del payload

```
payload      := valor { "," valor }
valor        := número | cadena | repetición | relleno
número       := decimal | hex | binario            // 0–255
cadena       := "\"" { carácter | escape } "\""
escape       := "\\" | "\"" | "\n" | "\r" | "\t" | "\0" | "\x" hex hex
repetición   := "repeat" número "{" payload "}"
relleno      := "zero" magnitud
```

Ejemplos:

```fasm
data a: Byte:4   = 1, 2, 3, 4
data b: Byte:10  = "FluentASM\n"
data c: Byte:8   = "\x00\x01\x02\x03\x04\x05\x06\x07"
data d: Byte:64  = repeat 64 { 0 }
data e: Byte:16  = "AB" , zero Byte:14
data f: Byte:1   = '\n'          // ✗ no hay literales de carácter
```

Reglas:

- Cada número debe caber en un byte (0–255) → `FASM2027`.
- La suma de tamaños debe igualar la capacidad → `FASM2019`.
- `repeat N { ... }` se expande en tiempo de ensamblado; `N` es una capacidad.
- `zero Byte:N` rellena con ceros.
- Una cadena es una secuencia de bytes UTF-8; los escapes producen un byte.

---

## 7. Lo que el checker debe rechazar

### 7.1 Reglas de decisión

El checker recorre el AST y, para cada literal numérico, consulta la **firma del verbo** o la **posición gramatical**:

| Posición en la firma | Tipo de literal | Veredicto |
|---|---|---|
| `capacity` | `Byte:N` / `Bit:N` | forma |
| `form` | índice, partición, cuenta | forma |
| `immediate` | número suelto | **dato** → error en `.execute` |
| `payload` | número o cadena en `data` | dato, permitido con aviso |

Si un verbo declara un parámetro `immediate`, el lenguaje **no** lo expone en la superficie: el verbo debe tener una variante estructural o recibir la fuente desde memoria. Por eso no existe `Gpr0.add(<número>)`.

### 7.2 Casos límite

| Caso | Veredicto | Motivo |
|---|---|---|
| `Gpr0.shiftLeft(Bit:3)` | forma | cuenta de bits |
| `Gpr0.rotateRight(Bit:1)` | forma | cuenta de bits |
| `Gpr0.testBit(Bit:7)` | forma | índice de bit |
| `Gpr0.setBits(Bit:8)` | forma | ancho de máscara |
| `Frf.carry(1)` | forma | estado |
| `Simd0.r512.part{32}` | forma | partición |
| `Byte:32 [Gpr5 - 32]` | forma | offset |
| `.align(Byte:16)` | forma | alineación |
| `Ipr.flowInterrupt(0x80)` | forma | vector/dirección |
| `Port[0x60]` | forma | dirección de E/S |
| `data x: Byte:1 = 42` | dato permitido | payload |
| `Gpr0.add(42)` | **dato prohibido** | operando inmediato |
| `Gpr0.copy(0)` | **dato prohibido** | use `clear()` |
| `Gpr0.compare(0)` | **dato prohibido** | use `test` o memoria |
| `Gpr0.low8.copy('A')` | **dato prohibido** | no hay caracteres |

---

## 8. Diagnósticos

| Código | Severidad | Situación | Corrección |
|---|---|---|---|
| `FASM2007` | error | Dato literal en `.execute` | mueva el valor a `.rodata`/`.data` o a un parámetro |
| `FASM2008` | error | Payload en `.bss` | use `.data` o quite el payload |
| `FASM2019` | error | Payload de tamaño distinto a la capacidad | ajuste `Byte:N` o el payload |
| `FASM2025` | error | Selector de configuración inválido | use un valor de la lista |
| `FASM2026` | error | Payload en modo `--no-payload` | use `resource file(...)` |
| `FASM2027` | error | Byte de payload fuera de 0–255 | use `Byte:2` o `repeat` |
| `FASM2028` | error | Cadena en contexto de instrucción | use un símbolo de `.rodata` |
| `FASM2029` | error | Carácter literal (`'A'`) | use `data` con `"A"` |
| `FASM2030` | error | Flotante literal | no existe; use bytes IEEE 754 en `.rodata` |
| `FASM8101` | info | Payload usado (bootstrap) | ninguno, o `--no-payload` |
| `FASM8103` | info | `data` sin payload en `.rodata` | ¿quería `.bss`? |

---

## 9. Ejemplos

### 9.1 Programa sin payload

```fasm
program eco.abi(win64).entry(main) {
    section .execute {
        routine main {
            parameter entrada: Gpr        // el valor lo aporta quien llama
            Gpr0.copy(entrada)
            Gpr0.clear()                  // cero estructural, no literal
            Ipr.flowStackJump()
        }
    }
}
```

### 9.2 Programa con bootstrap

```fasm
program saludo.abi(win64).entry(main) {
    section .rodata {
        data texto: Byte:11 = "Hola mundo\n"     // FASM8101 (info)
    }
    section .execute {
        routine main {
            Gpr0.loadAddress(texto)               // dirección, no contenido
            Gpr0.clear()
            Ipr.flowStackJump()
        }
    }
}
```

### 9.3 El mismo programa en modo estricto

```fasm
program saludo.abi(win64).entry(main) {
    section .rodata {
        resource texto: file("texto.bin")         // el contenido vive fuera del fuente
    }
    section .execute {
        routine main {
            Gpr0.loadAddress(texto)
            Ipr.flowStackJump()
        }
    }
}
```

---

## 10. Resumen

- Un literal es legal si describe **forma**, no **contenido**.
- Posiciones estructurales: registros, capacidades, particiones, cuentas, índices, offsets, alineaciones, estados, direcciones, selectores.
- Posiciones de dato: operandos inmediatos, máscaras aritméticas, cadenas, caracteres, flotantes.
- Un valor entra por memoria, parámetro, hardware, E/S o forma estructural.
- El payload de datos es una concesión de *bootstrap*, aislada en `.rodata`/`.data`, marcada y desactivable.
- Todo caso límite tiene un `FASM20xx` con corrección.
