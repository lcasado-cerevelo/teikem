# Lote 222 — Pedido de vendedores, P2: la orden con sus datos nuevos, el envío, el cliente prospecto y los permisos (2026-11-07)

Parte P2 de [pedido-vendedores-plan-desarrollo.md](pedido-vendedores-plan-desarrollo.md) (§1.1, §1.3, §1.4, §1.7, §2, §3). Solo backend; las pantallas van en su parte de frontend.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Base (`logistica-db-update.sql`, sección 2026-11-07 «Lote 222») | `SalesOrder`: `IsUrgent`, `DeliveryMode` (DELIVERY/PICKUP, NULL = se deduce del consignatario), `PaymentMethodLookupId`, `PaymentTermLookupId`, `PaymentChanged`, `ShippingCharge`, `ShippingTaxAmount`, `ShippingReasonLookupId`, `ShippingInvoiced`, `IsConsignment`, `AuthorizedByName`, `SignatureFileId`, `SubmittedWithAuth`, `AwaitingClientApproval`. `Client`: `MerchantRegistration`, `ShippingMode` (NULL/FREE/OWN), `ShippingCharge`, `ShippingFreeFrom`, `RequireOrderSignature`. `Tenant`: `SalesShippingCharge`, `SalesShippingFreeFrom`, `SalesShippingTaxable`. `InvoiceLine.SalesOrderId`. Tabla `ClientDocument`. Catálogos `SalesPaymentMethod`, `ClientDocumentType`, `ShippingReason`; tipo de cargo `SHIPPING`; EntityType `CLIENT_DOCUMENT`. Permisos `sales.approve_client`, `clients.create_basic`, `pulse.sales` y su propagación a los roles ya clonados (`L222_SALES_PROSPECT`). Idempotente, solo agrega. |
| Reglas puras | `ShippingRules` (compañía/cliente, NULL = compañía, mínimo opcional comparado con el total después del IVU, solo entrega, porqué), `SalesPaymentRules` (prellenado, marca de cambio, término solo con crédito, consignación solo con su forma), `SalesOrderExtraRules`, `ClientProspectRules`. |
| Orden de venta | `SalesOrderRequest` acepta `IsUrgent`, `DeliveryMode`, `PaymentMethod`, `PaymentTerm`, `IsConsignment`, `AuthorizedByName`. `SalesOrderDto.Terms` (`SalesOrderTermsDto`) y la lista traen los distintivos. El envío se calcula al guardar el borrador, al solicitar y al modificar una línea; `Total` ya incluye envío e IVU del envío. Solicitar: un cliente SUSPENDIDO no compra; la firma que exige el cliente; un PROSPECTO deja la orden `AwaitingClientApproval` y `DispatchAsync` la rechaza (422). |
| Ajustes → Operación → Ventas | `GET /sales-orders/settings` (sales.view o admin.tenant) y `PATCH` (admin.tenant): cargo, mínimo, IVU. |
| Cliente prospecto | `POST /clients/basic` (clients.create_basic o clients.create) → nace PROSPECTO por `StatusService.StartAtAsync` (nuevo: nacer en una etapa del pipeline anterior a la inicial) y publica `SALES_CLIENT_PENDING_APPROVAL`. `POST /clients/{id}/approve` y `/reject` (sales.approve_client), por `StatusService`, con constancia en el historial. El cambio de estatus genérico ya no saca a un prospecto sin `sales.approve_client` (403). Perfil: SURI, regla de envío, «Exige la firma de quien autoriza el pedido». |
| Documentos del cliente | `GET/POST /clients/{id}/documents`, `DELETE /clients/{id}/documents/{docId}`; StoredFile DOCUMENT del cliente; se ve por `GET /files/{id}`. |
| Avisos | Eventos `SALES_CLIENT_PENDING_APPROVAL` (no silenciable) y `SALES_ORDER_URGENT` (al solicitar un urgente). «Necesita tu atención»: `SALES_CLIENT_PENDING` (sales.approve_client) y `SALES_ORDER_URGENT` (warehouse.pick). |
| Pulso | Panel `SALES` (`pulse.sales` + `sales.view`, módulo SALES_ORDERS, orden 13): `GET /sales-orders/pulse`. |
| Factura | El cargo de envío entra como línea de servicio `SHIPPING` una sola vez por orden (`ShippingInvoiced` bajo bloqueo de la orden); cancelar la corrida o anular la factura lo libera. |
| Entrega | La orden de transporte de un pedido urgente nace con prioridad `HIGH` si no mandan otra. El aparato recibe `IsUrgent` y `AwaitingClientApproval` en la sincronización. |
| Roles de fábrica | `SalesSupervisor` («Supervisor de ventas»: sales.pricelist, sales.approve_client, sales.view, clients.read, clients.update, pulse.sales), `SalesOffice` («Oficina de ventas»: sales.view, sales.create, pulse.sales; los de QuickBooks y consignación los suman P5/P6); `Sales` suma clients.create_basic y pulse.sales. |

