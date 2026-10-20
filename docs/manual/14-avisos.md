# 14 — Avisos (notificaciones)

Lote 51 (P2.1, 2026-10-14). Los **avisos** le dicen a las personas correctas que pasó algo («llegó un recibo con diferencia», «se
reportó un daño»…) por el canal que cada compañía elija. Este capítulo describe el **motor**: reglas, canales, cola, registro y la
bandeja de cada usuario. La pantalla para armar las reglas y la campanita de la web llegan en el Lote 52; los avisos de cada módulo
(recibos, daños, conteos…) se conectan en lotes aparte, uno por módulo. Hoy existe un **evento de prueba** para probarlo todo.

## 1. Cómo funciona

1. Un módulo **anota** el aviso en una cola **dentro de la misma operación** (la misma transacción). Si la operación se revierte, no
   se avisa nada; si el aviso falla después, la operación **nunca** se deshace.
2. Un servicio de fondo (cada 10 segundos) toma lo pendiente, busca las **reglas activas** de esa compañía para ese evento, arma los
   destinatarios y el texto de cada canal y lo envía.
3. Cada envío queda en el **registro** (evento × regla × canal × destinatario). Nunca se avisa dos veces lo mismo: un reintento solo
   repite lo que falló.

## 2. Permisos y dónde

- **Reglas, envío de prueba y registro**: permiso **`notifications.manage`** («Gestionar las reglas de aviso»; lo trae el rol **Admin de
  compañía** y se agregó a los roles de administrador que ya existían). Módulo **Sistema** (siempre encendido; los avisos no son un módulo).
- **Bandeja** (la campanita): sin permiso especial; cada usuario ve y toca **solo la suya**.
- API: `/api/v1/notifications/*` (eventos, canales, reglas, prueba, registro) y `/api/v1/inbox/*` (bandeja). No existe DELETE: una regla se apaga.

## 3. Los eventos

Cada módulo define los suyos en código (con sus variables y textos por omisión). Hoy: **`TEST` — Aviso de prueba**, con las variables
`{message}` (mensaje) y `{user}` (quién lo disparó). Se dispara con `POST /api/v1/notifications/test-event` y lo reciben las reglas
**activas** de ese evento. Un evento **opcional** puede silenciarse en la bandeja de cada usuario.

### 3.1 Eventos de las órdenes de venta (Lote 66)

Módulo **Órdenes de venta** (los eventos existen siempre; solo se disparan si la compañía usa el módulo). **No viene ninguna regla armada**: el administrador
las crea en Sistema → Reglas de aviso, eligiendo a quién y por qué canal. Todos se pueden silenciar en la bandeja de cada usuario **salvo el de crédito**.

| Evento (código) | Cuándo se dispara | Variables |
|---|---|---|
| **Orden de venta solicitada** (`SALES_ORDER_REQUESTED`) | Al solicitar una orden (validada y reservada). | `{orderNumber}` `{client}` `{warehouse}` `{lineCount}` `{detail}` (si quedó algo en backorder) |
| **Orden de venta despachada** (`SALES_ORDER_SHIPPED`) | Cuando la orden queda despachada **por completo** (un despacho parcial no avisa). | `{orderNumber}` `{client}` `{warehouse}` `{shipped}` (unidades) |
| **Orden de venta cancelada** (`SALES_ORDER_CANCELLED`) | Al cancelarla, y también cuando se cancela sola porque todas sus líneas se cerraron con faltante sin despachar nada. | `{orderNumber}` `{client}` `{warehouse}` `{reason}` |
| **Orden de venta necesita autorizar crédito** (`SALES_CREDIT_OVERRIDE_NEEDED`) | Cuando alguien intenta solicitar una orden que excede el crédito del cliente y no la autoriza. Una vez por situación (el mismo intento repetido no avisa otra vez; si cambia el límite, lo en curso o el monto, sí). | `{orderNumber}` `{client}` `{warehouse}` `{user}` `{limit}` `{exposure}` `{amount}` |
| **Backorder que ya se puede surtir** (`SALES_BACKORDER_AVAILABLE`) | Cuando hay inventario disponible para vender que alcanza para surtir líneas con backorder. Una vez por orden y situación (si entra más, o cambia lo surtible, vuelve a avisar). | `{orderNumber}` `{client}` `{warehouse}` `{lines}` (p. ej. «MED × 3, VIT × 2») `{lineCount}` |

