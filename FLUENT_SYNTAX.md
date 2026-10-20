# Referencia de sintaxis de FluentASM

## ¿Qué es FluentASM?

FluentASM es un **ensamblador** con una sintaxis fluida.

Un ensamblador es una herramienta que traduce instrucciones escritas por personas a los bytes que el
procesador ejecuta directamente. FluentASM es el ensamblador; el programa en C# que lo implementa es
su **bootstrap** (la primera implementación, que más adelante se sustituirá por el propio lenguaje,
ver Fase 11 en la sección "Roadmap" de [CONTRIBUTING.md](CONTRIBUTING.md)).

Si vienes de C#:

- En C# escribes código, el compilador lo traduce a **IL** y el *runtime* de .NET lo ejecuta.
- En FluentASM escribes instrucciones, el ensamblador las traduce a **código máquina nativo** (bytes
  crudos) y la CPU los ejecuta sin ninguna máquina virtual intermedia.

## Analogías con C#

| Concepto de ensamblador | Equivalente aproximado en C# |
|---|---|
| Instrucción | Una llamada a método |
| Registro | Una variable local muy rápida dentro del procesador |
| Memoria | Un arreglo o un objeto en el *heap* |
| Etiqueta (*label*) | El nombre de un método o un `goto` |
| Salto (*jump*) | Un `goto`, `break`, `continue` o llamada a método |
| Ensamblado | Compilar |
| Código máquina | Los bytes que la CPU entiende (análogo al IL, pero sin máquina virtual) |
| Rutina | Un método con parámetros y valor de retorno |
| ABI | El contrato que dice en qué registro llega cada parámetro |

---

## 1. Las dos superficies

| Superficie | Aspecto | Cuándo se usa | Estado |
|---|---|---|---|
| **DSL `.fasm`** (canónica) | Archivos de texto con instrucciones encadenadas | Escribir programas | Fase 2 |
| **API fluent de C#** | Objetos y métodos que devuelven el binario | Programas, pruebas y herramientas | Fase 1 |

La superficie canónica es el DSL: define el lenguaje. La API de C# es la misma cosa escrita en C# y
**debe producir exactamente los mismos bytes** (ver "Decisiones de diseño" en [CONTRIBUTING.md](CONTRIBUTING.md), superficie canónica).
Por eso ambas usan los mismos mnemónicos y el mismo orden de operandos:

```fasm
.Mov(Rax, 42)          // DSL .fasm
```
```csharp
.Mov(Rax, 42)          // API de C#
```

La diferencia es el envoltorio: el DSL usa un bloque `Program … End` con instrucciones precedidas de
punto, y la API encadena métodos hasta llamar a `.Build()`.

---

## 2. Estructura de un archivo `.fasm`

```fasm
program MiPrograma              // nombre del programa (opcional)
    section .text               // declaración de sección
    entry main                  // símbolo de entrada (opcional)

    routine Doblar              // una rutina con parámetro
        parameter n
        .Mov(Rax, n)
        .Add(Rax, Rax)
        .Ret()
    end

    routine main                // punto de entrada
        .Mov(Rcx, 21)
        .Call(Doblar)           // devuelve 42 en RAX
        .Ret()
    end
end
```

Reglas de estructura:

1. La **indentación es cosmética**: los bloques terminan con `End`, nunca con espacio en blanco.
2. Puede haber **un** `Program` por archivo; es opcional si el archivo solo tiene rutinas.
3. Sin `Section` explícita, todo va a `.text`.
4. Sin `Entry`, el punto de entrada es el primer símbolo de `.text`.
5. El orden de las secciones en la imagen es el orden de declaración, con `.text` primero.

---

## 3. Gramática formal (EBNF)

