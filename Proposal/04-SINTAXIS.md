# 04 — Sintaxis

> **Normativo.** Este documento define el léxico, la forma de una sentencia, el patrón `destino.verbo(fuente)`, las vistas, los operandos de memoria y las reglas de composición. La gramática EBNF completa está en [14-GRAMÁTICA](14-GRAMATICA.md).

---

## 1. La idea en una línea

```fasm
destino.verbo(fuente1, fuente2, ...)
```

| Parte | Qué es | Ejemplo |
|---|---|---|
| **destino** | El contenedor que muta | `Gpr0`, `Byte:8 [x]`, `Frf`, `(Gpr2:Gpr0)`, `Simd3.r512.part{32}` |
| **verbo** | La operación física | `.copy`, `.add`, `.shiftLeft`, `.flowJump` |
| **fuente** | De dónde vienen los bits | `Gpr1`, `Byte:8 [x]`, `Bit:64`, `@etiqueta` |

Todo lo demás —secciones, rutinas, datos, atributos— es estructura alrededor de esta línea.

---

## 2. Léxico

### 2.1 Codificación

- UTF-8, BOM opcional.
- Fin de línea: `LF` o `CRLF` (el compilador normaliza).
- Tabuladores permitidos; el formateador usa 4 espacios.

### 2.2 Comentarios

```fasm
// Comentario de línea

/* Comentario
   de bloque */
```

No se anidan. `#` y `;` **no** son comentarios: `;` es un separador opcional de sentencias y `#` está reservado.

### 2.3 Identificadores

```
identificador := [A-Za-z_][A-Za-z0-9_]*
```

| Categoría | Convención | Ejemplo | Sensible a mayúsculas |
|---|---|---|---|
| Archivos de registro | `PascalCase` + índice | `Gpr0`, `Simd12`, `Krf3` | sí |
| Verbos | `lowerCamelCase` | `copy`, `add`, `flowStackJump` | sí |
| Atributos | `lowerCamelCase` | `.abi`, `.frame`, `.preserves` | sí |
| Secciones | `lowercase` con punto | `.execute`, `.rodata` | sí |
| Nombres de usuario | `camelCase` | `main`, `procesar`, `miRutina` | sí |
| Etiquetas | `@` + identificador | `@loop`, `@fin` | sí |
| Magnitudes | `Bit`, `Byte`, `KiloBit`, … | `Byte:8` | sí |

El compilador **no** acepta `GPR0`, `Copy` ni `.EXECUTE`. La sensibilidad a mayúsculas es total, y es lo que permite distinguir `Simd0` (registro) de `simd0` (nombre de usuario).

### 2.4 Números

| Forma | Ejemplo | Notas |
|---|---|---|
| Decimal | `64`, `1_024` | `_` es separador visual |
| Hexadecimal | `0x1F`, `0xDEAD_BEEF` | prefijo `0x` |
| Binario | `0b1010`, `0b1111_0000` | prefijo `0b` |
| Negativo | `-16` | solo en desplazamientos y offsets |
| Decimal con signo en offset | `[x - 8]` | |

No existen literales de coma flotante, de carácter ni de cadena **en instrucciones**. Las cadenas solo aparecen como payload de datos (doc 06).

### 2.5 Puntuación

| Símbolo | Uso |
|---|---|
| `.` | acceso a verbo, vista o atributo; prefijo de sección |
| `,` | separador de argumentos |
| `(` `)` | argumentos; tuplas de destino |
| `[` `]` | operando de memoria; puerto de E/S |
| `{` `}` | bloques; parámetro de partición; predicado de máscara |
| `:` | capacidad (`Byte:8`), tupla (`Gpr2:Gpr0`), declaración (`nombre: Gpr`) |
| `@` | etiqueta o referencia a etiqueta |
| `+` `-` `*` | aritmética de direcciones |
| `=` | payload de datos (solo en secciones de datos) |

---

## 3. Sentencias

Un cuerpo de rutina contiene cinco tipos de sentencia, y solo cinco:

