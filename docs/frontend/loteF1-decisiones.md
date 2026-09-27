# Lote F1 — Núcleo mínimo del frontend web: qué se construyó y decisiones a revisar

Plan aprobado: `docs/frontend/loteF1-plan.md`. Contrato del kit: `web-app/KIT.md`.

## Mapa de lo construido

### Pantallas

| Pantalla | Ruta | Acceso | Permiso · módulo | Componentes / código principal |
|---|---|---|---|---|
| Iniciar sesión | `/login` | Pública | ninguno | `features/auth/LoginPage.tsx`, `AuthLayout.tsx` |
| Verificación en dos pasos (MFA) | `/mfa` | Pública (exige el challenge token del paso 1) | ninguno | `features/auth/MfaPage.tsx` (verificar, enrolar, códigos de recuperación) |
| Elegir compañía | `/select-tenant` | Pública (exige selección pendiente) | ninguno | `features/auth/SelectTenantPage.tsx` |
| Pulso del día (inicio) | `/` | Privada, dentro del shell | `analytics.view` · `ANALYTICS` (sin uno de los dos: bienvenida sin datos, no se llama al API) | `features/analytics/Pulse.tsx`, `RangeModal.tsx`, `api.ts`, `format.ts` |
| Mi cuenta · Perfil | `/account` (`?tab=profile`) | Privada | cualquier usuario autenticado | `features/account/AccountPage.tsx`, `ProfileTab.tsx` |
| Mi cuenta · Contraseña | `/account?tab=password` | Privada | cualquier usuario autenticado | `PasswordTab.tsx` |
| Mi cuenta · Verificación en dos pasos | `/account?tab=mfa` | Privada | cualquier usuario autenticado (desactivar exige reautenticación AAL2) | `MfaTab.tsx` |
| Mi cuenta · Sesiones | `/account?tab=sessions` | Privada | cualquier usuario autenticado | `SessionsTab.tsx` |
| Sin permiso | `/forbidden` (no está en el menú; la pinta cualquier ruta sin el permiso exigido) | Privada | — | `kernel/access/AccessScreens.tsx` (`ForbiddenScreen`) |
| Módulo apagado | `/module-off` (ídem, para el módulo) | Privada | — | `kernel/access/AccessScreens.tsx` (`ModuleOffScreen`) |
| Shell (menú lateral + cabecera) | envuelve toda pantalla privada | — | filtra cada entrada del menú por el `perm`/`module` de su ruta (`navigation.ts: visibleNav`) | `app/AppShell.tsx`, `app/navigation.ts`, `app/LangSelect.tsx`, `app/memberships.ts` |
| Modal de reautenticación (AAL2) | se abre sobre cualquier pantalla ante un 403 `aal2_required`, o a pedido de la pantalla (`useReauth().reauth()`) | — | — | `kernel/auth/ReauthProvider.tsx` |

### Núcleo sin pantalla propia (base para los lotes F2-F6)

| Pieza | Código | Qué deja listo |
|---|---|---|
| Cliente del API | `kernel/api/client.ts`, `problem.ts`, `dates.ts` | `openapi-fetch` tipado, `Authorization`/`Accept-Language`, refresh de 401 con un solo vuelo compartido (`refreshOnce`), reintento de 403 `aal2_required` con el modal (`stepUpOnce`), `applyProblemDetails` |
| Autenticación | `kernel/auth/auth.ts`, `tokens.ts` | `login`/`selectTenant`/`verifyMfa`/enrolar-confirmar MFA, tokens en memoria + `sessionStorage` |
| Acceso | `kernel/access/*` | `Can`, `useCan`, `ModuleGate`, `AccessProvider`, `ModuleKeys` |
| Idioma | `kernel/i18n/*` | `t()`, `useT`, `useLang`, `es.json`/`en.json`, cambio sin remontar pantalla ni menú |
| Kit de componentes | `kernel/ui/*` | `Panel`, `DataTable`, `Filters`/`SelectFilter`/`DateRangeFilter`/`SearchSelect`/`SearchMultiSelect`, `QBox`, `Chip`, `Modal`, `ConfirmDialog`, `Form`/`Field` + controles, `Tabs`, `EmptyState`, `Spinner`, `toast`, `ClientPicker`; plantillas `templates/ListScreen.example.tsx` y `DetailScreen.example.tsx` (compilan, no están montadas en rutas) |
| Catálogos y estatus | `kernel/catalogs/*` | `useLookups`, `useStatuses`, `StatusChip`, `StatusPipeline`, `StatusHistory` |
| Campos personalizados | `kernel/custom-fields/*` | `CustomFieldsForm`, conversión y validación (`values.ts`) |
| DSL | `kernel/dsl/RuleEvaluator.ts` | Puerto en TypeScript de `RuleEvaluator.cs` (misma semántica) |
| Estilos | `styles/tokens.css`, `styles/base.css` | Paleta, tipografía y clases de la maqueta (`.pal/.pi/.pb/.ft`, `.chip`, `.qbox`, etc.) |

