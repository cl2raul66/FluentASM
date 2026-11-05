# Referencia de sintaxis de FluentASM — x64 (Nivel 3: Nativo)

> **Documento específico de arquitectura x64.** Para la sintaxis común compartida, véase [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md).

---

## 5.2 Registros x64 (Nivel 3: Nativo)

Cuando necesitas control total o instrucciones específicas de x64, usas los registros nativos. Se nombran con `PascalCase` y el mismo nombre en los cuatro tamaños.

| 64 bits | 32 bits | 16 bits | 8 bits | N.º | Rol en la ABI Win64 |
|---|---|---|---|---|---|
| `Rax` | `Eax` | `Ax` | `Al` | 0 | Valor de retorno; volátil |
| `Rcx` | `Ecx` | `Cx` | `Cl` | 1 | Parámetro 1; volátil |
| `Rdx` | `Edx` | `Dx` | `Dl` | 2 | Parámetro 2; volátil |
| `Rbx` | `Ebx` | `Bx` | `Bl` | 3 | Se debe preservar |
| `Rsp` | `Esp` | `Sp` | `Spl` | 4 | Puntero de pila; no usar como índice |
| `Rbp` | `Ebp` | `Bp` | `Bpl` | 5 | Marco de pila; se debe preservar |
| `Rsi` | `Esi` | `Si` | `Sil` | 6 | Volátil |
| `Rdi` | `Edi` | `Di` | `Dil` | 7 | Volátil |
| `R8` | `R8d` | `R8w` | `R8b` | 8 | Parámetro 3; volátil |
| `R9` | `R9d` | `R9w` | `R9b` | 9 | Parámetro 4; volátil |
| `R10` | `R10d` | `R10w` | `R10b` | 10 | Volátil |
| `R11` | `R11d` | `R11w` | `R11b` | 11 | Volátil |
| `R12` | `R12d` | `R12w` | `R12b` | 12 | Se debe preservar |
| `R13` | `R13d` | `R13w` | `R13b` | 13 | Se debe preservar |
| `R14` | `R14d` | `R14w` | `R14b` | 14 | Se debe preservar |
| `R15` | `R15d` | `R15w` | `R15b` | 15 | Se debe preservar |

**Bytes altos (solo con los registros originales):** `Ah`, `Ch`, `Dh`, `Bh`. **Prohibido** combinarlos con `R8`–`R15` o con `Spl`/`Bpl`/`Sil`/`Dil`: comparten codificación y el prefijo REX los invalida. FluentASM lo detecta y emite `FASM3002`.

**Registro de banderas:** `rFlags` (64 bits). Cada bit es una bandera independiente (0 o 1). Ver propuesta 0002.

> **Nota:** los ejemplos de 64 bits usaban `Eax`, que es el registro de **32 bits**. En x64, un valor de 64 bits se maneja con `Rax`. `Eax` sigue siendo válido, pero significa exactamente "los 32 bits bajos" y **pone a cero la mitad alta**.

---

## ABI x64 (Win64)

| Concepto | Valor |
|---|---|
| Parámetro 1 | `Rcx` |
| Parámetro 2 | `Rdx` |
| Parámetro 3 | `R8` |
| Parámetro 4 | `R9` |
| Valor de retorno entero | `Rax` |
| Espacio de sombra | 32 bytes, reservado por el llamante |
| Alineación de pila en la llamada | 16 bytes |
| Callee-saved | `Rbx`, `Rbp`, `Rdi`, `Rsi`, `R12`–`R15`, `Rsp` |
| Caller-saved (volátiles) | `Rax`, `Rcx`, `Rdx`, `R8`–`R11` |

La ABI activa se declara siempre (en `.fproj` o línea de comandos). Nunca se adivina.

---

## Modos de direccionamiento x64