| Tipo | Forma | Ejemplo |
|---|---|---|
| **Acción** | `receptor.verbo(args)` | `Gpr0.copy(Gpr1)` |
| **Etiqueta** | `@nombre:` | `@loop:` |
| **Declaración** | `parameter` / `local` / `data` / `resource` | `local t: Byte:8` |
| **Directiva** | `.nombre(args)` al inicio de línea | `.align(Byte:16)` |
| **Comentario** | `//` o `/* */` | `// nota` |

No hay expresiones sueltas, ni asignaciones con `=`, ni `return`, ni `if` como sentencia: el control de flujo es un verbo (`Ipr.flow*`).

### 3.1 Separación

- Una sentencia termina en fin de línea.
- `;` es un separador opcional para escribir dos sentencias en la misma línea:

```fasm
Gpr0.copy(Gpr1); Gpr0.add(Gpr2)
```

El formateador convierte siempre a una sentencia por línea.

- Los bloques `{ ... }` delimitan cuerpos y no necesitan `end`.

### 3.2 Una acción por línea

En cuerpos de rutina, el estilo obligatorio es **una acción física por línea**. El parser acepta cadenas, pero el formateador y el linter las expanden:

```fasm
// Aceptado, marcado con FASM8001 (info):
Gpr0.copy(origen).add(Byte:8 [destino]).shiftLeft(Bit:2)

// Estilo canónico:
Gpr0.copy(origen)
Gpr0.add(Byte:8 [destino])
Gpr0.shiftLeft(Bit:2)
```

En cabeceras, encadenar es el estilo correcto (doc 01 §7).

---

## 4. El receptor

El receptor es el **destino real**. Existen seis formas.

### 4.1 Registro o vista

```fasm
Gpr0
Gpr0.low32
Gpr0.high8
Frf
Frf.carry
Ipr
Srf3.base
Crf4
Drf6
Simd0.r512.part{32}
Simd0.low64
Krf1.low16
Xrf0
Xcr0
Pkru
Ssp
```

### 4.2 Memoria con capacidad

```fasm
Byte:8 [contador]
Byte:1 [Gpr4]
Byte:4 [Gpr5 - 16]
Byte:16 [tabla + Gpr1*16]
Byte:64 [Gpr7 + 128]
```

La capacidad es **obligatoria**: `[x].copy(Gpr0)` → `FASM3023`. Sin capacidad no se sabe cuántos bytes se escriben.

### 4.3 Tupla de destinos

Para verbos que escriben más de un registro:

```fasm
(Gpr2:Gpr0).multiply(Gpr3)
(Gpr0:Gpr2).divide(Gpr3)
(Gpr2:Gpr0).signExtend()
(Gpr0:Gpr3:Gpr1:Gpr2).identify()
```

El orden es el del hardware, documentado por verbo. Una tupla en un verbo de un solo destino → `FASM3016`.

> No confundir `(A:B)` (tupla de destinos) con `Byte:8` (capacidad). La diferencia es el tipo de los elementos: registros vs magnitudes.

### 4.4 Archivo o recurso

```fasm
Stack.push(Gpr0)
Stack.reserve(Byte:32)
Fence.full()
Cpu.pause()
Msr.efer.write(Gpr0, Gpr2)
Port[0x60].copy(Gpr0.low8)
```

### 4.5 Rutina (solo en cabecera)

```fasm
routine procesar.abi(win64).frame(Byte:32) { ... }
```

El receptor de una cadena de cabecera es la rutina o el programa.

### 4.6 Lo que no es receptor

| Inválido | Por qué | Alternativa |
|---|---|---|
| `mov(Gpr0, Gpr1)` | no hay destino | `Gpr0.copy(Gpr1)` |
| `.ret()` | no se sabe qué muta | `Ipr.flowStackJump()` |
| `Flow.create()` | objeto temporal | `Ipr.flowCall(@x)` |
| `Gpr0 = Gpr1` | no hay asignación | `Gpr0.copy(Gpr1)` |
| `mem1.copy(mem2)` | temporal implícito | `Gpr0.copy(Byte:8 [mem2])` + `Byte:8 [mem1].copy(Gpr0)` |

