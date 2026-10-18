# Prueba de concepto: "hola mundo" en NASM para Win64

## Qué es

`hello-win64.asm` es un "hola mundo" escrito en **NASM** (sintaxis Intel clásica, no FluentASM) que
llama a `printf` y a `_exit` del CRT de Microsoft. Se conserva aquí como referencia histórica: fue el
primer experimento del proyecto, antes de que existiera el diseño del lenguaje.

## Cómo se ensambló

```powershell
nasm -f win64 hello-win64.asm -o hello.obj      # COFF x64
link hello.obj /subsystem:console /entry:main /out:hello.exe
```

En el repositorio había además un `hola.o` de **ELF64** (variante Linux con `_start`, `mensaje` y
`longitud`) cuyo código fuente ya no existe; los artefactos binarios se eliminaron porque no son
recursos, son salidas de compilación.

## Qué se concluyó (y por qué importa al diseño)

1. **La cadena de herramientas funciona**, pero es de otro ensamblador: NASM + `link.exe`. FluentASM
   no la va a usar, porque la visión exige un ensamblador autocontenido sin dependencias externas.
2. **Llamar a `printf` exige el CRT y un enlazador**: es decir, hace falta un ejecutable del SO con
   tabla de importaciones. Eso confirmó que la Fase 1 debía limitarse a imágenes planas sin símbolos
   externos (ver [ADR 0003](../../../docs/adr/0003-salidas-imagen-plana.md)).
3. **NASM sirve como oráculo.** Este es el hallazgo más útil: se puede ensamblar un fragmento con
   `nasm -f bin` y comparar los bytes con los que genera FluentASM. Todas las tablas de bytes de la
   documentación se verificaron así.

## El oráculo, en la práctica

```powershell
# Un fragmento mínimo, sin secciones de datos para no arrastrar relleno
Set-Content s.asm "bits 64`ndefault rel`nsection .text`nstart:`n    mov eax, 42`n    ret"
nasm -f bin s.asm -o s.bin
Format-Hex s.bin        # -> b8 2a 00 00 00 c3
```

Detalle importante aprendido con este método: `nasm -f bin` **alinea las secciones a 4 bytes**, así
que los bytes que siguen a la instrucción pueden ser relleno de la sección de datos, no parte de la
instrucción. En las pruebas hay que ensamblar un fragmento por caso, o aislar la sección `.text`.
