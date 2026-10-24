# 26 — Portal del cliente: acceso y aislamiento
<!-- kemi:portal -->

Lote 101 (portal de clientes, 2026-10-17). El portal tiene su **propio login**, separado del del staff, y cada sesión queda ligada a **un cliente**. Este capítulo es del servidor (la app `app-cliente` con sus pantallas es el lote 102). Las invitaciones y los usuarios de portal se administran como siempre desde el expediente del cliente (`portalusers.manage`, módulo CLIENT_PORTAL).

## 1. Cómo entra un usuario de portal
1. **Invitación** (desde el expediente del cliente): le llega un enlace que **vence a las 48 horas** (`Portal:InviteHours`), se usa **una sola vez** y, si se reenvía, el anterior deja de servir. Con el enlace escoge su contraseña (`POST /api/v1/portal-users/accept-invite`); la cuenta queda activa.
2. **Entrar:** `POST /api/v1/portal/auth/login` `{ email, password, clientPublicId?, trustedBrowserToken, captchaToken?, doNotTrust?, deviceInfo? }`. Solo cuentas de **portal**: un usuario del staff **no** entra por aquí ni un usuario de portal por el login del staff.
3. **Computadora nueva:** responde `browser_confirmation_required` y manda un **correo** de confirmación (ver capítulo 25). **En el portal esto es siempre así**, aunque el staff no lo use.
4. **Segundo paso (MFA) obligatorio:** `mfa_required`. Si no tiene MFA inscrito se le exige inscribirlo (con el challenge: `POST /auth/mfa/totp/enroll` y `/confirm`); con el MFA inscrito escribe su código en `POST /auth/mfa/verify` (mandando también `trustedBrowserToken`).
5. **Entra:** recibe sus tokens. Desde esa computadora **no se le vuelve a pedir el código por 30 días** (se anota cuándo lo escribió); a los 31 días lo pide otra vez. La computadora vale 30 días sin uso.
6. **«No confiar en esta computadora»** (`doNotTrust = true`, para equipos compartidos): entra **una vez**, la computadora **no se recuerda** (la próxima vez pide otra vez el correo de confirmación) y el código MFA se pide **siempre**. La pantalla de login (lote 102) **recuerda** que la casilla estaba marcada y la muestra marcada.

Respuestas posibles del login (`status`): `ok`, `mfa_required`, `browser_confirmation_required`, `captcha_required`.

## 2. Un cliente por sesión
- La sesión (y su token de acceso) lleva el **cliente** con el que entró (`cid`) y sobrevive a la renovación. Si la cuenta tiene acceso a **varios clientes**, entra con el de `clientPublicId` (o el primero por nombre) y cambia con `POST /api/v1/portal/auth/switch-client` `{ refreshToken, clientPublicId }` (403 `No tiene acceso a ese cliente.` si no es suyo).
- **Aislamiento (dos filtros):** cada consulta del portal queda limitada a **la compañía y al cliente de la sesión**. Es un filtro de datos del contexto (no solo una comprobación en el servicio): aunque una consulta olvidara filtrar por cliente, no vería lo de otros. Un envío de **otro cliente** (de la misma compañía o de otra) responde **404 igual que una que no existe**.
- La sesión **muere** (401) al renovar si la cuenta se suspende, se da de baja o pierde el acceso a ese cliente, o si el cliente o la compañía se desactivan.
- **Un usuario de portal nunca alcanza las rutas del staff** (403 `portal_scope`: `Esta ruta no está disponible para usuarios del portal.`) aunque su token sea válido; y una sesión del staff no entra a `/api/v1/portal/*` (403 `portal_scope`: `Esta ruta es solo para usuarios del portal.`). Siguen abiertos para el portal la sesión (`/auth/*`: renovar, salir, contraseña, MFA y computadoras de confianza), `/portal/*` y el documento legal.
- El portal exige aceptar el **documento legal** (capítulo 24) como todos.

## 3. Lecturas disponibles (este lote)
<!-- kemi:staff -->
| Endpoint | Qué devuelve |
|---|---|
| `GET /api/v1/portal/me` | La cuenta, el cliente actual y los demás clientes a los que tiene acceso |
| `GET /api/v1/portal/orders?search&status&fromUtc&toUtc&skip&take` | Mis envíos (solo las del cliente de la sesión): conduce, envío, factura del cliente, consignatario, pueblo, servicio, piezas, COD, estatus. Máximo 100 por página |
| `GET /api/v1/portal/orders/{id}` | Un envío mía con la dirección de entrega, los paquetes y su **línea de tiempo** (estatus y cuándo) |
| `GET /api/v1/portal/summary` | El inicio: total de mis envíos, cuántas hay por estatus y las 5 más recientes (Lote 102) |
| `GET /api/v1/portal/notifications?skip&take` | La bandeja de avisos: los resúmenes de mi cliente marcados para el portal, los más nuevos primero; máximo 100 por página (Lote 102) |
| `GET /api/v1/portal/auth/config` | Si el login pide reto anti-robot y con qué llave del sitio |

Los DTO del portal son **propios y mínimos**: no llevan precios cotizados, notas internas, rutas, choferes ni datos de otros clientes. Facturas, COD, avisos y el resto de lecturas llegan en los siguientes lotes.

## 4. Protección del login
- Mismo **bloqueo de cuenta** que el staff (5 fallos → 1, 5, 15 y 60 minutos), mismo mensaje `Credenciales inválidas.` (401) para cuenta inexistente, del staff, inactiva o bloqueada, con el mismo tiempo de respuesta; tope de **60 intentos por minuto por IP**.
- **Reto anti-robot (Cloudflare Turnstile):** apagado por omisión. Encendido (`Auth:Turnstile:Enabled`, `SiteKey`, `SecretKey`; la llave secreta va en la configuración o en una variable de ambiente, nunca en el repositorio), se pide **solo después de 3 fallos seguidos** de esa cuenta (`RequireAfterFailures`); hasta que se resuelva, ni siquiera se mira la contraseña (`status = captcha_required`, y se manda `captchaToken` con lo que da el widget). Si Cloudflare no responde, el reto **no se salta**.
- Todo queda en la bitácora de seguridad con `portal: true`.

## 5. Mensajes y errores
| Situación | Respuesta |
|---|---|
| Cuenta inexistente, del staff, inactiva, bloqueada o contraseña mala | 401 `Credenciales inválidas.` |
| Cuenta de portal sin acceso activo a ningún cliente | 403 `El usuario no tiene acceso activo al portal.` |
| `clientPublicId` que no es de la cuenta | 403 `No tiene acceso a ese cliente.` |
| Ruta del staff con una sesión de portal / ruta del portal con una del staff | 403 `portal_scope` |
| Envío de otro cliente o inexistente | 404 (igual respuesta) |
| Módulo CLIENT_PORTAL apagado para la compañía | 403 `module_disabled` |

## Casos frecuentes
- **«El portal no me deja entrar» (cliente):** ¿aceptó la invitación (vence a las 48 h)? ¿su usuario está ACTIVO en el expediente del cliente? ¿es una computadora nueva (revise el correo)? ¿la cuenta está bloqueada por intentos fallidos?
- **Un usuario del staff no entra por el portal** (ni al revés): es a propósito; cada uno tiene su login.
- **El cliente dice que no ve un envío:** puede ser de otro cliente de la compañía (el portal solo muestra las del cliente con el que entró) o aún no estar creada; el portal responde igual en los dos casos.
