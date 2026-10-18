# Roadmap de FluentASM

## Estado actual: Fase 0 — Cierre de diseño

El diseño está prácticamente cerrado: visión, sintaxis, IR, ABI y formatos binarios están
especificados y las decisiones están registradas en [docs/adr/](docs/adr/). Falta convertir este
roadmap en issues.

**Criterio de salida de la Fase 0:** una persona que no haya participado en el diseño puede
implementar la Fase 1 leyendo solo la documentación, **sin hacer ni una pregunta**.

---

## Cómo se lee este roadmap

1. Cada fase tiene un **objetivo**, unos **entregables** y un **criterio de salida verificable**. No
   se considera terminada una fase porque "el código compila", sino porque su criterio se puede
   ejecutar y comprobar.
2. El orden es de **dependencias**, no de deseabilidad. La sección "Por qué este orden" lo justifica.
3. **Cada casilla de este documento es un issue.** Ver "De roadmap a issues" al final.

---

## Fase 1 — Núcleo x64, imagen plana y ejecución en memoria

**Objetivo:** generar bytes x64 correctos y **ejecutarlos de verdad**, con una API de C# que no
necesite analizador sintáctico.

**Entregables**

- [ ] Proyecto `src/FluentASM.Core`: IR completo según [docs/IR.md](docs/IR.md) (registros, operandos,
      secciones, símbolos, relocalizaciones).
- [ ] Catálogo de instrucciones de la Fase 1 definido **como datos** (mnemónico, formas, codificación),
      consumible desde cualquier superficie.
- [ ] Codificador x64 con las reglas de forma más corta y trazabilidad de la decisión.
- [ ] Layout con asignación de direcciones y ajuste iterativo de saltos `rel8` → `rel32`.
- [ ] Emisor de **imagen plana** `.x64` sin cabecera.
- [ ] API fluent de C# (superficie secundaria, ver [ADR 0004](docs/adr/0004-superficie-canonica.md)).
- [ ] CLI `fluentasm` que ensambla desde argumentos (`--source`, `--output`, `--arch x64`).
- [ ] **Arrancador en memoria**: reserva ejecutable, copia, salto al punto de entrada y lectura del
      resultado en `Rax`.
- [ ] Pruebas: bytes dorados, **oráculo NASM**, ejecución real, independencia de posición y
      diagnósticos (`FASM4xxx`, `FASM5xxx`).
- [ ] Integración continua: compilar, pasar pruebas y ensamblar los ejemplos en cada PR.

**Criterio de salida**

1. `.Mov(Eax, 42)` + `.Ret()` produce exactamente `b8 2a 00 00 00 c3`, igual que NASM.
2. El ejemplo `AddTwoNumbers` de [docs/ABI.md](docs/ABI.md) ejecutado en memoria devuelve **42**.
3. La misma imagen cargada en dos direcciones distintas devuelve 42 en ambas.
4. Un salto lejano se promociona a forma cercana y el binario sigue siendo correcto.
5. Un ejemplo con `extern` falla con `FASM5001` y un mensaje que menciona la Fase 5, sin excepción.

**Fuera de alcance:** leer `.fasm`, `.fproj`, llamadas externas, otras arquitecturas.

---

## Fase 2 — Frontend del lenguaje: archivos `.fasm`

**Objetivo:** que la superficie canónica (el DSL) funcione: analizador léxico, sintáctico y semántico
según la gramática de [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md).

**Entregables**

- [ ] Analizador léxico con la tabla de la sección 4 de la referencia de sintaxis.
- [ ] Analizador sintáctico de la EBNF de la sección 3, con mensajes de error útiles.
- [ ] Directivas: `Section`, `Entry`, `Db`/`Dw`/`Dd`/`Dq`, `Res*`, `Align`, `Equ`, `Include`.
- [ ] Etiquetas locales (`@nombre`) y resolución de visibilidad por rutina.
- [ ] Rutinas con `Parameter`, incluido `Naked`.
- [ ] Diagnósticos `FASM1xxx`–`FASM3xxx` con línea y columna.
- [ ] Los ejemplos de `rsc/examples/` compilan de verdad.

**Criterio de salida**

1. Cada ejemplo de `rsc/examples/` compila y produce **los mismos bytes** que la API de C# (prueba
   comparativa automática).
2. Un archivo con un error de sintaxis produce un diagnóstico con línea, columna y sugerencia; nunca
   una excepción ni un mensaje genérico.
3. Un `.fasm` de FASM (sintaxis Intel) falla con un mensaje que menciona explícitamente esa
   posibilidad.

**Fuera de alcance:** compatibilidad con sintaxis Intel como entrada (Fase 2.5, opcional).

---

## Fase 3 — Proyectos y soluciones: `.fproj` y `fasm_admin`

**Objetivo:** que el archivo de proyecto binario y el administrador de producto funcionen de verdad.