```ebnf
archivo             = { elemento } ;

elemento            = bloquePrograma | bloqueRutina | declaracionSeccion
                    | directiva | etiqueta | instruccion | constante ;

bloquePrograma      = "Program" identificador finLinea { elemento } "End" ;
bloqueRutina        = "Routine" identificador [ "Naked" ] finLinea
                      { parametro } { elemento } "End" ;
parametro           = "Parameter" identificador { "," identificador } ;

declaracionSeccion  = "Section" nombreSeccion [ "," atributo { "," atributo } ] ;
nombreSeccion       = ".text" | ".data" | ".bss" | identificador ;   (* sin distinguir mayúsculas *)
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

instruccion         = "." mnemonicos [ "(" [ operando { "," operando } ] ")" ] ;
mnemonicos          = identificador ;                       (* validado contra el catálogo *)
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

Notas de la gramática:

- **`$`** es la posición actual (offset dentro de la sección). Permite escribir
  `longitud equ $ - mensaje`.
- **`.Text`, `.Data`, `.Bss`**: nombres de sección entre comillas no; los `Section` de la versión
  anterior se mantienen. Los nombres personalizados son válidos y siempre deben empezar por `.`.
- Las instrucciones **exigen** el prefijo `.`, lo que las distingue de etiquetas y directivas sin
  ambigüedad. Las etiquetas locales usan `@` (no `.`), precisamente para no chocar con ese prefijo.

---

## 4. Léxico

| Elemento | Regla | Ejemplos |
|---|---|---|
| Identificador | Letra o `_`, seguido de letras, dígitos o `_` | `main`, `mensaje`, `_inicio` |
| Símbolo local | `@` + identificador; visible solo en su rutina | `@bucle`, `@fin` |
| Mnemónico | No distingue mayúsculas ni minúsculas | `.Mov`, `.mov`, `.MOV` son el mismo |
| Registro | No distingue mayúsculas ni minúsculas | `Rax`, `rax`, `RAX` |
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

## 6. Comentarios y documentación

FluentASM distingue tres formas de comentario en el código fuente:

| Tipo | Sintaxis | ¿Se traduce? | ¿Va al binario? |
|---|---|---|---|
| Línea | `// texto` | No | No |
| Bloque | `/* texto */` (no se anida) | No | No |
| Documentación | `///` (texto en `fasm.fdoc`) | Sí | Solo la clave |

### 6.1 Comentario de línea `//`

Ignorado por el ensamblador. Todo lo que sigue a `//` hasta el final de la línea es comentario:

```fasm
.Mov(Rax, n)   // n es el parámetro de entrada
```

### 6.2 Comentario de bloque `/* ... */`

Ignorado por el ensamblador. Puede ocupar varias líneas. **No se anida**: el primer `*/` cierra el bloque.

```fasm
/* Inicializa el registro
   con el valor recibido */
.Mov(Rax, n)
```

### 6.3 Documentación `///`

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

### 6.4 `fasm.fdoc` — Catálogo de documentación

Es un archivo binario indexado, almacenado en la raíz del workspace. Cada clave tiene su texto por idioma (BCP 47: `es`, `en`, `fr-CA`, …). La búsqueda es O(1) mediante tabla hash mapeada en memoria: el lector solo carga la entrada pedida, nunca el archivo entero.

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

Los datos usan UTF-8 normalizado NFC para que `á` precompuesta y `á` combinada no dupliquen entradas.

### 6.5 Resolución de idioma

1. `.fworkspace` → `DefaultLanguage`
2. `.fproj` → `DocumentationLanguages`
3. Idioma del sistema operativo
4. Cadena de respaldo: `fr-CA` → `fr` → idioma por defecto → primero disponible

### 6.6 `.bin` v1.1 — Tabla de metadatos de la Parte

Para que la clave automática `Parte.Símbolo` sea estable, el `.bin` versión 1.1 lleva una **tabla de metadatos** aparte de las representaciones:

| Tag | Nombre | Contenido |
|---|---|---|
| `0x0001` | `PartName` | Nombre estable de la Parte (`bootloader`) |
| `0x0002` | `Description` | Descripción breve de la Parte |
| `0x0003` | `DocSymbols` | Símbolos documentados de la Parte |

Solo se guardan los nombres de símbolos, no el texto. La cabecera del `.bin` v1.1 añade `MetaCount` y `MetaOffset`. Al compilar, el texto de documentación se valida y **se descarta**: nunca entra en la imagen ejecutable. Una traducción nueva solo edita `fasm.fdoc`; no obliga a recompilar.

### 6.7 Diagnósticos de documentación

| Código | Situación |
|---|---|
| `FASM3006` | `///` sin `program` o `routine` inmediatamente después |
| `FASM3007` | `@param` que no coincide con ningún `parameter` de la rutina |
| `FASM3008` | Clave `Parte.Símbolo` sin entrada en `fasm.fdoc` |
| `FASM3009` | Entrada de documentación sin el idioma por defecto del proyecto |

---

## 5. Registros

Los registros son ranuras dentro del procesador: rapidísimos y muy pocos.

### 5.1 Registros Portables (Nivel 1)

Para escribir código que funcione en cualquier arquitectura sin cambios, FluentASM utiliza una numeración canónica independiente de la ISA. 

**Sintaxis:** `R` + `Número (0-15)` + `Sufijo de Ancho`.