Los cuatro primeros salen **en la misma operación** que los causa (si la operación se revierte, no se avisa). El de backorder lo detecta un trabajo programado
(`SALES_BACKORDER_SCAN`, [capítulo 13](13-trabajos-programados.md)) cada 10 minutos: cubre **cualquier forma** en que entra inventario (recibo, ajuste,
transferencia, liberar cuarentena, cancelar otra orden), sin tocar esos módulos.

### 3.2 Eventos de inventario (Lote 141, 2026-10-20)

Módulo **Inventario por lote y serie** (`WMS_LOTSERIAL`). Los publica el trabajo programado `INVENTORY_ALERT_SCAN` ([capítulo 13](13-trabajos-programados.md)), **todos los
días a las 6:00** (hora de la compañía; «Ejecutar ahora» lo repite). **No viene ninguna regla armada**: el administrador las crea en Sistema → Reglas de aviso (a quién y por qué
canal). Todos se pueden silenciar en la bandeja de cada usuario. Cada evento sale como **un solo aviso con el conjunto completo** (no uno por lote); las variables `{count}` (cuántos)
e `{items}` (un renglón por cada uno, hasta 10 y «… +N») están en todos.

| Evento (código) | Qué avisa | Variables extra y formato de `{items}` |
|---|---|---|
| **Lotes por vencer** (`INVENTORY_LOT_EXPIRING`) | Lotes **con existencia** que vencen hoy o dentro de los próximos N días (N = ajuste de la compañía, 30 por omisión). | `{days}`; `producto · lote · vencimiento · cantidad · almacén` |
| **Lotes vencidos con existencia** (`INVENTORY_LOT_EXPIRED`) | Lotes cuyo vencimiento es **anterior a hoy** y todavía tienen existencia. | `producto · lote · vencimiento · cantidad · almacén` |
| **Productos bajo el mínimo** (`INVENTORY_STOCK_LOW`) | Productos **activos** con mínimo (`minQty`) cuyo disponible total (en mano − reservado, todos los almacenes) es menor que el mínimo. | `producto · disponible · mínimo` |
| **Productos sin existencia** (`INVENTORY_STOCK_OUT`) | Productos activos que **sí tuvieron movimientos** y hoy no tienen nada en mano (un producto recién creado que nunca se recibió no cuenta). | `producto` |
| **Productos sin movimiento** (`INVENTORY_NO_MOVEMENT`) | Productos activos **con existencia** cuyo último movimiento del Kárdex tiene N días o más (N = ajuste, 90 por omisión). | `{days}`; `producto · días · en mano` |

**No se repite lo mismo:** la clave de no repetición es *evento + semana + huella del conjunto*. Si el conjunto no cambió en la semana, no vuelve a avisar; si entra un lote nuevo al
plazo o otro producto baja del mínimo, sale un aviso nuevo con el conjunto completo; y mientras el problema siga vivo hay un **recordatorio semanal**.

Ajustes (web: **Ajustes de la compañía → Operación → «Avisos de inventario»**, `admin.tenant`; API: `PUT /api/v1/tenant/settings`):
`inventoryExpiryWarnDays` (1 a 365, 30) e `inventoryNoMovementDays` (1 a 3650, 90).

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `inventoryExpiryWarnDays` fuera de 1 a 365 | `Los días de aviso de vencimiento deben estar entre 1 y 365.` | 400 (campo `inventoryExpiryWarnDays`) |
| `inventoryNoMovementDays` fuera de 1 a 3650 | `Los días sin movimiento deben estar entre 1 y 3650.` | 400 (campo `inventoryNoMovementDays`) |

**Agregar otro aviso de inventario más adelante** (p. ej. exceso de inventario, conteo con diferencias): un código en `InventoryEventCodes`, su definición en `InventoryEventProvider` y su
detección en `InventoryAlertService`; el motor de avisos, las reglas y la bandeja no cambian.

## 4. Las reglas

Una regla = **un evento** + **un nombre** (único en la compañía, hasta 100 caracteres) + **canales** + **destinatarios**.

- **Canales**: `EMAIL`, `SMS`, `APP` (la campanita) y `PUSH`. Los cuatro existen siempre en la regla y **nacen apagados** con los textos por
  omisión del evento. Cada uno lleva **asunto y texto en español e inglés** con `{variables}` del evento (una variable que el evento no
  tiene se rechaza; una que falte al enviar se deja vacía).
