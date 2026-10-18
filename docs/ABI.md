# ABI y modelo de ejecución

Este documento responde a una pregunta que la sintaxis dejaba abierta: **cuando una rutina declara
`parameter a, b`, ¿dónde están realmente `a` y `b`?** La respuesta la da la ABI (*Application Binary
Interface*) de la plataforma, no la imaginación del usuario.

Decisiones asociadas: [ADR 0005](adr/0005-modelo-de-ejecucion.md) y [ADR 0003](adr/0003-salidas-imagen-plana.md).

---

## 1. ABI por plataforma

| | **Win64** (Microsoft x64) | **System V AMD64** (Linux/macOS) |
|---|---|---|
| ABI por defecto en | Windows | Linux, macOS |
| Parámetro 1 | `Rcx` | `Rdi` |
| Parámetro 2 | `Rdx` | `Rsi` |
| Parámetro 3 | `R8` | `Rdx` |
| Parámetro 4 | `R9` | `Rcx` |
| Parámetros 5+ | Pila (derecha a izquierda) | `R8`, `R9`, luego pila |
| Valor de retorno entero | `Rax` | `Rax` |
| Valor de retorno (2.º) | Reservado | `Rdx` |
| Espacio de sombra | **32 bytes, reservado por el llamante** | No existe |
| Alineación de pila en la llamada | **16 bytes** | 16 bytes |
| Zona roja (*red zone*) | No existe | 128 bytes bajo `Rsp` |
| Preservar (callee-saved) | `Rbx`, `Rbp`, `Rdi`, `Rsi`, `R12`–`R15`, `Rsp` | `Rbx`, `Rbp`, `R12`–`R15`, `Rsp` |
| Volátiles (caller-saved) | `Rax`, `Rcx`, `Rdx`, `R8`–`R11` | `Rax`, `Rcx`, `Rdx`, `Rsi`, `Rdi`, `R8`–`R11` |

**La ABI activa se declara siempre** (en el `.fproj` o en la línea de comandos). Nunca se adivina por
el sistema operativo en el que corre el ensamblador.

Errores que la generación automática debe hacer imposibles: olvidar el espacio de sombra, fallar la
alineación a 16, o asumir que `Rcx` sobrevive a una llamada en Win64 (no sobrevive).

---

## 2. Rutinas: parámetros, prólogo y epílogo generados

```fasm
routine AddTwoNumbers
    parameter a, b          ; Win64: a -> Rcx, b -> Rdx

    .Mov(Rax, a)            ; 'a' es un alias legible de Rcx
    .Add(Rax, b)
    .Ret()                  ; el valor de retorno es RAX
end
```

El ensamblador genera, para ese cuerpo, esta secuencia completa:

| Parte | Instrucciones | Bytes (x64) |
|---|---|---|
| Prólogo | `push rbp` / `mov rbp, rsp` / `sub rsp, 32` | `55 48 89 e5 48 83 ec 20` |
| Cuerpo | `mov rax, rcx` / `add rax, rdx` | `48 89 c8 48 01 d0` |
| Epílogo + retorno | `leave` / `ret` | `c9 c3` |

Reglas:

1. `parameter` **asigna por orden de declaración** según la ABI activa. El usuario no elige registros.
2. El nombre del parámetro es un **alias** del registro: sirve para leer mejor el código, no para
   cambiar la ABI.
3. El prólogo reserva, además de los 32 bytes de sombra si la rutina va a llamar a otra, el espacio
   de las variables locales, siempre en múltiplos de 16.
4. El epílogo se emite **una sola vez** y `.Ret()` no duplica el `ret`.
5. El prólogo y el epílogo se generan **solo cuando son necesarios**: si la rutina declara
   `parameter`, declara variables locales, o realiza llamadas. Una rutina **hoja** sin ninguno de
   esos tres casos se emite sin marco, byte a byte: por eso `main` del ejemplo mínimo produce
   exactamente `b8 2a 00 00 00 c3` y nada más. La regla es determinista: nunca depende de una
   decisión del ensamblador.
6. `naked` fuerza la ausencia de prólogo y epílogo aunque la rutina tenga parámetros o locales: quien
   lo escribe asume la responsabilidad del marco. Se usa en código de arranque y manejadores.
7. Fase 1: máximo **4 parámetros**. El quinto produce `FASM3001` indicando en qué fase llega el paso
   por pila.

### Ejemplo verificado

```fasm
program Ejemplo
    section .text
    entry main

    routine AddTwoNumbers
        parameter a, b
        .Mov(Rax, a)
        .Add(Rax, b)
        .Ret()
    end

    routine main
        .Mov(Rcx, 10)
        .Mov(Rdx, 32)
        .Call(AddTwoNumbers)   ; devuelve 42 en RAX
        .Ret()
    end
end
```

