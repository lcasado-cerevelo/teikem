# Lote 81 — Devoluciones de venta (P6, 3 de 5) (2026-10-16)

Tercer lote de P6 (el servidor de las devoluciones). Siguen: **82** pantallas de devoluciones (web: lista, ficha, pedir, autorizar, recibir, acreditar) y **83** la app del almacén (Recibir → devolución con buenas y dañadas). Quedan para después: avisos (`SalesReturnRequested/Authorized/Received/Damaged`), fuentes de análisis, devolución de kits (por piezas) y de productos con serie, y reclamar al transportista.

## Qué se construyó
- **Entidades** `SalesReturn` y `SalesReturnLine` (update.sql sección **(w)**), dominio de estatus `SalesReturnStatus` (Solicitada → Autorizada → Recibida → Acreditada; Rechazada y Cancelada), catálogo `ReturnReason`, causa de daño **`CUSTOMER_RETURN`**, EntityType `SALES_RETURN`, contador `SALESRETURN` (`DV-#####`), `Tenant.SalesReturnWindowDays` (30), `ReceiptHeader.SalesReturnId`, `CreditNote.SalesReturnId` y los permisos **`sales.return`** (roles que crean órdenes de venta y administrador) y **`sales.return.authorize`** (administrador y roles que cancelan órdenes).
- **`SalesReturnService`** y `/api/v1/sales-returns`: ajustes (ventana), lista, ficha, **devolvible** por orden, pedir, autorizar, rechazar, cancelar, **recibir** y **acreditar**.
- **Pedir:** por línea hasta despachado − devuelto − pendiente en devoluciones abiertas; ventana desde el último despacho (fuera de ella solo con `sales.return.authorize` confirmándolo); kits y series rechazados con su mensaje.
- **Recibir reutiliza el recibo:** crea y confirma un recibo **tipo RETURN** con lo bueno y lo dañado por línea (en una sola transacción); el inventario lo mueve el recibo y lo dañado sigue el flujo de daños (cuarentena o desecho). Lo recibido cuenta en `SalesOrderLine.QtyReturned` y deja de ser facturable; en partes, con «cerrar lo pendiente».
- **Acreditar** crea una nota de crédito en borrador por factura con lo recibido que sí se facturó (Lote 79) y deja la devolución Acreditada.
- Manual (capítulo 20 nuevo), FAQ y README.

## Cómo se probó
- xunit (10 nuevas): reglas puras; pedir con todas sus validaciones, numeración y el tope con devoluciones abiertas; orden sin despachar; ventana (dentro, fuera, con y sin permiso, sin ventana y límites); autorizar, rechazar y cancelar con su estatus y lo que libera; **recibir** (validaciones, recibo RETURN enlazado, lo bueno entra, lo dañado a cuarentena con la causa nueva, `QtyReturned` y lo facturable); recibir en partes, el tope de lo pendiente y «cerrar lo pendiente»; **acreditar** (nota en borrador con su factura y cantidad, devolución Acreditada); lo no facturado sin nada por acreditar pero ya no facturable; listas y aislamiento por compañía. Suite completa contra SQL Server real: 4181 pasan; el SQL corrió dos veces sin error.
- **De punta a punta contra SQL Server real vía API:** orden de 10 con 8 despachadas y facturada; devolvible 8; motivo inválido 400 y 9 de más 422; pedir 3, autorizar, acreditar sin recibir 422, recibir con daño sin descripción 400, recibir 2 buenas y 1 dañada (recibo REC-… tipo RETURN, reporte de daño, `QtyReturned` = 3) y acreditar: nota de crédito en borrador de 60 (3 × 20) y devolución Acreditada.

## Decisiones a revisar con Luis
1. **Una devolución siempre es de una orden de venta** (se dejó fuera la «sin orden identificable, con aprobación» del diseño).
2. **Dañado = declarado al recibir:** el reporte de daño se crea con la causa «Devolución de cliente» y queda en cuarentena (o se desecha de una vez); «reportar un daño suelto» después de recibir se hace desde Daños.
3. **«Volver a vender» = recibo normal** (lo bueno entra por el acomodo o la posición destino del recibo); no hay disposición «cuarentena» separada para lo bueno: lo dudoso se declara dañado.
4. **Autorizar no distingue si el cliente la lleva o se recoge**: lo dice la nota de autorización.
5. **Lo pendiente de una recibida cuenta** contra lo que se puede devolver hasta «cerrar lo pendiente»; no hay cierre automático por tiempo.
6. **Acreditar es un paso aparte** (`billing.credit`) que crea el borrador de la nota; no se acredita solo al recibir.
7. Sin avisos ni análisis todavía.

## Fuera de este lote
Pantallas (82) y app (83), avisos y análisis, kits y series, devolución sin orden, reclamar al transportista.
