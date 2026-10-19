# Lote 136 — P11: Webhooks de salida y bandeja de salidas (dictado de Luis, 2026-10-19)

## Qué se construyó
- **SQL:** sección (bm) de `update.sql` — `IntegrationWebhook` (URL, eventos, secreto de firma cifrado, suspensión, quitado lógico) e `IntegrationWebhookDelivery` (cuerpo, intentos, siguiente intento; UNDELIVERED = la bandeja de salidas). Las tablas `WebhookSubscription`/`WebhookDelivery` del esquema congelado quedan sin uso.
- **Reglas puras** (`WebhookRules`): URL https y pública (rechaza localhost, nombres sin dominio, `.local`/`.internal`, IP privadas, de bucle, de enlace local, CGNAT, multicast), eventos, firma `sha256=` HMAC de «marca de tiempo.cuerpo», verificación en tiempo constante con tolerancia de 5 min, espera 1 min / 5 / 30 / 2 h / 12 h, cuerpo mínimo.
- **Eventos:** efectos de estatus (`WebhookOrderEffect`, `WebhookInvoiceEffect`) anotan las salidas en la misma transacción del cambio (si el cambio se deshace, la salida también). Solo de integraciones API encendidas del cliente, con el webhook activo, no suspendido y suscrito.
- **Entrega:** trabajo `WEBHOOK_DELIVERY` (cada minuto; los reintentos no esperan menos de 1 min de todos modos): envía en paralelo entre webhooks (8 a la vez) y en orden dentro de cada uno, 10 s de límite por intento, no sigue redirecciones, revisa la dirección ya resuelta (contra rebote por DNS) y, si un webhook falla 3 veces seguidas en una vuelta, no insiste con el resto. Éxito → DELIVERED; agotados 6 intentos → UNDELIVERED; 5 no entregadas seguidas → webhook **suspendido** + aviso del sistema (evento `INTEGRATION_WEBHOOK`, a quienes se definieron en la integración). Purga de salidas a los 30 días.
- **API del cliente** (`/api/v1/webhooks`, alcance `webhooks:manage`, solo su integración; el único DELETE de la API es «quitar»): crear (secreto una vez), listar, ver, editar, quitar, probar (`webhook.ping`), rotar secreto, ver sus salidas.
- **Personal:** `/api/v1/integrations/{id}/webhooks…` (+ reactivar) y `/api/v1/integrations/outbox` (listar, reintentar, descartar; `integrations.inbox`).
- **Web:** acción «Webhooks» (modal), pestaña «Salidas» con contador, es/en.

## Cómo se probó
- xunit `WebhookRulesTests` (URL permitida/rechazada, direcciones internas, eventos, firma y repetición, reintentos, cuerpo, ruta del DELETE) y suite completa.
- vitest de reglas (estado, eventos, tonos) y las de Integraciones.
- Playwright `lote136-webhooks.spec.ts` (escritorio y móvil) contra el API real y un receptor HTTP: suscripción por API, secreto una vez, firma verificada con HMAC (y falla con otro secreto), solo salen eventos del cliente, rotar, 6 intentos con espera adelantada por SQL hasta «No entregada», suspensión a la 5.ª, reintento bloqueado hasta reactivar, reintentar/descartar, quitar (404 después), alcance y aislamiento entre integraciones; y el recorrido de pantalla con capturas.

## Decisiones a revisar
1. **Salida de archivos por evento u horario (SFTP, correo, API del cliente) pasa al Lote 137**, porque necesita el mapeo de salida; los webhooks cubren la salida por evento.
2. **La entrega corre cada minuto** (mínimo del programador de trabajos): un aviso nuevo sale en ≤ 1 min. Para inmediatez habría que enviar al momento del cambio de estatus.
3. **«Una entrega que salta estatus manda un `order.status_changed` por cada estatus»** (es lo que hace el motor de estatus); el cliente que solo quiera el final se suscribe a `order.delivered`.
4. **Sin garantía de orden entre eventos tras un reintento**: se manda en orden por webhook, pero un fallo se reintenta después de los siguientes. El `id` y `createdAt` permiten reordenar.
5. **Suspensión a las 5 salidas no entregadas seguidas** (cada una tras 6 intentos ≈ 14 h): un servidor caído un día entero sí suspende. Es ajustable (`WebhookRules.SuspendAfterUndelivered`).
6. **El aviso de suspensión** solo llega si la integración tiene a quién avisar; las integraciones API creadas antes del Lote 136 sin editar no tienen la regla hasta que se guarden.
7. **No se prueba contra https real ni TLS**: el receptor de prueba es http en 127.0.0.1 (con `AllowInsecure`); la validación de URL y direcciones internas está cubierta por xunit.
