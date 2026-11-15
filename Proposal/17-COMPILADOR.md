# 17 — Compilador

> **Normativo para la implementación.** Arquitectura del compilador, contratos entre etapas, proyectos .NET, milestones y estrategia de pruebas. Un equipo que siga este documento y los documentos 00–16 obtiene un compilador conforme.

---

## 1. Alcance

### 1.1 Qué construye

Un ensamblador **de una sola pasada lógica sobre un AST completo**: lee `.fasm`, valida, codifica y emite una imagen plana. Sin optimizador, sin enlazador dinámico, sin *runtime*.

```
.fasm  →  Lexer  →  Parser  →  Binder  →  Checker  →  Selector  →  Encoder  →  Linker  →  .x64
                                     ↘ Diagnósticos ↗
```

### 1.2 Qué no construye (v1)

| Ausente | Motivo |
|---|---|
| Optimizador | D-025: el mapeo es 1:1 y determinista |
| Enlazador dinámico | `FASM5001`: no hay símbolos externos |
| Reubicación en tiempo de carga | La imagen es plana y PIC |
| Gestión de registros | El programador nombra los registros físicos |
| Macro sistema | No hay macros en v1 (reservado) |
| Soporte de 32 bits | Solo x64 |

---

## 2. Pipeline

### 2.1 Etapas y contratos

| # | Etapa | Entrada | Salida | Invariante al terminar |
|---:|---|---|---|---|
| 1 | **Lexer** | texto UTF-8 | `Token[]` | Cada token tiene `Kind`, `Text`, `Line`, `Column`, `Offset` |
| 2 | **Parser** | tokens | `Ast` | Árbol parcial válido incluso con errores; nodos con `Span` |
| 3 | **Binder** | AST | AST + `SymbolTable` | Cada `identifier`/`@label` resuelto a un símbolo con `Kind` y `Owner` |
| 4 | **Checker ATM** | AST ligado | AST anotado | Cada receptor tiene `RegisterFile`, `Width`, `View`; cada magnitud tiene `Bits` |
| 5 | **Checker de política** | AST anotado | AST anotado | Ningún `integerSource` en posición de dato (`FASM2007`) |
| 6 | **Selector de verbos** | AST anotado | `IrInstruction[]` | Cada acción → una instrucción abstracta con plantilla |
| 7 | **Encoder** | `IrInstruction[]` | `byte[]` + `Relocation[]` | Cada instrucción tiene bytes y tamaño; cada reloc tiene tipo y offset |
| 8 | **Linker** | secciones + símbolos + relocs | imagen | Todas las relocs resueltas; `entry` correcto; mapa emitido |

Cada etapa es **pura** respecto de su entrada: no muta el AST de la etapa anterior, produce una estructura nueva. Esto permite ejecutar el pipeline dos veces (para determinismo) y comparar.

### 2.2 Orden de validación dentro de una acción

```
1. ¿El receptor existe y es un contenedor?          → FASM2003 / FASM3001
2. ¿La vista es válida para ese contenedor?         → FASM3024 / FASM3025
3. ¿El verbo existe para ese receptor?              → FASM3001
4. ¿El número de argumentos coincide?               → FASM4001
5. ¿Cada argumento tiene la clase correcta?         → FASM4002
6. ¿El argumento es forma y no dato?                → FASM2007
7. ¿El target soporta la instrucción?               → FASM6001 / FASM3080
8. ¿Se cumplen las restricciones de operandos?      → FASM4003…4036
9. ¿La ABI lo permite?                              → FASM5xxx
```

El primer fallo de cada categoría se reporta; los siguientes de la misma categoría se agrupan. Nunca se codifica una instrucción con un diagnóstico de error abierto.

### 2.3 Errores y recuperación

- El lexer y el parser **nunca lanzan** por entrada inválida; emiten diagnósticos y siguen.
- Un error en una acción no impide validar las demás.
- El compilador **no escribe** la imagen si hay algún error de severidad `error`. Los `warning` e `info` no la bloquean.
- Máximo 50 errores por archivo (`FASM1009`).

