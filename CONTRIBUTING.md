# Contribución

Gracias por interesarte en contribuir a FluentASM. Este proyecto está diseñado para ser accesible, claro y transparente, especialmente si no tienes experiencia previa con compiladores o ensambladores.

## Requisitos previos

Para trabajar en FluentASM necesitas:

| Herramienta | Versión | Para qué |
|---|---|---|
| .NET SDK | 10.0 (o superior) | Compilar y probar el compilador en Phase 1 |
| Git | cualquier reciente | Control de versiones |
| Editor | VS 2022 / VS Code / Rider | Escribir código |
| QEMU | reciente (recomendado) | Ejecutar y probar binarios x64 *bare metal* |
| NASM | 2.16+ | Referencia opcional para verificar bytes x64 |

Siempre usamos la versión más reciente estable de cada herramienta; no se pinchan versiones antiguas.

## Cómo contribuir

1. **Bifurca el repositorio** (haz un *fork*) en GitHub.
2. **Crea una rama nueva** a partir de `develop`, basada en el issue que vas a resolver: `git checkout -b nombre-de-la-rama develop`
3. **Haz tus cambios** y commitea con mensajes claros que expliquen qué hiciste y por qué.
4. **Sube tu rama** al repositorio: `git push origin nombre-de-la-rama`
5. **Abre un Pull Request** para fusionar tu rama en `develop`.

## Primeros pasos en el repositorio

1. Clona tu fork: `git clone https://github.com/<tu-usuario>/FluentASM.git`
2. Entra al directorio: `cd FluentASM`
3. Verifica que compila: `dotnet build src/FluentASM.slnx`
4. Crea o abre un proyecto de prueba en `src/<nombreProyecto>/` con su archivo `<nombreProyecto>.fproj` (texto legible en Fase 1) y su fuente `sourcesCode.fasm`
5. Ensámblalo con el compilador (`fluentasm`) y pruébalo con el lanzador de pruebas o QEMU
6. Trabaja en una rama propia creada desde `develop`, nunca directamente en `main` ni en `develop`

El repositorio utiliza únicamente dos ramas principales:

| Rama | Propósito |
|---|---|
| `main` | Versión estable del proyecto. Solo recibe código probado y listo para producción. |
| `develop` | Rama de integración. Aquí se fusionan todas las funcionalidades nuevas antes de pasar a `main`. |

Nunca hagas commits directamente en `main` ni en `develop`. Siempre trabaja en una rama propia creada desde `develop`.

## Proceso de trabajo

### Issues (tickets)
Usa los issues para reportar errores, proponer nuevas características o hacer preguntas. Antes de crear uno, busca si ya existe un issue similar para evitar duplicados.

Cada trabajo parte de un issue. La rama de trabajo debe nombrarse haciendo referencia al issue, por ejemplo: `issue-123-mejorar-parser`.

### Pull Requests
Todo cambio debe pasar por un Pull Request dirigido a la rama `develop`.

Al menos una persona del equipo debe revisarlo y aprobarlo antes de fusionarse.

#### Plantilla del PR
El cuerpo del Pull Request debe incluir una **sugerencia de versión** en el siguiente formato:

```
## Resumen de cambios
[Describe brevemente qué se hizo]

## Issue relacionado
closes #[número del issue]

## Sugerencia de versión
x.x.x-preview
```

Si cambia una decisión de diseño, incluye o enlaza la sección correspondiente de "Decisiones de diseño" en este documento.

La versión de la sugerencia es una propuesta; la fuente de verdad del número de versión se explica en la sección de versionado.

## Versionado

- Cada merge a la rama `develop` genera una versión preliminar de tipo `x.x.x-preview`.
- Cuando se hace merge de `develop` a `main`, se genera una versión estable (release) de tipo `x.x.x`, sin el sufijo `-preview`.
- Se sigue el versionado SemVer: `MAJOR.MINOR.PATCH`.

**Fuente de verdad del número de versión:** un archivo de **texto** versionado en el repositorio (`VERSION` y/o la propiedad de versión en `Directory.Build.props`). GitHub Actions lee ese archivo para saber qué versión se está publicando.

## Estándares de código

El proyecto sigue convenciones consistentes tanto en el código C# del compilador como en el código `.fasm`:

- **Registros y unidades del lenguaje:** `PascalCase` (`Gpr0`, `Frf`, `Ipr`, `Stack`, `Cpu`, `Bit`, `Byte`).
- **Verbos, atributos y vistas:** `camelCase` (`copy`, `add`, `low32`, `flowCall`, `flowStackJump`).
- **Identificadores de usuario (`.fasm`) y variables locales (C#):** `camelCase` (`miCalculadoraCli`, `numA`, `sumar`, `valorOrigen`).
- **Etiquetas locales (`.fasm`):** `@` + `camelCase` (`@bucle`, `@fin`).
- **Comentarios:** `//` hasta fin de línea o `/* ... */` en bloque.
- **No uses abreviaturas crípticas.** `registroDestino` es mejor que `rd`.
- **Fuente única de nombres:** los registros, verbos y reglas del lenguaje se toman siempre de [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md) (común) y [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md) (x64); no se inventan alias.
- `.editorconfig` en la raíz es obligatorio y las advertencias se tratan como errores en la integración continua.

**Verbos y elementos canónicos de Fase 1 (x64 *bare metal*):**

| Categoría | Nombres en FluentASM |
|---|---|
| Transferencia y memoria | `copy`, `copyUnsigned`, `copySigned`, `loadAddress`, `exchange`, `conditionalCopy`, `copyBlock`, `fillBlock` |
| Aritmética | `add`, `sub`, `addWithCarry`, `subWithBorrow`, `increment`, `decrement`, `negate`, `multiply`, `multiplyWide`, `divide`, `divideSigned`, `signExtend`, `compare` |
| Lógica y bits | `and`, `or`, `xor`, `not`, `test`, `shiftLeft`, `shiftRightLogical`, `shiftRightArithmetic`, `rotateLeft`, `rotateRight`, `testBit`, `setBit`, `clearBit`, `toggleBit` |
| Banderas (`Frf`) | `carry`, `direction`, `interrupt`, condiciones (`zero`, `carry`, `sign`, `overflow`, `parity`, y alias legibles `equal`, `notEqual`, `less`, `greater`, `below`, `above`) |
| Pila (`Stack`) | `Stack.push`, `pop(Stack)`, `Stack.reserve`, `Stack.release` |
| Control de flujo (`Ipr`) | `flowJump`, `flowCall`, `flowStackJump`, `flowLoop` |
| Hardware (*bare metal*) | `Cpu.noOperation`, `Cpu.pause`, `Cpu.halt`, `Port[...]` |

**Nombres prohibidos (legacy o descartados):** `Rax`, `Eax`, `Rcx`–`R15`, `RIP`, `rFlags`/`RFLAGS`, `Mov`, `Alu`/`.alu`, `subNoWrite`, `andNoWrite`, `Gpr.mult<2,0>`, `Push`, `Pop`, `stackWrite`, `stackRead`, `Jmp`, `Je`–`Jnp`, `Call`, `Ret`, `Nop`, `Hlt`, `Movzx`, `Movsx`, `Lea`.

## Estructura del código fuente (`src/`)

**No existen proyectos de pruebas formales (`*Tests.csproj`) ni generación de tests automáticos.**
La validación se realiza compilando y ejecutando directamente ejemplos reales en `FluentASM.Sample` (y/o en QEMU *bare metal*). `FluentASM.Sample` varía libremente con cada función y no entra en el versionado.

```
src/
  FluentASM.Core/           → Lexer, parser, validador Tipo 1 / ATM, codificador x64 y emisor de imagen plana
  FluentASM.Compiler/       → CLI (fluentasm): lee .fproj y sourcesCode.fasm y genera la salida .x64
  FluentASM.Sample/         → Proyecto de pruebas manuales (no entra en versionado)
  miCalculadoraCli/         → Programa de ejemplo de Fase 1 (.fproj + sourcesCode.fasm)
  FluentASM.Admin/          → Administrador binario futuro (fasm_admin) — diferido a fases posteriores
  FluentASM.IDE/            → Entorno de desarrollo integrado — diferido a fases posteriores
```

La solución .NET de Fase 1 es `src/FluentASM.slnx`; la versión se publica desde `VERSION` (raíz) a través de `src/Directory.Build.props`.

Las carpetas `Proposal00/` y `Proposal01/` en la raíz se conservan únicamente como archivo histórico de diseño.


## Visión del proyecto

### Qué es FluentASM

FluentASM es un lenguaje ensamblador moderno, práctico, seguro, eficiente y cómodo. Combina una estructura **fluida guiada por contexto (*type-state*)** con **bloques `{}` legibles**, midiendo toda información físicamente en **`Bit:N`** y **`Byte:N`** y traduciendo directamente a **código máquina nativo** sin máquina virtual ni *runtime*.

### Principios rectores

1. **Claridad y honestidad física.** Prefiere `Gpr0.copy(Gpr1)` sobre `mov rax, rcx`. Todo constructo refleja lo que ocurre físicamente en la CPU.
2. **Estructura *type-state* + bloques legibles.** La cabecera del programa sigue un flujo guiado por el nombre del proyecto (`Proyecto.data(...).execute(...).initialize()`), con un argumento por método fluido (comas solo en listas homogéneas). Adentro, bloques `{}` claros: sin `()` señala estado/ventana (`Gpr0.low32`), con `()` ejecuta una instrucción real (`Gpr0.copy(...)`).
3. **Un solo modelo físico (`Bit` / `Byte`).** Sin abstracciones como `int`, `float`, `string` o `struct`.
4. **Principio de Garantía de Completitud y cero datos duros en ejecución.** Las plantillas aportan valores predeterminados explícitos. Dentro de `.execute(...)` no se escriben datos literales mágicos: el estado base limpio se expresa con `defaultValue`, y los valores reales viven en `.data(...)` o recursos externos.
5. **Seguridad Tipo 1 y eficiencia.** Cero código oculto en tiempo de ejecución. El compilador valida tamaños, vistas, banderas válidas y contratos de llamada antes de emitir bytes.
6. **Libertad *bare metal* y evolución multi-CPU.** Se diseña libre de ataduras a sistemas operativos existentes, implementando primero x64 real y extrayendo el estándar multi-arquitectura al sumar una segunda CPU.

### Qué FluentASM NO es

- No es un lenguaje de alto nivel con recolección de basura ni tipos abstractos.
- No es un compilador optimizador que reordene o invente código oculto a espaldas del programador.
- No es una máquina virtual ni requiere *runtime* instalado.

**Principio fundamental:** nada en FluentASM es una "caja negra". Todo se reduce a instrucciones y bytes visibles.


## Modelo de ejecución y convenciones (Fase 1)

### Objetivo de Fase 1: x64 *Bare Metal*

- **Salida:** imagen plana (`.x64`, bytes puros sin cabecera PE/ELF), independiente de posición (PIC), ejecutable en arrancador en memoria o emulador QEMU.
- **Sin OS previo:** el lenguaje no impone convenciones de Windows ni Linux como obligatorias en el diseño, aunque adopta un mapeo físico determinista y claro para rutinas.

### Convención de registros y llamadas en Fase 1

| Concepto | Registro físico asignado |
|---|---|
| Entrada 1 | `Gpr1` |
| Entrada 2 | `Gpr2` |
| Entrada 3 | `Gpr8` |
| Entrada 4 | `Gpr9` |
| Retorno principal | `Gpr0` |
| Puntero de pila | `Gpr4` (`Stack`) |
| Puntero de marco | `Gpr5` |

### Contratos de llamada y pila

- Las llamadas se escriben con contrato visible: `Ipr.flowCall(c -> c.target(rutina).inputs(...).output(...))` (o `Ipr.flowCall(rutina)` si no tiene entradas ni salida).
- Al salir de `flowCall`, el compilador **invalida lógicamente** los registros usados como entrada para evitar lecturas accidentales (sin emitir instrucciones ocultas).
- Toda rutina declara su retorno (`d.returns(...)`), recibe alias tipados en `d.run(...)` si tiene entradas, y cierra explícitamente con `Ipr.flowStackJump()` o `Cpu.halt()`.
- La pila crece hacia abajo y debe estar alineada a 16 bytes al ejecutar `Ipr.flowCall(...)`.


## Pipeline del compilador y Diagnósticos (Fase 1)

### Etapas del proceso

```
.fproj + sourcesCode.fasm → Léxico/Sintáctico → Validador Tipo 1 y ATM → Selección de verbos → Layout → Codificador x64 → Imagen .x64
```

| Etapa | Invariante al terminar |
|---|---|
| Léxico y Sintáctico | Árbol sintáctico válido siguiendo el flujo *type-state* y bloques |
| Validador Tipo 1 / ATM | Sin datos duros en `.execute`, anchos `Bit`/`Byte` consistentes, contratos `flowCall` y banderas `Frf` verificados |
| Layout | Cada rutina y símbolo tiene `Offset`; saltos `rel8`/`rel32` convergidos de forma determinista |
| Codificación y Emisión | Codificación más corta determinista; todas las relocalizaciones resueltas |

### Rangos de diagnóstico (v1)

| Rango | Etapa |
|---|---|
| `FASM1xxx` | Léxico y sintáctico |
| `FASM2xxx` | Programa, símbolos, secciones y datos duros en `.execute` |
| `FASM3xxx` | ATM (`Bit`/`Byte`), registros, vistas y banderas |
| `FASM4xxx` | Operandos, direccionamiento, verbos y flujo |
| `FASM5xxx` | Contratos de llamada (`flowCall`), pila y alineación |
| `FASM9xxx` | Errores internos del compilador |


## Archivos de proyecto y evolución del ecosistema

### Fase 1 (Texto simple legible)

Cada proyecto vive en su carpeta `src/<nombreProyecto>/` y contiene:
- `<nombreProyecto>.fproj`: archivo de configuración en **texto simple legible** (nombre del proyecto, arquitectura `x64`, archivos fuente, punto de entrada).
- `sourcesCode.fasm`: código fuente cuyo identificador raíz coincide con el nombre definido en el `.fproj`.

Cuando hay múltiples proyectos en una solución, se agrupan mediante un `.fworkspace` también en texto simple en Fase 1.

### Fases posteriores (Ecosistema completo)

Una vez que el compilador de Fase 1 genere y ejecute binarios reales, se incorporarán de forma incremental:
- Formatos binarios indexados para `.fproj` y `.fworkspace` junto con el CLI `fasm_admin`.
- Contenedores `.bin` ("Partes") para reutilización binaria y comunicación entre proyectos.
- Catálogo multilingüe `fasm.fdoc` para documentación de símbolos.
- Entorno de desarrollo integrado (`FluentASM.IDE`).


## Roadmap

| Fase | Objetivo |
|---|---|
| **1 (Actual)** | Compilador en .NET (`.fproj` simple, léxico, parser, validador Tipo 1, codificador x64 mínimo viable) + imagen plana `.x64` ejecutada en lanzador de pruebas / QEMU *bare metal* |
| **2** | Ampliación de x64 (memoria avanzada, bloques, utilidades de arranque en QEMU) y comunicación entre binarios / Partes (`.bin`) |
| **3** | Segunda arquitectura real (ARM64 / RISC-V en QEMU) y extracción del estándar común multi-CPU basado en implementaciones reales |
| **4** | Formatos binarios del ecosistema (`.fproj`, `.fworkspace`, `fasm_admin`, `fasm.fdoc`) y extensiones x64 avanzadas (SIMD, x87, sistema/paginación) |
| **5** | Ejecutables de sistemas operativos (PE/ELF) si se requieren y entorno de desarrollo (`FluentASM.IDE`) |
| **6** | Auto-albergue (*self-hosting*) y operación independiente sin OS previo |


## Vocabulario prescriptivo

### Reglas de diseño

1. **Un concepto, un nombre.** Los sinónimos antiguos se rechazan como legacy.
2. **Definir por lo que hace físicamente.** Entradas, salidas y efecto observable.
3. **Anchos siempre explícitos.** `Bit:N` o `Byte:N`; nunca "palabra" ni "entero" a secas.
4. **Sin `()` = parte o estado; con `()` = acción ejecutada.**


## Decisiones de diseño

Una decisión que afecte a más de un archivo (formato, sintaxis, arquitectura de código, compatibilidad) se registra aquí en `CONTRIBUTING.md`. Las decisiones no se editan: si una cambia, se escribe una nueva que la reemplaza.

Para debates internos, usa documentos locales con prefijos `proposal-`, `idea-` o `plan-`. Estos **no se suben al origen**.


### Decisión 0007 — Pacto de Diseño v1: *Type-State* fluido, `Bit`/`Byte`, Seguridad Tipo 1 y x64 *Bare Metal* (2026-11-17)

*(Reemplaza la Decisión 0006 y resuelve las contradicciones entre `Proposal00` y `Proposal01`).*

**Contexto:** Las especificaciones iniciales (`Proposal00`) y la propuesta extendida (`Proposal01`) presentaban tensiones entre ergonomía, reglas extremas sin datos literales, sintaxis incompatibles (`.alu` vs `destino.verbo`, `program ... end` vs constructor fluido con nombre de proyecto) y un alcance inicial inabarcable (`.fproj` binario, `fasm.fdoc`, SIMD/VMX/IDE desde Fase 1).

**Decisión:**
1. **Estructura:** Raíz igual al nombre del proyecto (`.fproj`), flujo *type-state* (`Proyecto.data(...).execute(...).initialize()`) con bloques `{}` y un argumento por método fluido (comas solo en listas homogéneas).
2. **Modelo de datos:** Solo `Bit:N` y `Byte:N` (+ múltiplos). Sin datos duros en `.execute(...)`; se usa `defaultValue` para el estado limpio en código y `.data(...)` (`.value(...)` o recursos) para valores concretos.
3. **Contratos y llamadas:** Verbo unificado `destino.verbo(fuente)` y llamadas visibles `Ipr.flowCall(c -> c.target(...).inputs(...).output(...))` con invalidación lógica de entradas al retornar.
4. **Alcance Fase 1:** x64 *bare metal* (incluyendo `Cpu.halt()`, `Frf.interrupt()` y `Port[...]`), `.fproj`/`.fworkspace` en texto simple legible, diferiendo binarios de producto, `fasm_admin`, `fasm.fdoc`, IDE, SIMD/x87 y multi-CPU a fases posteriores.

**Consecuencias:**
- **Ganancia:** Lenguaje coherente, predecible, seguro en tiempo de compilación (Tipo 1), alineado con el flujo creativo *type-state* y viable para implementar en .NET desde el día uno.
- **Pérdida:** Rompe compatibilidad con sintaxis de borradores previos (`.alu`, `.copy(... to ...)`, `program ... end`, `Rax`).
- **Mitigación:** `Proposal00/` y `Proposal01/` quedan como archivo histórico; los 4 documentos oficiales de raíz se alinean bajo esta única especificación.


### Decisión 0008 — Formato `.fproj` v1 en texto simple legible (2026-11-17)

**Contexto:** El Pacto v1 exige `.fproj` en texto simple legible para Fase 1, pero no fija su gramática. Sin un formato único, el CLI, el ejemplo `miCalculadoraCli` y las pruebas no podrían validarse de extremo a extremo.

**Decisión:**
1. **Texto UTF-8** con una asignación `clave = valor` por línea. Se admiten líneas en blanco, comentarios `//` hasta fin de línea y valores entre comillas dobles cuando contienen espacios. Las claves son sensibles a mayúsculas y minúsculas.
2. **Claves canónicas:** `name` (obligatoria), `architecture` (por defecto `x64`), `entryPoint` (por defecto `main`), `source` (obligatoria y repetible, una por archivo fuente) y `output` (por defecto `<name>.x64`, solo nombre de archivo).
3. **Identidad:** `name` es un identificador de usuario `camelCase` y debe coincidir exactamente con el nombre del archivo `<name>.fproj`.
4. **Diagnósticos:** la lectura del `.fproj` se reporta con el rango `FASM2xxx` (`FASM2001` archivo ausente o ilegible, `FASM2002` sintaxis o clave desconocida, `FASM2003` campo obligatorio ausente, `FASM2004` campo o fuente duplicada, `FASM2005` arquitectura no soportada, `FASM2006` identificador inválido, `FASM2008` fuente inexistente, `FASM2009` nombre distinto al archivo).
5. **Diferido:** los formatos binarios indexados, `.fworkspace` y `fasm_admin` siguen reservados para fases posteriores.

**Consecuencias:**
- **Ganancia:** El proyecto es legible, diffeable y parseable sin dependencias externas; los valores predeterminados quedan documentados bajo el Principio de Garantía de Completitud.
- **Pérdida:** No se admiten comentarios de bloque ni estructuras anidadas en `.fproj` v1.
- **Mitigación:** Si el formato crece, se escribirá una decisión nueva que reemplace esta.


## CI-CD (Integración y Despliegue Continuos)

En FluentASM usamos GitHub Actions para la CI a partir de que exista código en `src/`. Cada Pull Request:

1. Compila todos los proyectos activos de `src/` con el SDK de .NET del repositorio.
2. Ensambla y valida los ejemplos de `rsc/` de extremo a extremo.
3. Trata las advertencias como errores y falla si algún archivo no respeta `.editorconfig`.

Cada merge a `develop` genera automáticamente una versión `x.x.x-preview` leyendo la versión del archivo de texto del repositorio (`VERSION` / `Directory.Build.props`).

## Reportar errores

Si encuentras un error, abre un issue con la siguiente información:
1. Qué esperabas que pasara.
2. Qué pasó en realidad.
3. El código que usaste (fragmento de FluentASM y su `.fproj` si aplica).
4. Tu versión del SDK de .NET.

## Licencia

Al contribuir, aceptas que tus contribuciones se liberen bajo la misma licencia del proyecto.
