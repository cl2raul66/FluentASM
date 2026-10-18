# Formato binario del archivo de solución `.fworkspace`

Especificación **byte a byte** del archivo de solución de FluentASM. Igual que el `.fproj`, es
estrictamente binario y solo se manipula con `fasm_admin`.

Decisión asociada: [ADR 0002](adr/0002-fproj-binario.md).

---

## 1. Cuándo hace falta

| Caso | ¿Necesario? |
|---|---|
| Un único `.fproj` | **No.** Se ensambla directamente el `.fproj` |
| Dos o más proyectos | **Sí.** Es la única forma de conocer su ubicación y su orden de compilación |
| El usuario quiere compilar todo de una vez | Sí, es más cómodo que invocar cada `.fproj` por separado |

Se ubica en el directorio **padre** de los proyectos y guarda rutas **relativas** a cada `.fproj`.

---

## 2. Estructura general

```
┌──────────────────────────────┐ 0
│ Cabecera (16 bytes)          │
├──────────────────────────────┤ 16
│ Tabla de índice (16 × N)     │
├──────────────────────────────┤ 16 + 16 × N
│ Bloque de datos 1            │ ← alineado a 8
│ Bloque de datos 2            │
│ ...                          │
└──────────────────────────────┘
```

La cabecera y las entradas del índice tienen **exactamente** la misma forma que en el `.fproj`
(ver [FORMAT_FPROJ.md](FORMAT_FPROJ.md), secciones 3 y 4), cambiando solo el `Magic`.

---

## 3. Cabecera (16 bytes)

| Offset | Bytes | Campo | Tipo | Valor |
|---|---|---|---|---|
| 0 | 4 | `Magic` | ASCII | `FWKS` (`46 57 4B 53`) |
| 4 | 2 | `VersionMajor` | u16 | 1 |
| 6 | 2 | `VersionMinor` | u16 | 0 |
| 8 | 2 | `EntryCount` | u16 | Número de entradas del índice |
| 10 | 2 | `Flags` | u16 | Reservado, 0 en la versión 1.0 |
| 12 | 4 | `IndexOffset` | u32 | Normalmente 16 |

## 4. Catálogo de etiquetas (`Tag`)

| Tag | Nombre | Tipo de bloque | Obligatorio |
|---|---|---|---|
| `0x0001` | `WorkspaceName` | Cadena | Sí |
| `0x0002` | `ProductVersion` | Versión (`u16`×3 + cadena) | Sí |
| `0x0003` | `Projects` | Lista de proyectos | Sí |
| `0x0004` | `Dependencies` | Lista de aristas | Sí (puede estar vacía) |
| `0x0005` | `BuildDefaults` | Opciones globales | No |
| `0x0006` | `Description` | Cadena | No |
| `0x000C` | `ToolVersion` | Cadena | Sí |
| `0x000D` | `CreatedUtc` | u64 | Sí |
| `0x000E` | `ModifiedUtc` | u64 | Sí |
| `0x00FF` | `Extension` | Libre | No |
| `0xFFFF` | `IndexTerminator` | Vacío | No |

Los tipos elementales, la alineación, el CRC32 y las reglas de rutas son **los mismos** que en
[FORMAT_FPROJ.md](FORMAT_FPROJ.md) (secciones 5.1, 5.2, 6 y 7) y no se repiten aquí.

### 4.1 Bloque `Projects`

```text
u32  ProjectCount
u32  ProjectCount × {
       u16  ProjectId      // identificador estable dentro del workspace, único
       u16  Flags          // reservado, 0
       u32  PathLength
       u8[] PathBytes      // ruta relativa al .fproj, con '/'
       u32  NameLength
       u8[] NameBytes      // nombre del proyecto, para mostrar en 'list'
     }
```

`ProjectId` es un número pequeño y estable: se asigna al añadir el proyecto y **no** cambia al
renombrar rutas. Las dependencias se expresan con él, no con índices posicionales, para que quitar un
proyecto de la lista no corrompa el grafo.

### 4.2 Bloque `Dependencies`

```text
u32  EdgeCount
u32  EdgeCount × {
       u16  FromProjectId   // el que depende
       u16  ToProjectId     // la dependencia (debe compilarse antes)
     }
```

