# 12 — ABI

> **Normativo.** La convención de llamada es un contrato físico: define qué registro lleva qué argumento, quién preserva qué, cuánta pila se reserva y cómo se retorna. FluentASM **nunca la adivina**: se declara con `.abi(...)` y el compilador la verifica.

---

## 1. Declaración

```fasm
program demo.abi(win64) { ... }        // todo el programa
routine f.abi(sysv) { ... }            // una rutina concreta
```

| Valor | Convención |
|---|---|
| `win64` | Microsoft x64 |
| `sysv` | System V AMD64 (Linux, BSD, macOS) |

Reglas:

- `.abi` en la rutina sobreescribe la del programa.
- Una rutina sin `.abi` hereda la del programa (el programa **debe** declarar una).
- Llamar a una rutina con ABI distinta a la del llamador → `FASM5010` (error): el lenguaje no genera *thunks*.
- La ABI de una rutina es parte de su tipo: `flowCall` la comprueba.

---

## 2. Win64 (Microsoft x64)

### 2.1 Argumentos enteros

| Orden | Registro |
|---|---|
| 1.º | `Gpr1` (RCX) |
| 2.º | `Gpr2` (RDX) |
| 3.º | `Gpr8` (R8) |
| 4.º | `Gpr9` (R9) |
| 5.º+ | pila |

### 2.2 Argumentos vectoriales

| Orden | Registro |
|---|---|
| 1.º | `Simd0` |
| 2.º | `Simd1` |
| 3.º | `Simd2` |
| 4.º | `Simd3` |
| 5.º+ | pila |

Los espacios de registro son **independientes**: un `float` no consume un hueco entero. El 5.º argumento entero va a la pila aunque el 2.º sea vectorial.

### 2.3 Shadow space

El llamador reserva **32 bytes** contiguos en la pila, justo encima de la dirección de retorno, para que el llamado pueda *spillar* los cuatro registros de argumento.

```fasm
routine llamador.abi(win64).frame(Byte:64).shadow(1) {
    ...
    Ipr.flowCall(objetivo)
    ...
}
```

Sin `.shadow(1)` en una rutina que llama → `FASM5002`.

### 2.4 Pila de argumentos

```
[RSP + 0]   shadow space (32 bytes)
[RSP + 32]  argumento 5
[RSP + 40]  argumento 6
...
```

### 2.5 Retorno

| Tipo | Registro |
|---|---|
| Entero / puntero | `Gpr0` |
| Vector | `Simd0` |
| 128 bits (struct) | `Gpr0` + `Gpr2` |
| Mayor | puntero oculto en `Gpr1` |

En v1, `.returns(...)` solo admite `Gpr`, `Simd` o `none`.

### 2.6 Registros

| Registro | Rol |
|---|---|
| `Gpr0`, `Gpr1`, `Gpr2`, `Gpr8`, `Gpr9`, `Gpr10`, `Gpr11` | volátiles |
| `Gpr3`, `Gpr5`, `Gpr6`, `Gpr7`, `Gpr12`–`Gpr15` | callee-saved |
| `Gpr4` | pila |
| `Simd0`–`Simd5` | volátiles |
| `Simd6`–`Simd15` | callee-saved |
| `Frf` | volátil (salvo `DF`, `MXCSR`) |
| `Xrf` | vacía al entrar y al salir |

> `Gpr5` (RBP) es callee-saved en Win64 aunque el compilador lo use como frame pointer.

---

## 3. System V AMD64

### 3.1 Argumentos enteros

| Orden | Registro |
|---|---|
| 1.º | `Gpr7` (RDI) |
| 2.º | `Gpr6` (RSI) |
| 3.º | `Gpr2` (RDX) |
| 4.º | `Gpr1` (RCX) |
| 5.º | `Gpr8` (R8) |
| 6.º | `Gpr9` (R9) |
| 7.º+ | pila |

### 3.2 Argumentos vectoriales

`Simd0`–`Simd7`; el 9.º+ va a la pila.

### 3.3 Retorno

| Tipo | Registro |
|---|---|
| Entero | `Gpr0` |
| Par entero | `Gpr0` : `Gpr2` |
| Vector | `Simd0` : `Simd1` |
| Mayor | puntero oculto en `Gpr7` |

### 3.4 Red zone

128 bytes por debajo de `RSP` que la rutina puede usar sin reservar:

```fasm
routine hoja.abi(sysv).redzone(1) {
    Byte:8 [Gpr4 - 8].copy(Gpr0)     // dentro de la red zone
    ...
}
```

- Solo en SysV → `FASM5009` si se declara en Win64.
- Si la rutina llama a otra, la red zone se invalida.

### 3.5 Registros

