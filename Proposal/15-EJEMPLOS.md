# 15 — Ejemplos

> **Normativo.** Seis programas completos, con mapa de secciones, desensamblado y bytes esperados. Son los *golden files* de la primera suite de pruebas del compilador (doc 17). Si el compilador produce otros bytes para estas fuentes, uno de los dos está mal.

---

## 1. Convenciones de medida

| Convención | Significado |
|---|---|
| Offset | Dirección relativa al inicio de la imagen plana |
| `d32` | Desplazamiento de 32 bits, *little-endian* |
| `r32` | Desplazamiento relativo de 32 bits con signo (`rel32`) |
| `i8` / `i32` | Inmediato de 8 / 32 bits |
| Secciones | Orden canónico `.execute`, `.rodata`, `.data`, `.bss` (doc 16 §11) |
| Alineación | La declarada; por defecto `Byte:16` |
| Tamaño | Bytes reales del archivo; no se cuenta relleno posterior al último dato |

Todas las codificaciones son **la más corta posible** para el target declarado (D-025). No hay optimización posterior: el encoder elige la forma canónica una sola vez y la emite.

### 1.1 Por qué los ejemplos no tienen literales de dato

En `.execute` no aparece ningún valor que circule: ni `42`, ni `"texto"`, ni `1.5`, ni `'A'`. Los números que sí aparecen son **forma**:

| Aparición | Naturaleza | Ejemplo |
|---|---|---|
| Capacidad | forma | `Byte:8` |
| Cuenta de desplazamiento | forma | `Bit:3` |
| Partición SIMD | forma | `part{32}` |
| Desplazamiento de dirección | forma | `[Gpr6 + 16]` |
| Estado de flag | forma | `Frf.carry(1)` |
| Índice de syscall / puerto | forma | `Port[0x60]` |

Los datos viven en `.rodata`/`.data` (bootstrap, D-1) y entran por dirección y capacidad.

---

## 2. E1 — `suma`: el programa mínimo

Demuestra: cabecera, `.rodata`, carga RIP-relativa, suma registro↔memoria, retorno.

```fasm
program suma.abi(win64).target(x64-sse2).entry(main) {

    section .rodata.align(Byte:16) {
        data a: Byte:8 = 10, 0, 0, 0, 0, 0, 0, 0
        data b: Byte:8 = 20, 0, 0, 0, 0, 0, 0, 0
    }

    section .execute {
        routine main.abi(win64).naked(1) {
            Gpr0.copy(Byte:8 [a])       // rax = a
            Gpr0.add(Byte:8 [b])        // rax = a + b
            Ipr.flowStackJump()         // ret
        }
    }
}
```

### 2.1 Mapa de secciones

| Sección | Inicio | Tamaño | Fin | Contenido |
|---|---:|---:|---:|---|
| `.execute` | 0 | 15 | 15 | `main` |
| *(relleno)* | 15 | 1 | 16 | ceros |
| `.rodata` | 16 | 16 | 32 | `a` (16), `b` (24) |

Archivo: **32 bytes**.

### 2.2 Desensamblado

| Offset | Bytes | Instrucción | Cálculo |
|---:|---|---|---|
| 0 | `48 8B 05 09 00 00 00` | `MOV RAX, [RIP+9]` | `16 − 7 = 9` |
| 7 | `48 03 05 0A 00 00 00` | `ADD RAX, [RIP+10]` | `24 − 14 = 10` |
| 14 | `C3` | `RET` | — |

Resultado en `Gpr0.low8` = 30. `main` es `.naked(1)`: no llama a nadie, no necesita prólogo ni alineación.

---

## 3. E2 — `factorialDemo`: prólogo, llamada, bucle y `.data`

Demuestra: `.frame` + `.shadow` Win64, `flowCall`, bucle con `flowJump` condicional, `multiplyLow`, escritura en `.data`.

```fasm
program factorialDemo.abi(win64).target(x64-sse2).entry(main) {

    section .rodata.align(Byte:16) {
        data n: Byte:8 = 5, 0, 0, 0, 0, 0, 0, 0
    }

    section .data.align(Byte:16) {
        data resultado: Byte:8 = zero Byte:8
    }

    section .execute {

        routine main.abi(win64).frame(Byte:16).shadow(1) {
            Gpr1.copy(Byte:8 [n])             // arg0 = n
            Ipr.flowCall(factorial)            // rax = n!
            Byte:8 [resultado].copy(Gpr0)      // guarda rax
            Ipr.flowStackJump()                // epílogo + ret
        }

        routine factorial.abi(win64).naked(1) {
            parameter n: Gpr                   // alias de Gpr1 (RCX)
            Gpr0.clear()                       // rax = 1
            Gpr0.increment()
        @loop:
            Gpr0.multiplyLow(n)                // rax *= n
            n.decrement()                      // n--
            Ipr.flowJump(@loop if not Frf.zero)
            Ipr.flowStackJump()
        }
    }
}
```

