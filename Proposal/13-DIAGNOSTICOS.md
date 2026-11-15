# 13 — Diagnósticos

> **Normativo.** Cada restricción del lenguaje tiene un código estable `FASMnnnn`, una severidad y una corrección sugerida. Los códigos no se renumeran ni se reutilizan (D-026).

---

## 1. Formato

```
FASM3015: .high8 no existe en 'Gpr9' (solo Gpr0–Gpr3)
  archivo.fasm:42:12
  encontrado: Gpr9.high8
  esperado:  Gpr0.high8 … Gpr3.high8
  sugerencia: use Gpr9.low8 o mueva el valor a Gpr0
```

Toda salida contiene:

| Campo | Obligatorio |
|---|---|
| Código | sí |
| Severidad | sí |
| Mensaje | sí |
| Posición (archivo:línea:columna) | sí |
| Valor encontrado | cuando aplica |
| Valor esperado | cuando aplica |
| Sugerencia | siempre que exista una corrección única |

### 1.1 Severidades

| Severidad | Efecto |
|---|---|
| `error` | No se genera binario |
| `warning` | Se genera binario; el usuario puede promoverlo a error |
| `info` | Solo informativo; no se muestra por defecto |

---

## 2. Rangos

| Rango | Ámbito |
|---|---|
| `1000`–`1099` | Léxico |
| `1100`–`1199` | Sintaxis |
| `2000`–`2099` | Programa, símbolos, secciones, datos |
| `3000`–`3099` | ATM, registros, vistas, flags, SIMD, x87 |
| `4000`–`4099` | Operandos, direccionamiento, verbo |
| `5000`–`5099` | ABI, pila, llamadas |
| `6000`–`6099` | Target y features |
| `8000`–`8099` | Estilo y bootstrap |
| `9000`–`9099` | Internos del compilador |

---

## 3. Léxico (1xxx)

| Código | Sev. | Mensaje | Corrección |
|---|---|---|---|
| `FASM1001` | error | Carácter inesperado `{c}` | Elimine el carácter o use un identificador válido |
| `FASM1002` | error | Cadena no cerrada | Cierre con `"` |
| `FASM1003` | error | Comentario de bloque no cerrado | Cierre con `*/` |
| `FASM1004` | error | Número malformado `{texto}` | Use decimal, `0x` o `0b` |
| `FASM1005` | error | Identificador de más de 255 caracteres | Acórtelo |
| `FASM1006` | error | BOM o byte nulo en mitad del archivo | Guarde como UTF-8 sin BOM |

## 4. Sintaxis (11xx)

| Código | Sev. | Mensaje | Corrección |
|---|---|---|---|
| `FASM1101` | error | Falta `)` | Cierre la llamada |
| `FASM1102` | error | Falta `}` | Cierre el bloque |
| `FASM1103` | error | Falta `]` | Cierre el operando de memoria |
| `FASM1104` | error | Operando de memoria sin capacidad | Escriba `Byte:8 [ … ]` |
| `FASM1105` | error | Verbo sin receptor | Escriba `destino.verbo(…)` |
| `FASM1106` | error | Argumento inesperado | Revise la firma del verbo |
| `FASM1107` | error | Coma final | Elimine la coma |
| `FASM1108` | error | Bloque sin cabecera | `program`, `section` o `routine` |
| `FASM1109` | error | Cadena en contexto de instrucción | Mueva el texto a `.rodata` |
| `FASM1110` | error | Escape inválido `\{c}` | Use `\n`, `\t`, `\\`, `\"`, `\0`, `\xNN` |
| `FASM1111` | error | Dos sentencias sin separador | Use salto de línea o `;` |

## 5. Programa y símbolos (2xxx)

