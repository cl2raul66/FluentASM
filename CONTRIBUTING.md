# Contribución

Gracias por interesarte en contribuir a FluentASM. Este proyecto está diseñado para ser accesible, especialmente si vienes de C# y no tienes experiencia previa con compiladores o ensambladores.

## Cómo contribuir

1. **Bifurca el repositorio** (haz un *fork*) en GitHub.
2. **Crea una rama nueva** a partir de `develop`, basada en el issue que vas a resolver: `git checkout -b nombre-de-la-rama develop`
3. **Haz tus cambios** y commitea con mensajes claros que expliquen qué hiciste y por qué.
4. **Sube tu rama** al repositorio: `git push origin nombre-de-la-rama`
5. **Abre un Pull Request** para fusionar tu rama en `develop`.

## Estrategia de ramas

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

Dos reglas adicionales según lo que cambie el PR:

- Si toca un archivo `.fproj` o `.fworkspace`, pega en el cuerpo el resultado de `fasm_admin dump <archivo>` y explica el cambio. Como son binarios, ese volcado es la única forma de revisarlos en GitHub.
- Si cambia una decisión de diseño, incluye o enlaza la sección correspondiente de "Decisiones de diseño" en este documento.

La versión de la sugerencia es una propuesta; la fuente de verdad del número de versión se explica en la sección de versionado.

## Versionado

- Cada merge a la rama `develop` genera una versión preliminar de tipo `x.x.x-preview`.
- Cuando se hace merge de `develop` a `main`, se genera una versión estable (release) de tipo `x.x.x`, sin el sufijo `-preview`.
- Se sigue el versionado SemVer: `MAJOR.MINOR.PATCH`.

**Fuente de verdad del número de versión:** un archivo de **texto** versionado en el repositorio
(`VERSION` y/o la propiedad de versión en `Directory.Build.props`). GitHub Actions lee ese archivo —
nunca el `.fproj` binario—, de modo que no hace falta instalar FluentASM para saber qué versión se
está publicando. Cuando `fasm_admin` escribe un `.fproj`, **copia** ese valor al bloque
`ProductVersion`.

## Estándares de código (estilo C#)

Dado que FluentASM está escrito en C#, seguimos las convenciones de C#:

- Usa **PascalCase** para clases, métodos y propiedades: `Mov`, `Add`, `Rax`.
- Usa **camelCase** para variables locales y parámetros: `valorOrigen`, `registroDestino`.
- Usa `var` cuando el tipo sea obvio, igual que en C# moderno.
- Comenta el código usando `//` en español o inglés, sé claro y directo.
- No uses abreviaturas crípticas. `registroDestino` es mejor que `rd`.
- Espacios de nombres con ámbito de archivo (`namespace FluentASM.Core;`) y tipos de referencia
  anulables activados (`<Nullable>enable</Nullable>`).
- Los nombres de registros, mnemónicos y directivas del lenguaje se toman **siempre** de
  [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md); no se inventan alias.
- `.editorconfig` en la raíz es obligatorio y las advertencias del compilador se tratan como errores
  en la integración continua.

### Pruebas

- Todo cambio en el codificador, el layout o los emisores exige una **prueba de bytes dorados**:
  los bytes esperados escritos literalmente en la prueba.
- Cuando exista un oráculo disponible (NASM para x86/x64), la prueba **compara** la salida propia con
  la suya. Un byte de diferencia es un fallo, no una preferencia.
- Todo mensaje de diagnóstico nuevo necesita su prueba con el caso que lo provoca.

## Estructura del código fuente (`src/`)

La carpeta `src/` contiene varios proyectos independientes:

```
src/
  FluentASM.Core/           → Modelo intermedio, catálogo de instrucciones, codificador, layout y emisores
  FluentASM.Compiler/       → El ensamblador (fluentasm): lee .fasm y .fproj y genera la salida
  FluentASM.Admin/          → El CLI administrador de producto (fasm_admin)
  FluentASM.Core.Tests/     → Pruebas de Core: bytes dorados, oráculo NASM, ejecución en memoria
  FluentASM.Compiler.Tests/ → Pruebas del ensamblador de extremo a extremo
  FluentASM.IDE/            → Entorno de desarrollo integrado (Fase 9)
```

Cada proyecto es independiente pero forma parte del mismo ecosistema. Puedes ampliar esta estructura según se necesiten más herramientas.

---

## Visión del proyecto

### Qué es FluentASM

