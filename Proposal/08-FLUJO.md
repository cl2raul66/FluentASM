# 08 — Flujo de control

> **Normativo.** Todo cambio de `Ipr` pasa por un verbo `flow*`. No hay saltos implícitos ni estructuras de control del lenguaje: hay instrucciones de salto, llamada, retorno, bucle e interrupción, y un registro de flags que las condiciona.

---

## 1. Modelo

El flujo tiene dos piezas:

| Pieza | Registro | Cambia por |
|---|---|---|
| Puntero de instrucción | `Ipr` | Solo verbos `flow*` (D-009) |
| Condición | `Frf` | Verbos de ALU, `Frf.*`, comparaciones |

No existe `if`, `while`, `for`, `return` ni `goto` como palabras clave. Existe:

```fasm
Ipr.flowJump(@etiqueta if condición)
```

que es exactamente `Jcc rel32`.

### 1.1 Propiedades verificadas

El compilador construye el **CFG** (grafo de flujo) por rutina y verifica:

- Que cada etiqueta referenciada exista (`FASM2021`).
- Que cada camino termine en `flowStackJump`, `flowJump` o `Cpu.halt` (`FASM2013`, aviso).
- Que el delta de `Gpr4` (RSP) sea cero al entrar y salir de cada bloque (`FASM5004`).
- Que la pila esté alineada a 16 antes de cada `flowCall` (`FASM5003`).
- Que no se lea un flag indefinido (`FASM4012`).

---

## 2. Salto

```fasm
Ipr.flowJump(@destino)
Ipr.flowJump(@destino if condición)
Ipr.flowJump(Gpr0)
Ipr.flowJump(Byte:8 [tabla + Gpr1*8])
```

| Forma | Encoding | Cuándo |
|---|---|---|
| Etiqueta cercana | `EB cb` (rel8) o `E9 cd` (rel32) | El más corto que alcance |
| Etiqueta lejana | `E9 cd` | Fuera de ±2 GiB → `FASM4020` |
| Registro | `FF /4` | Destino calculado |
| Memoria | `FF /4` | Tabla de saltos |

**Un salto a la instrucción siguiente se elimina** solo si es incondicional y no tiene efectos; el compilador lo marca con `FASM8105` (info) en lugar de emitirlo.

### 2.1 Condiciones

La condición se escribe como sufijo de la fuente:

```fasm
Ipr.flowJump(@fin)
Ipr.flowJump(@fin if Frf.zero)
Ipr.flowJump(@fin if not Frf.carry)
Ipr.flowJump(@reintentar if Frf.sign unequal Frf.overflow)
```

La tabla completa de expresiones → `Jcc` está en [07-VERBOS](07-VERBOS.md) §6.1.

Regla de composición: el compilador busca primero una correspondencia **exacta** con un `Jcc`; si la expresión es una combinación booleana que no corresponde a ningún predicado del hardware, la expande:

```fasm
// No hay Jcc para (ZF=1 and CF=0):
Ipr.flowJump(@x if Frf.zero and not Frf.carry)
// se expande a:
//   JNE @no
//   JC  @no
//   JMP @x
// @no:
```

La expansión se documenta y se marca con `FASM8104` (info). Nunca cambia la semántica, solo el tamaño.

### 2.2 Orden de las condiciones

`Frf.equal` es sinónimo de `Frf.zero`; `Frf.notEqual` de `not Frf.zero`. Son **alias de lectura**, no bits nuevos: el compilador los resuelve al mismo `Jcc`.

| Alias | Equivalente |
|---|---|
| `Frf.equal` | `Frf.zero` |
| `Frf.notEqual` | `not Frf.zero` |
| `Frf.less` | `Frf.sign unequal Frf.overflow` |
| `Frf.greaterOrEqual` | `Frf.sign equal Frf.overflow` |
| `Frf.lessOrEqual` | `Frf.zero or Frf.sign unequal Frf.overflow` |
| `Frf.greater` | `not Frf.zero and Frf.sign equal Frf.overflow` |
| `Frf.below` | `Frf.carry` |
| `Frf.aboveOrEqual` | `not Frf.carry` |
| `Frf.belowOrEqual` | `Frf.carry or Frf.zero` |
| `Frf.above` | `not Frf.carry and not Frf.zero` |

Los alias hacen el código legible sin ocultar los flags: el hover del editor muestra la expresión expandida.

---

## 3. Llamada

```fasm
Ipr.flowCall(rutina)
Ipr.flowCall(Gpr0)
Ipr.flowCall(Byte:8 [tabla + Gpr1*8])
```

### 3.1 Efecto físico

| Paso | Efecto |
|---|---|
| 1 | `Gpr4 -= 8` |
| 2 | `[Gpr4] = Ipr_siguiente` |
| 3 | `Ipr = destino` |

El compilador verifica antes de emitir:

