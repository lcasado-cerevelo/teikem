# Lote 68 — Kits en el catálogo (P4-T, 1 de 3) (2026-10-16)

Primer lote de los kits del diseño de Truss ([plan P4-T](plan-de-desarrollo-pendiente.md)). Los siguientes: **69** orden de venta con kits (reserva por componente, foto de componentes
por línea, «ajustable al pedir») y **70** despacho/deshacer/aparato con kits y la explosión al recolectar.

## Qué se construyó
- **Datos:** `Product.ProductTypeLookupId` (catálogo `ProductType`: STANDARD / KIT; NULL = normal), `Product.KitAdjustable` y la tabla `ProductKitComponent` (kit → componente → cantidad, único por par). Sección `2026-10-16 (p)` de `logistica-db-update.sql`.
- **`KitService`** + `GET/PUT /products/{id}/kit`: convertir en kit con componentes, reemplazarlos, volver a producto normal; «se pueden armar N» por almacén con lo disponible para vender (`SalesOrderService.SellableAsync`, la misma cuenta de Solicitar).
- **El kit no tiene existencia propia:** el ledger rechaza recibir/mover un kit (`InventoryLedger.PostAsync`); no se puede convertir un producto con existencia o reservas ni que ya sea componente.
- **Web:** pestaña **Kit** en la ficha del producto (convertir, editar componentes, ajustable, volver a normal, «se pueden armar N» por almacén) y distintivo «Kit» en la lista y la ficha.
- Manual 06 §8b y FAQ.

## Pruebas
- xunit (11): `CanBuild`, kit con componentes y disponibilidad por almacén, reemplazo y vuelta a normal, todos los mensajes de validación, existencia/componente/uso en ventas, ledger, aislamiento por compañía, y las pruebas de cobertura (entidades WMS, auditoría, permisos del controlador, firma del DTO). **Suite completa verde contra SQL Server real** (4102 pruebas, 0 omitidas).
- Web (vitest, 12): reglas del editor y la pestaña (producto normal, con y sin permiso, con rastreo, validación, cuerpo enviado, mensaje del servidor, kit con componentes, volver a normal).

## Decisiones por omisión (a revisar con Luis)
1. **Sin kits dentro de kits** y componentes del **mismo dueño** de inventario; máximo 50 componentes.
2. **Un producto con existencia o reservas no se convierte en kit**; uno que ya se usó en órdenes de venta no vuelve a normal.
3. El kit **no puede rastrearse** por lote o serie.
4. **Recibir un kit se rechaza en el ledger** (al confirmar), no al agregar la línea del recibo; queda para pulir si hace falta.
5. «Se pueden armar N» muestra todos los almacenes activos (con 0 si no alcanza).
6. Los componentes se auditan con el tipo de entidad del producto (como los lotes).

## Fuera de este lote
- Orden de venta con kits, recolección con explosión, aparato (Lotes 69–70). Hasta entonces un kit en una orden de venta se comporta como un producto sin existencia (queda en backorder).
