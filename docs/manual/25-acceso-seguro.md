# 25 — Acceso seguro: bloqueo de cuenta y computadoras de confianza
<!-- kemi:portal -->

Lote 100 (portal de clientes, 2026-10-17). Dos protecciones que valen para **todos los accesos** (staff hoy; clientes y consignatarios cuando existan sus portales).

## 1. Bloqueo de la cuenta con espera que crece
- Cada **5 intentos fallidos seguidos** bloquean la cuenta. El primer bloqueo dura **1 minuto**, el segundo **5**, el tercero **15** y del cuarto en adelante **60**. Si pasan **24 horas** sin otro bloqueo se empieza de nuevo. Nunca es para siempre, así nadie puede dejar fuera a un usuario real a propósito.
- Cuentan los intentos de: entrar con la contraseña, el código del segundo paso (MFA), la reautenticación, el cambio de contraseña, el código del correo del primer ingreso y el código del MFA al restablecer la contraseña.
- Con la cuenta bloqueada, **ni la contraseña correcta entra**, y el mensaje es el mismo de siempre: `Credenciales inválidas.` (401). Un usuario que no existe, uno inactivo y uno bloqueado responden **exactamente igual** y tardan lo mismo (no se puede averiguar qué usuarios existen).
- Al bloquearse, el dueño de la cuenta recibe un **correo** («Su cuenta se bloqueó temporalmente»: cuánto y desde qué dirección) para que cambie la contraseña si no fue él. Si el correo no está configurado, no se manda (el login no se rompe por eso).
- **Tope por dirección IP:** `POST /auth/login` y `/auth/mfa/verify` aceptan **60 por minuto por IP** (`RateLimiting:LoginPerMinute`); pasado el tope, 429 `Demasiados intentos; espere un minuto e intente de nuevo.` Frena a quien prueba la misma contraseña en muchas cuentas.
- Quedan en la bitácora de seguridad (eventos `LOCKOUT`, con la etapa —`login`, `mfa`, `reauth`, `password_change`— y los minutos) y se ven en **Seguridad y auditoría → Actividad**.
- **Todavía no incluido:** el reto anti-robot (tipo Turnstile) tras varios fallos; necesita contratar un proveedor.

## 2. Computadoras de confianza (30 días)
**Cómo funciona.** La web crea una **marca secreta** aleatoria en el navegador (`teikem.browser`) y la manda en cada ingreso; el servidor guarda solo su resumen. La primera vez que alguien entra bien con su contraseña desde una computadora (navegador) que no se reconoce:
1. No se emiten tokens; el login responde **`browser_confirmation_required`** y la pantalla dice «Confirme esta computadora».
2. Le llega un **correo** con el navegador, la dirección y la hora del intento, y un enlace «Confirmar esta computadora» (vale **24 horas**, se usa una sola vez). El enlace se puede abrir **desde el teléfono**: confirma la computadora donde se intentó entrar.
3. Con el enlace confirmado, vuelve a la pantalla de entrada y entra; esa computadora queda de confianza **30 días sin uso** (cada ingreso corre la ventana otros 30 días).
4. Si no fue la persona, el correo dice que no haga clic y que cambie su contraseña.

**Reglas.**
- Otro navegador, el modo incógnito o borrar los datos del navegador cuentan como **computadora nueva**.
- No se manda más de un correo cada **2 minutos** por computadora ni más de **10 al día** por usuario (nadie puede llenarle el correo a otro).
- **Cambiar o restablecer la contraseña quita la confianza de todas las computadoras** (vuelven a confirmarse).
- Cada persona ve y quita las suyas en **Mi cuenta → Sesiones → Mis computadoras de confianza** (la actual aparece como «Esta computadora»). Quitar una la manda a confirmar la próxima vez.
- Se pide después de la contraseña y antes del segundo paso (MFA); no se pide durante el primer ingreso por invitación (ahí el correo ya se verifica).
- Un cliente que no manda la marca (una integración, por ejemplo) no entra cuando la función está encendida: `Este cliente no identifica la computadora; entre desde la web.`
- Las sesiones de **aparato** (app de almacén con PIN) no pasan por esto.