- `Gpr4` alineado a 16 (`FASM5003`).
- Shadow space reservado si la ABI es Win64 y la rutina destino no es *leaf* (`FASM5002`).
- El destino existe y es una rutina del mismo programa (`FASM2003`, `FASM5001`).

### 3.2 Argumentos

Los argumentos se colocan **antes** de la llamada, con `copy` explícito:

```fasm
routine main.abi(win64).frame(Byte:16).shadow(1) {
    Gpr1.copy(Byte:8 [a])       // arg 0 (Win64)
    Gpr2.copy(Byte:8 [b])       // arg 1
    Ipr.flowCall(sumar)
}
```

No hay "paso de argumentos" implícito en `flowCall`: el verbo solo cambia el flujo. La correspondencia entre `parameter` y registro la define la ABI (doc 12) y la verifica el checker.

### 3.3 Llamada de cola

```fasm
Ipr.flowJump(rutina)
```

Un `flowJump` a una rutina es una llamada de cola: no empuja dirección de retorno. El checker comprueba que el frame esté restaurado (`FASM5004`) antes de permitirlo.

---

## 4. Retorno

```fasm
Ipr.flowStackJump()
Ipr.flowStackJump(Byte:16)
```

| Forma | Encoding | Efecto |
|---|---|---|
| `flowStackJump()` | `C3` | `Ipr = [Gpr4]`; `Gpr4 += 8` |
| `flowStackJump(Byte:N)` | `C2 iw` | Además `Gpr4 += N` (libera argumentos) |

Reglas:

- El valor de retorno debe estar en `Gpr0` (o `Simd0`) si la rutina declara `.returns(...)` (`FASM5007`).
- El frame debe estar restaurado; si `.frame` no es cero y no se ha hecho `leave`/`release`, `FASM5004`.
- Un `flowStackJump` fuera de una rutina → `FASM2029`.
- `Byte:N` debe ser múltiplo de 8 → `FASM4021`.

---

## 5. Bucle

```fasm
Ipr.flowLoop(@inicio)
Ipr.flowLoop(@inicio if Frf.zero)
Ipr.flowLoop(@inicio if not Frf.zero)
```

| Forma | Encoding | Condición |
|---|---|---|
| `flowLoop` | `E2 cb` | `Gpr1 -= 1`; salta si `Gpr1 != 0` |
| `flowLoop ... if Frf.zero` | `E1 cb` | Además `ZF = 1` |
| `flowLoop ... if not Frf.zero` | `E0 cb` | Además `ZF = 0` |

`flowLoop` **decrementa `Gpr1`** (64 bits) sin tocar flags. Es el único verbo de flujo con un registro implícito de datos, y está declarado en [03-REGISTROS](03-REGISTROS.md) §12.

Alcance: `rel8`, ±127 bytes. Un cuerpo mayor → `FASM4022`; el compilador sugiere la forma `decrement` + `flowJump`.

### 5.1 Patrones

```fasm
// Conteo descendente
Gpr1.copy(Byte:8 [n])          // n viene de memoria
@ciclo:
    // ... cuerpo ...
    Ipr.flowLoop(@ciclo)

// Conteo ascendente con comparación
Gpr0.clear()
@ciclo:
    // ... cuerpo con Gpr0 ...
    Gpr0.increment()
    Gpr0.compare(Byte:8 [n])
    Ipr.flowJump(@ciclo if Frf.below)
```

---

## 6. Transferencias lejanas

```fasm
(Ipr:Srf0).flowJump(Gpr0)
(Ipr:Srf0).flowCall(Gpr0)
(Ipr:Srf0).flowStackJump()
```

| Verbo | Encoding | Efecto |
|---|---|---|
| `flowJump` | `FF /5` con `m16:64` | Carga `CS:EIP` desde memoria |
| `flowCall` | `FF /3` | Empuja `CS:RIP`, carga el par |
| `flowStackJump` | `CB` | `RETF` |

El par `(Ipr:Srf0)` no es un objeto: es la declaración de que la transferencia cambia **segmento y offset**. Es la única forma de escribir un `jmp far` en el lenguaje.

---

## 7. Interrupciones y sistema

| Verbo | Encoding | Uso |
|---|---|---|
| `Ipr.flowInterrupt(0x80)` | `CD ib` | Puerta de interrupción |
| `Ipr.flowInterrupt(Byte:1 [v])` | `CD /3` | Vector en memoria |
| `Ipr.flowBreakpoint()` | `CC` | Punto de parada |
| `Ipr.flowOverflow()` | `CE` | `INTO` si `OF = 1` |
| `Ipr.flowSyscall()` | `0F 05` | `SYSCALL` (x64) |
| `Ipr.flowSysret()` | `0F 07` | `SYSRET` |

### 7.1 Convención de syscall

`SYSCALL` no sigue la ABI de función: usa registros fijos. El lenguaje no los esconde; el programa los coloca:

| Registro | Contenido |
|---|---|
| `Gpr0` | Número de servicio |
| `Gpr1` | Arg 1 |
| `Gpr2` | Arg 2 |
| `Gpr10` | Arg 3 |
| `Gpr8` | Arg 4 |
| `Gpr9` | Arg 5 |
| `Gpr11` | `RFLAGS` guardado |
| `Gpr0` | Resultado |

> El número de servicio es un **dato** del sistema operativo: entra por memoria o por parámetro, nunca se escribe como literal en `.execute` (doc 06).

### 7.2 Privilegio

`Ipr.flowInterrupt`, `flowSysret` y `Cpu.halt` requieren ring 0 → `FASM4023`. `SYSCALL` desde ring 3 es legal y cambia a ring 0 según `Msr.lstar`.

---

## 8. Tablas de despacho

```fasm
Gpr0.copy(Byte:8 [indice])
Gpr1.copy(Byte:8 [tabla + Gpr0*8])
Ipr.flowJump(Gpr1)
```

La tabla contiene direcciones de 64 bits. En imagen PIC, las entradas deben ser **offsets relativos** a la tabla, porque la base no se conoce en tiempo de carga:

```fasm
Gpr1.copy(Byte:8 [tabla + Gpr0*8])   // offset relativo
Gpr1.add(Gpr0)                         // ... cálculo explícito
Ipr.flowJump(Gpr1)
```

Un salto a una dirección absoluta almacenada en datos → `FASM5008`.

---

## 9. Código inalcanzable

El checker marca como `FASM4013` (aviso) cualquier sentencia que:

- sigue a un `flowJump` incondicional sin etiqueta intermedia,
- sigue a `flowStackJump`,
- sigue a `Cpu.halt`.

No la elimina (D-025): el programador decide.

---

## 10. Balance de pila

El compilador simula `Gpr4` por bloque básico. Cada verbo declara su delta:

| Verbo | Δ RSP |
|---|---|
| `Stack.push` | −8 |
| `Gpr0.pop(Stack)` | +8 |
| `Stack.reserve(Byte:N)` | −N |
| `Stack.release(Byte:N)` | +N |
| `Stack.enter(Byte:N)` | −N − 8 |
| `Stack.leave()` | variable (restaura) |
| `Ipr.flowCall` | −8 (y +8 al retorno) |
| `Ipr.flowStackJump` | +8 |
| `Stack.align(Byte:N)` | indeterminado → invalida el análisis (`FASM5005`) |

Al final de cada camino, el delta debe ser 0 → `FASM5004`. `Stack.align` se permite pero obliga a que la rutina declare `.frame(0)` y gestione todo manualmente.

---

## 11. Diagrama de un bucle completo

```fasm
routine contar.abi(win64).frame(Byte:16).returns(Gpr) {
    parameter limite: Gpr
    local i: Byte:8

    Gpr0.clear()
    Byte:8 [i].copy(Gpr0)              // i = 0 (cero estructural)
@ciclo:
    Gpr0.copy(Byte:8 [i])
    Gpr0.compare(limite)
    Ipr.flowJump(@fin if not Frf.below)   // i >= limite → salir

    Byte:8 [i].copy(Gpr0)
    Gpr0.increment()
    Byte:8 [i].copy(Gpr0)

    Ipr.flowJump(@ciclo)

@fin:
    Gpr0.copy(Byte:8 [i])
    Ipr.flowStackJump()
}
```

---

## 12. Diagnósticos de flujo

| Código | Situación |
|---|---|
| `FASM4010` | Etiqueta no definida |
| `FASM4011` | Salto fuera de alcance `rel8` (informativo; se usa `rel32`) |
| `FASM4012` | Lectura de un flag indefinido |
| `FASM4013` | Código inalcanzable (aviso) |
| `FASM4020` | Destino fuera de ±2 GiB |
| `FASM4021` | `flowStackJump(Byte:N)` con `N` no múltiplo de 8 |
| `FASM4022` | Cuerpo de `flowLoop` mayor de 127 bytes |
| `FASM4023` | Verbo de flujo privilegiado en ring insuficiente |
| `FASM5001` | Llamada a símbolo externo (no soportada en v1) |
| `FASM5002` | Shadow space no reservado |
| `FASM5003` | Pila desalineada en `flowCall` |
| `FASM5004` | Frame no restaurado al retornar |
| `FASM5005` | `Stack.align` impide el análisis de pila |
| `FASM5007` | `.returns` declarado y `Gpr0`/`Simd0` no escrito |
| `FASM5008` | Dirección absoluta en imagen PIC |

---

## 13. Resumen

- Todo el flujo es `Ipr.flow*`; no hay estructuras de control.
- La condición es un sufijo de la fuente y se resuelve a un `Jcc`.
- Los alias (`Frf.less`, `Frf.above`) son legibles y se expanden.
- `flowCall` no pasa argumentos: solo cambia el flujo.
- `flowLoop` decrementa `Gpr1` y no toca flags.
- El par lejano es `(Ipr:Srf0)`.
- El compilador verifica CFG, pila, alineación, privilegio y retorno.