| Sufijo | Ancho | Significado | Ejemplo |
|---|---|---|---|
| `q` | 64 bits | Quadword | `R0q` |
| `d` | 32 bits | Doubleword | `R0d` |
| `w` | 16 bits | Word | `R0w` |
| `b` | 8 bits | Byte | `R0b` |

**Mapeo:** El compilador traduce estos registros a los nativos de la ISA destino (ej: `R0q` $\to$ `Rax` en x64, `X0` en ARM64).

> **IMPORTANTE:** No confundas la numeración canónica con los parámetros. La directiva `parameter` asigna nombres a los registros según la ABI. En x64, el primer parámetro llega en `Rcx` (`R1` canónico), mientras que en ARM64 llega en `X0` (`R0` canónico). Usa siempre los nombres de los parámetros para asegurar la portabilidad.

### 5.2 Uso general x64 (Nivel 3: Nativo)

Cuando necesitas control total o instrucciones específicas de x64, usas los registros nativos. Se nombran con `PascalCase` y el mismo nombre en los cuatro tamaños.

| 64 bits | 32 bits | 16 bits | 8 bits | N.º | Rol en la ABI Win64 |
|---|---|---|---|---|---|
| `Rax` | `Eax` | `Ax` | `Al` | 0 | Valor de retorno; volátil |
| `Rcx` | `Ecx` | `Cx` | `Cl` | 1 | Parámetro 1; volátil |
| `Rdx` | `Edx` | `Dx` | `Dl` | 2 | Parámetro 2; volátil |
| `Rbx` | `Ebx` | `Bx` | `Bl` | 3 | Se debe preservar |
| `Rsp` | `Esp` | `Sp` | `Spl` | 4 | Puntero de pila; no usar como índice |
| `Rbp` | `Ebp` | `Bp` | `Bpl` | 5 | Marco de pila; se debe preservar |
| `Rsi` | `Esi` | `Si` | `Sil` | 6 | Volátil |
| `Rdi` | `Edi` | `Di` | `Dil` | 7 | Volátil |
| `R8` | `R8d` | `R8w` | `R8b` | 8 | Parámetro 3; volátil |
| `R9` | `R9d` | `R9w` | `R9b` | 9 | Parámetro 4; volátil |
| `R10` | `R10d` | `R10w` | `R10b` | 10 | Volátil |
| `R11` | `R11d` | `R11w` | `R11b` | 11 | Volátil |
| `R12` | `R12d` | `R12w` | `R12b` | 12 | Se debe preservar |
| `R13` | `R13d` | `R13w` | `R13b` | 13 | Se debe preservar |
| `R14` | `R14d` | `R14w` | `R14b` | 14 | Se debe preservar |
| `R15` | `R15d` | `R15w` | `R15b` | 15 | Se debe preservar |

Bytes altos (solo con los registros originales): `Ah`, `Ch`, `Dh`, `Bh`. **Prohibido** combinarlos
con `R8`–`R15` o con `Spl`/`Bpl`/`Sil`/`Dil`: comparten codificación y el prefijo REX los invalida.
FluentASM lo detecta y emite `FASM3002`.

Otros registros: `Rip` (solo como base de un operando `[Rip + desplazamiento]`) y `Rflags`
(Fase 2).

> **Nota para quien viene de la documentación anterior:** los ejemplos de 64 bits usaban `Eax`, que
> es el registro de **32 bits**. En x64, un valor de 64 bits se maneja con `Rax`. `Eax` sigue siendo
> válido, pero significa exactamente "los 32 bits bajos" y **pone a cero la mitad alta**.

### 5.2 Correspondencia con la ABI

Cuando una rutina declara `parameter`, los nombres de los parámetros son **alias** de los registros
que dicta la ABI: no se eligen a mano. Ver "Modelo de ejecución y ABI" en [CONTRIBUTING.md](CONTRIBUTING.md).

---

## 6. Operandos y modos de direccionamiento

| Modo | Ejemplo | Significado |
|---|---|---|
| Inmediato | `42` | El valor va dentro de la instrucción |
| Registro | `Rax` | El valor está en el registro |
| Memoria indirecta | `[Rbx]` | El valor está en la dirección que guarda `Rbx` |
| Memoria con desplazamiento | `[Rbx - 8]` | Igual, con desplazamiento |
| Memoria con índice y escala | `[Rbx + Rcx*4 + 16]` | Arreglo: base + índice × tamaño + desplazamiento |
| Relativo a la instrucción | `[Rip + mensaje]` o `[Rel mensaje]` | Posición independiente (PIC) |
| Dirección de un símbolo | `mensaje` | Dirección del símbolo (se codifica como `Lea`) |
| Contenido de un símbolo | `[mensaje]` | El dato guardado en el símbolo |
| Tamaño explícito | `Byte [Rbx]`, `QWord [Rbx]` | Obligatorio cuando el tamaño es ambiguo |