Ninguna pantalla de negocio (clientes, órdenes, flota, trips, inventario) se construyó en F1: es explícitamente lo que dice `docs/frontend/loteF1-plan.md` que queda fuera de este lote.

## Cómo se prueba

1. `cd web-app && npm ci` (ya instalado en este entorno).
2. `npm run check` (`api:types` sobre `openapi.json` + `tsc -b` + `oxlint src e2e` + `vitest run` + `vite build`). Ejecutado en este
   entorno ahora mismo, verde: 20 archivos de prueba, 144 pruebas, sin errores de tipos ni de lint, build de producción generado
   en `web-app/dist`.
3. Recorrido Playwright (`e2e/loteF1.spec.ts`, proyectos `escritorio` y `movil` = Pixel 7 a 360 px) contra el API real (`API_URL`,
   por defecto `http://localhost:5000`, con `db-init` hecho) y Vite en `:5173`: `npm run e2e`. Cubre los 9 pasos del "Recorrido
   Playwright" del plan (login → Pulso, menú por permisos/módulos, cambio de idioma sin perder pantalla/estado, indicador y
   gráfico en Pulso, sesiones + contraseña con la actual incorrecta, MFA activar/código inválido/cancelar, 'Sin permiso' y
   'Módulo apagado', menú en cajón y tarjetas apiladas a 360 px, sin scroll horizontal en las pantallas del recorrido). Cada
   paso deja una captura en `docs/manual/frontend/img/f1-*.png` para el manual.
4. `.github/workflows/ci.yml` corre `npm run check` en el job `frontend`; el recorrido Playwright se ejecuta localmente con el API arriba.

> **Cómo se verificó realmente** (2026-09-27): en este entorno se levantó SQL Server 2022 y el API (`db-init` + `dotnet run`) y se
> ejecutaron los pasos 2 y 3: `npm run check` verde (20 archivos de prueba, 144 pruebas, build de producción) y `npx playwright test`
> verde en ambos proyectos (9 pruebas pasadas: 7 de escritorio y 2 de móvil a 360 px; cada proyecto salta las del otro). Las 13
> capturas de `docs/manual/frontend/img/` salen de esa corrida. El job `frontend` de CI ejecuta solo `npm run check` (el recorrido
> Playwright necesita el API real y se corre localmente); el enlace a la corrida verde se agrega al pie de este documento.

## Decisiones tomadas (revisar)

1. **Cliente único (`openapi-fetch` + `schema.d.ts` generado)**: ninguna pantalla usa `fetch` directo. El 401 dispara un solo
   refresh compartido aunque varias peticiones fallen a la vez (`refreshOnce`, `client.ts`); si el token ya se renovó mientras la
   petición viajaba, no se repite el refresh, solo se reintenta. Las rutas de login/MFA/refresh/logout/switch-tenant no
   refrescan (su propio 401 es la respuesta del paso); `/auth/reauth` solo refresca el 401 del esquema JWT, no el de credenciales
   con `code`.
2. **403 `aal2_required` intercepted globalmente**: el cliente abre el modal de `ReauthProvider` y reintenta la acción una sola
   vez (`stepUpOnce`); `MfaTab.turnOff` además llama `reauth()` de una vez antes de desactivar MFA, para no depender solo del
   reintento automático.
3. **Tokens en memoria + `sessionStorage`** (no `localStorage`): sobreviven a recargar la pestaña, no a cerrarla ni a otra
   pestaña. Decisión de la pieza P0, no ratificada por Luis explícitamente en el plan; queda documentada aquí para revisión.
4. **Cambiar idioma no remonta nada**: `setLang` solo cambia el diccionario, la cabecera `Accept-Language` e invalida las
   consultas (`session.tsx`); el grupo del menú abierto y los campos escritos en un formulario se conservan (probado en el paso
   3 del recorrido Playwright, incluido un campo de contraseña a medio llenar).
5. **Pulso nunca redirige a 'Módulo apagado'**: sin `analytics.view` o sin el módulo `ANALYTICS`, la pantalla de inicio muestra
   una bienvenida sin datos y sin llamar al API (`Pulse.tsx: canView`), en vez de la pantalla genérica. Es la única ruta con esa
   excepción; el resto usa `RouteGate` (`module` → `ModuleGate`; si no, `perm` → `ForbiddenScreen`).
