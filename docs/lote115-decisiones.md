# Lote 115 — Cargo por devolución (2026-10-18)

Luis dictó: cargo por devolución **opcional por cliente** (a todos los tipos de paquete, por tipo de paquete o por paquete). Sus respuestas: se configura **en el contrato del cliente**, con **tres modos a escoger**, y se cobra **a la orden devuelta, al quedar «Devuelta»**.

## Qué se construyó
- **SQL (update.sql, sección (aw))**: `Contract.ReturnFeeMode` (ORDER | PACKAGE_TYPE | PIECE) y `ReturnFeeAmount`; tabla `ContractReturnFee` (monto por tipo de paquete); `TransportOrder.ReturnFeeAmount` y `ReturnFeeInvoiced`; `InvoiceLine.IsReturnFee`.
- **Contrato**: `PATCH /api/v1/contracts/{publicId}/return-fee` (`contracts.update`) valida modo, montos (>= 0) y tipos de paquete; la ficha del contrato trae `returnFee`. **Web**: Cliente → Contrato → «Cargo por devolución» (modo, monto o filas por tipo de paquete).
- **Cálculo** (`ReturnFeeRules.Compute`, al devolver en `ReturnOrderService`): ORDER cobra el monto una sola vez por orden; PIECE, monto × piezas devueltas; PACKAGE_TYPE, la suma del monto de cada tipo devuelto. Se acumula en `TransportOrder.ReturnFeeAmount` de la orden devuelta al sumar piezas (botón o escaneo).
- **Facturación** (`BillingService`): las órdenes Devuelta con cargo sin facturar entran en la corrida del flete (Incluir flete, por la fecha en que quedaron Devuelta) como una línea **«ORD-… · Cargo por devolución»** de tipo recargo (SURCHARGE) con su impuesto; reclama `ReturnFeeInvoiced` aparte de `FreightInvoiced` (se pueden facturar por separado) y cancelar la corrida o anular la factura lo libera.

## Cómo se probó (solo el área)
- xunit `ReturnFeeRulesTests` (cálculo y modos), `ReturnOrderServiceTests` (+5: por orden una sola vez, por pieza creciente, por tipo, sin cargo, sin contrato) y `FreightBillingTests` (+1: su propia línea, aparte del flete, y se libera al cancelar la corrida). vitest: `ReturnFeeSection` (6).
- Contra SQL real (`db-update` dos veces): el contrato guarda por pieza y por tipo (400 con negativo y con modo malo), y devolver 2 piezas con 4 por pieza dejó `ReturnFeeAmount = 8.00` en la orden devuelta. La corrida de facturación no se pudo ejecutar en la base de desarrollo (el módulo BILLING no está encendido en esa compañía); esa parte la cubre xunit.

## Decisiones a revisar con Luis
1. **Solo se acumula mientras no se haya facturado**: si el cargo ya se facturó y después se devuelven más piezas, ese excedente no se cobra (habría que generar otro cargo).
2. **El cargo usa el contrato congelado de la orden** (`ContractId` de la orden), no el contrato vigente al devolver.
3. **Por tipo de paquete**, un tipo sin monto no cobra (no se rechaza la devolución).
