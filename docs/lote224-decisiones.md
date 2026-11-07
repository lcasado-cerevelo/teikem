# Lote 224 — Pedido de vendedores, P6: consignación (2026-11-07)

Parte P6 de [pedido-vendedores-plan-desarrollo.md](pedido-vendedores-plan-desarrollo.md) (§1.7 cifra «consignación sin pagar», §1.8, §2 fila `ConsignmentPayment`, §3 y §4.1 fila P6). Solo backend; la pestaña **Consignación** de Facturación va en su parte de frontend.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Base (`logistica-db-update.sql`, sección «2026-11-07 — Lote 224») | Tabla `ConsignmentPayment` (factura → monto, `PaidDate`, método `PaymentMethod`, referencia, `RecordedBy`/`RecordedAtUtc`, `PaymentId` del pago de la factura que generó; anulación `VoidedAtUtc`/`VoidedBy`/`VoidReason`). Permiso `billing.consignment.pay` y su propagación a los roles ya clonados `TenantAdmin` y `SalesOffice` (marca `L224_CONSIGNMENT`). «Ventas (30 días)» y «Ventas por día» de fábrica pasan a la fuente `SALES_REVENUE` solo si siguen como se sembraron. Idempotente, solo agrega (probado dos veces sobre una base local nueva). |
| Entidad y reglas | `Domain/Billing/Consignment.cs`: `ConsignmentPayment` (`[AuditEntity("INVOICE")]`, como `Payment`) y `ConsignmentRules` (cifras y mensajes). Mapeo `Configurations/ConsignmentConfigurations.cs`. |
| Servicio | `ConsignmentService`: lista, detalle, reporte por cliente, registrar y anular pagos, conteo para el Pulso. |
| Endpoints (`ConsignmentController`, módulo BILLING) | `GET /api/v1/billing/consignment/invoices?balance=OPEN\|PAID\|ALL&clientPublicId&q&skip&take` · `GET …/invoices/{id}` · `GET …/by-client` (reporte «Consignado por cliente») — con `billing.view` o `billing.consignment.pay`. `POST …/invoices/{id}/payments` (`billing.consignment.pay`) · `POST …/payments/{id}/void` (`billing.consignment.pay` + AAL2). |
| Lo vendido | Fuente nueva `SALES_REVENUE` («Ventas (lo vendido)», `sales.view`, módulo SALES_ORDERS): órdenes confirmadas que **no** son de consignación en su `CreatedAtUtc` + pagos de consignación que cuentan en su fecha de pago (campos `Amount`, `SoldAtUtc`). `SALES_ORDER` suma el campo `IsConsignment`. |
| Pulso | Panel Ventas: `ConsignmentUnpaid` = facturas con saldo en consignación (ruta `/money/billing?tab=consignment`), para quien tiene `billing.view` o `billing.consignment.pay` con BILLING encendido; si no, `null`. |
| Permiso | `billing.consignment.pay` (BILLING), en `PermissionCatalog`, `PermissionDisplayOrder`, plantillas `TenantAdmin` y `SalesOffice`; implica entrar a Facturación (`billing.view`). Conteo del catálogo 156 → 157 en las pruebas. |
| Contrato | `web-app/openapi.json`: solo lo nuevo (5 rutas, 9 esquemas) del swagger del API levantado en Development sin llave de Brevo; `schema.d.ts` regenerado en web-app y copiado igual a app-cliente y app-almacen (eran idénticos). |

## Cifras de una factura en consignación
- **En consignación** = la factura (emitida, vencida o pagada, activa) tiene líneas de órdenes `IsConsignment`: la venta del producto (por `SalesOrderLineId`) y el cargo de envío (por `SalesOrderId`).
- **Dejado** = esas líneas con su IVU. **Devuelto** = notas de crédito APROBADAS o APLICADAS de esas líneas (la devolución de venta de siempre las crea). **Pagado** = pagos de consignación no anulados (ni anulado su pago de la factura). **Saldo** = dejado − devuelto − pagado (nunca negativo).

