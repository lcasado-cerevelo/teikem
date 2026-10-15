# Lote 62 — Entrega y deshacer un despacho de una orden de venta (P4, S2 / servidor) (2026-10-15)

Sigue al Lote 61 ([lote61-decisiones.md](lote61-decisiones.md)); diseño en [p4-s2-despacho-diseno.md](p4-s2-despacho-diseno.md).

## Qué se construyó
- **Entrega** (`POST /sales-orders/{id}/delivery`): empaca la recolección con la misma operación del empaque de hoy (crea la orden de transporte real) y la enlaza con la orden de venta (`TransportOrder.SalesOrderId`, update.sql **(m)**). Cliente y consignatario salen de la orden de venta si no se mandan. Una entrega por despacho.
- **Deshacer un despacho** (eliminar su recolección): un gancho (`ISalesDispatchReversal`, que implementa `SalesOrderService` y llama `PickBatchService` dentro de la misma transacción, con parámetro opcional para no romper nada) baja lo despachado de cada línea y **vuelve a reservar** en la misma posición y lote; una orden despachada o parcial se **reabre**.
- La orden muestra sus **despachos** (`dispatches`): número, estatus (despachado / con entrega / deshecho), cantidad y su orden de transporte.
- **Ajuste de estatus (update.sql (n))**: el motor de estatus solo deja avanzar de una etapa del pipeline a la siguiente y un terminal no admite más transiciones. Para poder reabrir y para saltar de solicitada a despachada, **«Despachada parcial» y «Despachada» pasaron a ser etapas laterales** (se puede entrar a ellas desde solicitada y volver a solicitada). Lo encontraron las pruebas de la reversa.
- Manual (capítulo 18, sección 11) y FAQ.

## Pruebas
- xunit (4): deshacer el único despacho (existencia devuelta, orden solicitada, reservada otra vez y se puede volver a despachar), deshacer uno de dos (parcial, solo esa parte reservada), una recolección normal no se ve afectada por el gancho y las validaciones de la entrega con su mensaje.
- **Contra SQL Server real** (API): despacho → entrega (crea ORD-00001 enlazada, el despacho pasa a PACKED) → segunda entrega rechazada (422) → segundo despacho (SHIPPED) → eliminar esa recolección (204) → la orden vuelve a PARTIAL con 4 despachadas y 2 reservadas; el inventario cuadró (en mano 6, reservado 2).
- `db-update --dry-run` contra Teikem_verif: OK.

## Decisiones por omisión (a revisar con Luis)
1. **El retiro no tiene operación propia**: un despacho sin entrega ES el retiro (queda como «despachado»). Si se necesita constancia del retiro (firma, quién recogió) se diseña aparte.
2. **Una entrega por despacho**; para entregar por partes se despacha por partes.
3. **Deshacer una recolección de una orden cancelada** devuelve la existencia pero no vuelve a reservar.
4. El enlace entre la orden de transporte y la de venta se guarda **después** de empacar, en la misma transacción.
