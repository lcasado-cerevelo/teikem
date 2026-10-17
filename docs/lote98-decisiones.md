# Lote 98 — Avisos al cliente (P10, 2) (2026-10-17)

Segundo lote del portal de clientes ([p10-portal-diseno.md](p10-portal-diseno.md)): los avisos que Teikem manda al cliente (el sender). Servidor, trabajo y la pantalla en el expediente del cliente.

## Qué se construyó
- **Configuración por cliente, la decide la compañía** (`ClientNotificationSetting`): cinco avisos × tres canales (correo, bandeja del portal, SMS) + correos y teléfonos extra; sin fila = apagado.
- **Hechos** (`ClientNotificationEvent`): efectos sobre el cambio de estatus de la orden (Recogido, En tránsito, Entregado), de la remesa COD (Remitida) y de la factura (Emitida); **solo se anotan si el cliente tiene el aviso encendido**.
- **Resúmenes** (`ClientNotification`) y **envíos** (`ClientNotificationDelivery`): el trabajo `CLIENT_NOTIFICATION_DIGEST` (cada 2 min) agrupa por cliente y ruta (o remesa, o factura) cuando el grupo lleva 3 minutos sin hechos nuevos, arma el texto (es/en, con cajas, órdenes, ruta y COD cobrado), manda por correo a los usuarios activos del portal y a los correos extra, deja el resumen en la bandeja del portal y reintenta a 1, 5 y 30 minutos.
- API `/api/v1/clients/{id}/notifications/{settings,log}` (`clients.read` / `clients.update`); `update.sql` (ag): 4 tablas y EntityType de la bitácora.
- Web: panel «Avisos al cliente» en la ficha del cliente (casillas por aviso y canal, correos y teléfonos extra con validación, guardar/descartar, registro de resúmenes); openapi y cliente generado; textos es/en.
- Manual 23, manual de pantallas L98 con capturas, FAQ y este documento.

## Cómo se probó
- xunit `ClientNotificationTests` (9): reglas de correos y teléfonos, configuración (apagado por omisión, guardar, validaciones con mensaje exacto, aislamiento entre clientes y compañías), hechos solo con el aviso encendido y solo para los estatus correctos, un resumen por cliente y ruta tras asentarse (cajas, órdenes, destinatarios sin los invitados), entregado con COD y descarte si se apaga, COD procesado y factura emitida, reintentos hasta «con error», SMS y correo no configurado «omitidos», sin destinatarios, y el trabajo. Suite completa.
- vitest: reglas (3) y el panel (4). Playwright contra el API real, escritorio y móvil: todo apagado, correo mal escrito no deja guardar, se enciende Entregado (correo y bandeja con un correo extra), se guarda y sigue al recargar.
- Contra SQL real (`db-init` aplicó (ag)): guardar y leer la configuración, aviso inexistente (400), registro vacío y el trabajo corre.

## Decisiones a revisar con Luis
1. **«COD procesado» = remesa remitida** (cuando el cliente recibe su dinero). Si se prefiere avisar al cuadrar (cobro cuadrado), se cambia el estatus que dispara.
2. **Los hechos solo cuentan desde que el aviso se enciende**; no hay avisos retroactivos.
3. **Recogido** se dispara al pasar la orden a Recogido (estatus PICKUP). Hoy ese estatus no lo marca ningún flujo automático fuera del cambio manual o de la futura app del chofer (P7).
4. **El destinatario del correo** son los usuarios **activos** del portal más los correos extra por aviso; no se usan los contactos del cliente (pueden agregarse como correos extra).
5. **Sin avisos al consignatario** (lo manejará el cliente desde su portal, con su aceptación) ni SMS real (espera Twilio).
6. Los resúmenes quedan en la bandeja del portal pero **no hay portal todavía**: se leen en los lotes 99 y 100.
