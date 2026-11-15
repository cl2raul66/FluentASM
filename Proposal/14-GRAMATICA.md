# 14 — Gramática

> **Normativo.** Este documento contiene la gramática completa del lenguaje en EBNF, las decisiones de parsing y la frontera exacta entre lo que acepta el parser y lo que rechaza el checker. Un parser que acepte este documento y un checker que implemente 02–13 producen un compilador conforme.

---

## 1. Convención de la notación

Se usa EBNF ISO/IEC 14977 con estas abreviaturas:

| Símbolo | Significado |
|---|---|
| `=` | definición |
| `,` | concatenación |
| `;` | fin de producción |
| `\|` | alternativa |
| `[ x ]` | opcional (0 o 1) |
| `{ x }` | repetición (0 o más) |
| `( x )` | agrupación |
| `"x"` | terminal literal |
| `? x ?` | descripción informal |

Los terminales son **sensibles a mayúsculas**. `Gpr0` y `gpr0` son tokens distintos; el segundo es un identificador de usuario.

---

## 2. Léxico

```ebnf
file             = { trivia } , program , { trivia } , EOF ;

trivia           = whitespace | comment | newline ;
whitespace       = " " | "\t" | "\r" ;
newline          = "\n" ;
comment          = lineComment | blockComment ;
lineComment      = "//" , { ? cualquier carácter salvo newline ? } ;
blockComment     = "/*" , { ? cualquier carácter ? - "*/" } , "*/" ;

identifier       = letter , { letter | digit } ;
letter           = "A".."Z" | "a".."z" | "_" ;
digit            = "0".."9" ;
hexDigit         = digit | "A".."F" | "a".."f" ;
binDigit         = "0" | "1" ;

integer          = decimal | hexadecimal | binary ;
decimal          = digit , { [ "_" ] , digit } ;
hexadecimal      = "0x" , hexDigit , { [ "_" ] , hexDigit } ;
binary           = "0b" , binDigit , { [ "_" ] , binDigit } ;

registerName     = registerFile , index ;
registerFile     = "Gpr" | "Srf" | "Crf" | "Drf" | "Simd" | "Krf" | "Xrf" ;
index            = digit , [ digit ] ;
```

### 2.1 Reglas léxicas

| Regla | Detalle | Diagnóstico |
|---|---|---|
| Codificación | UTF-8; BOM opcional al inicio | `FASM1006` |
| Fin de línea | `LF` o `CRLF`; el lexer normaliza a `LF` | — |
| Comentarios | No se anidan; `/* /* */` cierra en el primer `*/` | `FASM1003` |
| `;` | Separador de sentencias, no comentario | — |
| `#` | Reservado; su uso es error | `FASM1001` |
| Longitud de identificador | ≤ 255 caracteres | `FASM1005` |
| Número malformado | `0x`, `0b2`, `1__2` | `FASM1004` |
| Carácter inesperado | Cualquiera fuera de este léxico | `FASM1001` |

### 2.2 Newline significativo

El fin de línea **termina una sentencia**. Por eso el parser no necesita un terminador explícito, y `;` es solo una alternativa para escribir dos sentencias en una línea (doc 04 §3.1).

Consecuencia para el lexer: debe emitir tokens `NEWLINE` (colapsando líneas en blanco) o exponer los saltos de línea al parser. Se elige lo primero: un único token `NEWLINE` por secuencia de uno o más saltos, y ninguno dentro de `( … )`, `[ … ]` o `{ … }` de una expresión.

### 2.3 Palabras reservadas

Reservadas **incondicionalmente** (nunca pueden ser identificadores de usuario):

```
program  section  routine  parameter  local  data  resource
repeat   zero     absolute  if        and    or    not
```

Contextuales (solo son palabra clave en su posición; en cualquier otro lugar son identificadores):

| Token | Posición en que es clave | Ejemplo |
|---|---|---|
| `Gpr`, `Srf`, `Simd`, … | inicio de un `registerName` | `Gpr0` |
| `Bit`, `Byte`, `KiloByte`, … | antes de `:` en una magnitud | `Byte:8` |
| `Msr`, `Port`, `Stack`, `Cpu`, `Fence`, `Xcr0`, `Pkru`, `Ssp` | inicio de un receptor o fuente | `Msr.efer` |
| `win64`, `sysv`, `x64-sse2`… | argumento de `.abi`/`.target` | `.abi(win64)` |
| `copy`, `add`, `flowJump`, … | tras `.` | `Gpr0.copy(…)` |