### 3.1 Mapa de secciones

| Sección | Inicio | Tamaño | Fin | Contenido |
|---|---:|---:|---:|---|
| `.execute` | 0 | 48 | 48 | `main` (0–32), `factorial` (33–47) |
| `.rodata` | 48 | 8 | 56 | `n` |
| *(relleno)* | 56 | 8 | 64 | ceros |
| `.data` | 64 | 8 | 72 | `resultado` |

Archivo: **72 bytes**.

### 3.2 Desensamblado

`main` — `.frame(Byte:16).shadow(1)`, sin `preserves`; `total = 16 + 0 + 32 = 48`:

| Offset | Bytes | Instrucción |
|---:|---|---|
| 0 | `55` | `PUSH RBP` |
| 1 | `48 89 E5` | `MOV RBP, RSP` |
| 4 | `48 83 EC 30` | `SUB RSP, 48` (16 frame + 32 shadow) |
| 8 | `48 8B 0D 21 00 00 00` | `MOV RCX, [RIP+33]` |
| 15 | `E8 0D 00 00 00` | `CALL 33` (`factorial`) |
| 20 | `48 89 05 25 00 00 00` | `MOV [RIP+37], RAX` |
| 27 | `48 83 C4 30` | `ADD RSP, 48` |
| 31 | `5D` | `POP RBP` |
| 32 | `C3` | `RET` |

Cálculos: `48 − 15 = 33`; `33 − 20 = 13`; `64 − 27 = 37`.

`factorial` — `.naked(1)`:

| Offset | Bytes | Instrucción |
|---:|---|---|
| 33 | `31 C0` | `XOR EAX, EAX` |
| 35 | `48 FF C0` | `INC RAX` |
| 38 | `48 0F AF C1` | `IMUL RAX, RCX` |
| 42 | `48 FF C9` | `DEC RCX` |
| 45 | `75 F7` | `JNE 38` |
| 47 | `C3` | `RET` |

Cálculo del salto: instrucción en 45, siguiente en 47, destino 38 → `38 − 47 = −9 = 0xF7`.

### 3.3 Traza

| Iteración | `RAX` | `RCX` | `ZF` tras `DEC` | Salto |
|---:|---:|---:|---|---|
| inicial | 1 | 5 | — | — |
| 1 | 5 | 4 | 0 | sí |
| 2 | 20 | 3 | 0 | sí |
| 3 | 60 | 2 | 0 | sí |
| 4 | 120 | 1 | 0 | sí |
| 5 | 120 | 0 | 1 | no |

`resultado` = 120. No hay literales en `.execute`: el `5` está en `.rodata` y el `1` inicial se construye con `clear` + `increment`.

> El `DEC RCX` no toca `CF`, pero sí `ZF`; el salto depende solo de `ZF`. El checker lo verifica (`FASM4012`).

---

## 4. E3 — `sumaSimd`: AVX-512 y partición de 32 bits

Demuestra: `.r512`, `part{32}`, `addFloat` sobre memoria, `copy` de 64 bytes, target `x64-avx512`.

```fasm
program sumaSimd.abi(win64).target(x64-avx512).entry(main) {

    section .rodata.align(Byte:64) {
        data a: Byte:64 = repeat 16 { 0x00, 0x00, 0x80, 0x3F }   // 16 × 1.0f
        data b: Byte:64 = repeat 16 { 0x00, 0x00, 0x00, 0x40 }   // 16 × 2.0f
    }

    section .data.align(Byte:64) {
        data r: Byte:64 = zero Byte:64
    }

    section .execute {
        routine main.abi(win64).naked(1) {
            Simd0.r512.copy(Byte:64 [a])                 // zmm0 = a
            Simd0.r512.part{32}.addFloat(Byte:64 [b])    // zmm0 += b
            Byte:64 [r].copy(Simd0.r512)                 // r = zmm0
            Ipr.flowStackJump()
        }
    }
}
```

### 4.1 Mapa de secciones