**Entregables**

- [ ] Escritura y lectura del `.fproj` según [docs/FORMAT_FPROJ.md](docs/FORMAT_FPROJ.md), con lectura
      selectiva y CRC32.
- [ ] Escritura y lectura del `.fworkspace` según [docs/FORMAT_FWORKSPACE.md](docs/FORMAT_FWORKSPACE.md),
      con grafo de dependencias y orden topológico determinista.
- [ ] CLI `fasm_admin`: `new`, `get`, `set`, `dump`, `verify`, `init`, `add`, `remove`, `dep add`,
      `dep remove`, `list`, `build`, `clean`, `run`.
- [ ] `fluentasm` acepta un `.fproj` como entrada (además de argumentos).
- [ ] Pruebas de bytes dorados y de ida y vuelta de ambos formatos.
- [ ] Comprobación de rutas: relativas, dentro del proyecto, sin `..`.

**Criterio de salida**

1. El ejemplo hexadecimal de la sección 8 de `FORMAT_FPROJ.md` se reproduce byte a byte con `new`.
2. `dump` de un `.fproj` generado coincide con el volcado documentado.
3. Un workspace de dos proyectos con dependencia compila en orden correcto y de forma determinista.
4. Un ciclo en el grafo se rechaza indicando el ciclo completo.

**Fuera de alcance:** interfaz gráfica de gestión de proyectos (es del IDE).

---

## Fase 4 — Bibliotecas nativas incrustadas (`.bin`)

**Objetivo:** incrustar fragmentos de código máquina ya compilado dentro de la imagen generada, con
resolución de saltos entre ambos.

**Entregables**

- [ ] Directiva de incrustación en `.fasm` y su equivalente en la API de C#.
- [ ] Extracción de fragmentos de `.bin` por offset y tamaño.
- [ ] Resolución de relocalizaciones entre el código principal y los fragmentos.
- [ ] Empaquetado: todo lo necesario dentro del `.x64`, sin dependencias sueltas.
- [ ] Pruebas: relocalización a fragmento, fragmento que llama de vuelta al código principal.

**Criterio de salida:** un programa que llama a una rutina definida en un `.bin` produce el resultado
esperado y el binario final no depende de ningún archivo externo en tiempo de ejecución.

---

## Fase 5 — Ejecutables del sistema operativo

**Objetivo:** producir programas que el sistema operativo cargue por sí mismo, con llamadas al
sistema, sin CRT y sin runtime .NET.

**Entregables**

- [ ] Emisor PE x64 con secciones, alineaciones y **tabla de importaciones** (`kernel32`).
- [ ] Emisor ELF x64 con llamadas al sistema (equivalente Linux).
- [ ] Ruta "sin CRT": un `.exe` que use solo la API de Windows, al estilo de `WinUIAsmCtrl`.
- [ ] Espacio de sombra y alineación a 16 verificados en cada llamada generada.
- [ ] Pruebas: el `.exe` se ejecuta en la integración continua de Windows y devuelve el código de
      salida esperado.

**Criterio de salida**

1. Un "hola mundo" de consola generado por FluentASM se ejecuta y muestra su texto.
2. Un `.exe` que llama a `ExitProcess(42)` devuelve 42 al sistema.
3. El `.exe` no requiere .NET, ni el CRT, ni ningún archivo adicional.

---

## Fase 6 — Otras arquitecturas

**Objetivo:** x86 de 32 bits y ARM de 64 bits.

**Entregables**

- [ ] Codificador x86-32, incluida la ABI `cdecl`/`stdcall` y el paso de parámetros por pila.
- [ ] Codificador ARM64 (AArch64) con su propia ABI.
- [ ] Selección de arquitectura desde el `.fproj` y desde la línea de comandos.
- [ ] Banco de pruebas por arquitectura con el mismo oráculo (NASM para x86, `as`/`llvm-mc` para ARM).

**Criterio de salida:** el mismo programa `.fasm` compila para `.x64`, `.x86` y `.arm`, cada uno
ejecutado en su entorno y devolviendo el mismo resultado.

---

## Fase 7 — Ejecución sin sistema operativo (bare metal)

**Objetivo:** generar imágenes que arranquen sobre el hardware sin sistema operativo.

> **Aclaración importante:** arrancar por BIOS implica código de **16 bits en modo real**, que es un
> codificador **distinto** del x86 de 32 bits de la Fase 6. No es "el mismo x86 en pequeño". UEFI, en
> cambio, arranca en modo largo (64 bits).

**Entregables**

- [ ] Modo de dirección fija: aquí **sí** se permiten direcciones absolutas y una base conocida, al
      contrario que en la regla PIC de la Fase 1.
