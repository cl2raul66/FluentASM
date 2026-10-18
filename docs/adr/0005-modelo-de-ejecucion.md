# ADR 0005 — Modelo de ejecución y ABI de la Fase 1

- **Estado:** Aceptada
- **Fecha:** 2026-10-18
- **Decide:** mantenedor del proyecto

## Contexto

La documentación de sintaxis mostraba ejemplos como:

```fasm
program AddTwoNumbers
    parameter a, b
    .Mov(Eax, a)
    .Add(Eax, b)
    .Ret()
end
```

Es decir: parámetros sin decir **dónde** llegan (`a` y `b` son mágicos), registros de 32 bits en
ejemplos declarados para 64 bits, y `.Ret()` sin decir quién coloca el valor de retorno ni quién
restaura la pila. Sin esas reglas, cada implementación inventaría las suyas y el lenguaje no sería
determinista.

## Decisión

### ABI objetivo de la Fase 1: **Microsoft x64 (Win64)**

- Los cuatro primeros parámetros llegan en `Rcx`, `Rdx`, `R8`, `R9`.
- El valor de retorno entero viaja en `Rax` (y `Edx` para el segundo valor en las funciones que lo
  devuelven, reservado para más adelante).
- El llamante reserva **espacio de sombra de 32 bytes** antes de cada llamada.
- La pila debe estar alineada a **16 bytes** en el momento de la llamada.
- Registros que la función debe preservar: `Rbx`, `Rbp`, `Rdi`, `Rsi`, `R12`–`R15`, `Rsp`.
- Registros volátiles para el llamante: `Rax`, `Rcx`, `Rdx`, `R8`–`R11`.

`System V AMD64` (Linux) se documenta en [ABI.md](../ABI.md) y se implementa cuando exista la salida
ELF. La ABI activa se declara en el `.fproj` y en la línea de comandos, nunca se adivina.

### `parameter` se traduce por ABI, con prólogo y epílogo generados

```fasm
routine AddTwoNumbers
    parameter a, b        ; Win64 -> a = Rcx, b = Rdx

    .Mov(Rax, Rcx)        ; o .Mov(Rax, a); el nombre es alias del registro
    .Add(Rax, Rdx)
    .Ret()                ; el valor de retorno es RAX
end
```

- `parameter` **no elige registros**: los asigna la ABI, en orden. El nombre es un alias legible.
- El compilador **genera** el prólogo (guardar `Rbp`, establecer el marco, reservar el espacio de
  variables locales alineado a 16) y el epílogo (restaurar) alrededor del cuerpo de la rutina.
- `.Ret()` es el final de la rutina: ejecuta el epílogo y el `ret`. No se escribe `ret` a mano en
  una rutina con `parameter`.
- Límite de la Fase 1: **4 parámetros** por rutina. Un quinto produce un error de diagnóstico
  explícito, no una asignación silenciosa a la pila.

### Modelo de ejecución

- El ejecutable de Fase 1 produce una **imagen plana** cargada **en memoria** desde la herramienta
  (reserva ejecutable, copia, salto al punto de entrada), tal como fija el
  [ADR 0003](0003-salidas-imagen-plana.md).
- El código generado es **independiente de posición** y **no puede llamar a nada externo**.
- Lo observable es el **valor de retorno en `Rax`** y, opcionalmente, la memoria compartida con el
  cargador para los proyectos que la necesiten.
- Los ejemplos de la Fase 1 son, por tanto, puros: instrucciones de registro, inmediatos y
  aritmética. El "hola mundo" con salida por pantalla llega en la fase de los ejecutables del SO.

### Qué NO es de la Fase 1

| Fuera de alcance | Fase en la que llega |
|---|---|
| Llamadas al CRT o a la API del sistema (`printf`, `ExitProcess`) | 5 |
| Emisión de PE/ELF | 5 |
| Más de 4 parámetros, registro de marcos complejos, excepciones | 5 |
| Parámetros por la pila y ABI `cdecl`/`stdcall` (32 bits) | 6 |
| Bare metal, modo real, llamadas al BIOS | 7 |

## Consecuencias

### Positivas

- Los ejemplos de la documentación se vuelven implementables: no queda ningún símbolo mágico.
- Elegir ABI explícita permite invocar el resultado desde el mundo exterior (un puntero a función de
  C# con la firma `long f(long, long)`) y comprobarlo con una prueba real.
- El prólogo y epílogo generados evitan los errores de alineación más habituales al llamar a la API
  del sistema en las fases siguientes.

### Negativas y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Quien venga de Linux espera otra ABI | El `.fproj` y la CLI declaran la ABI; `docs/ABI.md` explica ambas y el roadmap dice cuándo llega cada una |
| El prólogo generado "esconde" lo que hace el procesador y contradice el espíritu educativo | `fasm_admin dump --asm` (fase 3) y los ejemplos de la documentación muestran el prólogo y el epílogo generados, con la opción `naked` para rutinas sin prólogo |
| El código de Fase 1 no puede imprimir nada | Es una limitación **declarada**, con su fase de resolución en el roadmap; los ejemplos de Fase 1 devuelven valores y se comprueban en pruebas |

## Alternativas consideradas

1. **Elegir los registros a mano en `parameter` (`parameter a in Rcx`).** Máximo control, pero
   obliga a conocer la ABI para escribir cualquier ejemplo y contradice el principio de accesibilidad.
2. **System V como ABI primaria.** Más sencilla (sin espacio de sombra), pero el entorno de
   desarrollo y el corpus de pruebas del proyecto son Windows x64, y las llamadas a la API de
   Windows (objetivo de la Fase 5) exigen Win64.
3. **No generar prólogo ni epílogo y obligar a escribir todo a mano.** Coherente con un ensamblador
   clásico, incompatible con el principio "claridad sobre brevedad".