| Sección | Inicio | Tamaño | Fin | Contenido |
|---|---:|---:|---:|---|
| `.execute` | 0 | 31 | 31 | `main` |
| *(relleno)* | 31 | 33 | 64 | ceros |
| `.rodata` | 64 | 128 | 192 | `a` (64), `b` (128) |
| `.data` | 192 | 64 | 256 | `r` |

Archivo: **256 bytes**.

### 4.2 Desensamblado

| Offset | Bytes | Instrucción | Cálculo |
|---:|---|---|---|
| 0 | `62 F1 FE 48 6F 05 36 00 00 00` | `VMOVDQU64 ZMM0, [RIP+54]` | `64 − 10 = 54` |
| 10 | `62 F1 7C 48 58 05 6C 00 00 00` | `VADDPS ZMM0, ZMM0, [RIP+108]` | `128 − 20 = 108` |
| 20 | `62 F1 FE 48 7F 05 A2 00 00 00` | `VMOVDQU64 [RIP+162], ZMM0` | `192 − 30 = 162` |
| 30 | `C3` | `RET` | — |

### 4.3 Desglose de los prefijos EVEX

`VADDPS ZMM0, ZMM0, m512` = `62 F1 7C 48 58 /r`:

| Byte | Valor | Campos |
|---|---|---|
| firma | `62` | EVEX |
| P0 | `F1` | `R̄ X̄ B̄ R̄'` = `1111`, `mm` = `01` (mapa 0F) |
| P1 | `7C` | `W` = 0, `v̄v̄v̄v̄` = `1111` (src1 = zmm0), `pp` = `00` |
| P2 | `48` | `z` = 0, `L'L` = `10` (512 bits), `b` = 0, `V'` = `1` |
| opcode | `58` | `VADDPS` |
| ModRM | `05` | `mod=00`, `reg=000`, `r/m=101` (RIP-relativo) |

`r` queda con 16 elementos de 3.0f (`0x40400000`).

---

## 5. E4 — `copia`: bloque con `REP MOVSB`

Demuestra: `copyBlock`, materialización estructural de la cuenta, `Frf.direction`, clobbers declarados.

```fasm
program copia.abi(win64).target(x64-avx2).entry(main) {

    section .rodata.align(Byte:16) {
        data origen: Byte:64 = repeat 64 { 0xFF }
    }

    section .data.align(Byte:16) {
        data destino: Byte:64 = zero Byte:64
    }

    section .execute {
        routine main.abi(win64).naked(1) {
            Frf.direction(0)                            // DF = 0 (ascendente)
            Gpr6.loadAddress(origen)                    // RSI = &origen
            Gpr7.loadAddress(destino)                   // RDI = &destino
            Byte:64 [Gpr7].copyBlock(Byte:64 [Gpr6])    // REP MOVSB
            Ipr.flowStackJump()
        }
    }
}
```

### 5.1 Mapa de secciones

| Sección | Inicio | Tamaño | Fin | Contenido |
|---|---:|---:|---:|---|
| `.execute` | 0 | 23 | 23 | `main` |
| *(relleno)* | 23 | 9 | 32 | ceros |
| `.rodata` | 32 | 64 | 96 | `origen` |
| `.data` | 96 | 64 | 160 | `destino` |

Archivo: **160 bytes**.

### 5.2 Desensamblado

| Offset | Bytes | Instrucción | Cálculo |
|---:|---|---|---|
| 0 | `FC` | `CLD` | — |
| 1 | `48 8D 35 18 00 00 00` | `LEA RSI, [RIP+24]` | `32 − 8 = 24` |
| 8 | `48 8D 3D 51 00 00 00` | `LEA RDI, [RIP+81]` | `96 − 15 = 81` |
| 15 | `B9 40 00 00 00` | `MOV ECX, 64` | cuenta = capacidad |
| 20 | `F3 A4` | `REP MOVSB` | — |
| 22 | `C3` | `RET` | — |

### 5.3 La cuenta materializada

`copyBlock` recibe la capacidad `Byte:64` como **forma**. El `REP MOVSB` no codifica la cuenta en el opcode: el hardware la lee de `RCX`. El compilador debe escribirla:

```
MOV ECX, 64        // 5 bytes, valor = capacidad del bloque
```

Ese `64` no es un dato del programa — es la misma capacidad que ya está escrita en la fuente. Se marca con `FASM8106` (info) para que quede visible en el listado. Si la capacidad cambia, cambia el `imm32`; si el programador usa la forma con cuenta explícita `copyBlock(Byte:64 [Gpr6], Gpr1)`, no se materializa nada.

