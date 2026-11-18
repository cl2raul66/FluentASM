## Resumen de cambios
<!-- Describe brevemente qué se hizo y por qué. -->

## Issue relacionado
closes #

## Tipo de cambio
<!-- Marca con [x] lo que corresponda. -->
- [ ] Nueva funcionalidad o instrucción
- [ ] Corrección de error
- [ ] Cambio en archivos de proyecto (`.fproj`, `.fworkspace`)
- [ ] Documentación o decisión de diseño
- [ ] `FluentASM.Sample` o integración continua

## Cómo se ha comprobado
<!-- Deja marcado lo que ejecutaste. No se usan proyectos de pruebas formales (*Tests.csproj). -->
- [ ] `dotnet build` sin advertencias (`.editorconfig` respetado)
- [ ] Ejemplo en `FluentASM.Sample` o `rsc/` compilado y ejecutado
- [ ] Verificación en QEMU *bare metal* / contraste de bytes con NASM (si toca el codificador)

## Revisión de proyecto (si se ha tocado un `.fproj` o `.fworkspace`)
<!-- Si no aplica, escribe "no aplica". En Fase 1 son texto legible (se revisan directo en el diff). En fases binarias, pega aquí la salida de: fasm_admin dump <archivo> -->

## Decisión de diseño (si cambia el comportamiento del lenguaje)
<!-- Si no aplica, escribe "no aplica". En caso contrario, enlaza la sección añadida en CONTRIBUTING.md. -->

## Versiones a publicar
<!-- Deja o cambia a x.x.x-preview solo los proyectos que quieras publicar al hacer merge.
     Si un proyecto no lleva versión (déjalo comentado o con x.x.x-preview), no se publica. -->
ver(core): x.x.x-preview
ver(compiler): x.x.x-preview
ver(sample): x.x.x-preview

## Notas para quien revisa
<!-- Opcional: puntos de riesgo, dudas abiertas o partes en las que conviene fijarse. -->
