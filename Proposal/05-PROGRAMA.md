# 05 — Programa, secciones y rutinas

> **Normativo.** Define la estructura del fuente: el bloque `program`, las cuatro secciones, las rutinas, los parámetros, los locales, los datos, los recursos y los atributos de cada nivel.

---

## 1. Un archivo, un programa

Un archivo `.fasm` contiene **exactamente un** bloque `program` en el nivel superior. Nada de código suelto fuera de él (salvo comentarios).

```fasm
program demo {
    section .execute {
        routine main {
            Ipr.flowStackJump()
        }
    }
}
```

Un proyecto (`.fproj`) agrupa uno o más archivos; el linker recibe los objetos y produce una imagen (doc 16). Un archivo sin `program` → `FASM2001`.

---

## 2. Cabecera del programa

```fasm
program <nombre> [ .atributo(...) ]* {
    ...
}
```

| Atributo | Valores | Por defecto | Efecto |
|---|---|---|---|
| `.abi(...)` | `win64`, `sysv` | **requerido** | Convención de llamada del programa |
| `.target(...)` | `x64-sse2`, `x64-avx2`, `x64-avx512`, `x64-avx512full` | `x64-avx2` | Conjunto de instrucciones permitido |
| `.entry(...)` | nombre de rutina | primera rutina | Punto de entrada |
| `.image(...)` | `flat` | `flat` | Formato de salida (v1 solo plano) |
| `.stack(Byte:N)` | capacidad | `MegaByte:1` | Pila inicial que reserva el cargador |
| `.ring(...)` | `0`–`3` | `3` | Nivel de privilegio esperado |

Ejemplo:

```fasm
program hola.abi(win64).target(x64-avx2).entry(main).stack(MegaByte:1) {
    ...
}
```

Reglas:

- Los atributos se encadenan en la cabecera, en cualquier orden.
- Un atributo repetido → `FASM2002`.
- Un atributo desconocido → `FASM2004` con la lista de válidos.
- `.abi` y `.target` se heredan a todas las rutinas; una rutina puede sobreescribir `.abi`.

---

## 3. Secciones

Cuatro secciones canónicas (D-015). El orden de declaración en el fuente es libre; el linker las ordena.

| Sección | Permisos | Contenido | Payload |
|---|---|---|---|
| `.execute` | R + X | Rutinas | **prohibido** |
| `.rodata` | R | Datos constantes | permitido (bootstrap) |
| `.data` | R + W | Datos inicializados | permitido (bootstrap) |
| `.bss` | R + W | Datos reservados | **prohibido** (siempre cero) |

```fasm
section .rodata.align(Byte:16) {
    data mensaje: Byte:10 = "FluentASM\n"
}
```

| Atributo | Valores | Efecto |
|---|---|---|
| `.align(Byte:N)` | potencia de 2 | alineación inicial; por defecto 16 |
| `.visibility(...)` | `public`, `private` | exportación de símbolos |

Reglas:

- Una sección puede declararse **una sola vez** por programa; repetirla → `FASM2005` (el linker las fusionaría sin que el programador lo vea).
- Sección desconocida → `FASM2006`.
- `data` en `.execute` → `FASM2007`.
- `data` con payload en `.bss` → `FASM2008`.
- `routine` fuera de `.execute` → `FASM2009`.
- Una sección vacía es válida (se omite del binario).

---

## 4. Rutinas

```fasm
routine <nombre> [ .atributo(...) ]* {
    [ parameter ... ]*
    [ local ... ]*
    <sentencias>
}
```

### 4.1 Atributos de rutina

| Atributo | Valores | Por defecto | Efecto |
|---|---|---|---|
| `.abi(...)` | `win64`, `sysv` | **requerido** | Convención de esta rutina |
| `.frame(Byte:N)` | capacidad múltiplo de 16 | `Byte:0` | Reserva de locales; alinea la pila |
| `.preserves(...)` | lista de `GprN` | vacío | Guarda/restaura alrededor del cuerpo |
| `.shadow(...)` | `0`, `1` | `1` en Win64, `0` en SysV | 32 bytes de shadow space |
| `.redzone(...)` | `0`, `1` | `1` en SysV, `0` en Win64 | 128 bytes bajo RSP |
| `.naked(...)` | `0`, `1` | `0` | Sin prólogo/epílogo generado |
| `.align(Byte:N)` | potencia de 2 | ninguna | Alineación del inicio |
| `.ring(...)` | `0`–`3` | heredado | Privilegio requerido |
| `.visibility(...)` | `public`, `private` | `private` | Exportación |
| `.returns(...)` | `Gpr`, `Simd`, `none` | `none` | Contrato de retorno |
| `.leaf(...)` | `0`, `1` | `0` | Declara que no llama a nadie |

