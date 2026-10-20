# FluentASM

Un ensamblador con **sintaxis fluida**, escrito en C#.

Escribir ensamblador debería ser tan cómodo y legible como escribir C#. FluentASM traduce instrucciones
encadenadas con aspecto de método de C# a **código máquina nativo** y genera imágenes binarias que no
necesitan ningún runtime.

```fasm
program MiPrograma
    section .text
    entry main

    routine main
        .Mov(Rax, 42)
        .Ret()
    end
end
```

## ¿Qué es FluentASM?

- Un lenguaje ensamblador con sintaxis inspirada en C# (métodos encadenados, `PascalCase`, autocompletado).
- Genera **código máquina nativo**, no bytecode ni intermedio. No hay máquina virtual ni runtime.
- Soporta múltiples arquitecturas (empezando por x64) y puede ejecutarse sobre sistema operativo o directamente sobre hardware (bare metal).
- Los archivos de proyecto (`.fproj`) y de solución (`.fworkspace`) son estrictamente binarios para lectura rápida por herramientas.

## Estado del proyecto

**Fase 0 — Cierre de diseño.** No hay código todavía. Las especificaciones se están cerrando para que cualquier persona pueda implementar la Fase 1 sin hacer preguntas.

## Cómo empezar

```powershell
dotnet build
dotnet test
```

Más detalles sobre arquitectura, contribución, ramas, PRs y versionado en [CONTRIBUTING.md](CONTRIBUTING.md).
La referencia completa de sintaxis, registros y catálogo de instrucciones está en [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md).

## Estructura del repositorio

```
src/     → Código fuente principal (FluentASM.Core, FluentASM.Compiler, FluentASM.Admin, pruebas)
rsc/     → Recursos del proyecto (ejemplos .fasm, pruebas de concepto, bibliotecas .bin)
.github/ → Plantillas de issues y pull requests
```

## Licencia

MIT. Ver [LICENSE](LICENSE).
