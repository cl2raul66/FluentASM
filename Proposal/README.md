# FluentASM — Especificación del lenguaje

> **Versión:** 1.0.0-draft
> **Fecha:** 14 de noviembre de 2026
> **Arquitectura objetivo:** x64 (nativo)
> **Estado:** normativa lista para implementar el compilador; 5 decisiones pendientes de confirmación humana (véase §7).

---

## 1. Qué es FluentASM

FluentASM es un **ensamblador de flujo de datos** (*dataflow assembly*) para x64.

No es un lenguaje de alto nivel disfrazado. No hay tipos abstractos, no hay objetos, no hay *runtime*, no hay máquina virtual. Hay **contenedores físicos** (registros, memoria, flags, pila) y **verbos físicos** (copiar, sumar, desplazar, saltar) que describen qué bits se mueven, se transforman o se comparan, y a dónde van.

La sintaxis completa cabe en una idea:

```fasm
destino.verbo(fuente)
```

El **receptor** es siempre el destino real y mutable. La **fuente** es siempre de donde vienen los bits. El **verbo** es siempre lo que el hardware hace físicamente. No hay objetos temporales, ni *builders*, ni contextos que se confirmen al final.

```fasm
Gpr0.copy(Byte:8 [entrada])          // memoria → registro
Gpr0.add(Byte:8 [incremento])       // registro + memoria → registro
Frf.carry(1)                         // CF = 1
Simd0.r512.part{32}.addFloat(Simd1.r512.part{32})
Ipr.flowJump(@fin if Frf.zero)
```

FluentASM separa tres planos que nunca se mezclan:

| Plano | Pregunta que responde | Dónde vive |
|---|---|---|
| **SDM** | ¿Qué hace el silicio? | Manuales Intel/AMD; tabla de verdad física |
| **ATM** | ¿Qué contenedores y formas existen? | Modelo de tipos del lenguaje (`Bit:N`, `Byte:N`, vistas) |
| **Superficie** | ¿Cómo lo escribe el humano? | Sintaxis, verbos, bloques, IntelliSense |

La regla de oro: **la superficie no puede inventar nada que el SDM no pueda ejecutar, y el ATM no puede esconder nada que el programador necesite saber.**

---

## 2. Los diez principios

1. **El código describe rutas y transformaciones, no valores.** Las instrucciones llevan fuentes, destinos y formas (tamaños, particiones, desplazamientos, estados); nunca datos incrustados.
2. **Honestidad física.** Todo constructo corresponde a estado o encoding real. Si el hardware tiene 16 registros de 64 bits, el lenguaje dice 16; si una instrucción toca CF, lo dice.
3. **Sin tipos abstractos.** Solo `Bit:N` y `Byte:N` (y sus múltiplos). La interpretación (entero, flotante, máscara) es propiedad del **verbo**, no del tipo.
4. **Un solo patrón sintáctico.** `receptor.verbo(fuente)`. Sin excepciones para "instrucciones especiales".
5. **Dos ritmos, una sintaxis.** Fluent para configuración y declaraciones; bloque para procedimientos. La gramática es la misma; cambia el contexto.
6. **El receptor manda en el IntelliSense.** `Gpr0.` sugiere operaciones de GPR; `Frf.` sugiere flags; `Ipr.` sugiere flujo; `Simd0.r512.part{32}.` sugiere operaciones por elemento.
7. **Sin nombres legacy.** `Rax`, `Eax`, `rFlags`, `XMM`, `CS`, `CR0`, `mov`, `ret` no existen en la superficie. Existen `Gpr0`, `Gpr0.low32`, `Frf`, `Simd0`, `Srf0`, `Crf0`, `copy`, `Ipr.flowStackJump()`.
8. **Sin temporales ocultos.** El compilador no crea registros ni memoria que el programador no haya declarado. Memoria→memoria exige un registro explícito.
9. **Sin optimización silenciosa.** La codificación es determinista: misma fuente + mismo target ⇒ mismos bytes. Las transformaciones opcionales se declaran y se documentan.
10. **Todo error tiene código.** Cada restricción del hardware y de la ABI tiene un diagnóstico estable (`FASMxxxx`), con sugerencia de corrección.

---

## 3. Cómo leer esta especificación

Los documentos están numerados en orden de lectura. Los marcados **N** son normativos (el compilador debe cumplirlos); los marcados **I** son informativos (explican, no obligan).

