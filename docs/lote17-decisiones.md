# Lote 17 — Acceso: login directo, primer ingreso obligatorio, MFA con QR y app en varias compañías (2026-09-30 / 10-01)

Pedidos directos de Luis después del Lote 16. Commits: `b6ebfb0` (login directo, app Aceptar/editar conteo, recrear base),
`7eac7d5` (app en varias compañías), `0fe425d` (QR del MFA), `7dc0a44` (título "Almacén hoy") y el de este documento
(primer ingreso).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Login directo | Sin pantalla "Elija la compañía": entra a la predeterminada (o la primera por nombre); el selector de la cabecera lista solo las compañías del usuario | `LoginTenantRules`, `AuthService.LoginAsync`, web `LoginPage` (se borró `SelectTenantPage`) |
| App en varias compañías | Un teléfono, un registro por compañía; "¿En qué compañía vas a trabajar?"; una SQLite por compañía; 409 si el código es de una compañía ya registrada (sin gastar el código) | `app-almacen/src/kernel/auth/session.ts`, `kernel/db/database.ts`, `kernel/db/kv.ts`, `app/login.tsx`; `DeviceService.EnrollAsync` |
| QR del MFA | Código QR del enlace `otpauth://` generado en el navegador (`qrcode-generator`), al entrar y en Mi cuenta | `web-app/src/kernel/ui/QrCode.tsx` |
| Primer ingreso | Correo con código de 6 dígitos (Brevo) → contraseña propia → MFA; sin tokens ni PIN hasta completarlo | `AuthService.Onboarding.cs`, `BrevoEmailSender`, `OnboardingPage.tsx`, columnas `AspNetUsers.OnboardingRequired/MustChangePassword/EmailVerifiedUtc` |
| Usuario en varias compañías al crearlo | "También agregar a estas compañías" en Nuevo usuario: misma cuenta, una membresía y los mismos roles por nombre en cada una; solo compañías donde quien crea administra usuarios; todo se valida antes de crear (`GET /users/assignable-companies`, `alsoTenantIds`) | `UserAdminService.CreateUserCoreAsync`, `UsersTab.tsx` |
| Pulso | El título de la franja dice solo "Almacén hoy" | `WarehouseDayBand.tsx` |
| Recrear la base | `scripts/recrear-base.ps1` (siempre recrea; lee el MSWM de producción por `ConnectionStrings__LegacyMswm`) | `docs/migracion/recrear-base.md` |
| Códigos de recuperación | Descargar (.txt), casilla obligatoria "Ya los guardé", "Generar códigos nuevos" en Mi cuenta (`POST /auth/mfa/recovery-codes`, AAL2) y correo de aviso cuando un admin reinicia el MFA | `RecoveryCodes.tsx`, `AuthService.RegenerateRecoveryCodesAsync`, `AdminResetMfaAsync` |

## Decisiones del dueño (2026-09-30)

- Compañía al entrar: no se pregunta; selector en la cabecera solo con las compañías con acceso.
- App: registrar otra compañía agrega; una base SQLite por compañía; aviso si el código es de una compañía ya registrada.
- Primer ingreso: correo por **Brevo** (variables `Brevo__ApiKey`, `Brevo__FromEmail` del servidor), **código de 6 dígitos**,
  orden **correo → contraseña → MFA**, para **usuarios nuevos y existentes**.
- Administrador de plataforma: no se asigna desde Usuarios (escalada de permisos); queda el de la siembra
  (`Seed:Demo:PlatformAdminEmail`).
- Rol **Operador de almacén** (2026-10-01): la plantilla toma los 18 permisos que tenía en Advance Logistics (más ajustar y
  administrar inventario y administrar almacenes; sin conciliar COD, sin rutas y sin compras: no recibe contra orden de compra)
  para todas las compañías al recrear. Los roles son por compañía: editar el de una compañía no cambia las otras. El rol
  propio "Warehouse" de la demo se deja perder. El smoke prueba los 403 de esos permisos con Solo lectura.

## Cómo se probó

- Backend: `LoginTenantRulesTests`, `OnboardingTests` (orden de pasos, código malo, misma contraseña, login a mitad),
  `DeviceServiceTests` (409 de compañía repetida sin gastar el código), `DeviceContractsTests`. Toda la suite pasa salvo pruebas
  que dependen de la ruta del repositorio cuando se compila en otra carpeta (el API corría desde Visual Studio).
- Web: `auth.test.tsx` (login directo; recorrido completo del primer ingreso hasta el MFA), `account.test.tsx` (QR), `Pulse.test.tsx`.
- App: 189 pruebas (`deviceAuth.test.ts` con varias compañías y la migración del registro viejo; `loginCompanies.test.tsx`).
- CI: el smoke y los recorridos e2e corren con `Auth__Onboarding__Enabled=false` (crean usuarios y entran con ellos).

## Decisiones a revisar

1. **Nadie queda exento** del primer ingreso (2026-10-01, Luis: "no quiero darlo por sentado"): tampoco los usuarios de la demo
   ni el admin de plataforma; el correo se crea sin confirmar. `Seed:Demo:CompleteOnboarding=true` solo para correr los recorridos
   e2e en local; en CI el primer ingreso se apaga con `Auth__Onboarding__Enabled=false`.
   El admin de plataforma ve en el selector de la cabecera todas las compañías activas aunque sea miembro de una.
2. **MFA después del primer ingreso**: queda activo porque el usuario ya lo configuró; Mi cuenta todavía permite desactivarlo
   (con reautenticación). Si debe ser imposible desactivarlo, es un cambio aparte.
3. **Sin límite propio de reenvíos** del código de correo (solo el límite por IP de 60/min de la ruta). Revisar si Brevo cobra
   por correo.
4. El primer ingreso **no se probó contra Brevo real** desde aquí (la máquina local no tiene las variables); en Development el
   código viene en la respuesta y se ve en pantalla como "solo en desarrollo".
5. Una base SQLite por compañía en el teléfono: lo pendiente de una compañía se envía solo cuando alguien entra a esa compañía.
