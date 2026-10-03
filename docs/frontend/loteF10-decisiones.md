# Lote F10 — Seguridad y auditoría en la web (2026-10-03)

Parte 2 del cierre del lote "Región y formatos de la compañía": la pantalla **Seguridad y auditoría** (`/system/audit`), que era
un `pending(...)`. Especificación: el mock `Diseño/teikem-mockups.html` (`auditoriaScreen`, `auditoriaActividadTab`,
`auditoriaSesionesTab`, `setSecTab`, `TENANT_SECURITY`, textos `audit:{…}`), las bitácoras "Portal de clientes al final del menú,
y Seguridad/auditoría + Ajustes del tenant dejan de ser pantallas placeholder" y "Compañía en vez de tenant… exportar el log de
auditoría" y el módulo D del documento maestro. Número de lote: en `docs/frontend/` existían F1, F6, F7A, F8, F8a y F9; se tomó
el siguiente libre, **F10**. Se tocó el backend (mínimo, ver abajo); no se tocó el mock.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Pantalla | `/system/audit` real (`admin.audit`, SYSTEM, misma posición del menú): cabecera con el segmento Actividad / Sesiones y MFA (`?tab=sessions`) y la nota de la maqueta | `web-app/src/features/system/AuditScreen.tsx`, `app/routes.tsx` |
| Actividad | tabla única de cambios y eventos de seguridad paginada en el servidor; filtro Tipo (segmento Todo/Cambios/Seguridad), Fecha (días de la compañía), buscador libre al API (300 ms), columnas Cuándo/Tipo/Usuario/Detalle ordenables, etiqueta Cambio/Evento/Alerta por código, detalle JSON legible ("Campo: antes → después"), **Exportar CSV** con todo lo filtrado en el orden de la tabla (`exportTable`) y el Exportar del pie | `features/system/audit/ActivityTab.tsx` |
| Sesiones y MFA | sesiones activas de **toda la compañía** (usuario, dispositivo, ubicación = IP, última actividad; la propia "Esta sesión" sin revocar), Revocar por fila y "Cerrar las demás sesiones" (`admin.users`, con confirmación); política: MFA obligatorio, ventana de reautenticación 15/30/60 (+ el valor guardado si es otro), duración de la sesión y del aparato (1–365), PUT parcial, 400 bajo el campo, solo lectura sin `admin.tenant` | `features/system/audit/CompanySessionsTab.tsx` |
| Lógica pura y hooks | `audit/auditView.ts` (pestañas, consulta, etiqueta, detalle, orden, política) y `auditApi.ts` | `features/system/` |
| Enlace | Ajustes → General → "Abrir Seguridad y auditoría" abre `/system/audit?tab=sessions` | `settings/GeneralTab.tsx` |
| Textos | `system.audit.*` en es/en | `kernel/i18n/{es,en}.json` |
| Backend mínimo | `GET /api/v1/audit/sessions` (admin.audit), `DELETE /api/v1/audit/sessions/{id}` (+ admin.users), `POST /api/v1/audit/sessions/revoke-others` (+ admin.users + AAL2) en `CompanySessionService`; columna `RefreshToken.IpAddress` (se llena al emitir y renovar); `ActivityRowDto.TypeCode/OutcomeCode` (y `AuditLogDto.ActionCode`, `SecurityEventDto.EventTypeCode/OutcomeCode`); `GET /audit/activity` con **total real** y texto buscado en la base | `src/Teikem.Infrastructure/Services/CompanySessionService.cs`, `AuditQueryService.cs`, `AuthService.cs`, `Contracts/SecurityContracts.cs`, `Controllers/SecurityControllers.cs`, `Diseño/logistica-db-estructura.sql` |
| Contrato | `web-app/openapi.json` (Swagger del API) y los `schema.d.ts` de la web y de la app (`openapi-typescript` 7.13.0) | — |
| Pruebas | xunit `CompanySessionsTests` (6) y `AuditActivityTests` (3); smoke: paso "sesiones de toda la compañía (Lote F10)" y total/texto de la actividad; vitest `auditView.test.ts` (20) y `AuditScreen.test.tsx` (6); `TenantSettingsPage.test.tsx` ajustada; Playwright `e2e/loteF10-audit.spec.ts` | `tests/`, `scripts/smoke.sh`, `web-app/src/features/system/`, `web-app/e2e/` |
| Documentación | este archivo, capítulo de pantallas F10 con capturas `f10-*`, capítulo 01 (secciones 1.5, 9 y 9.1 nueva), FAQ "Lote F10", índice, `KIT.md` | `docs/` |