FluentASM es un lenguaje ensamblador con sintaxis fluida, escrito en C#. Traduce instrucciones encadenadas con aspecto de método de C# a **código máquina nativo** y genera imágenes binarias que no necesitan ningún runtime.

### Principios rectores

1. **Claridad sobre brevedad.** Prefiere `.Mov(Eax, 10)` sobre `mov eax, 10`.
2. **Empatía con quien aprende.** Cada concepto nuevo viene con analogía y ejemplo.
3. **Sintaxis fluida y encadenada.** Inspirado en LINQ y builders de C#.
4. **C# como lengua franca.** El proyecto está en C# y su API refleja la experiencia de .NET.
5. **Educación antes que potencia.** Primero que la base sea clara, luego se añade potencia.

### Qué FluentASM NO es

- No es un lenguaje de alto nivel como C# o Java.
- No intenta reemplazar a C#.
- No es un compilador optimizador complejo (por ahora).
- No requiere entender pipelines de CPU, caché o memoria virtual para empezar.
- No es una máquina virtual: no ejecuta bytecode intermedio ni necesita runtime instalado.

**Principio fundamental:** nada en FluentASM es una "caja negra". Todo debe ser reducible a instrucciones y bytes visibles.

---

## Modelo de ejecución y ABI

### ABI objetivo de la Fase 1: Microsoft x64 (Win64)

| Concepto | Valor |
|---|---|
| Parámetro 1 | `Rcx` |
| Parámetro 2 | `Rdx` |
| Parámetro 3 | `R8` |
| Parámetro 4 | `R9` |
| Valor de retorno entero | `Rax` |
| Espacio de sombra | 32 bytes, reservado por el llamante |
| Alineación de pila en la llamada | 16 bytes |
| Callee-saved | `Rbx`, `Rbp`, `Rdi`, `Rsi`, `R12`–`R15`, `Rsp` |
| Caller-saved (volátiles) | `Rax`, `Rcx`, `Rdx`, `R8`–`R11` |

La ABI activa se declara siempre (en `.fproj` o línea de comandos). Nunca se adivina.

### `parameter`, prólogo y epílogo

- `parameter` **no elige registros**: los asigna la ABI, en orden. El nombre es un alias legible.
- El compilador **genera** el prólogo (guardar `Rbp`, establecer marco, reservar locales alineado a 16) y el epílogo alrededor del cuerpo.
- `.Ret()` ejecuta el epílogo y el `ret`. No se escribe `ret` a mano en rutinas con `parameter`.
- Límite Fase 1: máximo 4 parámetros por rutina.
- `naked` fuerza ausencia de prólogo/epílogo aunque haya parámetros o locales.

### Modelo de ejecución

- Salida Fase 1: **imagen plana** (bytes puros, sin cabecera) cargada en memoria.
- Código independiente de posición (PIC). No puede llamar a nada externo.
- Lo observable: valor de retorno en `Rax`.

### Pila

- Crece hacia abajo.
- Al momento de `call`, `Rsp` debe ser múltiplo de 16.
- Espacio de sombra de 32 bytes en Win64 cumple el ajuste de alineación tras empujar la dirección de retorno.
- No existe "zona roja" (red zone) en Win64.

---

## Modelo intermedio (IR)

### Etapas del proceso

```
Entrada .fasm → IR de origen → Layout → Codificación → Emisión → Imagen .x64
Entrada API C# ─────────────┘
```

| Etapa | Invariante al terminar |
|---|---|
| IR de origen | Cada símbolo tiene dueño; cada sección tiene nombre, flags, alineación |
| Layout | Cada símbolo tiene `Offset`; cada sección tiene `VirtualAddress` |
| Codificación | Cada instrucción tiene codificación seleccionada y tamaño conocido |
| Emisión | Todas las relocalizaciones resueltas; ninguna pendiente |

### Layout y ajuste de saltos

1. Todos los saltos se codifican en forma corta (`Rel8`).
2. Se asignan direcciones.
3. Si un desplazamiento no cabe en `Rel8` (-128..127), se promociona a `Rel32`.
4. Se repite hasta convergencia (monótono, termina).

### Diagnósticos

| Rango | Etapa | Ejemplo |
|---|---|---|
| `FASM1xxx` | Léxico | `FASM1002` carácter inesperado |
| `FASM2xxx` | Sintáctico | `FASM2001` falta paréntesis de cierre |
| `FASM3xxx` | Semántico y ABI | `FASM3001` más de 4 parámetros; `FASM3002` registro 8 bits alto con REX |
| `FASM4xxx` | Codificación | `FASM4001` no existe forma para esos operandos |
| `FASM5xxx` | Layout y relocalizaciones | `FASM5001` símbolo externo en imagen plana |
| `FASM9xxx` | Errores internos | `FASM9001` layout no converge |

