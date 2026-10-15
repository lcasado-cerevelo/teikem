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
