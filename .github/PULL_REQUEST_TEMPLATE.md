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
<!-- Una línea por proyecto publicable. Escribe números reales (p. ej. ver(core): 0.2.0-preview);
     el marcador x.x.x-preview significa "no publicar". Solo se publica si la versión es estrictamente mayor que la última publicada.
     En el PR de promoción develop -> main usa exactamente las mismas líneas: el flujo quita -preview y publica la estable.
     FluentASM.Sample no entra en el versionado ni se publica en releases. -->
ver(core): x.x.x-preview
ver(compiler): x.x.x-preview
ver(lsp): x.x.x-preview
ver(ide): x.x.x-preview

## Notas para quien revisa
<!-- Opcional: puntos de riesgo, dudas abiertas o partes en las que conviene fijarse. -->