Este es el ejemplo que cierra la Fase 1: los bytes se comparan con NASM y el resultado (42) se
verifica ejecutando la imagen en memoria.

---

## 3. Llamadas dentro de la imagen

| Aspecto | Regla en Fase 1 |
|---|---|
| Forma | `Call(simbolo)` → `e8` + desplazamiento relativo de 32 bits |
| Resolución | Relativa, dentro de la misma imagen. No hay tabla de importaciones |
| Espacio de sombra | Reservado por el llamante cuando llama a una rutina con `parameter` |
| Alineación | El llamante garantiza 16 bytes |
| Registros volátiles | Se **destruyen**; si el llamante los necesita, los guarda él |
| Recursión | Permitida: no hay estado global compartido por el compilador |
| Llamadas indirectas | `Call(rax)` → `ff d0`, con el mismo contrato |

Una llamada a un símbolo declarado `extern` **no** es posible en una imagen plana y produce `FASM5001`
con un mensaje que dice explícitamente en qué fase llega.

---

## 4. Punto de entrada

| Aspecto | Regla |
|---|---|
| Declaración | Directiva `entry <simbolo>` dentro del programa |
| Valor por defecto | El primer símbolo de `.text` |
| Qué recibe | En imagen plana: **nada**. No hay argumentos ni entorno |
| Qué debe devolver | `Rax` con un entero; se convierte en el resultado del proceso |
| Quién limpia la pila | Nadie: en el arranque no hay pila que limpiar (`Rsp` lo fija el cargador) |
| Debe terminar con | `.Ret()`, que en el punto de entrada se convierte en `ret` |

Cuando lleguen los ejecutables del SO (Fase 5) el punto de entrada cambiará de significado: en
Windows habrá `ExitProcess` y en Linux una llamada al sistema `exit`.

---

## 5. Reglas de la pila

```
Direcciones altas
        ┌──────────────────┐
        │  argumentos 5+   │
        ├──────────────────┤
        │  dirección de    │  ← empujada por 'call'
        │  retorno         │
        ├──────────────────┤
        │  espacio de      │  ← 32 bytes en Win64, reservados por el llamante
        │  sombra          │
        ├──────────────────┤
        │  variables       │  ← reservadas por el prólogo, múltiplo de 16
        │  locales         │
        └──────────────────┘  ← Rsp
Direcciones bajas
```

1. La pila crece **hacia abajo**.
2. En el momento de ejecutar `call`, `Rsp` debe ser múltiplo de 16. Consecuencia práctica: tras
   empujar la dirección de retorno (`Rsp ≡ 8 mod 16`) la rutina debe ajustar 8 o 40 bytes, no 16 ni
   32, para volver a alinearse. El espacio de sombra cumple precisamente ese papel.
3. El prólogo guarda `Rbp` y establece el marco antes de reservar locales.
4. En System V existe una zona roja de 128 bytes bajo `Rsp` que una función hoja puede usar sin
   reservar; en Win64 **no existe** y usarla corrompe la pila. Los ejemplos de la documentación no la
   usan nunca.

---

## 6. Naked, interrupciones y código de arranque

| Caso | Cómo se escribe | Por qué |
|---|---|---|
| Rutina sin prólogo | `routine Foo naked` | Código de arranque, manejadores, rutinas que ya reciben la pila preparada |
| Instrucción de software | `.Int(0x80)` → `cd 80` | La instrucción existe y se codifica; su **efecto** depende del sistema y no es portable |
| Espera ocupada | `.Nop()` → `90` | Útil en pruebas y relleno de alineación |
| Sin llamadas al sistema | Regla de Fase 1 | No hay imports ni CRT hasta la Fase 5 |

`Int` se documenta con honestidad: codifica `int imm8`, pero en un bare metal la tabla de vectores la
debe preparar el propio programa, y en un sistema operativo moderno la mayoría de interrupciones no
están disponibles para el usuario. La Fase 7 (bare metal) es donde tendrá uso real.

---

## 7. ¿Y el "sin runtime" de la visión?

Se mantiene intacto y conviene precisar qué significa:

- La imagen generada **no** necesita .NET, Java ni ninguna máquina virtual. Es código máquina.
- La imagen de Fase 1 se ejecuta desde el arrancador de FluentASM o sobre el hardware, porque no
  tiene cabecera de sistema operativo.
- En la Fase 5, el ejecutable PE/ELF seguirá sin necesitar runtime .NET: los imports serán como
  mucho funciones del sistema (`kernel32`, `ntdll`, `libc`), nunca el runtime de C#. El compilador
  escrito en C# sí necesita .NET, pero eso es la herramienta, no el resultado.
