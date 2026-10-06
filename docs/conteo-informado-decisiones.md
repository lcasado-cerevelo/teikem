# Conteo informado al capturar — decisiones (tarea 25)

## Qué se construyó
- **Servidor:** columnas nuevas (Tenant: `CountExpectedReveal`, `CountRecountTolerancePct`, `CountRevealShowsNumber`; UserTenant: `CountSeeExpected`; CycleCountLine: `CheckState`, `FirstCheckQty`, `LastCheckQty`) en `Diseño/logistica-db-estructura.sql` (`db-init` y `db-update` verificados). `CountRevealRules` (puro), `CycleCountService.CheckLineAsync` / `RevealViewAsync`, `POST /cycle-counts/{id}/lines/{lineId}/check`, `reveal` en la ficha, `PUT /users/{id}/count-see-expected`, ajustes en `PUT /tenant/settings`. La captura de una línea verificada solo acepta la última cifra verificada.
- **Web:** panel «Conteo cíclico: lo esperado al contar» en Ajustes → Operación y marca «Ve lo esperado al contar» por usuario (columna y acciones).
- **App:** verificación al aceptar (conteo por posición) o al confirmar (conteo por producto), reconteo sin decir el esperado, línea cerrada, mensajes.

## Cómo se probó
- Servidor: 3231 pruebas (reglas, servicio con permisos y ajustes reales, contratos, seguridad de controladores).
- Web: `npm run check` (tipos, tsc, lint, vitest, build) con pruebas del panel (guardado parcial y validación del margen) y de la marca por usuario.
- App: 489 pruebas (lógica de verificación, pantalla por posición, pantalla por producto, 403 y sin señal), typecheck y lint.
- No probado en el Zebra ni con CI de este push.

## Decisiones tomadas (a revisar)
1. **Supervisor = quien tiene `warehouse.count`**: ve lo esperado desde el inicio y no pasa por estos ajustes. No existe un «rol supervisor» aparte (se recomienda crear «Supervisor de conteo» con Contar).
2. **La compañía en «Nadie» no cierra lo que ve el supervisor** (pendiente tu decisión: si debe cerrarlo también durante una auditoría).
3. **Defecto de la compañía: «Solo los marcados»** (nadie lo ve hasta que se marque a alguien).
4. **Margen en %** de lo esperado (0 = cualquier diferencia); esperado 0 → cualquier cantidad es diferencia.
5. **Un reconteo máximo:** primera cifra → (si fuera del margen) segunda cifra → línea cerrada (FINAL aunque siga sin coincidir; queda para revisión del supervisor, que ve ambas cifras en la bitácora).
6. **El esperado nunca se manda con RECOUNT;** con MATCH/FINAL solo si la compañía muestra el número.
7. **Sin señal no se verifica** (se captura a ciegas, como antes): el servidor solo puede revelar lo que verifica en línea. Un 403 apaga la verificación para ese conteo en la app.
8. **Series no se verifican** (el conteo por serie va por lista de números).
9. **Fuera de esta versión:** conteo abierto con varios productos (7.2), líneas agregadas a mano, opción «ciego/informado» al crear cada conteo y la marca «Conteo informado» en la pantalla. La app ya recibe `reveal` en la ficha para usarlo después.
10. **Conteo por producto:** la verificación ocurre al Confirmar y ese toque no confirma (para que se lea lo que pide recontar); el siguiente sí.