- **Canales no configurados**: **SMS** y **PUSH** todavía no tienen proveedor en Teikem y **no se pueden encender**. **EMAIL** funciona con el
  correo que ya usa Teikem (Brevo); si ese servicio no está configurado en el servidor, tampoco se puede encender. Un canal **APP** siempre
  está disponible.
- **Destinatarios**: un **rol** de la compañía (le llega a todos sus usuarios activos), un **usuario** activo de la compañía, un **correo suelto** o un
  **teléfono suelto** (se guarda; sin SMS no se usa). Sin repetir: si alguien está por rol y por correo, recibe uno.
- **Activar** exige al menos un destinatario y un canal encendido.
- El texto se arma en el **idioma de la compañía** (con respaldo: español, inglés).
- **Enviar prueba** (`POST /rules/{id}/test`): manda el texto de un canal, con valores de ejemplo, **a quien la pide** (no queda en el registro).

## 5. El registro de envíos

`GET /api/v1/notifications/log` (los más recientes primero; filtros por evento, canal, estado y fechas). Estados:

| Estado | Significa |
|---|---|
| `SENT` | Salió. |
| `SKIPPED` | No correspondía o no se pudo, **sin ser una falla**: canal no configurado, el destinatario no tiene correo, o el usuario silenció el aviso. El motivo queda en el registro. |
| `FAILED` | Falló (p. ej. el servicio de correo estaba caído); se reintenta. |
| `PENDING` | Anotado, en proceso. |

**Reintentos**: si algún envío falla, el aviso se reintenta a **1, 5 y 30 minutos**; si el cuarto intento también falla, queda fallido. Una
falla de un destinatario no detiene a los demás, y el reintento repite **solo lo que falló**.

## 6. La bandeja del usuario

`GET /api/v1/inbox` (con `unreadOnly`), `GET /inbox/unread-count`, `POST /inbox/{id}/read`, `POST /inbox/read-all`, y las preferencias
`GET /inbox/prefs` y `PUT /inbox/prefs/{evento}` (silenciar un evento opcional; un aviso silenciado no llega a la bandeja y queda `SKIPPED` en el registro).

## 7. Validaciones y mensajes

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Regla inexistente (o de otra compañía) | `Regla de aviso no encontrada.` | 404 |
| Sin evento | `Indique el evento de la regla.` | 400 (`eventCode`) |
| Evento que no existe | `Evento de aviso desconocido: 'X'.` | 400 (`eventCode`) |
| Cambiar el evento de una regla | `El evento de una regla no se cambia; cree otra regla.` | 400 (`eventCode`) |
| Sin nombre | `Escriba el nombre de la regla.` | 400 (`name`) |
| Nombre de más de 100 caracteres | `El nombre de la regla no puede pasar de 100 caracteres.` | 400 (`name`) |
| Nombre repetido | `Ya existe una regla con ese nombre.` | 409 |
| Canal sin nombre / desconocido | `Indique el canal.` / `Canal desconocido: 'X'. Use EMAIL, SMS, APP o PUSH.` | 400 (`channel(s)`) |
| Encender un canal no configurado | `El canal SMS todavía no está configurado en Teikem.` | 400 (`channels`) |
| EMAIL o APP encendido sin asunto | `El canal EMAIL necesita un asunto.` | 400 (`channels`) |
| Canal encendido sin texto | `El canal APP necesita un texto.` | 400 (`channels`) |
| Asunto de más de 200 / texto de más de 1000 caracteres | `El asunto del canal EMAIL no puede pasar de 200 caracteres.` / `El texto del canal EMAIL no puede pasar de 1000 caracteres.` | 400 (`channels`) |
| Variable que el evento no tiene | `La plantilla usa una variable que el evento no tiene: {zzz}.` | 400 (`channels`) |
| Activar sin destinatarios | `La regla necesita al menos un destinatario para poder activarse.` | 400 (`recipients`) |
| Activar sin canal encendido | `La regla necesita al menos un canal encendido para poder activarse.` | 400 (`channels`) |
| Tipo de destinatario desconocido | `Tipo de destinatario desconocido: 'X'. Use ROLE, USER, EMAIL o PHONE.` | 400 (`recipients`) |
| Correo / teléfono inválido | `Correo no válido: 'x'.` / `Teléfono no válido: 'x'.` | 400 (`recipients`) |
| Rol que no es de la compañía | `Rol no encontrado.` | 400 (`recipients`) |
| Usuario que no es de la compañía o está inactivo | `Usuario no encontrado en esta compañía.` | 400 (`recipients`) |
| La regla cambió desde que la abrió (rowVersion) | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Aviso ajeno o inexistente en la bandeja | `Aviso no encontrado.` | 404 |
| Silenciar un evento que no es opcional | `Ese aviso no se puede silenciar.` | 409 |
| Sin el permiso | «Sin permiso» | 403 |

