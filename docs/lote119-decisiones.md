# Lote 119 (P10) — lo que se puede hacer ya del portal del cliente (dictado de Luis, 2026-10-18)

## Qué se construyó
1. **Comprobante de entrega en el portal.** `PortalOrderDto.DeliveryProof` (`PortalDeliveryProofDto`): quién recibió, cuándo, si lo registró el personal (sin decir quién), si firmó el consignatario y las coordenadas solo si se encontraron. Tarjeta en la ficha de la orden de la app.
2. **Facturas del cliente, solo lectura.** `PortalInvoiceService` + `GET /portal/invoices` y `/portal/invoices/{id}`: solo las del cliente de la sesión (filtro global y condición explícita), nunca borradores, con pagado, acreditado y saldo, líneas con su orden y pagos recibidos sin los anulados; exige el módulo BILLING. Pantallas «Mis facturas» y la ficha (sin ninguna acción de pago).
3. **Preferencias de aviso de cada persona** (la tabla `PortalNotificationPref` del diseño original nunca se usó; se creó `PortalNoticePref`, sección (bc) de `update.sql`). La compañía manda (techo = `ClientNotificationSetting`); la persona solo apaga. Se aplica al **correo** (`ClientNotificationService` no manda el aviso a quien apagó el correo de ese aviso) y a la **bandeja** (`PortalService.ListNotificationsAsync` no le muestra lo que apagó). `GET/PUT /portal/notice-prefs`. Pantalla «Mis avisos» en Ajustes.
4. **Lista de consignatarios.** Ya existía (la pantalla «Avisos a mis consignatarios» lista todos los del cliente con buscador, marca de aviso y acceso). Se **rotuló** como lo que es («Mis consignatarios» / «Ver mis consignatarios»).

## Cómo se probó
- **xunit:** `PortalAccessTests` (+1: comprobante sin nombre del usuario ni notas, coordenadas solo si se encontró, otro cliente no ve nada), `PortalInvoiceServiceTests` (4: solo mis facturas sin borradores, saldo sin pagos anulados, línea y pago sin notas internas, 404 igual para ajena/borrador/inexistente, sin BILLING no hay facturas, solo lectura), `PortalNoticePrefServiceTests` (4: techo de la compañía, apagar sin afectar a otros, nada que guardar si todo está encendido, aviso inexistente 400, sin acceso) y `ClientNotificationTests` (+1: quien apagó el correo de ese aviso no lo recibe y los demás sí). Suite completa verde.
- **jest** (app del cliente): 126 pruebas (lista y ficha de facturas, Mis avisos, comprobante en la ficha de la orden).
- **Playwright** (`lote119-portal.spec.ts`, escritorio y móvil) contra el API y SQL Server reales: cliente con persona administradora, orden entregada desde la web, corrida de facturación aprobada (factura emitida) y avisos encendidos; comprobante en la ficha, facturas (lista, filtro, ficha, 404 de una ajena), Mis avisos (se apaga el correo y el API lo confirma) y Mis consignatarios.

## Decisiones a revisar
1. **El cliente no ve las notas de la entrega** (pueden ser internas) ni el nombre del usuario que la registró: solo «registrada por el personal de la compañía».
2. **El comprobante muestra «Ver en el mapa»** con las coordenadas cuando se encontraron (las del consignatario o las de Google).
3. **Las preferencias son por persona y solo apagan.** Los «correos extra» que la compañía anota para un aviso no son de una persona: no se apagan desde aquí.
4. **Las facturas anuladas sí salen** (con el chip «Anulada»); los borradores no.
5. **Sin PDF ni descarga de la factura** todavía: solo consulta en pantalla.
6. **Facturas en la web del personal:** sin cambios.

## Qué quedó fuera de P10
Configuración, perfil e indicadores del cliente (siguiente), documentos adjuntos (falta decidir dónde se guardan archivos) y el seguimiento en vivo (espera al rastreo de la app del chofer, lo último de todo).
