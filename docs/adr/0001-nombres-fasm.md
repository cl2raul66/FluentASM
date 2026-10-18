# ADR 0001 — Se mantienen los nombres `.fasm` y `fasm_admin`

- **Estado:** Aceptada
- **Fecha:** 2026-10-18
- **Decide:** mantenedor del proyecto

## Contexto

FluentASM usa la extensión `.fasm` para el código fuente y `fasm_admin` como nombre del CLI
administrador de proyectos. Ambos colisionan con **FASM (*flat assembler*)**, un ensamblador x86
publicado en 1999, aún en desarrollo activo, cuyo ejecutable se llama `fasm`, cuyo paquete de editor
integrado se distribuye a menudo con la extensión `.fasm`, y que además produce **imagen plana**,
PE, ELF y COFF igual que queremos hacer nosotros.

La colisión no es teórica:

- Búsquedas de `.fasm` y `fasm` devuelven mayoritariamente material de FASM.
- Los editores asocian `.fasm` con su resaltado de sintaxis, que **no** es nuestra sintaxis fluida.
- En una máquina con FASM instalado, escribir `fasm` en la terminal no invoca FluentASM.

## Decisión

Se **mantienen** los nombres `.fasm` (fuente), `.fproj`, `.fworkspace`, y `fasm_admin` (CLI).
Los ejecutables propios se llamarán **`fluentasm`** (ensamblador) y **`fasm_admin`** (administrador),
evitando deliberadamente el nombre desnudo `fasm`.

Se convive con la ambigüedad mediante documentación explícita y no mediante renombrado.

## Consecuencias

### Positivas

- No se cambia la marca ni el vocabulario que ya usan la visión, la sintaxis y el roadmap.
- Cero retrabajo en documentos y en los ejemplos ya escritos.
- `fasm_admin` no colisiona con nada de FASM al ser un nombre compuesto.

### Negativas y mitigaciones

| Riesgo | Mitigación obligatoria |
|---|---|
| Un editor abre un `.fasm` de FluentASM con resaltado de FASM y lo muestra mal | Documentar en `README.md` y `FLUENT_SYNTAX.md` que un `.fasm` de FluentASM **no** es un `.fasm` de FASM; publicar la extensión/gramática para editores en la fase del IDE |
| Confusión en búsquedas y conversaciones | El nombre visible del lenguaje es siempre **FluentASM**, nunca "fasm" a secas |
| Un script de terceros invoca `fasm` esperando FluentASM | El binario nunca se llama `fasm`; se documenta `fluentasm` como el único nombre del ensamblador |
| Un `.fasm` de terceros (FASM) se pasa a FluentASM | El ensamblador debe fallar con un error de sintaxis claro y un mensaje que mencione la posibilidad de que el archivo sea de FASM |

## Alternativas consideradas

1. **Renombrar la extensión a `.fluent` / `.flasm` y el CLI a `fluent`.** Mejor aislamiento, pero
   rompe toda la documentación y la identidad ya publicada, y el coste de la ambigüedad es bajo
   mientras el proyecto sea de un solo autor y no publique releases.
2. **Aceptar un modo de compatibilidad de entrada** que lea además sintaxis Intel/NASM clásica
   (útil para migrar `WinUIAsmCtrl`). No se descarta: se anota como posible entrada adicional en
   una fase posterior, no como sustituto de la extensión.
