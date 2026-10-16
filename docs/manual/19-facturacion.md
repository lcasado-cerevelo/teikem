# 19 — Facturación

Lote 72 (P5, 2026-10-16). La **facturación** convierte lo que ya salió (despachado) en facturas al cliente, por **corridas** que se generan, se revisan, se aprueban y se exportan.
Este capítulo describe el **núcleo**: las corridas y facturas de la **venta de producto** de las órdenes de venta ([capítulo 18](18-ordenes-de-venta.md)). El flete de las órdenes de
transporte, los pagos, el estado de cuenta y la exportación a contabilidad se agregan en lotes siguientes.

## 1. Quién puede y dónde

- Módulo **Facturación** (`BILLING`; viene apagado, se enciende por compañía) y, para vender producto, el módulo Órdenes de venta.
- **Ver** corridas y facturas: `billing.view` (nuevo; se agregó a los roles que ya generan, aprueban o exportan facturación y al administrador de la compañía).
- **Generar, revisar, cancelar y ajustar:** `billing.generate`. **Aprobar una corrida, anular una factura y cambiar los ajustes:** `billing.approve`; aprobar y anular exigen además **reautenticación reciente (AAL2)**.
- API: `/api/v1/billing/*` (`settings`, `runs`, `invoices`).

## 2. Cómo funciona

1. **Generar** (`POST /billing/runs` con período y, si se quiere, un cliente): el sistema junta lo **despachado y no facturado** de las órdenes de venta y arma una **factura en borrador por cliente** (y moneda).
   Cada línea es una `PRODUCT_SALE` con la orden, el producto, la cantidad, el precio de la orden y la **copia del impuesto** de la línea de la orden (en proporción a lo que se factura; la última parte toma el resto exacto, así las partes suman el impuesto de la línea).
2. **Revisar** (`.../review`): marca la corrida como revisada. Mientras está generada o revisada se pueden **ajustar** los montos.
3. **Aprobar** (`.../approve`, AAL2): las facturas pasan a **Emitida** con fecha de emisión de hoy y vencimiento según los días de la compañía.
4. **Cancelar** (`.../cancel`): solo una corrida generada o revisada; sus facturas se anulan y lo que reclamaron vuelve a quedar por facturar. Una corrida aprobada se corrige **anulando sus facturas**.

**Nunca se factura dos veces.** Al generar, cada línea de orden de venta se bloquea y se anota lo facturado (`QtyInvoiced`); dos corridas a la vez no facturan lo mismo (probado con tres simultáneas
contra SQL Server). Lo facturable de una línea es `despachado − devuelto − ya facturado`. Anular una factura o cancelar la corrida lo libera exactamente.

**Qué entra en el período.** Por omisión entra **todo lo pendiente hasta la fecha final** (lo despachado antes que no se facturó no se pierde). Con *solo dentro del período* (`onlyInPeriod`), solo lo
despachado entre la fecha inicial y la final. El kit se factura **en kits**.

**Base de facturación (ajuste de la compañía).** `ON_DISPATCH` (por omisión, Advance Logistics): se factura al despachar. `ON_DELIVERY` (Island Wide): se factura lo que no lleva entrega (retiro) o cuya
orden de transporte ya está **Entregada**. Se cambia en `PUT /billing/settings` junto con los **días de vencimiento** (30 por omisión).

## 3. El ajuste manual

`PATCH /billing/invoices/{id}/lines/{lineId}` con el nuevo monto unitario (de 0 en adelante, hasta 4 decimales) y el **motivo obligatorio** (hasta 250 caracteres). La línea conserva el monto
**calculado** (`originalUnitAmount`; un segundo ajuste no lo pisa), se recalcula el impuesto de la línea con su tasa copiada y se actualizan los totales de la factura y de la corrida. Queda en la bitácora.

## 4. Estatus

- **Corrida:** Generada → Revisada → Aprobada → Exportada; o Cancelada.
- **Factura:** Borrador → Emitida → Pagada; Vencida y Anulada.

## 5. Validaciones y mensajes

| Situación | Mensaje | HTTP |
|---|---|---|
| Generar sin fechas | `Indique el período de la corrida (fecha inicial y final).` | 400 |
| Fecha final anterior a la inicial | `La fecha final no puede ser anterior a la inicial.` | 400 |
| Nota de más de 500 caracteres | `La nota no puede pasar de 500 caracteres.` | 400 |
| Nada por facturar | `No hay nada por facturar con esos datos.` | 422 |
| Revisar una corrida que no está generada | `Solo se puede revisar una corrida generada.` | 422 |
| Aprobar sin revisar | `Revise la corrida antes de aprobarla.` | 422 |
| Cancelar una corrida aprobada o exportada | `Solo se cancela una corrida generada o revisada; una aprobada se corrige anulando sus facturas.` | 422 |
| Ajustar fuera de una corrida generada o revisada | `Solo se ajusta una factura de una corrida generada o revisada.` | 422 |
| Ajuste sin monto / monto inválido | `Indique el nuevo monto unitario.` / `El monto unitario debe ser un número de 0 en adelante con hasta 4 decimales.` | 400 |
| Ajuste sin motivo / motivo largo | `Escriba el motivo del ajuste.` / `El motivo del ajuste no puede pasar de 250 caracteres.` | 400 |
| Ajuste al mismo monto | `El monto unitario es el mismo; no hay nada que ajustar.` | 422 |
| Anular una factura que no está emitida ni vencida | `Solo se anula una factura emitida o vencida.` | 422 |
| Anular una factura con pagos | `La factura tiene pagos registrados: no se puede anular.` | 422 |
| Base de facturación inválida | `La base de facturación debe ser ON_DISPATCH (al despachar) u ON_DELIVERY (al entregar).` | 400 |
| Días de vencimiento fuera de 0–365 | `Los días de vencimiento deben estar entre 0 y 365.` | 400 |
| Aprobar o anular sin reautenticación reciente | `Esta acción requiere reautenticación reciente (AAL2).` | 403 |
| Corrida / factura / línea inexistente | `Corrida de facturación no encontrada.` / `Factura no encontrada.` / `Línea de factura no encontrada.` | 404 |

## 6. Para soporte

- Tablas `BillingRun`, `Invoice`, `InvoiceLine` y `Payment` (las del diseño original) más las columnas de la sección **2026-10-16 (r)** de `Diseño/logistica-db-update.sql`: `BillingRun.ClientId/Note/ReviewedBy/ReviewedAtUtc/ExportedAtUtc/ExportedBy`,
  `Invoice.Notes`, `InvoiceLine.SalesOrderLineId/OriginalUnitAmount/AdjustmentNote`, `Tenant.SalesBillingTrigger/InvoiceDueDays`; el contador `BILLINVOICE` (**FAC-#####**, uno por compañía), el estatus
  **Cancelada** de la corrida, el EntityType `BILLING_RUN` y el permiso `billing.view`.
- `InvoiceLine.LineTotal` e `Invoice.Total` son columnas calculadas de la base; el sistema suma las líneas.
- El historial de estatus de la corrida y de cada factura está en `EntityStatusHistory` (con el comentario de cancelar/anular).
