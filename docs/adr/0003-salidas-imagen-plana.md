# ADR 0003 — Salidas: imagen plana primero, ejecutable del SO después

- **Estado:** Aceptada
- **Fecha:** 2026-10-18
- **Decide:** mantenedor del proyecto

## Contexto

La visión y la referencia de sintaxis afirmaban que el resultado de ensamblar es un **binario crudo**
con extensión por arquitectura (`.x64`, `.x86`, `.arm`) y que ese archivo "puede ejecutarse con o sin
sistema operativo: con OS, como un programa nativo cualquiera".

Eso segundo es **incorrecto tal como está escrito**. Una imagen plana (una secuencia de bytes sin
cabecera) no es un formato que Windows ni Linux sepan cargar: el cargador del sistema operativo
espera PE en Windows o ELF en Linux (cabeceras, secciones, y una tabla de importaciones cuando se
llaman funciones del sistema). Un archivo llamado `programa.x64` con código máquina correcto **no se
puede ejecutar** con doble clic ni desde una terminal.

Formas reales de ejecutar una imagen plana:

1. **En memoria**, desde un cargador propio (reservar memoria ejecutable, copiar los bytes, saltar al
   punto de entrada). Es lo que hará la herramienta de FluentASM y sus pruebas.
2. **Sobre el hardware, sin sistema operativo** (bare metal), donde no existe cargador y el propio
   firmware salta a esos bytes.
3. **Envuelta en un ejecutable del sistema operativo** (PE/ELF), momento en el que dejan de ser una
   imagen plana.

Además, mientras no exista un emisor PE/ELF con tabla de importaciones, el código generado **no
puede llamar a nada externo**: ni `printf`, ni `ExitProcess`, ni funciones de la API de Windows.
Lo único observable es el valor de retorno y lo que el código escriba directamente en memoria.

## Decisión

1. La **primera** salida de FluentASM es la **imagen plana** (`.x64`, `.x86`, `.arm`), ejecutada
   **en memoria** por la herramienta y por las pruebas. Es la ruta de la Fase 1.
2. El **ejecutable del sistema operativo** (PE en Windows, ELF en Linux) es una salida **adicional**
   de una fase posterior, necesaria para llamar a la API del sistema o al CRT. A partir de ahí la
   extensión puede ser `.exe`/`.dll` en lugar de `.x64`.
3. La **ejecución bare metal** se tratará como su propio camino (Fase 7), no como un caso de la
   imagen plana "de escritorio".
4. La documentación de visión y sintaxis se corrige para no prometer ejecución autónoma por el
   sistema operativo de una imagen plana.

### Reglas de la imagen plana de la Fase 1

| Regla | Valor |
|---|---|
| Cabecera en el archivo | **Ninguna** (bytes puros) |
| Punto de entrada | Símbolo declarado con `entry`; por defecto, el inicio de `.text` |
| Base de carga | Arbitraria. El código debe ser **independiente de posición (PIC)** |
| Direccionamiento a símbolos | **RIP-relativo**; el uso de direcciones absolutas queda prohibido en Fase 1 |
| Llamadas externas | Prohibidas (no hay imports ni CRT) |
| Secciones incluidas | `.text` y `.data`. `.bss` se reserva en memoria y **no** ocupa bytes en el archivo |
| Orden de secciones en el archivo | `.text`, luego `.data`, tal como aparecen en la imagen |
| Relocalizaciones de carga | Si en el futuro se admiten direcciones absolutas, se emitirá un archivo auxiliar `*.reloc` opcional; el binario seguirá siendo bytes puros |

La restricción de PIC es lo que permite ejecutar la imagen en cualquier dirección de memoria sin
parchear ni un solo byte, y es verificable con una prueba: cargar la misma imagen en dos direcciones
distintas y comprobar que el resultado es idéntico.

## Consecuencias

### Positivas

- El ciclo de verificación se cierra muy pronto: bytes generados → ejecutados → resultado comprobado.
- Sin PE/ELF no hay que implementar cabeceras, secciones ni tabla de importaciones para empezar.
- Coherente con el valor "educación antes que potencia": primero se aprende a generar bytes
  correctos, después se aprende a empaquetarlos.
- La restricción PIC obliga a un diseño limpio desde el principio, en lugar de parchear direcciones
  absolutas más tarde.

### Negativas y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Sensación de "no sirve para nada hasta la Fase 5" porque no se puede lanzar un `.exe` | Una prueba de ejecución en memoria demuestra resultados reales desde la Fase 1; el roadmap publica en qué fase llega el `.exe` |
| El usuario escribe direcciones absolutas y el ensamblador se niega | Error explícito con código de diagnóstico y sugerencia: usar `[rel simbolo]` |
| Bare metal necesita binario **con** dirección de carga fija, justo lo contrario que el PIC | Fase 7 define su propio modo (con direcciones absolutas y base conocida), sin relajar la regla de Fase 1 |

## Alternativas consideradas

1. **Empezar emitiendo PE `.exe` directamente.** Permitiría lanzar programas con doble clic desde el
   primer día, pero obliga a implementar cabeceras PE, alineaciones, tabla de importaciones y
   relocalizaciones **antes** de saber siquiera si el encoder genera bytes correctos.
2. **Enviar el binario plano a un enlazador externo (link.exe, ld) para producir el ejecutable.**
   Rápido, pero introduce una dependencia de herramienta externa que contradice la visión de
   "todo lo necesario viaja empacado" y del propio ensamblador autocontenido.
