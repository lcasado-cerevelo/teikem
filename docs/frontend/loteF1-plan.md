# Lote F1 — Núcleo mínimo del frontend web (plan aprobado)

Luis, 2026-09-27: React web + TypeScript, full responsive, recorrido mínimo hasta almacén; decisiones de estilo (portar la maqueta), entrega (zip estático), navegadores (escritorio y tableta, y responsive completo) y ubicación (web-app/ en el mismo repo) con las recomendaciones.

## Objetivo

Dejar el esqueleto sobre el que se montan todas las pantallas: autenticación completa, menú por módulos y permisos, kit de componentes portado de la maqueta, cliente generado del API, catálogos, estatus, campos personalizados, evaluador de reglas, selector de cliente, Pulso de solo lectura y 'Mi cuenta'.

## Fuera de alcance de F1 mínimo

- Roles y usuarios
- Seguridad y auditoría
- administración de catálogos y estatus
- Campos personalizados (administración)
- Vistas, Indicadores y Gráficos (administración)
- Módulos y Ajustes del tenant
- Administración de plataforma
- todo módulo de negocio (van en F2 a F6)

## Piezas

### P0 — Shell, autenticación, cliente del API, i18n y acceso

Orden 1 · agente core

Base de web-app/src: (1) `kernel/api/client.ts` con openapi-fetch sobre schema.d.ts, VITE_API_URL, cabeceras Authorization y Accept-Language, refresh con rotación ante 401 (POST /api/v1/auth/refresh) una sola vez y reintento, `applyProblemDetails(error, form?)` que devuelve {title, code, errors} y `ApiError`. (2) `kernel/auth`: `login(email, password, tenantId?)` con los tres estados de AuthResultDto (ok | mfa_required → pantalla de código TOTP con /api/v1/auth/mfa/verify | tenant_selection → lista de tenants), tokens en memoria + sessionStorage, `logout`, `useReauth()` para el 403 aal2_required (modal de contraseña con POST /api/v1/auth/reauth y reintento de la acción). Lee AuthController y AuthContracts.cs para las rutas y campos exactos. (3) `app/session.tsx`: `useSession()` con `me` (GET /api/v1/me), permisos, módulos, idioma (es/en; cambiar idioma NO reinicia la pantalla ni el menú: solo cambia el diccionario y la cabecera Accept-Language e invalida las consultas), `switchTenant` (POST /api/v1/auth/switch-tenant si existe). (4) `kernel/access`: `<Can perm>`, `useCan`, `<ModuleGate module>` con pantalla 'Módulo apagado' y 'Sin permiso'. (5) `kernel/i18n`: `t()` con es.json/en.json y hook `useT`. (6) `app/AppShell.tsx` responsive: barra lateral por grupos de la maqueta (Operación, Catálogo, Almacén, Análisis, Administración) filtrada por módulos y permisos, colapsable y en cajón bajo 900 px; cabecera con nombre del tenant, selector de idioma, usuario, botón de salir. (7) `app/routes.tsx` con carga diferida, rutas /login, /mfa, /select-tenant, / (Pulso), /account, /forbidden, /module-off. (8) Estilos globales `src/styles/tokens.css` y `base.css` portando la paleta, tipografía, `.pal/.pi/.pb/.ft`, `.chip` y `qbox` de Diseño/teikem-mockups.html (lee solo el bloque <style> de la maqueta; no copies HTML). (9) Pantalla de login y de MFA. Pruebas: applyProblemDetails, refresh una sola vez, Can/ModuleGate, t() con clave faltante. Documenta cada hook en KIT.md.

Archivos: `web-app/src/main.tsx`, `web-app/src/app/*`, `web-app/src/kernel/api/client.ts`, `web-app/src/kernel/api/problem.ts`, `web-app/src/kernel/auth/*`, `web-app/src/kernel/access/*`, `web-app/src/kernel/i18n/*`, `web-app/src/styles/*`, `web-app/src/features/auth/*`

### P1 — Kit de componentes y plantillas de pantalla

Orden 2 · agente core

`kernel/ui`: Panel, DataTable<T> (TanStack Table; orden por columna con flecha, paginación del servidor, tarjetas bajo 720 px, sin scroll horizontal de página, `rowActions` con guardas), Filters + SelectFilter + DateRangeFilter + SearchSelect (múltiple con buscador), QBox + `matchesQ(q, ...texts)`, Chip (nunca envuelve), Modal (mismo padding del shell), ConfirmDialog, Form + Field + TextInput/NumberInput/Select/DateInput/Toggle/TextArea integrados con react-hook-form y zod (errores del servidor bajo el campo vía applyProblemDetails), EmptyState, Spinner, Toast (`toast.success/error`), ClientPicker (GET /api/v1/clients?search=, muestra Code · Name, respeta includeInactive). Plantillas `kernel/ui/templates/ListScreen.example.tsx` y `DetailScreen.example.tsx` completas y compilables (no montadas en rutas). Pruebas unitarias: DataTable ordena y pagina, matchesQ, Field muestra error del servidor, ClientPicker busca. Documenta props en KIT.md.

