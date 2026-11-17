# Referencia de sintaxis de FluentASM (Común)

> **Documento compartido.** Define la sintaxis, el modelo de tipos físicos, las reglas de flujo *type-state*, los contratos y los verbos comunes a **todas** las arquitecturas soportadas por FluentASM.
> Los registros físicos concretos, modos de direccionamiento propios y codificación a bytes de cada procesador se definen en su documento específico (para x64, véase [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md)).

## 1. Propósito de la capa común

FluentASM separa lo que es **universal en la programación de máquinas** (cómo se estructura un programa, cómo se miden bits y bytes, cómo fluyen los datos, cómo se definen contratos entre rutinas y cómo se garantiza la Seguridad Tipo 1) de lo que pertenece a **una CPU concreta** (cuántos registros físicos tiene, cómo se llaman o qué instrucciones exclusivas posee).

Todo lo definido en este documento busca que un programa que utilice únicamente construcciones comunes produzca el **mismo resultado observable** sin importar la arquitectura destino, verificándose mediante pruebas equivalentes cuando se incorporen nuevas arquitecturas (Fase 3).

## 2. Proyecto y estructura de un archivo `sourcesCode.fasm`

### 2.1 Vínculo con `.fproj`

En Fase 1, cada proyecto vive en su propio directorio (`src/<nombreProyecto>/`) con dos archivos principales:

- `<nombreProyecto>.fproj`: configuración del proyecto en texto simple legible (nombre del proyecto, arquitectura objetivo, archivos fuente, punto de entrada).
- `sourcesCode.fasm`: código fuente cuyo identificador raíz **debe coincidir** con el nombre declarado en el `.fproj`.

No se repite una palabra clave genérica `program`: el proyecto ya fue nombrado en `.fproj`.

### 2.2 Flujo *Type-State* de nivel superior

El archivo sigue un flujo guiado por contexto (*type-state*):

```text
NombreDelProyecto  →  [ .data(...) ]  →  .execute(...)  →  .initialize()
```

- **`.data(dt -> { ... })` *(opcional)*:** Declara espacios de datos, valores iniciales o recursos externos. Si se omite, el programa no genera sección de datos propia.
- **`.execute(ex -> { ... })` *(obligatoria)*:** Contiene las rutinas ejecutables del programa.
- **`.initialize()` *(terminal obligatorio)*:** Cierra el contrato del programa e indica al compilador que la estructura está completa. Después de `.initialize()` no es válido encadenar más secciones.

Bajo el **Principio de Garantía de Completitud**, todo campo de configuración no especificado explícitamente (como tamaño de pila inicial o plantilla por defecto de la arquitectura activa) toma los valores predeterminados documentados del proyecto en `.fproj`, conocidos tanto por el programador como por el compilador.

### 2.3 Ejemplo completo (ilustrado sobre x64)

*(Los nombres `Gpr0`, `Gpr1`, `Gpr2` corresponden al archivo de registros nativo de x64 definido en [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md)).*

```fasm
// Proyecto: src/miCalculadoraCli/miCalculadoraCli.fproj
// Código:   src/miCalculadoraCli/sourcesCode.fasm

miCalculadoraCli
    .data(dt -> {
        dt.define<Byte:8>(d -> d.id(numA).value(10))
        dt.define<Byte:8>(d -> d.id(numB).value(20))
    })
    .execute(ex -> {
        ex.routines(rt -> {

            rt.define(d -> {
                d.id(main)
                d.returns(Gpr0)
                d.run(() -> {
                    Gpr1.copy([numA])
                    Gpr2.copy([numB])
                    Ipr.flowCall(c -> c.target(sumar).inputs(Gpr1, Gpr2).output(Gpr0))
                    Ipr.flowStackJump()
                })
            })

            rt.define(d -> {
                d.id(sumar)
                d.returns(Gpr0)
                d.run((arg1<Byte:8>, arg2<Byte:8>) -> {
                    Gpr0.copy(arg1)
                    Gpr0.add(arg2)
                    Ipr.flowStackJump()
                })
            })

        })
    })
    .initialize()
```

## 3. Reglas léxicas y sintácticas

### 3.1 Convenciones de escritura y sensibilidad a mayúsculas

El lenguaje es **estrictamente sensible a mayúsculas y minúsculas**.

