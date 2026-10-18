# Lote 114 — Tipo de servicio «Pick up» (2026-10-18)

Luis dictó: los tipos de servicio «pick up» (recojo y traigo) se distinguen de los de recogido y entrega y se tratan distinto: **Sometida → Recogida → En almacén** (final); si se quiere sacar, es otra orden o el cliente viene a buscarlo; **facturable o no según el tipo de servicio (interruptor configurable)**.

## Qué se construyó
- **Marcas del tipo de servicio** (`ServiceTypeMarks`, dominio): `{"pickUp":true,"billable":true}` en el `ExtraJson` del tipo de servicio, con el ajuste de la compañía (`CustomExtraJson`) encima (`ServiceTypeMarkReader`). Sin marcas: no es pick up y sí es facturable. SQL (update.sql, sección (av)): se siembra el tipo `PICK_UP` «Pick up».
- **Escaneo**: en una orden de pick up, **Llegó** lleva a `IN_WAREHOUSE` (Recogida «brincada» si venía de Sometida), las piezas siguientes con Llegó se cuentan aunque la orden ya sea final, **Enrutar/Salir** responden «No» con el mensaje de pick up, y el **cierre del conteo** de una orden En almacén cuenta como el de «Llegó» (lista lo no llegado). `ScanActions.ForStatus(IN_WAREHOUSE)` = Llegó.
- **Cotización**: un tipo de servicio **no facturable** no exige contrato ni tarifa al someter, queda cotizado en cero y no mide crédito (`OrderStatusService.QuoteOnSubmissionAsync`).
- **Facturación**: una orden **En almacén** es facturable sea cual sea el modelo del contrato (`BillingService`); la de monto cero nunca se factura.
- **Web**: Listas → Tipo de servicio → **Ajustar** (y valores propios) muestra los interruptores **Pick up** y **Facturable** y los guarda en el `ExtraJson`.

## Cómo se probó (solo el área)
- xunit `PickUpServiceTypeTests` (15), `FreightBillingTests` (+1: En almacén se cobra con contrato por entrega; cero no) y `OrderSubmissionLifecycleTests` (+1: no facturable cotiza en cero y no cuenta crédito). vitest: `serviceTypeMarks` (3) y `CatalogsPage` (+2).
- Contra SQL real (`db-update` dos veces): un pick up facturable sin tarifa da 422; apagando «Facturable» la orden nace Sometida con monto 0; Recoger, Llegó (→ En almacén), Llegó de la 2.ª pieza («2 de 3»), Enrutar («pick up: se queda»), y Cerrar conteo lista la 3.ª.

## Decisiones a revisar con Luis
1. **Las paradas** de una orden de pick up son las de siempre (recogido y entrega). Si «entrega» para un pick up debe ser el almacén de la compañía en vez del consignatario, hay que decidir de dónde sale esa dirección.
2. **La Sometida de un pick up** se puede llevar directo a En almacén escaneando «Llegó» (Recogida queda brincada); la app del chofer, cuando exista, usará Recoger.
3. **Un pick up no facturable** queda cotizado en **cero** (no sin cotizar) para que la orden no pida contrato ni tarifa y los informes la vean con monto 0.

- (2026-10-18) Recorrido Playwright y capturas: `web-app/e2e/lote114-pick-up.spec.ts` (escritorio y móvil; las marcas del tipo de servicio solo en escritorio porque en móvil las listas son tarjetas), capítulo `docs/manual/frontend/l114-pick-up.md`.