Reglas:

1. La **escala** solo puede ser 1, 2, 4 u 8. Otro valor es `FASM4002`.
2. `Rsp` y `R12` no pueden ser índice (`FASM4003`).
3. Si el tamaño del dato no se deduce del otro operando, hay que declararlo. Escribir
   `.Mov([Rbx], 1)` es `FASM3005`; se escribe `.Mov(Byte [Rbx], 1)`.
4. En Fase 1, el direccionamiento a símbolos es **siempre relativo** (`[Rel símbolo]`). Una dirección
   absoluta es `FASM5002` con la sugerencia de cómo reescribirla.
5. `.Mov(Rax, símbolo)` carga la **dirección** (equivalente a `.Lea(Rax, símbolo)`), mientras que
   `.Mov(Rax, [símbolo])` carga el **contenido**. Es la misma distinción que en un ensamblador
   clásico entre `offset` y `[ ]`.

---

## 7. Directivas

| Directiva | Efecto | Fase |
|---|---|---|
| `Program <nombre>` … `End` | Agrupa el programa y le da nombre | 2 |
| `Routine <nombre> [Naked]` … `End` | Declara una rutina; `Naked` evita prólogo y epílogo | 2 |
| `Parameter a, b` | Declara parámetros según la ABI activa | 2 |
| `Section .text [, Align=16, Execute, Read]` | Declara una sección y sus atributos | 1 |
| `Extern nombre` | Declara un símbolo definido fuera de la imagen | 2 (sin uso hasta 5) |
| `Global nombre` | Exporta un símbolo para que otras rutinas lo usen | 2 |
| `Entry nombre` | Fija el punto de entrada | 1 |
| `Db`/`Dw`/`Dd`/`Dq` | Datos de 1, 2, 4 u 8 bytes | 2 |
| `Resb`/`Resw`/`Resd`/`Resq` | Reserva bytes sin valor inicial (va a `.bss`) | 2 |
| `Align <n>` | Alinea a `n` bytes (potencia de dos) | 2 |
| `nombre Equ <expresión>` | Constante calculada en tiempo de ensamblado | 2 |
| `Include "archivo.fasm"` | Inserta otro archivo de texto | 2 |

Ejemplo de datos:

```fasm
section .data
    mensaje  Db "Hola mundo", 0
    longitud Equ $ - mensaje          // 10
    tabla    Dq 1, 2, 3, 4
```

---

## 8. Rutinas, parámetros y llamadas

```fasm
routine AddTwoNumbers
    parameter a, b          // Win64: a -> Rcx, b -> Rdx

    .Mov(Rax, a)
    .Add(Rax, b)
    .Ret()                  // el valor de retorno es Rax
end
```

| Concepto | Regla |
|---|---|
| Parámetros | Se asignan **en orden** según la ABI; el nombre es un alias |
| Máximo en Fase 1 | 4 (en Win64 llegan en `Rcx`, `Rdx`, `R8`, `R9`). El quinto es `FASM3001` |
| Prólogo | Generado cuando la rutina tiene parámetros, locales o llamadas: `push rbp` / `mov rbp, rsp` / `sub rsp, N` (N múltiplo de 16) |
| Epílogo | Generado y emitido una sola vez: `leave` / `ret` |
| Valor de retorno | `Rax` |
| `.Ret()` | Termina la rutina: ejecuta el epílogo y el `ret` |
| Rutina hoja | Sin parámetros, locales ni llamadas: **sin** prólogo ni epílogo (por eso el ejemplo mínimo son 6 bytes exactos) |
| `naked` | Sin prólogo ni epílogo aunque tenga parámetros o locales; el autor se responsabiliza del marco |
| Llamada | `.Call(símbolo)` → `e8` + desplazamiento relativo; `.Call(rax)` → `ff d0` |

Detalle completo, incluidas las pilas y la alineación, en "Modelo de ejecución y ABI" en [CONTRIBUTING.md](CONTRIBUTING.md).

---

## 9. Catálogo de instrucciones de la Fase 1

La columna **Bytes** está **verificada con NASM** para el ejemplo indicado (misma técnica que se
usará como oráculo en las pruebas). `rel` significa que el desplazamiento depende del layout.

