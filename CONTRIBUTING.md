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
- Si cambia una decisión de diseño, incluye o enlaza el **ADR** correspondiente en `docs/adr/` (ver "Decisiones de diseño").

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

## Decisiones de diseño

Una decisión que afecte a más de un archivo (formato, sintaxis, arquitectura de código, compatibilidad)
no se decide en un comentario de PR: se registra como **ADR** en `docs/adr/`.

1. Abre un issue de tipo **decisión** explicando el problema y las opciones.
2. Escribe el ADR con el formato de `docs/adr/0001-nombres-fasm.md`: contexto, decisión, consecuencias
   y alternativas descartadas.
3. Al fusionarse, la decisión es obligatoria y los documentos afectados se actualizan en el mismo PR.

Los ADR no se editan: si una decisión cambia, se escribe uno nuevo que la reemplaza.

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

Los documentos de diseño viven en la raíz (`VISION.md`, `FLUENT_SYNTAX.md`, `roadmap.md`,
`CONTRIBUTING.md`) y las especificaciones en `docs/`. Los ejemplos y pruebas de concepto en `rsc/`:
**los ejemplos de `rsc/examples/` son casos de prueba vivos**, no adornos, y se ejecutan en la
integración continua.

## Archivo de proyecto `.fproj`

Cada programa se define mediante un archivo `.fproj` binario ubicado junto a sus archivos fuente (mismo directorio o subdirectorios). Este archivo contiene la configuración de compilación: arquitectura, sistema objetivo, rutas relativas a los fuentes `.fasm` y bibliotecas `.bin`, y nombre de salida.

El `.fproj` es estrictamente binario e incluye una **tabla de índice** al inicio que indica la posición y tamaño de cada sección de información, permitiendo lectura selectiva y rápida por máquinas y herramientas. No se edita a mano con un editor de texto; se crea y se modifica con `fasm_admin`, que acepta todos los campos por línea de comandos y ofrece `dump` y `verify` para revisarlo.

La especificación byte a byte es [docs/FORMAT_FPROJ.md](docs/FORMAT_FPROJ.md). La versión del producto **no** se lee de este archivo: vive en un archivo de texto del repositorio.

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
4. Ensambla los ejemplos de `rsc/examples/` de extremo a extremo.
5. Trata las advertencias como errores y falla si algún archivo no respeta `.editorconfig`.

En la Fase 5 se añade un paso que **ejecuta** el `.exe` generado y comprueba su código de salida.

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