Esto es lo que permite que `and`, `or` y `not` sean a la vez operadores de condición y verbos (`.and`, `.or`, `.not`): **tras un `.` nunca se busca palabra clave**, se busca nombre de verbo, atributo o miembro.

---

## 3. Estructura del archivo

```ebnf
program          = "program" , identifier , { programAttribute } , block ;
block            = "{" , { sectionItem } , "}" ;
sectionItem      = section | dataDeclaration | resourceDeclaration ;

section          = "section" , sectionName , { sectionAttribute } , "{" , { sectionEntry } , "}" ;
sectionName      = "." , identifier ;                     (* .execute .rodata .data .bss *)
sectionEntry     = routine | dataDeclaration | resourceDeclaration ;
```

La gramática **acepta** cualquier nombre de sección; el checker rechaza los no canónicos con `FASM2006`. Del mismo modo, acepta `routine` en cualquier sección y aplica `FASM2009` si no está en `.execute`. Se separa así el reconocimiento sintáctico de la validación semántica: el parser nunca necesita conocer la tabla de secciones.

### 3.1 Atributos

```ebnf
programAttribute = "." , programAttrName , "(" , [ attributeArgs ] , ")" ;
programAttrName  = "abi" | "target" | "entry" | "image" | "stack" | "ring" ;

sectionAttribute = "." , ( "align" | "visibility" ) , "(" , [ attributeArgs ] , ")" ;
routineAttribute = "." , routineAttrName , "(" , [ attributeArgs ] , ")" ;
routineAttrName  = "abi" | "frame" | "preserves" | "shadow" | "redzone"
                 | "naked" | "align" | "ring" | "visibility" | "returns"
                 | "leaf" | "variadic" ;

attributeArgs    = argument , { "," , argument } ;         (* ≥ 1 si el atributo lo exige *)
```

Un atributo desconocido es sintácticamente válido y se rechaza en el checker con `FASM2004`, que lista los nombres admitidos para ese receptor. Esto es deliberado: el parser no codifica la lista de atributos y el editor puede ofrecer completado de atributos desde una única fuente (doc 18).

### 3.2 Rutinas

```ebnf
routine          = "routine" , identifier , { routineAttribute } , "{" , routineBody , "}" ;
routineBody      = { parameterDeclaration } , { localDeclaration } , { statement } ;

parameterDeclaration = "parameter" , identifier , ":" , parameterClass ;
parameterClass       = "Gpr" | "Simd" | "Stack" ;

localDeclaration     = "local" , identifier , ":" , magnitude ;
```

`resource` **no** se declara dentro de una rutina: un recurso es un símbolo de sección de datos y una rutina vive en `.execute`, donde `resource` es `FASM2023`. La gramática, por tanto, no lo contempla en `routineBody`.

### 3.3 Datos y recursos

```ebnf
dataDeclaration     = "data" , identifier , ":" , magnitude , [ "=" , payload ] ;

resourceDeclaration = "resource" , identifier , ":" , resourceSpec ;
resourceSpec        = resourceKind , "(" , [ attributeArgs ] , ")" ;
resourceKind        = "file" | "memory" | "port" | "library" ;
```

`library` se reconoce y se rechaza con `FASM2020` en v1 (reservado para fase 2).

---

## 4. Sentencias

```ebnf
statement        = action | label | directive | emptyStatement ;
label            = "@" , identifier , ":" ;
directive        = "." , identifier , "(" , [ argumentList ] , ")" ;
emptyStatement   = ";" ;
```

Toda sentencia puede ir seguida de `;`. No hay más tipos: ni asignación, ni `if`, ni `return`, ni expresión suelta (doc 04 §3).

### 4.1 Desambiguación de una sentencia

El primer token decide la producción, sin *backtracking*:

| Primer token | Producción | Ejemplo |
|---|---|---|
| `@` | `label` | `@fin:` |
| `.` | `directive` | `.align(Byte:16)` |
| `(` | `action` (tupla de destino) | `(Gpr2:Gpr0).multiply(Gpr3)` |
| identificador / registro | `action` | `Gpr0.copy(Gpr1)` |
| `;` | `emptyStatement` | `;` |
| `NEWLINE` | — (se descarta) | |