**Semántica explícita, sin ambigüedad:** la arista `A → B` significa *«A depende de B»*, por lo que
**B se compila antes que A**. Un proyecto no puede depender de sí mismo (`FPWKS0006`).

### 4.3 Bloque `BuildDefaults`

```text
u32  Flags            // bit 0 = usar valores por defecto para arquitectura/sistema/ABI
u8   Architecture     // 1=x64, 2=x86, 3=arm (0 = no definida)
u8   TargetSystem     // 0=bare-metal, 1=windows, 2=linux, 3=macos
u8   Abi              // 0=none, 1=win64, 2=systemv
u8   Reserved
u32  OutputDirLength
u8[] OutputDirBytes   // directorio de salida relativo al .fworkspace (por defecto "bin")
```

Es **opcional** y solo aporta valores por defecto: lo que declare cada `.fproj` manda.

---

## 5. Orden de compilación

1. Se construye el grafo dirigido con las aristas de `Dependencies`.
2. Se calcula un orden topológico: **las dependencias primero**.
3. Si hay un ciclo, se falla con `FPWKS0007` y el mensaje **muestra el ciclo completo** con los
   nombres de los proyectos, no solo un identificador.
4. El orden es determinista incluso cuando hay varias soluciones válidas: a igualdad de
   disponibilidad, se compila antes el proyecto cuyo `ProjectId` es menor. Esto evita que el orden de
   compilación cambie entre máquinas.

---

## 6. Ejemplo hexadecimal comentado

Workspace `MiSolucion` versión `1.0.0`, con dos proyectos: `ProyectoA` (en `A/proyecto.fproj`) y
`ProyectoB` (en `B/modulo.fproj`), donde **A depende de B**. Siete bloques ⇒ índice de
`16 + 7 × 16 = 128` bytes y primer bloque de datos en el offset **128** (`0x80`, múltiplo de 8).

Tamaño de cada bloque (para poder seguir los offsets):

| Bloque | Cálculo | Tamaño |
|---|---|---|
| `WorkspaceName` | `4 + 10` (`"MiSolucion"`) | 14 |
| `ProductVersion` | `2+2+2` + `4+0` | 10 |
| `Projects` | `4 + (4+4+16+4+9) + (4+4+14+4+9)` | 76 |
| `Dependencies` | `4 + 4` | 8 |
| `ToolVersion` | `4 + 5` (`"0.1.0"`) | 9 |
| `CreatedUtc` | `u64` | 8 |
| `ModifiedUtc` | `u64` | 8 |

Reparto con alineación a 8: `0x80` WorkspaceName → `0x90` ProductVersion → `0xA0` Projects →
`0xF0` Dependencies → `0xF8` ToolVersion → `0x108` CreatedUtc → `0x110` ModifiedUtc.

```
offset  bytes                        significado
0x00    46 57 4B 53                  magic "FWKS"
0x04    01 00                        VersionMajor = 1
0x06    00 00                        VersionMinor = 0
0x08    07 00                        EntryCount = 7
0x0A    00 00                        Flags = 0
0x0C    10 00 00 00                  IndexOffset = 16
0x10    01 00 00 00 80 00 00 00 0E 00 00 00 00 00 00 00   tag 0x0001 WorkspaceName, offset 0x80, 14 bytes
0x20    02 00 00 00 90 00 00 00 0A 00 00 00 00 00 00 00   tag 0x0002 ProductVersion,  offset 0x90, 10 bytes
0x30    03 00 00 00 A0 00 00 00 4C 00 00 00 00 00 00 00   tag 0x0003 Projects,        offset 0xA0, 76 bytes
0x40    04 00 00 00 F0 00 00 00 08 00 00 00 00 00 00 00   tag 0x0004 Dependencies,    offset 0xF0,  8 bytes
0x50    0C 00 00 00 F8 00 00 00 09 00 00 00 00 00 00 00   tag 0x000C ToolVersion,     offset 0xF8,  9 bytes
0x60    0D 00 00 00 08 01 00 00 08 00 00 00 00 00 00 00   tag 0x000D CreatedUtc,      offset 0x108, 8 bytes
0x70    0E 00 00 00 10 01 00 00 08 00 00 00 00 00 00 00   tag 0x000E ModifiedUtc,     offset 0x110, 8 bytes
```

**Bloque `Projects` en `0xA0` (76 bytes):**

