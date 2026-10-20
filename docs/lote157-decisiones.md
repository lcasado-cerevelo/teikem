# Lote 157 — Avisos de llegada: eventos, edición y vencimiento del lote (2026-10-20)

## Qué se construyó
- Eventos de aviso `ASN_REQUESTED`, `ASN_UPDATED`, `ASN_RECEIVED`, `ASN_CANCELLED` (como los del aviso de salida; opcionales, sin reglas sembradas; los avisos nacidos de una orden de compra no publican).
- Edición de un aviso de llegada de cliente esperado y sin recibo abierto: `PATCH /api/v1/asns/{id}` (almacén, referencia, fecha, líneas) y «Editar» en la lista web; mismas reglas del alta (validación extraída a un método compartido).
- `AsnLine.ExpiryDate` (SQL idempotente): el vencimiento viaja con el lote y el lote nace con esa fecha al recibir. En la web el lote y el vencimiento solo salen en líneas de productos por lote (la línea trae `trackingType`); en la app del cliente también.

## Cómo se probó
`AsnUpdateTests` (editar, campos sin cambio, cancelado/ajeno, vencimiento con y sin lote, eventos), contratos posicionales y mapa de seguridad de controladores; suite .NET completa y `npm run check`.

## Decisiones a revisar
- El portal del cliente puede crear y cancelar avisos de llegada pero todavía no editarlos (solo el personal edita).
- «Despachado» del aviso de llegada se llama «recibido» (es el equivalente a la salida despachada).
- Editar reemplaza todas las líneas; no hay historial de líneas anteriores más allá de la auditoría.