- [ ] Imagen de arranque (`.img`) con sector de arranque.
- [ ] Rutinas mínimas de entrada/salida sin llamadas al sistema.
- [ ] Prueba de arranque en QEMU automatizada en la integración continua.
- [ ] (Opcional) Arranque UEFI x64.

**Criterio de salida:** la imagen arranca en QEMU y escribe el mensaje esperado por pantalla.

---

## Fase 8 — Auto-albergue (*self-hosting*)

**Objetivo:** escribir el ensamblador FluentASM **en FluentASM** y que se compile a sí mismo.

**Entregables**

- [ ] `compiler.fasm`: analizadores, layout, codificador y emisor escritos en el propio lenguaje.
- [ ] Compilado con la versión en C# de la Fase 5, produce un `.exe` funcional.
- [ ] El binario resultante compila cualquier `.fasm`, incluido su propio código fuente.
- [ ] Prueba de equivalencia: para un conjunto de ejemplos, la salida del ensamblador en C# y la del
      ensamblador auto-albergado es **idéntica**.

**Criterio de salida:** el ensamblador escrito en FluentASM compila `compiler.fasm` y el resultado
compila los ejemplos de prueba con bytes idénticos.

> Esta es la fase más costosa del proyecto: exige escribir gestión de cadenas, tablas y análisis
> sintáctico sin CRT. Va al final a propósito. Precedente alentador: FASM (*flat assembler*) está
> escrito en su propio lenguaje.

---

## Fase 9 — IDE

**Objetivo:** entorno de desarrollo integrado para el lenguaje.

**Entregables**

- [ ] Proyecto `src/FluentASM.IDE` con editor y resaltado de sintaxis `.fasm`.
- [ ] Compilación integrada para todas las arquitecturas soportadas.
- [ ] Inspección de los bytes generados por instrucción (aprovecha la trazabilidad del IR).
- [ ] Gestión de proyectos basada en `fasm_admin`.
- [ ] Depurador básico.

**Criterio de salida:** se puede crear, compilar, ejecutar e inspeccionar un proyecto sin salir del
IDE.

---

## Por qué este orden

| Fase | Depende de | Motivo del orden |
|---|---|---|
| 1 | — | Sin bytes correctos y ejecutables no hay nada que construir encima |
| 2 | 1 | El analizador necesita un IR y un catálogo que ya funcionen |
| 3 | 1, 2 | Un archivo de proyecto solo tiene sentido si hay algo que compilar |
| 4 | 1, 3 | Incrustar código requiere relocalizaciones y una noción de proyecto |
| 5 | 1 | Los ejecutables del SO son una capa de empaquetado sobre el mismo núcleo |
| 6 | 5 | Cada arquitectura nueva necesita su propio entorno de prueba y ejecución |
| 7 | 6 | Bare metal reutiliza el trabajo de arquitecturas y añade la suya de 16 bits |
| 8 | 5 | **Automantenerse exige poder generar un ejecutable**, que llega en la Fase 5 |
| 9 | 3, 5 | El IDE consume el compilador, el administrador y los formatos de proyecto |

El auto-albergue **no** va antes: en la documentación inicial figuraba como Fase 2, lo que habría
obligado a escribir el analizador del lenguaje *en el propio lenguaje* antes de saber si el
codificador funcionaba.

---

## Fuera de alcance (por ahora)

- Optimizador de código (no es un compilador optimizador).
- Máquina virtual, bytecode o intérprete (contradice la visión).
- Gestor de dependencias o repositorio de paquetes.
- Coma flotante y SIMD: necesarios para código numérico, no para aprender.
- Compatibilidad binaria con objetos generados por otros ensambladores (la interfaz es el `.bin`).

---

## De roadmap a issues

Cada casilla de este documento se convierte en un issue del repositorio:

| Elemento | Cómo se usa |
|---|---|
| **Milestone** | Uno por fase (`Fase 1`, `Fase 2`, …). Agrupa sus issues |
| **Etiqueta de área** | `area/core`, `area/encoder`, `area/layout`, `area/parser`, `area/fproj`, `area/admin`, `area/runner`, `area/ci`, `area/docs` |
| **Etiqueta de tipo** | `tipo/feature`, `tipo/bug`, `tipo/docs`, `tipo/decision` |
| **Etiqueta de tamaño** | `tamaño/S`, `tamaño/M`, `tamaño/L` (una estimación honesta, revisable) |
| `primera-contribución` | Issues de documentación y ejemplos, aptos sin conocimiento de compiladores |
| **Definition of Done** | El issue se cierra cuando su criterio es **ejecutable**: una prueba, un byte comparado o una salida verificada |

Un issue que no se puede comprobar de forma automática se reescribe hasta que se pueda.

> **Nota:** este roadmap se revisa al completar cada fase. Las prioridades pueden cambiar según las
> necesidades del proyecto y los issues abiertos; los cambios de contenido se registran como ADR.
