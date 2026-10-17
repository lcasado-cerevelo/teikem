# Lote 79 — Notas de crédito (P6, 1 de 5) (2026-10-16)

Primer lote de P6 (devoluciones y notas de crédito). Se empezó por las notas de crédito (servidor) porque no dependen de las devoluciones, cierran el ciclo del dinero (corrigen una factura ya Pagada y dan saldo a favor) y las devoluciones se enlazan después con `CreditNote.SalesReturnId`. Siguen: **80** pantallas de notas de crédito (web); **81** devoluciones de venta (servidor: devolución, recibo tipo RETURN con buenas y dañadas, ledger); **82** pantallas de devoluciones; **83** la app (recibir una devolución). Quedan para después: exportar las notas con las plantillas, avisos (`CreditNoteApproved`) y fuentes de análisis.

## Qué se construyó
- **Entidades** `CreditNote`, `CreditNoteLine` y `CreditApplication` (update.sql sección **(v)**), dominio de estatus `CreditNoteStatus` (Borrador → Aprobada → Aplicada; Anulada), catálogo `CreditReason` (devolución, ajuste de precio, falla del servicio, error de facturación), EntityType `CREDIT_NOTE`, contador `CREDITNOTE` (`NC-#####`) y el permiso **`billing.credit`** (propagado a los roles que ya aprueban facturación, a la plantilla y al administrador, también en los roles ya clonados).
- **`CreditNoteService`** y `/api/v1/billing/credit-notes`: lista, ficha, crear, cambiar (borrador), **aprobar (AAL2)**, **aplicar** y anular.
- **Sobre una factura:** cada línea acredita una línea de la factura; el tope es lo facturado de esa línea menos lo que ya acreditan las notas aprobadas o aplicadas, comprobado al crear y **otra vez al aprobar bajo bloqueo de la factura**; el impuesto es la copia de la línea original, proporcional a lo acreditado y nunca más del cobrado. **Sin factura:** líneas libres con el motor de impuestos.
- **Aplicar:** descuenta del saldo de una factura abierta del mismo cliente (con la factura bloqueada); la factura pasa a Pagada si queda en cero y la nota a Aplicada cuando se agota. El saldo de una factura ahora es total − pagos − notas aplicadas (ficha, lista, pagos, estado de cuenta y por cobrar).
- **Estado de cuenta y por cobrar:** las notas aprobadas son movimientos de crédito, `UnappliedCredit` (saldo a favor) y `CreditBalance` por cliente.
- **Anular una factura** con notas aprobadas o aplicadas queda bloqueado.
- Manual (capítulo 19 §4d y mensajes), FAQ, README.

## Cómo se probó
- xunit (13 nuevas): reglas puras; nota sobre factura con impuesto copiado; ajuste de precio con impuesto proporcional y reemplazo de líneas; tope con notas aprobadas (comprobado también al aprobar); todas las validaciones con su mensaje; nota libre con el motor de impuestos; borrador/aprobada/anulada; aplicar parcial y total con la factura y la nota cerrándose; pagos y notas comparten el saldo; cliente distinto y saldo de la factura; factura con notas no se anula; estado de cuenta y por cobrar con saldo a favor; listas y aislamiento por compañía. Suite completa contra SQL Server real: 4171 pasan; el SQL corrió dos veces sin error. De paso se arregló una prueba del Lote 77 que comparaba la fecha UTC con la de la compañía.
- **De punta a punta contra SQL Server real vía API:** nota de 40 sobre una factura de 100 (motivo inválido 400; pasarse del tope 422); aprobar con reautenticación (AAL2); estado de cuenta con el crédito y el saldo a favor; anular la factura bloqueado (422); aplicar 25, luego 15 (la nota queda Aplicada) y el saldo de la factura 75; aplicar de más 422.

## Decisiones a revisar con Luis
1. **Un solo permiso `billing.credit`** para crear, aplicar y anular (aprobar con AAL2), como pide el diseño; se asignó a quien ya aprueba facturación.
2. **El tope cuenta solo notas aprobadas o aplicadas**, no borradores: se comprueba al crear y al aprobar, así que un borrador que ya no cabe falla al aprobarlo.
3. **No hay «desaplicar»:** una nota con aplicaciones no se anula; si se aplicó mal, se corrige con otra nota. Se puede agregar un desaplicar con motivo y AAL2 si cobranza lo pide.
4. **Aplicación manual:** el saldo a favor no se aplica solo a la próxima factura; se aplica con el botón/endpoint (así no sorprende en cobranza).
5. **Una moneda por cliente** (como el estado de cuenta).
6. **Las notas no se exportan todavía** con las plantillas de contabilidad; el motor del Lote 77 admite el alcance cuando se pida.

## Fuera de este lote
Pantallas de notas de crédito (80), devoluciones de venta y su recepción (81–83), exportar notas con plantillas, avisos y análisis.
