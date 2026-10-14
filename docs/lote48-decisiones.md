# Lote 48 — «Imprimir los labels automáticamente» por cliente y recogida en cualquier dirección del cliente (2026-10-14)

Servidor del lote F-B5 ([loteFB5-decisiones.md](frontend/loteFB5-decisiones.md)).

## Qué cambió

| Pieza | Detalle |
|---|---|
| `dbo.Client.AutoPrintLabels` | Columna nueva `BIT NOT NULL DEFAULT 0` en la sección «2026-10-14 (d)» de `logistica-db-update.sql` (idempotente, al final). `Client.AutoPrintLabels` en el dominio; `ClientDetailDto.AutoPrintLabels` y `ClientProfileUpdateRequest.AutoPrintLabels` (null = no cambia; no arrastra a `AutoPrintInvoice`). |
| Punto de recogido por defecto | `Client.DefaultPickupLocationId` ya puede apuntar a la dirección **física** (CORPORATE) o **postal** (BILLING) del cliente, no solo a un punto PICKUP/BOTH. `ClientService.GetAsync` la reconoce y `LocationService.ReconcileDefaultPickupAsync` solo la limpia si deja de servir. Mensaje nuevo (400): «El punto de recogido debe ser una dirección activa del cliente: la física, la postal o un punto de recogida.» (antes pedía un almacén PICKUP o BOTH). Sin cambio de esquema. |
| Contrato | `web-app/openapi.json` (`autoPrintLabels` en los dos esquemas; el resto del Swagger real es idéntico) y `schema.d.ts` de la web y de la app regenerados. |

## Pruebas
`ClientAutoPrintLabelsTests` (la bandera apagada por omisión en la entidad, el detalle y la solicitud; la sección de update.sql, idempotente y sin DROP/DELETE) y el área SQL/clientes/localizaciones: 207 en verde. `scripts/smoke.sh`: pasos nuevos (recoger en la postal y volver a la física; bandera de labels apagada por omisión, se enciende sin tocar la de factura).

## Para revisar
- La bandera guarda la preferencia **del cliente**; quien imprime es la pantalla de órdenes (ver el lote web). Con la bandera encendida pero sin impresora de labels en la estación, la orden se guarda y se avisa.
- No hay prueba automática del servicio completo de perfil (el recorrido real es el smoke); no se corrió el smoke completo en este lote (regla de pruebas del área): hay que correrlo antes de pasar a producción.