| Modo | Ejemplo | Significado |
|---|---|---|
| Inmediato | `42` | El valor va dentro de la instrucción |
| Registro | `Rax` | El valor está en el registro |
| Memoria indirecta | `[Rbx]` | El valor está en la dirección que guarda `Rbx` |
| Memoria con desplazamiento | `[Rbx - 8]` | Igual, con desplazamiento |
| Memoria con índice y escala | `[Rbx + Rcx*4 + 16]` | Arreglo: base + índice × tamaño + desplazamiento |
| Relativo a RIP | `[Rip + mensaje]` o `[Rel mensaje]` | Posición independiente (PIC) |
| Dirección de un símbolo | `mensaje` | Dirección del símbolo (se codifica como `Lea`) |
| Contenido de un símbolo | `[mensaje]` | El dato guardado en el símbolo |
| Tamaño explícito | `Byte [Rbx]`, `QWord [Rbx]` | Obligatorio cuando el tamaño es ambiguo |

**Reglas x64:**

1. La **escala** solo puede ser 1, 2, 4 u 8. Otro valor es `FASM4002`.
2. `Rsp` y `R12` no pueden ser índice (`FASM4003`).
3. Si el tamaño del dato no se deduce del otro operando, hay que declararlo. Escribir `.copy(1 to [Rbx])` es `FASM3005`; se escribe `.copy(1 to Byte [Rbx])`.
5. `.copy(Rax to mensaje)` carga la **dirección** (equivalente a `.lea(Rax to mensaje)`), mientras que `.copy(Rax to [mensaje])` carga el **contenido**.

---

## ABI de llamada x64 (Win64)

```fasm
routine AddTwoNumbers
    parameter a, b          // Win64: a -> Rcx, b -> Rdx

    .copy(a to Rax)
    .alu(Rax add b)
    .ret()                  // el valor de retorno es Rax
end
```

| Concepto | Regla |
|---|---|
| Parámetros | Se asignan **en orden** según la ABI; el nombre es un alias |
| Máximo en Fase 1 | 4 (en Win64 llegan en `Rcx`, `Rdx`, `R8`, `R9`). El quinto es `FASM3001` |
| Prólogo | Generado cuando la rutina tiene parámetros, locales o llamadas: `push rbp` / `mov rbp, rsp` / `sub rsp, N` (N múltiplo de 16) |
| Epílogo | Generado y emitido una sola vez: `leave` / `ret` |
| Valor de retorno | `Rax` |
| `.ret()` | Termina la rutina: ejecuta el epílogo y el `ret` |
| Rutina hoja | Sin parámetros, locales ni llamadas: **sin** prólogo ni epílogo |
| `naked` | Sin prólogo ni epílogo aunque tenga parámetros o locales; el autor se responsabiliza del marco |
| Llamada | `.call(símbolo)` → `e8` + desplazamiento relativo; `.call(rax)` → `ff d0` |

---

## Catálogo de instrucciones x64 (Fase 1)

La columna **Bytes** está **verificada con NASM** para el ejemplo indicado. `rel` significa que el desplazamiento depende del layout.

### Movimiento de datos

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `copy` (r32, imm) | `.copy(10 to Eax)` | 1 | `b8 0a 00 00 00` | Forma más corta para un inmediato sin signo |
| `copy` (r64, imm pequeño) | `.copy(42 to Rax)` | 1 | `b8 2a 00 00 00` | Forma más corta: `mov r32, imm32` (pone a cero mitad alta) |
| `copy` (r64, imm negativo) | `.copy(-1 to Rax)` | 1 | `48 c7 c0 ff ff ff ff` | Signo extendido a 64 bits |
| `copy` (r64, imm grande) | `.copy(0x123456789 to Rax)` | 1 | `48 b8 89 67 45 23 01 00 00 00` | `imm64` solo cuando no cabe en 32 bits |
| `copy` (r64, r64) | `.copy(Rbx to Rax)` | 1 | `48 89 d8` | |
| `copy` (r8 alto, r8) | `.copy(R9 to R8)` | 3 | `4d 89 c8` | Prefijo REX necesario |
| `copy` (r64, [mem]) | `.copy([Rbx] to Rax)` | 1 | `48 8b 03` | |
| `copy` ([mem], r64) | `.copy(Rax to [Rbx])` | 1 | `48 89 03` | |
| `copy` (r64, [mem-disp]) | `.copy([Rbx - 8] to Rax)` | 1 | `48 8b 43 f8` | `disp8` con signo |
| `copy` (r64, [base+idx*esc]) | `.copy([Rbx + Rcx*4 + 16] to Rax)` | 1 | `48 8b 44 8b 10` | SIB con escala |
| `copy` (byte [mem], imm) | `.copy(1 to Byte [Rbx])` | 1 | `c6 03 01` | Tamaño explícito |

