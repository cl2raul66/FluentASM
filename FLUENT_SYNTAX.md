# Referencia de sintaxis de FluentASM (Común)

> **Documento compartido** — sintaxis común a todas las arquitecturas. Para detalles específicos de x64, véase [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md).

---

## ¿Qué es FluentASM?

FluentASM es un **ensamblador** con una sintaxis fluida.

Un ensamblador es una herramienta que traduce instrucciones escritas por personas a los bytes que el procesador ejecuta directamente. FluentASM es el ensamblador; su **bootstrap** (la primera implementación, que más adelante se sustituirá por el propio lenguaje, ver Fase 11 en la sección "Roadmap" de [CONTRIBUTING.md](CONTRIBUTING.md)).

Un ensamblador traduce instrucciones a **código máquina nativo** (bytes crudos) que la CPU ejecuta directamente, sin máquina virtual intermedia ni *runtime* instalado.

---

## 1. Código fluido (DSL `.fasm`)

FluentASM se escribe en archivos `.fasm` con **código fluido**: instrucciones encadenadas con sintaxis fluida (`.copy(42 to Rax)`). Esta es la **única** forma de escribir programas.

El compilador/ensamblador implementa internamente una **API fluida** que usa para procesar el código fluido, generar bytes, tests, tooling, etc. Esta API es **interna** — no es una forma alternativa de escribir programas.

---

## 2. Estructura de un archivo `.fasm`

```fasm
program MiPrograma              // nombre del programa (opcional)
    section .execute            // declaración de sección
    entry main                  // símbolo de entrada (opcional)

    routine Doblar              // una rutina con un parámetro
        parameter n             // n es el nombre del 1.er parámetro (ABI: Rcx en Win64)
        .copy(n to Rax)         // copia el valor del parámetro en Rax
        .alu(Rax add Rax)       // Rax = Rax + Rax
        .ret()                  // devuelve Rax
    end

    routine main                // punto de entrada
        .copy(21 to Rcx)        // pone el argumento 1 en Rcx (donde Doblar lo espera)
        .call(Doblar)           // llama a Doblar; el resultado queda en Rax
        .ret()                  // devuelve Rax (que ahora vale 42)
    end
end
```

### 2.1 Cómo se lee el ejemplo

| Paso | Instrucción | Qué ocurre |
|---|---|---|
| 1 | `.copy(21 to Rcx)` | `main` escribe `21` en `Rcx`, que es donde `Doblar` leerá su primer argumento. |
| 2 | `.call(Doblar)` | Salto a `Doblar`. El valor `21` ya está en `Rcx`, así que `Doblar` lo recibe como su `parameter n`. |
| 3 | `parameter n` | `n` no es un valor ni una variable: es el **nombre** del registro que la ABI asigna al primer parámetro. En Win64, `n` ≡ `Rcx`. |
| 4 | `.copy(n to Rax)` | Copia `Rcx` (que vale 21) en `Rax`. Ahora `Rax = 21`. |
| 5 | `.alu(Rax add Rax)` | `Rax = 21 + 21 = 42`. |
| 6 | `.ret()` | `Doblar` devuelve `Rax` (42). |
| 7 | `.ret()` | `main` devuelve `Rax` (que contiene 42). `Rcx` sigue valiendo 21, pero ya no importa. |

**Conclusión:** `n` es `Rcx`; el `21` es el contenido de `Rcx` en ese momento. El valor de retorno de `main` viaja en `Rax`, no en `Rcx`.

### 2.2 Reglas de estructura

1. La **indentación es cosmética**: los bloques terminan con `End`, nunca con espacio en blanco.
2. Puede haber **un** `Program` por archivo; es opcional si el archivo solo tiene rutinas.
3. Sin `Section` explícita, todo va a `.execute`.
4. Sin `Entry`, el punto de entrada es el primer símbolo de `.execute`.
5. El orden de las secciones en la imagen es el orden de declaración, con `.execute` primero.
6. `parameter n` declara un parámetro nombrado; la ABI activa decide en qué registro físico llega.

### 2.3 Más casos de uso