### 4.2 Ejemplo

```fasm
routine procesar.abi(win64).frame(Byte:32).preserves(Gpr3, Gpr12).returns(Gpr) {
    parameter origen: Gpr
    parameter cantidad: Gpr
    local acumulador: Byte:8

    Gpr0.copy(origen)
    Byte:8 [acumulador].copy(Gpr0)
    Gpr0.copy(Byte:8 [acumulador])
    Ipr.flowStackJump()
}
```

### 4.3 Reglas

- El nombre debe ser único en el programa → `FASM2010` (duplicado).
- No hay rutinas anidadas → `FASM2011`.
- `parameter` y `local` solo al inicio del cuerpo, antes de la primera sentencia → `FASM2012`.
- Una rutina sin `flowStackJump` en algún camino → `FASM2013` (advertencia, no error: puede terminar con `flowJump`).
- `.naked(1)` con `.frame`, `.preserves` o `.shadow` → `FASM2014` (contradicción).
- `.frame(Byte:N)` no múltiplo de 16 → `FASM2015`.

---

## 5. Parámetros

```fasm
parameter <nombre>: <clase>
```

| Clase | Significado | Cómo se usa |
|---|---|---|
| `Gpr` | El parámetro llega en un registro de propósito general | `nombre` es el registro |
| `Simd` | Llega en un registro vectorial | `nombre` es el registro |
| `Stack` | Llega en la pila (o se pasa en pila) | `Byte:N [nombre]` es el slot |

Ejemplos:

```fasm
parameter a: Gpr                 // Gpr1 en Win64 (primer entero)
parameter b: Gpr                 // Gpr2
parameter v: Simd                // Simd0
parameter extra: Stack           // slot en [Gpr5 + 16]
```

Reglas:

- El orden de los `parameter` determina la asignación según la ABI (doc 12).
- Un `parameter` de clase `Gpr` **es** el registro: `Gpr1.copy(a)` es ilegal (mismo registro); `a.copy(Gpr0)` es válido.
- Un `parameter` de clase `Stack` solo se usa con capacidad y corchetes.
- `parameter` con nombre duplicado → `FASM2016`.
- Más parámetros que los que la ABI permite en registro se asignan a `Stack` automáticamente; el compilador **no** crea el slot, solo lo nombra → `FASM5002` si el frame no lo reserva.

---

## 6. Locales

```fasm
local <nombre>: Byte:N
```

Un local es **memoria** en el frame de la rutina, direccionada como `Byte:N [Gpr5 - offset]`.

```fasm
local temporal: Byte:8
local tabla: Byte:256

Byte:8 [temporal].copy(Gpr0)
Byte:1 [tabla + Gpr1].copy(Gpr2.low8)
```

Reglas:

- El frame debe ser suficiente: la suma de locales y su alineación no puede exceder `.frame(Byte:N)` → `FASM2017`.
- Sin `.frame`, declarar locales → `FASM2018`.
- `local` de una clase de registro no existe: para un registro se usa `parameter` o directamente `GprN`.
- Un local no se inicializa: el contenido es el que haya en la pila. Si se necesita cero, se escribe explícitamente.

---

## 7. Datos

```fasm
data <nombre>: Byte:N [ = <payload> ]
```

| Forma | Ejemplo | Sección |
|---|---|---|
| Reserva sin payload | `data buffer: Byte:4096` | `.data` o `.bss` |
| Inicializado con número | `data x: Byte:8 = 42` | `.rodata` / `.data` |
| Inicializado con lista | `data v: Byte:4 = 1, 2, 3, 4` | `.rodata` / `.data` |
| Inicializado con texto | `data s: Byte:10 = "FluentASM\n"` | `.rodata` |
| Repetición | `data ceros: Byte:64 = repeat 64 { 0 }` | `.rodata` / `.data` |

Reglas:

- `Byte:N` es la **capacidad**. El payload debe ocupar exactamente `N` bytes → `FASM2019`.
- El payload es la única forma de dato literal del lenguaje y solo puede aparecer en `.rodata` / `.data` (D-1).
- `data` sin payload en `.rodata` es válido pero inútil; se marca con `FASM8103` (info).
- Un dato sin payload en `.data` queda con contenido indeterminado; si se necesita cero, va en `.bss`.
- El nombre del dato es su **dirección**; el contenido se lee con capacidad y corchetes.

---

## 8. Recursos

```fasm
resource <nombre>: <tipo>(<parámetros>)
```

| Tipo | Forma | Disponibilidad |
|---|---|---|
| `file` | `file("ruta")` | v1: solo lectura, embebido |
| `memory` | `memory(Byte:N, <permisos>)` | v1 |
| `port` | `port(<dirección>)` | v1 |
| `library` | `library("nombre")` | fase 2 |

