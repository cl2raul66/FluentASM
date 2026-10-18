# Formato binario del archivo de proyecto `.fproj`

Especificación **byte a byte** del archivo de proyecto de FluentASM. Es un formato
**estrictamente binario**: no se edita con un editor de texto, se crea y se modifica con
`fasm_admin`.

Decisión asociada: [ADR 0002](adr/0002-fproj-binario.md).

---

## 1. Principios del formato

1. **Lectura selectiva.** Una tabla de índice al principio permite leer la arquitectura sin recorrer
   las rutas de archivos, o leer el nombre y la versión sin leer nada más.
2. **Sin ambigüedad.** Anchuras fijas, *little-endian* siempre, sin varints, sin BOM, sin finales de
   línea.
3. **Extensible.** Las versiones MINOR solo añaden etiquetas; un lector antiguo puede ignorarlas.
4. **Autovalidable.** El lector comprueba cabecera, límites de la tabla de índice y coherencia de
   cada bloque antes de usar cualquier dato.

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

Los bloques de datos pueden estar en **cualquier orden** y no tienen por qué aparecer en el mismo
orden que sus entradas en el índice: el índice es la única fuente de verdad.

---

## 3. Cabecera (16 bytes)

| Offset | Bytes | Campo | Tipo | Valor |
|---|---|---|---|---|
| 0 | 4 | `Magic` | ASCII | `FPRJ` (`46 50 52 4A`) |
| 4 | 2 | `VersionMajor` | u16 | 1 para esta versión |
| 6 | 2 | `VersionMinor` | u16 | 0 |
| 8 | 2 | `EntryCount` | u16 | Número de entradas del índice |
| 10 | 2 | `Flags` | u16 | Reservado. Debe ser 0 en la versión 1.0. Un bit desconocido ⇒ error |
| 12 | 4 | `IndexOffset` | u32 | Offset absoluto del inicio de la tabla de índice (normalmente 16) |

## 4. Entrada de la tabla de índice (16 bytes)

| Offset | Bytes | Campo | Tipo | Descripción |
|---|---|---|---|---|
| 0 | 2 | `Tag` | u16 | Identificador del bloque (sección 5) |
| 2 | 2 | `Flags` | u16 | bit 0 = atenuada (*opcional/ignorable*), bit 1 = el bloque termina con CRC32, resto reservado |
| 4 | 4 | `Offset` | u32 | Offset absoluto del bloque desde el inicio del archivo |
| 8 | 4 | `Size` | u32 | Tamaño del bloque **sin** contar el relleno de alineación ni el CRC32 |
| 12 | 4 | `Reserved` | u32 | Debe ser 0. Reservado para crecimiento sin cambiar el tamaño de la entrada |

Una entrada con `Tag` desconocido y `Flags` bit 0 activo **se ignora sin error**. Un `Tag`
desconocido sin ese bit ⇒ error `FPROJ0007` (archivo de una versión MINOR más reciente).

---

## 5. Catálogo de etiquetas (`Tag`)

| Tag | Nombre | Tipo de bloque | Obligatorio |
|---|---|---|---|
| `0x0001` | `ProgramName` | Cadena | Sí |
| `0x0002` | `ProductVersion` | Versión | Sí |
| `0x0003` | `Architecture` | u8 | Sí |
| `0x0004` | `TargetSystem` | u8 | Sí |
| `0x0005` | `Abi` | u8 | Sí |
| `0x0006` | `Sources` | Lista de cadenas | Sí (puede estar vacía) |
| `0x0007` | `Libraries` | Lista de cadenas | No |
| `0x0008` | `OutputName` | Cadena | Sí |
| `0x0009` | `EntrySymbol` | Cadena | No |
| `0x000A` | `Description` | Cadena | No |
| `0x000B` | `BuildOptions` | u32 | No |
| `0x000C` | `ToolVersion` | Cadena | Sí |
| `0x000D` | `CreatedUtc` | u64 | Sí |
| `0x000E` | `ModifiedUtc` | u64 | Sí |
| `0x00FF` | `Extension` | Libre | No |
| `0xFFFF` | `IndexTerminator` | Vacío | No |

### 5.1 Tipos elementales