`copyBlock` declara que clobber `Gpr1` (RCX), `Gpr6` (RSI) y `Gpr7` (RDI). El checker lo exige (`FASM4028`).

---

## 6. E5 — `salir`: syscall de Linux (System V)

Demuestra: `.abi(sysv)`, `flowSyscall`, número de syscall desde `.rodata`, registro de argumento `Gpr7`.

```fasm
program salir.abi(sysv).target(x64-sse2).entry(main) {

    section .rodata.align(Byte:16) {
        data numeroExit: Byte:8 = 60, 0, 0, 0, 0, 0, 0, 0   // SYS_exit
        data codigo: Byte:8     = 0, 0, 0, 0, 0, 0, 0, 0
    }

    section .execute {
        routine main.abi(sysv).naked(1) {
            Gpr0.copy(Byte:8 [numeroExit])      // RAX = 60
            Gpr7.copy(Byte:8 [codigo])          // RDI = 0
            Ipr.flowSyscall()                   // syscall
            Ipr.flowStackJump()                 // por si retorna
        }
    }
}
```

### 6.1 Mapa de secciones

| Sección | Inicio | Tamaño | Fin | Contenido |
|---|---:|---:|---:|---|
| `.execute` | 0 | 17 | 17 | `main` |
| *(relleno)* | 17 | 15 | 32 | ceros |
| `.rodata` | 32 | 16 | 48 | `numeroExit` (32), `codigo` (40) |

Archivo: **48 bytes**.

### 6.2 Desensamblado

| Offset | Bytes | Instrucción | Cálculo |
|---:|---|---|---|
| 0 | `48 8B 05 19 00 00 00` | `MOV RAX, [RIP+25]` | `32 − 7 = 25` |
| 7 | `48 8B 3D 1A 00 00 00` | `MOV RDI, [RIP+26]` | `40 − 14 = 26` |
| 14 | `0F 05` | `SYSCALL` | — |
| 16 | `C3` | `RET` | — |

### 6.3 Notas

- `Gpr1` (RCX) y `Gpr9` (R11) los clobber `SYSCALL`: el checker los invalida después (`FASM5008` no aplica, pero el modelo de registros sí los marca volátiles).
- El número `60` no está en `.execute`; está en `.rodata` y se carga por dirección. Cambiar de `exit` a `write` es cambiar un dato, no el código.
- En Win64 este programa sería inválido: no hay `syscall` de Linux y la ABI no lo permite (`FASM5010`).

---

## 7. E6 — `hipotenusa`: x87 y precisión extendida

Demuestra: pila x87, `loadFloat`/`storeFloatPop`, `addFloatPop`, `squareRoot`, bytes IEEE 754 en `.rodata`.

```fasm
program hipotenusa.abi(win64).target(x64-sse2).entry(main) {

    section .rodata.align(Byte:16) {
        data x: Byte:8 = 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x40   // 3.0
        data y: Byte:8 = 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x40   // 4.0
    }

    section .data.align(Byte:16) {
        data h: Byte:8 = zero Byte:8
    }

    section .execute {
        routine main.abi(win64).naked(1) {
            Xrf.loadFloat(Byte:8 [x])          // st0 = x
            Xrf0.multiplyFloat(Xrf0)           // st0 = x²
            Xrf.loadFloat(Byte:8 [y])          // st0 = y, st1 = x²
            Xrf0.multiplyFloat(Xrf0)           // st0 = y², st1 = x²
            Xrf1.addFloatPop(Xrf0)             // st0 = x² + y²
            Xrf0.squareRoot()                  // st0 = sqrt(x² + y²)
            Xrf0.storeFloatPop(Byte:8 [h])     // h = st0, pop
            Ipr.flowStackJump()
        }
    }
}
```

### 7.1 Mapa de secciones

| Sección | Inicio | Tamaño | Fin | Contenido |
|---|---:|---:|---:|---|
| `.execute` | 0 | 27 | 27 | `main` |
| *(relleno)* | 27 | 5 | 32 | ceros |
| `.rodata` | 32 | 16 | 48 | `x` (32), `y` (40) |
| `.data` | 48 | 8 | 56 | `h` |

Archivo: **56 bytes**.

### 7.2 Desensamblado