### Por qué se tocó el backend

1. **Sesiones de toda la compañía**: el API solo tenía las del propio usuario (`GET /auth/sessions`) y "cerrar todas las de un
   usuario" (`DELETE /users/{id}/sessions`); la maqueta lista las de todos con revocar por fila. Faltaba listar y revocar por
   sesión en la compañía. La columna "Ubicación" pedía un dato que no se guardaba: se agregó la IP a `RefreshToken`.
2. **Actividad paginada**: `GET /audit/activity` calculaba `total` sobre una ventana de `skip + take` filas de cada bitácora
   (con 25 por página decía 50 y crecía al paginar) y filtraba el texto solo dentro de esa ventana; así la paginación y la
   exportación completa (`fetchAllPages`, que se fía del total) no servían. Ahora cuenta con `COUNT`, filtra el texto en la base,
   mezcla solo las llaves (fecha, id) y carga completas solo las filas de la página.
3. **Códigos**: la maqueta pinta "Alerta" para fallos y permisos denegados; el DTO solo traía etiquetas traducidas. Se agregaron
   los códigos (campos opcionales al final de los records: compatibles).

## Cómo se probó (resultado real, en este entorno)

- `dotnet build Teikem.sln -c Release`: **0 errores**. `dotnet test Teikem.sln`: **2815 pasaron, 0 fallaron** (antes del lote,
  2806; +9 nuevas).
- SQL Server 2022 que ya corría en el contenedor; `db-reset --yes` (la estructura cambió) y `db-init` **dos veces**: las tres
  terminaron con código 0 ("Inicialización de BD completada.").
- API en `http://localhost:5000` (Release, Development, `Auth__Onboarding__Enabled=false`, como el CI) y `scripts/smoke.sh` con
  `SMOKE_SQL` y `SMOKE_MIGRATION_RUN`: **SMOKE OK** de punta a punta, dos veces (antes y después de corregir la actividad; la
  segunda sobre base limpia), con los pasos nuevos "sesiones de toda la compañía (Lote F10)" y total/texto de la actividad.
- `openapi.json` regenerado desde `/swagger/v1/swagger.json`: el diff solo trae lo nuevo; tras corregir la actividad el Swagger es
  idéntico byte a byte al del repositorio. `npm run typecheck` de la app de almacén: pasó.
- `cd web-app && npm run check` (tipos generados, `tsc -b`, oxlint, vitest, build): **pasó** (también al final, tras el último
  ajuste de estilos). Vitest: **109 archivos, 1023 pruebas** (antes: 107 archivos, 997). oxlint: sin avisos en los archivos nuevos (los 9 de siempre).
- Playwright (Vite con `VITE_API_URL=http://localhost:5000`, `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`):
  - `e2e/loteF10-audit.spec.ts` + `e2e/loteF9.spec.ts` en `escritorio` y `movil`: **4 pasaron, 2 omitidos** (por proyecto).
  - Corrida completa: ver "Corrida completa de Playwright" al final.

## Decisiones para el dueño

1. **Permisos de revocar**: listar sesiones pide `admin.audit` (el de la pantalla); revocar una o "las demás" pide además
   `admin.users` (es la misma acción que ya existía para un usuario completo). "Cerrar las demás sesiones" pide además AAL2 por
   ser masiva. Si se prefiere que `admin.audit` baste, se quita el atributo en `AuditController`.
2. **"Cerrar las demás sesiones" = todas las de la compañía menos la propia** (incluidas las del mismo administrador en otros
   aparatos y las de los aparatos de almacén), como la maqueta (`ACTIVE_SESSIONS.filter(x => x.current)`). Alternativa más
   conservadora: solo las demás del propio usuario (eso ya está en Mi cuenta).
