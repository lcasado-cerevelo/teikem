# Lote 243 — Correo con la orden de venta completa al solicitarla: qué se construyó y decisiones a revisar

Lote G del plan de Solutions. Al solicitar una orden de venta, el aviso «Orden de venta solicitada» (`SALES_ORDER_REQUESTED`) puede mandar un correo con la orden completa: encabezado, datos generales, tabla de líneas con las piezas de los kits, subtotal, envío, IVU por componente, total, quién autorizó y la firma adjunta. Hay una copia para el personal y otra para el cliente. Va sobre el motor de avisos que ya existía (Lote 51); no hay motor nuevo.

## Mapa de lo construido

| Capa | Tablas | Código principal | Endpoints |
|---|---|---|---|
| Destinatario «del evento» | `NotificationRuleRecipient`: el CHECK `CK_NotificationRuleRecipient_Kind` admite `Kind = 'CONTEXT'` (`Value` = clave del evento) | `NotificationRecipientKinds.Context`, `NotificationContextRecipientDef`, `NotificationEventDefRules`, `NotificationProcessor`, `NotificationRuleService` | `GET /api/v1/notifications/events` (`contextRecipients`, `channelDefaults`, `isBlock`); `POST/PATCH /api/v1/notifications/rules` acepta `kind: "CONTEXT"` |
| Variables bloque y plantilla del correo | — | `NotificationVariable.IsBlock`, `NotificationHtml.Compose`, `NotificationRules.RenderText/Merge/SplitBlocks`, `NotificationChannelDefault` (la regla nueva trae la plantilla del correo con `{orderDetail}`) | — |
| Contenido de la orden | — | `SalesOrderEmailContent` (`INotificationContentProvider`: variables, `{orderDetail}`, firma, destinatarios `SELLER` y `CLIENT`), `SalesOrderEmailHtml` / `SalesOrderEmailRules` (HTML y huella), `SalesOrderSeller`, `TaxBreakdownRules` (IVU por componente), `TenantFormatText` (fechas, dinero y teléfono con el formato de la compañía) | — |
| Vista previa y prueba | — | `NotificationRuleService.PreviewAsync` y `TestAsync` (con `reference`) | `POST /api/v1/notifications/preview` (`notifications.manage`); `GET /api/v1/sales-orders/{id}/email-preview?audience=&lang=` (`sales.view`); `POST /api/v1/notifications/rules/{id}/test` con `reference` |
| Contactos del cliente | `ClientContact.ReceivesEmailNotifications` (BIT, default 0) | `ClientContactQueries.NoticeEmailsAsync` y `CardAsync`, `ClientService` | `POST/PATCH /api/v1/clients/{id}/contacts` (campo `receivesEmailNotifications`); la lista devuelve `receivesEmailNotifications` y `hasEmail` |
| Envío con adjuntos y buzón local | — | `ITransactionalEmailSender` (`EmailMessage`, `EmailAttachment`, `EmailAttachmentRules` tope de 5 MB), `BrevoEmailSender` (adjuntos), `SmtpEmailSender` y `EmailSenderSelection` (Mailpit, solo Development) | `GET /api/v1/notifications/channels` (`isLocal`) |
| Aviso al solicitar | — | `SalesOrderService.Notifications` (`orderPublicId`, `orderFingerprint`, clave `REQ-{guid}`) | — |
| Web | — | `EmailPreviewModal`, `RuleModal` (destinatario «Del evento», botón «Vista previa», variable bloque), `ClientPeoplePanel` (casilla y chip), `SettingsSalesSection` (línea de ayuda) | pantallas Reglas de aviso, Clientes y Ajustes → Operación → Ventas |
| Pruebas y CI | — | `scripts/smoke-correo-orden-venta.sh`, `web-app/e2e/lote243-correo-orden-venta.spec.ts`, servicio `mailpit` en `.github/workflows/ci.yml`, `scripts/api-para-pruebas.ps1 -Mailpit` | — |

SQL: una sola sección nueva al final de `Diseño/logistica-db-update.sql` (2026-11-24, Lote 243). Cambia el CHECK de `NotificationRuleRecipient` (el DROP solo corre si la definición vigente no menciona `CONTEXT`) y agrega la columna `ClientContact.ReceivesEmailNotifications`. No hay permisos nuevos, ni datos, ni reglas sembradas, ni DELETE. Los scripts congelados no se tocaron.

