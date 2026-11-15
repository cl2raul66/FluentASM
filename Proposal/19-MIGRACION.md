# 19 — Migración

> **Informativo.** Cómo pasar del FluentASM anterior (`FLUENT_SYNTAX.md`, `FLUENT_SYNTAX_X64.md`, propuestas 0001–0023) a la sintaxis de esta especificación. Incluye la tabla completa de equivalencias, los cambios que **no** son mecánicos y el diseño de la herramienta `fasm migrate`.

---

## 1. Política de compatibilidad

- **No hay compatibilidad hacia atrás.** El compilador v1 no acepta la sintaxis anterior. Un `end` produce `FASM1001`; un `Rax` produce `FASM3012` con la sugerencia del nombre canónico.
- **La migración es explícita.** `fasm migrate` reescribe lo que puede demostrar y **marca** lo que no, sin inventar datos.
- **La semántica no cambia en silencio.** Toda transformación que altere el conjunto de instrucciones se reporta.
- Los documentos anteriores quedan como histórico; la referencia vigente es este directorio.

---

## 2. Bloques y estructura

| Antes | Ahora | Nota |
|---|---|---|
| `program X` … `end` | `program X { … }` | llaves (D-4) |
| `section .execute` … `end` | `section .execute { … }` | |
| `Routine X` … `End` | `routine X { … }` | minúsculas, llaves |
| `Routine X Naked` | `routine X.naked(1) { … }` | atributo encadenado |
| `Routine X` sin `End` | `routine X { … }` | el `end` deja de existir |
| Indentación significativa | Indentación cosmética | el bloque lo definen las llaves |
| `parameter a, b` | `parameter a: Gpr` + `parameter b: Gpr` | una declaración por línea, con clase |
| `parameter n` | `parameter n: Gpr` | la clase es obligatoria |
| `local x` | `local x: Byte:8` | la capacidad es obligatoria |
| `Program` opcional | `program` obligatorio | `FASM2001` |
| `Section` implícita | `.execute` implícita solo si hay un único bloque | el resto exige sección |
| `program` con `parameter` | `parameter` solo dentro de `routine` | `FASM2012` |
| `///` (doc en `fasm.fdoc`) | comentario `//` + `fasm.fdoc` | el doc no vive en el fuente |

### 2.1 Cabecera de programa

| Antes | Ahora |
|---|---|
| `program SumaDos` | `program sumaDos.abi(win64).target(x64-avx2).entry(main) { … }` |
| ABI en `.fproj` o CLI | `.abi(...)` en la cabecera (el CLI puede sobrescribir si no se declara) |
| Sin target | `.target(x64-avx2)` por defecto si se omite |

---

## 3. Registros

| Antes | Ahora | Diagnóstico si se deja |
|---|---|---|
| `Rax` | `Gpr0` | `FASM3012` |
| `Eax` | `Gpr0.low32` | `FASM3012` |
| `Ax` | `Gpr0.low16` | `FASM3012` |
| `Al` | `Gpr0.low8` | `FASM3012` |
| `Ah` | `Gpr0.high8` | `FASM3012` |
| `Rcx` … `R15` | `Gpr1` … `Gpr15` | `FASM3012` |
| `Rsp` | `Gpr4` | `FASM3012` |
| `Rbp` | `Gpr5` | `FASM3012` |
| `Rsi` / `Rdi` | `Gpr6` / `Gpr7` | `FASM3012` |
| `rFlags` / `Rflags` | `Frf` | `FASM3012` |
| `Rip` | `Ipr` | `FASM3012` |
| `CS` / `DS` / `ES` / `FS` / `GS` / `SS` | `Srf0` / `Srf1` / `Srf2` / `Srf3` / `Srf4` / `Srf5` | `FASM3012` |
| `CR0` … `CR4`, `CR8` | `Crf0` … `Crf4`, `Crf8` | `FASM3012` |
| `DR0` … `DR7` | `Drf0` … `Drf7` | `FASM3012` |
| `XMM0` | `Simd0.r128` | `FASM3012` |
| `YMM0` | `Simd0.r256` | `FASM3012` |
| `ZMM0` | `Simd0.r512` | `FASM3012` |
| `MM0` | **no existe** | `FASM3073` (MMX prohibido) |
| `ST(0)` / `ST0` | `Xrf0` | `FASM3012` |
| `K0` … `K7` | `Krf0` … `Krf7` | `FASM3012` |
| `R0q`, `R1q` (portable) | `GprN` del target | capa portable fuera de v1 |

