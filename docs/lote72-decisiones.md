# Lote 72 — Facturación: corridas y venta de producto (P5, 1 de 4) (2026-10-16)

Primer lote de la facturación. Siguen: **73** pantallas de Facturación (web), **74** flete de las órdenes de transporte (`BillingModel`/tarifas), **75** pagos, estado de cuenta y exportación por plantillas.

## Qué se construyó
- **Entidades** `BillingRun`, `Invoice`, `InvoiceLine` y `Payment` (las tablas del diseño original) con las columnas nuevas de la sección `2026-10-16 (r)` de `update.sql`; contador `BILLINVOICE` (FAC-#####), estatus Cancelada de la corrida, EntityType `BILLING_RUN`, permiso `billing.view` (también a los roles clonados que ya generan, aprueban o exportan).
- **`BillingService`** + `/api/v1/billing/*`: ajustes (base de facturación y días de vencimiento), corridas (generar, revisar, aprobar con AAL2, cancelar), facturas (lista, detalle, ajustar una línea con motivo, anular con AAL2).
- **Venta de producto**: lo despachado y no facturado de las órdenes de venta, una factura por cliente y moneda, con la **copia del impuesto** por línea (proporcional; la última parte toma el resto exacto).
- **Nunca dos veces**: al generar se bloquean las líneas (`InventoryQueries.LockSalesOrderLineAsync`, UPDLOCK) y se reclama `QtyInvoiced`; probado con tres corridas simultáneas contra SQL Server (solo una factura; las otras 422).
- Base `ON_DISPATCH` / `ON_DELIVERY`, kits facturados en kits, período acumulado o solo-el-período.
- Manual (capítulo 19 nuevo), FAQ y README.

## Pruebas
- xunit (12): reglas (facturable, impuesto parcial, vencimiento), generar con copia de impuesto, nada por facturar y nunca dos veces, la última parte del impuesto, revisar/aprobar con sus guardas, cancelar libera, ajuste (original conservado, impuesto y totales), anular (con y sin pagos), un cliente por factura y el filtro de período, base «al entregar», ajustes e aislamiento entre compañías. Suite completa verde **contra SQL Server real** (4129).
- **De punta a punta contra SQL Server real vía API**: orden despachada → corrida → ajuste → revisar → aprobar (403 sin reautenticación, 200 con ella) → anular → volver a generar; y la carrera de tres corridas simultáneas.

## Decisiones por omisión (a revisar con Luis)
1. **Numeración:** las facturas son `FAC-#####`, un contador por compañía (no usa la numeración por cliente, que es la del «número de factura del cliente» de sus órdenes de transporte). D9 sigue abierta para Truss.
2. **Lo que entra:** por omisión todo lo pendiente hasta la fecha final (el inicio solo etiqueta la corrida salvo `onlyInPeriod`).
3. **Base «al entregar»:** cuentan los retiros (sin orden de transporte) y las entregas con la orden Entregada; la fecha del despacho es la de la recolección (no la de la entrega). Un despacho «para después» (sin entrega aún) se factura como retiro.
4. **Aprobar exige haber revisado** (el motor de estatus solo avanza de a una etapa).
5. **Una factura por cliente y moneda**; el contrato (`ContractId`) queda vacío en este lote.
6. **Devoluciones:** `QtyReturned` ya descuenta lo facturable; una devolución posterior a facturar se corregirá con nota de crédito (P6).

## Fuera de este lote
- Pantallas, flete de órdenes de transporte, pagos y estado de cuenta, exportación (plantillas) y las corridas de compras y despachos.
