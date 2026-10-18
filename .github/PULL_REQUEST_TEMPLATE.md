<!--
Gracias por contribuir a FluentASM.
Rellena las secciones aplicables. Si algo no aplica, escribe "no aplica" en lugar de borrarlo:
que quede claro que se ha tenido en cuenta.
-->

## Resumen de cambios

<!-- Describe brevemente qué se hizo y por qué. -->

## Issue relacionado

closes #

## Tipo de cambio

- [ ] Nueva funcionalidad
- [ ] Corrección de errores
- [ ] Documentación
- [ ] Decisión de diseño (incluye ADR)
- [ ] Pruebas o integración continua
- [ ] Refactorización sin cambio de comportamiento

## Cómo se ha comprobado

<!--
Los cambios en el codificador, el layout o los emisores exigen bytes dorados.
Los cambios de sintaxis o de formato exigen ejemplos ejecutados.
-->

- [ ] `dotnet build` sin errores ni advertencias
- [ ] `dotnet test` en verde
- [ ] Prueba de bytes dorados añadida o actualizada
- [ ] Comparación con el oráculo NASM, si aplica
- [ ] Ejemplos de `rsc/examples/` siguen compilando

## Volcado de proyecto (si se ha tocado un `.fproj` o `.fworkspace`)

<!-- Pega aquí la salida de: fasm_admin dump <archivo> -->

```text

```

## Decisión de diseño (si cambia el comportamiento del lenguaje)

<!-- Enlaza el ADR nuevo en docs/adr/ y explica por qué el anterior ya no sirve. -->

no aplica

## Sugerencia de versión

<!-- MAJOR.MINOR.PATCH, con -preview si va a develop. Ver CONTRIBUTING.md. -->

x.x.x-preview

## Notas para quien revisa

<!-- Qué mirar con más atención y qué se ha dejado fuera a propósito. -->