#### A) Rutina con dos parámetros y retorno usado por el llamante

```fasm
program SumaDos
    section .execute
    entry main

    routine AddTwoNumbers
        parameter a, b        // a ≡ Rcx, b ≡ Rdx (Win64)
        .copy(a to Rax)       // Rax = a
        .alu(Rax add b)       // Rax = a + b
        .ret()                // devuelve Rax
    end

    routine main
        .copy(10 to Rcx)      // argumento 1 -> Rcx
        .copy(32 to Rdx)      // argumento 2 -> Rdx
        .call(AddTwoNumbers)  // Rax = 42
        .ret()                // main devuelve 42
    end
end
```

#### B) Sin `parameter` (rutina hoja, sin argumentos)

```fasm
program RetornaCuarentaYDos
    section .execute
    entry main

    routine main
        .copy(42 to Rax)      // fija el valor de retorno directamente
        .ret()
    end
end
```

#### C) El mismo código, núcleo portable (funciona en x64 y ARM)

```fasm
program SumaDosPortable
    section .execute
    entry main

    routine AddTwoNumbers
        parameter a, b        // sin registros nativos; el compilador los mapea
        .copy(a to R0q)       // R0q ≡ Rax (x64) o X0 (ARM64)
        .alu(R0q add b)
        .ret()
    end

    routine main
        .copy(10 to R0q)
        .copy(32 to R1q)
        .call(AddTwoNumbers)
        .ret()
    end
end
```

---

## 3. Gramática formal (EBNF) — Común

```ebnf
archivo             = { elemento } ;

elemento            = bloquePrograma | bloqueRutina | declaracionSeccion
                    | directiva | etiqueta | instruccion | constante ;

bloquePrograma      = "Program" identificador finLinea { elemento } "End" ;
bloqueRutina        = "Routine" identificador [ "Naked" ] finLinea
                      { parametro } { elemento } "End" ;
parametro           = "Parameter" identificador { "," identificador } ;

declaracionSeccion  = "Section" nombreSeccion [ "," atributo { "," atributo } ] ;
nombreSeccion       = ".execute" | ".text" | ".data" | ".bss" | identificador ;   (* sin distinguir mayúsculas *)
atributo            = "Align" "=" expresion | "Read" | "Write" | "Execute" | "Uninitialized" ;

directiva           = directivaExtern | directivaGlobal | directivaEntry
                    | directivaDatos | directivaReserva | directivaAlign | directivaInclude ;
directivaExtern     = "Extern" identificador { "," identificador } ;
directivaGlobal     = "Global" identificador { "," identificador } ;
directivaEntry      = "Entry" identificador ;
directivaDatos      = ( "Db" | "Dw" | "Dd" | "Dq" ) dato { "," dato } ;
directivaReserva    = ( "Resb" | "Resw" | "Resd" | "Resq" ) expresion ;
directivaAlign      = "Align" expresion ;
directivaInclude    = "Include" cadena ;
constante           = identificador "Equ" expresion ;

etiqueta            = ( identificador | etiquetaLocal ) ":" ;
etiquetaLocal       = "@" identificador ;

instruccion         = "." mnemonicos [ "(" [ operando { separador operando } ] ")" ] ;

mnemonicos          = "copy" | "alu" | "jump" | "call" | "ret"
                    | "stackWrite" | "stackRead" | "stackPointerGet" | "stackPointerSet"
                    | "nop" | "setCarry" | "clearCarry" | "toggleCarry"
                    | "setDirection" | "clearDirection"
                    | "lea" | "copyZeroExtended" | "copySignedExtended"
                    | mnemonicoAlu ;  (* validado contra el catálogo *)

mnemonicoAlu        = "add" | "sub" | "and" | "or" | "xor" | "imul"
                    | "subNoWrite" | "andNoWrite" ;

separador           = "to" | " " ;   (* 'to' para copy/copy*; espacio para alu/jump *)

clausulaIf          = "if" rFlagsCondicion ;  (* solo para jump *)

rFlagsCondicion     = [ "not" ] banderaSimple
                    | "rFlags.carry" "or" "rFlags.zero"
                    | "not" "rFlags.carry" "and" "not" "rFlags.zero"
                    | "rFlags.sign" ( "equal" | "unequal" ) "rFlags.overflow"
                    | "rFlags.zero" "or" "rFlags.sign" "unequal" "rFlags.overflow"
                    | "not" "rFlags.zero" "and" "rFlags.sign" "equal" "rFlags.overflow" ;

banderaSimple       = "rFlags." ( "carry" | "parity" | "zero" | "sign" | "overflow" ) ;

operando            = registro | memoria | inmediato | referencia ;
memoria             = [ tamano ] "[" expresionMemoria "]" ;
tamano              = "Byte" | "Word" | "DWord" | "QWord" ;
expresionMemoria    = base { "+" termino } ;
base                = registro | ("Rel" referencia) ;
termino             = registro [ "*" escala ] | expresion ;
escala              = "1" | "2" | "4" | "8" ;
referencia          = identificador ;
inmediato           = [ "-" ] ( entero | caracter ) ;

expresion           = terminoExp { ("+" | "-" | "*" | "/") terminoExp } ;
terminoExp          = entero | caracter | identificador | "$" | "(" expresion ")" ;
dato                = entero | caracter | cadena | identificador ;

entero              = decimal | hexadecimal | binario ;
decimal             = digito { digito | "_" } ;
hexadecimal         = "0x" hexdigito { hexdigito | "_" } ;
binario             = "0b" ("0" | "1") { ("0" | "1") | "_" } ;
caracter            = "'" ( caracterSimple | escape ) "'" ;
cadena              = '"' { caracterSimple | escape } '"' ;
comentarioLinea   = "//" { ? carácter salvo fin de línea ? } ;
comentarioBloque  = "/*" { ? cualquier carácter salvo "*/" ? } "*/" ;
comentarioDoc     = "///" { ? carácter salvo fin de línea ? } ;
```