---

## 5. Acciones

```ebnf
action           = receiver , verb ;
verb             = "." , identifier , [ maskPredicate ] , "(" , [ argumentList ] , ")" ;
maskPredicate    = "{" , maskRegister , [ "." , "z" ] , "}" ;
maskRegister     = "Krf" , digit ;

argumentList     = argument , { "," , argument } ;
argument         = source , [ conditionSuffix ] ;
conditionSuffix  = "if" , condition ;
```

`maskPredicate` solo puede aparecer si el verbo lo admite; en caso contrario → `FASM3084` (target sin AVX-512) o `FASM4002` (verbo sin predicado).

### 5.1 Receptores

```ebnf
receiver         = registerChain
                 | memoryOperand
                 | tupleDestination
                 | unitReceiver ;

registerChain    = registerName , { view } ;

tupleDestination = "(" , registerChain , ":" , registerChain ,
                       { ":" , registerChain } , ")" ;

unitReceiver     = "Stack" | "Frf" | "Ipr" | "Cpu" | "Fence"
                 | "Xcr0" | "Pkru" | "Ssp"
                 | msrName
                 | portOperand
                 | identifier ;                     (* alias de parameter *)

msrName          = "Msr" , "." , identifier ;
portOperand      = "Port" , "[" , ( integer | registerName ) , "]" ;
```

`(Ipr:Srf0)` se **reconoce** como `tupleDestination` y el checker lo reclasifica como par lejano (doc 04 §6.3, doc 08 §6): es la única tupla cuyo primer elemento es `Ipr`. La gramática no distingue ambos casos porque la forma es idéntica; la semántica sí.

Un `identifier` desnudo como receptor solo es válido si nombra un `parameter` de clase `Gpr` o `Simd`; en otro caso es un símbolo no resoluble (`FASM2003`).

### 5.2 Vistas

```ebnf
view             = widthView | elementView | memberView ;

widthView        = "." , ( "low8" | "low16" | "low32" | "low64"
                         | "high8" | "r128" | "r256" | "r512" ) ;

elementView      = "." , "part" , "{" , partition , "}"
                 | "." , "element" , "(" , integer , ")" ;

partition        = "8" | "16" | "32" | "64" ;
memberView       = "." , identifier ;
```

`memberView` cubre `.base`, `.status8`, `.current`, `.carry`, `.pointer`, `.status`, `.control`, `.tag` y cualquier miembro con nombre. El checker valida la existencia y compatibilidad (`FASM3001`, `FASM3025`).

**No se permite vista sobre vista** (`Gpr0.low32.low16` → `FASM3024`). La gramática lo permite (la repetición `{ view }` es libre) y el checker lo rechaza; así el mensaje puede mostrar la cadena completa y sugerir la vista correcta.

`element(k)` es una **vista de lectura** con índice estructural. No es una llamada: no muta nada y solo aparece como fuente.

### 5.3 Fuentes

```ebnf
source           = registerChain
                 | memoryOperand
                 | symbolReference
                 | absoluteReference
                 | formValue
                 | stateReference
                 | portOperand
                 | tupleSource
                 | elementSource
                 | enumerator
                 | integerSource
                 | flagState ;

formValue        = magnitude ;
stateReference   = "Frf" , "." , identifier
                 | "Xrf" , "." , ( "status" | "control" | "tag" )
                 | "Stack" , "." , ( "pointer" | "framePointer" )
                 | "Ipr" , "." , "current" ;
tupleSource      = "(" , registerChain , ":" , registerChain , ")" ;
elementSource    = registerChain , "." , "element" , "(" , integer , ")" ;
enumerator       = "." , identifier ;                     (* .nearest .t0 .nta *)
integerSource    = integer ;
flagState        = "0" | "1" | "not" ;
```

### 5.4 La frontera crítica: `integerSource`

Un entero desnudo **es sintácticamente válido** en posición de argumento. El parser no lo rechaza; el checker lo hace salvo que el verbo declare un parámetro de tipo *estructural*.

