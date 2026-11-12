# Referencia de sintaxis de FluentASM — x64 (Nivel 3: Nativo)

> **Documento específico de arquitectura x64.** Para la sintaxis común compartida, véase [FLUENT_SYNTAX.md](FLUENT_SYNTAX.md).

**Propuestas aplicadas:**
- [Propuesta 0001](proposal-0001-copy-operand-order.md) — `copy` = `dest.copy(src)`
- [Propuesta 0003](proposal-0003-gpr.md) — Registros `Gpr0`–`Gpr15` + vistas `.low32/.low16/.low8/.high8`
- [Propuesta 0004](proposal-0004-types.md) — Tipos base `Bit` / `Byte`
- [Propuesta 0006](proposal-0006-instruction-syntax.md) — Sintaxis unificada (Categorías A–E)
- [Propuesta 0008](proposal-0008-x64-abi.md) — ABI x64 (Win64 default, System V opt-in)
- [Propuesta 0009](proposal-0009-x64-segment-registers.md) — Segment Register File `Srf0`–`Srf5`
- [Propuesta 0010](proposal-0010-x64-control-registers.md) — Control Register File `Crf0`–`Crf4`, `Crf8`
- [Propuesta 0012](proposal-0012-x64-rip-rflags.md) — Ipr y Flags Register File `Frf`

---

## 5.2 Registros x64 (Nivel 3: Nativo) — Gpr0–Gpr15

Cuando necesitas control total o instrucciones específicas de x64, usas los registros GPR nativos bajo el esquema unificado `GprN`. Cada registro es una celda de 64 bits en el *register file*; las vistas (`.low32`, `.low16`, `.low8`, `.high8`) son **partes direccionables** de esa misma celda.

> **Diferencia conceptual:** Un **registro** (`Gpr0`) es el contenedor físico. Una **vista** (`Gpr0.low32`) es una ventana sobre ese contenedor. Un **tipo** (`Bit:64`, `Byte:8`) describe el significado de los bits. [Propuesta 0004]

### Tabla de Registros

| GprN | Legacy | Vistas válidas | Rol en ABI Win64 | Rol en ABI System V |
|------|--------|----------------|------------------|---------------------|
| `Gpr0` | `Rax` | `Gpr0` (64b), `.low32`, `.low16`, `.low8`, `.high8` | Valor de retorno; volátil | Valor de retorno; volátil |
| `Gpr1` | `Rcx` | `Gpr1`, `.low32`, `.low16`, `.low8`, `.high8` | Parámetro 1; volátil; contador `flowLoop`/string; shift amount | Parámetro 4; volátil; contador `flowLoop`/string; shift amount |
| `Gpr2` | `Rdx` | `Gpr2`, `.low32`, `.low16`, `.low8`, `.high8` | Parámetro 2; volátil; high half `Gpr.mult/div` | Parámetro 3; volátil; high half `Gpr.mult/div` |
| `Gpr3` | `Rbx` | `Gpr3`, `.low32`, `.low16`, `.low8`, `.high8` | Callee-saved; implícito `cmpxchg8b/16b` | Callee-saved; implícito `cmpxchg8b/16b` |
| `Gpr4` | `Rsp` | `Gpr4`, `.low32`, `.low16`, `.low8` | Stack pointer; NO índice SIB; `.low8` requiere REX | Stack pointer; NO índice SIB; `.low8` requiere REX |
| `Gpr5` | `Rbp` | `Gpr5`, `.low32`, `.low16`, `.low8` | Frame pointer; modo directo requiere `disp8=0` | Frame pointer; modo directo requiere `disp8=0` |
| `Gpr6` | `Rsi` | `Gpr6`, `.low32`, `.low16`, `.low8` | Volátil; string source | Parámetro 2; callee-saved; string source |
| `Gpr7` | `Rdi` | `Gpr7`, `.low32`, `.low16`, `.low8` | Volátil; string dest | Parámetro 1; callee-saved; string dest |
| `Gpr8` | `R8`  | `Gpr8`, `.low32`, `.low16`, `.low8` | Parámetro 3; volátil | Parámetro 5; volátil |
| `Gpr9` | `R9`  | `Gpr9`, `.low32`, `.low16`, `.low8` | Parámetro 4; volátil | Parámetro 6; volátil |
| `Gpr10` | `R10` | `Gpr10`, `.low32`, `.low16`, `.low8` | Volátil; destruido por `syscall` | Parámetro 4 (System V); volátil; destruido por `syscall` |
| `Gpr11` | `R11` | `Gpr11`, `.low32`, `.low16`, `.low8` | Volátil; destruido por `syscall` (guarda RFLAGS) | Volátil; destruido por `syscall` (guarda RFLAGS) |
| `Gpr12` | `R12` | `Gpr12`, `.low32`, `.low16`, `.low8` | Callee-saved; como base fuerza byte SIB extra | Callee-saved; como base fuerza byte SIB extra |
| `Gpr13` | `R13` | `Gpr13`, `.low32`, `.low16`, `.low8` | Callee-saved; como base requiere `disp8=0` | Callee-saved; como base requiere `disp8=0` |
| `Gpr14` | `R14` | `Gpr14`, `.low32`, `.low16`, `.low8` | Callee-saved | Callee-saved |
| `Gpr15` | `R15` | `Gpr15`, `.low32`, `.low16`, `.low8` | Callee-saved | Callee-saved |

### Reglas de Vistas (Físicas)

| Vista | Bits | Efecto al escribir | Fuente |
|-------|------|-------------------|--------|
| `GprN` | 0–63 | Reemplaza todo | — |
| `.low32` | 0–31 | **Zero-extend**: limpia bits 32–63 | SDM Vol. 1 §3.4.1.1 |
| `.low16` | 0–15 | **Merge**: conserva bits 16–63 | SDM Vol. 1 §3.4.1.1 |
| `.low8` | 0–7 | **Merge**: conserva bits 8–63 | SDM Vol. 1 §3.4.1.1 |
| `.high8` | 8–15 | **Merge**: solo en `Gpr0`–`Gpr3` | SDM Vol. 1 §3.4.1.1 |

**Restricciones:**
- `Gpr4`–`Gpr15` **no tienen `.high8`** (encoding ocupado por `SPL`/`BPL`/`SIL`/`DIL` + REX).
- `.low8` en `Gpr4`–`Gpr7` (`SPL`/`BPL`/`SIL`/`DIL`) **requiere prefijo REX** (`0x40`).
- Una instrucción no puede referenciar a la vez `AH`/`BH`/`CH`/`DH` y `SPL`/`BPL`/`SIL`/`DIL` → `FASM3002`. [SDM Vol. 1 §3.4.1.1]
- Nombres legacy (`Rax`, `Eax`...) emiten `FASM3012` con sugerencia al canónico. [Propuesta 0003]

---

## 5.2.1 Catálogo por Registro (Instrucciones Específicas)

Instrucciones donde cada registro tiene **rol implícito** en el hardware.

### Gpr0 (Rax) — Acumulador / Retorno

| Operación | Sintaxis | Qué hace |
|-----------|----------|----------|
| **Retorno función** | `Gpr0.copy(Bit:42)` antes de `Ipr.flowStackJump()` | Valor de retorno en `Gpr0` |
| **Multiplicación 128b** | `Gpr.mult<2,0>(Gpr3)` | `Gpr2:Gpr0 = Gpr0 × Gpr3` |
| **División 128b** | `Gpr.div<0,2>(Gpr3)` | `Gpr0=cociente, Gpr2=resto` |
| **`cmpxchg`** | *(Fase 2)* | Compara `Gpr0` con destino; intercambia si igual |
| **String ops** | *(Fase 2)* | `lodsb`/`stosb`/`scasb` usan `Gpr0.low8` implícito |
| **Syscall Linux** | `.syscall()` (Fase 3) | Número de llamada en `Gpr0` |

### Gpr1 (Rcx) — Contador / Parámetro 1

| Operación | Sintaxis | Qué hace |
|-----------|----------|----------|
| **Loop hardware** | `Ipr.flowLoop(@label)` | `Gpr1--; if Gpr1≠0: RIP = label` — **no modifica flags** |
| **Contador string** | *(Fase 2)* `rep movsb` | Cuenta de elementos |
| **Shift amount** | `Gpr0.shl(Gpr1.low8)` | Cantidad de shift desde `CL` (máscara 6 bits en 64b) |
| **Parámetro 1 Win64** | `parameter n` → `n ≡ Gpr1` | Primer argumento entero/puntero |
| **Destruido por syscall** | — | `syscall` guarda RIP en `Gpr1` (solo System V) |

### Gpr2 (Rdx) — High Half / Parámetro 2

| Operación | Sintaxis | Qué hace |
|-----------|----------|----------|
| **High half `mult`** | `Gpr.mult<2,0>(Gpr3)` | Recibe high 64 bits del producto 128b |
| **High half `div`** | `Gpr.div<0,2>(Gpr3)` | Recibe resto de división 128÷64 |
| **División 128b preparación** | `Gpr2.copy(Bit:0)` | Limpiar high half antes de `div` si dividendo es 64b |
| **Sign-extend** | `Gpr0.signExtend<2,0>()` | Extiende `Gpr0` → `Gpr2:Gpr0` (prepara `idiv`) |
| **Parámetro 2 Win64** | `parameter a, b` → `b ≡ Gpr2` | Segundo argumento |
| **`rdtsc`** | *(Fase 2)* | Timestamp counter → `Gpr2:Gpr0` |

### Gpr3 (Rbx) — Callee-Saved / CMPXCHG

| Operación | Sintaxis | Qué hace |
|-----------|----------|----------|
| **Preservar valor** | `Gpr3.copy(x)` antes de `Ipr.flowCall` | Sobrevive a llamadas |
| **`cmpxchg8b/16b`** | *(Fase 2)* | Usa `Gpr3` implícito (EDX:EAX vs EBX:ECX) |
| **Sin rol en `mult/div`** | Operando explícito | Puede usarse como multiplicador/divisor |

### Gpr4 (Rsp) — Stack Pointer

| Operación | Sintaxis | Qué hace |
|-----------|----------|----------|
| **Implícito pila** | `.stackWrite(Gpr0)` | `Gpr4 -= 8; [Gpr4] = Gpr0` |
| **Implícito pila** | `Gpr0.stackRead()` | `Gpr0 = [Gpr4]; Gpr4 += 8` |
| **Prólogo frame** | `Gpr5.copy(Gpr4)` | Establecer frame pointer |
| **Reservar locales** | `Gpr4.sub(Bit:0x20)` | Ajustar pila para locales + shadow |
| **Obtener RSP** | `Gpr4.stackPointerGet()` | `Gpr0 = Gpr4` |
| **Establecer RSP** | `Gpr4.stackPointerSet(Gpr0)` | `Gpr4 = Gpr0` |

