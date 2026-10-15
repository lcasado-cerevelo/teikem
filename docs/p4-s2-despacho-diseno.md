# P4 · S2 — Despachar contra una orden de venta (diseño acordado con Luis, 2026-10-15)

Decisiones de Luis (todas las recomendadas):
1. **Posición y lote guiados, con cambio permitido**: el aparato propone la posición/lote que reservó el sistema; si el operario toma de otra posición con saldo, el sistema **mueve la reserva** a esa posición; si no hay saldo, avisa.
2. **Al terminar el despacho en el aparato**: **Retiro / Entrega / Dejar para después**. Retiro cierra ahí; Entrega pide pocos datos (consignatario, servicio, paquetes, COD) y crea la orden de transporte (como el empaque de hoy); Dejar para después permite crear la entrega luego desde la web.
3. **Sin señal se puede despachar**: la cola del aparato y validación del servidor al sincronizar (si la orden cambió o se canceló, se rechaza con su mensaje y queda en pendientes).
4. **Orden con todas las líneas cerradas con faltante y nada despachado**: **se cancela sola**, con nota en el historial.

## Servidor
- Columnas opcionales: `PickBatch.SalesOrderId`, `PickBatchLine.SalesOrderLineId`, `TransportOrder.SalesOrderId`.
- `POST /sales-orders/{id}/dispatch` (permiso `warehouse.pick`): una transacción que consume la reserva, saca el inventario por el ledger, crea la recolección enlazada, sube `QtyShipped`, baja `QtyReserved` y pasa la orden a `PARTIAL` / `SHIPPED`. Nunca despacha más de lo reservado; por partes.
- Una línea está **terminada** cuando no le queda reserva ni backorder; la orden es `SHIPPED` cuando todas lo están (con algo despachado) y `CANCELLED` si ninguna despachó.
- Deshacer una recolección devuelve la existencia, baja `QtyShipped` y vuelve a reservar si la orden sigue abierta.
- Entrega: crea la `TransportOrder` enlazada (`SalesOrderId`) con los datos del empaque.

## Lotes
61 despacho (servidor) · 62 deshacer + entrega · 63 web (botón Despachar) · 64 sincronización y app (órdenes por despachar, guía por posición, cola sin señal).
