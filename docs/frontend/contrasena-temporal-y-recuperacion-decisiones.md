# Recuperar contraseña: «Olvidé mi contraseña» y contraseña temporal del administrador (2026-10-07)

## Qué se construyó
- Servidor: `POST auth/forgot-password` y `POST auth/reset-password` (anónimos, 5/min por IP, respuesta genérica); `POST users/{id}/temporary-password` (`admin.users` + AAL2). Columnas nuevas en `AspNetUsers` (`TempPasswordExpiresUtc`, `PreviousPasswordHash`, `PreviousOnboardingRequired`, `PreviousMustChangePassword`) en `Diseño/logistica-db-estructura.sql`.
- Web: enlace «¿Olvidó su contraseña?» en el login, páginas `/forgot-password` y `/reset-password`, acción «Contraseña temporal» en Usuarios.

## Cómo se probó
Servidor: 11 pruebas nuevas (`PasswordRecoveryTests`; 3244 en total), `db-init` y `db-update` sobre SQL Server local. Web: pruebas de las dos páginas y del diálogo (1355 en total, `npm run check`). Sin probar con el correo real (Brevo) ni en un navegador.

## Decisiones a revisar
- La temporal reemplaza la contraseña durante 10 min (la anterior no sirve en ese lapso); el restablecimiento es perezoso: ocurre en el siguiente intento de entrar después del vencimiento.
- La temporal entra por el flujo de primer ingreso (cambio obligatorio + MFA); no se manda por correo.
- El enlace del correo envuelve el token de Identity con un límite de 60 min (`TimeLimitedDataProtector`) porque el token de Identity está configurado a 48 h por las invitaciones del portal.
- `Auth:PasswordReset:WebBaseUrl` hay que configurarla en cada ambiente; sin ella no sale el correo.
- Sin Cloudflare Turnstile en el login ni en «Olvidé mi contraseña» (no existe en el proyecto). La defensa hoy es el límite por IP, el bloqueo por cuenta y la respuesta genérica.
