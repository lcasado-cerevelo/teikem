# Empaque por producto — decisiones (2026-10-09)

## Qué se construyó
- **Servidor/SQL**: `Product.PackUomLookupId` y `PackQty` (nulos juntos, `CK_Product_Pack`), catálogo *Unidad de medida* con Paquete, Barril, Bulto, Galón, Tambor, Rollo; alta/edición con validaciones y `ClearPack`; DTO de lista/detalle, sync de productos y fuente de datos con `PackUom`/`PackQty`.
- **Web**: campos en el editor, columna *Empaque*, tooltip del Disponible, calculadora del conteo con unidad por bloque y empaques sueltos.
- **App**: migración local v9 (columnas del producto; reinicia la marca de agua de productos), calculadora con selector Unidades|Empaque en Conteo y Recibir, y «= N Caja + M sueltas» al recibir.

## Cómo se probó
`dotnet test` (3298), `npm run check` web (1389), `npx jest` app (559) y `tsc`. No se probó contra SQL Server (el `db-init` de CI es el árbitro) ni en el Zebra.

## Decisiones a revisar
1. Un solo empaque por producto (pedido del dueño). El inventario sigue en unidades base.
2. El empaque se puede cambiar con movimientos (no altera lo guardado).
3. Fuera de alcance por ahora: captura en cajas en Despacho, ajustes y transferencias; recibo web por cajas; «Caja de 12» en la etiqueta.