| Elemento | Convención | Ejemplos |
|---|---|---|
| Registros, unidades y magnitudes | `PascalCase` | `Gpr0`, `Frf`, `Ipr`, `Stack`, `Cpu`, `Bit`, `Byte` |
| Verbos, vistas y miembros | `camelCase` | `copy`, `add`, `low32`, `flowCall`, `flowStackJump` |
| Identificadores de usuario | `camelCase` | `miCalculadoraCli`, `numA`, `sumar` |
| Etiquetas locales | `@` + `camelCase` | `@bucle`, `@fin` |
| Comentarios | `//` o `/* ... */` | `// nota de línea`, `/* bloque */` |

### 3.2 Literales numéricos estructurales y de datos

| Tipo | Sintaxis | Ejemplos |
|---|---|---|
| Decimal | dígitos (con `_` opcional) | `8`, `64`, `1_024` |
| Hexadecimal | `0x` + dígitos hex | `0x10`, `0x03F8`, `0xFF` |
| Binario | `0b` + bits | `0b1010_0101` |
| Negativo | `-` + entero (solo offsets de dirección) | `-16` |
| Cadena | comillas dobles con escapes (solo en `.data`) | `"Hola\n"` |

Escapes válidos en cadenas de `.data`: `\n`, `\t`, `\r`, `\0`, `\\`, `\"`, `\xNN`.
No existen literales de coma flotante ni de carácter aislado en instrucciones.

### 3.3 Puntuación y desambiguación

| Símbolo / Forma | Regla | Ejemplo |
|---|---|---|
| **`()`** | Realmente vacío (sin parámetros ni argumentos) | `d.run(() -> { ... })`, `Ipr.flowStackJump()` |
| **`(_)` o `_`** | Implícito por plantilla (elemento único o configuración por defecto) | `.routines(_ -> { ... })` |
| **`(x, y)`** | Lista homogénea del mismo tipo y en orden | `.inputs(Gpr1, Gpr2)`, `(arg1<Byte:8>, arg2<Byte:8>)` |
| **`.`** | Navegación *type-state*, acceso a ventana/estado o invocación de acción | `Gpr0.low32`, `Gpr0.copy(...)` |
| **`:`** | Calificación de magnitud y medida física | `Bit:64`, `Byte:8`, `KiloByte:4` |
| **`<...>`** | Especificación de capacidad/ancho en definiciones o entradas | `dt.define<Byte:8>(...)`, `arg1<Byte:8>` |
| **`{ ... }`** | Bloque legible de declaraciones o instrucciones | `d.run(() -> { ... })` |
| **`[ ... ]`** | Contenido de memoria o dirección de puerto | `[numA]`, `Byte:8 [Gpr3]`, `Port[...]` |

### 3.4 Reglas de oro de las llamadas fluidas

1. **Un solo argumento por verbo de acción:** Las acciones fluidas dentro del cuerpo (`destino.verbo(fuente)`) reciben un único argumento; la coma `,` queda reservada exclusivamente para listas homogéneas (`.inputs(...)`, lista de entradas de `d.run(...)` o lista de bytes en `.value(...)`).
2. **Estado vs. Acción ejecutada:**
   - **Sin paréntesis `()`:** señala una parte física o estado (`Gpr0.low32`, `Frf.zero`, `Ipr.current`). No genera instrucción por sí solo.
   - **Con paréntesis `()`:** representa una instrucción real que el procesador ejecuta (`Gpr0.copy(...)`, `Cpu.halt()`, `Ipr.flowStackJump()`).
3. **Prohibido inventar propiedades sobre constructores o registros:** Después de un `.`, el editor y el compilador solo aceptan miembros pertenecientes al tipo del receptor (nunca nombres de usuario como `rt.sumar` o `Gpr0.sumar`). Los nombres definidos por el usuario se pasan como identificador dentro de métodos dedicados (`d.id(sumar)`, `c.target(sumar)`).
4. **Una acción por línea en bloques:** En el cuerpo de `d.run(...)` se escribe una instrucción por línea para favorecer la lectura y depuración. En las configuraciones declarativas (`d -> d.id(...).value(...)`) sí se encadenan propiedades del mismo descriptor.

## 4. Modelo de Tipos Físicos (ATM): `Bit:N` y `Byte:N`

FluentASM no posee tipos abstractos (`int`, `uint`, `float`, `bool`, `char`, `string`, `struct`, `pointer`). Todo contenedor o acceso se mide únicamente mediante dos magnitudes físicas:

| Magnitud | Uso | Contexto válido |
|---|---|---|
| **`Bit:N`** | Ancho en bits (`1 Byte = 8 Bit`) | Anchos de registros, flags, cuentas de desplazamiento (`shiftLeft(Bit:3)`), índices de bit (`testBit(Bit:7)`) |
| **`Byte:N`** | Extensión en bytes | Memoria (`dt.define<Byte:8>`, `Byte:8 [Gpr3]`), reservas de pila (`Stack.reserve(Byte:16)`), alineaciones |

### 4.1 Múltiplos binarios

Solo potencias de 2 (`1 024`), destinados a capacidades de memoria o reservas:

- `KiloBit`, `MegaBit`, `GigaBit`, `TeraBit`
- `KiloByte`, `MegaByte`, `GigaByte`, `TeraByte`

### 4.2 Separación estricta de contextos (Seguridad Tipo 1)

- Usar `Byte:N` donde la CPU espera una cuenta de bits o ancho de registro produce error de contexto (`FASM3xxx`).
- Usar `Bit:N` donde se espera una extensión o desplazamiento en memoria produce error de contexto (`FASM3xxx`).
- Equivalencia matemática controlada: un valor definido en memoria como `Byte:8` ocupa exactamente 64 bits (`Bit:64`), por lo que puede transferirse de forma segura hacia o desde un registro de 64 bits.

## 5. Datos, `defaultValue` y Separación de `.execute`

### 5.1 Sin datos duros en `.execute`

Las instrucciones dentro de `.execute(...)` describen flujos y transformaciones, **nunca datos mágicos incrustados** (por ejemplo, `Gpr0.copy(42)` o `Gpr0.add(7)` están prohibidos: `FASM2xxx`).

Un literal numérico dentro de `.execute(...)` solo es válido cuando describe **forma o estructura física**:
- Índice de registro (`Gpr0`)
- Ancho o capacidad (`Bit:3`, `Byte:8`)
- Desplazamiento de dirección (`[Gpr5 - 16]`)
- Estado de bandera (`Frf.carry(1)`, `Frf.carry(0)`)
- Dirección de puerto hardware (`Port[0x03F8]`)

### 5.2 Estado base limpio: `defaultValue`

Cuando dentro de `.execute(...)` se necesita inicializar o limpiar un contenedor a su estado base seguro (todos sus bits en cero) sin escribir un `0` literal:

```fasm
Gpr0.copy(defaultValue)
```

El compilador emite la forma física más corta y directa de la arquitectura para poner el contenedor en cero, sin lecturas de memoria externa.

### 5.3 Declaración de valores en `.data(...)`

Los valores concretos viven en `.data(...)` o en recursos externos:

```fasm
.data(dt -> {
    dt.define<Byte:8>(d -> d.id(numA).value(10))
    dt.define<Byte:10>(d -> d.id(mensaje).value("FluentASM\n"))
    dt.define<Byte:64>(d -> d.id(buffer).value(defaultValue))
})
```

- **Símbolo definido en `.data`:** al leerlo o escribirlo en `.execute` mediante `[numA]`, el compilador ya conoce su capacidad (`Byte:8`) y valida que coincida con el registro receptor/fuente.
- **Dirección genérica en registro:** cuando se accede a memoria mediante la dirección contenida en un registro (`[Gpr3]` o `[Gpr3 + 8]`), es obligatorio anteponer la capacidad exacta: `Byte:8 [Gpr3]`.
- **Dirección del símbolo:** para cargar la dirección en sí misma (y no su contenido), se usa `loadAddress(numA)`.

## 6. Rutinas, Contratos y Flujo de Control

### 6.1 Definición de rutinas

Dentro de `.execute(ex -> { ex.routines(rt -> { ... }) })`:

```fasm
rt.define(d -> {
    d.id(sumar)
    d.returns(Gpr0)
    d.run((arg1<Byte:8>, arg2<Byte:8>) -> {
        Gpr0.copy(arg1)
        Gpr0.add(arg2)
        Ipr.flowStackJump()
    })
})
```

