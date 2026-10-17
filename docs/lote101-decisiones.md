# Lote 101 — Portal: acceso por cliente (portal de clientes, 5) (2026-10-17)

El corazón de seguridad del portal ([p10-portal-diseno.md](p10-portal-diseno.md)): un login propio para los usuarios de portal y datos limitados al cliente de la sesión. Es solo servidor; la app `app-cliente` es el lote 102.

## Qué se construyó
- **Login del portal** (`POST /api/v1/portal/auth/login`, `AuthService.Portal.cs`): solo cuentas PORTAL con acceso ACTIVO a un cliente (`PortalUser` ACTIVE, cliente y compañía vivos). Mismo bloqueo de cuenta del lote 100, mismo 401 y mismo tiempo para cuenta inexistente/del staff/bloqueada, tope por IP. **Computadora de confianza siempre**, **MFA obligatorio** recordado **30 días** por computadora (`TrustedBrowser.MfaVerifiedAtUtc`), **«No confiar en esta computadora»** (`doNotTrust`, `TrustedBrowser.SingleUse`: entra una vez, no se recuerda, MFA siempre).
- **Reto anti-robot Cloudflare Turnstile** (`ITurnstileVerifier`, `Auth:Turnstile`): apagado por omisión; tras 3 fallos de la cuenta el login pide el token y no mira la contraseña hasta tenerlo; si Cloudflare no responde, no se salta; llave secreta fuera del repositorio.
- **Sesión ligada a un cliente:** claim `cid` y `RefreshToken.PortalClientId` (`update.sql` (ak)), conservado al renovar; la renovación muere si se pierde el acceso al cliente; `POST /portal/auth/switch-client` para cuentas con varios clientes.
- **Segundo filtro de datos:** `TenantContextMiddleware` fija `ScopedClientIds = [cid]` (el filtro global por `ClientId` que ya existía), más `OrderScope(ClientId)` en cada lectura. Orden de otro cliente u otra compañía = 404 igual que una inexistente.
- **Portal y staff aislados** (`PortalScopeMiddleware`, `PortalScopeRules`): una sesión de portal solo llega a `/portal/*`, `/auth/*` y `/legal/*` (403 `portal_scope` en lo demás); el staff no entra a `/portal/*`.
- **Lecturas:** `/portal/me`, `/portal/orders` y `/portal/orders/{id}` (línea de tiempo) con DTO propios y mínimos (sin precios, notas internas, rutas ni choferes).
- La invitación ya existía (vence a las 48 h, un solo uso); ahora **al aceptarla** la cuenta de portal queda sin el «primer ingreso» del staff.
- **Documento legal:** nueva sección «Cookies y almacenamiento del navegador» (versión `2026-10-17.2`, todos aceptan de nuevo).
- Manual 26, FAQ, diseño del portal con lo decidido.

## Cómo se probó
- xunit `PortalAccessTests` (19): rutas permitidas/cerradas del portal, verificador de Turnstile (apagado, secreto/token/IP enviados, falla de red no salta el reto), claim `cid`, login (staff/inexistente/mala = mismo 401, bloqueo, 403 sin acceso o cliente ajeno, computadora nueva aun con la función del staff apagada, MFA y 30 días, «no confiar», reto tras 3 fallos sin poder saltarlo), renovación que conserva el cliente y muere al perder el acceso, cambio de cliente ajeno 403, y **acceso cruzado**: solo las órdenes del cliente, 404 para otro cliente u otra compañía, el filtro global por cliente, y DTO sin campos sensibles. Suite completa.
- Contra SQL real (API y base de desarrollo): invitar → aceptar → login → computadora nueva → inscribir MFA → entrar (`kind = PORTAL`, `cid`) → `/portal/me`, 3 órdenes exactas del cliente A, una orden con su línea de tiempo y sin precio, orden del cliente B = 404 igual que una inexistente, rutas del staff = 403 `portal_scope`, staff en `/portal` = 403, renovar conserva el cliente, cambiar a un cliente ajeno = 403, login siguiente entra sin pedir código, y suspender mata la sesión y el login.

## Decisiones a revisar con Luis
1. **Turnstile:** falta **crear el widget en tu cuenta de Cloudflare** y poner `Auth:Turnstile:SiteKey` y `SecretKey` (la secreta por variable de ambiente, no en el repositorio) y `Enabled=true`. Mientras, el login funciona sin reto.
2. **«No confiar en esta computadora»** obliga a confirmar por correo en cada entrada (la computadora nunca queda de confianza). Si prefieres que, para quien tiene MFA, bastara el código sin el correo, se cambia fácil (el MFA ya demuestra quién es); para el consignatario, que no tendrá MFA, el correo es lo que queda.
3. El **primer cliente** de una cuenta con varios es el de su compañía predeterminada y, a falta, el primero por nombre; no se escoge en el login (el cambio es desde dentro).
4. **Facturas, COD y avisos** del portal no están en este lote: entran con la app (lote 102 y siguientes).
5. El administrador del cliente **no** ve ni quita las computadoras de otros usuarios todavía.
6. La casilla «no confiar» y su recuerdo (preferencia en el navegador/teléfono) los construye la pantalla de login de `app-cliente`; el servidor ya lo soporta.