| Registro | Rol |
|---|---|
| `Gpr0`, `Gpr1`, `Gpr2`, `Gpr6`, `Gpr7`, `Gpr8`–`Gpr11` | volátiles |
| `Gpr3`, `Gpr5`, `Gpr12`–`Gpr15` | callee-saved |
| `Gpr4` | pila |
| `Simd0`–`Simd15` | volátiles |
| `Simd16`–`Simd31` | volátiles (con AVX-512) |
| `Frf` | `DF` debe ser 0 al entrar y salir |

---

## 4. Asignación de parámetros

El compilador aplica este algoritmo, en este orden:

1. Recorre los `parameter` en orden de declaración.
2. Clasifica cada uno: `Gpr`, `Simd`, `Stack`.
3. Asigna el siguiente registro libre de su clase, según la ABI.
4. Si la clase está agotada, lo asigna a la pila, en orden.
5. Verifica que `.frame` cubra los slots de pila que declare el cuerpo.

| Clase | Win64 | SysV |
|---|---|---|
| `Gpr` | `Gpr1`, `Gpr2`, `Gpr8`, `Gpr9` | `Gpr7`, `Gpr6`, `Gpr2`, `Gpr1`, `Gpr8`, `Gpr9` |
| `Simd` | `Simd0`–`Simd3` | `Simd0`–`Simd7` |
| `Stack` | `[RBP + 16 + 8k]` | `[RBP + 16 + 8k]` |

Un `parameter` de clase `Stack` no se asigna: el programador declara explícitamente que va en pila y accede a él por `Byte:N [nombre]`.

---

## 5. Prólogo y epílogo

### 5.1 Generación

Solo se genera lo declarado (D-021):

| Atributo | Genera |
|---|---|
| `.frame(Byte:N)` | `PUSH Gpr5`; `MOV Gpr5, Gpr4`; aporta `N` al `SUB` de reserva |
| `.preserves(GprA, ...)` | `PUSH` por registro; `POP` inverso |
| `.shadow(1)` | aporta 32 al `SUB` de reserva; `ADD` inverso |
| `.naked(1)` | nada |
| sin atributos | nada |

La reserva es **una sola** instrucción `SUB Gpr4, total` (doc 09 §6.3–§6.4): `total = N + cuña + 32·shadow`. Emitir varias restas sería más largo y violaría D-025.

### 5.2 Ejemplo generado

```fasm
routine f.abi(win64).frame(Byte:32).preserves(Gpr3, Gpr12).shadow(1) { ... }
```

Prólogo (instrucciones x64 emitidas, no fuente FluentASM):

```asm
PUSH Gpr5                    // 55
MOV  Gpr5, Gpr4              // REX.W 89 E5
PUSH Gpr3                    // 53
PUSH Gpr12                   // 41 54
SUB  Gpr4, 64                // 32 (frame) + 32 (shadow)
```

Epílogo:

```asm
ADD  Gpr4, 64
POP  Gpr12
POP  Gpr3
POP  Gpr5
RET
```

### 5.3 Alineación

Antes de cualquier `flowCall`, `RSP` debe ser múltiplo de 16. El compilador calcula el delta acumulado y:

- Si la rutina tiene prólogo declarado, comprueba que `.frame` (múltiplo de 16) y la cuña de alineación dejan `RSP ≡ 0 (mod 16)`; si no, emite la cuña de 8 bytes cuando `|preserves|` es impar (doc 09 §6.4).
- Si no lo tiene, verifica el delta estático y acepta `Stack.reserve`/`release` explícitos.
- Si no puede demostrarlo → `FASM5003`.

---

## 6. Verificación de llamadas

Para cada `flowCall`, el compilador comprueba:

| Comprobación | Diagnóstico |
|---|---|
| La rutina destino existe | `FASM2003` |
| La ABI coincide | `FASM5010` |
| La pila está alineada | `FASM5003` |
| El shadow space está reservado (Win64) | `FASM5002` |
| Los argumentos están en los registros correctos | `FASM5006` |
| El frame del llamador está intacto | `FASM5011` |

### 6.1 Comprobación de argumentos

El checker compara la firma declarada del destino con el estado de los registros en el punto de llamada:

```fasm
routine objetivo.abi(win64) {
    parameter a: Gpr
    parameter b: Gpr
}

routine llamador.abi(win64).frame(Byte:32).shadow(1) {
    Gpr1.copy(Byte:8 [x])      // arg 0
    Gpr2.copy(Byte:8 [y])      // arg 1
    Ipr.flowCall(objetivo)      // ✓
}
```

Si un registro de argumento no se ha escrito y no está `parameter`-aliasado → `FASM5006` (aviso).

### 6.2 Retorno

Si el destino declara `.returns(Gpr)`, el llamador debe leer `Gpr0` antes de que otro verbo lo sobrescriba. El checker lo marca con `FASM5007`.

---

## 7. TLS

| ABI | Registro de TLS | MSR |
|---|---|---|
| Win64 | `GS` (`Srf4`) | `Msr.gsBase` |
| SysV | `FS` (`Srf3`) | `Msr.fsBase` |