#### Extensión con ceros / signo

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `copyZeroExtended` | `.copyZeroExtended(R8w to Eax)` | 1 | `41 0f b7 c0` | Extiende sin signo (8/16/32 → 32/64) |
| `copySignedExtended` | `.copySignedExtended(Al to Eax)` | 1 | `0f be c0` | Extiende con signo (8/16/32 → 32/64) |

#### Cálculo de direcciones

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `lea` | `.lea(Rcx [Rel mensaje])` | 1 | `48 8d 0d disp32` | Base de PIC |
| `lea` (de símbolo) | `.lea(Rcx mensaje)` | 1 | `48 8d 0d disp32` | Azúcar: `.copy(Rcx to mensaje)` |

---

### Aritmética y lógica (`.alu`)

Todas las instrucciones ALU usan la sintaxis `.alu(origen operacion destino)` separado por espacios. Actualizan `rFlags` según la operación.

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `add` | `.alu(5 add Rax)` | 1 | `48 83 c0 05` | imm8 con signo |
| `add` | `.alu(1000 add Rax)` | 1 | `48 05 e8 03 00 00` | Forma con acumulador |
| `add` | `.alu(20 add Eax)` | 1 | `83 c0 14` | |
| `add` | `.alu(Rbx add Rax)` | 1 | `48 01 d8` | |
| `sub` | `.alu(0x68 sub Rsp)` | 1 | `48 83 ec 68` | Ajuste de pila |
| `sub` | `.alu(5 sub Eax)` | 1 | `83 e8 05` | |
| `and` | `.alu(0xFF and Eax)` | 1 | `25 ff 00 00 00` | CF=0, OF=0 |
| `or` | `.alu(Eax or Eax)` | 1 | `09 c0` | CF=0, OF=0 |
| `xor` | `.alu(Eax xor Eax)` | 1 | `31 c0` | Poner a cero idiomático |
| `xor` | `.alu(R8d xor R8d)` | 3 | `45 31 c0` | |
| `imul` | `.alu(Ebx imul Eax)` | 1 | `0f af c3` | Dos operandos. CF/OF según resultado; ZF/SF/PF/AF indefinidas |

#### Solo banderas (no escriben destino)

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `subNoWrite` | `.alu(101 subNoWrite Eax)` | 1 | `83 f8 65` | Resta descartada. Solo banderas. Igual que `cmp`. |
| `andNoWrite` | `.alu(Eax andNoWrite Eax)` | 1 | `85 c0` | AND descartado. Solo banderas. Igual que `test`. |

---

### Pila

| Instrucción | Ejemplo | Nivel | Bytes | Comentario |
|---|---|---|---|---|
| `stackWrite` | `.stackWrite(Rbp)` | 1 | `55` | `RSP -= 8; [RSP] = Rbp` |
| `stackWrite` | `.stackWrite(R8)` | 3 | `41 50` | |
| `stackWrite` | `.stackWrite(Rax)` | 1 | `50` | |
| `stackRead` | `.stackRead(Rbp)` | 1 | `5d` | `Rbp = [RSP]; RSP += 8` |

> **Nota física:** `RSP` es la dirección de la cima; `[RSP]` es el dato. `stackWrite` escribe en `[RSP]` tras decrementar `RSP`.

---

### Control de flujo

#### Salto incondicional

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `jump` | `.jump(@fin)` | 1 | `eb rel8` | Corto; promoción automática a `e9 rel32` |
| `jump` | `.jump(Rax)` | 1 | `ff e0` | Indirecto |