| Código | Sev. | Mensaje | Corrección |
|---|---|---|---|
| `FASM2001` | error | Archivo sin bloque `program` | Añada `program <nombre> { … }` |
| `FASM2002` | error | Atributo `.x` repetido | Elimine el duplicado |
| `FASM2003` | error | Símbolo `{s}` no definido | Declárelo o corrija el nombre |
| `FASM2004` | error | Atributo `.x` desconocido | Consulte la lista de atributos |
| `FASM2005` | error | Sección `{s}` declarada dos veces | Fusione ambas |
| `FASM2006` | error | Sección `{s}` desconocida | Use `.execute`, `.rodata`, `.data` o `.bss` |
| `FASM2007` | error | Dato literal en `.execute` | Use memoria, parámetro o verbo estructural |
| `FASM2008` | error | Payload en `.bss` | Use `.data` o quite el payload |
| `FASM2009` | error | `routine` fuera de `.execute` | Muévala a `.execute` |
| `FASM2010` | error | Rutina `{s}` duplicada | Renómbrela |
| `FASM2011` | error | Rutina anidada | Extraiga la rutina interna |
| `FASM2012` | error | Declaración después de una sentencia | Muévala al inicio |
| `FASM2013` | warning | Camino sin retorno en `{s}` | Añada `Ipr.flowStackJump()` |
| `FASM2014` | error | `.naked(1)` con prólogo declarado | Quite `.frame`/`.preserves`/`.shadow` |
| `FASM2015` | error | `.frame(Byte:N)` no múltiplo de 16 | Use un múltiplo de 16 |
| `FASM2016` | error | Parámetro `{s}` duplicado | Renómbrelo |
| `FASM2017` | error | Locales exceden el frame | Aumente `.frame` |
| `FASM2018` | error | Locales sin `.frame` | Declare `.frame(Byte:N)` |
| `FASM2019` | error | Payload de {n} bytes para capacidad {m} | Ajuste la capacidad |
| `FASM2020` | error | Recurso `{s}` no declarado | Declárelo con `resource` |
| `FASM2021` | error | Etiqueta `@{s}` duplicada | Renómbrela |
| `FASM2022` | error | Programa sin punto de entrada | Añada `.entry(…)` o una rutina |
| `FASM2023` | error | `resource` en `.execute` | Muévalo a una sección de datos |
| `FASM2024` | error | `.entry` apunta a `{s}`, que no existe | Corrija el destino |
| `FASM2025` | error | Selector `{s}` inválido | Use un valor de la lista |
| `FASM2026` | error | Payload no permitido con `--no-payload` | Use `resource file(…)` |
| `FASM2027` | error | Byte de payload {n} fuera de 0–255 | Use `Byte:2` o divida el valor |
| `FASM2028` | error | Cadena en instrucción | Use un símbolo de `.rodata` |
| `FASM2029` | error | Carácter literal `'{c}'` | Use `data` con `"{c}"` |
| `FASM2030` | error | Flotante literal | Use bytes IEEE 754 en `.rodata` |

## 6. ATM y registros (3xxx)

