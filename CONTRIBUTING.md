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
4. Para probar el IDE, despliega o instala su paquete MSIX y abre una carpeta de proyecto con `fluentasm-ide.exe "C:\ruta\miProyecto"`
5. Opcionalmente, instala la entrada de Windows Explorer para carpetas con `.\rsc\FluentASM-MenuContextual.ps1 -i`; usa `-u` para desinstalarla
6. Crea o abre un proyecto de prueba en `src/<nombreProyecto>/` con su archivo `<nombreProyecto>.fproj` (texto legible en Fase 1) y su fuente `sourcesCode.fasm`
7. Ensámblalo con el compilador (`fluentasm`) y pruébalo con el lanzador de pruebas o QEMU
8. Trabaja en una rama propia creada desde `develop`, nunca directamente en `main` ni en `develop`

El IDE y el LSP se validan mediante uso diario, revisión de sus diagnósticos, hover, símbolos y acciones rápidas, y el registro estructurado de sucesos. No crees proyectos `*Tests.csproj` para esta fase.

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
Usa la plantilla de `.github/PULL_REQUEST_TEMPLATE.md`. El cuerpo del Pull Request debe declarar las **versiones a publicar**, una línea por proyecto publicable:

```
## Versiones a publicar
ver(core): x.x.x-preview
ver(compiler): x.x.x-preview
ver(lsp): x.x.x-preview
ver(ide): x.x.x-preview
```

Solo se publican los proyectos cuya línea tenga una versión concreta (por ejemplo `ver(core): 0.2.0-preview`). Los que queden con el marcador `x.x.x-preview` no se publican; si no quieres publicar ninguno, deja las cuatro líneas como están. `FluentASM.Sample` no entra en el versionado: no lleva línea de versión ni se publica en releases.

Si cambia una decisión de diseño, incluye o enlaza la sección correspondiente de "Decisiones de diseño" en este documento.

El número declarado en el PR es la fuente de verdad para esa publicación; el detalle está en la sección de versionado.

**Valores sugeridos en cada `push`:** todo `push` de una rama de trabajo debe acompañarse de una propuesta de versiones para el PR, con este formato exacto:

```
Valores sugeridos
ver(core): x.x.x-preview
ver(compiler): x.x.x-preview
ver(lsp): x.x.x-preview
ver(ide): x.x.x-preview
```

La propuesta se calcula con `git tag` y `gh release list` (última versión publicada de cada proyecto) aplicando la regla de "solo se publica una versión estrictamente mayor"; los proyectos sin cambios en la rama conservan el marcador `x.x.x-preview`. Incluye además el motivo breve de cada número (funcionalidad nueva → MINOR, corrección → PATCH, primer release → versión inicial acordada). Así el autor conoce de antemano qué declarar en el PR y el workflow `dev-release.yml` no publica nada por sorpresa.

## Versionado

Cada proyecto publicable se versiona por separado: `core` (`FluentASM.Core`), `compiler` (`FluentASM.Compiler`), `lsp` (`FluentASM.Lsp`) e `ide` (`FluentASM_IDE`). Se sigue el versionado SemVer `MAJOR.MINOR.PATCH`.

| Merge | Versión publicada | Etiquetas | Línea necesaria en el PR |
|---|---|---|---|
| PR → `develop` | `x.x.x-preview` (prerelease) | `core-vx.x.x-preview`, `compiler-vx.x.x-preview`, `lsp-vx.x.x-preview`, `ide-vx.x.x-preview` | `ver(proyecto): x.x.x-preview` con números |
| PR → `main` | `x.x.x` (estable, sin sufijo) | `core-vx.x.x`, `compiler-vx.x.x`, `lsp-vx.x.x`, `ide-vx.x.x` | igual: `ver(proyecto): x.x.x-preview` |