### 9.1 Movimiento de datos

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `Mov` (r32, imm) | `.Mov(Eax, 10)` | 1 | `b8 0a 00 00 00` | Forma más corta para un inmediato sin signo |
| `Mov` (r64, imm pequeño) | `.Mov(Rax, 42)` | 1 | `b8 2a 00 00 00` | **La forma más corta es `mov r32, imm32`**, que pone a cero la mitad alta |
| `Mov` (r64, imm negativo) | `.Mov(Rax, -1)` | 1 | `48 c7 c0 ff ff ff ff` | Signo extendido a 64 bits |
| `Mov` (r64, imm grande) | `.Mov(Rax, 0x123456789)` | 1 | `48 b8 89 67 45 23 01 00 00 00` | `imm64` solo cuando no cabe en 32 bits |
| `Mov` (r64, r64) | `.Mov(Rax, Rbx)` | 1 | `48 89 d8` | |
| `Mov` (r8 alto, r8) | `.Mov(R8, R9)` | 3 | `4d 89 c8` | Prefijo REX necesario |
| `Mov` (r64, [mem]) | `.Mov(Rax, [Rbx])` | 1 | `48 8b 03` | |
| `Mov` ([mem], r64) | `.Mov([Rbx], Rax)` | 1 | `48 89 03` | |
| `Mov` (r64, [mem-disp]) | `.Mov(Rax, [Rbx - 8])` | 1 | `48 8b 43 f8` | `disp8` con signo |
| `Mov` (r64, [base+idx*esc]) | `.Mov(Rax, [Rbx + Rcx*4 + 16])` | 1 | `48 8b 44 8b 10` | SIB con escala |
| `Mov` (byte [mem], imm) | `.Mov(Byte [Rbx], 1)` | 1 | `c6 03 01` | Tamaño explícito |
| `Movzx` | `.Movzx(Eax, R8w)` | 1 | `41 0f b7 c0` | Extiende sin signo |
| `Lea` | `.Lea(Rcx, [Rel mensaje])` | 1 | `48 8d 0d disp32` | Base de PIC |
| `Lea` (de símbolo) | `.Lea(Rcx, mensaje)` | 1 | `48 8d 0d disp32` | Azúcar: `.Mov(Rcx, mensaje)` |

### 9.2 Aritmética y lógica

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `Add` (r64, imm8) | `.Add(Rax, 5)` | 1 | `48 83 c0 05` | imm8 con signo |
| `Add` (r64, imm32) | `.Add(Rax, 1000)` | 1 | `48 05 e8 03 00 00` | Forma con acumulador |
| `Add` (r32, imm8) | `.Add(Eax, 20)` | 1 | `83 c0 14` | |
| `Add` (r64, r64) | `.Add(Rax, Rbx)` | 1 | `48 01 d8` | |
| `Sub` (r64, imm8) | `.Sub(Rsp, 0x68)` | 1 | `48 83 ec 68` | Ajuste de pila |
| `Sub` (r32, imm8) | `.Sub(Eax, 5)` | 1 | `83 e8 05` | |
| `And` | `.And(Eax, 0xFF)` | 1 | `25 ff 00 00 00` | |
| `Or` | `.Or(Eax, Eax)` | 1 | `09 c0` | |
| `Xor` | `.Xor(Eax, Eax)` | 1 | `31 c0` | Poner a cero de forma idiomática |
| `Xor` (r8 alto) | `.Xor(R8d, R8d)` | 3 | `45 31 c0` | |
| `Cmp` | `.Cmp(Eax, 101)` | 1 | `83 f8 65` | Prepara banderas |
| `Test` | `.Test(Eax, Eax)` | 1 | `85 c0` | Comparar con cero |
| `Inc` | `.Inc(DWord [Rbx])` | 1 | `ff 03` | |
| `Dec` | `.Dec(Ecx)` | 1 | `ff c9` | |
| `Imul` (r32, r32) | `.Imul(Eax, Ebx)` | 1 | `0f af c3` | Dos operandos |

### 9.3 Pila

| Instrucción | Ejemplo | Nivel | Bytes |
|---|---|---|---|
| `Push` | `.Push(Rbp)` | 1 | `55` |
| `Push` (r8 alto) | `.Push(R8)` | 3 | `41 50` |
| `Push` (acumulador) | `.Push(Rax)` | 1 | `50` |
| `Pop` | `.Pop(Rbp)` | 1 | `5d` |