| Tipo | Codificación |
|---|---|
| `u8`, `u16`, `u32`, `u64` | Entero sin signo, *little-endian*, anchura fija |
| Cadena | `u32` longitud en bytes + bytes UTF-8 (sin BOM, sin terminador nulo) |
| Lista de cadenas | `u32` número de elementos + cada elemento como cadena |
| Versión | `u16` mayor + `u16` menor + `u16` parche + cadena de prelanzamiento (vacía si no hay) |
| Marca de tiempo | `u64` segundos desde 1970-01-01 UTC |

### 5.2 Valores de enumeración

| Campo | Valor | Significado |
|---|---|---|
| `Architecture` | 1 / 2 / 3 | `x64` / `x86` / `arm` |
| `TargetSystem` | 0 / 1 / 2 / 3 | `bare-metal` / `windows` / `linux` / `macos` |
| `Abi` | 0 / 1 / 2 | `none` / `win64` / `systemv` |

Un valor fuera de rango ⇒ error `FPROJ0007`, con el valor recibido en el mensaje.

### 5.3 `BuildOptions` (u32, campo de bits)

| Bit | Significado | Valor por defecto |
|---|---|---|
| 0 | Exigir código independiente de posición (PIC) | 1 (activado) |
| 1 | Ajuste automático de saltos (layout iterativo) | 1 |
| 2 | Incluir símbolos de depuración en la imagen | 0 |
| 3 | Prohibir símbolos externos (imagen plana pura) | 1 |
| 4–31 | Reservado | 0 |

Los valores por defecto de la tabla se aplican cuando el bloque está ausente.

---

## 6. Alineación y relleno

1. Cada bloque de datos empieza en un offset **múltiplo de 8**.
2. El relleno se hace con bytes `00` y **no** se cuenta en `Size`.
3. El bloque de una cadena no lleva relleno interno: `Size` es exactamente
   `4 + longitud_en_bytes`.
4. Si `Flags` bit 1 está activo, los 4 bytes **después** del bloque (tras el relleno) contienen el
   CRC32 (polinomio estándar `0xEDB88320`, reflejado) de los `Size` bytes del bloque. Corregir el
   CRC es responsabilidad de `fasm_admin`, nunca del usuario.

---

## 7. Rutas

| Regla | Detalle |
|---|---|
| Base | Relativas a la ubicación del archivo `.fproj` |
| Separador | Siempre `/`, incluso en Windows |
| Prohibido | Rutas absolutas, unidades (`C:`), y cualquier `..` que escape del directorio del proyecto |
| Comparación | Sin distinguir mayúsculas en Windows; con distinción en Linux |
| Almacenamiento | El nombre tal cual lo escribió el usuario, sin normalizar a minúsculas |

Un archivo `.fasm` o `.bin` referenciado debe existir **dentro** del mismo directorio del `.fproj` o
en un subdirectorio de este, tal como exige [VISION.md](../VISION.md).

---

## 8. Ejemplo hexadecimal comentado

Proyecto `Hola`: nombre `Hola`, versión `0.1.0`, arquitectura `x64`, sistema `windows`, ABI `win64`,
fuente `src/main.fasm`, salida `bin/hola.x64`, símbolo de entrada `main`. Once bloques ⇒ la tabla de
índice ocupa `16 × 11 = 176` bytes y el primer bloque de datos empieza en el offset **192** (`0xC0`).

**Cabecera y primeras entradas del índice (offsets `0x00`–`0x3F`):**

```
offset  bytes                                         significado
0x00    46 50 52 4A                                   magic "FPRJ"
0x04    01 00                                         VersionMajor = 1
0x06    00 00                                         VersionMinor = 0
0x08    0B 00                                         EntryCount = 11
0x0A    00 00                                         Flags = 0
0x0C    10 00 00 00                                   IndexOffset = 16
0x10    01 00 00 00 C0 00 00 00 08 00 00 00 00 00 00 00   tag 0x0001 ProgramName, offset 0xC0, tamaño 8
0x20    03 00 00 00 C8 00 00 00 01 00 00 00 00 00 00 00   tag 0x0003 Architecture, offset 0xC8, tamaño 1
0x30    04 00 00 00 D0 00 00 00 01 00 00 00 00 00 00 00   tag 0x0004 TargetSystem, offset 0xD0, tamaño 1
```

**Primeros bloques de datos (offset `0xC0`):**

```
0xC0    04 00 00 00 48 6F 6C 61                        ProgramName: longitud 4, "Hola"
0xC8    01 00 00 00 00 00 00 00                        Architecture: 1 (x64) + relleno a 8
0xD0    01 00 00 00 00 00 00 00                        TargetSystem: 1 (windows) + relleno a 8
```