**Notas de la gramática:**

- **`$`** es la posición actual (offset dentro de la sección). Permite escribir `longitud equ $ - mensaje`.
- Los nombres de sección empiezan por `.` (ej: `.execute`, `.text`, `.data`, `.bss`). Los nombres personalizados son válidos y siempre deben empezar por `.`.
- Las instrucciones **exigen** el prefijo `.`, lo que las distingue de etiquetas y directivas sin ambigüedad. Las etiquetas locales usan `@` (no `.`), precisamente para no chocar con ese prefijo.
- **Registros**: la sintaxis exacta depende de la arquitectura (véase documento específico: [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md) para x64).

---

## 4. Léxico — Común

| Elemento | Regla | Ejemplos |
|---|---|---|
| Identificador | Letra o `_`, seguido de letras, dígitos o `_` | `main`, `mensaje`, `_inicio` |
| Símbolo local | `@` + identificador; visible solo en su rutina | `@bucle`, `@fin` |
| Mnemónico | No distingue mayúsculas ni minúsculas | `.Copy`, `.copy`, `.COPY` son el mismo |
| Directiva | No distingue mayúsculas ni minúsculas | `Section`, `section` |
| Símbolo definido por el usuario | **Sí** distingue mayúsculas | `Total` y `total` son distintos |
| Comentario de línea | `//` hasta el final de la línea | `// copiamos a` |
| Comentario de bloque | `/* ... */`; no se anida | `/* esto es un bloque */` |
| Documentación | `///`; texto guardado en `fasm.fdoc`, clave `Parte.Símbolo` | `/// Punto de arranque del sistema` |
| Entero decimal | Dígitos, con `_` opcional como separador | `42`, `1_000_000` |
| Entero hexadecimal | `0x` + dígitos | `0x2A`, `0xDEAD_BEEF` |
| Entero binario | `0b` + dígitos | `0b1010_1010` |
| Carácter | Comillas simples con escapes | `'A'`, `'\n'` |
| Cadena | Comillas dobles con escapes | `"Hola"`, `"a\tb"` |
| Escapes | `\n` `\t` `\r` `\0` `\\` `\"` `\'` `\xNN` | `"\x41"` es `"A"` |