### 9.4 Control de flujo

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `Jmp` (corto) | `.Jmp(@fin)` | 1 | `eb rel8` | Forma preferida |
| `Jmp` (cercano) | — | 1 | `e9 rel32` | Automático cuando no cabe `rel8` |
| `Je`/`Jz` | `.Je(@fin)` | 1 | `74 rel8` | |
| `Jne`/`Jnz` | `.Jne(@fin)` | 1 | `75 rel8` | |
| `Ja`/`Jnbe` | `.Ja(@fin)` | 1 | `77 rel8` | Sin signo, mayor |
| `Jae`/`Jnb` | `.Jae(@fin)` | 1 | `73 rel8` | Sin signo, mayor o igual |
| `Jb`/`Jc` | `.Jb(@fin)` | 1 | `72 rel8` | Sin signo, menor |
| `Jbe`/`Jna` | `.Jbe(@fin)` | 1 | `76 rel8` | Sin signo, menor o igual |
| `Jg`/`Jnle` | `.Jg(@fin)` | 1 | `7f rel8` | Con signo, mayor |
| `Jge`/`Jnl` | `.Jge(@fin)` | 1 | `7d rel8` | Con signo, mayor o igual |
| `Jl`/`Jnge` | `.Jl(@fin)` | 1 | `7c rel8` | Con signo, menor |
| `Jle`/`Jng` | `.Jle(@fin)` | 1 | `7e rel8` | Con signo, menor o igual |
| `Js` / `Jns` | `.Js(@fin)` | 1 | `78` / `79` | Bandera de signo |
| `Jo` / `Jno` | `.Jo(@fin)` | 1 | `70` / `71` | Desbordamiento |
| `Jp` / `Jnp` | `.Jp(@fin)` | 1 | `7a` / `7b` | Paridad |
| Saltos condicionales, forma cercana | — | 1 | `0f 8x rel32` | Automático cuando no cabe `rel8` |
| `Call` (relativo) | `.Call(Rutina)` | 1 | `e8 rel32` | |
| `Call` (indirecto) | `.Call(Rax)` | 1 | `ff d0` | |
| `Ret` | — | 1 | `c3` | Dentro de una rutina se escribe `.Ret()` |

### 9.5 Sistema y relleno

| Instrucción | Bytes | Notas |
|---|---|---|
| `Nop` | `90` | |
| `Leave` | `c9` | Generado en el epílogo |
| `Int` | `cd imm8` (`.Int(0x80)` → `cd 80`) | Codificable; su efecto depende del sistema |
| `Int3` | `cc` | Punto de parada del depurador |
| `Syscall` | `0f 05` | Codificable, pero **fuera** del alcance útil de la Fase 1 |

Fuera del catálogo de Fase 1: `Div`, `IdiV`, multiplicación de un operando, `Movsx`, cadenas,
`Cmpxchg`, y toda la familia de coma flotante y SIMD. Ver "Próximos pasos".

---

## 10. Reglas de codificación

Son **obligatorias** porque hacen que los bytes sean predecibles y comparables con NASM:

1. **Forma más corta y determinista.** Para los mismos operandos, siempre la misma secuencia de
   bytes, la más corta posible. No hay opciones de usuario.
2. **Inmediatos con signo.** Si el inmediato cabe en 8 bits con signo y existe esa forma, se usa:
   `.Add(Rax, 5)` son 4 bytes, `.Add(Rax, 1000)` son 6.
3. **`Mov` con inmediato en 64 bits**, en este orden: si el valor cabe en 32 bits sin signo →
   `mov r32, imm32` (`b8`); si cabe con signo en 32 bits → `mov r/m64, imm32` (`48 c7 /0`); si no →
   `mov r64, imm64` (`48 b8`).
4. **Desplazamientos**: `disp8` cuando cabe con signo, `disp32` en caso contrario.
5. **Direccionamiento relativo a la instrucción** por defecto para todo símbolo (PIC). Nunca
   absoluto en Fase 1.
6. **Prefijo REX** cuando el tamaño es 64 bits o el registro es `R8`–`R15`, más los casos especiales
   `Spl`/`Bpl`/`Sil`/`Dil`.
7. **Saltos**: forma corta (`rel8`) y promoción automática a cercana (`rel32`) cuando el destino no
   cabe, con el algoritmo iterativo descrito en "Modelo intermedio (IR)" en [CONTRIBUTING.md](CONTRIBUTING.md), sección Layout.
8. **Tamaño de la instrucción** se conoce antes de emitir y queda registrado para poder explicar
   cualquier diferencia con otro ensamblador.

---

## 11. Semántica de ejecución

### 11.1 Salida de la Fase 1: imagen plana