Qué lleva el correo de la orden:
- **Personal**: todo lo anterior, más el botón «Abrir la orden en Teikem» (si hay `Notifications:WebBaseUrl` o `Auth:PasswordReset:WebBaseUrl`), las notas, la forma de pago cambiada, el faltante en backorder por línea, cómo se reautenticó el vendedor, «Cliente nuevo por aprobar» y el aviso «Esta orden cambió después de solicitarse; el correo muestra los datos vigentes.».
- **Cliente**: sin nada de lo interno de la lista anterior. `{detail}` sale vacío.
- Idioma: el de la compañía; la copia al cliente usa el idioma de aviso del cliente (`NoticeLang`) y, si no tiene, el de la compañía. Español e inglés.
- Todo valor va codificado para HTML (en la captura, la nota `<b>prueba</b>` sale como texto).
- Tabla con tope de 200 filas (cuenta las piezas de los kits); lo que sobra sale como «… y N líneas más.».

## Cómo se prueba

Resultados que me pasaron (no los volví a correr; esta documentación no corrió build ni pruebas):

1. `dotnet`: **235 pruebas del lote pasan y 38 se omiten** porque requieren SQL Server. Build sin warnings.
2. Web, `npm run check`: **3945 de 3946 pasan**. La única falla es `DispatchMap.test.tsx` «Plano esquemático»; ya fallaba antes del lote y no es de él.
3. Playwright con el API de pruebas sin correo (la captura del Registro muestra el canal «Campanita»): capturas en `docs/manual/frontend/img/l243-*.png`. Las miré y están en el capítulo [L243](manual/frontend/l243-correo-orden-venta.md).

**Pendiente de verificar con SQL Server / CI (no se probó):**
- Las 38 pruebas omitidas (entre ellas `Lote243SqlTests`, `SalesOrderEmailSqlTests`, `NotificationContentSqlTests`, `NotificationsSqlTests`).
- `scripts/smoke-correo-orden-venta.sh` contra un API levantado.
- El recorrido `web-app/e2e/lote243-correo-orden-venta.spec.ts` con Mailpit (`MAILPIT_URL`): es la única prueba que ve llegar el correo de verdad, con HTML y firma adjunta. Sin Mailpit el recorrido solo cubre la vista previa y el canal APP.
- El CI de GitHub con el servicio `mailpit` nuevo en `ci.yml`.