| Código | Sev. | Mensaje | Corrección |
|---|---|---|---|
| `FASM3001` | error | Verbo `.{v}` no disponible para `{r}` | Use uno de los verbos listados |
| `FASM3002` | error | Mezcla de `.high8` con `.low8` de `Gpr4`–`Gpr7` | Use registros compatibles |
| `FASM3012` | error | Nombre legacy `{n}` | Use `{canónico}` |
| `FASM3013` | error | Índice de registro fuera de rango | Use 0–{max} |
| `FASM3015` | error | `.high8` no existe en `{r}` | Solo `Gpr0`–`Gpr3` |
| `FASM3016` | error | Tupla de destinos en verbo de un solo destino | Use un destino |
| `FASM3017` | error | Verbo `{v}` requiere tupla de destinos | Escriba `(A:B).{v}(…)` |
| `FASM3020` | error | `Byte` en contexto de registro | Use `Bit:8N` |
| `FASM3021` | error | `Bit` en contexto de memoria | Use `Byte:N/8` |
| `FASM3022` | error | `Bit:{n}` no operable | Use 1/8/16/32/64/80/128/256/512 |
| `FASM3023` | error | `Byte:{n}` no accesible | Use 1/2/4/8/10/16/32/64 |
| `FASM3024` | error | Vista sobre vista | Escriba la vista final |
| `FASM3025` | error | Vista `{v}` incompatible con `{verbo}` | Cambie de vista o de verbo |
| `FASM3026` | error | Operación aritmética entre formas | Calcule la capacidad |
| `FASM3027` | error | Múltiplo usado como ancho de operación | Use la forma base |
| `FASM3028` | error | `part{N}` con `N` no admitido | Use 8/16/32/64 |
| `FASM3029` | error | Ancho de máscara incompatible | Ajuste `Krf.lowN` |
| `FASM3030` | error | Escritura directa a `Srf0` | Use `(Ipr:Srf0).flow*` |
| `FASM3031` | error | `Srf3.base`/`Srf4.base` sin `FSGSBASE` | Active `Crf4.fsgsbase(1)` |
| `FASM3032` | error | Selector inválido | Revise GDT/LDT y RPL |
| `FASM3040` | error | `Crf1` es reservado | No lo use |
| `FASM3041` | error | Escritura a `Crf{n}` fuera de ring 0 | Requiere CPL 0 |
| `FASM3042` | error | `Crf0.PG` sin `Crf4.PAE` en x64 | Active PAE |
| `FASM3043` | warning | Caché deshabilitada con `Crf0.CD` | ¿Es intencional? |
| `FASM3044` | warning | `Simd` en uso con `Crf4.OSFXSR = 0` | Active SSE en el SO |
| `FASM3050` | error | `Drf4`/`Drf5` con `Crf4.DE = 1` | Use `Drf6`/`Drf7` |
| `FASM3051` | error | Acceso a `Drf{n}` fuera de ring 0 | Requiere CPL 0 |
| `FASM3060` | error | `copy` sobre `Ipr` | Use `Ipr.flow*` o `Ipr.current` |
| `FASM3061` | error | Flag de sistema escrito fuera de ring 0 | Requiere CPL 0 |
| `FASM3062` | error | Puerto de E/S sin `IOPL` | Eleve `Frf.iopl` o ejecute en ring 0 |
| `FASM3064` | error | `Frf.low32`/`.low16` en modo largo | Use `Frf` completo |
| `FASM3070` | error | `XSETBV` fuera de ring 0 | Requiere CPL 0 |
| `FASM3071` | error | Desbordamiento de pila x87 | Equilibre push/pop |
| `FASM3072` | error | Subdesbordamiento de pila x87 | Falta un push |
| `FASM3073` | error | Uso de MMX | Use `Simd` |
| `FASM3074` | error | Ancho de memoria inválido para x87 | Use 4/8/10 (FP) o 2/4/8 (entero) |
| `FASM3075` | error | `store*` solo desde `Xrf0` | Mueva el valor al tope |
| `FASM3076` | warning | Pila x87 no vacía al final del camino | Añada `Xrf.pop()` |
| `FASM3077` | error | Profundidades x87 incompatibles en la unión | Iguale la profundidad |
| `FASM3078` | warning | `rotateUp`/`rotateDown` impide el análisis | Evítelos en bloques verificados |
| `FASM3079` | warning | Excepción FP no enmascarada | Ajuste `Xrf.control` |
| `FASM3080` | error | Vista `{v}` no soportada por `{target}` | Use una vista menor o suba el target |
| `FASM3081` | error | `Simd{n}` requiere AVX-512 | Use `Simd0`–`Simd15` o suba el target |
| `FASM3082` | error | Verbo no disponible para esa partición | Cambie `part{N}` |
| `FASM3083` | error | `Krf0` no puede ser máscara de escritura | Use `Krf1`–`Krf7` |
| `FASM3084` | error | Máscara en target sin AVX-512 | Suba el target o quite la máscara |
| `FASM3085` | error | `part{N}` no divide al ancho | Ajuste la vista |
| `FASM3086` | warning | Mezcla de anchos SIMD en una secuencia | Separe las secuencias |
| `FASM3087` | error | `part{N}` no admitido por el verbo | Consulte el catálogo |
| `FASM3088` | error | Ancho de máscara incompatible | Use `Krf.low{elementos}` |
| `FASM3089` | error | Índice `{k}` fuera de rango (0–{max}) | Use un índice válido |

