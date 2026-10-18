# `rsc/` — Recursos

Todo lo que **no** es código C# del proyecto: ejemplos, pruebas de concepto y bibliotecas nativas.

```
rsc/
├── examples/                 → Ejemplos en sintaxis fluida. Son casos de prueba vivos.
├── poc/                      → Pruebas de concepto con herramientas externas (NASM, ...).
└── libs/                     → Bibliotecas nativas .bin (se versionan: son recursos, no salidas)
```

## `rsc/examples/`

Los ejemplos **no son adornos**: los que pertenecen a una fase ya implementada se ensamblan y se
ejecutan en la integración continua.

| Ejemplo | Fase | Estado | Qué demuestra |
|---|---|---|---|
| [ret-42.fasm](examples/ret-42.fasm) | 1 | Esperando la Fase 1 | El ciclo mínimo: bytes exactos y ejecución en memoria |
| [add-two-numbers.fasm](examples/add-two-numbers.fasm) | 1 | Esperando la Fase 1 | Rutinas, parámetros, prólogo y epílogo generados |
| [hola-mundo-win64.fasm](examples/hola-mundo-win64.fasm) | 5 | **Boceto, no compila** | Salida por consola con la API de Windows, sin CRT |
| [button-winui.fasm](examples/button-winui.fasm) | 5 | **Boceto, no compila** | Traducción de un control real de `WinUIAsmCtrl` |

Regla: un ejemplo solo puede declararse "implementado" cuando su fase está cerrada y compila en la
integración continua.

## `rsc/libs/`

Bibliotecas nativas: ficheros `.bin` con código máquina ya compilado que FluentASM incrusta dentro de
la imagen final. **Se versionan**, porque son recursos de origen y no resultados de compilación (a
diferencia de las imágenes `.x64`, que están en `.gitignore`).

## `rsc/poc/`

Experimentos con herramientas externas, para aprender y para comparar. Cada uno lleva su `NOTES.md`
explicando qué se probó y qué se concluyó.
