# FluentASM

Un ensamblador con **sintaxis fluida**, escrito en C#.

Escribir ensamblador debería ser tan cómodo y legible como escribir C#. FluentASM traduce instrucciones
encadenadas con aspecto de método de C# a **código máquina nativo x64** y genera imágenes binarias que no
necesitan ningún runtime.

```fasm
program MiPrograma
    section .text
    entry main

    routine main
        .Mov(Rax, 42)     ; el valor de retorno viaja en RAX
        .Ret()
    end
end
```

## Estado del proyecto

**Fase 0 — Cierre de diseño.** No hay código todavía: se están cerrando las especificaciones para que
cualquier persona pueda implementar la Fase 1 sin preguntar nada. Ver [roadmap.md](roadmap.md).

## Documentación

| Documento | Contenido |
|---|---|
| [VISION.md](VISION.md) | Qué es FluentASM, principios y qué **no** es |
| [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md) | Gramática, registros, catálogo de instrucciones y semántica |
| [roadmap.md](roadmap.md) | Fases, criterios de salida y orden de trabajo |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Cómo contribuir, ramas, PRs, versionado y estilo |
| [docs/IR.md](docs/IR.md) | Modelo intermedio: operandos, secciones, símbolos y relocalizaciones |
| [docs/ABI.md](docs/ABI.md) | Convenciones de llamada y modelo de ejecución |
| [docs/FORMAT_FPROJ.md](docs/FORMAT_FPROJ.md) | Especificación byte a byte del archivo de proyecto `.fproj` |
| [docs/FORMAT_FWORKSPACE.md](docs/FORMAT_FWORKSPACE.md) | Especificación byte a byte del archivo de solución `.fworkspace` |
| [docs/adr/](docs/adr/) | Registro de decisiones de diseño (ADR) |

## Estructura

```
src/     → Código fuente (FluentASM.Core, FluentASM.Compiler, FluentASM.Admin, pruebas)
rsc/     → Recursos: ejemplos .fasm, pruebas de concepto y bibliotecas .bin
docs/    → Especificaciones y decisiones de diseño
.github/ → Plantillas de issues y pull requests
```

## Compilar y probar

Todavía no hay nada que compilar. A partir de la Fase 1:

```powershell
dotnet build
dotnet test
```

## Licencia

MIT. Ver [LICENSE](LICENSE).
