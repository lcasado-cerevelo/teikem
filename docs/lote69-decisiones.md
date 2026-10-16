# Lote 69 — Kits en las órdenes de venta (P4-T, 2 de 3) (2026-10-16)

Usa los kits del Lote 68. Falta el **Lote 70**: despacho de kits desde el aparato y la explosión del kit en la recolección normal.

## Qué se construyó
- **Foto de componentes por línea** (`SalesOrderLineComponent`): se toma al guardar el borrador y se conserva al volver a guardar; un kit **ajustable** acepta las piezas de esa orden (`components` en la línea).
- **Planeación por kits enteros** (`PlanAsync`/`PlanKit`): disponible = menor cociente de los componentes (hacia abajo); se asigna cada componente por su FEFO/FIFO; pedido/disponible/faltante en kits. Solicitar, cambiar cantidad, cancelar, surtir backorder y cerrar con faltante funcionan sobre los componentes (`SalesOrderReservation.ProductId`).
- **Despacho de kits** de lo reservado (sin escoger posición ni lote), kits enteros; la recolección lleva una línea por componente con `KitProductId` y `KitQuantity` (los kits, en una sola línea por kit); Kárdex con la nota «KIT × n · EMP · OV». **Deshacer** devuelve y vuelve a reservar los componentes y baja los kits.
- **DTO** de la línea: `isKit` y `components` (por kit, pedido, reservado, backorder, despachado). El aparato **no recibe** líneas de kit todavía.
- **Web:** distintivo «Kit» y componentes bajo la línea del borrador (con piezas editables si el kit es ajustable), tabla de componentes por kit en la orden solicitada.
- Update.sql sección `2026-10-16 (q)`; manual 18 §17, FAQ.

## Pruebas
- xunit (12 nuevas, `SalesKitTests`): foto y cambio de catálogo, reservar/cancelar, faltante en kits y backorder de kits enteros, surtir al entrar inventario, cambiar cantidad, despachar (parcial y completo, kits anotados una sola vez, cantidad en kits), validaciones del despacho, deshacer, ajustable/fijo y piezas inválidas, kits enteros, sync sin líneas de kit, conservar la foto al guardar de nuevo. Suite completa verde **contra SQL Server real** (4113+).
- **De punta a punta contra SQL Server real vía API** (base de pruebas propia): crear 3 productos y el kit, ajustar existencia, orden de 3 kits → reservó 6 y 3 piezas; dos solicitudes más de 3 kits con solo 2 disponibles → ambas 409 con el mensaje «pedido 3, disponible 2, faltante 1»; despachar 2 kits → PARCIAL; deshacer → reabre y vuelve a reservar.
- Web (vitest): reglas del borrador (kit entero, piezas), `KitLineBlock` y la sección de componentes. `npm run check` verde.

## Decisiones por omisión (a revisar con Luis)
1. **La foto se toma al agregar el kit al borrador y se conserva** al volver a guardar (no se refresca sola con el catálogo); para tomar el catálogo nuevo, quitar y volver a agregar la línea. (El diseño decía «al confirmar la orden».)
2. **El kit se despacha de lo reservado** de sus componentes; no se escoge posición ni lote (cambiar de posición de un kit no está soportado).
3. **Kits en piezas enteras** y sin lote en la línea (el lote del kit se ignora).
4. El **precio del kit** es el `SalePrice` del kit (no la suma de sus componentes); el costo de cada pieza sigue siendo el del componente.
5. El aparato **no despacha kits** hasta el Lote 70 (el servidor no manda sus líneas).