## Mensajes (para el manual y la FAQ)
| Caso | HTTP | Mensaje |
|---|---|---|
| Pago mayor que el saldo en consignación | 422 | «El pago (X) es mayor que el saldo en consignación de la factura (Y).» |
| Pago mayor que el saldo de la factura (lo demás ya se cobró por Pagos) | 422 | «El pago (X) es mayor que el saldo de la factura (Y).» |
| Factura sin nada en consignación | 422 | «La factura no tiene nada en consignación.» (en el detalle: 404 «Factura en consignación no encontrada.») |
| Factura borrador o anulada | 422 | «Solo se registran pagos de consignación de una factura emitida, vencida o pagada.» |
| Ya pagado todo lo consignado | 422 | «Lo consignado en esta factura ya está pagado por completo.» |
| Filtro inválido | 400 | «El filtro debe ser OPEN, PAID o ALL.» |
| Monto, método, fecha, referencia, motivo de anular | 400 / 422 | Los mismos de Pagos (`PaymentRules`): «Indique el monto del pago.», «El monto del pago debe ser mayor que cero y tener hasta 2 decimales.», «Indique el método de pago.», «El método de pago no existe.», «La fecha del pago no puede ser futura.», «La fecha del pago no puede ser anterior a la emisión de la factura.», «La referencia no puede pasar de 80 caracteres.», «Escriba el motivo para anular el pago.», «El pago ya está anulado.» (422), «La factura ya está pagada por completo: …» (422). |

## Decisiones (para que Luis las ratifique)
1. **El pago de consignación es también el pago de la factura.** Si la factura tiene saldo, registrarlo crea el `Payment` de siempre (mismas reglas; la factura pasa a PAGADA al saldarse) y queda ligado (`PaymentId`): no se entra dos veces. Si la factura ya se cobró por Pagos, el pago de consignación solo informa lo vendido (sin `Payment`).
2. **Se pide el método de pago** (ACH, cheque, tarjeta, efectivo), además de monto, fecha y referencia del plan: lo exige el pago de la factura.
3. **Anular** un pago de consignación (con motivo y AAL2) anula también su pago de la factura; no se puede si la factura ya quedó pagada por completo (regla de Pagos). Anular el pago desde Pagos también lo saca de la consignación.
4. **Lo devuelto** sin vender baja el saldo por las notas de crédito aprobadas o aplicadas de esas líneas (la devolución de venta de siempre). No hay otra pantalla de devolución.
5. **Los reportes de ventas**: «Ventas (30 días)» y «Ventas por día» leen de `SALES_REVENUE`. La orden en consignación no cuenta; cada pago cuenta por su monto (con IVU y, si lo lleva, el envío) en su fecha de pago. Las vistas de **órdenes** (`SALES_ORDER`: lista, «por cliente», backorders) son operativas y siguen igual, con el campo nuevo `IsConsignment` para filtrar. «Pedidos de hoy» del panel Ventas cuenta pedidos, no ventas, y tampoco cambia.
6. **Quién ve la pestaña**: `billing.view` o `billing.consignment.pay`; el permiso implica entrar a Facturación (la «Oficina de ventas» entra a Facturación con él).
7. **La lista se calcula al leer** (sin columnas de saldo en la factura): con muchas facturas de consignación convendría guardarlo; hoy no hace falta.
8. **Vencimiento**: las facturas de consignación vencen como cualquier factura (el trabajo de vencidas no sabe de consignación). Si Luis quiere que no venzan hasta el pago, es otro cambio.

## Cómo se probó
- `ConsignmentTests` (xunit, InMemory, 10): reglas puras; pago parcial y total (saldo, pago ligado a la factura, factura PAGADA, no pasa del saldo, ya pagada); validaciones con sus mensajes; solo cuentan las líneas en consignación (con envío) y la nota de crédito aprobada baja el saldo; factura ya cobrada por Pagos; anular (y anular por Pagos); lo vendido excluye lo consignado hasta el pago y lo cuenta en la fecha del pago (rangos por día, pago anulado fuera); lista, filtros, reporte por cliente y panel del Pulso con y sin permiso/módulo; permiso y roles; sección SQL. `AnalyticsSeedFieldsTests` ajustada (las ventas en `SALES_REVENUE`).
- `dotnet build Teikem.sln` y `dotnet test Teikem.sln`: verdes (resultado literal en el reporte de la parte).
- Base local nueva `TeikemL224` con `db-init` (sección aplicada) y la sección corrida dos veces más a mano (convierte el indicador y el gráfico viejos una vez; la segunda no toca nada). Recorrido con curl contra el API local sin llave de Brevo: lista, pago de más (422), método malo (400), parcial (factura con su pago), reporte por cliente, panel Ventas (`consignmentUnpaid: 1`), «Ventas (30 días)» = 40 (30 de la venta normal + 10 pagado; los 20 consignados no) y 59.30 tras el pago total, anular sin AAL2 = 403 `aal2_required`.

## Pendiente / para otras partes
- Frontend: pestaña **Consignación** en `/money/billing` (lista con `exportRows`, detalle, registrar pago, anular, reporte por cliente), clave i18n de `consignmentUnpaid` del panel Ventas; `ConsignmentTab.test.tsx`.
- Manual (capítulo de Facturación) y FAQ con los mensajes de arriba.
