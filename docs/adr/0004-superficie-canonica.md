# ADR 0004 — La superficie canónica es el DSL `.fasm`

- **Estado:** Aceptada
- **Fecha:** 2026-10-18
- **Decide:** mantenedor del proyecto

## Contexto

La documentación de sintaxis propone **dos** formas de escribir lo mismo:

**(a) DSL de texto** en archivos `.fasm`:

```fasm
program AddTwoNumbers
    parameter a, b

    .Mov(Rax, a)
    .Add(Rax, b)
    .Ret()
end
```

**(b) API fluent de C#**, que devuelve el binario construido en memoria:

```csharp
var codigo = asm
    .Mov(Rax, 10)
    .Add(Rax, 20)
    .Build();
```

Mantener dos lenguajes en paralelo significa: dos gramáticas, dos juegos de pruebas, dos
documentaciones, dos catálogos de instrucciones y, sobre todo, dos oportunidades de divergir. Además
la Fase 8 (auto-albergue) exige que el ensamblador se escriba **en FluentASM**, es decir en el DSL
de texto: si el DSL no es la superficie principal, el auto-albergue se vuelve imposible.

## Decisión

1. La **superficie canónica** es el DSL de texto `.fasm`. Su gramática formal vive en
   [FLUENT_SYNTAX.md](../../FLUENT_SYNTAX.md) y es la referencia para todo lo demás.
2. La **API fluent de C#** es una **superficie secundaria** que construye el **mismo IR** y produce
   los **mismos bytes**. Se implementa **antes** (Fase 1) porque no necesita parser y permite
   verificar el encoder de inmediato, pero no define el lenguaje.
3. Sinónimos permitidos entre superficies:

   | DSL `.fasm` | API C# | Significado |
   |---|---|---|
   | `.Mov(Rax, 42)` | `.Mov(Rax, 42)` | El mismo mnemónico, el mismo orden de operandos |
   | `equ` / `align` / `section` | `Const()` / `Align()` / `Section()` | Directivas como métodos |

4. **Regla de oro:** ninguna característica se considera terminada si existe en una superficie y no
   en la otra, **o** si produce bytes distintos. Las pruebas comparan ambas salidas.

## Consecuencias

### Positivas

- Una sola gramática que mantener, documentar y probar.
- El auto-albergue (Fase 8) es alcanzable: el lenguaje ya es el DSL de texto.
- La API de C# se convierte en la herramienta de desarrollo y de prueba del encoder, no en un
  segundo lenguaje.
- Autocompletado y detección de errores en tiempo de compilación quedan como una ventaja de la API
  de C# para quien la use, sin obligar al lenguaje a imitarla.

### Negativas y mitigaciones

| Riesgo | Mitigación |
|---|---|
| El DSL de texto exige escribir un analizador léxico y sintáctico desde cero | Se pospone a la Fase 2, después de que la Fase 1 valide el IR y el encoder |
| Los ejemplos de la documentación mezclan las dos formas | `FLUENT_SYNTAX.md` marca cada ejemplo con su superficie y usa el DSL como forma por defecto |
| Doble implementación del catálogo de instrucciones | El catálogo se define **una vez** como datos (mnemónico, formas, encoding) y ambas superficies lo consumen |
| Tentación de añadir azúcar al DSL que la API no puede expresar | La regla de oro lo impide; las discrepancias se detectan en las pruebas comparativas |

## Alternativas consideradas

1. **API de C# como superficie canónica y el DSL como azúcar.** Reduce el trabajo inicial, pero
   hace imposible el auto-albergue tal y como está definido en la Fase 8 y contradice el formato
   de archivo `.fasm` que da nombre al proyecto.
2. **Adoptar además la sintaxis Intel clásica (`mov rax, 42`) como entrada válida.** Muy útil para
   migrar código existente como `WinUIAsmCtrl`, pero multiplica los casos del parser y las
   ambigüedades con las directivas. Se anota como posible **modo de entrada** futuro, nunca como
   sintaxis canónica.