| Aspecto | Regla |
|---|---|
| Formato | Bytes puros, **sin cabecera** |
| Extensión | `.x64` |
| Punto de entrada | El símbolo de `Entry`, o el primero de `.text` |
| Argumentos | Ninguno |
| Resultado | Entero en `Rax` (lo que el código haya dejado) |
| Quién carga la imagen | El arrancador de FluentASM: reserva memoria ejecutable, copia los bytes y salta |
| Independencia de posición | **Obligatoria**: la misma imagen debe funcionar en cualquier dirección |
| Llamadas externas | **Prohibidas** (`FASM5001`) |

### 11.2 Qué **no** se puede hacer en la Fase 1, y cuándo llega

| Lo que quieres hacer | ¿Fase 1? | Fase |
|---|---|---|
| Devolver un número y comprobarlo | Sí | 1 |
| Llamar a una rutina propia | Sí | 1 |
| Escribir en memoria compartida con el arrancador | Sí | 1 |
| `printf`, `ExitProcess`, API de Windows | **No** | 5 |
| Abrir un archivo o una ventana | **No** | 5 |
| Arrancar sobre el hardware sin sistema operativo | **No** | 7 |
| Usar direcciones absolutas fijas | **No** | 7 (bare metal) |

Esta limitación es deliberada y está registrada en "Decisiones de diseño" en [CONTRIBUTING.md](CONTRIBUTING.md) (imagen plana primero).

---

## 12. Formatos de salida

| Fase | Salida | Extensión | Se ejecuta con |
|---|---|---|---|
| **1** | Imagen plana x64 | `.x64` | El arrancador de FluentASM, en memoria |
| 6 | Imagen plana x86 / ARM | `.x86`, `.arm` | Su arrancador correspondiente |
| 5 | Ejecutable Windows | `.exe` (PE) | Windows |
| 5 | Ejecutable Linux | (sin extensión, ELF) | Linux |
| 7 | Imagen de arranque | `.img` | Firmware, hardware o emulador |

> **Corrección respecto a la versión anterior de este documento:** una imagen plana **no** se puede
> ejecutar con doble clic ni desde una terminal. El cargador del sistema operativo necesita un
> formato (PE o ELF) con cabeceras y tabla de importaciones; hasta que exista ese emisor, la imagen
> plana se ejecuta desde el arrancador de FluentASM o directamente sobre el hardware. El espíritu de
> la visión —binario nativo, sin máquina virtual, sin runtime instalado— no cambia.

---

## 13. Ejemplos verificados (Fase 1)

### 13.1 Ejemplo Nativo (Nivel 3)
Este ejemplo utiliza registros concretos de x64. No es portable.

```fasm
program AddTwoNumbers
    section .text
    entry main

    routine AddTwoNumbers
        parameter a, b
        .Mov(Rax, a)
        .Add(Rax, b)
        .Ret()
    end

    routine main
        .Mov(Rcx, 10)
        .Mov(Rdx, 32)
        .Call(AddTwoNumbers)
        .Ret()
    end
end
```

### 13.2 Ejemplo Portable (Nivel 1)
Este ejemplo es idéntico en resultado, pero utiliza el núcleo portable. Funcionará en x64, ARM y x86.

```fasm
program AddTwoNumbersPortable
    section .text
    entry main

    routine AddTwoNumbers
        parameter a, b
        .Mov(R0q, a)         // R0q mapea a Rax (x64) o X0 (ARM)
        .Add(R0q, b)
        .Ret()
    end

    routine main
        .Mov(R0q, 10)
        .Mov(R1q, 32)
        .Call(AddTwoNumbers)
        .Ret()
    end
end
```

**Bytes generados para la versión Nativa (x64):**

| Parte | Instrucciones | Bytes |
|---|---|---|
| Prólogo de `AddTwoNumbers` | `push rbp` / `mov rbp, rsp` / `sub rsp, 32` | `55 48 89 e5 48 83 ec 20` |
| Cuerpo | `mov rax, rcx` / `add rax, rdx` | `48 89 c8 48 01 d0` |
| Epílogo | `leave` / `ret` | `c9 c3` |

Criterio de aceptación: cargado en memoria y llamado como `long AddTwoNumbers(long, long)` devuelve **42**, y el mismo binario funciona cargado en dos direcciones distintas.

---

## 14. Diagnósticos

