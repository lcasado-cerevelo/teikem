# Lote 64 — Órdenes por despachar para el aparato (servidor) (P4, S2) (2026-10-15)

Primera mitad de la parte de la app del diseño [p4-s2-despacho-diseno.md](p4-s2-despacho-diseno.md); la app (Lote 65) la usa.

## Qué se construyó
- `GET /sync/sales-orders`: órdenes abiertas con líneas y **reservas por posición/lote** (la guía del despacho). Siempre trae todas las abiertas; con `since`, además las cerradas (`isActive = false`).
- `POST /sales-orders/{id}/dispatch` acepta `delivery`: **despacho + entrega en una sola transacción** (`SalesDeliveryService.DispatchAndDeliverAsync`), porque el aparato despacha sin señal y no conoce el número de la recolección.
- Manual (capítulo 18, sección 12) y FAQ.

## Pruebas
- xunit: la sincronización (abiertas con reservas por posición, borradores fuera, despachadas como inactivas con `since`, carga completa solo abiertas, aislamiento entre compañías, módulo y permiso del endpoint) y la prueba de rutas del `SyncController` (ahora con `sales-orders`).
- **Contra SQL Server real**: la sincronización devuelve las reservas por posición; despacho con entrega crea la orden de transporte y deja el despacho «Con entrega»; con una entrega inválida (servicio inexistente) el despacho **no ocurre** (la orden queda con 4 despachadas y 2 reservadas, como antes).

## Decisiones por omisión (a revisar con Luis)
1. **El aparato recibe todas las órdenes abiertas en cada sincronización** (no solo las cambiadas): sus líneas y reservas cambian sin tocar el encabezado y no se auditan.
2. Una orden **sin nada reservado** deja de ir al aparato (no hay qué despachar), aunque siga solicitada con backorder.
