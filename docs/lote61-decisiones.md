# Lote 61 — Despachar contra la orden de venta (P4, S2 / servidor) (2026-10-15)

Diseño acordado con Luis: [p4-s2-despacho-diseno.md](p4-s2-despacho-diseno.md).

## Qué se construyó
- `POST /sales-orders/{id}/dispatch` (`warehouse.pick`): una transacción que consume la reserva, saca el inventario por el ledger (movimiento de salida con la recolección como referencia), crea la **recolección enlazada** (`PickBatch.SalesOrderId`, `PickBatchLine.SalesOrderLineId`, costo congelado) y sube `QtyShipped` / baja `QtyReserved`. Por partes; nunca más de lo reservado.
- **Posición y lote guiados con cambio permitido**: sin posición sale de lo reservado; con otra posición la reserva se mueve (se reserva ahí con el candado del ledger y se libera lo equivalente de las demás).
- **Estatus**: línea terminada = sin reserva ni backorder; orden `PARTIAL` al despachar parte, `SHIPPED` cuando todas están terminadas, **`CANCELLED` sola** si todas se cerraron con faltante sin despachar nada (con nota en el historial). «Cerrar con faltante» ahora también refresca el estatus.
- update.sql sección **2026-10-15 (l)** (las dos columnas, con sus índices). Manual (capítulo 18, sección 10) y FAQ.

## Pruebas
- xunit (8): despacho completo (inventario, reserva, recolección enlazada, movimiento, estatus), por partes con reserva repartida entre dos posiciones, mover la reserva a otra posición, posición sin saldo / no vendible / inexistente, todas las validaciones con su mensaje, backorder pendiente (parcial hasta cerrar la línea), orden vacía que se cancela sola y aislamiento entre compañías.
- **Contra SQL Server real**: despacho desde otra posición, intento de despachar de más (409) y despacho final → `SHIPPED`; inventario en mano y reservado cuadraron con las filas de reserva. **Encontró un error que la base simulada no ve**: una fila de reserva con cantidad cero violaba `CK_SalesOrderReservation_Qty` cuando el ledger guardaba en medio; ahora la fila vacía se borra al instante.
- `db-update --dry-run` contra Teikem_verif: OK.

## Decisiones por omisión (a revisar con Luis)
1. El despacho usa el **contador de recolecciones (EMP-#####)**; no hay un contador propio de despachos de venta.
2. La recolección de una venta nace **COLLECTED** (como cualquier recolección); el retiro/entrega (lote 62) decide si se empaca.
3. Un despacho **no exige** que se despache todo lo reservado. Al tomar de **otra posición**, si el producto maneja lotes hay que indicar también el lote (sin lote se busca el saldo sin lote); sin posición se usa lo reservado tal cual.
4. **Deshacer** la recolección (volver a reservar) y la **entrega** (orden de transporte) quedan para el lote 62.