El orden de `Srf` **no** es el campo `sreg` del encoding: `Srf0`=CS(1), `Srf1`=DS(3), `Srf2`=ES(0), `Srf3`=FS(4), `Srf4`=GS(5), `Srf5`=SS(2). El encoder traduce (doc 03 §4).

---

## 4. Verbos

| Antes | Ahora | Instrucción |
|---|---|---|
| `.copy(42 to Rax)` | `Gpr0.copy(<fuente>)` — **el 42 debe venir de memoria/parámetro** | `MOV` |
| `.copy(a to Rax)` | `Gpr0.copy(a)` | `MOV` |
| `.alu(Rax add Rax)` | `Gpr0.add(Gpr0)` | `ADD` |
| `.alu(Rax add b)` | `Gpr0.add(b)` | `ADD` |
| `.alu(Rax sub b)` | `Gpr0.sub(b)` | `SUB` |
| `.alu(Rax and b)` | `Gpr0.and(b)` | `AND` |
| `mov` / `Mov` | `copy` | `MOV` |
| `add` | `add` | `ADD` |
| `sub` | `sub` | `SUB` |
| `cmp` | `compare` | `CMP` |
| `test` | `test` | `TEST` |
| `imul r, r/m` | `GprN.multiplyLow(r/m)` | `IMUL r64, r/m64` |
| `mul r/m` | `(Gpr2:Gpr0).multiply(r/m)` | `MUL` |
| `imul` (128) | `(Gpr2:Gpr0).multiplySigned(r/m)` | `IMUL` |
| `div` | `(Gpr0:Gpr2).divide(r/m)` | `DIV` |
| `idiv` | `(Gpr0:Gpr2).divideSigned(r/m)` | `IDIV` |
| `cqo` / `cdq` | `(Gpr2:Gpr0).signExtend()` | `CQO` |
| `movzx` | `GprN.loadUnsigned(m8/m16)` | `MOVZX` |
| `movsx` | `GprN.loadSigned(m8/m16/m32)` | `MOVSX` |
| `lea` | `GprN.loadAddress(símbolo)` | `LEA` |
| `shl` / `Gpr0.shl(...)` | `shiftLeft` | `SHL` |
| `shr` | `shiftRightLogical` | `SHR` |
| `sar` | `shiftRightArithmetic` | `SAR` |
| `rol` / `ror` | `rotateLeft` / `rotateRight` | `ROL` / `ROR` |
| `not` | `not` (sin paréntesis) | `NOT` |
| `neg` | `negate` | `NEG` |
| `inc` / `dec` | `increment` / `decrement` | `INC` / `DEC` |
| `xor eax, eax` | `Gpr0.clear()` | `XOR` |
| `push` / `pop` | `Stack.push(fuente)` / `Stack.pop(destino)` | `PUSH` / `POP` |
| `lock add` | `addLocked` | `LOCK ADD` |
| `xchg` | `exchange` | `XCHG` |
| `nop` | `Cpu.noOperation()` | `NOP` |
| `hlt` | `Cpu.halt()` | `HLT` |
| `int N` | `Ipr.flowInterrupt(N)` | `INT` |
| `syscall` | `Ipr.flowSyscall()` | `SYSCALL` |
| `sysret` | `Ipr.flowSysret()` | `SYSRET` |
| `ret` | `Ipr.flowStackJump()` | `RET` |
| `call X` | `Ipr.flowCall(X)` | `CALL` |
| `jmp X` | `Ipr.flowJump(X)` | `JMP` |
| `jz` / `je X` | `Ipr.flowJump(X if Frf.zero)` | `JZ` |
| `jnz` / `jne X` | `Ipr.flowJump(X if not Frf.zero)` | `JNZ` |
| `jc X` | `Ipr.flowJump(X if Frf.carry)` | `JC` |
| `jnc X` | `Ipr.flowJump(X if not Frf.carry)` | `JNC` |
| `jl X` | `Ipr.flowJump(X if Frf.sign unequal Frf.overflow)` | `JL` |
| `jle X` | `Ipr.flowJump(X if Frf.zero or Frf.sign unequal Frf.overflow)` | `JLE` |
| `loop X` | `Ipr.flowLoop(X)` | `LOOP` |
| `rep movsb` | `Byte:N [Gpr7].copyBlock(Byte:N [Gpr6])` | `REP MOVSB` |
| `rep stosb` | `Byte:N [Gpr7].fillBlock(Gpr0.low8)` | `REP STOSB` |
| `Gpr.mult<2,0>(Gpr3)` | `(Gpr2:Gpr0).multiply(Gpr3)` | `MUL` |
| `Gpr.div<0,2>(Gpr3)` | `(Gpr0:Gpr2).divide(Gpr3)` | `DIV` |
| `Gpr4.sub(Bit:0x20)` | `Stack.reserve(Byte:32)` o `.frame(Byte:32)` | `SUB RSP` |
| `rFlags.carry` | `Frf.carry` | — |
| `rFlags.carry(1)` | `Frf.carry(1)` | `STC` |

