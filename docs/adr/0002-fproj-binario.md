# ADR 0002 — `.fproj` y `.fworkspace` estrictamente binarios

- **Estado:** Aceptada
- **Fecha:** 2026-10-18
- **Decide:** mantenedor del proyecto

## Contexto

La visión del proyecto define el archivo de proyecto (`.fproj`) y el de solución (`.fworkspace`)
como **estrictamente binarios**, con una tabla de índice al inicio que permite lectura selectiva y
rápida por parte de herramientas automáticas. Se diseña así para que un compilador, un administrador
de producto o un IDE puedan leer, por ejemplo, solo la arquitectura sin recorrer las rutas de
archivos.

El coste de esa decisión es conocido: un archivo binario no se puede leer, editar ni diferenciar
con un editor de texto o con `git diff`, y no se puede revisar en un pull request. Además la
documentación original proponía que GitHub Actions obtuviera la **versión** leyendo el `.fproj`
binario, lo que crea una dependencia circular: para leer el `.fproj` hace falta instalar FluentASM,
y FluentASM todavía no existe.

## Decisión

1. `.fproj` y `.fworkspace` siguen siendo **estrictamente binarios** y su especificación byte a byte
   vive en [FORMAT_FPROJ.md](../FORMAT_FPROJ.md) y [FORMAT_FWORKSPACE.md](../FORMAT_FWORKSPACE.md).
2. **Toda** la creación y edición pasa por `fasm_admin`. No se editan a mano.
3. La **versión del producto** no se lee del `.fproj`: vive en un archivo de texto
   (`Directory.Build.props` y/o `VERSION`), que es la fuente de verdad que consumen CI/CD y GitHub
   Actions. El `.fproj` **copia** ese valor cuando se genera.
4. El formato se valida con **pruebas de bytes dorados**: leer y volver a escribir un archivo debe
   producir una secuencia idéntica byte a byte.

## Consecuencias

### Positivas

- Lectura selectiva real, sin depender de un parser de texto.
- Sin ambigüedad de codificación, finales de línea ni comillas.
- El formato es trivialmente implementable en cualquier lenguaje cuando llegue el auto-albergue.
- La dependencia circular de CI queda resuelta con el punto 3.

### Negativas y mitigaciones

| Riesgo | Mitigación obligatoria |
|---|---|
| No se puede revisar un cambio de configuración de proyecto en un PR | Añadir `fasm_admin dump <archivo.fproj>` con salida de texto estable y ordenada, y pegar esa salida en la descripción del PR cuando cambie un `.fproj` |
| No se puede crear un proyecto sin herramienta | `fasm_admin new` acepta **todos** los campos por línea de comandos, sin necesidad de plantilla de texto |
| Un `.fproj` corrupto es indetectable a simple vista | Campo de integridad (CRC32 opcional declarado en la cabecera) y validación estricta de la tabla de índice |
| Comparar dos `.fproj` en un merge es imposible | `fasm_admin dump --format text` y regla de proyecto: los `.fproj` **no se fusionan a mano**, se regeneran |
| MAJOR/MINOR del formato mal gestionados | Política de compatibilidad en `FORMAT_FPROJ.md`: MAJOR rompe, MINOR solo añade etiquetas que el lector puede ignorar |

## Alternativas consideradas

1. **Texto (TOML/JSON) como fuente de verdad y binario como caché generada.** Es la mejor
   experiencia de desarrollo y la más alineada con revisar en PR, pero contradice la visión
   declarada y obliga a mantener dos formatos y su sincronización.
2. **Binario con gemelo de texto generado automáticamente en CI.** Se descartó por ser una variante
   del punto 1 con más piezas móviles: si el texto se genera, nadie lo edita y el problema no
   desaparece; si se edita, hay dos verdades.