**Restricciones hardware:** NO usable como índice SIB (`FASM4003`). Encoding `r/m=100` siempre fuerza byte SIB. [SDM Vol. 2A, Tabla 2-5]

### Gpr5 (Rbp) — Frame Pointer

| Operación | Sintaxis | Qué hace |
|-----------|----------|----------|
| **Guardar RBP** | `.stackWrite(Gpr5)` | Preservar frame pointer anterior |
| **Establecer frame** | `Gpr5.copy(Gpr4)` | `mov rbp, rsp` |
| **Acceso locales** | `[Gpr5 + offset]` | Parámetros/locales relativos a frame |
| **Restaurar pila** | `Gpr4.copy(Gpr5)` | `mov rsp, rbp` |
| **Restaurar RBP** | `Gpr5.stackRead()` | `pop rbp` |

**Restricción hardware:** Encoding `r/m=101` + `mod=00` requiere `disp32` obligatorio. Compilador emite `disp8=0` automático. [SDM Vol. 2A, Tabla 2-5]

### Gpr6 (Rsi) / Gpr7 (Rdi) — String / Parámetros System V

| Registro | Win64 | System V |
|----------|-------|----------|
| **Gpr6** | Volátil, string source (`rep movs`) | Parámetro 2, callee-saved, string source |
| **Gpr7** | Volátil, string dest (`rep movs`) | Parámetro 1, callee-saved, string dest |

### Gpr8–Gpr11 — Parámetros Extendidos

| Registro | Win64 | System V |
|----------|-------|----------|
| **Gpr8** | Parámetro 3; volátil | Parámetro 5; volátil |
| **Gpr9** | Parámetro 4; volátil | Parámetro 6; volátil |
| **Gpr10** | Volátil; destruido por `syscall` | Parámetro 4; volátil; destruido por `syscall` |
| **Gpr11** | Volátil; destruido por `syscall` (RFLAGS) | Volátil; destruido por `syscall` (RFLAGS) |

### Gpr12–Gpr15 — Callee-Saved / Quirks SIB

| Registro | Notas |
|----------|-------|
| **Gpr12** | Como base fuerza byte SIB extra; como índice **SÍ permitido** con REX.X |
| **Gpr13** | Como base requiere `disp8=0` (como Gpr5) |
| **Gpr14** | Callee-saved normal |
| **Gpr15** | Callee-saved normal |

---

## 5.2.2 Instruction Pointer (Ipr)

> **Referencia completa:** [Propuesta 0012](proposal-0012-x64-rip-rflags.md)

`Ipr` es el **Instruction Pointer** canónico de FluentASM. No es un GPR: **no** se lee ni escribe con `.copy` (`FASM3060`); solo cambia mediante instrucciones de control de flujo (`flowCall`, `flowJump`, `flowStackJump`, `flowLoop`) o far jumps.

### Tabla de Registros

| Ipr | Legacy (hardware) | Tamaño | Notas |
|-----|-------------------|--------|-------|
| `Ipr` | `RIP` | Bit:64 | Puntero de instrucción; los símbolos se direccionan relativos a `Ipr` (PIC) |
| `Ipr.current` | `lea rax, [RIP]` | Bit:64 | Lectura del valor actual (Fase 2) |
| `(Ipr:SrfN)` | `CS:RIP` | Bit:80 | Par *far pointer* para `flowJump`/`flowCall`/`flowStackJump` far |

> **Convención de nomenclatura:** `Ipr` es el nombre canónico del registro en todo el lenguaje. `RIP` se conserva **solo** como referencia legacy/hardware: en las descripciones de **comportamiento físico** y en las citas al *Intel SDM* / *AMD APM* (es decir, de dónde viene `Ipr`). Ver `push RIP; RIP = simbolo` en §5.5 *Control Flow*. Usar `RIP` como identificador directo en código es alias legacy → `FASM3012`.

---

## 5.3 Segment Register File (Srf0–Srf5)

> **Referencia completa:** [Propuesta 0009](proposal-0009-x64-segment-registers.md)

En modo largo (x64), la segmentación está **casi deshabilitada**: las bases de CS/DS/ES/SS son ignoradas (base=0, límite=4GB). Solo FS y GS tienen bases configurables vía MSR (`IA32_FS_BASE` / `IA32_GS_BASE`), usadas para TLS.

Los selectores de segmento (16 bit) existen en 6 registros fijos (**Segment Register File**). No son GPRs: encoding dedicado (`sreg` de 3 bits), no renombrables, solo ring 0 para escritura.

### Tabla de Registros

| SrfN | Legacy | Tamaño | Notas |
|------|--------|--------|-------|
| `Srf0` | `CS` | Bit:16 | Solo lectura (selector); escritura vía far jump/call/ret; RPL = CPL |
| `Srf1` | `DS` | Bit:16 | Lectura/escritura; no usado para addressing en x64 |
| `Srf2` | `ES` | Bit:16 | Lectura/escritura; no usado para addressing en x64 |
| `Srf3` | `FS` | Bit:16 | Lectura/escritura; base configurable vía `MSR_FS_BASE` (`.base`) |
| `Srf4` | `GS` | Bit:16 | Lectura/escritura; base configurable vía `MSR_GS_BASE` / `MSR_KERNEL_GS_BASE` (`.base`) |
| `Srf5` | `SS` | Bit:16 | Lectura/escritura; cambio atómico con `mov ss, reg` + siguiente instrucción |

### Sintaxis

```fasm
// Lectura de selector (Bit:16) — Categoría D
Gpr0.copy(Srf0)     // mov eax, cs
Gpr0.copy(Srf3)     // mov eax, fs

// Escritura de selector (Bit:16) — Categoría E
Srf1.copy(Gpr0.low16)   // mov ds, ax
Srf3.copy(Gpr0.low16)   // mov fs, ax
Srf5.copy(Gpr0.low16)   // mov ss, ax

// Acceso a base MSR (Bit:64) — Solo Srf3/Srf4
Gpr0.copy(Srf3.base)    // rdfsbase rax
Gpr0.copy(Srf4.base)    // rdgsbase rax
Srf3.base.copy(Gpr0)    // wrfsbase rax
Srf4.base.copy(Gpr0)    // wrgsbase rax
```

> **Lectura** (destino GPR): Categoría D — ver §5.2 Categoría D.
> **Escritura** (destino SRF): Categoría E — ver §5.5 Categoría E.

### Diagnósticos

| Código | Situación |
|--------|-----------|
| `FASM3030` | Escritura a `Srf0` directo (usar `(Ipr:Srf0).flowJump(...)` / `(Ipr:Srf0).flowCall(...)`) |
| `FASM3031` | `Srf3.base` / `Srf4.base` usado sin `Crf4.FSGSBASE=1` |
| `FASM3032` | Selector inválido (fuera de GDT/LDT, RPL/CPL violado) |

---

## 5.4 Control Register File (Crf0–Crf4, Crf8)

> **Referencia completa:** [Propuesta 0010](proposal-0010-x64-control-registers.md)

Los registros de control configuran el estado fundamental del procesador: paginación, protección, extensiones, virtualización. Solo accesibles en **ring 0** (privilegio 0). Acceso vía `mov crN, reg` / `mov reg, crN`.

No son GPRs: encoding dedicado (`0F 20`/`0F 22`), solo ring 0, efectos globales inmediatos (TLB flush, cambio de modo).

### Tabla de Registros

| CrfN | Legacy | Tamaño | Notas |
|------|--------|--------|-------|
| `Crf0` | `CR0` | Bit:64 | Modo maestro: PE, MP, EM, TS, ET, NE, WP, AM, NW, CD, PG |
| `Crf1` | `CR1` | Bit:64 | Reservado (siempre 0) |
| `Crf2` | `CR2` | Bit:64 | PFLA (Page-Fault Linear Address) — actualizado por hardware en #PF |
| `Crf3` | `CR3` | Bit:64 | PML4 base (bits 51:12), PCID (bits 11:0 si `Crf4.PCIDE=1`) |
| `Crf4` | `CR4` | Bit:64 | Feature control: VME, PVI, TSD, DE, PSE, PAE, MCE, PGE, PCE, OSFXSR, OSXMMEXCPT, UMIP, LA57, VMXE, SMXE, FSGSBASE, PCIDE, OSXSAVE, SMEP, SMAP, PKE, CET, PKS, UINTR |
| `Crf8` | `CR8` | Bit:64 | TPR[3:0] (Task Priority Register) — solo modo x2APIC; alias de `IA32_TPR` MSR |

### Sintaxis

```fasm
// Lectura (CR → GPR) — Categoría D
Gpr0.copy(Crf0)     // mov rax, cr0
Gpr0.copy(Crf3)     // mov rax, cr3

// Escritura (GPR → CR) — Categoría E
Crf0.copy(Gpr0)     // mov cr0, rax
Crf3.copy(Gpr0)     // mov cr3, rax
Crf4.copy(Gpr0)     // mov cr4, rax
Crf8.copy(Gpr0)     // mov cr8, rax
```