Archivos: `web-app/src/kernel/ui/*`, `web-app/src/kernel/ui/templates/*`

### P2 — Catálogos, estatus, campos personalizados y evaluador de reglas

Orden 2 · agente core

`kernel/catalogs`: useLookups(domain) (GET /api/v1/catalogs/{entity}, caché 10 min), useStatuses(domain) (GET /api/v1/status/{entity}: code, label, color, stageKind, isInitial), StatusChip, StatusPipeline (dibuja pipeline/lateral/terminal a partir de StageKind y ofrece las transiciones válidas consultando GET /api/v1/status/{entity}/validate; `onTransition(toCode, comment)`), StatusHistory (GET /api/v1/status/history/{entityType}/{id}). `kernel/custom-fields`: CustomFieldsForm(entityType, entityId, form) que renderiza GET /api/v1/custom-fields/definitions por tipo de dato (texto, número, fecha, booleano, opción, multi) y guarda con PUT /api/v1/custom-fields/values/{entityType}/{entityId}. `kernel/dsl/RuleEvaluator.ts`: misma semántica que src/Teikem.Infrastructure/Dsl/RuleEvaluator.cs (and/or/not; eq, ne, gt, gte, lt, lte, contains, startsWith, endsWith, in, notIn, between, isNull, notNull, isTrue, isFalse; validaciones regex/min/max/minLength/maxLength) con los casos de tests/Teikem.Tests/RuleEvaluatorTests.cs portados a vitest. Lee los controladores CatalogsController/StatusController/CustomFieldsController para las rutas exactas.

Archivos: `web-app/src/kernel/catalogs/*`, `web-app/src/kernel/custom-fields/*`, `web-app/src/kernel/dsl/*`

### P3 — Pulso del día (inicio, solo lectura)

Orden 3 · agente pantalla

Pantalla de inicio `/`: GET /api/v1/analytics/pulse → tarjetas de indicadores (nombre, valor formateado con miles/decimales/dólar según el DTO, módulo de negocio como agrupador) y gráficos (Recharts: barra, dona, línea según el tipo del DTO). Selector de rango de fecha del indicador solo si el DTO lo expone; si no, solo lectura. Responsive: tarjetas en una columna a 360 px. Estado vacío cuando no hay indicadores en Pulso. Permiso analytics.view; sin él, la pantalla muestra bienvenida sin datos.

Archivos: `web-app/src/features/analytics/Pulse.tsx`, `web-app/src/features/analytics/api.ts`

### P4 — Mi cuenta: perfil, contraseña, MFA y sesiones

Orden 3 · agente core

Pantalla `/account` con pestañas: Perfil (datos de /api/v1/me), Contraseña (POST /api/v1/auth/change-password o la ruta real de AuthController), MFA (activar: POST enroll → QR con el otpauth URI en imagen generada en cliente con una librería ligera o mostrando la clave manual; confirmar con código; códigos de recuperación mostrados una sola vez con botón de copiar; desactivar con reauth), Sesiones activas (lista y revocar). Lee AuthController y MeController para rutas y campos exactos; cada acción sensible pasa por useReauth cuando el API responde aal2_required.

Archivos: `web-app/src/features/account/*`

## Recorrido Playwright

- Login con admin@teikem.local / Teikem_Admin_2026! → llega a Pulso; la barra lateral muestra grupos según módulos y permisos.
- Login con despacho@teikem.local → el menú NO muestra Administración; /account visible.
- Cambiar idioma a inglés desde la cabecera: la pantalla actual y el grupo expandido del menú se conservan; los textos cambian.
- Pulso muestra al menos un indicador y un gráfico del tenant demo.
- Mi cuenta → pestaña Sesiones lista la sesión actual; cambiar contraseña con la actual incorrecta muestra el mensaje del API bajo el campo.
- MFA: activar muestra la clave y pide código; un código inválido muestra el error del API; cancelar deja MFA desactivado.
- Ruta protegida sin permiso (/forbidden de prueba) muestra la pantalla 'Sin permiso'; módulo apagado muestra 'Módulo apagado'.
- Proyecto móvil (Pixel 7): el menú es un cajón, Pulso apila tarjetas, no hay scroll horizontal (document.documentElement.scrollWidth <= viewport).
