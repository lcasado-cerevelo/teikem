# Lote 205 — Acceso y sesión del chofer (servidor + app-choferes)

## Qué se construyó
- **Chofer = usuario «solo app»** (`UserKinds.Driver`): la web lo rechaza («Los choferes entran con la app del chofer.») y la app rechaza sesiones que no son de chofer.
- **Duración de sesión por compañía** (`Tenant.DriverSessionMinutes`, vacío = la general), con tope en `appsettings` (`DriverApp:MaxSessionMinutes`). Si el chofer trabaja en varias compañías manda **la más permisiva** (`DriverSessionRules`).
- **Una sola sesión para todas sus compañías**: `POST /api/v1/driver-app/auth/company-tokens` rota el refresh token (el vencimiento absoluto no se alarga) y devuelve un token de acceso por compañía; si una compañía exige MFA y la sesión no lo cumplió, esa compañía viene `blocked = mfa_required`.
- **app-choferes**: guarda solo el refresh token (SecureStore) y los tokens de acceso en memoria; cada llamada lleva `tenantId` para firmarse con el token de su compañía; renovación única compartida (single-flight). El Perfil muestra las compañías (solo lectura) en lugar del selector.
- La huella solo abre la sesión ya guardada; nunca sustituye contraseña ni MFA.

## Cómo se probó
`DriverSessionRulesTests`, `AuthMfaTests.DriverApp`, `npx tsc --noEmit` en app-choferes.

## Decisiones a revisar
- Tope de sesión en appsettings y regla «la más permisiva» (aceptada por Luis).
- Falta: primer ingreso dentro de la app (correo personal, contraseña, MFA), tema Automático/Claro/Oscuro, capa de datos y GPS.