## 7. Operandos y verbos (4xxx)

| Código | Sev. | Mensaje | Corrección |
|---|---|---|---|
| `FASM4001` | error | Número de argumentos incorrecto | Consulte la firma |
| `FASM4002` | error | Tipo de argumento incorrecto | Consulte la firma |
| `FASM4003` | error | `Gpr4` no puede ser índice SIB | Use otro registro |
| `FASM4004` | error | Escala `{s}` inválida | Use 1, 2, 4 u 8 |
| `FASM4005` | error | Más de una base o índice | Calcule la dirección en un GPR |
| `FASM4006` | error | Desplazamiento fuera de 32 bits | Materialice la base |
| `FASM4007` | error | `.high8` como base o índice | Use el registro completo |
| `FASM4008` | error | Vista distinta de 64 bits como base/índice | Use `GprN` |
| `FASM4009` | error | Capacidad `{n}` incompatible con `{ancho}` | Iguale capacidad y registro |
| `FASM4010` | error | Etiqueta `@{s}` no definida | Declárela |
| `FASM4011` | info | Salto `rel8` fuera de alcance | Se emitirá `rel32` |
| `FASM4012` | error | Flag `Frf.{f}` indefinido por la instrucción anterior | Compare antes de saltar |
| `FASM4013` | warning | Código inalcanzable | Elimínelo o alcáncelo |
| `FASM4014` | error | Alineación `{n}` no es potencia de 2 | Use 1, 2, 4, 8, … |
| `FASM4015` | error | Acceso alineado sobre dirección no alineada | Use la variante no alineada |
| `FASM4016` | error | Verbo requiere privilegio | Ejecute en ring adecuado |
| `FASM4017` | error | Feature `{f}` no disponible | Suba el target |
| `FASM4020` | error | Destino de salto fuera de ±2 GiB | Reestructure |
| `FASM4021` | error | `flowStackJump(Byte:{n})` no múltiplo de 8 | Use múltiplo de 8 |
| `FASM4022` | error | Cuerpo de `flowLoop` mayor de 127 bytes | Use `decrement` + `flowJump` |
| `FASM4023` | error | Verbo de flujo privilegiado en ring insuficiente | Requiere CPL 0 |
| `FASM4026` | error | Memoria → memoria con `copy` | Intercale un GPR |
| `FASM4028` | error | Operación requiere scratch no declarado | Declare un registro de trabajo |
| `FASM4029` | error | Dirección no representable en un SIB | Calcule la base |
| `FASM4030` | error | Cuenta de desplazamiento variable fuera de `Gpr1.low8` | Use `Gpr1.low8` |
| `FASM4031` | error | División por cero estructural | Corrija el divisor |
| `FASM4032` | warning | División con cociente posiblemente fuera de rango | Compruebe antes |
| `FASM4033` | info | Divisor estructural constante | — |
| `FASM4034` | warning | Atómico sobre dirección no alineada | Alinee a 8 |
| `FASM4035` | error | `copyNonTemporal` mal alineado | Alinee a 8 |
| `FASM4036` | warning | `flushCacheLine` mal alineado | Alinee a 64 |

## 8. ABI (5xxx)

