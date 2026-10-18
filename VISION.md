# Visión

Queremos que escribir ensamblador sea tan cómodo y legible como escribir C#.

Hoy en día, el ensamblador se percibe como algo difícil, críptico y reservado a expertos. FluentASM nace para cambiar eso: hacer que el lenguaje máquina sea accesible para cualquier persona que sepa programar, incluso sin conocimientos profundos de compiladores o hardware.

## Principios rectores

### 1. Claridad sobre brevedad

Prefiere escribir `.Mov(Eax, 10)` en lugar de `mov eax, 10`. El primero se lee como una frase clara; el segundo requiere conocer la sintaxis exacta del ensamblador tradicional.

### 2. Empatía con quien aprende

Cada concepto nuevo debe venir acompañado de una analogía sencilla y un ejemplo. Si vienes de C#, debes sentirte en casa desde el primer día.

### 3. Sintaxis fluida y encadenada

Inspirado en LINQ y en los builders de C#, las instrucciones se encadenan como métodos. Esto permite el autocompletado en el IDE y detecta errores mientras escribes, no cuando ejecutas el programa.

### 4. C# como lengua franca

El proyecto está escrito en C# y su diseño API refleja la experiencia de desarrollador de C#. Los nombres, la estructura y las convenciones siguen el ecosistema .NET.

### 5. Educación antes que potencia

FluentASM no busca ser el ensamblador más potente del mundo. Busca ser el más fácil de aprender. La potencia puede añadirse después, una vez que la base sea clara y accesible.

## Qué FluentASM NO es

- No es un lenguaje de alto nivel como C# o Java.
- No intenta reemplazar a C#.
- No es un compilador optimizador complejo (por ahora).
- No requiere que entiendas pipelines de CPU, caché o memoria virtual para empezar a usarlo.
- No es una máquina virtual: no ejecuta bytecode intermedio ni necesita un runtime instalado en el equipo.

## Estructura y orden del proyecto

Para mantener el orden, el proyecto se organiza así:

```
src/     → Código fuente principal (varios proyectos: core, ensamblador, administrador, IDE, pruebas)
rsc/     → Recursos (ejemplos .fasm, pruebas de concepto, bibliotecas .bin)
docs/    → Especificaciones y decisiones de diseño (IR, ABI, formatos binarios, ADR)
.github/ → Plantillas de issues y pull requests, y flujos de integración continua
```

| Carpeta | Contenido |
|---|---|
| `src/FluentASM.Core` | Modelo intermedio, catálogo de instrucciones, codificador y emisor |
| `src/FluentASM.Compiler` | El ensamblador: lee `.fasm` y `.fproj`, genera la imagen de salida |
| `src/FluentASM.Admin` | El CLI administrador de producto (`fasm_admin`) |
| `src/FluentASM.IDE` | Entorno de desarrollo integrado (Fase 9) |

Cada carpeta y cada proyecto dentro de `src/` tiene un propósito claro. Si agregas código, colócalo donde corresponda y actualiza este documento si la estructura cambia. Las decisiones de diseño se registran en [docs/adr/](docs/adr/) en lugar de reescribir la historia de estos documentos.

## Archivo de proyecto `.fproj`

Cada programa o aplicación en FluentASM se define mediante un archivo `.fproj` binario. Este archivo es el punto de entrada para el compilador y para las herramientas de FluentASM (compilador, administrador de producto, futuro IDE, etc.).

El `.fproj` utiliza una **tabla de índice** al inicio del archivo que indica la posición (offset) y el tamaño de cada sección de información. Esto permite lectura selectiva y rápida: una herramienta puede saltar directamente a la arquitectura, a las rutas de fuentes o al nombre de salida sin leer todo el archivo secuencialmente.

El `.fproj` contiene:
- La arquitectura de destino (`.x64`, `.x86`, `.arm`, etc.).
- El sistema objetivo (ninguno / bare metal, Windows, Linux, macOS).
- Los archivos fuente `.fasm` y las bibliotecas `.bin`, referenciados por rutas relativas desde la ubicación del `.fproj`.
- El nombre del archivo de salida.

Diseñado estrictamente en binario para permitir lectura rápida por máquinas y herramientas automatizadas.

Como es binario, **no se edita a mano**: se crea y se modifica con el administrador de producto `fasm_admin`, que acepta todos los campos por línea de comandos y ofrece un volcado de texto para poder revisarlo en un pull request. La especificación byte a byte está en [docs/FORMAT_FPROJ.md](docs/FORMAT_FPROJ.md).