Lectura selectiva en acción: leer la arquitectura de este archivo cuesta **4 lecturas** (cabecera,
entrada del índice, salto y bloque), no recorrer el archivo completo.

**Volcado de texto equivalente** (`fasm_admin dump hola.fproj`), que es lo que se pega en un pull
request cuando cambia un `.fproj`:

```text
fproj v1.0  (11 bloques, 192 bytes de datos)
ProgramName      Hola
ProductVersion   0.1.0
Architecture     x64
TargetSystem     windows
Abi              win64
Sources          src/main.fasm
Libraries        (ninguna)
OutputName       bin/hola.x64
EntrySymbol      main
BuildOptions     pic=on relax=on debug=off no-extern=on
ToolVersion      0.1.0
CreatedUtc       2026-10-18T12:00:00Z
ModifiedUtc      2026-10-18T12:00:00Z
```

---

## 9. Creación y edición desde la CLI

Como el formato es binario, **la CLI es la única vía de autoría**. Todos los campos se pueden dar por
línea de comandos, sin necesidad de ningún archivo de texto intermedio:

```powershell
fasm_admin new hola.fproj `
    --name Hola `
    --version 0.1.0 `
    --arch x64 `
    --target windows `
    --abi win64 `
    --source src/main.fasm `
    --output bin/hola.x64 `
    --entry main

fasm_admin get hola.fproj --arch          # x64
fasm_admin set hola.fproj --entry main2
fasm_admin dump hola.fproj                # volcado de texto como el de la sección 8
```

| Comando | Efecto |
|---|---|
| `new` | Crea un `.fproj` desde cero con los campos indicados |
| `get` | Lee un único campo (lectura selectiva real, no vuelca el archivo) |
| `set` | Modifica un campo y reescribe el archivo (actualiza `ModifiedUtc`) |
| `dump` | Volcado de texto estable y ordenado |
| `verify` | Valida cabecera, índice, bloques, rutas y CRC32 |

---

## 10. Validación

| Comprobación | Error |
|---|---|
| Magic distinto de `FPRJ` | `FPROJ0001` (indica si el archivo parece un `.fworkspace`) |
| `EntryCount` mayor que lo que cabe en el archivo | `FPROJ0002` |
| Bloque fuera de los límites del archivo | `FPROJ0003` |
| Bloque solapado con otro o con el índice | `FPROJ0004` |
| Falta un bloque obligatorio | `FPROJ0005` |
| Ruta absoluta, con `..` o fuera del proyecto | `FPROJ0006` |
| Enumeración o etiqueta desconocida no ignorable | `FPROJ0007` |
| CRC32 incorrecto | `FPROJ0008` |
| Cadena UTF-8 no válida | `FPROJ0009` |

Un `.fproj` inválido **nunca** se usa parcialmente: el ensamblador aborta indicando el error y el
offset donde se detectó.

---

## 11. Compatibilidad entre versiones

| Cambio | Qué implica | Cómo lo trata un lector antiguo |
|---|---|---|
| MINOR (1.0 → 1.1) | Se añaden etiquetas nuevas, normalmente con `Flags` bit 0 activo | Las ignora y sigue funcionando |
| MAJOR (1.x → 2.0) | Cambia la cabecera o el significado de un bloque existente | Falla con `FPROJ0007` y no intenta interpretarlo |
| `Reserved` distinto de 0 | Error | Falla: indica una versión que este lector no entiende |

Regla para las herramientas: al escribir, siempre la versión MINOR **más baja** que cubra los datos
usados. Añadir una etiqueta de adorno no debe obligar a subir la versión del archivo de todos los
usuarios.

---

## 12. Pruebas obligatorias del formato

1. **Bytes dorados:** el ejemplo de la sección 8 se genera con `new` y se compara byte a byte con un
   archivo de referencia versionado en el repositorio.
2. **Ida y vuelta:** `parse(serialize(x))` es igual a `x` para los archivos de referencia.
3. **Lectura selectiva:** `get --arch` no lee más de 2 bloques (se comprueba instrumentando el lector).
4. **Ficheros corruptos:** cada error de la sección 10 tiene su archivo de prueba y su mensaje.
5. **Compatibilidad:** un archivo con una etiqueta MINOR futura se lee sin error si está marcada como
   ignorable.