---

## 3. Arquitectura de proyectos .NET

### 3.1 Proyectos

```
src/
  FluentASM.Core/          → biblioteca: lexer, parser, binder, checkers, encoder, linker
    Syntax/                → Token, Lexer, Parser, AstNodes
    Semantics/             → SymbolTable, RegisterModel, CapacityModel, VerbCatalog
    Encoding/              → OpcodeTable, Encoder, Relocation, ImageBuilder
    Diagnostics/           → Diagnostic, DiagnosticBag, Catalog
    Api/                   → Compilation, CompilationOptions, CompilationResult
  FluentASM.Compiler/      → CLI `fluentasm` (build, check, disasm, map, migrate)
  FluentASM.IDE/           → WinUI 3 (Fase 9); consume Core vía LSP
  FluentASM.Sample/        → pruebas manuales; no versionado
  FluentASM.Admin/         → `fasm_admin` (producto, versiones)
```

`Core` no depende de `Microsoft.CodeAnalysis` ni de ningún paquete de red. Solo BCL. Objetivo: `net10.0`, `nullable enable`, `TreatWarningsAsErrors`.

### 3.2 Dependencias

```
Compiler → Core
IDE      → Core (o LSP)
Sample   → Core
Admin    → (nada)
```

`Core` no referencia a nadie. Ningún proyecto referencia servicios en línea.

### 3.3 Estructuras clave

```csharp
record Token(TokenKind Kind, string Text, int Line, int Column, int Offset, int Length);
record Span(int Start, int Length);

abstract record AstNode(Span Span);
record ProgramNode(string Name, ImmutableArray<AttributeNode> Attributes,
                   ImmutableArray<SectionNode> Sections, Span Span);
record RoutineNode(string Name, ImmutableArray<AttributeNode> Attributes,
                   ImmutableArray<ParameterNode> Parameters,
                   ImmutableArray<LocalNode> Locals,
                   ImmutableArray<StatementNode> Body, Span Span);
record ActionNode(ReceiverNode Receiver, VerbNode Verb, Span Span);
record MemoryNode(MagnitudeNode Capacity, AddressNode Address, Span Span);
record MagnitudeNode(string Unit, long Value, int Bits, Span Span);

record Symbol(string Name, SymbolKind Kind, SectionKind Section,
              int? Offset, int Size, bool IsGlobal, Symbol Owner);
record IrInstruction(VerbId Verb, Operand[] Operands, EncodingTemplate Template);
record Relocation(RelocationKind Kind, int Offset, Symbol Target);
```

### 3.4 Catálogo de verbos como datos

El catálogo no se escribe en `switch` dispersos. Es una tabla declarativa:

```csharp
new VerbSpec(
    Id: "add",
    Receivers: new[] { RegisterFile.Gpr },
    Signature: new[] { ArgKind.Rm64 },
    Encoding: EncodingId.Add_Rm64_R64,
    FlagsWritten: Flags.All,
    Target: TargetProfile.X64V1);
```

Ventajas: el IntelliSense (doc 18), el checker y el encoder leen **la misma** tabla. Añadir un verbo es añadir una fila, no tocar tres archivos.

---

## 4. Representación intermedia

### 4.1 IR de instrucción

```csharp
enum OperandKind { Reg, Mem, Imm, Label, Port, Msr, Tuple, FlagState }

record Operand(OperandKind Kind, int Reg, int Width, MemoryRef? Mem,
               long Imm, Symbol? Label);
```

No hay IR de alto nivel: la acción FluentASM ya es una instrucción. El "IR" es la instrucción con su plantilla de encoding elegida.

### 4.2 Layout

| Paso | Acción |
|---|---|
| 1 | Asignar a cada sección su `align` y su offset inicial |
| 2 | Asignar offsets a rutinas, datos y labels dentro de la sección |
| 3 | Estimar cada instrucción en su forma **larga** |
| 4 | Iterar acortando saltos hasta punto fijo |
| 5 | Recalcular símbolos |
| 6 | Resolver relocaciones |