---

## Formatos binarios

### `.fproj` — Archivo de proyecto

Estrictamente binario, tabla de índice al inicio para lectura selectiva. No se edita a mano; se crea y modifica con `fasm_admin`.

**Cabecera (16 bytes):**
- `Magic`: `FPRJ` (4 bytes ASCII)
- `VersionMajor`, `VersionMinor`: u16 (actual: 1.1)
- `EntryCount`: u16
- `Flags`: u16 (reservado, 0)
- `IndexOffset`: u32

**Entrada de índice (16 bytes):** `Tag` (u16), `Flags` (u16), `Offset` (u32), `Size` (u32), `Reserved` (u32)

**Etiquetas principales:**

| Tag | Nombre | Obligatorio |
|---|---|---|
| `0x0001` | `ProgramName` | Sí |
| `0x0002` | `ProductVersion` | Sí |
| `0x0003` | `Architecture` | Sí |
| `0x0004` | `TargetSystem` | Sí |
| `0x0005` | `Abi` | Sí |
| `0x0006` | `Sources` | Sí |
| `0x0007` | `Libraries` | No |
| `0x0008` | `OutputName` | Sí |
| `0x0009` | `EntrySymbol` | No |
| `0x000A` | `Description` | No |
| `0x000B` | `BuildOptions` | No |
| `0x000C` | `ToolVersion` | Sí |
| `0x000D` | `CreatedUtc` | Sí |
| `0x000E` | `ModifiedUtc` | Sí |
| `0x000F` | `Destinations` | No |
| `0x0010` | `Representations` | No |
| `0x0011` | `ServiceParts` | No |

**Valores de enumeración:**
- `Architecture`: 1=x64, 2=x86, 3=arm
- `TargetSystem`: 0=bare-metal, 1=windows, 2=linux, 3=macos
- `Abi`: 0=none, 1=win64, 2=systemv
- `Representations`: 0=portable-only, 1=native-only, 2=both

**BuildOptions (u32, campo de bits):**
- Bit 0: PIC (defecto: 1)
- Bit 1: Ajuste automático de saltos (defecto: 1)
- Bit 2: Símbolos de depuración (defecto: 0)
- Bit 3: Prohibir símbolos externos (defecto: 1)

### `.fworkspace` — Archivo de solución

Misma estructura que `.fproj` con magic `FWKS`. Agrupa múltiples `.fproj` con grafo de dependencias y orden topológico determinista.

### `.bin` — Parte

Contenedor de representaciones (portable y nativas) de un mismo código.

**Cabecera (16 bytes):**
- `Magic`: `FPRT`
- `VersionMajor`, `VersionMinor`: u16
- `RepCount`: u16
- `Flags`: u16 (reservado)
- `IndexOffset`: u32

**Entrada de representación (16 bytes):** `RepType` (u8: 0=portable, 1=nativo), `Arch` (u8), `Flags` (u16), `Offset` (u32), `Size` (u32), `CRC32` (u32)

### Reglas comunes

- Little-endian siempre, anchuras fijas, sin varints, sin BOM, sin finales de línea.
- Bloques alineados a 8 bytes. Relleno con `00`.
- Cadenas: u32 longitud + bytes UTF-8.
- Versión del producto vive en archivo de texto (`VERSION` / `Directory.Build.props`), **no** en el `.fproj`.

---

## Modelo de portabilidad

Cuatro niveles de promesa:

| Nivel | Nombre | Promesa | Restricción |
|---|---|---|---|
| 1 | Núcleo Portable | Mismo resultado en cualquier arquitectura | No nombrar registros nativos ni banderas específicas |
| 2 | Servicios del Lenguaje | Misma interfaz, distinta implementación por plataforma | Depende de una "Parte" de servicio compatible |
| 3 | Extensiones ISA | Control total sobre la CPU | Sin promesa de portabilidad |
| 4 | Artefactos del Lenguaje | Independencia de herramientas externas | Formatos propios `.fasm`, `.bin`, `.fproj`, `.fworkspace` |

### Registros portables (Nivel 1)

Numeración canónica independiente de ISA con ancho explícito: `R0q`, `R1d`, `R0w`, `R0b`. El compilador los mapea a registros nativos (`R0q` → `Rax` en x64, `X0` en ARM64).