## Cómo se probó
xunit (InMemory, `--filter`): `ShippingRulesTests`, `SalesPaymentTests` (+ `SalesShippingInvoiceTests`), `ClientProspectTests`, `ClientDocumentTests`, `PermissionSeederTests`, `SalesNoticesTests`, `PulseSalesPanelTests`. Resultado en el reporte de la parte.

## Decisiones tomadas (para que Luis las ratifique)
1. **La «forma de pago» del cliente es su término de pago** (catálogo PaymentTerm ya migrado: CHEQUE/CASH/ACH, NET15…NET60, PK_BY_REP, CONSIGNMENT, COD). No se agregó otra columna al cliente: el prellenado lo traduce (CHEQUE → contra entrega cheque, NET30 → crédito 30, etc.). Un cliente «COD» sin decir cómo deja escoger cheque/efectivo/ACH sin marcar «cambió».
2. **El término solo va con Crédito**; «vendedor recoge cheque» es el término `PK_BY_REP` (crédito).
3. **Consignación**: la orden de un cliente de consignación nace con la casilla marcada; cambiar la forma de pago la quita; marcarla con otra forma es 400.
4. **Entrega o recogido**: `DeliveryMode` NULL se deduce del consignatario (con consignatario = entrega), para no cambiar las órdenes de antes.
5. **El envío con IVU** usa la tasa de la venta de producto (respeta la exención del cliente) cuando el ajuste «lleva IVU» está encendido y el módulo de impuestos también. Se guarda aparte (`ShippingTaxAmount`); `Subtotal`/`TaxAmount` siguen siendo solo de productos (los análisis por producto no cambian).
6. **El envío se recalcula** al guardar el borrador, al solicitar y al modificar una línea; una vez facturado no cambia. Cambiar los ajustes de la compañía no toca las órdenes ya solicitadas.
7. **Prospecto**: nace PROSPECTO con `StatusService.StartAtAsync` (solo la inicial o una etapa del pipeline de menor orden). El cambio de estatus genérico exige `sales.approve_client` para sacar a un prospecto (si no, 403). Rechazar exige comentario; aprobar no (el comentario de constancia lleva quién, límite y término).
8. **Un cliente SUSPENDIDO no puede solicitar órdenes** (antes sí se podía): sin esto el rechazo no detenía nada.
9. **Alta mínima**: obligatorios negocio, contacto, teléfono de 10 dígitos y dirección física (línea 1 y pueblo); correo, SURI, DBA (va a «razón social»), postal y forma de pago opcionales. Los teléfonos y el correo van en el contacto principal. El pueblo no se valida contra la lista en el servidor.
10. **Documentos**: el vendedor (clients.create_basic) solo sube o quita documentos mientras el cliente es prospecto; después, clients.update.
11. **Avisos**: la campana necesita una regla en Sistema → Reglas de aviso (como todos los avisos de ventas: no se siembra ninguna); por eso además van filas en «Necesita tu atención», que salen solas por permiso.
12. **pulse.sales** lo traen TenantAdmin, Ventas, Supervisor de ventas y Oficina de ventas.
13. **Urgente → prioridad HIGH** en la orden de transporte que crea la entrega (si no mandan otra).
14. **No hay PDF** de la orden de venta ni de la factura en el sistema: los datos (envío, consignación, firma) quedan en los DTO para que la pantalla los imprima.

## Pendiente / para otras partes
- Subir la firma dibujada (StoredFile SIGNATURE de la orden) y `SubmittedWithAuth` (reautenticación): P3/P4 (las columnas y la regla «exige firma» ya están).
- Las cifras de QuickBooks y consignación del panel Ventas (`InvoicesToQuickBooks`, `ConsignmentUnpaid`) quedan en null hasta P5/P6.
- Frontend: claves i18n de los códigos de «Necesita tu atención» (`SALES_CLIENT_PENDING`, `SALES_ORDER_URGENT`) y del panel `SALES`; pantallas de la ficha, de la orden y de Ajustes → Ventas.
- Manual y FAQ del lote (capítulo 18 y el de clientes): los mensajes exactos están en `ShippingRules`, `SalesPaymentRules`, `SalesOrderExtraRules` y `ClientProspectRules`.
