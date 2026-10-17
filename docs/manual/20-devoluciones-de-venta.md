# 20 — Devoluciones de venta

Lote 81 (P6, 2026-10-16). El cliente devuelve parte de lo que se le vendió. La **devolución** (`DV-#####`, un contador por compañía) pide la mercancía de vuelta, la autoriza un supervisor, el almacén la **recibe** (en partes, con lo bueno y lo dañado) y, si ya se había facturado, se **acredita** con una nota de crédito ([capítulo 19 §4d](19-facturacion.md)). Es parte del módulo **Órdenes de venta** ([capítulo 18](18-ordenes-de-venta.md)).

## 1. Quién puede y dónde

- Módulo **Órdenes de venta** (`SALES_ORDERS`). API: `/api/v1/sales-returns`.
- **Ver** (lista, ficha, lo devolvible, la ventana): `sales.view`.
- **Pedir y cancelar:** `sales.return` (permiso nuevo; se dio a los roles que ya crean órdenes de venta y al administrador de la compañía, también en los roles ya clonados).
- **Autorizar, rechazar y cambiar la ventana de devolución:** `sales.return.authorize` (permiso nuevo; administrador de la compañía y roles que ya cancelan órdenes de venta). También el único que puede pedir una devolución **fuera de la ventana**.
- **Recibir:** `warehouse.receive` (el mismo del recibo). **Acreditar:** `billing.credit`.

## 2. El flujo

**Solicitada → Autorizada → Recibida → Acreditada**, o **Rechazada** / **Cancelada**.

1. **Pedir** (`POST /sales-returns`): la orden de venta, el **motivo** (dañado, equivocado, no lo necesitaba o sobrante, vencido, otro) y las líneas con la cantidad. Por línea se puede devolver hasta **lo despachado − lo ya devuelto − lo que ya piden otras devoluciones abiertas** (`GET /sales-returns/returnable?salesOrderPublicId=` lo muestra). Los **kits** y los productos **con serie** no se devuelven en esta versión.
2. **Ventana de devolución:** `SalesReturnWindowDays` (30 por omisión; 0 = sin ventana; `GET/PUT /sales-returns/settings`). Se cuenta desde el **último despacho** de la orden. Fuera de la ventana solo la pide quien tiene `sales.return.authorize` y debe confirmarlo (`confirmOutsideWindow`); la devolución queda marcada «fuera de la ventana».
3. **Autorizar** (`POST .../authorize`, con una nota: cómo regresa la mercancía) o **Rechazar** (`POST .../reject`, con motivo). **Cancelar** (`POST .../cancel`) una solicitada o autorizada que todavía no se ha recibido; lo que pedía vuelve a estar disponible.
4. **Recibir** (`POST .../receive`): por línea **GoodQty** (bueno: vuelve al inventario) y **DamagedQty** (dañado; la descripción del daño es obligatoria; `damageDiscard` lo desecha de una vez; si no, queda en cuarentena). Se puede recibir **en partes**. Reutiliza el recibo: crea y **confirma un recibo tipo RETURN** (en una sola transacción) con la devolución como referencia: el inventario lo mueve ese recibo (acomodo o posición destino `targetBinId`, y `lot` si el producto lleva lote); lo dañado genera su **reporte de daño** con la causa nueva **«Devolución de cliente»** (cuarentena y luego dar salida o recuperar). Lo recibido (bueno + dañado) cuenta como **devuelto** en la línea de la orden de venta (`QtyReturned`) y deja de ser facturable. Con `closeRest` lo que no llegó se da por no devuelto (deja de contar como pendiente).
5. **Acreditar** (`POST .../credit`): crea una **nota de crédito en borrador por factura** con lo recibido que **sí se facturó** (motivo Devolución, al monto unitario facturado; solo facturas emitidas, vencidas o pagadas: una factura en borrador se ajusta en su corrida) y pasa la devolución a **Acreditada**. Lo recibido que **no** se había facturado simplemente ya no se facturará. La nota se aprueba y se aplica como cualquier otra (capítulo 19).

## 3. Validaciones y mensajes

