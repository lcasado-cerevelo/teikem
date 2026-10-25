# Lote 220 — Un solo resumen al día para el cliente (transporte y almacén)

Fecha: 2026-10-25. Dictado de Luis: el cliente no puede recibir mil avisos al día; debe recibir **uno solo**, a la hora que el cliente escoja, con todo lo que tenga marcado; las facturas van aparte; si no hubo nada, no se manda nada. Y el dueño de la mercancía (el cliente) debe enterarse de lo **recibido** y lo **despachado** en el almacén, configurable.

## Qué se construyó

- **Un resumen por cliente y día** en lugar de uno por cliente, aviso y ruta. `ClientNotificationService.DigestAsync` ahora hace dos cosas:
  1. Remesas COD y facturas: igual que antes, un aviso por documento cuando el grupo lleva 3 minutos sin hechos nuevos (no esperan al corte).
  2. Resumen del día: junta, por cliente, los hechos de **PICKED_UP, OUT_FOR_DELIVERY, DELIVERED, WH_RECEIVED y WH_DISPATCHED** ocurridos hasta el último corte (`LastDigestMomentUtc`: la última vez que el reloj de la compañía marcó la hora del cliente). Una línea por aviso encendido; lo posterior espera al día siguiente; sin hechos no se crea nada.
- **Hora por cliente**: columna `Client.NoticeDigestHour` (0–23, NULL = 18), sección `2026-10-25` de `Diseño/logistica-db-update.sql` (idempotente). Se lee y se guarda con los mismos endpoints de configuración (`digestHour` en `GET`/`PUT .../notifications/settings`, 400 `La hora del resumen debe estar entre 0 y 23.`).
- **Avisos del almacén**: dos clases nuevas, `WH_RECEIVED` y `WH_DISPATCHED`, con su propia configuración por cliente (apagados por omisión). Se anotan con dos efectos de estatus: `ClientNotificationAsnReceivedEffect` (ASN → RECEIVED; los avisos que nacen de una compra, sin cliente, no se avisan) y `ClientNotificationExitNoticeDispatchedEffect` (aviso de salida → PARTIAL o SHIPPED; el mismo aviso en dos partes cuenta una vez). Las unidades se calculan al armar el resumen (recibidas: `ReceiptLine.ReceivedQty`; despachadas: `ExitNoticeLine.QtyShipped`).
- **Canales del resumen**: el resumen es un solo `ClientNotification` de clase `DAILY`. Correo = unión de los destinatarios de los avisos incluidos (usuarios activos del portal que no apagaron esos avisos + correos extra de cada uno); bandeja del portal = si al menos uno de los incluidos la tiene encendida.
- **Portal del cliente**: textos de los avisos nuevos en la configuración de «Mis avisos» y en la bandeja; un resumen `DAILY` solo se oculta si la persona apagó la bandeja de **todos** los avisos que lo componen.
- **Web del personal**: selector «Hora del resumen del día» en el panel «Avisos al cliente»; los dos avisos del almacén aparecen como filas nuevas; el panel del portal pasó a ir después de «Personas de contacto» en la ficha del cliente.

## Cómo se probó

- `ClientNotificationTests` (14): resumen único con una sección por aviso encendido y nada antes del corte; hora del cliente, evento posterior al corte que espera al día siguiente y día sin nada que no manda nada; validación 0–23 y cálculo del corte; avisos del almacén (incluye ASN sin cliente, estatus que no cuentan y apagado); facturas y COD siguen aparte; los reintentos y canales omitidos de siempre.
- `ClientNotificationsPanel.test.tsx`: la hora (18 por omisión) y que cambiarla se manda al guardar.
- Suites relacionadas del API (Portal, Notice, ASN, aviso de salida, SQL congelado) y todo `src/kernel` + `src/features/clients` de la web (841 pruebas) en verde. La sección SQL se probó en una transacción con rollback.

## Decisiones a revisar

1. **La hora la configura la compañía por cliente** (en la ficha del cliente), no el propio cliente desde su portal. Si quiere que el cliente la cambie desde su portal, hay que agregar el permiso y la pantalla.
2. **Las remesas de COD salen aparte**, igual que las facturas (el dictado nombró solo las facturas).
3. **Un destinatario del correo del resumen ve todas las líneas**, aunque solo tuviera encendido uno de los avisos (los avisos se configuran por cliente, no por persona).
4. **Las unidades despachadas** de un aviso de salida parcial son el total despachado a la fecha, no solo lo del día.
5. El corte usa la zona horaria de la compañía; si el trabajo `CLIENT_NOTIFICATION_DIGEST` está apagado no sale nada (al prenderlo sale lo acumulado).

## Qué quedó fuera

- Nada del manual quedó pendiente: capítulo 23, FAQ y pantalla L98, en español y en inglés.
- Recorrido de Playwright de la hora del resumen.