### 4.3 Tabla de símbolos

| Campo | Uso |
|---|---|
| `Name` | Nombre canónico (`main`, `n`, `@loop`) |
| `Kind` | `Routine`, `Data`, `Resource`, `Label`, `ParameterAlias` |
| `Section` | `.execute`, `.rodata`, `.data`, `.bss` |
| `Offset` | Tras layout |
| `Size` | Capacidad declarada |
| `Owner` | Rutina dueña (para labels) |
| `Visibility` | `public` / `private` |

Los labels `@` son hijos de la rutina y **no** entran en la tabla global. El binder los guarda en una tabla por rutina.

---

## 5. Emisión

### 5.1 Imagen plana

`ImageBuilder` recibe:

```
sections ordenadas → byte[] con relleno → relocaciones → símbolos → entry
```

Escribe un `.x64` sin cabecera salvo `.image(...)`. El archivo es reproducible: mismo AST ⇒ mismo `byte[]`.

### 5.2 Mapa

`--map` produce un texto estable (doc 16 §11.4) con secciones, símbolos, offsets y tamaños. Es parte de la salida, no un diagnóstico.

### 5.3 Verificación interna

Antes de escribir:

- Ninguna relocación sin resolver (`FASM9003`).
- Ningún símbolo sin sección.
- `entry` dentro de `.execute`.
- Tamaño de cada sección == suma de sus elementos + relleno.
- Hash del archivo estable entre dos ejecuciones.

---

## 6. Diagnósticos

### 6.1 Motor

```csharp
record Diagnostic(string Code, Severity Severity, Span Span, string Message,
                  string? Help, ImmutableArray<Diagnostic> Notes);
```

- El catálogo de códigos y mensajes vive en `Diagnostics/Catalog.cs`, generado desde `13-DIAGNOSTICOS.md`.
- Cada diagnóstico tiene **código, severidad, span, mensaje y corrección sugerida**.
- Los mensajes son estables: cambiarlos es un cambio de versión menor como mínimo.

### 6.2 Salida

| Formato | Cuándo |
|---|---|
| Texto con `archivo:línea:columna: severidad CODE: mensaje` | por defecto |
| JSON (`--diagnostics=json`) | IDE y CI |
| SARIF | integración con herramientas |

### 6.3 Supersesión de rangos

`CONTRIBUTING.md` describe rangos antiguos (`2xxx` sintáctico, `3xxx` semántico/ABI). **La referencia vigente es [13-DIAGNOSTICOS](13-DIAGNOSTICOS.md)**; la tabla de migración está en [19-MIGRACION](19-MIGRACION.md). El compilador nuevo no implementa los rangos antiguos.

---

## 7. CLI

```
fluentasm build   <archivo.fasm> [--target x64-sse2|x64-avx2|x64-avx512|x64-avx512full]
                                 [--abi win64|sysv]
                                 [--no-payload]
                                 [--map]
                                 [--out salida.x64]
fluentasm check   <archivo.fasm>          // solo diagnósticos, no escribe
fluentasm disasm  <archivo.x64>           // desensambla a acciones FluentASM
fluentasm map     <archivo.fasm>          // imprime el mapa sin emitir
fluentasm migrate <archivo.fasm>          // sintaxis antigua → nueva (doc 19)
fluentasm version
```

Códigos de salida:

| Código | Significado |
|---:|---|
| 0 | Éxito sin diagnósticos |
| 1 | Éxito con warnings/infos |
| 2 | Errores de compilación |
| 3 | Error de uso (argumentos) |
| 4 | Error interno (`9xxx`) |

`--no-payload` convierte el bootstrap (D-1) en error (`FASM2026`). `--target` sobrescribe la cabecera solo si esta no la declara. `--abi` **no** sobrescribe; si el fuente no declara `.abi`, es error (`FASM2004`).