> El `.alu(...)` desaparece. Cada operación tiene su verbo, y el verbo decide el opcode. Ya no hay un "ALU genérico" que el compilador tenga que desambiguar.

---

## 5. Cambios que no son mecánicos

Estos requieren decisión del programador. `fasm migrate` los **marca** y no los aplica.

### 5.1 Literales de dato

```
antes:  .copy(42 to Rax)
ahora:  Gpr0.copy(<origen>)
```

`42` ya no puede aparecer en `.execute`. Opciones:

| Caso | Solución |
|---|---|
| Constante del programa | `data c: Byte:8 = 42, 0,0,0,0,0,0,0` en `.rodata` + `Gpr0.copy(Byte:8 [c])` |
| Valor que aporta quien llama | `parameter n: Gpr` + `Gpr0.copy(n)` |
| Cero | `Gpr0.clear()` |
| Uno | `Gpr0.clear()` + `Gpr0.increment()` |
| Resultado de hardware | `(Gpr2:Gpr0).readTimestamp()` |

La herramienta inserta `// TODO(FASM2007): literal 42 movido a datos` y un `data` sugerido en `.rodata`, pero **no** lo crea sin confirmación.

### 5.2 `parameter` sin clase

```
antes:  parameter a, b
ahora:  parameter a: Gpr
        parameter b: Gpr
```

La clase no se puede inferir en todos los casos (un parámetro podría ser `Simd` o `Stack`). La herramienta propone `Gpr` y lo marca para revisión cuando el cuerpo usa el parámetro en un contexto SIMD o de pila.

### 5.3 `local` sin capacidad

`local x` necesita capacidad. La herramienta no la adivina: propone `Byte:8` y marca `// TODO(FASM3023)`.

### 5.4 Prólogo manual

```
antes:  Gpr4.sub(Bit:0x20)     // reservar 32 bytes
        ...
        Gpr4.add(Bit:0x20)
```

Ahora el prólogo se declara, no se escribe:

```fasm
routine f.abi(win64).frame(Byte:32) { … }
```

La herramienta detecta el par `sub/add` simétrico sobre `Gpr4` y lo convierte en `.frame`, pero solo si el delta es constante y no hay otro uso de `Gpr4` en medio.

### 5.5 `naked` y parámetros

