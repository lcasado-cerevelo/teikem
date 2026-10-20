# Lote 155 — Precio del producto en documentos y aviso «productos con existencia sin costo» (2026-10-20)

## Qué se construyó
- `ProductPriceRules` (dominio, puro): el producto sin costo toma el de la primera línea con precio; con otro costo solo cambia con «actualizar» (`inventory.manage`, 403 si no); igual no se toca.
- Aviso de llegada y orden de compra: `UpdateProductPrice` por línea; la web precarga el costo del producto, muestra el aviso ⓘ si el precio escrito difiere y la casilla «Actualizar el precio del producto con este» (componente `ProductPriceNote`).
- Aviso de inventario `INVENTORY_NO_COST` («Productos con existencia sin costo»): productos activos con existencia y costo vacío o cero; un aviso con el conjunto, sin repetirse en la semana; lo publica el trabajo diario ya existente. Opcional: las reglas de destino las arma el administrador.

## Cómo se probó
Pruebas unitarias y de servicio (reglas, aviso de llegada, orden de compra, aviso sin costo, contratos posicionales); `npm run check` completo.

## Decisiones a revisar
- En la orden de compra, las líneas ya existentes no muestran el aviso de diferencia al editar (solo las nuevas).
- Solo cuenta el precio escrito explícitamente; si se omite, la línea ya usa el costo del producto.
- La moneda se asume la de la compañía; no se convierte.

## Fuera de este lote
Lote E del portal (crear productos, avisos de llegada y de salida desde el portal del cliente).
