# Lote 74 — Flete de los envíos de transporte en la facturación (P5, 3 de 4) (2026-10-16)

Tercer lote de la facturación. Siguen: **75** pagos, estado de cuenta y plantillas de exportación (`ACCT_TEMPLATES`, QuickBooks genérico); después la contabilización de compras y de despachos y el cargo por intento de entrega (depende de P7).

## Qué se construyó
- **El flete entra en la misma corrida** que la venta de producto: `BillingService.GenerateAsync` junta las dos cosas por cliente y moneda en una factura. La solicitud trae `IncludeProducts` e `IncludeFreight` (verdadero por omisión; al menos uno: `Elija qué facturar: …`, 400).
- **Cuándo se factura** (`FreightRules.BillableStatuses`, según el `BillingModel` del contrato de la orden): por recogido desde Recogido; por entrega, mixto y sin contrato/modelo solo Entregada. Fallida, parcial, en espera, borrador y cancelada nunca. El hecho se toma de `EntityStatusHistory` (primera vez en un estatus facturable); sin historial, la fecha de confirmación o de cotización.
- **El monto es lo cotizado** (`QuotedAmount`, congelado al confirmar). Se desglosa con `ContractRateResolver.Breakdown` (nuevo, puro) vía `RateService.BreakdownAsync` / `OrderQuoteService.BreakdownAsync`, con las tarifas vigentes **el día de la cotización**: tarifa base y piezas extra (FREIGHT, con `RateComponentId`), despacho (HANDLING), COD (SURCHARGE) y servicio especial (FREIGHT). Si el desglose no suma lo cotizado, o no se puede calcular, una sola línea «Flete (monto cotizado)».
- **Impuesto** con `TaxService.CalculateAsync` por tipo de cargo y exención del cliente; la copia queda en la línea.
- **Nunca dos veces:** `TransportOrder.FreightInvoiced` (update.sql sección **(s)**) se reclama bajo `InventoryQueries.LockTransportOrderAsync` (UPDLOCK; el 21.º enunciado de SQL crudo permitido) y se libera al cancelar la corrida o anular la factura.
- Web: dos casillas en «Nueva corrida». La descripción de cada línea de flete ya lleva el número de la orden. DTO: `InvoiceLineDto.TransportOrderNumber` y `RateComponentId`.
- Manual (capítulo 19, §2b), FAQ y README.

## Cómo se probó
- xunit (7 nuevas): reglas de estatus y tipo de cargo, desglose que suma la cotización con la fila de tarifa de cada concepto, orden entregada facturada una vez y liberada por cancelar y por anular, modelo por recogido contra por entrega, borrador/inactiva/sin cotizar/en cero no se facturan, producto y flete juntos o separados, ajuste de una línea de flete. Suite completa contra SQL Server real: 4136 pasan.
- **De punta a punta contra SQL Server real vía API:** cliente con contrato (por servicio, pieza extra, despacho y COD), orden de 7 piezas con COD 100 cotizada en 15.50 → entregada → corrida con 4 líneas (6.50 + 5.50 + 2.00 + 1.50, con las tarifas 78 y 79), segunda corrida «nada por facturar», cancelar y volver a facturar.
- vitest de las reglas y de la pantalla (casillas).

## Decisiones a revisar con Luis
1. **Mixto cobra al entregar** (necesita recogida y entrega cumplidas). Si «mixto» debe cobrar la base al recoger y el resto al entregar, hay que partir los conceptos.
2. **Lo cotizado manda:** si el desglose de hoy no coincide, una sola línea con el monto cotizado.
3. **Tipos de cargo:** tarifa base, piezas extra y servicio especial = Flete; despacho = Manejo; COD = Recargo (la regla de impuesto por tipo de cargo los trata por separado).
4. El impuesto del flete se calcula **al generar** (tasa vigente ese día), no el día de la cotización.
5. Una orden **Parcial** no se factura hasta que quede Entregada.

## Fuera de este lote
Pagos y estado de cuenta, plantillas de exportación, contabilización de compras y despachos, cargo por intento de entrega (P7).