| Verbo | Parámetro | `0x80` es… |
|---|---|---|
| `Ipr.flowInterrupt(0x80)` | vector de interrupción | forma |
| `Port[0x60]` | dirección de E/S | forma |
| `Gpr0.shiftLeft(Bit:3)` | magnitud `Bit` | forma |
| `Gpr0.add(42)` | `r/m64` | **dato** → `FASM2007` |
| `Gpr0.copy(0)` | `r/m64` | **dato** → `FASM2007` |

Es decir: el parser acepta `integerSource` en cualquier argumento y el checker aplica la tabla de la firma (doc 06 §7.1). Esta separación es obligatoria: sin ella, el parser tendría que conocer las 200 firmas de verbo, y el mensaje de error perdería el contexto del verbo.

### 5.5 Condiciones

```ebnf
condition        = conjunction , { "or" , conjunction } ;
conjunction      = negation , { "and" , negation } ;
negation         = [ "not" ] , flagTest ;
flagTest         = flagReference , [ relation , flagReference ] ;
relation         = "equal" | "unequal" ;
flagReference    = "Frf" , "." , identifier ;
```

`and` liga más que `or` (precedencia convencional). `not` se aplica solo a un `flagTest`, no a una conjunción completa: `not (A and B)` no se escribe; se escribe `not A or not B`.

Los alias (`Frf.less`, `Frf.above`, …) son `flagReference` normales y el checker los expande (doc 08 §2.2).

---

## 6. Memoria

```ebnf
memoryOperand    = magnitude , "[" , address , "]" ;

magnitude        = capacityName , ":" , integer ;
capacityName     = "Bit"    | "Byte"
                 | "KiloBit" | "KiloByte"
                 | "MegaBit" | "MegaByte"
                 | "GigaBit" | "GigaByte"
                 | "TeraBit" | "TeraByte" ;

address          = addressAtom , { ( "+" | "-" ) , addressAtom } ;
addressAtom      = gpr64
                 | scaledIndex
                 | displacement
                 | symbolReference
                 | absoluteReference ;

scaledIndex      = gpr64 , [ "*" , scale ] ;
scale            = "1" | "2" | "4" | "8" ;
gpr64            = "Gpr" , index ;
displacement     = integer | "-" , integer ;
symbolReference  = labelReference | identifier ;
labelReference   = "@" , identifier ;
absoluteReference = "absolute" , "(" , symbolReference , ")" ;
```

### 6.1 Precedencia en direcciones

Solo hay dos operadores, y `*` liga más que `+`/`-`:

```
Gpr3 + Gpr1 * 4 + 8    ≡    Gpr3 + (Gpr1 * 4) + 8
```

La evaluación es de izquierda a derecha sobre los sumandos, sin asociatividad problemática porque la suma de direcciones es conmutativa y el encoder reordena a la forma SIB canónica (base + índice·escala + desplazamiento).

### 6.2 Restricciones que aplica el checker

| Restricción | Diagnóstico |
|---|---|
| Una sola base y un solo índice | `FASM4005` |
| `Gpr4` no puede ser índice | `FASM4003` |
| Escala ∈ {1, 2, 4, 8} | `FASM4004` |
| Desplazamiento en 32 bits con signo | `FASM4006` |
| Solo `GprN` completo como base/índice | `FASM4008` |
| Capacidad obligatoria | `FASM3023` |

---

## 7. Payload

```ebnf
payload          = payloadItem , { "," , payloadItem } ;
payloadItem      = integer | string | repetition | fill ;
repetition       = "repeat" , integer , "{" , payload , "}" ;
fill             = "zero" , magnitude ;

string           = '"' , { stringChar | escape } , '"' ;
stringChar       = ? cualquier carácter UTF-8 salvo '"' , "\" y newline ? ;
escape           = "\" , ( "n" | "r" | "t" | "0" | "\" | '"'
                         | "x" , hexDigit , hexDigit ) ;
```

Límites del parser (no del checker):

| Límite | Valor | Diagnóstico |
|---|---|---|
| Anidamiento de `repeat` | 8 | `FASM1007` |
| Elementos por payload | 1 048 576 | `FASM1007` |
| Longitud de una cadena | 65 535 bytes | `FASM1007` |
| Anidamiento de bloques `{}` | 64 | `FASM1008` |

---

## 8. Gramática completa, en un bloque