Ejemplo:

```fasm
resource tabla: file("datos/tabla.bin")
resource scratch: memory(KiloByte:4, readWrite)
```

Reglas:

- Un recurso es un símbolo de dirección, como un `data`.
- `file` se embebe en `.rodata` y el linker lo alinea.
- Un recurso no declarado → `FASM2020`.
- En v1 no hay carga dinámica: todo recurso es estático (D-029).

---

## 9. Etiquetas

```fasm
@nombre:
```

- Ámbito: la rutina que las contiene.
- Dos etiquetas con el mismo nombre en la misma rutina → `FASM2021`.
- Una etiqueta sin referencia es válida (puede ser destino de un cálculo de dirección).
- `@nombre` es siempre una dirección de código.
- Las etiquetas no son visibles fuera de su rutina: para eso se usan rutinas.

---

## 10. Punto de entrada

Tres formas, en orden de precedencia:

1. `.entry(rutina)` en la cabecera del programa. El argumento es un **símbolo de programa** (nombre de rutina), nunca un label `@local`.
2. La primera rutina declarada en `.execute`.
3. Si `.execute` no tiene rutinas → `FASM2022`.

El linker emite un salto de 5 bytes al inicio de `.execute` si la entrada no está ya ahí (doc 16).

---

## 11. Orden de declaraciones

Dentro de una rutina:

```
parameter*  →  local*  →  sentencias
```

Cualquier declaración después de una sentencia → `FASM2012`.

Dentro del programa:

```
program { section* }
section .execute { routine* }
section .rodata { data* | resource* }
section .data   { data* | resource* }
section .bss    { data* }
```

Un `resource` en `.execute` → `FASM2023`.

---

## 12. Ejemplo completo

```fasm
program calculadora.abi(win64).target(x64-avx2).entry(main) {

    section .rodata {
        data etiqueta: Byte:8 = "result: "
        data tabla: Byte:32 = repeat 32 { 0 }
    }

    section .bss {
        data resultado: Byte:8
    }

    section .execute {

        routine sumar.abi(win64).frame(Byte:32).preserves(Gpr3).returns(Gpr) {
            parameter a: Gpr
            parameter b: Gpr
            local temporal: Byte:8

            Gpr0.copy(a)
            Byte:8 [temporal].copy(Gpr0)
            Gpr0.copy(Byte:8 [temporal])
            Gpr0.add(b)
            Ipr.flowStackJump()
        }

        routine main.abi(win64).frame(Byte:32).shadow(1).returns(Gpr) {
            parameter entrada: Gpr

            Gpr0.copy(entrada)
            Gpr1.copy(entrada)
            Ipr.flowCall(sumar)             // Gpr0 = entrada + entrada
            Byte:8 [resultado].copy(Gpr0)
            Ipr.flowStackJump()
        }
    }
}
```

---

## 13. Diagnósticos del programa

| Código | Situación |
|---|---|
| `FASM2001` | Archivo sin bloque `program` |
| `FASM2002` | Atributo repetido |
| `FASM2003` | Símbolo no definido |
| `FASM2004` | Atributo desconocido |
| `FASM2005` | Sección declarada dos veces |
| `FASM2006` | Sección desconocida |
| `FASM2007` | `data` en `.execute` / dato en instrucción |
| `FASM2008` | Payload en `.bss` |
| `FASM2009` | `routine` fuera de `.execute` |
| `FASM2010` | Rutina duplicada |
| `FASM2011` | Rutina anidada |
| `FASM2012` | Declaración fuera de la cabecera de la rutina |
| `FASM2013` | Camino sin retorno (advertencia) |
| `FASM2014` | `.naked(1)` con prólogo declarado |
| `FASM2015` | `.frame` no múltiplo de 16 |
| `FASM2016` | Parámetro duplicado |
| `FASM2017` | Locales exceden el frame |
| `FASM2018` | Locales sin `.frame` |
| `FASM2019` | Payload de tamaño distinto a la capacidad |
| `FASM2020` | Recurso no declarado |
| `FASM2021` | Etiqueta duplicada en la rutina |
| `FASM2022` | Programa sin entrada |
| `FASM2023` | `resource` en `.execute` |
| `FASM2024` | `.entry` apunta a un símbolo inexistente |

---

## 14. Resumen

- Un archivo, un `program`, cuatro secciones canónicas.
- Cabecera encadenada para configuración; cuerpo en bloque para ejecución.
- `parameter` declara el contrato de entrada; `local` declara memoria de frame.
- `data` declara capacidad y, opcionalmente, payload (solo en datos).
- `resource` declara un activo externo embebido.
- Las etiquetas son locales a la rutina.
- La entrada es explícita o la primera rutina.