---

## 5. El verbo

### 5.1 Forma

```
verbo := "." identificador [ "{" máscara "}" ] "(" [ argumentos ] ")"
```

```fasm
.copy(Gpr1)
.addFloat{Krf1}(Simd1.r512.part{32})
.flowJump(@fin if Frf.zero)
.flowStackJump()
```

### 5.2 Familias de nombres

| Prefijo / sufijo | Significado | Ejemplo |
|---|---|---|
| `copy` | copia de bits | `copy`, `copyBlock` |
| `add` / `sub` | suma / resta | `add`, `subWithBorrow` |
| `multiply` / `divide` | producto / cociente | `multiplySigned` |
| `and` / `or` / `xor` / `not` | lógica bit a bit | `and`, `not` |
| `shift*` / `rotate*` | desplazamiento / rotación | `shiftLeft`, `rotateRight` |
| `compare` / `test` | comparación sin escritura | `compare`, `testBit` |
| `load*` / `store*` | transferencia con semántica | `loadAddress`, `storeFloatPop` |
| `flow*` | control de flujo | `flowCall`, `flowJump` |
| `Int` / `Float` / `Signed` / `Unsigned` | interpretación, solo donde hay ambigüedad | `addInt`, `addFloat`, `divideSigned` |
| `convert*` | cambio de representación | `convertToInt` |

Un verbo desconocido para el receptor → `FASM3001`, con la lista de verbos válidos en el mensaje.

### 5.3 Predicados de máscara

```fasm
Simd0.r512.part{32}.addFloat{Krf1}(Simd1.r512.part{32})       // merge-masking
Simd0.r512.part{32}.addFloat{Krf1.z}(Simd1.r512.part{32})     // zero-masking
Simd0.r512.part{32}.compareEqualInt{Krf2}(Simd1.r512.part{32}) // escribe la máscara
```

| Forma | Semántica EVEX |
|---|---|
| `{KrfN}` | merge: los elementos no seleccionados conservan su valor |
| `{KrfN.z}` | zero: los elementos no seleccionados se ponen a 0 |
| `{KrfN}` en verbo de comparación | la máscara es **destino**, no predicado |

Reglas: `Krf0` no puede ser predicado (`FASM3083`); el ancho de la máscara se deriva de la partición (`FASM3088`).

---

## 6. La fuente

### 6.1 Formas de fuente

| Forma | Ejemplo | Semántica |
|---|---|---|
| Registro / vista | `Gpr1`, `Simd2.r128.part{8}` | valor del registro |
| Memoria | `Byte:8 [x]` | contenido de 8 bytes |
| Símbolo desnudo | `@fin`, `rutina`, `dato` | **dirección** |
| Capacidad / forma | `Bit:64`, `Byte:16`, `part{32}` | forma, no dato |
| Estado | `Frf.zero`, `Frf.carry` | bit de flag |
| Recurso | `Msr.efer`, `Xcr0`, `Port[0x60]` | estado de hardware |
| Tupla de origen | `(Gpr0:Gpr2)` | solo en verbos que lo declaran |

### 6.2 Regla de la dirección

Un identificador **desnudo** es siempre una dirección (o un alias de registro). El **contenido** de memoria siempre lleva capacidad y corchetes.

```fasm
Gpr0.loadAddress(dato)          // Gpr0 = dirección de 'dato'
Gpr0.copy(Byte:8 [dato])        // Gpr0 = los 8 bytes que hay en 'dato'
Ipr.flowCall(rutina)            // salta a la dirección de 'rutina'
Ipr.flowJump(@ciclo)            // salta a la etiqueta local
```

Esto elimina la ambigüedad clásica entre `mov rax, simbolo` y `mov rax, [simbolo]`.

### 6.3 Paréntesis con nombre