## 8. Para soporte

- Tablas `NotificationRule`, `NotificationRuleChannel`, `NotificationRuleRecipient`, `NotificationOutbox` (cola), `NotificationLog`, `UserNotification`
  y `UserNotificationPref`; sección **2026-10-14 (g)** de `Diseño/logistica-db-update.sql`. Los eventos NO están en la base: viven en código.
- Configuración: `Notifications:Enabled` (por omisión `true`) y `Notifications:PollSeconds` (por omisión 10). Los comandos de consola no arrancan el servicio.
- Correo: usa la configuración de Brevo ya existente (`Brevo__ApiKey`, `Brevo__FromEmail`). Sin ella el canal EMAIL queda «no configurado»: no se enciende y,
  si una regla vieja lo tenía encendido, el registro dice `SKIPPED`.
- Un aviso que quedó `FAILED` en la cola (`NotificationOutbox.Status`) agotó sus reintentos; el error está en `LastError` y el detalle por destinatario en el registro.
- Cada procesamiento corre **como la compañía, sin usuario** («Sistema»). Varias copias del sistema nunca procesan el mismo aviso a la vez.

## Kemi: «Pregúntame cómo usar el sistema» (Lotes 123–124)

Entre las opciones de Kemi (un clic en la mascota) está **«Pregúntame cómo usar el sistema»**. Kemi **busca**; no usa inteligencia artificial:

1. La persona escribe su pregunta con sus propias palabras (por ejemplo «cómo cambio mi contraseña» o «no me deja entrar») y pulsa **Preguntar**.
2. Kemi busca esas palabras en el **manual** y en la **FAQ** (sin acentos ni mayúsculas, singular o plural, y las formas del verbo juntas: «registro», «registrar»). Las palabras raras pesan más que las comunes, el título pesa más que el cuerpo y la forma de la pregunta decide dónde mirar primero («no me deja», «qué significa» → FAQ; «cómo se hace» → manual). No se dice de dónde salió cada opción.
3. Kemi muestra hasta **5 opciones**; al elegir una se ve su **respuesta** ahí mismo.
4. Kemi pregunta **«¿Te contesté?»**. **Sí** → agradece y deja hacer otra pregunta. **No** → **busca por otro lado**: trae otras opciones, sin repetir las ya mostradas.
5. Si no hay nada que se parezca lo bastante, Kemi lo dice («No encontré nada sobre eso. Prueba con otras palabras.») en vez de adivinar. **Cambiar la pregunta** vuelve a la caja; **Volver** regresa a las opciones de Kemi.

No requiere permiso ni módulo: la ve cualquier persona con Kemi despierta. El índice se baja **solo la primera vez que alguien pregunta** (es un archivo aparte, no pesa en el arranque de la web) y trabaja en el propio navegador: sin servicio, sin costo y sin enviar la pregunta a ninguna parte.

**De dónde sale lo que Kemi sabe:** de `docs/manual/*.md` (capítulos y la FAQ), convertidos a un índice con `npm run help:index` (lo corre `npm run check`); al escribir o corregir el manual o la FAQ el índice se regenera y Kemi lo sabe en cuanto se compile la web. El manual está solo en español. **En inglés** Kemi entiende las palabras comunes (order, invoice, password, delivery, driver…: se acompañan de su equivalente en español para buscar) y avisa que sus respuestas salen en español.

**La pantalla donde está la persona cuenta:** lo documentado de esa pantalla sube en las opciones (estando en Órdenes, una pregunta de «cancelar» trae primero lo de Órdenes; en Rentas, lo de Rentas; en Facturación, lo de Facturación…).