**Quién la usa.** Decidido con Luis (2026-10-17): **el staff queda como está** (su login no cambia: contraseña, MFA e «IP preferida»). Las computadoras de confianza son para los **portales** (cliente y consignatario) y ahí van **siempre encendidas**; la opción `Auth:TrustedBrowsers:Enabled` queda apagada y solo sirve si algún día se quisiera aplicar también al staff. En los portales además: el **código MFA se recuerda 30 días** por computadora de confianza y existe **«No confiar en esta computadora»** (ver capítulo 26).

**Encenderla para el staff (no recomendado hoy).** Viene **apagada** (`Auth:TrustedBrowsers:Enabled = false`) porque, encendida, todo el que entre desde una computadora nueva necesita **recibir el correo**: hay que encenderla solo con el envío de correo (Brevo) andando y `Auth:PasswordReset:WebBaseUrl` apuntando a la web. Otras opciones: `Auth:TrustedBrowsers:Days` (30), `LinkHours` (24) y, **solo en desarrollo**, `ReturnLinkInResponse` (devuelve el enlace en la pantalla para probar sin correo).

## 3. Endpoints
| Endpoint | Para qué | Quién |
|---|---|---|
| `POST /api/v1/auth/login` `{ email, password, tenantId?, deviceInfo?, trustedBrowserToken? }` | Puede responder `status = browser_confirmation_required` con `browser { email (enmascarado), sent, message, devLink }` | Público (60/min por IP) |
| `POST /api/v1/auth/confirm-browser` `{ token }` | Confirma la computadora con el enlace del correo | Público |
| `GET /api/v1/auth/trusted-browsers` (encabezado `X-Trusted-Browser`) | Mis computadoras de confianza (marca la actual) | Sesión |
| `DELETE /api/v1/auth/trusted-browsers/{id}` | Quita una de mis computadoras | Sesión |

## 4. Mensajes y errores
| Situación | Respuesta |
|---|---|
| Contraseña mal, usuario inexistente/inactivo o cuenta bloqueada | 401 `Credenciales inválidas.` |
| Más de 60 intentos por minuto desde una IP | 429 `Demasiados intentos; espere un minuto e intente de nuevo.` |
| Enlace de confirmación vencido, usado o inventado | 400 `El enlace no es válido o venció. Vuelva a intentar entrar para recibir uno nuevo.` |
| Quitar una computadora que no existe o no es mía | 404 `Computadora no encontrada.` |
| Ya se mandaron 10 correos de confirmación hoy | `browser_confirmation_required` con `Ya se enviaron varios correos de confirmación hoy; revise su correo o intente más tarde.` |
| No se pudo enviar el correo | `browser_confirmation_required` con `No se pudo enviar el correo de confirmación.` (puede reintentar pasados 2 minutos) |

## Casos frecuentes
- **«Me pide confirmar otra vez»:** entró desde otro navegador, en modo incógnito, borró los datos del navegador o pasaron 30 días sin usar esa computadora, o cambió su contraseña.
- **No llega el correo:** revise correo no deseado; espere 2 minutos y vuelva a intentar entrar (se reenvía). Si sigue sin llegar, que el administrador revise que el correo de la plataforma esté configurado.
- **Estoy en una computadora compartida:** al terminar, entre a Mi cuenta → Sesiones → Mis computadoras de confianza y quite esa computadora.
- **Mi cuenta se bloqueó:** espere el tiempo que dice el correo (1, 5, 15 o 60 minutos) y entre con la contraseña correcta; si no la recuerda, «¿Olvidó su contraseña?» (al cambiarla se reinician las computadoras de confianza).