| Offset | Bytes | Instrucción | Cálculo |
|---:|---|---|---|
| 0 | `DD 05 1A 00 00 00` | `FLD qword [RIP+26]` | `32 − 6 = 26` |
| 6 | `D8 C8` | `FMUL ST(0), ST(0)` | — |
| 8 | `DD 05 1A 00 00 00` | `FLD qword [RIP+26]` | `40 − 14 = 26` |
| 14 | `D8 C8` | `FMUL ST(0), ST(0)` | — |
| 16 | `DE C1` | `FADDP ST(1), ST(0)` | — |
| 18 | `D9 FA` | `FSQRT` | — |
| 20 | `DD 1D 16 00 00 00` | `FSTP qword [RIP+22]` | `48 − 26 = 22` |
| 26 | `C3` | `RET` | — |

### 7.3 Verificación de profundidad

| Paso | Profundidad | `st0` |
|---|---:|---|
| inicio | 0 | — |
| `loadFloat(x)` | 1 | x |
| `multiplyFloat` | 1 | x² |
| `loadFloat(y)` | 2 | y |
| `multiplyFloat` | 2 | y² |
| `addFloatPop` | 1 | x² + y² |
| `squareRoot` | 1 | 5.0 |
| `storeFloatPop` | 0 | — |

`h` = `0x4014000000000000` (5.0). La pila queda vacía al final, como exige `FASM3076`.

---

## 8. Resumen de tamaños

| Ejemplo | Target | `.execute` | `.rodata` | `.data` | Archivo |
|---|---|---:|---:|---:|---:|
| E1 `suma` | `x64-sse2` | 15 | 16 | — | 32 |
| E2 `factorialDemo` | `x64-sse2` | 48 | 8 | 8 | 72 |
| E3 `sumaSimd` | `x64-avx512` | 31 | 128 | 64 | 256 |
| E4 `copia` | `x64-avx2` | 23 | 64 | 64 | 160 |
| E5 `salir` | `x64-sse2` | 17 | 16 | — | 48 |
| E6 `hipotenusa` | `x64-sse2` | 27 | 16 | 8 | 56 |

Ninguno supera los 256 bytes y todos cubren una familia distinta del lenguaje: carga, aritmética, control de flujo, SIMD, bloque, E/S de sistema y coma flotante.

---

## 9. Lo que el checker rechaza en estos mismos programas

Cada ejemplo tiene un "gemelo inválido" que la suite usa como prueba negativa:

| Ejemplo | Cambio | Diagnóstico |
|---|---|---|
| E1 | `Gpr0.copy(10)` en lugar de la carga | `FASM2007` (dato en instrucción) |
| E2 | `.shadow(1)` omitido en `main` | `FASM5002` (shadow space) |
| E2 | `Gpr0.multiply(Gpr1)` (sin tupla) | `FASM3017` (tupla requerida) |
| E3 | `.r512` con `target(x64-avx2)` | `FASM3080` / `FASM6003` |
| E3 | `part{10}` | `FASM3087` / `FASM3028` |
| E4 | `Byte:64 [Gpr7].copyBlock(Byte:64 [Gpr6])` sin declarar clobbers | `FASM4028` |
| E5 | `.abi(win64)` con `flowSyscall` | `FASM5010` |
| E6 | dejar la pila x87 con un elemento | `FASM3076` (aviso) |
| E6 | `Xrf2.storeFloatPop(h)` | `FASM3075` |

---

## 10. Uso como suite de pruebas

1. **Golden bytes.** Cada programa produce exactamente los bytes de la tabla. La prueba compara hex, no mnemónicos.
2. **Round-trip de desensamblado.** El desensamblador del IDE debe reconstruir la acción FluentASM con las mismas capacidades y vistas.
3. **Errores negativos.** La tabla de §9 comprueba que el código se emite y que el mensaje es el esperado.
4. **Determinismo.** Compilar dos veces con el mismo target produce archivos idénticos byte a byte (`sha256` igual).

El formato de los *golden files* (`tests/golden/E1.expected`) es: `offset, byte, byte, …` por línea, con un comentario opcional que repite la instrucción. La herramienta `fasm test` los recorre sin intervención.

---

## 11. Resumen

- Seis programas, seis familias del lenguaje, todos sin literales de dato en `.execute`.
- Bytes calculados a mano y verificados con las tablas de encoding de [07-VERBOS](07-VERBOS.md) y [16-CODIFICACION](16-CODIFICACION.md).
- Los tamaños son consecuencia de la regla "encoding más corto", no de una optimización.
- Cada ejemplo tiene un gemelo inválido con su diagnóstico.
- La suite es ejecutable desde el milestone M1 del compilador (doc 17).