```
02 00 00 00                                     ProjectCount = 2
01 00 00 00                                     ProjectId = 1, Flags = 0
10 00 00 00                                     ruta: longitud 16
41 2F 70 72 6F 79 65 63 74 6F 2E 66 70 72 6F 6A   "A/proyecto.fproj"
09 00 00 00                                     nombre: longitud 9
50 72 6F 79 65 63 74 6F 41                        "ProyectoA"
02 00 00 00                                     ProjectId = 2, Flags = 0
0E 00 00 00                                     ruta: longitud 14
42 2F 6D 6F 64 75 6C 6F 2E 66 70 72 6F 6A         "B/modulo.fproj"
09 00 00 00                                     nombre: longitud 9
50 72 6F 79 65 63 74 6F 42                        "ProyectoB"
```

**Bloque `Dependencies` en `0xF0` (8 bytes):**

```
01 00 00 00                                     EdgeCount = 1
01 00 02 00                                     arista: A(1) → B(2), es decir "A depende de B"
```

Orden de compilación resultante: **ProyectoB, luego ProyectoA**. Volcado de texto equivalente:

```text
fworkspace v1.0  (7 bloques)
WorkspaceName    MiSolucion
ProductVersion   1.0.0
Projects         A → A/proyecto.fproj      (ProyectoA)
                 B → B/modulo.fproj        (ProyectoB)
Dependencies     A depende de B
BuildDefaults    (sin valores por defecto)
Build order      ProyectoB, ProyectoA
```

---

## 7. Comandos de la CLI

| Comando | Efecto sobre el `.fworkspace` |
|---|---|
| `fasm_admin init [nombre]` | Crea un workspace vacío en el directorio actual |
| `fasm_admin add <ruta/proyecto.fproj>` | Añade el proyecto con un `ProjectId` nuevo; añade un `.fproj` de plantilla si no existe |
| `fasm_admin remove <nombre-proyecto>` | Quita el proyecto **y todas sus aristas** |
| `fasm_admin dep add <A> <B>` | Registra que A depende de B, rechazando ciclos en el momento |
| `fasm_admin dep remove <A> <B>` | Elimina la arista |
| `fasm_admin list` | Muestra proyectos, arquitectura, estado de compilación y orden resuelto |
| `fasm_admin build` | Compila todo el workspace en orden topológico |
| `fasm_admin build <proyecto>` | Compila un proyecto y, si hace falta, sus dependencias |
| `fasm_admin clean` | Borra las imágenes generadas de todos los proyectos |
| `fasm_admin run <proyecto>` | Ejecuta una imagen generada, solo si la plataforma coincide |
| `fasm_admin verify` | Valida el workspace, los `.fproj` referenciados y el grafo |

`dep add` **rechaza** la arista que crearía un ciclo, de modo que el grafo del archivo siempre es
válido y el error se descubre al escribir, no al compilar.

---

## 8. Validación

| Comprobación | Error |
|---|---|
| Magic distinto de `FWKS` | `FPWKS0001` |
| Cabecera o índice fuera de límites | `FPWKS0002` |
| Falta `Projects` o contiene cero proyectos | `FPWKS0003` |
| `ProjectId` duplicado | `FPWKS0004` |
| Arista que referencia un `ProjectId` inexistente | `FPWKS0005` |
| Proyecto que depende de sí mismo | `FPWKS0006` |
| Ciclo en el grafo | `FPWKS0007`, con el ciclo completo en el mensaje |
| Ruta de proyecto absoluta, con `..` o inexistente | `FPWKS0008` |
| El `.fproj` referenciado no existe o es inválido | `FPWKS0009`, indicando el proyecto y el error original |
| Rutas duplicadas apuntando al mismo `.fproj` | `FPWKS0010` |

---

## 9. Pruebas obligatorias del formato

1. **Bytes dorados** del ejemplo de la sección 6.
2. **Ida y vuelta** byte a byte para workspaces de 1, 2 y 10 proyectos.
3. **Grafo:** orden correcto con dependencias en cadena y en diamante; detección de ciclo con el
   ciclo completo en el mensaje.
4. **Estabilidad de `ProjectId`:** quitar un proyecto intermedio no altera las dependencias de los
   demás.
5. **Determinismo del orden:** dos ejecuciones y dos máquinas producen el mismo orden de compilación.
