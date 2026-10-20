# Lote 148 — Valuación del inventario (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Ajuste de la compañía «Valuación del inventario»** (`Tenant.ValuationMethod`, Ajustes → Operación): **Costo promedio** (por omisión) o **Por capas** (lo primero que entró). SQL idempotente al final de `Diseño/logistica-db-update.sql` con `CHECK`.
- **Reglas puras** (`ValuationRules`) sobre las capas de costo del Lote 147 (líneas de recibos confirmados, de la más vieja a la más nueva):
  - *Promedio:* costo promedio ponderado de todas las capas recibidas del producto; el valor es lo que hay en mano × ese promedio.
  - *Por capas:* lo que hay en mano se atribuye a las capas **más nuevas** (lo viejo se fue primero); el valor es la suma.
  - Una capa sin costo congelado, o existencia más allá de todas las capas (inventario anterior al Lote 147), se valúa al **costo del producto**; sin él, queda **sin valuar** (se informa en «Unidades sin costo»).
- **`ValuationService`:** reporte y **costo de las salidas** (`IssueCostsAsync`). Solo lee; no mueve inventario.
- **Costo congelado de las salidas:** recolección, despacho manual, despacho de ventas y despacho de avisos de salida congelan ahora `PickBatchLine.UnitCost` según el método (promedio, o la capa más vieja que aún queda), en vez de siempre `Product.PurchaseCost`. Sin capas el resultado es el costo del producto, igual que antes.
- **Reporte «Valor del inventario»:** `GET /api/v1/inventory/valuation` (permiso nuevo **`inventory.value`**: TenantAdmin y Billing, también a los roles ya clonados en el SQL) con filtros de almacén, cliente dueño y buscador; paginado (≤ 500), ordenado por valor; los totales (valor, unidades, unidades sin costo, método) son de todo el filtro. Web: pestaña **Valor del inventario** en Productos e inventario (`?tab=valuation`), con exportación completa.

## Cómo se probó
- xunit `ValuationTests` (7): parseo, promedio, capas, capa sin costo / sin ningún costo, costo de salida (promedio y capa más vieja que queda), reporte con los dos métodos sobre recibos reales con precio, totales y filtros 404, costo de salidas línea por línea. Catálogo de permisos (110), mapa de seguridad de controladores y contrato de ajustes actualizados. Suite completa .NET verde.
- vitest: panel (totales, método, filtro de almacén) y selector de ajustes; `npm run check` verde.

## Decisiones a revisar
1. **Las capas son por producto**, no por lote (el costo de dos lotes del mismo producto se mezcla). Si hace falta costo por lote, es un lote aparte.
2. **El promedio es el de todo lo recibido** (no un promedio móvil que descuente las salidas). Es simple y estable; un promedio móvil exige reproducir el Kárdex.
3. **Las salidas ya registradas no se recalculan**; solo las nuevas usan el método. Cambiar el método de la compañía tampoco reescribe el pasado.
4. Existencia de **alquiler, transferencias y ajustes** se valúa igual (es existencia); un ajuste de entrada no crea capa (se valúa al costo del producto).
5. Falta (a propósito): valor del inventario en el Pulso / indicadores y por lote; el portal del cliente de almacén vendrá después.
