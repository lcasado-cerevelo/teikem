# Lote 147 — Capa de costo por línea de recibo (pedido de Luis, 2026-10-20)

## Qué se construyó
- **La línea del recibo ES la capa.** Al confirmar un recibo, cada línea con algo recibido congela su **costo unitario** (`ReceiptLine.UnitCost`) y su **origen** (`CostSource`: `PO` | `ASN` | `PRODUCT`). Prioridad: **costo de la línea de la orden de compra > precio de la línea del aviso de llegada > costo del producto (`Product.PurchaseCost`)**; sin ninguno, la capa queda sin costo. Una vez congelado no se vuelve a tocar: **no hay sincronización en ningún sentido** (cambiar después el costo del producto, el precio del aviso o el costo de la orden no cambia una capa).
- **Precio opcional en el aviso de llegada** (`AsnLine.UnitPrice`, DECIMAL(18,4), ≥ 0, hasta 4 decimales): solo se captura en avisos **de un cliente** (el alta web de avisos de llegada es siempre de un cliente); en uno nacido de una orden de compra manda el costo de la orden. Error 400 `lines[i].unitPrice` con «El precio unitario debe ser cero o mayor y de 4 decimales como máximo.» (o «…excede el máximo permitido.»).
- **DTO:** `ReceiptLineDto.UnitCost` muestra la capa congelada (antes, en un recibo de orden de compra, solo el costo de la PO) y `CostSource` (campo nuevo al final); `AsnLineRequest/AsnLineDto.UnitPrice` (campo nuevo al final).
- **Web:** columna **Costo unitario** (con el origen en el tooltip) en las líneas de un recibo ya confirmado; campo **Precio unitario (opcional)** por línea en «Nuevo aviso de llegada».
- SQL idempotente al final de `Diseño/logistica-db-update.sql`: columnas nuevas y `CK_AsnLine_UnitPrice` / `CK_ReceiptLine_Cost`. Los recibos ya confirmados quedan **sin capa** (no se inventa costo histórico).

## Cómo se probó
- xunit `CostLayerTests` (11): prioridad y casos límite de la regla, precio válido/inválido, precio del aviso congelado con origen ASN, costo del producto congelado y no afectado por un cambio posterior, costo de la orden de compra por encima del del producto, línea sin nada recibido sin capa, 400 con la clave de la línea; contratos posicionales actualizados. Suite completa .NET verde.
- vitest: el recibo confirmado muestra la columna de costo con su origen; `npm run check` verde.

## Decisiones a revisar
1. **Todavía no se consume la capa.** El costo congelado de una salida (`PickBatchLine.UnitCost`, venta, aviso de salida) sigue siendo `Product.PurchaseCost`. El siguiente paso (valuación) decide si salen por **capas (FIFO/lote)** o a **costo promedio**, y con él el reporte «valor del inventario».
2. Un recibo de **devolución** o **a ciegas** también congela capa (con el costo del producto si no hay documento).
3. La app de almacén no captura precio (el aviso de llegada se crea en la web).
4. Los recibos anteriores a este lote no tienen capa; la valuación tendrá que decidir cómo tratarlos (costo del producto).