| Código | Sev. | Mensaje | Corrección |
|---|---|---|---|
| `FASM5001` | error | Llamada a símbolo externo | v1 no enlaza dinámicamente |
| `FASM5002` | error | Shadow space no reservado | Añada `.shadow(1)` |
| `FASM5003` | error | Pila desalineada antes de `flowCall` | Ajuste el frame |
| `FASM5004` | error | Frame no restaurado al retornar | Restaure `Gpr4` |
| `FASM5005` | warning | `Stack.align` impide el análisis | Gestione la pila a mano |
| `FASM5006` | warning | Argumento `{n}` no colocado en `{reg}` | Escriba el registro antes de llamar |
| `FASM5007` | error | `.returns` y `{reg}` no se escribe | Escriba el valor de retorno |
| `FASM5008` | error | Dirección absoluta en imagen PIC | Use RIP-relativo |
| `FASM5009` | error | `.redzone(1)` en Win64 | Elimine el atributo |
| `FASM5010` | error | Llamada entre ABIs distintas | Escriba un puente |
| `FASM5011` | error | Frame del llamador modificado | Restaure antes de llamar |
| `FASM5012` | warning | TLS de otra ABI (`{reg}`) | Use el registro de su ABI |
| `FASM5013` | warning | Rutina variádica sin guardar `Simd0`–`Simd7` | Guárdelos |

## 9. Target (6xxx)

| Código | Sev. | Mensaje | Corrección |
|---|---|---|---|
| `FASM6001` | error | Instrucción `{i}` no disponible en `{target}` | Suba el target |
| `FASM6002` | error | Target `{t}` desconocido | Use `x64-sse2`…`x64-avx512full` |
| `FASM6003` | error | `.r512` requiere AVX-512 | Use `.r256` o suba el target |
| `FASM6004` | warning | Uso de instrucción obsoleta | Prefiera la alternativa |
| `FASM6005` | error | Perfil incompatible con la imagen | Revise `.image` |

## 10. Estilo y bootstrap (8xxx)

| Código | Sev. | Mensaje | Corrección |
|---|---|---|---|
| `FASM8001` | info | Cadena de acciones en cuerpo de rutina | Expanda a una por línea |
| `FASM8101` | info | Payload de `{n}` bytes en `{s}` (bootstrap) | Use `--no-payload` para prohibirlo |
| `FASM8102` | info | Línea de más de 100 columnas | Divida |
| `FASM8103` | info | `data` sin payload en `.rodata` | ¿Quería `.bss`? |
| `FASM8104` | info | Condición expandida en varios `Jcc` | Simplifique la expresión |
| `FASM8105` | info | Salto a la instrucción siguiente | Eliminado |
| `FASM8106` | info | Constante de hardware usada | Documente su origen |
| `FASM8107` | info | Rutina sin `.returns` que escribe `Gpr0` | Declare el contrato |

## 11. Internos (9xxx)

| Código | Sev. | Mensaje |
|---|---|---|
| `FASM9001` | error | Error interno: nodo AST desconocido |
| `FASM9002` | error | Error interno: verbo sin plantilla de encoding |
| `FASM9003` | error | Error interno: símbolo sin sección asignada |

Si aparece un `9xxx`, es un bug del compilador: el mensaje incluye versión, nodo y posición.

---

## 12. Supresión y promoción

```bash
fasm build programa.fasm --warn-as-error
fasm build programa.fasm --silence FASM8101,FASM8103
```

| Regla | Detalle |
|---|---|
| Supresión | Solo para `info` y `warning` |
| Promoción | `--warn-as-error` convierte todo `warning` en `error` |
| Códigos `9xxx` | Nunca se suprimen |
| Códigos `error` | Nunca se suprimen |

---

## 13. Estabilidad

- Un código publicado no cambia de significado.
- Un código retirado se marca `obsoleto` y no se reutiliza.
- Los mensajes pueden mejorar sin cambiar el código.
- Cada código tiene al menos un test en la suite de diagnósticos (`tests/diagnostics/FASMxxxx.fasm`).

---

## 14. Resumen

- Todo error tiene código, posición y corrección.
- Los rangos separan léxico, sintaxis, programa, ATM, operandos, ABI, target, estilo e interno.
- Las severidades son tres: `error`, `warning`, `info`.
- Los códigos son estables y testeables.
- Un `9xxx` siempre es un bug.
