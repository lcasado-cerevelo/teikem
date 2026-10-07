# App: cerrar sesión, bloqueo y quitar compañía (2026-10-07)

## Qué se construyó
- Rótulo «Cerrar sesión» (antes «Cambiar de usuario»).
- Bloqueo: candado compacto en la fila del nombre (sin ocupar otra fila); pantalla `/lock` con el PIN del mismo usuario; la app se abre siempre bloqueada si hay sesión guardada (`locked` solo en memoria).
- Quitar compañía: deslizar a la izquierda (o pulsación larga) con confirmación y aviso de capturas sin enviar; 401 del servidor → se quita sola y borra su base.

## Cómo se probó
jest (502 pruebas): bloqueo/desbloqueo con PIN bueno y malo, `lockSession`, conteo de pendientes y `removeCompany`, 401 → compañía quitada. No probado en el Zebra ni con el gesto real de deslizar.

## Decisiones a revisar
- No se hizo cierre por inactividad (se puede agregar después, configurable por compañía).
- No se hizo `allowBackup=false` (hipótesis sobre por qué reinstalar conservaba la sesión; no verificada).
- El registro de antes (sin `dbName`) se quita pero su base común no se borra (guarda configuración del teléfono).
- El heartbeat con aparato desactivado ahora también borra la base local de esa compañía.
