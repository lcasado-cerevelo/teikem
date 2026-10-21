# Lote 126 — Procesar la factura del cliente («second check») sin órdenes ficticias (dictado de Luis, 2026-10-18)

## Qué se construyó
- **Base (sección (bd) y (be) de `update.sql`):** `TransportOrder.InvoiceProcessedAtUtc / InvoiceProcessedBy / InvoiceProcessRunId` (→ `CodReconcileRun`), `Contract.BillInvoiceCheck / InvoiceCheckFee` y el tipo de cargo `INVOICE_CHECK`. Aplicado a la base local.
- **Lectura de hojas (`POST /cod/reconcile`):** `mode` AUTO/COD/INVOICE/BOTH. A cada hoja se le busca la orden (por número de envío o factura, como siempre); **con COD** cuadra el cobro y además marca la factura procesada; **sin COD** la coteja: `INVOICE_CHECKED` si está entregada, `NOT_DELIVERED` si no, `ALREADY_PROCESSED` si ya se procesó. Una sola vez por orden. `items[].kind`, `checked` y `warnings[]` (informativas; nunca se descarta una hoja). Las órdenes sin COD ya no salen como «Sin COD» en la lectura (ese resultado quedó sin uso).
- **Web — Procesar entregas → Reconciliar:** selector «¿Qué vas a subir?» (Detectar por omisión), advertencias, columna Tipo, resumen con facturas procesadas y Cuadrar habilitado si hay cotejos aunque no haya COD. Ficha de la orden: «Factura procesada: fecha · quién».
- **Contrato → Cotejo de factura:** interruptor y monto (`PATCH /contracts/{id}/invoice-check-fee`); la corrida de facturación mete la línea «Cotejo de factura de la orden X» (`INVOICE_CHECK`) una vez por orden, reclamándola bajo bloqueo como el cargo por devolución.

## Cómo se probó
- xunit: `InvoiceCheckTests` (4: cotejo y marca por hoja de COD con vista previa, modos y advertencias con 400 del modo inválido, cargo facturado una sola vez y solo con contrato que cobra, validación del contrato). `CodTests` ajustado (la orden sin COD ya cuenta como factura procesada). Suite completa verde.
- vitest: `CodScreen.test.tsx` (selector, modo en la solicitud, tipo Factura y advertencias), `codRules.test.ts` (mode), `InvoiceCheckFeeSection.test.tsx` (2). `npm run check` verde.
- Playwright: `lote126-procesar-factura.spec.ts` contra el API y SQL Server reales (ver abajo).

## Decisiones a revisar
1. **Todo se procesa, el modo solo advierte** (propuesta aceptada por Luis): elegir mal nunca pierde una hoja.
2. **La hoja de COD también marca la factura procesada** (idea de Luis): los tres campos significan «la factura de esta orden ya se procesó», sea COD o cotejo.
3. **Solo cuenta «Entregada»** para cotejar; una orden devuelta o en almacén (pick up) sale «No entregada». Si hace falta cotejar devueltas, se amplía.
4. **El cargo** sale del contrato de la orden al momento de facturar (no se congela al procesar). **Luis (2026-10-18):** el cliente de las hojas es **obligatorio** para procesar facturas (la pantalla lo pide antes de Cuadrar) y el **portal del cliente** muestra «Factura procesada: fecha» en la ficha de la orden (hecho).
5. **Migración:** las órdenes del sistema viejo con factura procesada pueden venir con fecha y usuario y `InvoiceProcessRunId` NULL; las órdenes ficticias no se migran.
