# Lote 83 — Recibir devoluciones de venta en el aparato (P6, 5 de 5) (2026-10-16)

Último lote de P6: el almacén recibe en la app la devolución autorizada en la web (Lotes 81 y 82), con lo bueno y lo dañado.

## Qué se construyó
- **Servidor:** `GET /api/v1/sync/sales-returns` (`SyncController`, módulo `SALES_ORDERS`, permiso `warehouse.receive`): las devoluciones autorizadas o recibidas en parte con pendiente del almacén, con sus líneas (producto, tipo de seguimiento NONE/LOT, pedido, recibido, dañado, pendiente); con `since` llegan también las cerradas o canceladas como `isActive = false`. `SyncService.SalesReturnsAsync`.
- **App (Expo):** esquema 16 (`sales_return`, `sales_return_line`); bajada completa en cada sincronización (`downloadSalesReturnsIfAllowed`, un 403 se salta y se borra lo de antes); pantalla `/sales-return` (lista, escaneo del número DV-…, recepción por línea con bueno, dañado, descripción del daño, desechar, lote y «cerrar lo pendiente»); botón **Devoluciones** en Inicio (solo con devoluciones por recibir y `warehouse.receive`); cola `salesReturnReceive` con efecto local inmediato y reaplicado tras cada bajada; rechazo del servidor con su mensaje exacto.
- `openapi.json` y los clientes generados (web y app); textos es/en.
- Manual: 09 §13, 20 §5, FAQ y este documento.

## Cómo se probó
- xunit (`SalesReturnTests`): el endpoint de sync (solicitada no sale; autorizada sí con su pendiente; recibida en parte sigue; recibida del todo sale y con `since` llega inactiva; otra compañía no ve nada; módulo y permiso del endpoint).
- Jest (app, 746 pruebas, `npm run check` completo): lógica pura (validación y cuerpo), copia local (buscar, reemplazar, recibir en parte y del todo, reaplicar la cola), bajada (reemplazo y 403), pantalla completa (todo bueno con lote, daño con descripción y desecho, tope de lo pendiente, sin señal, rechazo del servidor, número inexistente).

## Decisiones a revisar con Luis
1. **No se proyecta la existencia local**: el servidor decide dónde entra lo recibido (modo de recepción del almacén); la existencia llega con la siguiente bajada de saldos.
2. **Sin posición destino ni vencimiento del lote** en la app: lo bueno entra como cualquier recibo (acomodo o directo según el almacén); si un producto exige vencimiento, el servidor rechaza con su mensaje y se recibe desde la web.
3. **Sin borrador:** la recepción se captura y se envía de una vez (no bloquea los demás botones de Inicio).
4. **Permiso:** la bajada y el botón usan `warehouse.receive` (el mismo de recibir); no hace falta `sales.view`.

## Fuera de este lote
- Prueba en el aparato real (Zebra) y recorrido Maestro; avisos y análisis de devoluciones; kits y series en devoluciones.