Antes `Routine X Naked` convivía con `parameter`. Ahora `.naked(1)` con `.frame`/`.preserves`/`.shadow` es contradictorio (`FASM2014`), y un `parameter` en una rutina naked sigue siendo un alias (válido). La herramienta conserva `naked` solo si no hay prólogo declarado.

### 5.6 Implícitos y clobbers

Antes `flowLoop` usaba `Rcx` implícitamente sin decirlo. Ahora `flowLoop` documenta que usa `Gpr1` y el checker lo exige (`FASM4028` si falta declaración). La herramienta añade el comentario y la declaración de clobber cuando corresponde.

---

## 6. Rangos de diagnóstico

`CONTRIBUTING.md` describe rangos antiguos. La tabla de conversión:

| Antes | Ahora | Contenido |
|---|---|---|
| `FASM1xxx` léxico | `FASM1xxx` | léxico |
| `FASM2xxx` sintáctico | `FASM11xx` | sintaxis |
| `FASM2xxx` programa | `FASM2xxx` | programa, símbolos, datos |
| `FASM3xxx` semántico/ABI | `FASM3xxx` | ATM, registros, SIMD, x87 |
| `FASM4xxx` codificación | `FASM4xxx` | operandos, verbos, flujo |
| `FASM5xxx` layout/relocs | `FASM5xxx` | ABI |
| — | `FASM6xxx` | target |
| — | `FASM8xxx` | estilo y bootstrap |
| — | `FASM9xxx` | interno |

Los códigos con el mismo número **no** significan lo mismo entre versiones. Un mensaje antiguo `FASM3001` ("más de 4 parámetros") ahora es `FASM3001` ("verbo no disponible para ese receptor") o `FASM5002` (parámetros de pila). No se conserva la numeración antigua: se conserva la **estabilidad** de la nueva a partir de v1.0.

---

## 7. La herramienta `fasm migrate`

### 7.1 Interfaz

```
fluentasm migrate <archivo.fasm> [--in-place] [--report migracion.md]
```

Sin `--in-place` imprime el diff. `--report` genera un informe de lo que no pudo aplicarse.

### 7.2 Fases

| Fase | Acción | Seguridad |
|---|---|---|
| 1 | Lexer del lenguaje **antiguo** | completa |
| 2 | Parser del lenguaje antiguo | recupera bloques `End` |
| 3 | Reescritura estructural: `end` → `}`, `Routine` → `routine`, `Naked` → `.naked(1)` | mecánica |
| 4 | Renombrado de registros (`Rax` → `Gpr0`, …) | mecánica |
| 5 | Renombrado de verbos (`.alu` → verbo concreto) | mecánica si el operando es identificable |
| 6 | Reescritura de flujo (`jz` → `flowJump(... if Frf.zero)`) | mecánica |
| 7 | Detección de literales de dato | **no aplica**; marca `FASM2007` |
| 8 | Detección de prólogo manual | convierte si es simétrico |
| 9 | Emisión del fuente nuevo + informe | — |

### 7.3 Garantías

1. **Nunca borra código.** Lo que no entiende lo deja comentado con un `TODO`.
2. **Nunca inventa datos.** Un literal se convierte en `data` solo con `--materialize-literals`, y aun así lo marca con `FASM8101`.
3. **Es idempotente.** Ejecutarla dos veces no cambia el resultado de la primera.
4. **Produce un diff revisable.** `--in-place` exige `--yes` en CI.
5. **No compila.** La migración termina con `fluentasm check`; si hay errores, el informe los lista.

### 7.4 Ejemplo

Antes:

```fasm
program SumaDos
section .execute
routine AddTwoNumbers
    parameter a, b
    .copy(a to Rax)
    .alu(Rax add b)
    Ipr.flowStackJump()
end
routine main
    .copy(10 to Rcx)
    .copy(32 to Rdx)
    Ipr.flowCall(AddTwoNumbers)
    Ipr.flowStackJump()
end
end
```

Después (`fasm migrate --report`):