---

## 8. Milestones

Cada milestone es **ejecutable y verificable**. No se pasa al siguiente sin que la suite completa pase.

### M0 — Esqueleto (1 semana)

| Entregable | Aceptación |
|---|---|
| Solución .NET con `Core` y `Compiler` | `dotnet build` limpio |
| `Diagnostic` + catálogo vacío | `fluentasm version` responde |
| Corpus de `.fasm` de prueba | 20 archivos válidos e inválidos |

### M1 — Lexer y parser (2–3 semanas)

| Entregable | Aceptación |
|---|---|
| Lexer completo (doc 14 §2) | 100 % de tokens del corpus; `FASM1001`–`1006` |
| Parser EBNF completo | AST de los 20 archivos; `FASM11xx` |
| Recuperación de errores | AST parcial en todos los casos |
| *Golden* de AST | serialización estable |

**Criterio de salida:** `fluentasm check` sobre [15-EJEMPLOS](15-EJEMPLOS.md) no produce errores sintácticos.

### M2 — Binder y checker ATM (3 semanas)

| Entregable | Aceptación |
|---|---|
| Tabla de símbolos y ámbitos | `FASM2003`, `FASM2021` |
| Modelo de capacidades `Bit`/`Byte` | `FASM3020`–`3028` |
| Modelo de registros y vistas | `FASM3001`–`3025` |
| Política de literales | `FASM2007` |
| Checker de programa/secciones | `FASM2001`–`2030` |

**Criterio de salida:** los seis ejemplos válidos pasan; sus gemelos inválidos dan el código esperado (doc 15 §9).

### M3 — Catálogo de verbos y selector (3 semanas)

| Entregable | Aceptación |
|---|---|
| Tabla declarativa de verbos (GPR + flujo) | 100 % de doc 07 §3–§7 |
| Resolución de firmas | `FASM4001`, `FASM4002` |
| Flags escritos/leídos por verbo | `FASM4012` |
| Restricciones de operandos | `FASM4003`–`4036` |

**Criterio de salida:** cada verbo del catálogo tiene prueba de firma válida y de al menos un rechazo.

### M4 — Encoder legacy (3 semanas)

| Entregable | Aceptación |
|---|---|
| REX, ModRM, SIB, disp | doc 16 §3–§6 |
| Inmediatos estructurales | doc 16 §9 |
| Relajación de saltos | doc 16 §10.3 |
| Imagen plana + mapa | doc 16 §11 |

**Criterio de salida:** E1, E2, E4, E5 y E6 producen **exactamente** los bytes de doc 15.

### M5 — ABI y prólogo/epílogo (2 semanas)

| Entregable | Aceptación |
|---|---|
| Win64: parámetros, shadow, alineación | doc 12 §2–§5 |
| System V: parámetros, red zone | doc 12 §3 |
| Generación de prólogo/epílogo y cuña | doc 09 §6.3–§6.4 |
| Verificación de llamadas | `FASM5001`–`5013` |

**Criterio de salida:** E2 y E5 pasan; un `flowCall` desalineado da `FASM5003`.

### M6 — SIMD y x87 (3 semanas)

| Entregable | Aceptación |
|---|---|
| `.r128/.r256/.r512`, `part{8/16/32/64}` | doc 10 |
| VEX y EVEX, máscaras `Krf` | doc 16 §7–§8 |
| Pila x87 y profundidad | doc 11 |
| Selección VEX/EVEX | doc 16 §7.3 |

**Criterio de salida:** E3 y E6 pasan con bytes exactos.

### M7 — Robustez (2 semanas)

| Entregable | Aceptación |
|---|---|
| Fuzzing del parser y del encoder | 0 excepciones no controladas; 0 `9xxx` en 10⁶ entradas |
| Límites declarados | `FASM1007`–`1009` |
| Determinismo | doble compilación ⇒ `sha256` igual |
| Rendimiento | 1 MiB de fuente en < 1 s en hardware de 2024 |