Pasos reproducibles (API de pruebas **sin Brevo**; nunca contra TEST ni producción):
1. `docker compose up -d sqlserver` (o `scripts/dev-sqlserver.sh`) y `dotnet run --project src/Teikem.Api -- db-init`.
2. Levantar Mailpit: `docker run -d --name mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit`.
3. Levantar el API con `powershell -ExecutionPolicy Bypass -File scripts\api-para-pruebas.ps1 -Mailpit` (llave de Brevo vacía, correo al buzón local, página en http://localhost:8025).
4. `MAILPIT_URL=http://localhost:8025 scripts/smoke-correo-orden-venta.sh http://localhost:5000`. El script se niega a correr si el canal EMAIL es real (Brevo).
5. En `web-app`: `WEB_URL=http://localhost:5174 API_URL=http://localhost:5100 MAILPIT_URL=http://localhost:8025 npx playwright test lote243 --project=escritorio --no-deps --trace off --workers=1`.
6. A mano: Sistema → Reglas de aviso → Nueva regla del evento «Orden de venta solicitada». Debe traer «Vendedor de la orden» puesto y el correo con `{orderDetail}`. Agregar «Cliente de la orden», encender EMAIL, pulsar «Vista previa» con el número de una orden, y solicitar una orden. Ver el correo en Mailpit.
7. API: `GET /api/v1/sales-orders/{publicId}/email-preview?audience=CUSTOMER&lang=en` devuelve asunto, HTML y adjuntos sin enviar nada.

## Decisiones tomadas (revisar)

1. **El contenido se arma al enviar, no al solicitar.** La cola solo lleva `orderPublicId` y una huella de líneas y total. Al enviar se vuelve a leer la orden. Si cambió entre tanto, el correo del personal lo avisa («Esta orden cambió después de solicitarse…»). Los avisos publicados antes del lote no traen huella: no avisan cambio y encuentran la orden por su número.
2. **Una sola vez por orden.** La clave de no repetición del aviso es `REQ-{guid}`. Solicitar de nuevo la misma orden no manda otro correo.
3. **Orden cancelada o en borrador al enviar:** el aviso no sale. Cada destinatario queda `SKIPPED` en el Registro con `La orden {n} se canceló antes de enviar el aviso.` o `La orden {n} sigue en borrador.`.
4. **Quién es el vendedor** (`SalesOrderSeller`, un solo lugar; el Lote A lo cambiará cuando la orden guarde su vendedor). El nombre es el representante del cliente (`Client.SalesRepId`, activo) y, si no hay, quien solicitó la orden. «Capturó: X» sale solo si quien la capturó es otra persona. «Vendedor de la orden» recibe a quien la solicitó y al usuario del representante, solo si están activos y con membresía activa en la compañía.
5. **Nuevo tipo de destinatario `CONTEXT`** («Del evento»): la clave la declara el evento (`SELLER`, `CLIENT`) y no hace falta SQL para claves futuras. Una clave que el evento no declara se rechaza. Si una regla vieja tiene una clave que el evento ya no declara, se omite con una advertencia en el log.
6. **«Cliente de la orden»** (copia al cliente, apagada por omisión) recibe el correo principal del contacto principal activo (si no tiene, el correo registrado en el cliente) más cada contacto activo marcado «Recibe avisos por correo» con un correo válido; sin repetir. Solo por EMAIL o SMS; por APP o PUSH no aplica.
7. **«Recibe avisos por correo»** se adelantó del Lote D (plan §3.4). Se puede marcar sin correo (la web lo señala con el chip y el aviso); un contacto inactivo sale de los avisos aunque siga marcado.
8. **La regla nace apagada y sin sembrar.** No hay interruptor nuevo en Ajustes → Ventas: solo una línea de ayuda con un enlace a Reglas de aviso (se ve con `notifications.manage`). El administrador crea la regla y elige destinatarios y canal.
9. **Adjuntos:** solo la firma de quien autorizó, y solo si la plantilla usa un bloque. Tope de 5 MB sumando adjuntos; lo que no cabe se omite con una advertencia en el log (nombre y tamaño, nunca el contenido). Si la firma no se puede leer, el correo sale sin ella.
10. **Vista previa con un documento real** exige además `sales.view` y el módulo `SALES_ORDERS`. La ficha del cliente (dirección, teléfono, registro de comerciante) solo se muestra a quien tiene `clients.read`. El alcance de datos (almacén y cliente) de quien consulta se respeta: una orden fuera de su alcance da 404.
11. **Buzón local (Mailpit) solo en Development.** `EmailSenderSelection` lo elige antes que Brevo aunque haya llave, así que un API de desarrollo con buzón local nunca manda correo real. `SmtpEmailSender` no aplica el desvío de TEST (`Email:RedirectTo`): el buzón ya atrapa todo y las pruebas necesitan ver al destinatario real. Fuera de Development el `Email:Smtp:Host` se ignora.
12. **IVU por componente** (estatal, municipal) a partir del impuesto ya guardado en las líneas, con residuo mayor a nivel de orden: lo que se muestra suma exactamente el impuesto guardado. Un caso ambiguo sale como «IVU (sin desglose)».
13. **Formato de la compañía** (`TenantFormatText`): fecha, hora, dinero, cantidades y teléfono salen con el formato de la compañía.
14. **`SalesOrderEmailRules.MaxHtmlBytes` (100 KB) está declarado pero no se usa:** hoy solo hay tope de 200 filas. Gmail recorta a partir de 102 KB. Falta decidir si se aplica.
15. **Tipografía del texto de arriba:** en las capturas, el párrafo «Se solicitó la orden…» (fuera del bloque) sale con la letra por omisión del visor (serif) y el bloque con Arial. Es el texto de la plantilla, sin estilo. Falta decidir si se le pone la misma tipografía.
16. **En la vista previa**, las etiquetas del correo cambian de idioma pero los datos no (en la captura en inglés, el almacén sigue como «Almacén principal»).

## Lo que queda fuera de este lote (a propósito)

- **Sin PDF:** el correo es HTML; no se adjunta la orden en PDF.
- **Sin permisos nuevos:** se usan `notifications.manage`, `sales.view` y `clients.read`. Sin módulos nuevos.
- Sin reglas de aviso sembradas y sin interruptor nuevo en Ajustes.
- Los demás eventos de ventas y los demás canales (campanita, SMS) no cambian; en ellos `{orderDetail}` sale como una línea de resumen.
- El vendedor guardado en la orden (Lote A) y el resto de los contactos del Lote D.
- No se mandó ningún correo real ni se usó Brevo en este lote.