No hay literales de coma flotante (ver "Próximos pasos").

---

## 5. Registros — Nivel 1 (Portable)

Para escribir código que funcione en cualquier arquitectura sin cambios, FluentASM utiliza una numeración canónica independiente de la ISA.

**Sintaxis:** `R` + `Número (0-15)` + `Sufijo de Ancho`.

| Sufijo | Ancho | Significado | Ejemplo |
|---|---|---|---|
| `q` | 64 bits | Quadword | `R0q` |
| `d` | 32 bits | Doubleword | `R0d` |
| `w` | 16 bits | Word | `R0w` |
| `b` | 8 bits | Byte | `R0b` |

**Mapeo:** El compilador traduce estos registros a los nativos de la ISA destino (ej: `R0q` → `Rax` en x64, `X0` en ARM64).

> **IMPORTANTE:** No confundas la numeración canónica con los parámetros. La directiva `parameter` asigna nombres a los registros según la ABI. En x64, el primer parámetro llega en `Rcx` (`R1` canónico); en ARM64, llega en `X0` (`R0` canónico). Usa siempre los nombres de los parámetros para asegurar la portabilidad.

> **Detalles de registros nativos:** Véase el documento específico de cada arquitectura. Para x64: [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md#52-registros-x64-nivel-3-nativo).

---

## 6. Operandos y modos de direccionamiento — Comunes

| Modo | Ejemplo | Significado |
|---|---|---|
| Inmediato | `42` | El valor va dentro de la instrucción |
| Registro | `Rax` | El valor está en el registro |
| Memoria indirecta | `[Rbx]` | El valor está en la dirección que guarda `Rbx` |
| Memoria con desplazamiento | `[Rbx - 8]` | Igual, con desplazamiento |
| Memoria con índice y escala | `[Rbx + Rcx*4 + 16]` | Arreglo: base + índice × tamaño + desplazamiento |
| Relativo a la instrucción | `[Rel mensaje]` | Posición independiente (PIC) |
| Dirección de un símbolo | `mensaje` | Dirección del símbolo (se codifica como `Lea`) |
| Contenido de un símbolo | `[mensaje]` | El dato guardado en el símbolo |
| Tamaño explícito | `Byte [Rbx]`, `QWord [Rbx]` | Obligatorio cuando el tamaño es ambiguo |

**Reglas comunes:**

1. La **escala** solo puede ser 1, 2, 4 u 8. Otro valor es `FASM4002`.
2. Si el tamaño del dato no se deduce del otro operando, hay que declararlo. Escribir `.copy(1 to [Rbx])` es `FASM3005`; se escribe `.copy(1 to Byte [Rbx])`.
3. En Fase 1, el direccionamiento a símbolos es **siempre relativo** (`[Rel símbolo]`). Una dirección absoluta es `FASM5002` con la sugerencia de cómo reescribirla.
4. `.copy(Rax to mensaje)` carga la **dirección** (equivalente a `.lea(Rax to mensaje)`), mientras que `.copy(Rax to [mensaje])` carga el **contenido**.

> **Detalles x64:** RIP-relative, SIB, escalas, restricciones `Rsp`/`R12` — véase [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md#modos-de-direccionamiento-x64).

---

## 7. Directivas — Comunes

| Directiva | Efecto | Fase |
|---|---|---|
| `Program <nombre>` … `End` | Agrupa el programa y le da nombre | 2 |
| `Routine <nombre> [Naked]` … `End` | Declara una rutina; `Naked` evita prólogo y epílogo | 2 |
| `Parameter a, b` | Declara parámetros según la ABI activa | 2 |
| `Section .execute [, Align=16, Execute, Read]` | Declara una sección y sus atributos | 1 |
| `Extern nombre` | Declara un símbolo definido fuera de la imagen | 2 (sin uso hasta 5) |
| `Global nombre` | Exporta un símbolo para que otras rutinas lo usen | 2 |
| `Entry nombre` | Fija el punto de entrada | 1 |
| `Db`/`Dw`/`Dd`/`Dq` | Datos de 1, 2, 4 u 8 bytes | 2 |
| `Resb`/`Resw`/`Resd`/`Resq` | Reserva bytes sin valor inicial (va a `.bss`) | 2 |
| `Align <n>` | Alinea a `n` bytes (potencia de dos) | 2 |
| `nombre Equ <expresión>` | Constante calculada en tiempo de ensamblado | 2 |
| `Include "archivo.fasm"` | Inserta otro archivo de texto | 2 |

---

## 8. Rutinas, parámetros y llamadas — Común

```fasm
routine AddTwoNumbers
    parameter a, b          // Win64: a -> Rcx, b -> Rdx

    .copy(a to Rax)
    .alu(Rax add b)
    .ret()                  // el valor de retorno es Rax
end
```

| Concepto | Regla |
|---|---|
| Parámetros | Se asignan **en orden** según la ABI; el nombre es un alias |
| Prólogo | Generado cuando la rutina tiene parámetros, locales o llamadas |
| Epílogo | Generado y emitido una sola vez: `leave` / `ret` |
| Valor de retorno | Registro de retorno definido por la ABI (x64: `Rax`) |
| `.ret()` | Termina la rutina: ejecuta el epílogo y el `ret` |
| Rutina hoja | Sin parámetros, locales ni llamadas: **sin** prólogo ni epílogo |
| `naked` | Sin prólogo ni epílogo aunque tenga parámetros o locales |
| Llamada | `.call(símbolo)` → relativa; `.call(rax)` → indirecta |

> **ABI concreta:** véase documento específico. x64: [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md#abi-x64-win64).

---

## 9. Documentación de símbolos y `fasm.fdoc` — Común

`///` marca documentación de una declaración (`program` o `routine`). El texto **no se escribe en el fuente**: el IDE lo guarda en `fasm.fdoc` con la clave automática `Parte.Símbolo`.

```fasm
///
/// El punto de entrada del bootloader. Inicializa la pila y
/// salta al núcleo del lenguaje.
///
routine start
    ...
end
```

La clave es `bootloader.start`. Renombrar la rutina mueve la entrada del catálogo automáticamente desde el IDE, así que las traducciones no se rompen.

### 9.1 `fasm.fdoc` — Catálogo de documentación

Archivo binario indexado, almacenado en la raíz del workspace. Cada clave tiene su texto por idioma (BCP 47). Búsqueda O(1) mediante tabla hash mapeada en memoria.

**Estructura v1.0:**

| Campo | Tamaño | Descripción |
|---|---|---|
| Magic `FDOC` | 4 bytes | Identificador |
| `VersionMajor`/`VersionMinor` | 2 + 2 bytes | `1.0` |
| `EntryCount` | 4 bytes | Nº de claves documentadas |
| `LangCount` | 2 bytes | Nº de idiomas definidos |
| Tabla de idiomas | variable | `Tag` BCP 47 + dirección (LTR/RTL) |
| Índice de claves | variable | Tabla hash: `hash(clave) → offset` |
| Datos de clave | variable | Clave UTF-8 NFC + campos estructurados |

**Campos de cada entrada:**

| Tipo | Significado |
|---|---|
| 1 | Resumen (una línea) |
| 2 | Notas (texto largo) |
| 3 | Parámetro (`Name` = nombre del parámetro) |
| 4 | Retorno |
| 5 | Véase también (referencia a otra clave) |

Los datos usan UTF-8 normalizado NFC.

### 9.2 Resolución de idioma

1. `.fworkspace` → `DefaultLanguage`
2. `.fproj` → `DocumentationLanguages`
3. Idioma del sistema operativo
4. Cadena de respaldo: `fr-CA` → `fr` → idioma por defecto → primero disponible

### 9.3 `.bin` v1.1 — Tabla de metadatos de la Parte

| Tag | Nombre | Contenido |
|---|---|---|
| `0x0001` | `PartName` | Nombre estable de la Parte (`bootloader`) |
| `0x0002` | `Description` | Descripción breve de la Parte |
| `0x0003` | `DocSymbols` | Símbolos documentados de la Parte |

Solo se guardan los nombres de símbolos, no el texto. Al compilar, el texto de documentación se valida y **se descarta**: nunca entra en la imagen ejecutable.

---

## 13. Formatos de salida — Comunes

| Fase | Salida | Extensión | Se ejecuta con |
|---|---|---|---|
| **1** | Imagen plana x64 | `.x64` | Arrancador FluentASM, en memoria |
| 6 | Imagen plana x86 / ARM | `.x86`, `.arm` | Su arrancador correspondiente |
| 5 | Ejecutable Windows | `.exe` (PE) | Windows |
| 5 | Ejecutable Linux | (sin extensión, ELF) | Linux |
| 7 | Imagen de arranque | `.img` | Firmware, hardware o emulador |

> Una imagen plana **no** se puede ejecutar con doble clic ni desde una terminal. El cargador del SO necesita un formato (PE o ELF) con cabeceras y tabla de importaciones.

---

## 16. Cambios respecto a la versión anterior — Comunes

| Antes | Ahora | Motivo |
|---|---|---|
| Ejemplos x64 con `Eax` | `Rax` para 64 bits, `Eax` solo para 32 | `Eax` = exactamente 32 bits, pone a cero la mitad alta |
| "El `.x64` se ejecuta como nativo" | Imagen plana ejecutada por arrancador/hardware; `.exe` en Fase 7 | Binario crudo no es formato que el SO sepa cargar |
| Instrucciones mínimas | 30+ mnemónicos con formas, tablas, directivas | Conjunto anterior no bastaba para "hola mundo" |
| `program` + `parameter` mezclados | `program` y `routine` son bloques distintos | Un programa no es una función |
| `parameter a, b` sin destino | Parámetros asignados por ABI, con prólogo/epílogo | Cada implementación inventaba su convención |
| Etiquetas locales con `.` | Etiquetas locales con `@` | `.` reservado para instrucciones |
| Sin gramática formal | EBNF en sección 3 | Permite implementar analizador sin interpretar |
| Dos superficies sin jerarquía | DSL `.fasm` canónica (única) | Evita mantener dos lenguajes |
| Sin definición de `parameter` | `parameter` = alias del registro de la ABI | El lector no sabía qué era `n` |
| `Mov` / `Movzx` / `Movsx` | `copy` / `copyZeroExtended` / `copySignedExtended` | `Mov` implica movimiento; físicamente es copia |
| `Add`/`Sub`/... | `.alu(origen operacion destino)` | Grupo ALU unificado; `subNoWrite`/`andNoWrite` para solo banderas |
| `Push` / `Pop` | `stackWrite` / `stackRead` | Nombres físicos: escriben/leen pila |
| `Jmp`/`Je`/... | `jump` / `jump(... if rFlags...)` | Gramática cerrada 16 formas; `rFlags` expone banderas |
| `Call` / `Ret` | `call` / `ret` | Minúscula; `ret` sin operando (retorno en `Rax` por ABI) |
| `Nop`/`Stc`/... | `nop`/`setCarry`/... | Nombres descriptivos físicos |
| `Lea` | `lea` | Mantenido; calcula dirección efectiva |
| `Movzx`/`Movsx` | `copyZeroExtended`/`copySignedExtended` | Explícito sobre extensión |
| `.text` (canónica) | `.execute` (canónica) | `.execute` = código que se ejecuta; `.text` = alias legacy |

---

## 17. Próximos pasos — Comunes

1. **Sistema de macros** — `%include`, `%define`, `CHILD`, `SENDI`, `PROLOGUE` para no repetir bloques.
2. **Condiciones compuestas** (`If`/`While`) — azúcar sobre `.alu` + `.jump`, sin esconder bytes.
3. **Capa portable (Nivel 1)** — Sintaxis unificada `.jump(@fin if a less b)` que el backend reduce a cada arquitectura.
4. **Diagnóstico de `rFlags`** — Rastreo de banderas definidas/indefinidas para emitir `FASM4012` en saltos.

> **Próximos pasos específicos por arquitectura:** véase [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md#próximos-pasos-x64) para x64.