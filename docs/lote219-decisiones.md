# Lote 219 — Contraseñas: ojito, mayúsculas, mensajes claros, marca y recuperar (2026-10-25)

## Pedido de Luis
Para la página de login y **donde quiera que haya un password**: el **ojito** para revelarlo; la **marca de la compañía arriba** (con una sola compañía en la instalación sale la suya; con varias, la última ingresada); **recuperar la contraseña por correo sin saltarse el MFA**; **aviso de mayúsculas activadas** y **mensajes de error claros**.

## Qué ya estaba y qué se hizo
| Pedido | Ya estaba | Se hizo |
|---|---|---|
| Ojito | Login de la web, primer ingreso y restablecer; `app-movil` (FormInput); `app-cliente` con un enlace «Mostrar» | **Web**: un solo componente `PasswordBox` (ojito + mayúsculas) y el `TextInput type="password"` del kit ya lo trae → lo tienen **todos** los campos (cambiar contraseña, PIN, reautenticación, integraciones, usuarios…); **portal**: `PasswordField` con el ojito dentro del campo (entrar, invitación, perfil, restablecer); **app móvil**: `PasswordInput` en el primer ingreso y etiqueta accesible del ojito |
| Marca arriba | Web: última compañía → dominio/subdominio → Teikem | **Servidor**: si la instalación tiene **una sola compañía activa**, sale la suya (`source = single`); **portal**: logo/nombre/Teikem arriba del login con la misma regla (y recuerda la última compañía al entrar) |
| Recuperar por correo sin saltarse el MFA | Web del personal: sí, con código MFA obligatorio (servidor, 2026-10-12) | **Personas del portal** (cliente y consignatario) **también** recuperan: antes el servidor no les mandaba correo; ahora el enlace abre el **portal** (`Auth:PasswordReset:PortalWebBaseUrl`, por omisión `Portal:WebBaseUrl`); pantallas «Olvidé» y «Elegir contraseña» en `app-cliente`; **app móvil**: «¿Olvidaste tu contraseña?» (el enlace abre la web) |
| Aviso de mayúsculas | No | Web y portal (en la web); en el teléfono el sistema no lo informa → solo ojito; los PIN no avisan |
| Mensajes claros | «Credenciales inválidas.» tal cual | Web, portal y app móvil traducen por código/estado: credenciales (qué revisar), demasiados intentos, servidor caído, sin red |

## Decisiones tomadas (revisar)
1. **El servidor sigue respondiendo lo mismo** ante correo inexistente, contraseña mala o cuenta bloqueada (no revela qué correos existen); por eso el mensaje claro es **«qué revisar»** (mayúsculas, ojito, bloqueo temporal) y no «la contraseña está mal» vs «el correo no existe».
2. **El aviso de mayúsculas** solo puede salir al **teclear** (el navegador no lo dice antes) y **no existe en el teléfono**.
3. **Portal sin dirección configurada = sin correo**: si el ambiente no tiene `Portal:WebBaseUrl` (o `Auth:PasswordReset:PortalWebBaseUrl`), la persona del portal no recibe el correo (la respuesta sigue siendo la misma). En producción la pone el instalador con `-UrlPortal`.
4. **App móvil**: el enlace abre la **web** (no hay pantalla de restablecer dentro de la app); `BRAND.logo` sigue siendo el de cada variante de la app (no hay «última compañía» ahí).
5. Límite del servidor: `forgot-password` y `reset-password` aceptan **5 por minuto por IP y por ruta** (`RateLimiting:PasswordResetPerMinute`).

## Cómo se probó
- **Servidor** (xunit): marca con una sola compañía activa, con varias (no adivina) y con la única restante tras inactivar una; recuperar para una persona del **portal** (enlace al portal, sin dirección = nada, y con MFA inscrito el código sigue siendo obligatorio).
- **Web** (vitest): `PasswordBox` (ojito, mayúsculas con `modifierCapsLock`, PIN, kit), `loginErrors`, login con el mensaje nuevo. **Portal** (jest): `PasswordField`, `loginErrorMessage`, `pickPublicSlot`, pantallas «Olvidé» y «Elegir contraseña» (MFA obligatorio, código malo, validaciones), login (ojito, olvidé, errores). **App móvil**: `npm run test:login`, `tsc`.
- **Playwright con el API real** (`lote219-contrasenas.spec.ts`, escritorio y móvil): web del personal (ojito, mayúsculas, mensaje) y portal (logo, ojito, mayúsculas, mensaje, pedir el enlace —con un correo inexistente la respuesta es la misma—, elegir la contraseña, que pida el código MFA, código malo, código de recuperación bueno, la vieja ya no entra y la nueva sí). Capturas `l219-*`.
- **No probado**: el correo real de recuperación (sin proveedor de correo aquí: se usó el enlace de desarrollo); la app móvil en un teléfono; el aviso de mayúsculas con un teclado real (se simula el evento).