`(Ipr:SrfN)` es la forma de expresar una transferencia lejana: el par `CS:RIP`.

```fasm
(Ipr:Srf0).flowJump(Gpr0)       // JMP FAR
(Ipr:Srf0).flowCall(Gpr0)       // CALL FAR
(Ipr:Srf0).flowStackJump()      // RETF
```

### 6.4 Condiciones

La condición se escribe como sufijo de la fuente de flujo:

```fasm
Ipr.flowJump(@fin)                          // incondicional
Ipr.flowJump(@fin if Frf.zero)              // ZF = 1
Ipr.flowJump(@fin if not Frf.zero)          // ZF = 0
Ipr.flowJump(@fin if Frf.carry)             // CF = 1
Ipr.flowJump(@rep if Frf.sign unequal Frf.overflow)   // JL
```

Gramática de condición:

```
condición := "if" expr
expr      := término { ("and" | "or") término }
término   := ["not"] "Frf" "." flag
           | "Frf" "." flag ("unequal" | "equal") "Frf" "." flag
```

El compilador selecciona un único `Jcc` cuando la expresión coincide con un predicado del hardware; si no, emite la secuencia de saltos equivalente y lo documenta en el mapa de expansión. Detalle en [08-FLUJO](08-FLUJO.md).

---

## 7. Operando de memoria

### 7.1 Forma

```
memoria := magnitud "[" expr_dirección "]"
expr_dirección := base [ "+" índice "*" escala ] [ "+" | "-" desp ]
                | símbolo [ "+" | "-" desp ]
```

Ejemplos:

```fasm
Byte:8 [Gpr4]                       // [RSP]
Byte:8 [Gpr5 - 16]                  // [RBP-16]
Byte:4 [Gpr3 + Gpr1*4]              // [RBX+RCX*4]
Byte:1 [Gpr8 + Gpr9*2 + 3]          // [R8+R9*2+3]
Byte:8 [contador]                  // RIP-relativo
Byte:16 [tabla + Gpr1*16]          // [tabla + RCX*16]
```

### 7.2 Reglas

| Regla | Diagnóstico |
|---|---|
| Escala ∈ {1, 2, 4, 8} | `FASM4004` |
| Índice ≠ `Gpr4` (RSP) | `FASM4003` |
| Base `Gpr5`/`Gpr13` requiere `disp8` (el encoder lo emite) | automático |
| Una base + un índice como máximo | `FASM4005` |
| Disp de 32 bits con signo | `FASM4006` |
| Sin capacidad → error | `FASM3023` |
| `.high8` no puede ser base ni índice | `FASM4007` |

### 7.3 RIP-relativo

Por defecto, `@símbolo` genera direccionamiento **RIP-relativo** (imagen PIC, D-030):

```fasm
Gpr0.copy(Byte:8 [contador])       // mov rax, [rip + contador]
```

Para absoluto se usa `absolute(símbolo)`:

```fasm
Gpr0.copy(Byte:8 [absolute(contador)])
```

En una imagen plana cargada en una base desconocida, el absoluto → `FASM5008`.

### 7.4 E/S

`Port[...]` es un espacio de direcciones distinto:

```fasm
Gpr0.low8.copy(Port[0x60])
Port[0x60].copy(Gpr0.low8)
Gpr0.low32.copy(Port[Gpr2.low16])
```

---

## 8. Cadenas (chaining)

### 8.1 En cabeceras

```fasm
routine procesar
    .abi(win64)
    .frame(Byte:32)
    .preserves(Gpr3, Gpr12)
{
    ...
}
```

Se encadenan atributos **del mismo receptor**. Cada eslabón es una declaración, no una acción.

### 8.2 En cuerpos

```fasm
Gpr0.copy(x).add(y)
```

Se permite porque el receptor no cambia (`Gpr0`), pero el linter emite `FASM8001` y el formateador expande. Encadenar acciones distintas (`Gpr0.copy(x).flowJump(@y)`) es un error: el segundo verbo pertenece a otro receptor.