| # | Documento | Estado | Qué contiene |
|---|---|---|---|
| 00 | [DECISIONES](00-DECISIONES.md) | N | Registro de decisiones de diseño, con contexto y consecuencias |
| 01 | [FILOSOFÍA](01-FILOSOFIA.md) | N | Dataflow, prohibición de datos, honestidad física, modelo cognitivo |
| 02 | [MODELO ATM](02-MODELO-ATM.md) | N | `Bit:N`, `Byte:N`, múltiplos, capacidades, contextos, interpretación por verbo |
| 03 | [REGISTROS](03-REGISTROS.md) | N | Archivos `Gpr`, `Srf`, `Crf`, `Drf`, `Frf`, `Ipr`, `Simd`, `Krf`, `Xrf`, `Xcr`, `Pkru`, `Msr` |
| 04 | [SINTAXIS](04-SINTAXIS.md) | N | Léxico, patrón `destino.verbo(fuente)`, vistas, cadenas, condiciones, tuplas |
| 05 | [PROGRAMA](05-PROGRAMA.md) | N | `program`, `section`, `routine`, `parameter`, `local`, `data`, configuración |
| 06 | [LITERALES](06-LITERALES.md) | N | Qué literal es forma y qué literal es dato; política y bootstrap |
| 07 | [VERBOS](07-VERBOS.md) | N | Catálogo verbo → instrucción x64, flags, encoding, restricciones |
| 08 | [FLUJO](08-FLUJO.md) | N | `Ipr.flow*`, condiciones, bucles, llamadas, lejos, interrupciones |
| 09 | [MEMORIA](09-MEMORIA.md) | N | Direccionamiento, SIB, RIP-relativo, secciones, símbolos, alineación |
| 10 | [SIMD](10-SIMD.md) | N | `Simd0–31`, `.r128/.r256/.r512`, `.part{8/16/32/64}`, máscaras `Krf` |
| 11 | [x87](11-X87.md) | N | `Xrf0–7`, pila x87, precisión extendida, MMX prohibido |
| 12 | [ABI](12-ABI.md) | N | Win64 y System V, parámetros, retorno, prólogo/epílogo, alineación |
| 13 | [DIAGNÓSTICOS](13-DIAGNOSTICOS.md) | N | Registro completo de códigos, severidad y corrección |
| 14 | [GRAMÁTICA](14-GRAMATICA.md) | N | EBNF completa y decisiones de parsing |
| 15 | [EJEMPLOS](15-EJEMPLOS.md) | N | Programas completos, trazas y bytes esperados |
| 16 | [CODIFICACIÓN](16-CODIFICACION.md) | N | REX, ModRM, SIB, VEX, EVEX, relocaciones, imagen |
| 17 | [COMPILADOR](17-COMPILADOR.md) | N | Arquitectura, pipeline, proyectos .NET, milestones, pruebas |
| 18 | [INTELLISENSE](18-INTELLISENSE.md) | N | LSP, completado por receptor, hover, diagnóstico en vivo |
| 19 | [MIGRACIÓN](19-MIGRACION.md) | I | Tabla de sintaxis antigua → nueva y herramienta `fasm migrate` |
| 20 | [GLOSARIO](20-GLOSARIO.md) | I | Términos, nombres canónicos, alias prohibidos |

**Convención de palabras clave:** *debe* / *no debe* = obligatorio; *debería* = recomendado; *puede* = opcional. Los ejemplos usan `//` para comentarios y `...` para código omitido.

---

## 4. Ejemplo mínimo

```fasm
program hola.abi(win64).target(x64-avx2).entry(main) {

    section .rodata {
        data mensaje: Byte:10 = "FluentASM\n"
    }

    section .execute {
        routine main {
            Gpr0.loadAddress(mensaje)     // Gpr0 = dirección del mensaje
            Gpr1.copy(Byte:1 [mensaje])   // Gpr1 = primer byte (demostración)
            Ipr.flowStackJump()           // ret
        }
    }
}
```

No hay `42`, no hay `'A'`, no hay `"texto"` dentro de `.execute`. El único dato del programa vive en `.rodata` y entra en los registros mediante una **dirección** y una **capacidad** (`Byte:1`).

---

## 5. Ejemplo de los dos ritmos

```fasm
// ── Cabecera: fluent, declarativa ─────────────────────────────
routine procesar.abi(win64).frame(Byte:32).preserves(Gpr3, Gpr12) {
    parameter origen: Gpr
    parameter destino: Gpr
    parameter cantidad: Gpr
    local temporal: Byte:8

    // ── Cuerpo: bloque, una acción por línea ──────────────────
    Gpr0.copy(origen)
    Gpr0.add(Byte:8 [destino])
    Byte:8 [temporal].copy(Gpr0)

    Ipr.flowCall(@normalizar)
    Ipr.flowStackJump()

    @normalizar:
        Gpr0.shiftRightLogical(Bit:1)  // forma: cuenta de desplazamiento
        Ipr.flowStackJump()
}
```

