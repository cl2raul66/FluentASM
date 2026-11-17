# FluentASM

Un lenguaje ensamblador moderno, práctico, seguro y eficiente con **sintaxis fluida (*type-state*) y bloques**.

FluentASM traduce instrucciones escritas con claridad y estructura guiada directamente a **código máquina nativo**, generando binarios ejecutables sin máquina virtual ni *runtime*.

```fasm
// Proyecto: src/miCalculadoraCli/miCalculadoraCli.fproj
// Código:   src/miCalculadoraCli/sourcesCode.fasm

miCalculadoraCli
    .data(dt -> {
        dt.define<Byte:8>(d -> d.id(numA).value(10))
        dt.define<Byte:8>(d -> d.id(numB).value(20))
    })
    .execute(ex -> {
        ex.routines(rt -> {

            rt.define(d -> {
                d.id(main)
                d.returns(Gpr0)
                d.run(() -> {
                    Gpr1.copy([numA])
                    Gpr2.copy([numB])
                    Ipr.flowCall(c -> c.target(sumar).inputs(Gpr1, Gpr2).output(Gpr0))
                    Ipr.flowStackJump()
                })
            })

            rt.define(d -> {
                d.id(sumar)
                d.returns(Gpr0)
                d.run((arg1<Byte:8>, arg2<Byte:8>) -> {
                    Gpr0.copy(arg1)
                    Gpr0.add(arg2)
                    Ipr.flowStackJump()
                })
            })

        })
    })
    .initialize()
```

## ¿Qué es FluentASM?

- **Fluido y guiado (*type-state*) + bloques:** La estructura del programa fluye por secciones (`Proyecto` → `.data(...)` → `.execute(...)` → `.initialize()`) y dentro de las rutinas usa bloques `{}` legibles, con una sola acción por línea y un argumento por llamada fluida.
- **Un único modelo físico de tamaños:** Sin tipos abstractos (`int`, `float`, `string`, `struct`, etc). Todo se expresa en **`Bit:N`** y **`Byte:N`** (y sus múltiplos binarios `Kilo`, `Mega`, `Giga`, `Tera`, etc).
- **Sin datos duros en el flujo de ejecución:** Las instrucciones describen rutas y transformaciones. Para inicializar en estado limpio se usa **`defaultValue`**; los valores concretos viven en `.data(...)` o en recursos externos.
- **Seguridad Tipo 1 y eficiencia:** Sin cajas negras ni código oculto en ejecución. El compilador valida anchos, vistas, banderas y contratos de llamada antes de emitir bytes.
- **Enfoque *bare metal* por fases:** Diseñado libre de las restricciones de sistemas operativos existentes. La **Fase 1** apunta a **x64 *bare metal*** (imagen plana ejecutable en arrancador o emulador QEMU), dejando la base lista para incorporar futuras arquitecturas (ARM, RISC-V) y extraer un estándar multi-CPU desde implementaciones reales.
- **Proyectos simples al inicio, ecosistema binario a futuro:** En Fase 1 los archivos `.fproj` y `.fworkspace` usan texto simple legible; la transición a formatos binarios propios, herramientas dedicadas y entorno integrado se aborda en fases posteriores.

## Estado del proyecto

**Fase 1 en marcha — M0 completado.**
La solución .NET (`FluentASM.Core`, `FluentASM.Compiler`) ya lee y valida el `.fproj` en texto simple y el CLI `fluentasm` resuelve nombre, arquitectura, punto de entrada y fuentes. Siguen M1 (léxico y parser), M2 (validador Tipo 1), M3 (codificador x64 e imagen plana) y M4 (ejecución real en QEMU).

## Documentación

- [CONTRIBUTING.md](CONTRIBUTING.md) — Principios, flujo de trabajo, decisiones de diseño, roadmap y estándares del proyecto.
- [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md) — Referencia de sintaxis común, modelo `Bit`/`Byte`, estructura fluida y contratos.
- [FLUENT_SYNTAX_X64.md](FLUENT_SYNTAX_X64.md) — Registros, direccionamiento y catálogo mínimo viable x64 *bare metal* (Fase 1).

*(Las carpetas `Proposal00/` y `Proposal01/` se conservan únicamente como archivo histórico de diseño).*

## Estructura del repositorio

- `src/` → Código fuente del compilador y herramientas (`FluentASM.Core`, `FluentASM.Compiler`, `FluentASM.Sample`, etc.) y la solución `src/FluentASM.slnx`.
- `rsc/` → Recursos de prueba y ejemplos (se añaden conforme se necesiten).
- `.github/` → Plantillas de issues, Pull Requests y flujos de CI/CD.

## Licencia

MIT. Ver [LICENSE](LICENSE).