```ebnf
(* ---------- archivo ---------- *)
file             = { trivia } , program , { trivia } , EOF ;
trivia           = whitespace | comment | newline ;

(* ---------- programa ---------- *)
program          = "program" , identifier , { programAttribute } , block ;
block            = "{" , { sectionItem } , "}" ;
sectionItem      = section | dataDeclaration | resourceDeclaration ;

programAttribute = "." , programAttrName , "(" , [ attributeArgs ] , ")" ;
programAttrName  = "abi" | "target" | "entry" | "image" | "stack" | "ring" ;

(* ---------- secciones ---------- *)
section          = "section" , sectionName , { sectionAttribute } ,
                   "{" , { sectionEntry } , "}" ;
sectionName      = "." , identifier ;
sectionEntry     = routine | dataDeclaration | resourceDeclaration ;
sectionAttribute = "." , ( "align" | "visibility" ) , "(" , [ attributeArgs ] , ")" ;

(* ---------- rutinas ---------- *)
routine          = "routine" , identifier , { routineAttribute } ,
                   "{" , routineBody , "}" ;
routineBody      = { parameterDeclaration } , { localDeclaration } , { statement } ;

routineAttribute = "." , routineAttrName , "(" , [ attributeArgs ] , ")" ;
routineAttrName  = "abi" | "frame" | "preserves" | "shadow" | "redzone"
                 | "naked" | "align" | "ring" | "visibility" | "returns"
                 | "leaf" | "variadic" ;

parameterDeclaration = "parameter" , identifier , ":" , parameterClass ;
parameterClass       = "Gpr" | "Simd" | "Stack" ;
localDeclaration     = "local" , identifier , ":" , magnitude ;

(* ---------- datos ---------- *)
dataDeclaration  = "data" , identifier , ":" , magnitude , [ "=" , payload ] ;
resourceDeclaration = "resource" , identifier , ":" , resourceSpec ;
resourceSpec     = resourceKind , "(" , [ attributeArgs ] , ")" ;
resourceKind     = "file" | "memory" | "port" | "library" ;

(* ---------- sentencias ---------- *)
statement        = action | label | directive | emptyStatement ;
label            = "@" , identifier , ":" ;
directive        = "." , identifier , "(" , [ argumentList ] , ")" ;
emptyStatement   = ";" ;

(* ---------- acciones ---------- *)
action           = receiver , verb ;
verb             = "." , identifier , [ maskPredicate ] , "(" , [ argumentList ] , ")" ;
maskPredicate    = "{" , maskRegister , [ "." , "z" ] , "}" ;
maskRegister     = "Krf" , digit ;

argumentList     = argument , { "," , argument } ;
argument         = source , [ conditionSuffix ] ;
conditionSuffix  = "if" , condition ;
attributeArgs    = argument , { "," , argument } ;

(* ---------- receptores ---------- *)
receiver         = registerChain | memoryOperand | tupleDestination | unitReceiver ;
registerChain    = registerName , { view } ;
tupleDestination = "(" , registerChain , ":" , registerChain ,
                       { ":" , registerChain } , ")" ;
unitReceiver     = "Stack" | "Frf" | "Ipr" | "Cpu" | "Fence"
                 | "Xcr0" | "Pkru" | "Ssp"
                 | msrName | portOperand | identifier ;
msrName          = "Msr" , "." , identifier ;
portOperand      = "Port" , "[" , ( integer | registerName ) , "]" ;

(* ---------- vistas ---------- *)
view             = widthView | elementView | memberView ;
widthView        = "." , ( "low8" | "low16" | "low32" | "low64"
                         | "high8" | "r128" | "r256" | "r512" ) ;
elementView      = "." , "part" , "{" , partition , "}"
                 | "." , "element" , "(" , integer , ")" ;
partition        = "8" | "16" | "32" | "64" ;
memberView       = "." , identifier ;

(* ---------- fuentes ---------- *)
source           = registerChain | memoryOperand | symbolReference | absoluteReference
                 | formValue | stateReference | portOperand | tupleSource | elementSource
                 | enumerator | integerSource | flagState ;
formValue        = magnitude ;
stateReference   = "Frf" , "." , identifier
                 | "Xrf" , "." , ( "status" | "control" | "tag" )
                 | "Stack" , "." , ( "pointer" | "framePointer" )
                 | "Ipr" , "." , "current" ;
tupleSource      = "(" , registerChain , ":" , registerChain , ")" ;
elementSource    = registerChain , "." , "element" , "(" , integer , ")" ;
enumerator       = "." , identifier ;
integerSource    = integer ;
flagState        = "0" | "1" | "not" ;

(* ---------- condiciones ---------- *)
condition        = conjunction , { "or" , conjunction } ;
conjunction      = negation , { "and" , negation } ;
negation         = [ "not" ] , flagTest ;
flagTest         = flagReference , [ relation , flagReference ] ;
relation         = "equal" | "unequal" ;
flagReference    = "Frf" , "." , identifier ;

(* ---------- memoria ---------- *)
memoryOperand    = magnitude , "[" , address , "]" ;
magnitude        = capacityName , ":" , integer ;
capacityName     = "Bit" | "Byte" | "KiloBit" | "KiloByte"
                 | "MegaBit" | "MegaByte" | "GigaBit" | "GigaByte"
                 | "TeraBit" | "TeraByte" ;
address          = addressAtom , { ( "+" | "-" ) , addressAtom } ;
addressAtom      = gpr64 | scaledIndex | displacement | symbolReference
                 | absoluteReference ;
scaledIndex      = gpr64 , [ "*" , scale ] ;
scale            = "1" | "2" | "4" | "8" ;
gpr64            = "Gpr" , index ;
displacement     = integer | "-" , integer ;
symbolReference  = labelReference | identifier ;
labelReference   = "@" , identifier ;
absoluteReference = "absolute" , "(" , symbolReference , ")" ;

(* ---------- payload ---------- *)
payload          = payloadItem , { "," , payloadItem } ;
payloadItem      = integer | string | repetition | fill ;
repetition       = "repeat" , integer , "{" , payload , "}" ;
fill             = "zero" , magnitude ;
string           = '"' , { stringChar | escape } , '"' ;
escape           = "\" , ( "n" | "r" | "t" | "0" | "\" | '"'
                         | "x" , hexDigit , hexDigit ) ;

(* ---------- léxico ---------- *)
identifier       = letter , { letter | digit } ;
letter           = "A".."Z" | "a".."z" | "_" ;
digit            = "0".."9" ;
hexDigit         = digit | "A".."F" | "a".."f" ;
binDigit         = "0" | "1" ;
integer          = decimal | hexadecimal | binary ;
decimal          = digit , { [ "_" ] , digit } ;
hexadecimal      = "0x" , hexDigit , { [ "_" ] , hexDigit } ;
binary           = "0b" , binDigit , { [ "_" ] , binDigit } ;
registerName     = registerFile , index ;
registerFile     = "Gpr" | "Srf" | "Crf" | "Drf" | "Simd" | "Krf" | "Xrf" ;
index            = digit , [ digit ] ;
```