#### Saltos condicionales — Gramática cerrada (16 formas)

Sintaxis: `.jump(destino if rFlags.condicion)`

| DSL | NASM | Lee en `rFlags` |
|---|---|---|
| `.jump(@fin if rFlags.zero)` | `je` / `jz` | `ZF = 1` (bit 6) |
| `.jump(@fin if not rFlags.zero)` | `jne` / `jnz` | `ZF = 0` |
| `.jump(@fin if rFlags.carry)` | `jc` / `jb` | `CF = 1` (bit 0) |
| `.jump(@fin if not rFlags.carry)` | `jnc` / `jae` | `CF = 0` |
| `.jump(@fin if rFlags.sign)` | `js` | `SF = 1` (bit 7) |
| `.jump(@fin if not rFlags.sign)` | `jns` | `SF = 0` |
| `.jump(@fin if rFlags.overflow)` | `jo` | `OF = 1` (bit 11) |
| `.jump(@fin if not rFlags.overflow)` | `jno` | `OF = 0` |
| `.jump(@fin if rFlags.parity)` | `jp` / `jpe` | `PF = 1` (bit 2) |
| `.jump(@fin if not rFlags.parity)` | `jnp` / `jpo` | `PF = 0` |
| `.jump(@fin if rFlags.carry or rFlags.zero)` | `jbe` | `CF=1` o `ZF=1` |
| `.jump(@fin if not rFlags.carry and not rFlags.zero)` | `ja` | `CF=0` y `ZF=0` |
| `.jump(@fin if rFlags.sign unequal rFlags.overflow)` | `jl` | `SF ≠ OF` |
| `.jump(@fin if rFlags.sign equal rFlags.overflow)` | `jge` | `SF = OF` |
| `.jump(@fin if rFlags.zero or rFlags.sign unequal rFlags.overflow)` | `jle` | `ZF=1` o `SF≠OF` |
| `.jump(@fin if not rFlags.zero and rFlags.sign equal rFlags.overflow)` | `jg` | `ZF=0` y `SF=OF` |

#### Llamada y retorno

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `call` | `.call(Rutina)` | 1 | `e8 rel32` | Empuja `RIP` en pila y salta |
| `call` | `.call(Rax)` | 1 | `ff d0` | Indirecto |
| `ret` | `.ret()` | 1 | `c3` | Saca `RIP` de pila. No recibe operando; retorno en `Rax` por ABI |

---

### Sistema y relleno (bare metal Fase 1)

| Instrucción | Ejemplo | Nivel | Bytes | Notas |
|---|---|---|---|---|
| `nop` | `.nop()` | 1 | `90` | No hace nada |
| `setCarry` | `.setCarry()` | 1 | `f9` | `CF = 1` |
| `clearCarry` | `.clearCarry()` | 1 | `f8` | `CF = 0` |
| `toggleCarry` | `.toggleCarry()` | 1 | `f5` | `CF = !CF` |
| `setDirection` | `.setDirection()` | 1 | `fd` | `DF = 1` (cadenas hacia atrás) |
| `clearDirection` | `.clearDirection()` | 1 | `fc` | `DF = 0` (cadenas hacia adelante) |

**Deferidos:** `sti`/`.enableInterrupts()`, `cli`/`.disableInterrupts()`, `int3`/`.breakpoint()`, `syscall`/`.syscall()`.

---

## Reglas de codificación x64