6. **"Mi rango de fecha" de Pulso es preferencia por usuario, no de la definición**: `PUT .../my-date-range` solo exige
   `analytics.view` (el mismo permiso de lectura); cambiar el rango por defecto para todo el tenant queda fuera de F1.
7. **`/account` no exige permiso ni módulo**: cualquier usuario autenticado ve su propio perfil, cambia su contraseña, activa/
   desactiva su MFA y revoca sus otras sesiones; solo desactivar MFA pide reautenticación (el API la exige con
   `[RequireAal2]`, según el comentario de `MfaTab.tsx`).
8. **Plantillas de pantalla no montadas**: `ListScreen.example.tsx`/`DetailScreen.example.tsx` compilan y se prueban con
   `tsc`/`vitest`/`build`, pero no tienen ruta ni aparecen en el menú; son el punto de partida documentado en `KIT.md` para
   quien construya una pantalla real en F2-F6.
9. **DSL portado como archivo aislado** (`kernel/dsl/RuleEvaluator.ts`), sin conexión aún a ninguna pantalla (no hay campos
   personalizados ni reglas de validación configurables desde una pantalla de F1); queda listo para cuando F2+ lo necesite.
10. **`StatusPipeline`/`StatusHistory`/`CustomFieldsForm`/`ClientPicker` se construyeron en F1 aunque ninguna pantalla de F1 los
    usa** (no hay entidades con estatus ni clientes en este lote): son parte del kit (P1/P2 del plan) para que F2 (Clientes) y
    los lotes siguientes no tengan que escribirlos.
11. **Hallazgo, no corregido en este lote**: el mensaje de "contraseña actual incorrecta" al cambiar la contraseña
    (`PUT /api/v1/auth/password`) llega en **inglés** ("Incorrect password.") porque `AuthService.ChangePasswordAsync`
    (`src/Teikem.Infrastructure/Services/AuthService.cs`) delega en `UserManager.ChangePasswordAsync` de ASP.NET Core Identity
    sin un `IdentityErrorDescriber` en español, y el mensaje se pinta bajo el campo `newPassword` (no `currentPassword`: el
    servicio agrupa ahí cualquier error de Identity, sea de la actual o de la política de la nueva). Verificado con la captura
    `docs/manual/frontend/img/f1-mi-cuenta-contrasena.png`. La pantalla ya hace exactamente lo que pide el contrato (mostrar el
    mensaje y el campo que nombra el servidor); arreglar el idioma del mensaje es un cambio de backend, fuera de alcance de F1.
12. **21 hallazgos corregidos en la ronda 1 de revisión** (commit `798c462`) y una ronda 2 adicional de correcciones (commit
    `98acac3`, sin conteo propio en el mensaje) antes del recorrido Playwright y esta documentación; no hay un documento de
    detalle por hallazgo para este lote (a diferencia de otros lotes de backend), así que no se puede enumerar cada uno aquí.
13. **Recorte de overflow del shell** (`.app`/`.main`/`.stage` con `overflow hidden`): la prueba de "sin scroll horizontal"
    (`expectNoHorizontalScroll` en el spec) no puede confiar solo en `document.documentElement.scrollWidth` porque el shell
    recorta cualquier desborde; mide además cada elemento visible dentro de `.stage`/`.bar` (y `.seg` se mide a sí mismo, por
    tener scroll horizontal propio a propósito en las pestañas).

## Lo que queda fuera de este lote (a propósito)

Copiado de la sección "Fuera de alcance de F1 mínimo" de `docs/frontend/loteF1-plan.md`, con lo verificado en el código:

- Roles y usuarios (administración).
- Seguridad y auditoría (pantallas de administración).
- Administración de catálogos y estatus (edición de overrides por tenant).
- Administración de campos personalizados (definir campos desde una pantalla).
- Vistas, indicadores y gráficos (administración: alta/edición de definiciones; F1 solo consume `/analytics/pulse`
  de solo lectura).
- Módulos y ajustes del tenant.
- Administración de plataforma.
- Todo módulo de negocio (clientes, órdenes, flota, trips, inventario): llegan en los lotes F2 a F6.
- Recuperación de contraseña sin sesión ("olvidé mi contraseña"): no está en el plan de F1 ni hay pantalla ni ruta para eso;
  `PasswordTab` solo cubre el cambio con sesión iniciada.
- SMS de respaldo y biometría para MFA: el backend tampoco los implementó en el Lote 1 (`docs/lote1-decisiones.md`, decisión
  11); el frontend solo cubre TOTP + códigos de recuperación, que es todo lo que expone el API.

## Verificación en CI

- Corrida verde de GitHub Actions (jobs `build-test` y `frontend`): https://github.com/lcasado-cerevelo/teikem/actions/runs/36293392824 (commit `ad69fe5`).
