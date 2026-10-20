# Lote 142 — Dos métodos de rotación: con lote y sin lote (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Dos campos de la compañía** en «Rotación del inventario»: `rotationMethod` (productos con lote: FEFO o FIFO, FEFO por omisión, sin cambio) y `rotationMethodNoLot` (productos sin lote ni series: FIFO o **por posición**, FIFO por omisión). Las series se tratan como sin lote (no expiran).
- **Nuevo método `POSITION`:** sin fechas; manda la zona y el código de la posición (el desempate de siempre).
- **Excepción por producto (se mantiene):** acepta FEFO/FIFO si el producto lleva lote y FIFO/POSITION si no; una que no aplica a su tipo se ignora al resolver y manda la de la compañía para ese tipo. La ficha solo ofrece las que aplican y limpia la que dejó de aplicar al cambiar el tipo.
- **Un solo lugar decide:** `RotationRules.Resolve(productMethod, CompanyRotation, lotTracked)` lo usan la recolección, el despacho manual, la reserva de órdenes de venta (incluidos kits), el reabasto y el orden de salida del aparato. La app de almacén no cambió (sigue el orden del servidor).
- `Diseño/logistica-db-update.sql` (sección nueva, idempotente): `Tenant.RotationMethodNoLot` con CHECK y ampliación del CHECK de `Product.RotationMethod` para admitir POSITION.

## Cómo se probó
- xunit: reglas puras (`AppliesTo`, `Resolve` por tipo, orden por posición con zonas), servicio (producto sin lote sale FIFO por omisión aunque otra posición tenga menor código; la compañía puede pedir por posición; el método de lote no afecta al de sin lote; excepción por producto; excepción que no aplica se ignora), ajustes (validación con mensajes exactos), alta/edición de producto (excepción según tipo) y la prueba de contrato de `TenantSettings*`.
- vitest: ajustes de la compañía (segundo selector sin FEFO, cuerpo del PUT) y ficha del producto (opciones según el tipo).

## Decisiones a revisar
1. **Cambio de comportamiento en producción:** los productos sin lote pasan de salir por código de posición a salir por antigüedad (FIFO). Elegir «Por posición» devuelve lo anterior.
2. Una excepción de producto guardada que ya no aplica a su tipo (p. ej. FEFO en un producto sin lote) **no se borra** de la base: solo se ignora. Se limpia cuando se edita el producto.
3. La réplica de FEFO que la web usa como respaldo cuando el usuario no tiene `inventory.view` no cambió (la propuesta normal sale del servidor).
4. **Pendiente para el lote del costo:** el aviso de llegada con precio opcional por producto.