- `d.id(nombre)`: identificador único de la rutina.
- `d.returns(registro)`: declara el contrato de retorno (o se omite si la rutina no devuelve valor).
- `d.run(() -> { ... })` o `d.run((arg1<...>, ...) -> { ... })`: bloque de instrucciones. Los argumentos declarados en `d.run` son **alias directos con tamaño verificado** sobre los registros físicos de entrada asignados por la convención de la arquitectura activa; no crean variables ni copias ocultas.
- Si una rutina es la única del programa, puede declararse con `main` o mediante la forma implícita `_`. Si hay varias, `main` (o el símbolo configurado en `.fproj`) es el punto de entrada.

### 6.2 Cierre explícito de rutinas

Toda rutina debe cerrar cada camino de ejecución de forma explícita:
- `Ipr.flowStackJump()` — retorna a quien llamó extrayendo la dirección de la pila (`Stack`).
- `Cpu.halt()` — detiene el procesador (válido en rutinas *bare metal* que no retornan a ningún arrancador superior).

### 6.3 Llamadas con contrato visible (`Ipr.flowCall`)

Para invocar una rutina que recibe entradas y produce salida:

```fasm
Ipr.flowCall(c -> c.target(sumar).inputs(Gpr1, Gpr2).output(Gpr0))
```

Si la rutina no recibe entradas ni devuelve valor, puede abreviarse:

```fasm
Ipr.flowCall(miRutina)
```

**Reglas de Seguridad Tipo 1 en `flowCall`:**
1. El compilador verifica que cada registro listado en `.inputs(...)` haya sido cargado previamente con un tamaño compatible con la rutina destino.
2. El compilador verifica que `.output(...)` coincida con `d.returns(...)` de la rutina destino.
3. **Invalidación lógica post-llamada:** al regresar de `Ipr.flowCall(...)`, el compilador marca lógicamente los registros usados en `.inputs(...)` como consumidos/no confiables (sin insertar instrucciones ocultas). Si se intenta volver a leerlos sin recargarlos antes, se emite error de compilación.

### 6.4 Saltos, etiquetas locales y condiciones (`Ipr` y `Frf`)

Las etiquetas locales llevan prefijo `@` y solo son visibles dentro de su rutina:

```fasm
@bucle:
    // instrucciones
    Ipr.flowJump(@bucle if Frf.notEqual)
```

Las condiciones consultan el registro de banderas `Frf`:
- **Banderas físicas directas:** `Frf.zero`, `Frf.carry`, `Frf.sign`, `Frf.overflow`, `Frf.parity` (precedibles por `not`, p. ej. `not Frf.zero`).
- **Alias legibles equivalentes:** `Frf.equal`, `Frf.notEqual`, `Frf.less`, `Frf.lessOrEqual`, `Frf.greater`, `Frf.greaterOrEqual` (con signo); `Frf.below`, `Frf.belowOrEqual`, `Frf.above`, `Frf.aboveOrEqual` (sin signo).

El compilador rastrea qué instrucciones definen o dejan indefinida cada bandera; leer una bandera indefinida en un salto condicional produce error (`FASM4xxx`).

## 7. Verbos Comunes

Los siguientes verbos forman la base común del lenguaje (el soporte exacto, registros válidos y codificación por arquitectura se detallan en cada documento de CPU):

| Grupo | Verbos |
|---|---|
| **Transferencia y memoria** | `copy`, `copyUnsigned`, `copySigned`, `loadAddress`, `exchange`, `conditionalCopy`, `copyBlock`, `fillBlock` |
| **Aritmética entera** | `add`, `sub`, `addWithCarry`, `subWithBorrow`, `increment`, `decrement`, `negate`, `multiply`, `multiplyWide`, `divide`, `divideSigned`, `signExtend`, `compare` |
| **Lógica y bits** | `and`, `or`, `xor`, `not`, `test`, `shiftLeft`, `shiftRightLogical`, `shiftRightArithmetic`, `rotateLeft`, `rotateRight`, `testBit`, `setBit`, `clearBit`, `toggleBit` |
| **Banderas (`Frf`)** | `carry(1 \| 0 \| not)`, `direction(1 \| 0)`, `interrupt(1 \| 0)` |
| **Flujo (`Ipr`)** | `flowJump`, `flowCall`, `flowStackJump`, `flowLoop` |
| **Pila (`Stack`)** | `Stack.push`, `pop(Stack)`, `Stack.reserve`, `Stack.release` |
| **Hardware (*bare metal*)** | `Cpu.noOperation`, `Cpu.pause`, `Cpu.halt`, `Port[...]` |

## 8. Gramática Formal Común (EBNF resumida)