- En los dos flujos el PR se escribe igual (`x.x.x-preview`); al mergear en `main` se publica la misma versión sin el sufijo `-preview`. El PR de promoción `develop` → `main` debe llevar **las mismas líneas `ver(...)`** del ciclo que se está promoviendo.
- Un proyecto no se publica si su línea conserva el marcador `x.x.x-preview` o si no aparece en el PR.
- **Solo se publica una versión estrictamente mayor que la última publicada.** Si la versión del PR es igual o menor, el flujo termina sin publicar y sin fallar (`skip`), dejando constancia en el registro del job.
  - En `develop` la comparación es contra la última *prerelease* del proyecto (`core-v*-preview`, `compiler-v*-preview`, `lsp-v*-preview`, `ide-v*-preview`).
  - En `main` la comparación es contra la última estable (`core-v*`, `compiler-v*`, `lsp-v*`, `ide-v*` sin sufijo).
- Cada publicación adjunta un ZIP `FluentASM.<Proyecto>-v<versión>.zip` con la salida de `dotnet publish -c Release` de ese proyecto.
- `FluentASM.Sample` **queda fuera del versionado**: no recibe versión propia, no genera ZIP ni release. Eso no lo excluye del repositorio: sus cambios se suben al origen (commit y push) como cualquier otro archivo, sin modificar `VERSION` ni las versiones de los proyectos.

**Fuente de verdad del número de versión:** las líneas `ver(...)` del cuerpo del PR en el momento del merge. El flujo escribe esa versión en `<Version>` del `.csproj` **solo en el runner** para publicar; no se commitea nada de vuelta. El archivo `VERSION` (raíz, leído por `src/Directory.Build.props`) es solo el valor por defecto de las compilaciones locales: no decide ninguna publicación.

**Ejemplo de ciclo completo:**

1. PR de `feature/x` → `develop` con `ver(core): 0.2.0-preview` → se publica `core-v0.2.0-preview`.
2. Otro PR a `develop` que vuelve a declarar `ver(core): 0.2.0-preview` → se salta (no es mayor).
3. PR de `develop` → `main` con `ver(core): 0.2.0-preview` → se publica `core-v0.2.0` (estable).
4. PR con `ver(lsp): 0.1.0-preview` y `ver(ide): 1.0.0-preview` → se publican `lsp-v0.1.0-preview` e `ide-v1.0.0-preview`.
5. PR de `develop` → `main` con `ver(lsp): 0.1.0-preview` y `ver(ide): 1.0.0-preview` → se publican `lsp-v0.1.0` e `ide-v1.0.0`.

## Estándares de código

El proyecto sigue convenciones consistentes tanto en el código C# del compilador como en el código `.fasm`:

- **Registros y unidades del lenguaje:** `PascalCase` (`Gpr0`, `Frf`, `Ipr`, `Stack`, `Cpu`, `Bit`, `Byte`).
- **Verbos, atributos y vistas:** `camelCase` (`copy`, `add`, `low32`, `flowCall`, `flowStackJump`).
- **Identificadores de usuario (`.fasm`) y variables locales (C#):** `camelCase` (`miCalculadoraCli`, `numA`, `sumar`, `valorOrigen`).
- **Etiquetas locales (`.fasm`):** `@` + `camelCase` (`@bucle`, `@fin`).
- **Comentarios:** `//` hasta fin de línea o `/* ... */` en bloque.
- **No uses abreviaturas crípticas.** `registroDestino` es mejor que `rd`.
- **Fuente única de nombres:** los registros, verbos y reglas del lenguaje se toman siempre de [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md) (común) y [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md) (x64); no se inventan alias.
- `.editorconfig` en la raíz es obligatorio y las advertencias se tratan como errores (`TreatWarningsAsErrors`) en la compilación local y en la publicación.
- En `FluentASM_IDE`, toda nueva llamada P/Invoke debe añadirse en `Helpers/NativeMethods.cs` mediante `[LibraryImport]` (no usar `[DllImport]` dispersos en servicios o controles).

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
La validación del compilador se realiza compilando y ejecutando directamente ejemplos reales en `FluentASM.Sample` (y/o en QEMU *bare metal*). `FluentASM.Sample` varía libremente con cada función: no entra en el versionado ni se publica como release, pero sus cambios se suben al origen sin modificar las versiones.

```
src/
  FluentASM.Core/           → Lexer, parser, validador Tipo 1 / ATM, codificador x64, emisor de imagen plana y LanguageCatalog de solo lectura
  FluentASM.Compiler/       → CLI (fluentasm): lee .fproj y sourcesCode.fasm y genera la salida .x64
  FluentASM.Lsp/            → Servicio de lenguaje en proceso (ILanguageService): diagnósticos, resaltado, completado, hover, símbolos y acciones rápidas; no depende de WinUI
  FluentASM_IDE.Common/     → Utilidades compartidas de la capa IDE (normalización de texto)
  FluentASM_IDE/            → Banco de trabajo WinUI 3: abre y edita la fuente de un proyecto y ofrece asistencia del lenguaje
  FluentASM.Sample/         → Validación manual (no se versiona ni se publica; sí se sube al origen)
  miCalculadoraCli/         → Programa de ejemplo de Fase 1 (.fproj + sourcesCode.fasm)
```

La arquitectura sigue la dirección `FluentASM.Core` → `FluentASM.Lsp` → `FluentASM_IDE`: la UI consume `ILanguageService` y no mantiene catálogos del lenguaje ni expresiones regulares propias. `FluentASM.Lsp` y `FluentASM_IDE` participan en los flujos de publicación de releases generando ZIPs `FluentASM.Lsp-v<versión>.zip` y `FluentASM.IDE-v<versión>.zip`.

```
rsc/
  FluentASM-MenuContextual.ps1          → Instalador temporal del menú de Windows Explorer para carpetas (HKCU)
  stitch_fluentasm_minimalist_ide/      → Referencias visuales del IDE (maquetas HTML y capturas)
```

El script se usa con `-i` para instalar y con `-u` para desinstalar; `-path` permite indicar explícitamente otro ejecutable. Escribe únicamente en `HKCU\Software\Classes`, por lo que no requiere permisos de administrador. Es un puente temporal hasta evaluar una extensión COM `IExplorerCommand` en C# NativeAOT.

La solución .NET de Fase 1 es `src/FluentASM.slnx`. Las compilaciones locales toman la versión de `VERSION` (raíz) mediante `src/Directory.Build.props`; las releases usan la versión declarada en el PR (ver «Versionado»).

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
- Integración del entorno `FluentASM_IDE`, ya activo como banco de trabajo, con formatos binarios, espacios de trabajo y herramientas del ecosistema.


## Roadmap

| Fase | Objetivo |
|---|---|
| **1 (Actual)** | Compilador en .NET (`.fproj` simple, léxico, parser, validador Tipo 1, codificador x64 mínimo viable) + imagen plana `.x64` ejecutada en lanzador de pruebas / QEMU *bare metal*, con banco de trabajo local LSP/IDE |
| **2** | Ampliación de x64 (memoria avanzada, bloques, utilidades de arranque en QEMU) y comunicación entre binarios / Partes (`.bin`) |
| **3** | Segunda arquitectura real (ARM64 / RISC-V en QEMU) y extracción del estándar común multi-CPU basado en implementaciones reales |
| **4** | Formatos binarios del ecosistema (`.fproj`, `.fworkspace`, `fasm_admin`, `fasm.fdoc`) y extensiones x64 avanzadas (SIMD, x87, sistema/paginación) |
| **5** | Ejecutables de sistemas operativos (PE/ELF) si se requieren; el IDE `FluentASM_IDE`, activo desde Fase 1 como banco de trabajo, se completa según su propia hoja de ruta |
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


### Decisión 0009 — Activación temprana de `FluentASM.Lsp` / `FluentASM_IDE` y exposición de `LanguageCatalog` en `Core` (2026-11-20)

*(Actualiza el diferimiento del IDE previsto en la Decisión 0007 sin alterar sus demás pilares).*

**Contexto:** Para crear aplicaciones reales de prueba y extender la sintaxis de Fase 1 con agilidad, se requiere un entorno de edición conectado directamente a la verdad del compilador. Mantener catálogos duplicados en `FluentASM.Lsp` y `FluentASM_IDE` provocó desincronización con la sintaxis v1.

**Decisión:**
1. **Banco de trabajo temprano:** `FluentASM.Lsp` y `FluentASM_IDE` pasan a desarrollarse activamente desde Fase 1 como entorno de prueba y evolución del lenguaje (en modo proyecto único e integración *in-process*).
2. **Fuente única en `Core`:** `FluentASM.Core` expone el catálogo público de solo lectura `LanguageCatalog`, proyectado desde `RegisterModel`, `VerbCatalog`, `FlagCatalog` y `DiagnosticIds`, consumido exclusivamente por `FluentASM.Lsp` (nunca duplicado en la UI).
3. **Validación y publicaciones:** La validación del IDE/LSP se apoya en el uso diario + registro estructurado de sucesos de fallos (sin proyectos `*Tests.csproj`). La incorporación del LSP/IDE a los flujos automáticos de release se aborda al cerrar su hito inicial.

**Consecuencias:**
- **Ganancia:** Cualquier extensión de sintaxis en `Core` se refleja automáticamente en resaltado, diagnósticos y completado del IDE; se elimina la duplicación de tablas.
- **Pérdida:** Aumenta ligeramente la superficie pública de solo lectura de `FluentASM.Core`.
- **Mitigación:** `LanguageCatalog` es estrictamente de solo lectura y no altera el pipeline público existente (`Lexer`, `Parser`, `SemanticAnalyzer`, `X64ImageBuilder`).


### Decisión 0010 — Modelo de proyecto único e integración Windows del IDE de Fase 1 (2026-11-20)

**Contexto:** La validación del IDE necesita abrir proyectos reales sin anticipar todavía los formatos binarios del ecosistema ni introducir una extensión COM que complique el despliegue. También requiere que la aplicación empaquetada abra archivos y carpetas mediante mecanismos nativos de Windows.

**Decisión:**
1. **Arquitectura en capas:** `FluentASM.Core` conserva la verdad del lenguaje y expone `LanguageCatalog` en modo de solo lectura; `FluentASM.Lsp` implementa `ILanguageService` *in-process*, sin WinUI ni `Windows.Storage`; `FluentASM_IDE` consume ese servicio y `FluentASM_IDE.Common` aporta utilidades compartidas sin semántica del lenguaje. La UI no replica catálogos ni expresiones regulares: completado, hover, símbolos y acciones rápidas proceden de `ILanguageService`.
2. **Modelo de proyecto único:** en Fase 1, cada carpeta representa un proyecto. El `.fproj` se abre en modo de solo lectura y `sourcesCode.fasm` es la fuente editable. Se difieren la edición de `.fproj`, la gestión de `.fworkspace` y cualquier integración con Git.
3. **Asociación y ejecución de archivos:** el manifiesto usa `<uap:Extension Category="windows.fileTypeAssociation">` con `<uap:FileTypeAssociation>` para `.fproj` y `.fasm`, y `<uap5:Extension Category="windows.appExecutionAlias">` para exponer `fluentasm-ide.exe`. El alias queda disponible después de desplegar o instalar el MSIX.
4. **Integración temporal con Explorer:** `rsc/FluentASM-MenuContextual.ps1` registra `Directory\shell\FluentASM` y `Directory\Background\shell\FluentASM` en `HKCU`, sin permisos de administrador; usa el alias por defecto y conserva `-path` como sustitución explícita. No se introduce una DLL COM: la extensión `IExplorerCommand` en C# NativeAOT se evaluará en una fase posterior.

**Consecuencias:**
- **Ganancia:** La aplicación empaquetada abre proyectos y fuentes con integración de Windows, mientras la IDE puede validarse de inmediato sobre un único directorio de proyecto.
- **Pérdida:** El menú de Explorer es una entrada clásica de Windows 11 y el alias requiere una instalación o despliegue previo; no hay todavía un modelo de espacios de trabajo ni integración con Git.
- **Mitigación:** El registro temporal es reversible con `-u` y no modifica el sistema más allá de `HKCU`; las capacidades ampliadas se incorporarán mediante decisiones posteriores.

### Decisión 0011 — Geometría de ventana (`WorkArea` + DWM), centralización `[LibraryImport]` y legibilidad base del IDE (2026-11-23)

* **Contexto:** Al incorporar el arranque centrado multi-monitor en `FluentASM_IDE` (Fase 1C), las ventanas estándar de Windows presentan marcos invisibles asimétricos (izquierda, derecha y abajo, pero no arriba) que rompían la simetría visual del margen, además de requerir llamadas Win32/DWM reutilizables a medida que crezca el IDE.
* **Decisión:**
  1. **Centralización de interoperabilidad nativa:** Todas las llamadas no administradas de `FluentASM_IDE` (`user32.dll`, `shcore.dll`, `dwmapi.dll` y futuras APIs Win32) deben vivir centralizadas en `FluentASM_IDE/Helpers/NativeMethods.cs` usando `[LibraryImport]` (`internal static partial class`) en lugar de `[DllImport]` dispersos por servicios o vistas.
  2. **Posicionamiento visual simétrico sobre `WorkArea`:** `Services/WindowGeometryService.cs` calcula el arranque centrado sobre `WorkArea` en el monitor activo del cursor con un hueco pequeño (`EdgeGapRatio = 0.008`, `10–18 DIP`), compensando los bordes invisibles DWM (`GetSystemMetricsForDpi` + `DWMWA_EXTENDED_FRAME_BOUNDS`) y leyendo métricas reales de sistema/presentador sin constantes visuales fijas.
  3. **Respeto a *Windows Snap* y persistencia mínima diferida:** Si el usuario mueve, redimensiona o acopla la ventana con *Snap*, el servicio no la recentra mientras sus límites visibles quepan en `WorkArea`. En `LocalFolder/window-state.json` (estado efímero de aplicación, ajeno al workspace) solo se persiste `IsMaximized`; recordar dimensiones elegidas por el usuario queda diferido hasta definir el almacenamiento de ajustes del editor.
  4. **Tipografía base del editor:** `RichEditBox` y la columna de números de línea (`LineNumbersText`) usan `FontSize="16"` sincronizado en `MainPage.xaml`.

## CI-CD (Integración y Despliegue Continuos)

GitHub Actions **no se ejecuta en cada Pull Request**: así no se consumen minutos de ejecución por cambios que aún están en revisión. La validación es local y obligatoria antes de abrir el PR:

1. `dotnet build src/FluentASM.slnx` sin advertencias (`.editorconfig` y `TreatWarningsAsErrors`).
2. `dotnet run --project src/FluentASM.Sample` con el `RESUMEN` final en orden; incluye la ejecución en memoria de M4 y, si QEMU está instalado, el arranque real de `serialBareMetal.img` (si no lo está, imprime la ruta y el comando sin fallar).
3. Si el cambio toca el codificador x64, contraste de bytes con NASM y prueba de la `.img` en QEMU.

La automatización solo actúa cuando un PR **ya está mergeado**:

| Flujo | Rama de destino | Resultado |
|---|---|---|
| `.github/workflows/dev-release.yml` | `develop` | Lee las líneas `ver(...)` del PR, aplica la versión, compila en Release y publica cada proyecto con versión como *prerelease* `x.x.x-preview`. Si la versión no es mayor que la última *prerelease*, no publica. |
| `.github/workflows/prod-release.yml` | `main` | El mismo proceso con las líneas `ver(...)` del PR de promoción, publicando las versiones estables `x.x.x` (sin `-preview`). Si la versión no es mayor que la última estable, no publica. |

Ningún flujo modifica el repositorio: la versión aplicada vive solo en el runner que genera los ZIP. Un `skip` deja el job en verde con el motivo en el registro; solo un error real (versión mal escrita, fallo de compilación o de publicación) marca el job como fallido.

## Reportar errores

Si encuentras un error, abre un issue con la siguiente información:
1. Qué esperabas que pasara.
2. Qué pasó en realidad.
3. El código que usaste (fragmento de FluentASM y su `.fproj` si aplica).
4. Tu versión del SDK de .NET.

## Licencia

Al contribuir, aceptas que tus contribuciones se liberen bajo la misma licencia del proyecto.