### 8.3 Regla

> **Cadena solo si el receptor es el mismo. Si el receptor cambia, es otra línea.**

---

## 9. Espacios y formato

| Elemento | Regla |
|---|---|
| Indentación | 4 espacios por nivel |
| Espacio tras coma | siempre |
| Espacio antes de `{` | sí, en cabeceras |
| Línea en blanco | entre rutinas y bloques de declaraciones |
| Longitud máxima | 100 columnas (aviso `FASM8102`) |
| Alineación de comentarios | a partir de la columna 48, opcional |

El formateador (`fasm fmt`) es determinista: mismo AST ⇒ mismos bytes de texto.

---

## 10. Azúcar sintáctico y su expansión

El lenguaje admite azúcar solo cuando la expansión es **única y documentada**.

| Azúcar | Expansión |
|---|---|
| `Gpr0.clear()` | `Gpr0.copy` de cero estructural (XOR reg, reg) |
| `Gpr0.increment()` | `INC r/m64` |
| `Gpr0.decrement()` | `DEC r/m64` |
| `Crf4.fsgsbase(1)` | `Crf4.bit(16, 1)` (leer-modificar-escribir) |
| `Xcr0.avx(1)` | `Xcr0.bit(2, 1)` |
| `Ipr.flowJump(@x if Frf.zero)` | `flowJump(@x if Frf.equal)` |
| `Simd0.r512.part{32}` | `Simd0.r512.part{32}` (explícito, sin cambio) |

Ningún azúcar introduce estado, temporales ni instrucciones no documentadas.

---

## 11. Errores léxicos y sintácticos

| Código | Situación |
|---|---|
| `FASM1001` | Carácter inesperado |
| `FASM1002` | Cadena no cerrada |
| `FASM1003` | Comentario de bloque no cerrado |
| `FASM1004` | Número malformado (`0x`, `0b2`) |
| `FASM1005` | Identificador demasiado largo (> 255) |
| `FASM1101` | Falta `)` |
| `FASM1102` | Falta `}` |
| `FASM1103` | Falta `]` |
| `FASM1104` | Falta capacidad en operando de memoria |
| `FASM1105` | Verbo sin receptor |
| `FASM1106` | Argumento inesperado |
| `FASM1107` | Coma final |
| `FASM1108` | Bloque sin cabecera |
| `FASM1109` | Cadena en contexto de instrucción |
| `FASM1110` | Carácter de escape inválido |

Todos son errores de recuperación: el parser reporta el punto exacto y continúa hasta el siguiente `;`, `}` o fin de línea, para que el editor muestre varios errores a la vez.

---

## 12. Ejemplo comentado

```fasm
program demo.abi(win64).target(x64-avx2).entry(main) {

    // Dirección: un símbolo desnudo
    section .rodata {
        data saludo: Byte:14
    }

    section .execute {
        routine main {
            // receptor: Gpr0        verbo: loadAddress   fuente: dirección
            Gpr0.loadAddress(saludo)

            // receptor: Gpr1.low8   verbo: copy          fuente: memoria de 1 byte
            Gpr1.low8.copy(Byte:1 [saludo])

            // receptor: Frf         verbo: carry         fuente: estado 0
            Frf.carry(0)

            // receptor: Ipr         verbo: flowStackJump fuente: ninguna
            Ipr.flowStackJump()
        }
    }
}
```

Cada línea responde las cinco preguntas del doc 01 §8: contenedor, operación, fuente, forma y condición.

---

## 13. Resumen

- Un patrón: `destino.verbo(fuente)`.
- El receptor es siempre el destino real; nunca un objeto temporal.
- La memoria exige capacidad: `Byte:N [dirección]`.
- El símbolo desnudo es dirección; el contenido lleva corchetes.
- Las vistas se encadenan sobre el registro, no entre sí.
- Las condiciones son sufijo de la fuente de flujo.
- Se encadena lo que configura; se bloquea lo que ejecuta.
- Todo error léxico o sintáctico tiene código y posición.