```fasm
program SumaDos.abi(win64).target(x64-avx2).entry(main) {

    section .rodata {
        // TODO(FASM2007): literal 10 movido desde .execute
        data literal_10: Byte:8 = 10, 0, 0, 0, 0, 0, 0, 0
        // TODO(FASM2007): literal 32 movido desde .execute
        data literal_32: Byte:8 = 32, 0, 0, 0, 0, 0, 0, 0
    }

    section .execute {

        routine AddTwoNumbers.abi(win64) {
            parameter a: Gpr
            parameter b: Gpr
            Gpr0.copy(a)
            Gpr0.add(b)
            Ipr.flowStackJump()
        }

        routine main.abi(win64).frame(Byte:0).shadow(1) {
            Gpr1.copy(Byte:8 [literal_10])
            Gpr2.copy(Byte:8 [literal_32])
            Ipr.flowCall(AddTwoNumbers)
            Ipr.flowStackJump()
        }
    }
}
```

Informe:

```
MIGRACIÓN — SumaDos.fasm
  estructural   : 1 program, 1 section, 2 routines
  registros     : 5 renombrados (Rax→Gpr0, Rcx→Gpr1, Rdx→Gpr2)
  verbos        : 4 (.copy→copy, .alu(add)→add)
  literales     : 2 marcados (FASM2007) — requieren revisión
  prólogo manual: 0
  no aplicado   : 0
Acción requerida: revisar los 2 literales en .rodata
```

---

## 8. Lista de verificación de una migración

- [ ] `program` tiene llaves y `.entry` apunta a una rutina existente.
- [ ] No queda ningún `end`, `End`, `Routine`, `Naked`, `Program`, `Section`.
- [ ] No queda ningún nombre legacy de registro (`Rax`, `Eax`, `XMM0`, `CR0`, …).
- [ ] Cada `parameter` tiene clase (`Gpr`, `Simd`, `Stack`).
- [ ] Cada `local` tiene capacidad (`Byte:N`).
- [ ] Ningún literal de dato en `.execute` (`fluentasm check` limpio de `FASM2007`).
- [ ] Los `jz`/`jl`/… son `flowJump(... if …)`.
- [ ] `ret` es `Ipr.flowStackJump()`; `call` es `Ipr.flowCall(...)`.
- [ ] El prólogo es declarativo (`.frame`, `.preserves`, `.shadow`), no manual.
- [ ] `rep movsb` es `copyBlock` con capacidad.
- [ ] Los `.rodata`/`.data` tienen capacidad correcta (`FASM2019`).
- [ ] `fluentasm build` produce bytes y `--map` lista los símbolos.
- [ ] Los *golden files* de [15-EJEMPLOS](15-EJEMPLOS.md) siguen pasando tras la migración.

---

## 9. Lo que no se migra

| Elemento antiguo | Estado |
|---|---|
| Capa portable (`R0q`, `X0`, ARM64) | fuera de v1 (solo x64) |
| `.fproj` como fuente de la ABI | opcional; la cabecera manda |
| `fasm.fdoc` | sigue siendo un archivo de documentación del IDE, no del lenguaje |
| Macros y `If`/`While` de alto nivel | no existen en v1 |
| `.bin` (partes) | formato de producto, no del lenguaje |
| Nombres de 32 bits (`Eax`) | solo como vista `.low32` |
| MMX | prohibido (`FASM3073`) |
| Sintaxis `Gpr.mult<2,0>` | sustituida por tuplas `(Gpr2:Gpr0)` |

---

## 10. Resumen

- La sintaxis anterior no es aceptada; la migración es explícita y revisable.
- Los cambios estructurales, de registro y de verbo son mecánicos.
- Los literales de dato, las clases de parámetro y el prólogo manual requieren decisión humana.
- Los rangos de diagnóstico se remapean; los números antiguos no conservan significado.
- `fasm migrate` reescribe, marca y reporta; nunca inventa datos ni compila a escondidas.
- La migración termina cuando `fluentasm check` está limpio y los *golden files* pasan.