| Situación | Mensaje | HTTP |
|---|---|---|
| Sin orden / sin motivo / motivo inexistente | `Indique la orden de venta de la devolución.` / `Indique el motivo de la devolución.` / `El motivo de la devolución no existe.` | 400 |
| Sin líneas / más de 200 / nota larga | `Agregue al menos una línea a la devolución.` / `La devolución no puede tener más de 200 líneas.` / `La nota no puede pasar de 500 caracteres.` | 400 |
| Línea que no es de la orden / repetida / cantidad inválida | `La línea no es de esa orden de venta.` / `Una línea de la orden está repetida en la devolución.` / `La cantidad debe ser mayor que cero (hasta 3 decimales).` | 400 |
| Orden sin nada despachado | `Solo se devuelve de una orden de venta despachada.` | 422 |
| Más de lo que se puede devolver | `De X solo se pueden devolver Y más.` | 422 |
| Kit / producto con serie | `Las líneas de kit no se devuelven en esta versión.` / `Un producto con serie no se devuelve en esta versión.` | 422 |
| Fuera de la ventana sin permiso o sin confirmar | `La devolución está fuera de la ventana de devolución: la autoriza quien tiene permiso para ello.` | 403 |
| Autorizar / rechazar una que no está solicitada | `Solo se autoriza una devolución solicitada.` / `Solo se rechaza una devolución solicitada.` | 422 |
| Rechazar sin motivo | `Escriba el motivo para rechazar la devolución.` | 400 |
| Cancelar una ya recibida o cerrada | `Solo se cancela una devolución solicitada o autorizada que no se haya recibido.` | 422 |
| Recibir una que no está autorizada | `Solo se recibe una devolución autorizada.` | 422 |
| Recibir sin líneas / línea ajena / repetida | `Indique qué línea se recibe y cuánto.` / `La línea no es de esa devolución.` / `Una línea de la devolución está repetida en lo que se recibe.` | 400 |
| Cantidades inválidas | `Las cantidades buenas y dañadas deben ser cero o más (hasta 3 decimales) y al menos una mayor que cero.` | 400 |
| Dañado sin descripción | `Describa el daño de lo que llegó dañado.` | 400 |
| Recibir más de lo que falta | `De X solo faltan por recibir Y.` | 422 |
| Acreditar una que no está recibida | `Solo se acredita una devolución recibida.` | 422 |
| Nada por acreditar | `No hay nada por acreditar: lo devuelto no se había facturado (ya no se facturará) o ya se acreditó.` | 422 |
| Ventana de días fuera de 0–3650 | `Los días de la ventana de devolución deben estar entre 0 y 3650.` | 400 |
| Devolución u orden inexistente | `Devolución no encontrada.` / `Orden de venta no encontrada.` | 404 |

## 4. Para soporte

- Tablas `SalesReturn` y `SalesReturnLine`; columnas `Tenant.SalesReturnWindowDays`, `ReceiptHeader.SalesReturnId` (el recibo tipo RETURN de cada recepción) y `CreditNote.SalesReturnId`; sección **2026-10-16 (w)** de `Diseño/logistica-db-update.sql` (también el dominio de estatus `SalesReturnStatus`, el catálogo `ReturnReason`, la causa de daño `CUSTOMER_RETURN`, el EntityType `SALES_RETURN`, el contador `SALESRETURN` y los permisos).
- El inventario lo mueve el **recibo** (Kárdex con el recibo de origen); lo dañado, el reporte `DAN-#####`. La devolución solo anota lo devuelto (`SalesOrderLine.QtyReturned`) y lo acreditado (`QtyCredited`).
- En las pruebas con base en memoria (no en SQL real) los servicios anidados limpian lo rastreado: el servicio vuelve a cargar lo suyo después de crear el recibo y cada nota.

## 5. En el aparato: recibir una devolución (Lote 83)

El almacén recibe la devolución con la app: **Inicio → Devoluciones** (ver [09 §13](09-app-almacen.md#13-recibir-una-devolucion-de-venta-lote-83)). El aparato baja las devoluciones **autorizadas** (o recibidas en parte con algo pendiente) con `GET /api/v1/sync/sales-returns` (módulo `SALES_ORDERS` y `warehouse.receive`; llegan todas las abiertas y, con `since`, las ya recibidas del todo, acreditadas o canceladas con `isActive = false` para que el aparato las borre). Por línea se anota lo **bueno** y lo **dañado** (con qué le pasó, y desechar o no), y el **lote** si el producto lo lleva; al enviar llama al mismo `POST /sales-returns/{id}/receive` de arriba, con la clave de idempotencia de la cola (un doble envío no duplica el recibo). Desde la web se puede recibir igual (L82).

Lo que el aparato **no** hace todavía: elegir la posición destino de lo bueno (la decide el servidor según el modo de recepción del almacén), capturar la fecha de vencimiento del lote ni recibir kits o series (tampoco se devuelven).