```fasm
// Win64: cargar el puntero de TLS
Gpr0.copy(Srf4.base)
Gpr0.copy(Byte:8 [Gpr0])

// SysV
Gpr0.copy(Srf3.base)
Gpr0.copy(Byte:8 [Gpr0])
```

El chequeo cruzado (`Srf3` en Win64) → `FASM5012` (aviso).

---

## 8. Funciones variádicas

```fasm
routine printfLike.abi(sysv).variadic(1) { ... }
```

| ABI | Requisito |
|---|---|
| SysV | `AL` = número de registros vectoriales usados |
| Win64 | El llamador duplica los argumentos vectoriales en la pila |

El compilador **no** genera el prólogo variádico automáticamente: el programador guarda `Simd0`–`Simd7` si los necesita (`FASM5013` lo recuerda).

En v1 no hay soporte de `va_list`; queda para fase 2.

---

## 9. Syscalls

Los servicios del sistema **no siguen la ABI de función**:

| Sistema | Número | Args | Resultado |
|---|---|---|---|
| Linux x64 | `Gpr0` | `Gpr7`, `Gpr6`, `Gpr2`, `Gpr10`, `Gpr8`, `Gpr9` | `Gpr0` |
| Windows x64 | `Gpr0` | `Gpr1`, `Gpr2`, `Gpr8`, `Gpr9` | `Gpr0` |

Se invocan con `Ipr.flowSyscall()`, no con `flowCall`. El número de servicio es un dato: entra por memoria o parámetro (doc 06 §7.1).

---

## 10. Interacción entre ABIs

Una rutina `sysv` puede llamar a una `win64` solo si el programador construye el puente a mano y lo declara:

```fasm
routine puente.abi(sysv).naked(1) {
    // mover argumentos de SysV a Win64, reservar shadow, alinear...
}
```

El compilador no lo hace por ti (`FASM5010`). Un *thunk* automático sería una optimización invisible y violaría D-012.

---

## 11. Tabla comparativa

| Aspecto | Win64 | SysV |
|---|---|---|
| Args enteros | 4 | 6 |
| Args vectoriales | 4 | 8 |
| Shadow space | 32 B obligatorios | no |
| Red zone | no | 128 B |
| Retorno entero | `Gpr0` | `Gpr0` |
| Retorno vector | `Simd0` | `Simd0` |
| Callee-saved GPR | 3, 5, 6, 7, 12–15 | 3, 5, 12–15 |
| Callee-saved SIMD | 6–15 | ninguno |
| Alineación en call | 16 | 16 |
| Dirección de pila | hacia abajo | hacia abajo |
| `DF` al entrar/salir | 0 | 0 |

---

## 12. Ejemplo completo

```fasm
program abiDemo.abi(win64).target(x64-avx2).entry(main) {

    section .rodata {
        data a: Byte:8
        data b: Byte:8
        data r: Byte:8
    }

    section .execute {

        routine suma.abi(win64).returns(Gpr) {
            parameter x: Gpr
            parameter y: Gpr

            Gpr0.copy(x)
            Gpr0.add(y)
            Ipr.flowStackJump()
        }

        routine main.abi(win64).frame(Byte:32).shadow(1).returns(Gpr) {
            Gpr1.copy(Byte:8 [a])
            Gpr2.copy(Byte:8 [b])
            Ipr.flowCall(suma)
            Byte:8 [r].copy(Gpr0)
            Ipr.flowStackJump()
        }
    }
}
```

---

## 13. Diagnósticos de ABI

| Código | Situación |
|---|---|
| `FASM5001` | Llamada a símbolo externo (no soportada en v1) |
| `FASM5002` | Shadow space no reservado |
| `FASM5003` | Pila desalineada antes de `flowCall` |
| `FASM5004` | Frame no restaurado al retornar |
| `FASM5005` | `Stack.align` impide el análisis de pila |
| `FASM5006` | Argumento no colocado en el registro de la ABI |
| `FASM5007` | `.returns` y el registro de retorno no se escribe |
| `FASM5008` | Dirección absoluta en imagen PIC |
| `FASM5009` | `.redzone(1)` en una ABI que no la tiene |
| `FASM5010` | Llamada entre ABIs distintas sin puente |
| `FASM5011` | Frame del llamador modificado |
| `FASM5012` | Registro de TLS de otra ABI (aviso) |
| `FASM5013` | Rutina variádica sin guardar los registros vectoriales |

---

## 14. Resumen

- La ABI se declara y se verifica; nunca se adivina.
- Win64: 4 enteros, 4 vectoriales, 32 B de shadow, `Gpr0` de retorno.
- SysV: 6 enteros, 8 vectoriales, 128 B de red zone.
- El prólogo se genera solo a partir de `.frame`, `.preserves`, `.shadow`.
- La pila debe estar a 16 antes de cada llamada.
- Las syscalls no son llamadas de función.
- Cruzar ABIs requiere un puente explícito.