La cabecera encadena configuración del mismo receptor (la rutina). El cuerpo ejecuta acciones físicas, una por línea. **Misma sintaxis, distinto contexto.**

---

## 6. Lo que el compilador debe poder hacer con esta especificación

Con los documentos 00–20, un equipo puede implementar:

1. **Lexer** con comentarios, literales estructurales, identificadores y símbolos (doc 04).
2. **Parser** completo del programa, secciones, rutinas, declaraciones y sentencias (doc 14).
3. **Checker ATM** de capacidades, vistas, contextos y clases de registro (docs 02, 03).
4. **Checker de política de literales**: forma vs dato (doc 06).
5. **Selector de instrucciones** verbo → operación física (doc 07).
6. **Encoder x64** con REX/ModRM/SIB/VEX/EVEX (doc 16).
7. **Linker de imagen plana** con secciones, símbolos y entrada (docs 05, 16).
8. **Validador ABI** y de pila (doc 12).
9. **Diagnósticos** estables y accionables (doc 13).
10. **LSP/IntelliSense** por tipo de receptor (doc 18).

La ruta de implementación, con milestones y criterios de aceptación, está en el documento [17-COMPILADOR](17-COMPILADOR.md).

---

## 7. Decisiones pendientes de confirmación

Estas cinco decisiones están **cerradas en la especificación con un valor por defecto**, pero se marcan para revisión humana porque cambian la superficie del lenguaje. Cada una tiene una recomendación y una alternativa.

| # | Decisión | Valor por defecto | Alternativa | Doc |
|---|---|---|---|---|
| D-1 | ¿Los datos pueden existir en `.rodata`/`.data` como *bootstrap*? | **Sí**, pero solo en secciones de datos; `.execute` los prohíbe | Prohibirlos también en datos: todo dato entra por recurso externo/hardware | 06 |
| D-2 | Nombre de los registros de máscara AVX-512 | **`Krf0`–`Krf7`** (K Register File) | `Mrf0`–`Mrf7` (Mask Register File) | 03, 10 |
| D-3 | Ancho por defecto de `SimdN` | **`.r512`** (el contenedor físico es de 512 bits); target debe soportar AVX-512 | Exigir siempre `.r128/.r256/.r512` explícito | 10 |
| D-4 | Sintaxis del programa | **Llaves `{}`** + configuración encadenada en la cabecera | `program ... end` (estilo de los documentos actuales) | 05 |
| D-5 | ABI por defecto | **Win64** | System V | 12 |

> Si apruebas estos valores, la especificación queda cerrada para la versión 1.0. Si cambias alguno, solo hay que actualizar el documento indicado y sus referencias cruzadas; el resto del modelo no depende de ellos.

---

## 8. Relación con los documentos existentes

Este directorio **no borra** ni sustituye los archivos actuales del repositorio. Los complementa y, en los puntos donde hay conflicto, esta especificación es la referencia para el compilador nuevo.

| Documento existente | Relación |
|---|---|
| `FLUENT_SYNTAX.md` | Sintaxis común anterior. Será reemplazado por 04–05 + 14. |
| `FLUENT_SYNTAX_X64.md` | Referencia x64 anterior. Su contenido físico se conserva en 03, 07, 09, 10, 11, 16; su sintaxis se actualiza. |
| `CONTRIBUTING.md` | Convenciones de contribución, pipeline y fases. La ruta del compilador se concreta en 17. |
| `proposal-0001` … `proposal-0023` | Propuestas históricas. Las decisiones vigentes se consolidan en 00 y en los documentos normativos. |
| `README.md` | Descripción del proyecto. Se actualizará cuando esta especificación se apruebe. |

La tabla completa de migración está en [19-MIGRACIÓN](19-MIGRACION.md).

---

## 9. Estado y siguiente paso

- **Especificación:** completa para empezar el compilador (lexer → parser → checker → encoder → linker).
- **Pendiente:** confirmación de D-1…D-5 y revisión de estilo de los ejemplos.
- **Siguiente paso técnico:** implementar el milestone M0 (lexer + parser + AST) descrito en [17-COMPILADOR](17-COMPILADOR.md), usando los *golden files* de [15-EJEMPLOS](15-EJEMPLOS.md) como primera suite de pruebas.

---

*FluentASM no manipula valores: describe rutas por donde circulan bits. El programa no incrusta datos en instrucciones; los datos entran por direcciones y capacidades declaradas.*