1. **Forma más corta y determinista.** Para los mismos operandos, siempre la misma secuencia de bytes, la más corta posible.
2. **Inmediatos con signo.** Si el inmediato cabe en 8 bits con signo y existe esa forma, se usa: `.alu(5 add Rax)` son 4 bytes, `.alu(1000 add Rax)` son 6.
3. **`copy` con inmediato en 64 bits**: si el valor cabe en 32 bits sin signo → `mov r32, imm32` (`b8`); si cabe con signo en 32 bits → `mov r/m64, imm32` (`48 c7 /0`); si no → `mov r64, imm64` (`48 b8`).
4. **Desplazamientos**: `disp8` cuando cabe con signo, `disp32` en caso contrario.
5. **Direccionamiento relativo a RIP** por defecto para todo símbolo (PIC). Nunca absoluto en Fase 1.
6. **Prefijo REX** cuando el tamaño es 64 bits o el registro es `R8`–`R15`, más `Spl`/`Bpl`/`Sil`/`Dil`.
7. **Saltos**: forma corta (`rel8`) y promoción automática a cercana (`rel32`) cuando el destino no cabe.
8. **Tamaño de la instrucción** se conoce antes de emitir y queda registrado.

---

## Semántica de ejecución x64 (Fase 1)

| Aspecto | Regla |
|---|---|
| Formato | Bytes puros, **sin cabecera** |
| Extensión | `.x64` |
| Punto de entrada | El símbolo de `Entry`, o el primero de `.execute` |
| Argumentos | Ninguno |
| Resultado | Entero en `Rax` |
| Cargador | Arrancador de FluentASM: reserva memoria ejecutable, copia bytes, salta |
| Independencia de posición | **Obligatoria**: misma imagen funciona en cualquier dirección |
| Llamadas externas | **Prohibidas** (`FASM5001`) |

---

## Ejemplos verificados x64 (Fase 1)

### Nativo (Nivel 3)

```fasm
program AddTwoNumbers
    section .execute
    entry main

    routine AddTwoNumbers
        parameter a, b
        .copy(a to Rax)
        .alu(Rax add b)
        .ret()
    end

    routine main
        .copy(10 to Rcx)
        .copy(32 to Rdx)
        .call(AddTwoNumbers)
        .ret()
    end
end
```

### Portable (Nivel 1)

```fasm
program AddTwoNumbersPortable
    section .execute
    entry main

    routine AddTwoNumbers
        parameter a, b
        .copy(a to R0q)
        .alu(R0q add b)
        .ret()
    end

    routine main
        .copy(10 to R0q)
        .copy(32 to R1q)
        .call(AddTwoNumbers)
        .ret()
    end
end
```

---

## Diagnósticos x64

| Código | Situación | Mensaje |
|---|---|---|
| `FASM3002` | `Ah` con `R8` | "`Ah` no puede combinarse con registros que exigen prefijo REX" |
| `FASM4002` | Escala no válida | "La escala debe ser 1, 2, 4 u 8, no 3" |
| `FASM4003` | Índice prohibido | "`Rsp` no puede usarse como índice" |
| `FASM4010` | Condición salto inválida | "Condición `rFlags.foo` no válida. Use una de las 16 formas." |
| `FASM4011` | `not` mal usado | "`not` solo precede bandera simple (`rFlags.zero`, etc.)." |
| `FASM4012` | Bandera indefinida | "Bandera `rFlags.zero` indefinida tras `.alu(Ebx imul Eax)`." |
| `FASM4013` | Operador no soportado | "Operador `xor` no válido. Use `equal`, `unequal`, `or`, `and`." |

---

## Próximos pasos x64

1. **Coma flotante y SIMD (`Xmm`/`Ymm`/`Zmm`)** — AVX/AVX2/AVX-512.
2. **`Div`, `Idiv`, multiplicación de un operando** — `mul`, `div`, `idiv`.
3. **Desplazamientos y rotaciones** — `shl`, `shr`, `sar`, `rol`, `ror`, `rcl`, `rcr`.
4. **`Movsx` / `Cmpxchg` / cadenas** — `movsx`, `cmpxchg`, `movsb`, `stosb`, `scasb`.
5. **Cadenas y `Cmpxchg`** — operaciones atómicas y de cadena.
6. **AVX-512 / AMX** — registros `Zmm`, máscaras `K`, `tile`.
7. **Capa portable unificada** — sintaxis `.jump(@fin if a less b)`.
8. **Diagnóstico `rFlags`** — rastreo de banderas definidas/indefinidas.