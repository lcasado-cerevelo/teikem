# Lote 205 — Acceso y sesión del chofer (servidor + app-movil)

## Qué se construyó
- **Chofer = usuario «solo app»** (`UserKinds.Driver`): la web lo rechaza («Los choferes entran con la app del chofer.») y la app rechaza sesiones que no son de chofer.
- **Duración de sesión por compañía** (`Tenant.DriverSessionMinutes`, vacío = la general), con tope en `appsettings` (`DriverApp:MaxSessionMinutes`). Si el chofer trabaja en varias compañías manda **la más permisiva** (`DriverSessionRules`).
- **Una sola sesión para todas sus compañías**: `POST /api/v1/driver-app/auth/company-tokens` rota el refresh token (el vencimiento absoluto no se alarga) y devuelve un token de acceso por compañía; si una compañía exige MFA y la sesión no lo cumplió, esa compañía viene `blocked = mfa_required`.
- **app-movil**: guarda solo el refresh token (SecureStore) y los tokens de acceso en memoria; cada llamada lleva `tenantId` para firmarse con el token de su compañía; renovación única compartida (single-flight). El Perfil muestra las compañías (solo lectura) en lugar del selector.
- La huella solo abre la sesión ya guardada; nunca sustituye contraseña ni MFA.

## Cómo se probó
`DriverSessionRulesTests`, `AuthMfaTests.DriverApp`, `npx tsc --noEmit` en app-movil.

## Decisiones a revisar
- Tope de sesión en appsettings y regla «la más permisiva» (aceptada por Luis).
- Falta: primer ingreso dentro de la app (correo personal, contraseña, MFA), tema Automático/Claro/Oscuro, capa de datos y GPS.

## Parte 2 — Primer ingreso dentro de la app (hecha)
- El login del chofer que responde `onboarding_required` ya no manda a la web: la app abre la pantalla **Onboarding** con el token del primer ingreso.
  1. Código de 6 dígitos al correo del chofer (`/api/v1/auth/onboarding/email/send|verify`; el correo personal lo captura el administrador al crear al chofer).
  2. Contraseña propia (`/onboarding/password`; el token se renueva porque el sello de seguridad rota).
  3. Inscripción del MFA con la pantalla del MFA existente; al terminar vuelve al login (contraseña + código).
- Pruebas: `npx tsc --noEmit`, JSON de idiomas válido. Sin prueba contra el API real todavía.
- **Pendiente / decisión**: la confirmación de teléfono nuevo (`browser_confirmation_required`) sigue siendo un aviso «revisa tu correo» que abre un enlace; hacerla 100 % dentro de la app requiere un endpoint de código en el servidor. No se exige que el correo sea «no corporativo»: el servidor no lo puede saber; es una regla de captura del administrador.

## Parte 3 — Tema Automático / Claro / Oscuro (hecha)
- `constants/theme.ts` define `lightColors` y `darkColors`; `hooks/ThemeContext.tsx` da `useTheme()` y `useStyles(createStyles)`. Modo por omisión **Automático** (sigue al teléfono); la elección se recuerda en el aparato (`services/prefsStorage.ts`, no se borra al cerrar sesión) y se cambia en Perfil → Tema.
- Las 30 pantallas/componentes pasaron de colores fijos a la paleta del tema; `components/Text.tsx` (Text y TextInput) pone el color del tema por omisión; la navegación (encabezados, pestañas) toma el mismo tema; la barra de estado cambia con él.
- Contraste WCAG AA verificado con `node scripts/check-theme-contrast.mjs` (ambos temas, todos los pares). Corregí de paso el contraste del distintivo En línea / Sin conexión y del rojo de error.
- La firma del cliente se queda con fondo blanco y tinta azul marino en ambos temas (es una imagen que se guarda).
- Pruebas: `npx tsc --noEmit`, `expo export --platform web` (arma), script de contraste. No se revisó a ojo en un teléfono.