**Regla importante:** `parameter` asigna nombres según la ABI. En x64 el parámetro 1 llega en `Rcx` (`R1` canónico); en ARM64 llega en `X0` (`R0` canónico). Nunca asumas que `parameter 1` es `R0`.

### Parte (`.bin`)

Unidad de composición. Contiene representación portable por defecto y opcionalmente representaciones nativas para una o varias ISAs. El compilador extrae los bytes de la representación nativa del destino y los incrusta en la imagen final.

### Frontera mínima (bare metal)

Servicios básicos garantizados sin SO: arranque, consola de E/S, tiempo, memoria básica. Implementados como Partes de Servicio (`metal.bin`, `win32.bin`, etc.).

---

## Roadmap

| Fase | Objetivo |
|---|---|
| 1 | Núcleo x64, imagen plana, ejecución en memoria |
| 2 | Frontend `.fasm`: léxico, sintáctico y semántico |
| 3 | ARM64 en QEMU: segunda implementación nativa |
| 4 | Extracción del núcleo portable (comparar x64 y ARM64) |
| 5 | Partes (`.bin`): contenedor de representaciones |
| 6 | Proyectos y soluciones (`.fproj`, `.fworkspace`, `fasm_admin`) |
| 7 | Ejecutables del SO (PE/ELF) |
| 8 | RISC-V en QEMU: tercera implementación nativa |
| 9 | Bare metal en QEMU (x64, ARM64, RISC-V) |
| 10 | ESP32 / Xtensa sobre hardware |
| 11 | Auto-albergue (*self-hosting*) |
| 12 | IDE |

**Criterio de salida Fase 0:** una persona ajena al diseño puede implementar la Fase 1 leyendo solo la documentación, sin hacer preguntas.

---

## Vocabulario prescriptivo

### Reglas de diseño

1. **Un concepto, un nombre.** Sinónimos se listan como "alias no usados".
2. **Definir por lo que hace, no por a qué se parece.** Entradas, salidas, efecto observable.
3. **Nombrar lo que ocurre físicamente.** Si guarda bits, se llama así.
4. **Anchos siempre explícitos.** Nunca "palabra" o "32 bits" a secas.
5. **Marketing no es vocabulario técnico.** Términos de venta se excluyen.
6. **Término estándar aceptable si se define con precisión.** No sustituir nombres conocidos por frases descriptivas.
7. **Vigencia física.** Un término histórico se conserva solo si su definición actual es físicamente exacta.

### Términos a evitar y su sustitución

| Evitar | Usar en su lugar |
|---|---|
| "RAM" | Memoria de trabajo |
| "ROM" | Memoria de solo lectura / memoria no volátil |
| "Disco" | Unidad de almacenamiento |
| "Endianness" | Orden de bytes: menor-primero / mayor-primero |
| "Palabra" | Ancho nativo (bits específicos) |
| "Núcleo" (sin calificar) | Unidad de ejecución independiente |
| "Hilo" (sin calificar) | Contexto de ejecución hardware |
| "Caché" (sin definir) | Memoria intermedia de acceso rápido |

### Correspondencia con código (C#)

| Concepto del glosario | Identificador en código | Prohibido |
|---|---|---|
| Memoria de trabajo | `WorkingMemory` | `Ram`, `Memory` a secas |
| Memoria persistente | `PersistentMemory` | `Rom`, `Storage` a secas |
| Unidad de almacenamiento | `StorageUnit` | `Disk`, `Drive` |
| Parte | `Part` | `Library`, `Module` |
| Bajar (*lowering*) | `Lower` | `Translate` |
| Imagen plana | `FlatImage` | `RawBinary` |

---

## Decisiones de diseño

Una decisión que afecte a más de un archivo (formato, sintaxis, arquitectura de código, compatibilidad) no se decide en un comentario de PR: se registra aquí en CONTRIBUTING.md.

1. Abre un issue de tipo **decisión** explicando el problema y las opciones.
2. Escribe la decisión con formato: contexto, decisión, consecuencias y alternativas descartadas.
3. Al fusionarse, la decisión es obligatoria y los documentos afectados se actualizan en el mismo PR.
4. Las decisiones no se editan: si una cambia, se escribe una nueva que la reemplaza.

Para debates internos, usa documentos locales con prefijos `proposal-`, `idea-` o `plan-`. Estos **no se suben al origen**. Una vez implementado en código y aprobado, el contenido se pasa a documentación en este archivo o en `FLUENT_SYNTAX.md`.