| Código | Situación | Mensaje esperado |
|---|---|---|
| `FASM2001` | Falta un paréntesis | "Falta el ')' de la instrucción `.Mov(`" |
| `FASM3001` | Quinto parámetro | "Win64 admite 4 parámetros en registro; el paso por pila llega en la Fase 7" |
| `FASM3002` | `Ah` con `R8` | "`Ah` no puede combinarse con registros que exigen prefijo REX" |
| `FASM3003` | Símbolo duplicado | "`main` ya está definido en la línea 12" |
| `FASM3004` | Símbolo desconocido | "No existe `mensaje1`. ¿Querías decir `mensaje`?" |
| `FASM3005` | Tamaño ambiguo | "No se puede deducir el tamaño. Escribe `Byte [Rbx]`" |
| `FASM3006` | Documentación sin declaración | "`///` sin `program` o `routine` inmediatamente después" |
| `FASM3007` | `@param` sin declararlo | "`@param x` pero la rutina no declara `parameter x`" |
| `FASM3008` | Clave de documentación no resuelta | "`bootloader.start` no existe en `fasm.fdoc`" |
| `FASM3009` | Falta idioma por defecto | "Entrada de documentación sin el idioma por defecto del proyecto" |
| `FASM4001` | Combinación no válida | "No existe la forma `.Add(Rax, Rbx, Rcx)`" |
| `FASM4002` | Escala no válida | "La escala debe ser 1, 2, 4 u 8, no 3" |
| `FASM4003` | Índice prohibido | "`Rsp` no puede usarse como índice" |
| `FASM5001` | Símbolo externo en imagen plana | "`printf` es externo: hace falta un ejecutable del SO (Fase 7)" |
| `FASM5002` | Dirección absoluta | "En Fase 1 usa `[Rel símbolo]`" |

---

## 15. Cambios respecto a la versión anterior de este documento

| Antes | Ahora | Motivo |
|---|---|---|
| Ejemplos x64 con `Eax` | `Rax` para valores de 64 bits, `Eax` solo para 32 | `Eax` significa exactamente 32 bits y pone a cero la mitad alta |
| "El `.x64` se ejecuta como un programa nativo" | Imagen plana ejecutada por el arrancador o sobre el hardware; `.exe` en Fase 7 | Un binario crudo no es un formato que el SO sepa cargar ("Decisiones de diseño" en [CONTRIBUTING.md](CONTRIBUTING.md)) |
| Instrucciones mínimas: `Mov`, `Add`, `Sub`, `Jmp`, `Call`, `Ret`, `Int` | 30+ mnemónicos con sus formas, tablas de datos y directivas | El conjunto anterior no bastaba ni para el "hola mundo" del propio repositorio |
| `program AddTwoNumbers` con `parameter` | `program` y `routine` son bloques distintos | Un programa no es una función; mezclarlos impedía definir la ABI |
| `parameter a, b` sin destino | Parámetros asignados por ABI, con prólogo y epílogo generados | Sin esto, cada implementación inventaba su convención ("Decisiones de diseño" en [CONTRIBUTING.md](CONTRIBUTING.md)) |
| Etiquetas locales con `.` | Etiquetas locales con `@` | El `.` está reservado para las instrucciones y no debía ser ambiguo |
| Sin gramática formal | EBNF en la sección 3 | Permite implementar el analizador sin interpretar ejemplos |
| Dos superficies sin jerarquía | DSL `.fasm` canónica, API de C# derivada | Evita mantener dos lenguajes ("Decisiones de diseño" en [CONTRIBUTING.md](CONTRIBUTING.md)) |

---

## 16. Próximos pasos de sintaxis

1. Coma flotante y SIMD (`Xmm`), necesaria para código numérico y de gráficos.
2. Directiva para incrustar fragmentos de archivos `.bin` (Fase 5) y su resolución de saltos.
Documentación de símbolos (Fase 5): `///` resuelve texto desde `fasm.fdoc`.
3. `Movsx`, `Div`, `Idiv`, multiplicación de un operando y desplazamientos (`Shl`, `Shr`, `Sar`).
4. Modo de entrada con sintaxis Intel/NASM clásica, para migrar código existente
   (`WinUIAsmCtrl`) sin reescribirlo a mano.
5. **Sistema de macros.** El corpus de `WinUIAsmCtrl` usa macros de NASM (`%include`, `%define`,
   `CHILD`, `SENDI`, `PROLOGUE`) para no repetir bloques de creación de controles. Sin macros,
   FluentASM solo puede expresar ese código expandido a mano, lo que lo hace inviable en la
   práctica. Es un requisito **de la fase de ejecutables**, no un adorno.
6. Condiciones compuestas del lenguaje (`If`/`While`) como azúcar sobre `.Cmp` + salto,
   solo si no esconden los bytes generados.