### M8 — Herramientas (2 semanas)

| Entregable | Aceptación |
|---|---|
| `disasm` | reconstruye los 6 ejemplos |
| `migrate` | tabla de doc 19 aplicada |
| `--diagnostics=json` / SARIF | consumible por IDE y CI |
| LSP mínimo | completado y hover (doc 18) |

---

## 9. Estrategia de pruebas

### 9.1 Pirámide

| Nivel | Cantidad | Qué prueba |
|---|---:|---|
| Unitarias | ~800 | lexer, parser, capacidades, firmas, ModRM, relajación |
| *Golden* | 60 | programas completos → bytes exactos |
| Negativas | 200 | cada diagnóstico, al menos una vez |
| Propiedades | 8 | invariantes (§9.3) |
| Fuzz | continuo | parser y encoder |

### 9.2 Golden files

```
tests/golden/E1.fasm
tests/golden/E1.expected     // offset: bytes ; comentario
tests/golden/E1.map
```

La prueba no compara texto desensamblado: compara **hex**. Un cambio de byte es un fallo hasta que se revise y se acepte explícitamente con `--update-golden` (que exige revisión en el PR).

### 9.3 Propiedades

1. **Determinismo.** `Compile(x) == Compile(x)` byte a byte.
2. **Idempotencia del parser.** `Parse(x)` no lanza para ninguna entrada de hasta 64 KiB.
3. **Conservación de tamaño.** `sum(section.Size) == image.Length`.
4. **Relajación monótona.** Acortar saltos nunca aumenta el tamaño total.
5. **Sin relocs pendientes.** Tras el linker, `Relocations.All(r => r.Resolved)`.
6. **Cobertura de diagnósticos.** Todo código de doc 13 tiene al menos una prueba.
7. **Cobertura de verbos.** Todo verbo del catálogo tiene encoding probado.
8. **Round-trip de `disasm`.** `Encode(Disasm(bytes)) == bytes` para los golden.

### 9.4 Pruebas de aceptación manuales

Cada milestone incluye una demo ejecutable: un pequeño cargador que reserva memoria, copia la imagen y salta a ella (en Windows, `VirtualAlloc` + `CreateThread`; en Linux, `mmap` + `call`). No forma parte del producto; vive en `tools/runner`.

---

## 10. Rendimiento

| Métrica | Objetivo |
|---|---|
| 1 MiB de fuente | < 1 s (una pasada, sin optimizador) |
| Memoria | < 10× el tamaño del fuente |
| Arranque del CLI | < 150 ms |
| Recompilación incremental | fuera de v1 |

El cuello de botella esperado es el parser; el encoder es lineal. Si el parser supera el objetivo, se cambia a un lexer *pull* con *lookahead* de 2, sin cambiar la gramática.

---

## 11. Riesgos

| Riesgo | Mitigación |
|---|---|
| La tabla de verbos crece sin control | Cada verbo exige una fila, una prueba y una entrada en doc 07 |
| Ambigüedad `integerSource` | El checker aplica la firma; el parser nunca decide |
| Relajación de saltos no converge | Algoritmo solo-acorta + cota de iteraciones |
| EVEX mal codificado | Bytes de oro en doc 15/16 + prueba de campo por campo |
| Diferencia Windows/Linux en ABI | Doc 12 normativo + pruebas por ABI |
| El IDE y el CLI divergen | Ambos consumen `Core`; el LSP es una capa fina |

---

## 12. Resumen

- Pipeline de 8 etapas puras, cada una con invariante verificable.
- `Core` sin dependencias externas, catálogo de verbos como datos.
- CLI con `build/check/disasm/map/migrate` y códigos de salida estables.
- Nueve milestones, cada uno con criterio de aceptación ejecutable.
- Pruebas: unitarias, *golden* por bytes, negativas por diagnóstico, propiedades y fuzz.
- El compilador nunca emite una instrucción con un error abierto ni una imagen con una relocación pendiente.