3. **Revocar una sesión puede acabar cerrando todas las del usuario**: si el aparato revocado intenta renovar la sesión, la regla
   del lote 1 (`AuthService.FindActiveAsync`) lo trata como reutilización de un token y revoca toda la cadena del usuario (en
   todas sus compañías). No se cambió (es seguridad): distinguir "revocada a propósito" (`ReplacedByTokenHash` vacío) de
   "rotada y reutilizada" es una línea, pero lo decide el dueño. Pasa igual desde Mi cuenta.
4. **No se cambia el SecurityStamp al revocar**: el token de acceso vivo dura hasta 15 minutos más. Cambiarlo cerraría las
   sesiones del usuario en sus otras compañías.
5. **"Ubicación" = IP** (no hay geolocalización; haría falta un servicio externo). Las sesiones abiertas antes del lote muestran
   "—" hasta renovarse. Detrás de un proxy hará falta configurar los encabezados reenviados para que no salga la IP del proxy.
6. **"Última actividad" = última renovación** de la sesión (cada 15 minutos de uso), no cada petición.
7. **Filtro de Fecha** en la Actividad: no está en la maqueta; se agregó porque es la forma de llegar a lo antiguo y de exportar un
   período para una auditoría externa. Se quita sin tocar lo demás.
8. **Búsqueda libre al servidor** (no sobre lo cargado, como dice `QBox` en el kit): la tabla está paginada en el servidor. Busca
   cada dato por separado (ver capítulo 01, sección 9): "Login · Éxito" ya no encuentra nada.
9. **El orden por columna** reordena la página que se ve (el endpoint no ordena); el CSV y el Exportar del pie ordenan todo lo
   filtrado igual. Por defecto, lo más reciente primero.
10. **CSV de la pantalla** (Cuándo, Tipo, Usuario, Detalle; fecha `AAAA-MM-DD HH:MM:SS` en la hora de la compañía; nombre
   `auditoria-AAAA-MM-DD.csv`, como la maqueta) en vez del `export.csv` del servidor (otras columnas, ISO UTC, 2000 filas). El del
   servidor se conserva para integraciones.
11. **"Alerta"** = evento con resultado distinto de éxito, o `PERMISSION_DENIED`/`LOCKOUT`. La maqueta además marcaba la
   revocación de una API key; aquí una revocación a propósito es "Evento".

## Pendientes

- **Pendiente de backend**: un orden del servidor para `/audit/activity` (hoy solo por fecha) y la agrupación por
  `CorrelationId` (maestro, módulo E: "No implementado"). Filas de API keys (Integraciones) cuando exista esa pantalla.
- **Pendiente de backend**: `TenantService` no valida que la ventana de reautenticación sea 15/30/60 (acepta 5–240); la pantalla
  muestra el valor guardado aunque no sea de la lista.
- La política se aplica a las sesiones nuevas o renovadas; no hay "forzar a todos a volver a entrar con MFA" al encenderla (lo
  hace el login siguiente).

## Corrida completa de Playwright

- 1.ª corrida (`npx playwright test`, todos los proyectos, después del smoke): **49 pasaron, 1 falló, 58 omitidos, 26 no
  corrieron**. Falló `lote14.spec.ts` 6 ("Conteo de lo cambiado…": el detalle del movimiento no mostró "Documento de origen" a
  tiempo con 4 workers); los 26 que no corrieron son los proyectos que dependen de `escritorio` (f8a, lote16, f9). Repetido solo
  (`lote14.spec.ts --project escritorio`): **7 pasaron, 1 omitido**: inestable en paralelo, no relacionado con este lote.
- 2.ª corrida completa: **69 pasaron, 0 fallaron, 65 omitidos** (los `test.skip` por proyecto), 4,3 min.
- Al revisar la captura `f10-actividad-filtrada` se vio que el recorrido no esperaba a que llegara la búsqueda (300 ms): se
  endureció (todas las filas deben ser de la revocación y cada línea del CSV debe traer `company_session`) y se repitió
  `loteF10-audit.spec.ts` + `loteF9.spec.ts` en escritorio y móvil: **4 pasaron, 2 omitidos**.
- Las capturas de otros lotes que regeneran esas corridas se descartaron; solo se agregan las `f10-*`.
- Al terminar se detuvieron el API y el Vite de Playwright (`ps` sin procesos `Teikem.Api`, `dotnet run`, `vite` ni Chromium).
  SQL Server ya corría al empezar y se dejó como estaba.