> **Nota:** `Crf1` es reservado (escritura → #GP). Solo ring 0.

### Helpers de campos (Fase 2+)

```fasm
Crf0.pagingEnabled.copy(Bit:1)      // set PG bit
Crf0.protectionEnabled.copy(Bit:1)  // set PE bit
Crf4.writeProtect.copy(Bit:1)       // set WP bit
Crf4.smepEnabled.copy(Bit:1)        // set SMEP bit
Crf4.smapEnabled.copy(Bit:1)        // set SMAP bit
Crf4.pcidEnabled.copy(Bit:1)        // set PCIDE bit
Crf4.fsgsbaseEnabled.copy(Bit:1)    // set FSGSBASE bit
Crf4.osxsaveEnabled.copy(Bit:1)     // set OSXSAVE bit
Crf3.pcid.copy(Byte:0x12)           // escribir PCID (bits 11:0)
Crf3.pml4Base.copy(Gpr0)            // escribir base PML4 (bits 51:12)
```

### Diagnósticos

| Código | Situación |
|--------|-----------|
| `FASM3040` | Acceso a `Crf*` en ring 3 (CPL > 0) → #GP |
| `FASM3041` | Escritura a `Crf1` (reservado) |
| `FASM3042` | `Crf3.pcid` escrito sin `Crf4.pcidEnabled=1` |
| `FASM3043` | `Crf8` accedido sin x2APIC habilitado |
| `FASM3044` | Cambio `Crf0.PG` (paginación) sin seguir secuencia requerida |

---

## 5.5 Flags Register File (Frf)

> **Referencia completa:** [Propuesta 0012](proposal-0012-x64-rip-rflags.md)

`Frf` es el **Flags Register File**: un registro físico de 64 bits (RFLAGS) con vistas de 32/16 bits (EFLAGS/FLAGS) y acceso bit a bit a cada bandera. **No existe `rFlags` separado**: `Frf` unifica **register file físico + DSL lógico** con nombres semánticos.

### Tabla de Registros

| Frf | Legacy | Tamaño | Notas |
|-----|--------|--------|-------|
| `Frf` | `RFLAGS` | Bit:64 | Registro completo |
| `Frf.low32` | `EFLAGS` | Bit:32 | Vista 32 bits (zero-extend al leer) |
| `Frf.low16` | `FLAGS` | Bit:16 | Vista 16 bits (zero-extend al leer) |

### Sintaxis

```fasm
// Lectura (Categoría D: destino GPR)
Gpr0.copy(Frf)           // pushfq; pop rax          (RFLAGS 64b)
Gpr0.copy(Frf.low32)     // pushfd; pop eax          (EFLAGS 32b, zero-extend)
Gpr0.copy(Frf.low16)     // pushf; pop ax            (FLAGS 16b, zero-extend)

// Escritura (Categoría E: destino NO es GPR)
Frf.copy(Gpr0)           // push rax; popfq          (RFLAGS 64b)
Frf.low32.copy(Gpr0.low32) // push eax; popfd        (EFLAGS 32b)
Frf.low16.copy(Gpr0.low16) // push ax; popf          (FLAGS 16b)

// Banderas individuales — Categoría F (instrucciones dedicadas, Fase 1)
// Sintaxis: Frf.bandera(1) = set, Frf.bandera(0) = clear, Frf.bandera(not) = toggle (solo CF)
Frf.carry(1)       // stc   — CF = 1
Frf.carry(0)       // clc   — CF = 0
Frf.carry(not)     // cmc   — CF = !CF  (toggle)

Frf.direction(1)   // std   — DF = 1
Frf.direction(0)   // cld   — DF = 0

Frf.interrupt(1)   // sti   — IF = 1  (Fase 3, ring 0)
Frf.interrupt(0)   // cli   — IF = 0  (Fase 3, ring 0)

// Acceso bit a bit — Categoría D/E (Fase 2+)
// Nombres SEMÁNTICOS (unificados: Frf = físico + DSL lógico)
// Lectura bit → GPR
Gpr0.copy(Frf.carry)           // CF (Bit:0)
Gpr0.copy(Frf.parity)          // PF (Bit:2)
Gpr0.copy(Frf.adjust)          // AF (Bit:4)
Gpr0.copy(Frf.zero)            // ZF (Bit:6)
Gpr0.copy(Frf.sign)            // SF (Bit:7)
Gpr0.copy(Frf.trap)            // TF (Bit:8)
Gpr0.copy(Frf.interrupt)       // IF (Bit:9)
Gpr0.copy(Frf.direction)       // DF (Bit:10)
Gpr0.copy(Frf.overflow)        // OF (Bit:11)
Gpr0.copy(Frf.iopl)            // IOPL (bits 12:13)
Gpr0.copy(Frf.nestedTask)      // NT (Bit:14)
Gpr0.copy(Frf.resume)          // RF (Bit:16)
Gpr0.copy(Frf.virtual8086)     // VM (Bit:17)
Gpr0.copy(Frf.alignmentCheck)  // AC (Bit:18)
Gpr0.copy(Frf.virtualInterrupt)     // VIF (Bit:19)
Gpr0.copy(Frf.virtualInterruptPending) // VIP (Bit:20)
Gpr0.copy(Frf.cpuid)           // ID (Bit:21)

// Lectura negada (para condiciones / guardar valor negado)
Gpr0.copy(not Frf.carry)       // Gpr0 = !CF
Gpr0.copy(not Frf.zero)        // Gpr0 = !ZF

// Escritura bit a bit (Fase 2+)
// Sintaxis: Frf.bandera(1/0) = set/clear, Frf.bandera(not) = toggle (solo CF)
Frf.carry(1)       // stc  — CF = 1
Frf.carry(0)       // clc  — CF = 0
Frf.carry(not)     // cmc  — CF = !CF

Frf.direction(1)   // std  — DF = 1
Frf.direction(0)   // cld  — DF = 0

Frf.interrupt(1)   // sti  — IF = 1
Frf.interrupt(0)   // cli  — IF = 0

// Toggle explícito (solo CF tiene instrucción hardware)
Frf.carry(not Frf.carry)  // cmc  — CF = !CF (equivalente a Frf.carry(not))
```

### Banderas individuales (mapa bit → nombre semántico)

| Bit | Nombre Frf (semántico) | NASM (legacy) | Descripción |
|-----|------------------------|---------------|-------------|
| 0 | `Frf.carry` | `CF` | Carry Flag |
| 2 | `Frf.parity` | `PF` | Parity Flag |
| 4 | `Frf.adjust` | `AF` | Adjust Flag (BCD) |
| 6 | `Frf.zero` | `ZF` | Zero Flag |
| 7 | `Frf.sign` | `SF` | Sign Flag |
| 8 | `Frf.trap` | `TF` | Trap Flag (single-step) |
| 9 | `Frf.interrupt` | `IF` | Interrupt Enable Flag |
| 10 | `Frf.direction` | `DF` | Direction Flag (strings) |
| 11 | `Frf.overflow` | `OF` | Overflow Flag |
| 12–13 | `Frf.iopl` | `IOPL` | I/O Privilege Level |
| 14 | `Frf.nestedTask` | `NT` | Nested Task |
| 16 | `Frf.resume` | `RF` | Resume Flag |
| 17 | `Frf.virtual8086` | `VM` | Virtual-8086 Mode |
| 18 | `Frf.alignmentCheck` | `AC` | Alignment Check |
| 19 | `Frf.virtualInterrupt` | `VIF` | Virtual Interrupt Flag |
| 20 | `Frf.virtualInterruptPending` | `VIP` | Virtual Interrupt Pending |
| 21 | `Frf.cpuid` | `ID` | CPUID Instruction Available |

### Diagnósticos

| Código | Situación |
|--------|-----------|
| `FASM3060` | `Gpr0.copy(Ipr)` / `Ipr.copy(Gpr0)` — acceso directo inválido |
| `FASM3061` | `Frf.interrupt` / `Frf.trap` / `Frf.iopl` / `Frf.virtual8086` / `Frf.resume` / `Frf.alignmentCheck` / `Frf.virtualInterrupt` / `Frf.virtualInterruptPending` escrito en ring 3 → #GP (solo ring 0) |
| `FASM3062` | `Frf` / `Frf.low32` escrito con bits reservados (22–63) ≠ 0 |
| `FASM3063` | `pushfq`/`popfq` en contexto donde pila no alineada 8 bytes |
| `FASM3064` | `Frf.low32` / `Frf.low16` usado en modo largo (x64 nativo) → #UD |

---

## 5.5 ABI x64 — Convención de Llamada

> **Referencia completa:** [Propuesta 0008](proposal-0008-x64-abi.md) — ABI en debate.

### ABI por Defecto: Win64

La ABI activa se declara siempre (en `.fproj` o línea de comandos). Nunca se adivina.

### ABI Configurable

```fasm
// Global en .fproj:
.fabi win64          // default
.fabi systemv        // alternativa

// Override por rutina:
routine MiFuncion
    .abi systemv     // solo esta rutina usa System V
    parameter a, b
    // a ≡ Gpr7 (RDI), b ≡ Gpr6 (RSI)
end
```

### Parámetros Enteros/Punteros

| Posición | Win64 | System V |
|----------|-------|----------|
| 1 | `Gpr1` (RCX) | `Gpr7` (RDI) |
| 2 | `Gpr2` (RDX) | `Gpr6` (RSI) |
| 3 | `Gpr8` (R8) | `Gpr2` (RDX) |
| 4 | `Gpr9` (R9) | `Gpr1` (RCX) |
| 5 | Stack | `Gpr8` (R8) |
| 6 | Stack | `Gpr9` (R9) |
| 7+ | Stack | Stack |

**Retorno:**
| Tipo | Ambos ABIs |
|------|------------|
| Entero 64b | `Gpr0` |
| Entero 128b | `Gpr2:Gpr0` |
| Flotante | *(Fase 2)* |

### Volátiles vs No Volátiles

| Categoría | Win64 | System V |
|-----------|-------|----------|
| **Volátiles (caller-saved)** | `Gpr0`, `Gpr1`, `Gpr2`, `Gpr8`–`Gpr11` | `Gpr0`, `Gpr1`, `Gpr2`, `Gpr6`, `Gpr7`, `Gpr8`–`Gpr11` |
| **No volátiles (callee-saved)** | `Gpr3`–`Gpr7`, `Gpr12`–`Gpr15`, `Gpr4` | `Gpr3`, `Gpr5`, `Gpr12`–`Gpr15`, `Gpr4` |
| **Especiales** | `Gpr4` (RSP), `Gpr5` (RBP) | `Gpr4` (RSP), `Gpr5` (RBP) |

### Shadow Space / Home Space (Solo Win64)

**Regla:** El caller debe reservar 32 bytes (4 slots × 8 bytes) antes de `CALL`. FluentASM lo genera **automáticamente** en prólogo si la rutina tiene parámetros o hace llamadas.

```fasm
routine ConParametros
    parameter a, b
    // Prólogo generado automáticamente:
    // .stackWrite(Gpr5)        // push rbp
    // Gpr5.copy(Gpr4)          // mov rbp, rsp
    // Gpr4.sub(Bit:0x30)       // sub rsp, 48 (32 shadow + 16 locals = alineado 16)
    // ... cuerpo ...
    Gpr4.copy(Gpr5)             // mov rsp, rbp
    Gpr5.stackRead()            // pop rbp
    Ipr.flowStackJump()
end
```

### Red Zone (Solo System V) — Opt-in

**Regla:** System V permite 128 bytes debajo de RSP en leaf functions. FluentASM requiere opt-in explícito:

```fasm
routine LeafSystemV
    .abi systemv
    .redzone
    // Usar [Gpr4 - 8] ... [Gpr4 - 128] sin tocar Gpr4
    Gpr0.copy(Bit:42)
    Ipr.flowStackJump()
end
```

### Alineación de Pila

| ABI | Regla |
|-----|-------|
| **Win64** | `Gpr4` ≡ 0 (mod 16) **antes de `Ipr.flowCall`** |
| **System V** | `Gpr4` ≡ 0 (mod 16) **antes de `Ipr.flowCall`** |

El compilador asegura alineación en prólogo automático.

### Prólogo / Epílogo

| Caso | Comportamiento |
|------|----------------|
| Rutina con parámetros, locales, o llamadas | **Automático** (push rbp / mov rbp,rsp / sub rsp,N / ... / leave-equivalent / ret) |
| Rutina hoja (leaf) sin parámetros/locales/llamadas | **Ninguno** (solo `ret` vía `Ipr.flowStackJump()`) |
| Rutina `naked` | **Manual** — autor responsable de todo |

```fasm
routine ManualNaked
    naked
    // Autor debe: alinear pila, preservar no volátiles, manejar shadow, emitir ret
    Gpr0.copy(Bit:42)
    Ipr.flowStackJump()   // emite RET directo
end
```

---

## 5.4 Modos de Direccionamiento x64

| Modo | Ejemplo | Significado |
|------|---------|-------------|
| Inmediato | `Bit:42` | El valor va dentro de la instrucción |
| Registro | `Gpr0` | El valor está en el registro |
| Memoria indirecta | `[Gpr3]` | El valor está en la dirección que guarda `Gpr3` |
| Memoria con desplazamiento | `[Gpr3 - 8]` | Igual, con desplazamiento |
| Memoria con índice y escala | `[Gpr3 + Gpr1*4 + 16]` | Arreglo: base + índice × tamaño + desplazamiento |
| Relativo a RIP | `[Ipr + mensaje]` o `[Rel mensaje]` | Posición independiente (PIC) |
| Dirección de un símbolo | `mensaje` | Dirección del símbolo (se codifica como `Lea`) |
| Contenido de un símbolo | `[mensaje]` | El dato guardado en el símbolo |
| Tamaño explícito | `Byte [Gpr3]`, `QWord [Gpr3]` | Obligatorio cuando el tamaño es ambiguo |

**Reglas x64:**
1. La **escala** solo puede ser 1, 2, 4 u 8. Otro valor es `FASM4002`.
2. `Gpr4` (RSP) no puede ser índice (`FASM4003`). `Gpr12` **sí puede** ser índice con REX.X. [SDM Vol. 2A, Tabla 2-5]
3. Si el tamaño del dato no se deduce del otro operando, hay que declararlo (`FASM3005`).
4. `.copy(Gpr0 to mensaje)` carga la **dirección** (equivalente a `.lea(Gpr0 to mensaje)`), mientras que `.copy(Gpr0 to [mensaje])` carga el **contenido**.

---

## 5.5 Catálogo de Instrucciones x64 (Fase 1)

> **Sintaxis unificada:** [Propuesta 0006](proposal-0006-instruction-syntax.md)

### Categorías de Sintaxis

| Categoría | Destino | Sintaxis | Prefijo |
|-----------|---------|----------|---------|
| **A** | 2+ GPRs | `Gpr.op<salidas>(entradas)` | `Gpr.` |
| **B** | 1 GPR | `GprN.op(entrada)` | `GprN.` |
| **C** | Flags | `GprN.cmp/test/testBit(...)` | `GprN.` |
| **D** | 1 GPR (fuente variada) | `GprN.copy/stackRead/...` | `GprN.` |
| **D** | 1 SRF (selector) | `SrfN.copy` (solo lectura) | `SrfN.` |
| **D** | 1 CRF (control) | `CrfN.copy` (solo lectura) | `CrfN.` |
| **E** | Mem/Stack/Ipr/Flags/SRF/CRF | Sintaxis propia | `.copy`, `.flow*`, `.stack*`, `SrfN.copy`, `CrfN.copy` |
| **F** | Sistema | `.nop()`, `.setCarry()`, etc. | `.` |

---

### Categoría A: GPR → GPR (Múltiples Salidas)

| Operación | Sintaxis | Salidas | Codificación x64 | Notas |
|-----------|----------|---------|------------------|-------|
| `mult` (unsigned) | `Gpr.mult<2,0>(Gpr3)` | `<2,0>` = Gpr2:Gpr0 | `MUL r64` (`F7 /4`) | `Gpr2:Gpr0 = Gpr0 × Gpr3` |
| `imult` (signed) | `Gpr.imult<2,0>(Gpr3)` | `<2,0>` = Gpr2:Gpr0 | `IMUL r64` (`F7 /5`) | `Gpr2:Gpr0 = Gpr0 × Gpr3` (signed) |
| `div` (unsigned) | `Gpr.div<0,2>(Gpr3)` | `<0,2>` = Gpr0, Gpr2 | `DIV r64` (`F7 /6`) | `Gpr0=cociente, Gpr2=resto` |
| `idiv` (signed) | `Gpr.idiv<0,2>(Gpr3)` | `<0,2>` = Gpr0, Gpr2 | `IDIV r64` (`F7 /7`) | `Gpr0=cociente, Gpr2=resto` (requiere `cqo`) |
| `signExtend` | `Gpr0.signExtend<2,0>()` | `<2,0>` = Gpr2:Gpr0 | `CQO` | Extiende signo Gpr0 → Gpr2:Gpr0 |

> **Encoding:** `GprN` = índice 0–15 → campo `reg` + `REX.R` si ≥ 8.

---

### Categoría B: GPR → GPR (Salida Única)

#### ALU Aritmética/Lógica

| Operación | Sintaxis | Codificación x64 | Flags afectados |
|-----------|----------|------------------|-----------------|
| `add` | `Gpr0.add(Gpr1)` | `ADD r/m64, r64` (`01 /r`) | CF, OF, SF, ZF, AF, PF |
| `sub` | `Gpr0.sub(Gpr1)` | `SUB r/m64, r64` (`29 /r`) | CF, OF, SF, ZF, AF, PF |
| `and` | `Gpr0.and(Gpr1)` | `AND r/m64, r64` (`21 /r`) | CF=0, OF=0, SF, ZF, PF |
| `or` | `Gpr0.or(Gpr1)` | `OR r/m64, r64` (`09 /r`) | CF=0, OF=0, SF, ZF, PF |
| `xor` | `Gpr0.xor(Gpr1)` | `XOR r/m64, r64` (`31 /r`) | CF=0, OF=0, SF, ZF, PF |

> **Encoding:** `Gpr0.add(Gpr1)` → `ADD Gpr0, Gpr1` → `48 01 C8` (si Gpr0=RAX, Gpr1=RCX).

#### Unarias

| Operación | Sintaxis | Codificación | Flags |
|-----------|----------|--------------|-------|
| `not` | `Gpr0.not()` | `NOT r/m64` (`F7 /2`) | Ninguno |
| `neg` | `Gpr0.neg()` | `NEG r/m64` (`F7 /3`) | CF, OF, SF, ZF, AF, PF |

#### Shifts / Rotates

| Operación | Sintaxis | Codificación | Notas |
|-----------|----------|--------------|-------|
| `shl` | `Gpr0.shl(Bit:3)` | `SHL r/m64, imm8` (`C1 /4 ib`) | Cuenta = inmediato (0–63) |
| `shr` | `Gpr0.shr(Bit:3)` | `SHR r/m64, imm8` (`C1 /5 ib`) | Lógico (ceros) |
| `rol` | `Gpr0.rol(Bit:3)` | `ROL r/m64, imm8` (`C0 /0 ib`) | Rotación izquierda |
| `ror` | `Gpr0.ror(Bit:3)` | `ROR r/m64, imm8` (`C0 /1 ib`) | Rotación derecha |

> **Nota:** Shift por registro (`Gpr1.low8`) usa encoding `D3 /4` (CL). Máscara: 6 bits en 64b (0–63), 5 bits en menores. [SDM Vol. 2B]

---

### Categoría C: Solo Flags

| Operación | Sintaxis | Codificación | Flags actualizados |
|-----------|----------|--------------|-------------------|
| `cmp` | `Gpr0.cmp(Gpr1)` | `CMP r/m64, r64` (`39 /r`) | CF, OF, SF, ZF, AF, PF |
| `test` | `Gpr0.test(Gpr1)` | `TEST r/m64, r64` (`85 /r`) | CF=0, OF=0, SF, ZF, PF |
| `testBit` | `Gpr0.testBit<0>(Bit:3)` | `BT r/m64, imm8` (`0F BA /4 ib`) | CF = bit probado |

---

### Categoría D: Registro Destino, Fuente Variada

| Operación | Sintaxis | Codificación | Qué hace |
|-----------|----------|--------------|----------|
| `copy` (reg→reg) | `Gpr0.copy(Gpr1)` | `MOV r64, r64` (`89 /r`) | `Gpr0 = Gpr1` |
| `copy` (imm→reg) | `Gpr0.copy(Bit:42)` | `MOV r64, imm32/64` (`B8+rb` / `48 B8`) | `Gpr0 = 42` |
| `copy` (vista) | `Gpr0.copy(Gpr1.low32)` | `MOV r32, r32` (`89 /r`) | Zero-extend a 64b |
| `stackRead` | `Gpr0.stackRead()` | `POP r64` (`58+rw`) | `Gpr0 = [Gpr4]; Gpr4 += 8` |
| `stackPointerGet` | `Gpr4.stackPointerGet()` | `MOV r64, RSP` (`48 89 E0`) | `Gpr0 = Gpr4` |
| `stackPointerSet` | `Gpr4.stackPointerSet(Gpr0)` | `MOV RSP, r64` (`48 89 E0`) | `Gpr4 = Gpr0` |

---

### Categoría E: Destino NO es GPR

#### Memoria (Fase 2 — listado para referencia)

| Operación | Sintaxis | Codificación | Nota |
|-----------|----------|--------------|------|
| `copy` reg→mem | `.copy(Gpr0 to [Gpr4+8])` | `MOV [r/m64], r64` (`89 /r`) | Fase 2 |
| `copy` mem→reg | `Gpr0.copy([Gpr4+8])` | `MOV r64, [r/m64]` (`8B /r`) | Fase 2 |
| `lea` | `Gpr0.lea(simbolo)` | `LEA r64, [rel32]` (`48 8D 05 ...`) | Fase 2 |

#### Segment Register File (SRF) — Escritura de selector (16 bit)

| Operación | Sintaxis | Codificación | Qué hace |
|-----------|----------|--------------|----------|
| `copy` (GPR→SRF) | `Srf1.copy(Gpr0.low16)` | `MOV DS, AX` (`8E /r`) | `DS = AX` |
| `copy` (GPR→SRF) | `Srf2.copy(Gpr0.low16)` | `MOV ES, AX` | `ES = AX` |
| `copy` (GPR→SRF) | `Srf3.copy(Gpr0.low16)` | `MOV FS, AX` | `FS = AX` |
| `copy` (GPR→SRF) | `Srf4.copy(Gpr0.low16)` | `MOV GS, AX` | `GS = AX` |
| `copy` (GPR→SRF) | `Srf5.copy(Gpr0.low16)` | `MOV SS, AX` | `SS = AX` |

> **Nota:** `Srf0` (CS) no se escribe con `copy`; usar far jumps abajo.

#### Segment Register File (SRF) — Escritura de base MSR (64 bit)

| Operación | Sintaxis | Codificación | Qué hace |
|-----------|----------|--------------|----------|
| `copy` (GPR→SRF.base) | `Srf3.base.copy(Gpr0)` | `WRFSBASE RAX` | `FS.base = RAX` |
| `copy` (GPR→SRF.base) | `Srf4.base.copy(Gpr0)` | `WRGSBASE RAX` | `GS.base = RAX` |

> **Nota:** Requiere `Crf4.FSGSBASE=1`. Solo `Srf3` (FS) y `Srf4` (GS).

#### Control Register File (CRF) — Escritura (64 bit)

| Operación | Sintaxis | Codificación | Qué hace |
|-----------|----------|--------------|----------|
| `copy` (GPR→CRF) | `Crf0.copy(Gpr0)` | `MOV CR0, RAX` (`0F 22 /0`) | `CR0 = RAX` |
| `copy` (GPR→CRF) | `Crf2.copy(Gpr0)` | `MOV CR2, RAX` | `CR2 = RAX` |
| `copy` (GPR→CRF) | `Crf3.copy(Gpr0)` | `MOV CR3, RAX` | `CR3 = RAX` |
| `copy` (GPR→CRF) | `Crf4.copy(Gpr0)` | `MOV CR4, RAX` | `CR4 = RAX` |
| `copy` (GPR→CRF) | `Crf8.copy(Gpr0)` | `MOV CR8, RAX` | `CR8 = RAX` |

> **Nota:** `Crf1` es reservado (escritura → #GP). Solo ring 0.

#### Stack

| Operación | Sintaxis | Codificación | Qué hace |
|-----------|----------|--------------|----------|
| `stackWrite` | `.stackWrite(Gpr0)` | `PUSH r64` (`50+rw`) | `Gpr4 -= 8; [Gpr4] = Gpr0` |
| `stackRead` | `Gpr0.stackRead()` | `POP r64` (`58+rw`) | `Gpr0 = [Gpr4]; Gpr4 += 8` |
| `stackPointerGet` | `Gpr4.stackPointerGet()` | `MOV r64, RSP` | `Gpr0 = Gpr4` |
| `stackPointerSet` | `Gpr4.stackPointerSet(Gpr0)` | `MOV RSP, r64` | `Gpr4 = Gpr0` |

#### Control Flow

| Operación | Sintaxis | Codificación | Qué hace |
|-----------|----------|--------------|----------|
| `flowCall` (rel) | `Ipr.flowCall(simbolo)` | `CALL rel32` (`E8 rel32`) | `push RIP; RIP = simbolo` |
| `flowCall` (ind) | `Ipr.flowCall(Gpr0)` | `CALL r/m64` (`FF /2`) | `push RIP; RIP = Gpr0` |
| `flowJump` (rel) | `Ipr.flowJump(@label)` | `JMP rel32/rel8` (`E9`/`EB`) | `RIP = label` |
| `flowJump` (ind) | `Ipr.flowJump(Gpr0)` | `JMP r/m64` (`FF /4`) | `RIP = Gpr0` |
| `flowStackJump` | `Ipr.flowStackJump()` | `RET` (`C3`) | `RIP = [Gpr4]; Gpr4 += 8` |
| `flowLoop` | `Ipr.flowLoop(@label)` | `LOOP rel8` (`E2 cb`) | `Gpr1--; if Gpr1≠0: RIP = label` |
| **Far jump** | `(Ipr:Srf0).flowJump(Gpr0)` | `JMP FAR` (`EA`) | `RIP=Gpr0; Srf0=selector` |
| **Far call** | `(Ipr:Srf0).flowCall(Gpr0)` | `CALL FAR` (`9A`) | `push RIP,CS; RIP=Gpr0; Srf0=selector` |
| **Far ret** | `(Ipr:Srf0).flowStackJump()` | `RETF` (`CB`) | `pop RIP, Srf0` |

> **Nota:** `flowLoop` **no modifica flags** (diferente de `dec`+`jnz`). [SDM Vol. 2B]

Sintaxis: `Ipr.flowJump(destino if Frf.condicion)`

| DSL | NASM | Lee en `Frf` |
|-----|------|-----------------|
| `Ipr.flowJump(@fin if Frf.zero)` | `je` / `jz` | `Frf.zero = 1` (Bit:6) |
| `Ipr.flowJump(@fin if not Frf.zero)` | `jne` / `jnz` | `Frf.zero = 0` |
| `Ipr.flowJump(@fin if Frf.carry)` | `jc` / `jb` | `Frf.carry = 1` (Bit:0) |
| `Ipr.flowJump(@fin if not Frf.carry)` | `jnc` / `jae` | `Frf.carry = 0` |
| `Ipr.flowJump(@fin if Frf.sign)` | `js` | `Frf.sign = 1` (Bit:7) |
| `Ipr.flowJump(@fin if not Frf.sign)` | `jns` | `Frf.sign = 0` |
| `Ipr.flowJump(@fin if Frf.overflow)` | `jo` | `Frf.overflow = 1` (Bit:11) |
| `Ipr.flowJump(@fin if not Frf.overflow)` | `jno` | `Frf.overflow = 0` |
| `Ipr.flowJump(@fin if Frf.parity)` | `jp` / `jpe` | `Frf.parity = 1` (Bit:2) |
| `Ipr.flowJump(@fin if not Frf.parity)` | `jnp` / `jpo` | `Frf.parity = 0` |
| `Ipr.flowJump(@fin if Frf.carry or Frf.zero)` | `jbe` | `Frf.carry=1` o `Frf.zero=1` |
| `Ipr.flowJump(@fin if not Frf.carry and not Frf.zero)` | `ja` | `Frf.carry=0` y `Frf.zero=0` |
| `Ipr.flowJump(@fin if Frf.sign unequal Frf.overflow)` | `jl` | `Frf.sign ≠ Frf.overflow` |
| `Ipr.flowJump(@fin if Frf.sign equal Frf.overflow)` | `jge` | `Frf.sign = Frf.overflow` |
| `Ipr.flowJump(@fin if Frf.zero or Frf.sign unequal Frf.overflow)` | `jle` | `Frf.zero=1` o `Frf.sign≠Frf.overflow` |
| `Ipr.flowJump(@fin if not Frf.zero and Frf.sign equal Frf.overflow)` | `jg` | `Frf.zero=0` y `Frf.sign=Frf.overflow` |

---

### Categoría F: Sistema y Relleno (Bare Metal Fase 1)

| Instrucción | Sintaxis | Codificación | Notas |
|-------------|----------|--------------|-------|
| `nop` | `.nop()` | `90` | No hace nada |
| `setCarry` | `Frf.carry(1)` | `f9` | `CF = 1` |
| `clearCarry` | `Frf.carry(0)` | `f8` | `CF = 0` |
| `toggleCarry` | `Frf.carry(not)` | `f5` | `CF = !CF` |
| `setDirection` | `Frf.direction(1)` | `fd` | `DF = 1` (cadenas hacia atrás) |
| `clearDirection` | `Frf.direction(0)` | `fc` | `DF = 0` (cadenas hacia adelante) |
| `setInterrupt` | `Frf.interrupt(1)` | `fb` | `IF = 1` (Fase 3, ring 0) |
| `clearInterrupt` | `Frf.interrupt(0)` | `fa` | `IF = 0` (Fase 3, ring 0) |

---

### Tabla de Codificación Rápida (Resumen)

| Sintaxis FluentASM | Opcode x64 | Bytes típicos |
|--------------------|------------|---------------|
| `Gpr0.add(Gpr1)` | `ADD Gpr0, Gpr1` | `48 01 C8` (3) |
| `Gpr0.sub(Gpr1)` | `SUB Gpr0, Gpr1` | `48 29 C8` (3) |
| `Gpr0.and(Gpr1)` | `AND Gpr0, Gpr1` | `48 21 C8` (3) |
| `Gpr0.or(Gpr1)` | `OR Gpr0, Gpr1` | `48 09 C8` (3) |
| `Gpr0.xor(Gpr1)` | `XOR Gpr0, Gpr1` | `48 31 C8` (3) |
| `Gpr0.not()` | `NOT Gpr0` | `48 F7 D0` (3) |
| `Gpr0.neg()` | `NEG Gpr0` | `48 F7 D8` (3) |
| `Gpr0.shl(Bit:3)` | `SHL Gpr0, 3` | `48 C1 E0 03` (4) |
| `Gpr0.shr(Bit:3)` | `SHR Gpr0, 3` | `48 C1 E8 03` (4) |
| `Gpr0.rol(Bit:3)` | `ROL Gpr0, 3` | `48 C0 C0 03` (4) |
| `Gpr0.ror(Bit:3)` | `ROR Gpr0, 3` | `48 C0 C8 03` (4) |
| `Gpr.mult<2,0>(Gpr3)` | `MUL Gpr3` | `48 F7 E3` (3) |
| `Gpr.imult<2,0>(Gpr3)` | `IMUL Gpr3` | `48 F7 EB` (3) |
| `Gpr.div<0,2>(Gpr3)` | `DIV Gpr3` | `48 F7 F3` (3) |
| `Gpr0.signExtend<2,0>()` | `CQO` | `48 99` (2) |
| `Gpr0.cmp(Gpr1)` | `CMP Gpr0, Gpr1` | `48 39 C8` (3) |
| `Gpr0.test(Gpr1)` | `TEST Gpr0, Gpr1` | `48 85 C8` (3) |
| `Gpr0.copy(Gpr1)` | `MOV Gpr0, Gpr1` | `48 89 C8` (3) |
| `Gpr0.copy(Bit:42)` | `MOV Gpr0, 42` | `48 B8 2A 00 00 00...` (10) |
| `Gpr0.copy(Gpr1.low32)` | `MOV EAX, ECX` | `89 C8` (2) |
| `Gpr0.stackRead()` | `POP RAX` | `58` (1) |
| `.stackWrite(Gpr0)` | `PUSH RAX` | `50` (1) |
| `Ipr.flowCall(sym)` | `CALL rel32` | `E8 xx xx xx xx` (5) |
| `Ipr.flowCall(Gpr0)` | `CALL RAX` | `FF D0` (2) |
| `Ipr.flowJump(@lbl)` | `JMP rel32` | `E9 xx xx xx xx` (5) |
| `Ipr.flowJump(Gpr0)` | `JMP RAX` | `FF E0` (2) |
| `Ipr.flowStackJump()` | `RET` | `C3` (1) |
| `Ipr.flowLoop(@lbl)` | `LOOP rel8` | `E2 xx` (2) |
| `(Ipr:Srf0).flowJump(Gpr0)` | `JMP FAR` | `EA` (10) |
| `(Ipr:Srf0).flowCall(Gpr0)` | `CALL FAR` | `9A` (10) |
| `(Ipr:Srf0).flowStackJump()` | `RETF` | `CB` (2) |
| `Srf1.copy(Gpr0.low16)` | `MOV DS, AX` | `8E D8` (2) |
| `Srf3.base.copy(Gpr0)` | `WRFSBASE RAX` | `0F AE /0` (3) |
| `Srf4.base.copy(Gpr0)` | `WRGSBASE RAX` | `0F AE /1` (3) |
| `Crf0.copy(Gpr0)` | `MOV CR0, RAX` | `0F 22 C0` (3) |
| `Crf3.copy(Gpr0)` | `MOV CR3, RAX` | `0F 22 D8` (3) |
| `Crf4.copy(Gpr0)` | `MOV CR4, RAX` | `0F 22 E0` (3) |
| `Crf8.copy(Gpr0)` | `MOV CR8, RAX` | `0F 22 F0` (3) |
| `.nop()` | `NOP` | `90` (1) |

---

## 5.6 Reglas de Codificación x64

1. **Forma más corta y determinista.** Para los mismos operandos, siempre la misma secuencia de bytes, la más corta posible.
2. **Inmediatos con signo.** Si el inmediato cabe en 8 bits con signo y existe esa forma, se usa: `Gpr0.copy(Bit:5)` son 4 bytes, `Gpr0.copy(Bit:1000)` son 6.
3. **`copy` con inmediato en 64 bits**: si el valor cabe en 32 bits sin signo → `mov r32, imm32` (`b8`); si cabe con signo en 32 bits → `mov r/m64, imm32` (`48 c7 /0`); si no → `mov r64, imm64` (`48 b8`).
4. **Desplazamientos**: `disp8` cuando cabe con signo, `disp32` en caso contrario.
5. **Direccionamiento relativo a Ipr** por defecto para todo símbolo (PIC). Nunca absoluto en Fase 1.
6. **Prefijo REX** cuando el tamaño es 64 bits o el registro es `Gpr8`–`Gpr15`, más `Spl`/`Bpl`/`Sil`/`Dil`.
7. **Saltos**: forma corta (`rel8`) y promoción automática a cercana (`rel32`) cuando el destino no cabe.
8. **Tamaño de la instrucción** se conoce antes de emitir y queda registrado.

---

## 5.7 Semántica de Ejecución x64 (Fase 1)

| Aspecto | Regla |
|---------|-------|
| Formato | Bytes puros, **sin cabecera** |
| Extensión | `.x64` |
| Punto de entrada | El símbolo de `Entry`, o el primero de `.execute` |
| Argumentos | Ninguno |
| Resultado | Entero en `Gpr0` |
| Cargador | Arrancador de FluentASM: reserva memoria ejecutable, copia bytes, salta |
| Independencia de posición | **Obligatoria**: misma imagen funciona en cualquier dirección |
| Llamadas externas | **Prohibidas** (`FASM5001`) |

---

## 5.8 Ejemplos Completos Fase 1

### 5.8.1 Hola Mundo (Retorno 42)

```fasm
program HolaMundo
    section .execute
    entry main

    routine main
        Gpr0.copy(Bit:42)
        Ipr.flowStackJump()
    end
end
```

### 5.8.2 Suma dos Números (Parámetros Win64)

```fasm
program Suma
    section .execute
    entry main

    routine Sumar
        parameter a, b          // a ≡ Gpr1, b ≡ Gpr2
        Gpr0.copy(a)
        Gpr0.add(b)
        Ipr.flowStackJump()
    end

    routine main
        Gpr1.copy(Bit:10)       // arg1 → Gpr1
        Gpr2.copy(Bit:32)       // arg2 → Gpr2
        Ipr.flowCall(Sumar)
        Ipr.flowStackJump()
    end
end
```

### 5.8.3 Multiplicación 128b

```fasm
routine Mul128
    parameter x, y              // x ≡ Gpr1, y ≡ Gpr2
    Gpr0.copy(x)
    Gpr.mult<2,0>(y)            // Gpr2:Gpr0 = x * y
    Ipr.flowStackJump()            // retorna low64 en Gpr0, high64 en Gpr2
end
```

### 5.8.4 División 128b / 64b

```fasm
routine Div128
    parameter hi, lo, divisor   // hi≡Gpr1, lo≡Gpr2, divisor≡Gpr8
    Gpr2.copy(hi)               // high64 del dividendo
    Gpr0.copy(lo)               // low64 del dividendo
    Gpr.div<0,2>(divisor)       // Gpr0=cociente, Gpr2=resto
    Ipr.flowStackJump()
end
```

### 5.8.5 División Firmada 64b

```fasm
routine IDiv64
    parameter n, d              // n≡Gpr1, d≡Gpr2
    Gpr0.copy(n)
    Gpr0.signExtend<2,0>()      // CQO: Gpr2:Gpr0 = signExtend(Gpr0)
    Gpr.idiv<0,2>(d)            // Gpr0=cociente, Gpr2=resto
    Ipr.flowStackJump()
end
```

### 5.8.6 Loop Hardware

```fasm
routine SumarN
    parameter n                 // n ≡ Gpr1
    Gpr0.copy(Bit:0)            // acumulador = 0
    @loop:
    Gpr0.add(Gpr1)              // suma n (se destruye n)
    Ipr.flowLoop(@loop)            // dec Gpr1; jnz @loop
    Ipr.flowStackJump()
end
```

### 5.8.7 Stack Frame Automático (Con Shadow Space)

```fasm
routine ConShadowSpace
    parameter a, b
    // Prólogo generado automáticamente:
    // .stackWrite(Gpr5)        // push rbp
    // Gpr5.copy(Gpr4)          // mov rbp, rsp
    // Gpr4.sub(Bit:0x30)       // sub rsp, 48 (32 shadow + 16 locals)
    Gpr0.copy(a)
    Gpr0.add(b)
    // Epílogo generado automáticamente:
    // Gpr4.copy(Gpr5)          // mov rsp, rbp
    // Gpr5.stackRead()         // pop rbp
    Ipr.flowStackJump()
end
```

### 5.8.8 Syscall Linux (System V ABI)

```fasm
routine SysWrite
    .abi systemv
    parameter fd, buf, count
    Gpr0.copy(Bit:1)            // sys_write = 1
    Gpr7.copy(fd)              // arg1 = fd (RDI)
    Gpr6.copy(buf)             // arg2 = buffer (RSI)
    Gpr2.copy(count)           // arg3 = count (RDX)
    .syscall()                 // Fase 3 — syscall instruction
    Ipr.flowStackJump()
end
```

---

## 5.9 Diagnósticos x64 (Fase 1)

| Código | Situación | Mensaje |
|--------|-----------|---------|
| `FASM3002` | `high8` + REX conflict | "`high8` no combinable con registros de byte que exigen prefijo REX (`SPL`/`BPL`/`SIL`/`DIL`)" |
| `FASM3005` | Tamaño ambiguo | "Tamaño de dato ambiguo; declare `Byte`/`Word`/`DWord`/`QWord` explícito" |
| `FASM3012` | Alias legacy usado | "`Rax` es alias legacy; use `Gpr0`" |
| `FASM3013` | Índice GPR fuera de rango | "`Gpr16` no existe; índices válidos: 0–15" |
| `FASM3014` | Vista inválida | "Vista `.low64` no existe; use `GprN` para 64 bits, o `.low32`/`.low16`/`.low8`/`.high8`" |
| `FASM3015` | `high8` en registro sin soporte | "`high8` no disponible en `Gpr4`–`Gpr15`; use `.low8` (`SPL`/`BPL`/`SIL`/`DIL`) o `Gpr0`–`Gpr3.high8`" |
| `FASM3016` | `GprN.op` con salidas múltiples | "Salidas múltiples requieren `Gpr.op<salidas>`; use `Gpr.mult<2,0>` en lugar de `Gpr0.mult`" |
| `FASM3017` | `Gpr.op<...>` con 1 sola salida | "Advertencia: 1 sola salida, use `GprN.op` en lugar de `Gpr.op<0>`" |
| `FASM3020` | `Bit` en contexto memoria | "`Bit:8` no válido en dirección de memoria; use `Byte:8`" |
| `FASM3021` | `Byte` en contexto registro/ALU | "`Byte:64` no válido en ancho de registro; use `Bit:64`" |
| `FASM3022` | Múltiplo no potencia de 2 | "`KiloByte 3` no válido; múltiplos deben ser potencias de 2" |
| `FASM3023` | Tipo desconocido | "`Kib` no reconocido; use `KiloBit` o `KiloByte`" |
| `FASM4002` | Escala no válida | "La escala debe ser 1, 2, 4 u 8, no 3" |
| `FASM4003` | `Gpr4` como índice SIB | "`Gpr4` (RSP) no puede usarse como índice en direccionamiento SIB" |
| `FASM4010` | Condición salto inválida | "Condición `Frf.foo` no válida. Use una de las 16 formas." |
| `FASM4011` | `not` mal usado | "`not` solo precede bandera simple (`Frf.zero`, etc.)." |
| `FASM4012` | Bandera indefinida | "Bandera `Frf.zero` indefinida tras `Gpr.mult<2,0>(Gpr0)`." |
| `FASM4013` | Operador no soportado | "Operador `xor` no válido. Use `equal`, `unequal`, `or`, `and`." |

> **Nota:** `FASM3012` cubre alias legacy de registros Y de instrucciones (`Rax` → `Gpr0`, `RIP` → `Ipr`, `.mul` → `Gpr.mult<2,0>`, `.ret()` → `Ipr.flowStackJump()`, etc.).

---

## 5.10 Registros x87 FPU (Legacy — Fase 2+)

> **Referencia completa:** [Propuesta 0013](proposal-0013-x64-x87-fpu.md)

### Stacks en x64 — Taxonomía

| Stack Canónico | Alias | Puntero | Descripción |
|----------------|-------|---------|-------------|
| **MainStack** | CallStack, RspStack | `RSP` | Stack principal: CALL/RET, locales, args spill |
| **FpuStack** | X87Stack, StStack | `TOP` (FSW bits 13:11) | Stack FPU x87: 8 registros 80-bit, LIFO, `ST(0)`=top |
| **ShadowStack** | CetStack | `SSP` (MSR) | CET: copia de direcciones de retorno |
| **InterruptStacks** | IST[1..7] | `IST[i]` (TSS) | Stacks aislados por vector de interrupción |
| **PrivilegeStacks** | Kernel/User | `RSP0..3` (TSS) | Stacks por nivel de privilegio (CPL 0/1/2/3) |

> **Nota:** `FpuStack` (x87) es distinto de `MainStack` (RSP). `FpuStack` usa organización LIFO con puntero `TOP` (3 bits en FSW); `MainStack` usa `RSP` y crece hacia abajo. `MM0`–`MM7` son alias de los 64 bits bajos de `ST(0)`–`ST(7)`.

### Tabla de Registros

| FluentASM | Legacy | Tamaño | Notas |
|-----------|--------|--------|-------|
| `Fpu.x87.ST(0)`–`ST(7)` | `ST(0)`–`ST(7)` | Bit:80 | Pila FPU; `ST(0)` = top |
| `Fpu.x87.MM(0)`–`MM(7)` | `MM0`–`MM7` | Bit:64 | Alias enteros (MMX); **deprecated** |
| `Fpu.x87.ControlWord` | `FCW` | Bit:16 | Control de precisión, redondeo, máscaras de excepción |
| `Fpu.x87.StatusWord` | `FSW` | Bit:16 | Bits C3,C2,C0 (condición), TOP, ES, SF, PE, UE, OE, ZE, DE, IE |
| `Fpu.x87.TagWord` | `FTW` | Bit:16 | Tags: 00=válido, 01=zero, 10=special, 11=empty |

### Sintaxis

```fasm
// Lectura/escritura de pila
Fpu.x87.ST(0).copy(Fpu.x87.ST(1))        // fxch st(1)
Fpu.x87.ST(0).copy(Byte:10 [Gpr0])     // fld tbyte [rax]  (Float80 ≡ Byte:10)
Fpu.x87.ST(0).copy(Byte:8 [Gpr0])      // fld qword [rax]  (Float64 ≡ Byte:8)
Fpu.x87.ST(0).copy(Byte:4 [Gpr0])      // fld dword [rax] (Float32 ≡ Byte:4)
Fpu.x87.ST(0).copy(Byte:8 [Gpr0])      // fild qword [rax] (Int64 ≡ Byte:8)
Fpu.x87.ST(0).copy(Byte:4 [Gpr0])      // fild dword [rax] (Int32 ≡ Byte:4)
Fpu.x87.ST(0).copy(Byte:2 [Gpr0])      // fild word [rax]  (Int16 ≡ Byte:2)

Byte:10 [Gpr0].copy(Fpu.x87.ST(0))     // fstp tbyte [rax] (Float80 ≡ Byte:10)
Byte:8 [Gpr0].copy(Fpu.x87.ST(0))      // fstp qword [rax] (Float64 ≡ Byte:8)
Byte:4 [Gpr0].copy(Fpu.x87.ST(0))      // fstp dword [rax] (Float32 ≡ Byte:4)
Byte:8 [Gpr0].copy(Fpu.x87.ST(0))      // fistp qword [rax] (Int64 ≡ Byte:8)
Byte:4 [Gpr0].copy(Fpu.x87.ST(0))      // fistp dword [rax] (Int32 ≡ Byte:4)
Byte:2 [Gpr0].copy(Fpu.x87.ST(0))      // fistp word [rax]  (Int16 ≡ Byte:2)

// Aritmética
Fpu.x87.ST(0).add(Fpu.x87.ST(1))         // fadd st(1), st(0)
Fpu.x87.ST(0).add(Byte:8 [Gpr0])       // fadd qword [rax] (Float64 ≡ Byte:8)
Fpu.x87.ST(0).sqrt()                     // fsqrt

// Comparación y flags
Fpu.x87.ST(0).cmp(Fpu.x87.ST(1))         // fcom st(1)
Fpu.x87.ST(0).cmp(Byte:8 [Gpr0])       // fcom qword [rax] (Float64 ≡ Byte:8)
Fpu.x87.status.copy(Frf)                 // fstsw ax; sahf → Frf

// Control de pila
Fpu.x87.free()                           // ffree st(0)
Fpu.x87.incStack()                       // fincstp
Fpu.x87.decStack()                       // fdecstp

// Estado
Gpr0.copy(Fpu.x87.ControlWord)
Fpu.x87.ControlWord.copy(Gpr0.low16)
Gpr0.copy(Fpu.x87.StatusWord)
Gpr0.copy(Fpu.x87.TagWord)
```

### Diagnósticos

| Código | Situación |
|--------|-----------|
| `FASM3070` | Instrucción x87 sin `CR0.EM=0` y `CR0.MP=1` |
| `FASM3071` | Stack overflow x87 |
| `FASM3072` | Stack underflow x87 |
| `FASM3073` | `Fpu.x87.MM*` usado (MMX deprecated) |
| `FASM3074` | `Fpu.x87.ST(i)` índice fuera de rango |
| `FASM3075` | Excepción FPU pendiente (div/0, sqrt neg, overflow) |

---

## 5.11 Registros Vectoriales SIMD (SSE/AVX/AVX-512 — Fase 2+)

> **Referencia completa:** [Propuesta 0014](proposal-0014-x64-simd-registers.md)

Registros vectoriales planos (flat register file): 128b (SSE), 256b (AVX), 512b (AVX-512). Herencia física: `ZMM` ⊃ `YMM` ⊃ `XMM`. 16 registros base + 16 extendidos (AVX-512). 8 máscaras `K0`–`K7`.

### Tabla de Registros

| FluentASM | Legacy | Ancho | Cantidad | Notas |
|-----------|--------|-------|----------|-------|
| `Fpu.simd.Vector128.Xmm0`–`Xmm15` | `XMM0`–`XMM15` | Bit:128 | 16 | SSE/SSE2/SSE3/SSSE3/SSE4 |
| `Fpu.simd.Vector256.Ymm0`–`Ymm15` | `YMM0`–`YMM15` | Bit:256 | 16 | AVX/AVX2 |
| `Fpu.simd.Vector512.Zmm0`–`Zmm15` | `ZMM0`–`ZMM15` | Bit:512 | 16 | AVX-512F |
| `Fpu.simd.Vector512.Zmm16`–`Zmm31` | `ZMM16`–`ZMM31` | Bit:512 | 16 | AVX-512VL/DQ/BW/VL |
| `Fpu.simd.Mask.K0`–`K7` | `K0`–`K7` | Bit {8/16/32/64} | 8 | AVX-512 predication |

### Vistas de sub-registros (herencia física)

```
ZMM0 (Bit:512)
  └─ YMM0 (Bit:256, bits 255:0)
       └─ XMM0 (Bit:128, bits 127:0)
```

- Escribir `XMM0` **zero-extiende** a `YMM0`/`ZMM0` (bits 128–511 = 0).
- Escribir `YMM0` zero-extiende a `ZMM0` (bits 256–511 = 0).
- `VZEROUPPER` / `VZEROALL` limpian bits altos.

### Sintaxis

```fasm
// Registros vectoriales
Fpu.simd.Vector128.Xmm0.copy(Fpu.simd.Vector128.Xmm1)      // vmovdqa xmm0, xmm1 (Bit:128)
Fpu.simd.Vector256.Ymm0.copy(Fpu.simd.Vector256.Ymm1)      // vmovdqa ymm0, ymm1 (Bit:256)
Fpu.simd.Vector512.Zmm0.copy(Fpu.simd.Vector512.Zmm1)      // vmovdqa zmm0, zmm1 (Bit:512)

// Registros extendidos AVX-512 (16–31)
Fpu.simd.Vector512.Zmm16.copy(Fpu.simd.Vector512.Zmm17)    // vmovdqa zmm16, zmm17 (Bit:512)

// Máscaras AVX-512 (K0–K7)
Fpu.simd.Mask.K0.copy(Fpu.simd.Mask.K1)                    // kmovw k0, k1 (Bit:16 default)
Fpu.simd.Mask.K0.copy(Byte:1 0xFF)                       // kmovb k0, 0xFF (Bit:8 immediate)

// Carga/almacenamiento memoria (alineada/desalineada)
Fpu.simd.Vector128.Xmm0.copy(Fpu.simd.Vector128 [Gpr0])           // vmovdqa xmm0, [rax] (16B aligned)
Fpu.simd.Vector128.Xmm0.copy(Fpu.simd.Vector128.Unaligned [Gpr0]) // vmovdqu xmm0, [rax]
Fpu.simd.Vector256.Ymm0.copy(Fpu.simd.Vector256 [Gpr0])           // vmovdqa ymm0, [rax] (32B aligned)
Fpu.simd.Vector512.Zmm0.copy(Fpu.simd.Vector512 [Gpr0])           // vmovdqa zmm0, [rax] (64B aligned)

// Almacenamiento
Fpu.simd.Vector128 [Gpr0].copy(Fpu.simd.Vector128.Xmm0)           // vmovdqa [rax], xmm0
Fpu.simd.Vector512 [Gpr0].copy(Fpu.simd.Vector512.Zmm0)           // vmovdqa [rax], zmm0

// Broadcast (escalar → vector)
Fpu.simd.Vector128.Xmm0.broadcast(Byte:4 [Gpr0])        // vbroadcastss xmm0, [rax] (Float32 ≡ Byte:4)
Fpu.simd.Vector256.Ymm0.broadcast(Byte:8 [Gpr0])        // vbroadcastsd ymm0, [rax] (Float64 ≡ Byte:8)
Fpu.simd.Vector512.Zmm0.broadcast(Byte:8 [Gpr0])        // vbroadcastsd zmm0, [rax] (Float64 ≡ Byte:8)

// Aritmética FP
Fpu.simd.Vector128.Xmm0.add(Float32x4 Fpu.simd.Vector128.Xmm1)              // vaddps xmm0, xmm0, xmm1 (Bit:128)
Fpu.simd.Vector256.Ymm0.add(Float32x8 Fpu.simd.Vector256.Ymm1)              // vaddps ymm0, ymm0, ymm1 (Bit:256)
Fpu.simd.Vector512.Zmm0.add(Float32x16 Fpu.simd.Vector512.Zmm1)             // vaddps zmm0, zmm0, zmm1 (Bit:512)

Fpu.simd.Vector128.Xmm0.mul(Float32x4 Fpu.simd.Vector128.Xmm1)              // vmulps xmm0, xmm0, xmm1 (Bit:128)
Fpu.simd.Vector512.Zmm0.fma(Float64x8 Fpu.simd.Vector512.Zmm1, Float64x8 Fpu.simd.Vector512.Zmm2) // vfmadd231pd (Bit:512)

// Aritmética entera SIMD
Fpu.simd.Vector128.Xmm0.add(Int32x4 Fpu.simd.Vector128.Xmm1)              // vpaddd xmm0, xmm0, xmm1 (Bit:128)
Fpu.simd.Vector512.Zmm0.mul(Int64x8 Fpu.simd.Vector512.Zmm1)              // vpmulq zmm0, zmm0, zmm1 (Bit:512)

// Máscaras (predicación AVX-512)
Fpu.simd.Vector512.Zmm0.add{Fpu.simd.Mask.K1}(Float32x16 Fpu.simd.Vector512.Zmm1)     // vaddps zmm0 {k1}, zmm0, zmm1 (Bit:512)
Fpu.simd.Vector512.Zmm0.copy{Fpu.simd.Mask.K2}(Float64x8 Fpu.simd.Vector512.Zmm1)    // vmovdqa zmm0 {k2}, zmm1 (Bit:512)

// Conversiones
Fpu.simd.Vector128.Xmm0.convertToInt(Float32x4 Fpu.simd.Vector128.Xmm1)     // vcvttps2dq xmm0, xmm1
Fpu.simd.Vector256.Ymm0.convertToFloat(Float64x4 Fpu.simd.Vector256.Ymm1)   // vcvtpd2ps ymm0, ymm1

// Permutaciones / shuffle
Fpu.simd.Vector128.Xmm0.shuffle(Float32x4 Fpu.simd.Vector128.Xmm1, Byte:0x1B) // vshufps xmm0, xmm0, xmm1, 0x1B
Fpu.simd.Vector512.Zmm0.permute(Float64x8 Fpu.simd.Vector512.Zmm1, Float64x8 Fpu.simd.Vector512.Zmm2) // vpermd zmm0, zmm1, zmm2
```

### Helpers de estado (MXCSR, XCR0)

```fasm
// MXCSR — Control/Status SSE/AVX (Bit:32)
Gpr0.copy(Fpu.simd.Mxcsr)              // vstmxcsr [mem]; mov eax, [mem]
Fpu.simd.Mxcsr.copy(Gpr0.low32)        // mov [mem], eax; vldmxcsr [mem]

// Campos MXCSR (helpers Fase 2+)
Fpu.simd.Mxcsr.roundingMode.copy(Byte:0)   // RC bits 13:14 (0=nearest, 1=down, 2=up, 3=truncate)
Fpu.simd.Mxcsr.flushToZero.copy(Bit:1)     // FTZ Bit:15
Fpu.simd.Mxcsr.denormalsAreZero.copy(Bit:1)// DAZ Bit:6
Fpu.simd.Mxcsr.exceptionMasks.copy(Byte:1 0x1F) // bits 0:5 (IE, DE, ZE, OE, UE, PE)
Fpu.simd.Mxcsr.exceptionFlags.copy(Byte:1 0x1F) // bits 7:12 (sticky flags)
```

### Diagnósticos

| Código | Situación |
|--------|-----------|
| `FASM3080` | AVX/AVX-512 sin `XCR0` habilitado |
| `FASM3081` | `Zmm16`–`Zmm31` / `Mask.K*` sin AVX-512VL |
| `FASM3082` | Memoria desalineada sin `.Unaligned` |
| `FASM3083` | `Mask.K0` usada como máscara de escritura |
| `FASM3084` | `vzeroupper` omitido tras AVX→SSE (warning) |
| `FASM3085` | Broadcast tamaño incorrecto |
| `FASM3086` | FMA sin feature bit |

---

## 5.10 Instrucciones Diferidas + Justificación

| Instrucción/Grupo | Fase | Justificación física | Propuesta futura |
|-------------------|------|----------------------|------------------|
| **Coma flotante** (`ADDSS`, `MULPS`, `SQRT`...) | Fase 2 | Unidad FPU/SSE/AVX físicamente separada de ALU entera; tipos `Fp32/Fp64/Fp16/Bf16` no definidos en Fase 1 | Propuesta de tipos vectoriales |
| **SIMD enteros** (`PADDB`, `PMULLW`...) | Fase 2 | Requieren tipos `Vector128<T>` con `T ∈ {Int8...UInt64}`; ancho de elemento + saturación no definidos | Propuesta de tipos vectoriales |
| **SIMD FP** (`ADDPS`, `DPPS`, `VFMADD`...) | Fase 2 | Precisión IEEE 754, redondeo, FMA, denormales — semántica distinta a ALU entera | Propuesta de tipos vectoriales |
| **`Movsx`/`Movzx` extendidos** | Fase 2 | `.copyZeroExtended`/`.copySignedExtended` ya cubren lo básico; formas vectoriales requieren tipos SIMD | Propuesta SIMD |
| **String ops** (`rep movsb`, `stosb`, `scasb`) | Fase 2 | Requieren modo memoria explícito (Fase 2); `[Gpr6]`/`[Gpr7]` como punteros implícitos | Propuesta de memoria |
| **Interrupts** (`int3`, `sti`, `cli`) | Fase 3 | Requiere privilegio kernel, IDT/TSS, manejo de excepciones | Propuesta de sistema |
| **`syscall`/`sysret`** | Fase 3 | Transición usuario↔kernel; MSRs (`LSTAR`/`STAR`/`FMASK`); ABI System V para argumentos | Propuesta de sistema |
| **Memoria explícita** (`[Gpr+offset]` en operandos) | Fase 2 | Se listan para referencia; direccionamiento completo requiere tipos `Byte`+offsets | Propuesta de memoria |
| **AVX-512 / AMX** (`Zmm`, `K`, `Tile`) | Fase 4+ | Registros vectoriales/extendidos no definidos; unidades físicamente separadas | Propuesta AVX-512/AMX |
| **Capa portable unificada** | Fase 5 | Requiere backend ARM64/RISC-V; sintaxis `Ipr.flowJump(@fin if a less b)` depende de tipos comparables | Propuesta portable |

---

## 5.11 Mapeo Legacy → Nueva Sintaxis (Migración)

> **Herramienta:** `fasm fmt --migrate-syntax` (automático)

| Legacy (Deprecado) | Nueva Sintaxis (Canónica) | Notas |
|--------------------|---------------------------|-------|
| `.copy(Gpr0 to Gpr1)` | `Gpr1.copy(Gpr0)` | **Orden invertido** |
| `.copy(42 to Gpr0)` | `Gpr0.copy(Bit:42)` | Auto |
| `.copy(Gpr1.low32 to Gpr0.low32)` | `Gpr0.low32.copy(Gpr1.low32)` | Auto |
| `.lea(Gpr0 simbolo)` | `Gpr0.lea(simbolo)` | Fase 2 |
| `.ret()` | `Ipr.flowStackJump()` | Auto |
| `.call(sym)` | `Ipr.flowCall(sym)` | Auto |
| `.jump(label)` | `Ipr.flowJump(label)` | Auto |
| `.jump(Gpr0)` | `Ipr.flowJump(Gpr0)` | Auto |
| `.loop(label)` | `Ipr.flowLoop(label)` | Auto |
| `.stackWrite(Gpr0)` | `.stackWrite(Gpr0)` | Sin cambio |
| `.stackRead(Gpr0)` | `Gpr0.stackRead()` | Categoría D |
| `Rax`/`Eax`/`Ax`/`Al`/`Ah` | `Gpr0`/`.low32`/`.low16`/`.low8`/`.high8` | FASM3012 |
| `Rcx`...`R15` | `Gpr1`...`Gpr15` | FASM3012 |
| `R8d`...`R15b` | `Gpr8.low32`...`Gpr15.low8` | FASM3012 |
| `cs`/`ds`/`es`/`fs`/`gs`/`ss` | `Srf0`/`Srf1`/`Srf2`/`Srf3`/`Srf4`/`Srf5` | FASM3012 |
| `mov ds, ax` | `Srf1.copy(Gpr0.low16)` | Auto |
| `mov fs, ax` | `Srf3.copy(Gpr0.low16)` | Auto |
| `rdfsbase rax` | `Gpr0.copy(Srf3.base)` | Auto |
| `wrfsbase rax` | `Srf3.base.copy(Gpr0)` | Auto |
| `mov cr0, rax` | `Crf0.copy(Gpr0)` | Auto |
| `mov rax, cr3` | `Gpr0.copy(Crf3)` | Auto |
| `jmp far` | `(Ipr:Srf0).flowJump(Gpr0)` | Auto |
| `retf` | `(Ipr:Srf0).flowStackJump()` | Auto |

---

## 5.12 Referencias y Fuentes

### Propuestas Aplicadas

| Propuesta | Tema | Estado |
|-----------|------|--------|
| [0001](proposal-0001-copy-operand-order.md) | Orden operandos `copy` | ✅ Aprobada |
| [0002](proposal-0002-rflags.md) | Modelo `rFlags` | ✅ Aprobada |
| [0003](proposal-0003-gpr.md) | Registros `Gpr0`–`Gpr15` + vistas | ✅ Aprobada |
| [0004](proposal-0004-types.md) | Tipos base `Bit`/`Byte` | ✅ Aprobada |
| [0006](proposal-0006-instruction-syntax.md) | Sintaxis unificada (Categorías A–E) | ✅ Aprobada |
| [0008](proposal-0008-x64-abi.md) | ABI x64 configurable | 🔴 En debate |
| [0009](proposal-0009-x64-segment-registers.md) | Segment Register File `Srf0`–`Srf5` | ✅ Aprobada |
| [0010](proposal-0010-x64-control-registers.md) | Control Register File `Crf0`–`Crf4`, `Crf8` | ✅ Aprobada |
| [0011](proposal-0011-x64-debug-registers.md) | Debug Register File `Drf0`–`Drf7` | ✅ Aprobada |
| [0012](proposal-0012-x64-rip-rflags.md) | Instruction Pointer `Ipr` y Flags Register File `Frf` | ✅ Aprobada |
| [0013](proposal-0013-x64-x87-fpu.md) | x87 FPU Stack `Fpu.x87.ST(0)`–`ST(7)` + MMX | ✅ Aprobada |
| [0014](proposal-0014-x64-simd-registers.md) | SIMD Vectoriales `Fpu.simd.Vector128/256/512` + Mask | ✅ Aprobada |

### Fuentes de Hardware

| Regla | Fuente |
|-------|--------|
| Escritura parcial (zero-extend 32b, merge 8/16b) | Intel SDM Vol. 1 §3.4.1.1 |
| Encoding SIB, base/índice RSP/RBP/R12/R13 | Intel SDM Vol. 2A, Tabla 2-5 |
| Restricción bytes altos + REX | Intel SDM Vol. 1 §3.4.1.1 |
| `flowLoop` no modifica flags | Intel SDM Vol. 2B, `LOOP` |
| Máscara shift count (6b/5b) | Intel SDM Vol. 2B, `SHL`/`SHR` |
| `mulx` usa RDX implícito | Intel SDM Vol. 2B, `MULX` |
| `cmpxchg8b/16b` usan RBX/RCX | Intel SDM Vol. 2B, `CMPXCHG8B/16B` |
| `xlat` usa RBX completo | Intel SDM Vol. 2B, `XLAT` |
| `syscall` destruye RCX/R11 | Intel SDM Vol. 2B, `SYSCALL` |
| Numeración GPR 0–15 | AMD APM Vol. 1, Figura registros 64b |
