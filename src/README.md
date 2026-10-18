# `src/` — Código fuente

Todavía **no hay código**: el proyecto está cerrando el diseño (Fase 0). Esta carpeta existe con la
estructura ya decidida para que nadie tenga que adivinar dónde va cada cosa.

```
src/
├── FluentASM.Core/            → Modelo intermedio, catálogo de instrucciones, codificador,
│                                layout, emisores y diagnósticos
├── FluentASM.Compiler/        → El ensamblador (fluentasm): lee .fasm y .fproj, genera la salida
├── FluentASM.Admin/           → El administrador de producto (fasm_admin)
├── FluentASM.Core.Tests/      → Bytes dorados, oráculo NASM, ejecución en memoria
├── FluentASM.Compiler.Tests/  → Pruebas de extremo a extremo
└── FluentASM.IDE/             → Entorno de desarrollo integrado (Fase 9)
```

Qué va en cada sitio:

| Proyecto | Contiene | No contiene |
|---|---|---|
| `FluentASM.Core` | IR, operandos, registros, codificación, layout, emisión, diagnósticos | Lectura de archivos, argumentos de línea de comandos |
| `FluentASM.Compiler` | Analizadores, CLI, lectura de `.fproj` | Codificación de instrucciones (eso es Core) |
| `FluentASM.Admin` | Comandos de `fasm_admin`, formatos `.fproj`/`.fworkspace` | Codificación |
| `FluentASM.IDE` | Interfaz gráfica | Lógica de ensamblado (consume Compiler) |

Regla de dependencias: `Admin` y `Compiler` dependen de `Core`; `Core` no depende de nadie. Los
proyectos de pruebas dependen de lo que prueban, nunca al revés.

Los primeros archivos que aparecerán aquí están enumerados en la [Fase 1 del roadmap](../roadmap.md).