## Archivo de solución `.fworkspace`

Cuando se trabaja con más de un proyecto, FluentASM utiliza un archivo de solución llamado `.fworkspace` (similar a un `.sln` de .NET). Este archivo se ubica en el directorio padre de los proyectos y agrupa múltiples archivos `.fproj`.

- Es **opcional para un solo proyecto**: puedes compilar el `.fproj` directamente sin necesidad de un `.fworkspace`.
- Es **requerido para múltiples proyectos**: permite conocer la ubicación de cada proyecto y gestionar dependencias entre ellos.
- Utiliza **formato estrictamente binario** con tabla de índice para lectura rápida por máquinas y herramientas automatizadas.

## Valores del proyecto

- **Accesibilidad:** cualquiera puede contribuir, sin importar su nivel de experiencia.
- **Transparencia:** las decisiones de diseño se documentan en estos documentos y en el registro de decisiones [docs/adr/](docs/adr/), que explica el porqué de cada elección y el coste que acepta.
- **Colaboración:** se valora más la ayuda mutua que la competencia técnica.
- **Sostenibilidad:** el código debe ser mantenible por personas que no son expertas en ensamblador.

## Formato de salida nativo

FluentASM genera binarios nativos compilados, no código intermedio. El archivo de salida es una **imagen plana** (*raw binary*, bytes puros sin cabecera) cuyo nombre refleja la arquitectura de destino:

- `.x64` → para procesadores x86-64 (64 bits)
- `.x86` → para procesadores x86 (32 bits)
- `.arm` → para procesadores ARM

Una imagen plana se ejecuta de dos formas:

- **Desde la herramienta de FluentASM:** el arrancador reserva memoria ejecutable, copia los bytes y salta al punto de entrada. Es la forma de ejecutar y comprobar programas durante el desarrollo.
- **Sobre el hardware (bare metal):** arrancando directamente, sin depender de Windows, Linux ni ningún otro sistema.

**Importante:** una imagen plana **no** la puede cargar un sistema operativo por sí mismo. Windows espera un ejecutable PE y Linux un ELF, con cabeceras y tabla de importaciones. Para obtener un programa que se lance con doble clic o desde una terminal, FluentASM emitirá además ejecutables del sistema operativo (`.exe`, o ELF en Linux) en una fase posterior, manteniendo la misma promesa de la visión: **nativo, sin máquina virtual y sin runtime .NET, Java ni similar**. Los detalles están en [ADR 0003](docs/adr/0003-salidas-imagen-plana.md).

## Bibliotecas nativas (`.bin`)

Las librerías en FluentASM no son paquetes dinámicos ni dependencias externas en tiempo de ejecución. Una librería es simplemente un fichero `.bin` que contiene código máquina ya compilado.

Durante el ensamblado, FluentASM puede **extraer fragmentos de bytes de esos ficheros `.bin` e incrustarlos directamente dentro del binario final**. Esto hace que:
- La aplicación sea rápida.
- No haya procesos externos ni dependencias sueltas al ejecutarse.
- Todo lo necesario viaje empacado en un solo archivo `.x64`, `.x86` o `.arm`.

## Estado actual

**Fase 0 — Cierre de diseño.** Todavía no hay código: primero se define cómo se escribe el lenguaje y
cómo se traduce a bytes, para que cualquiera pueda implementarlo sin preguntar nada.

Ya están escritos y son la referencia obligatoria:

- [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md) — gramática, registros, catálogo de instrucciones y semántica.
- [docs/IR.md](docs/IR.md) — modelo intermedio: operandos, secciones, símbolos y relocalizaciones.
- [docs/ABI.md](docs/ABI.md) — convenciones de llamada y modelo de ejecución.
- [docs/FORMAT_FPROJ.md](docs/FORMAT_FPROJ.md) y [docs/FORMAT_FWORKSPACE.md](docs/FORMAT_FWORKSPACE.md) — formatos binarios byte a byte.
- [docs/adr/](docs/adr/) — registro de decisiones de diseño.
- [roadmap.md](roadmap.md) — fases, criterios de salida y orden de trabajo.

El siguiente paso es la Fase 1: generar bytes x64 correctos, verificarlos contra un oráculo y
ejecutarlos en memoria.