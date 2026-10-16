# Lote 75 — Pagos, estado de cuenta y cuentas por cobrar (P5, 4 de 5) (2026-10-16)

Cuarto lote de la facturación (el servidor). Siguen: **76** pantallas de cobros (web), **77** plantillas de exportación a contabilidad (`ACCT_TEMPLATES`, QuickBooks genérico); después la contabilización de compras y de despachos y el cargo por intento de entrega (P7).

## Qué se construyó
- **`PaymentService`** y rutas en `/api/v1/billing`: registrar un pago (`POST invoices/{id}/payments`), anularlo (`POST payments/{id}/void`), estado de cuenta (`GET clients/{id}/statement`) y cuentas por cobrar (`GET receivables`). El detalle de la factura trae sus pagos (`InvoiceDto.Payments`).
- **Reglas** (`PaymentRules`, puras): monto > 0 con hasta 2 decimales y sin pasar del saldo, método del catálogo `PaymentMethod`, fecha no futura ni anterior a la emisión, referencia ≤ 80, solo facturas Emitidas o Vencidas; saldo = total − pagos no anulados; cubetas de antigüedad.
- **Concurrencia:** registrar o anular bloquea la factura (`InventoryQueries.LockInvoiceAsync`, UPDLOCK; el 22.º enunciado de SQL crudo permitido): dos pagos a la vez no pasan del saldo.
- **Estatus:** al cubrir el total la factura pasa a PAID por `StatusService` (también desde OVERDUE). Un pago no se borra: se anula con motivo (`Payment.VoidedAtUtc/VoidedBy/VoidReason`, update.sql sección **(t)**) y deja de contar; anular los pagos permite anular la factura.
- **Trabajo `BILLING_OVERDUE_SCAN`** (cada hora, encendido): pasa a OVERDUE lo emitido con vencimiento pasado.
- Manual (capítulo 19 §4b), FAQ, README y el capítulo 13 (trabajos programados).

## Cómo se probó
- xunit (10 nuevas): reglas y cubetas, pago parcial y total, tope del saldo, todas las validaciones con su mensaje, borrador no pagable, anular con motivo (y no sobre una factura pagada), estado de cuenta con saldo corrido, saldo inicial y antigüedad, cuentas por cobrar por cliente, el trabajo de vencidas y pagar una vencida, aislamiento por compañía. Suite completa contra SQL Server real: 4146 pasan.
- **De punta a punta contra SQL Server real vía API:** factura de 100 emitida con reautenticación (AAL2); pago de 150 rechazado (422); pago parcial de 30 con cheque; anularlo (saldo 100); estado de cuenta; pago total por ACH → Pagada; anular ese pago rechazado (422); el cliente pagado no sale en cuentas por cobrar.

## Decisiones a revisar con Luis
1. **Sin permiso nuevo:** registrar pagos usa `billing.generate`; anular un pago, `billing.approve` + AAL2. Si cobranza debe ser un rol aparte, se agrega `billing.payment` (con su propagación a los roles ya clonados).
2. **Un pago de una factura Pagada no se anula** (PAID es terminal); se corrige con nota de crédito (P6) o con soporte. La alternativa es permitir reabrir una factura Pagada.
3. **Sin sobrepago:** un cobro mayor al saldo se rechaza; el saldo a favor llegará con las notas de crédito (P6).
4. **Una moneda por cliente:** el estado de cuenta suma los montos sin separar monedas.
5. **Estado de cuenta por omisión de 90 días**; lo anterior es el saldo inicial.
6. Fecha del pago a mediodía local cuando no es hoy (la hora no importa; así cae en el día correcto en cualquier zona).

## Fuera de este lote
Pantallas (76), plantillas de exportación (77), avisos de factura vencida, el saldo del cliente alimentando el límite de crédito, notas de crédito (P6).