---

## Archivo de proyecto `.fproj`

Cada programa se define mediante un archivo `.fproj` binario ubicado junto a sus archivos fuente (mismo directorio o subdirectorios). Este archivo contiene la configuración de compilación: arquitectura, sistema objetivo, rutas relativas a los fuentes `.fasm` y bibliotecas `.bin`, y nombre de salida.

El `.fproj` es estrictamente binario e incluye una **tabla de índice** al inicio que indica la posición y tamaño de cada sección de información, permitiendo lectura selectiva y rápida por máquinas y herramientas. No se edita a mano con un editor de texto; se crea y se modifica con `fasm_admin`, que acepta todos los campos por línea de comandos y ofrece `dump` y `verify` para revisarlo.

La especificación byte a byte está definida más arriba en esta misma sección (Formatos binarios). La versión del producto **no** se lee de este archivo: vive en un archivo de texto del repositorio.

## Archivo de solución `.fworkspace`

Cuando se trabaja con más de un proyecto, se utiliza un archivo `.fworkspace` ubicado en el directorio padre. Este archivo agrupa múltiples archivos `.fproj` y gestiona dependencias entre ellos.

- Es **opcional para un solo proyecto**.
- Es **requerido para múltiples proyectos**.
- Utiliza **formato estrictamente binario** con tabla de índice para lectura rápida por máquinas y herramientas.

## CLI `fasm_admin`

El administrador de producto de FluentASM se ejecuta mediante la herramienta de línea de comandos `fasm_admin`. Este CLI permite gestionar uno o varios proyectos a través de archivos `.fproj` y `.fworkspace`.

Comandos principales:

| Comando | Descripción |
|---|---|
| `fasm_admin init` | Crea un archivo `.fworkspace` vacío en el directorio actual. |
| `fasm_admin add <ruta/proyecto.fproj>` | Agrega un proyecto existente al workspace. |
| `fasm_admin remove <nombre-proyecto>` | Quita un proyecto del workspace. |
| `fasm_admin list` | Muestra todos los proyectos registrados con su arquitectura y estado. |
| `fasm_admin build` | Compila todos los proyectos del workspace en orden de dependencias. |
| `fasm_admin build <nombre-proyecto>` | Compila solo un proyecto específico. |
| `fasm_admin clean` | Borra binarios generados de todos los proyectos. |
| `fasm_admin run <nombre-proyecto>` | Ejecuta un binario generado (si es para el SO actual). |

## CI-CD (Integración y Despliegue Continuos)

**¿Qué es CI-CD?** Son procesos automatizados que se ejecutan cada vez que subes código al repositorio:

- **CI (Integración Continua):** construye el proyecto y ejecuta pruebas automáticamente para asegurarse de que tu cambio no rompió nada existente.
- **CD (Despliegue Continuo):** si todo pasa correctamente, puede publicar automáticamente una nueva versión.

En FluentASM usamos GitHub Actions para la CI. **Se habilita en la Fase 1**, cuando exista código; hasta entonces no hay nada que construir. Cada Pull Request:

1. Compila todos los proyectos de `src/` con el SDK de .NET del repositorio.
2. Ejecuta las pruebas unitarias, incluidas las de **bytes dorados** y las de **ejecución en memoria**.
3. Instala **NASM** y ejecuta las pruebas del **oráculo**: el mismo código ensamblado por FluentASM y por NASM debe producir bytes idénticos.
4. Ensambla los ejemplos de `rsc/` de extremo a extremo.
5. Trata las advertencias como errores y falla si algún archivo no respeta `.editorconfig`.

En la Fase 7 se añade un paso que **ejecuta** el `.exe` generado y comprueba su código de salida.

Cada merge a `develop` genera automáticamente una versión `x.x.x-preview` mediante GitHub Actions.
La versión se lee del archivo de texto del repositorio (`VERSION` / `Directory.Build.props`), **no**
del `.fproj` binario: así la integración continua no depende de instalar FluentASM para saber qué
versión está publicando.

## Reportar errores

Si encuentras un error, abre un issue con la siguiente información:
1. Qué esperabas que pasara.
2. Qué pasó en realidad.
3. El código que usaste (fragmento de FluentASM o C#).
4. Tu versión de .NET / C#.

## Licencia

Al contribuir, aceptas que tus contribuciones se liberen bajo la misma licencia del proyecto.