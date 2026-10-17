# Lote 100 — Computadoras de confianza y candado de cuenta (portal de clientes, 4) (2026-10-17)

Segundo lote de seguridad del portal ([p10-portal-diseno.md](p10-portal-diseno.md)). Luis pidió que el sistema guarde desde qué computadora se entró y que otra computadora vuelva a pedir un correo de confirmación (30 días), y que **todos los logins** estén protegidos contra fuerza bruta. Hoy los usa el login del staff; los portales del cliente y del consignatario los reutilizan.

## Qué se construyó
- **Bloqueo con espera que crece** (`LockoutPolicy`, `AuthService.Lockout.cs`): 1, 5, 15 y 60 minutos (se reinicia a las 24 h); lo usan login, MFA, reautenticación, cambio de contraseña, código del correo del primer ingreso y MFA del restablecimiento. Columnas `LockoutLevel` y `LockoutLevelAtUtc` en `AspNetUsers` (`update.sql` (ai)). Correo de aviso al dueño. Respuesta y tiempo iguales para usuario inexistente, inactivo o bloqueado (se hace una verificación de contraseña de relleno).
- **Tope por IP** en login y `mfa/verify`: 60 por minuto (`RateLimiting:LoginPerMinute`), política `login`.
- **Computadoras de confianza** (`TrustedBrowser`, `update.sql` (aj); `TrustedBrowserService`): marca secreta del navegador (solo su SHA-256 en el servidor), correo con enlace de 24 h y un solo uso que confirma la computadora que lo intentó aunque se abra desde otro aparato, confianza de 30 días **sin uso** (cada ingreso corre la ventana), reenvío cada 2 min y tope de 10 correos al día, cambiar o restablecer la contraseña quita todas. Login: `browser_confirmation_required` (+ `BrowserConfirmationDto`); `POST /auth/confirm-browser`; `GET/DELETE /auth/trusted-browsers`.
- **Web:** marca de la computadora en cada login, pantalla «Confirme esta computadora», página `/confirm-browser`, panel «Mis computadoras de confianza» en Mi cuenta → Sesiones; openapi y cliente generado; textos es/en.
- Manual 25, manual de pantallas L100 con capturas, FAQ, diseño del portal actualizado.

## Cómo se probó
- xunit `LoginHardeningTests` (10): política 1/5/15/60 y reinicio a 24 h, el primer, segundo y tercer bloqueo con su duración y el correo de aviso, usuario inexistente = misma respuesta, etiqueta del navegador y marca válida, apagado por omisión, ciclo completo (pendiente → correo con enlace, navegador e IP → reenvío limitado → enlace inventado o reusado falla → confirmar → de confianza 30 días → otra computadora pide confirmar → sin marca no manda correo), vencimiento a los 30 días sin uso y ventana corrida, lista/quitar (404 exacto), quitar todas al cambiar la contraseña. Pruebas de bloqueo existentes ajustadas al primer bloqueo de 1 minuto. Suite completa.
- vitest: marca estable, pantalla de confirmación (manda la marca, mensaje de correo enviado o no, volver), enlace del correo (ok, vencido, sin marca) y el panel (lista, «Esta computadora», quitar). Playwright contra el API real con la función encendida, escritorio y móvil: computadora nueva → confirmar → entrar → aparece «Esta computadora» → un segundo contexto vuelve a pedir confirmación → quitar → vuelve a pedir; y el candado (5 fallos y la correcta ya no entra, igual que un usuario inexistente).
- Contra SQL real: `db-init` aplicó (ai) y (aj).

## Decisiones a revisar con Luis
1. **Viene apagada** (`Auth:TrustedBrowsers:Enabled=false`). Encendida, todo el que entre desde una computadora nueva **necesita recibir el correo**; si el envío de correo falla en producción nadie podría entrar desde una computadora nueva. Hay que encenderla con Brevo configurado y `Auth:PasswordReset:WebBaseUrl` puesto. También decide si aplica al **staff** (hoy el staff ya tiene MFA e «IP preferida»); el portal del cliente y el consignatario la llevarán encendida siempre.
2. **Falta el reto anti-robot** tras varios fallos (necesita proveedor, p. ej. Turnstile). Mientras, el bloqueo por cuenta y el tope por IP.
3. El tope por IP es **60/min** (una oficina entera sale por una IP); se baja con `RateLimiting:LoginPerMinute`.
4. «No confiar en esta computadora» al entrar (equipos compartidos) **no se hizo**: se quita desde Mi cuenta. Si se quiere la casilla en el login, se agrega.
5. La confianza del **MFA a 30 días** para el cliente (no repetir el código) entra con el login del portal (lote 101).
6. El administrador del cliente **no** ve ni quita las computadoras de otros usuarios todavía (lote 101).
7. Las **sesiones de aparato** (app de almacén con PIN) no pasan por esto.
