# Lote 128 — Filtros de Órdenes: factura procesada y fecha de procesamiento (pedido de Luis, 2026-10-18)

## Qué se construyó
- `GET /api/v1/orders`: `invoiceProcessed` (true/false), `processedFrom` (inclusivo) y `processedTo` (exclusivo) sobre `InvoiceProcessedAtUtc`; la fila del listado trae `invoiceProcessedAtUtc`.
- Lista de Órdenes (web): filtro **Factura procesada** (Procesada / Sin procesar), filtro **Fecha de procesamiento** con los rangos sugeridos (`DateRangeFilter`, el mismo de almacén: Hoy, Esta semana, Este mes… o Personalizado) y columna «Factura procesada» (fecha).
- Fuente de datos de órdenes (Vistas, indicadores, gráficos): campos `InvoiceProcessed` (sí/no) e `InvoiceProcessedAtUtc`.
- Los filtros de **zona** y **nivel de zona** que también pidió Luis entran con el lote de Isla/Metro (la zona todavía no se guarda en la orden).

## Cómo se probó
- xunit: `OrderListFilterTests.Lote128_invoice_processed_and_processing_date_filters` (sí/no, desde, hasta y la fecha en la fila). Suite completa verde.
- vitest: `OrderListScreen.test.tsx` (filtro sí/no en la consulta, rango «Hoy» del filtro de procesamiento, columna en su orden). `npm run check` verde.
- Playwright: el recorrido `lote126-procesar-factura.spec.ts` ahora también filtra la lista por API y en pantalla (procesada / sin procesar / Hoy) en escritorio y móvil.

## Decisiones a revisar
1. La columna «Factura procesada» va después de «Fecha» y queda vacía si no se ha procesado (sin guion, para no ensuciar la lista).
2. `processedTo` es exclusivo como `to` (la pantalla manda el día siguiente para incluir el día elegido).