---

## 9. Decisiones de parsing

### 9.1 LL(1) por construcción

Cada alternativa de cada producción se distingue por su **primer token**:

| No terminal | Discriminante |
|---|---|
| `statement` | `@`, `.`, `(`, identificador, `;` |
| `sectionItem` | `section`, `data`, `resource` |
| `sectionEntry` | `routine`, `data`, `resource` |
| `receiver` | `(`, `[`, `Msr.`, `Port[`, identificador de archivo, identificador desnudo |
| `source` | forma del token (registro, magnitud, símbolo, `.`, `(`, entero) |
| `view` | texto tras `.`: `part`, `element` o miembro |
| `payloadItem` | entero, `"`, `repeat`, `zero` |

No hay ambigüedad que requiera más de un token de *lookahead*, salvo un caso: `(` puede iniciar una tupla de destino o una tupla de origen. Como una tupla de origen nunca está al inicio de una sentencia, el contexto lo resuelve.

### 9.2 El caso `(` al inicio de línea

```fasm
(Gpr2:Gpr0).multiply(Gpr3)          // tupla de destino: acción
```

Una línea que empieza con `(` **solo** puede ser una acción con tupla de destino. No existe ninguna otra sentencia que empiece así, y una expresión suelta está prohibida. Si el checker no encuentra un verbo tras el `)`, emite `FASM1105`.

### 9.3 Newline dentro de paréntesis

Dentro de `( … )`, `[ … ]` y `{ … }` de un payload, el newline **no** termina la sentencia. Esto permite escribir:

```fasm
Simd0.r512.part{32}.addFloat(
    Simd1.r512.part{32},
    Simd2.r512.part{32}
)
```

