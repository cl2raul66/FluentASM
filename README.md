# FluentASM

Un ensamblador con **sintaxis fluida**.

Escribir ensamblador debería ser tan cómodo y legible como escribir **código fluido**. FluentASM traduce instrucciones
encadenadas con aspecto de **código fluido** a **código máquina nativo** y genera imágenes binarias que no
necesitan ningún runtime.

```fasm
program MiPrograma
    section .execute
    entry main

    routine main
        .copy(42 to Rax)        // el valor de retorno viaja en RAX
        .flowStackJump()
    end
end
```

## ¿Qué es FluentASM?

- Un lenguaje ensamblador con sintaxis inspirada en **código fluido**.
- Genera **código máquina nativo**, no bytecode ni intermedio. No hay máquina virtual ni runtime.
- Soporta múltiples arquitecturas (empezando por x64) y puede ejecutarse sobre sistema operativo o directamente sobre hardware (bare metal).
- Los archivos de proyecto (`.fproj`) y de solución (`.fworkspace`) son estrictamente binarios para lectura rápida por herramientas.
- La documentación de símbolos (`///`) vive en `fasm.fdoc` y nunca afecta a los bytes ejecutables.

## Estado del proyecto

**Fase 0 — Cierre de diseño.** No hay código todavía. Las especificaciones se están cerrando para que cualquier persona pueda implementar la Fase 1 sin hacer preguntas.

## Cómo empezar

Más detalles sobre arquitectura, contribución, ramas, PRs y versionado en [CONTRIBUTING.md](CONTRIBUTING.md).
La referencia completa de sintaxis común está en [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md) y la específica de x64 en [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md).

## Estructura del repositorio

- `src/` → Código fuente principal para cada proyecto, por ejemplo, FluentASM.Core, FluentASM.Compiler, FluentASM.Admin, etc
- `rsc/` → Recursos del proyecto (se añaden solo cuando se necesiten)
- `.github/` → Plantillas de issues y pull requests

## Licencia

MIT. Ver [LICENSE](LICENSE).
