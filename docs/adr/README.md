# Registro de decisiones de arquitectura (ADR)

Cada decisión de diseño relevante de FluentASM se documenta aquí con formato corto:
contexto, decisión, consecuencias y alternativas descartadas.

El objetivo es que cualquiera pueda entender **por qué** el proyecto es como es sin
tener que arqueología de commits ni de conversaciones, y que las decisiones que
aceptan un coste conocido lo hagan de forma explícita.

## Estado

| Estado | Significado |
|---|---|
| Propuesta | En discusión, todavía no obliga |
| **Aceptada** | Vigente y obligatoria para el código |
| Reemplazada por ADR-XXXX | Ya no aplica; se conserva por historial |
| Rechazada | Se evaluó y se descartó (se conserva el motivo) |

## Índice

| ADR | Título | Estado |
|---|---|---|
| [0001](0001-nombres-fasm.md) | Se mantienen los nombres `.fasm` y `fasm_admin` | Aceptada |
| [0002](0002-fproj-binario.md) | `.fproj` y `.fworkspace` estrictamente binarios | Aceptada |
| [0003](0003-salidas-imagen-plana.md) | Salidas: imagen plana primero, ejecutable del SO después | Aceptada |
| [0004](0004-superficie-canonica.md) | La superficie canónica es el DSL `.fasm` | Aceptada |
| [0005](0005-modelo-de-ejecucion.md) | Modelo de ejecución y ABI de la Fase 1 | Aceptada |

## Cómo agregar un ADR

1. Abre un issue de tipo **decisión** describiendo el problema.
2. Copia el formato del ADR más reciente y numera el siguiente consecutivo.
3. Envíalo por pull request. Al fusionarse, el estado pasa a **Aceptada**.
4. Si una decisión cambia, **no** edites el ADR antiguo: escribe uno nuevo que lo
   reemplace y actualiza la tabla del índice.