El formateador recompone la línea si cabe en 100 columnas.

### 9.4 Recuperación de errores

| Punto de sincronización | Uso |
|---|---|
| `NEWLINE` | Error dentro de una sentencia |
| `;` | Ídem |
| `}` | Error dentro de un bloque |
| `program` | Error a nivel de archivo |
| EOF | Fin |

Reglas:

- El parser reporta **como máximo 50 errores** por archivo y se detiene (`FASM1009` como resumen).
- Tras un error, consume hasta el siguiente sincronizador y continúa.
- Nunca lanza excepción por entrada inválida: siempre produce un AST parcial válido.
- Un `}` sin abrir o un `{` sin cerrar se reporta **una vez**, en la apertura, no en cada línea siguiente.

### 9.5 Profundidad y tamaño

| Límite | Valor | Motivo |
|---|---|---|
| Anidamiento de bloques | 64 | evitar desbordar la pila del parser |
| Argumentos por verbo | 8 | ningún verbo real pasa de 4 |
| Atributos por cabecera | 16 | ninguno real pasa de 8 |
| Sentencias por rutina | 1 000 000 | sanidad |
| Tamaño de archivo | 64 MiB | sanidad |

---

## 10. Lo que la gramática **no** contiene

| Ausente | Consecuencia | Dónde se expresa |
|---|---|---|
| Expresiones aritméticas generales | no hay `1 + 2` en argumentos | solo en `address`, con `+`/`-`/`*` |
| Precedencia de operadores | no aplica | — |
| `if` / `while` / `for` / `return` | no existen como sentencia | `Ipr.flow*` |
| Asignación `=` | solo en `data` (payload) | `copy` |
| Llamadas como expresión | `f(x)` no es un valor | `flowCall` + registro |
| Literales de carácter | `'A'` no es token | `data` + `"A"` |
| Literales de coma flotante | `1.5` no es token | bytes IEEE 754 en datos |
| Cadenas en instrucciones | `"x"` solo en `payload` | símbolo de `.rodata` |
| Operador ternario, `??`, `=>` | no existen | — |
| Genéricos, templates | no existen | — |

---

## 11. Correspondencia con los documentos

| Producción | Documento que la define semánticamente |
|---|---|
| `program`, `section`, `routine`, declaraciones | [05-PROGRAMA](05-PROGRAMA.md) |
| `action`, `receiver`, `verb`, `view`, `source` | [04-SINTAXIS](04-SINTAXIS.md) |
| `magnitude`, `partition`, `formValue` | [02-MODELO-ATM](02-MODELO-ATM.md) |
| `registerName`, `memberView` | [03-REGISTROS](03-REGISTROS.md) |
| `payload`, `integerSource`, `flagState` | [06-LITERALES](06-LITERALES.md) |
| `condition` | [08-FLUJO](08-FLUJO.md) |
| `address`, `memoryOperand` | [09-MEMORIA](09-MEMORIA.md) |
| `maskPredicate`, `partition` en SIMD | [10-SIMD](10-SIMD.md) |
| `routineAttribute` de ABI | [12-ABI](12-ABI.md) |
| Límites y recuperación | [13-DIAGNOSTICOS](13-DIAGNOSTICOS.md) |

---

## 12. Diagnósticos propios del parser

| Código | Severidad | Situación |
|---|---|---|
| `FASM1007` | error | Límite de payload o de cadena excedido |
| `FASM1008` | error | Anidamiento de bloques mayor de 64 |
| `FASM1009` | error | Demasiados errores en el archivo (resumen) |
| `FASM1111` | error | Dos sentencias sin separador |

El resto de diagnósticos sintácticos están en [13-DIAGNOSTICOS](13-DIAGNOSTICOS.md) §4.

---

## 13. Resumen

- Una gramática EBNF completa y LL(1) sin *backtracking*.
- Newline termina sentencia; `;` es un separador alternativo.
- Palabras clave contextuales: tras `.` nunca hay palabra reservada.
- El parser **acepta** todo lo reconocible; el checker **rechaza** lo inválido.
- `integerSource` es el único punto donde la sintaxis es permisiva a propósito.
- Los límites están declarados y tienen código de error.
- La recuperación produce siempre un AST parcial utilizable por el editor.