```ebnf
archivo          = nombreProyecto , [ seccionData ] , seccionExecute , ".initialize()" ;
nombreProyecto   = identificador ;

seccionData      = ".data" , "(" , ident , "->" , "{" , { defDato } , "}" , ")" ;
defDato          = ident , ".define" , "<" , magnitud , ">" , "(" , ident , "->" , cadenaConfigDato , ")" ;

seccionExecute   = ".execute" , "(" , ident , "->" , "{" , bloqueRutinas , "}" , ")" ;
bloqueRutinas    = ident , ".routines" , "(" , ( ident , "->" , "{" , { defRutina } , "}"
                                               | "_" , "->" , "{" , { sentencia } , "}" ) , ")" ;

defRutina        = ident , ".define" , "(" , ident , "->" , "{" , configRutina , "}" , ")" ;
configRutina     = ident , ".id" , "(" , ident , ")"
                 , [ ident , ".returns" , "(" , receptor , ")" ]
                 , ident , ".run" , "(" , firmaRun , "->" , "{" , { sentencia } , "}" , ")" ;

firmaRun         = "()"
                 | "(" , paramRutina , { "," , paramRutina } , ")" ;
paramRutina      = ident , "<" , magnitud , ">" ;

sentencia        = etiqueta | accion ;
etiqueta         = "@" , identificador , ":" ;
accion           = receptor , "." , verbo , "(" , [ argumento ] , ")" ;

receptor         = identificador , { "." , identificador }
                 | magnitud , "[" , exprDireccion , "]"
                 | "(" , identificador , ":" , identificador , ")"
                 | "Port" , "[" , ( identificador | entero ) , "]" ;

argumento        = fuente , [ "if" , condicion ]
                 | constructorLlamada ;

constructorLlamada = ident , "->" , ident , ".target" , "(" , ident , ")"
                   , [ ".inputs" , "(" , listaHomogenea , ")" ]
                   , [ ".output" , "(" , receptor , ")" ] ;

fuente           = "defaultValue"
                 | receptor
                 | "[" , identificador , "]"
                 | magnitud
                 | "@" , identificador
                 | identificador
                 | "0" | "1" | "not" ;

magnitud         = unidad , ":" , entero ;
unidad           = "Bit" | "Byte"
                 | "KiloBit" | "MegaBit" | "GigaBit" | "TeraBit"
                 | "KiloByte" | "MegaByte" | "GigaByte" | "TeraByte" ;
```

## 9. Portabilidad y Unificación Multi-Arquitectura

Para mantener el diseño honesto y evitar abstracciones inventadas sin respaldo en hardware real:

1. **Ya unificado desde la Fase 1:** la estructura de proyecto `.fproj` + `sourcesCode.fasm`, el modelo `Bit`/`Byte`, la prohibición de datos duros en `.execute`, `defaultValue`, el patrón `destino.verbo(fuente)`, los contratos `Ipr.flowCall(c -> ...)` y los verbos comunes (§7).
2. **Específico por arquitectura en Fase 1:** el archivo físico de registros (`Gpr0`–`Gpr15` en x64) y sus vistas se toman de la guía de la arquitectura destino ([FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md)).
3. **Consolidación en Fase 3:** una vez que el compilador x64 produzca y ejecute binarios reales y se incorpore una segunda arquitectura real (ARM64 o RISC-V en QEMU), se unificará el mapeo de registros común verificando que programas escritos con el núcleo común generen **exactamente el mismo resultado observable** en todas las arquitecturas soportadas.

## 10. Diagnósticos Comunes (Resumen)

| Rango | Qué valida |
|---|---|
| `FASM1xxx` | Errores léxicos o sintácticos (paréntesis, llaves, flujo *type-state* incompleto) |
| `FASM2xxx` | Nombre de proyecto vs `.fproj`, duplicados, datos duros prohibidos dentro de `.execute(...)` |
| `FASM3xxx` | Mezcla inválida entre `Bit` y `Byte`, anchos o vistas incompatibles, nombres legacy prohibidos |
| `FASM4xxx` | Verbo no válido para el receptor, direccionamiento inválido, lectura de bandera indefinida en salto |
| `FASM5xxx` | Desajuste en contrato de `Ipr.flowCall`, lectura de registro invalidado tras llamada, pila desalineada |
| `FASM9xxx` | Error interno del compilador |
