# Lote F9 — Ajustes de la compañía y región y formatos en la web (fase 3) (2026-10-03)

Fase 3 (frontend web) del pedido "Ajustes de la compañía: pestañas, región y formatos, calendario, y modo de recibo por
almacén" (bitácora del documento maestro). La fase 1 fue el mock (`Diseño/teikem-mockups.html`: `ajustesScreen` y las funciones
`fmtMoney/fmtDate/fmtDayMonth/fmtTime/fmtDateLong/fmtPhone/todayISO`, la especificación visual y funcional) y la fase 2 el
backend (`docs/lote18-decisiones.md`). Número de lote: en `docs/frontend/` existían F1, F6, F7A, F8 y F8a; se tomó el siguiente
libre, **F9**. No se tocó el backend (`src/`), `app-almacen/` ni el mock.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Proveedor único de formatos | `FormatSettings` (13 campos + región) leído de `GET /tenant/settings` con respaldo campo a campo; Puerto Rico mientras carga o sin sesión; estado global (`store.ts`) que alimenta funciones puras y hooks; `FormatProvider` montado en `SessionProvider` | `web-app/src/kernel/format/` |
| Funciones puras | `formatNumber`, `formatMoney`, `formatDate`, `formatDayMonth`, `formatTime`, `formatTimeOfDay`, `formatDateTime`, `formatDateLong(Time)`, `todayIso`, `parseNumber`, teléfono (`formatPhone`, `formatPhoneInput`, `normalizePhone`, `isValidPhone`, `phonePlaceholder`); los ajustes son el último parámetro (por defecto los vigentes) | `kernel/format/format.ts`, `phone.ts` |
| Re-pintado sin recargar | `useT()` y `useLang()` se suscriben también a los formatos (la función `t` cambia de identidad), así que cualquier pantalla que formatee con el idioma se vuelve a pintar al guardar Región y formatos | `kernel/i18n/useT.ts`, `i18n.ts` |
| Reemplazo de lo fijo | `numberFormat.ts` (ya no `es-PR`/`USD` fijos), `tenantZone.ts` (zona de la compañía, `tenantTimeZone()`), `analytics/format.ts` (ya no `en-US`), `phone.ts`/`PhoneInput` (máscara de la compañía), reloj de la cabecera, `StatusHistory`, `reportPdf`, `exportTable`, `exportGrouped`, `filterRegistry` (oración de filtros), `kardexView`, `inventoryReports`, `activity`, `attention`, `lineRules`, `account/format`, listas y fichas de órdenes, compras, cruce de muelle, citas y "Conteo de lo cambiado"; datetime-local de citas de muelle y campos personalizados DATETIME en la hora de la compañía | ver el commit "proveedor único de formatos" |
| Pantalla | `/system/settings` (antes `pending`): pestañas General, Región y formatos, Calendario, Módulos, Operación y Marca, `?tab=` | `features/system/TenantSettingsPage.tsx`, `features/system/settings/*` |
| Lógica pura de la pantalla | formulario de región (`regionForm.ts`), calendario (`tenantCalendar.ts`), módulos (`tenantModules.ts`), matriz y resumen de recepción (`settings/operations.ts`) | `features/system/` |
| Marca | 13 temas, colores propios, validación WCAG y de matiz, derivación del tema y aplicación sobre `tokens.css` (`TenantBrand` en la sesión, vista previa mientras se edita) | `kernel/ui/brandTheme.ts`, `brandPreview.ts`, `TenantBrand.tsx` |
| Kit | `IconPin`, `IconRoute`, `IconPhoneFormat`; KIT.md con "Región y formatos de la compañía", "Marca" y "Ajustes de la compañía" | `kernel/ui/screenIcons.tsx`, `web-app/KIT.md` |
| Recorridos | `e2e/loteF9.spec.ts` (escritorio y móvil, sin cambios persistentes) y `e2e/loteF9-region.spec.ts` (proyecto `escritorio-f9`, al final de todo) | `web-app/e2e/`, `playwright.config.ts` |
| Manual | capítulo F9 con capturas `f9-*`, FAQ "Lote F9", índice; F6 y FAQ del lote 12 (teléfono) actualizados | `docs/manual/` |

## Cómo se probó (resultado real, en este entorno)

- `cd web-app && npm ci && npm run check` (tipos generados, `tsc -b`, oxlint, vitest, build): **pasó**. Vitest: **107 archivos,
  997 pruebas** (antes del lote: 100 archivos, 942). `schema.d.ts` regenerado sin diferencias. oxlint: 9 avisos, los mismos 9
  que antes del lote (ninguno nuevo).
- Pruebas nuevas: `kernel/format/format.test.ts` (PR, US, DMY/MDY/YMD, separadores, 12/24 h, símbolo después, otra moneda,
  "hoy" a la 1:30 UTC del 3-oct = 2-oct en Puerto Rico y en Honolulú, zona vigente que alimenta `tenantZone`),
  `phone.test.ts` (máscara mientras se escribe, código de país, normalización), `settings.test.ts` (respaldo campo a campo,
  "Personalizada", intercambio de separadores), `FormatProvider.test.tsx` (PR mientras carga, cambio de la consulta sin
  desmontar), `features/system/settingsLogic.test.ts` (próximo día hábil, feriados, cascada de módulos, cuerpo del PUT, matriz,
  resumen de recepción), `kernel/ui/brandTheme.test.ts` (contraste, matiz, los 13 temas pasan, BrandingJson) y
  `TenantSettingsPage.test.tsx` (pestañas, lectura, guardar, error 400 junto al campo, solo lectura, vista previa, calendario,
  módulos con confirmación, operación, marca). Ajustadas por depender del formato anterior: reloj de la cabecera (12 h),
  Actividad (hora de la compañía), oración de filtros (MM/DD/AAAA también en español), PDF de fechas, `parseLocaleNumber`
  (separadores de la compañía), `ListPager` (`locale: 'es'`), Pulso (`formatYmd`), teléfono (`(787) 555-1234`) y Proveedores.
- API real: SQL Server 2022 con `scripts/dev-sqlserver.sh`, `dotnet build -c Release` (0 errores), `db-init`, API en
  `http://localhost:5000` (Development, `Auth__Onboarding__Enabled=false`) y Vite con `VITE_API_URL=http://localhost:5000`
  (el `.env.development` apunta a `https://localhost:5001`).
- Playwright con el Chromium del entorno (`PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`, sin `playwright install`):
  - `e2e/loteF9.spec.ts` en `escritorio` y `movil` (360 px): **2 pasaron** (antes de corregir, el móvil falló por el segmento de
    seis pestañas fuera del ancho; se corrigió con `.set-head .act`).
  - `e2e/loteF9-region.spec.ts` (`--project=escritorio-f9 --no-deps`): **2 pasaron**: US + 24 h + AAAA-MM-DD cambia el reloj, el
    Kárdex y "Conteo de lo cambiado" (America/New_York) sin recargar; "Puerto Rico" + Guardar deja el juego completo de
    `format-options` (comprobado por API) y `isRegionCustomized = false`.
  - Corrida completa (`npx playwright test`, todos los proyectos, contra la base de desarrollo ya usada en sesiones
    anteriores): **67 pasaron, 0 fallaron, 63 omitidos** (los `test.skip` por proyecto de siempre: móvil dentro de los
    proyectos de escritorio y al revés), 4,3 min. Las capturas de otros lotes que esa corrida regenera (f6, f7a, l14, l15) se
    descartaron para no mezclar cambios; las `f9-*` son de este lote.
- No se corrió `dotnet test` ni `scripts/smoke.sh` (no se tocó el backend).

## Decisiones a revisar

1. **Las fechas en español pasan a MM/DD/AAAA** (el valor de Puerto Rico del lote 18, decisión 1, todavía POR CONFIRMAR). Antes,
   la web mostraba "30 sept 2026" (`dateStyle: 'medium'`) o "30/09/2026" en español y "09/30/2026" en inglés; ahora el orden lo
   pone la compañía en los dos idiomas. Las fechas escritas con nombres (título del Pulso, eje y tooltip de los gráficos,
   "Generado el 30 de septiembre de 2026") siguen el idioma para los nombres, con la zona de la compañía. Si se decide DD/MM
   para Puerto Rico basta cambiar `PR_FORMAT.dateOrder` (y el backend, ver lote 18).
2. **La hora pasa a 12 h en Puerto Rico** (reloj "2:05:09 p. m.", Kárdex, listas): antes la web usaba 24 h en el reloj y en el
   Kárdex. Lo decide `TimeFormat` de la compañía.
3. **Signo del dinero negativo con guion ASCII** ("-$12.00"), no el "−" (U+2212) de la maqueta: es lo que la web mostraba
   antes, las pruebas lo esperaban y los PDF solo tienen Latin-1.
4. **Teléfono: se guarda solo con dígitos** (como pide la maqueta); antes la pantalla de Proveedores guardaba `(787)555-1234`.
   Los guardados antes se siguen mostrando con la máscara si tienen los dígitos. El mensaje de validación ahora dice la cantidad
   de dígitos y la máscara de la compañía ("El teléfono debe tener 10 dígitos: (###) ###-####."). La máscara de Puerto Rico es
   la del servidor, `(###) ###-####` (con espacio), no la vieja `(xxx)xxx-xxxx` de la web.
5. **Región y formatos con borrador y "Guardar"** (la maqueta aplica cada cambio al instante): la vista previa muestra el
   borrador y la app cambia solo al guardar; el PUT manda SIEMPRE la región y los 13 campos (así el resultado es exactamente
   lo que se ve, con o sin cambio de región: decisión 2 del lote 18). Días laborables, feriados, módulos y la matriz de
   acciones se guardan al instante, como en la maqueta. Cambiar de pestaña descarta el borrador de la pestaña.
6. **Otros usuarios ven los formatos nuevos al volver a leer los ajustes** (consulta vigente 10 minutos, luego se pide al volver
   a la ventana o cambiar de pantalla, como los catálogos). No hay aviso en vivo entre usuarios.
7. **Pipeline de estatus**: se construyó porque el API ya lo tiene (`GET/PUT /status/capabilities/TRANSPORT_ORDER`, permiso
   `admin.statusconfig`); solo con el módulo de órdenes (`LTL_GROUND`). Las 4 acciones de la maqueta (Editar carga, Asignar a
   ruta, Re-cotizar, Cancelar); `ADD_DOCUMENT` no se muestra.
8. **Recepción por almacén**: los conteos salen de `GET /receipts?warehousePublicId=&phase=OPEN|PENDING_PUTAWAY&take=1` (dos
   consultas por almacén). El aviso rojo usa `defaultReceivingBinId` vacío (la maqueta además miraba si hay posiciones de
   recepción en el almacén; el servidor usa la primera zona STAGING si no hay posición por defecto, así que el aviso puede
   salir en un almacén que sí recibiría con esa zona).
9. **Marca**: con el tema Teikem sin colores propios no se toca `tokens.css` (la derivación difiere de la paleta original en
   1-2 unidades por canal); con otro tema se sobrescriben superficies, líneas, texto y acentos en `<html>`, nunca los colores
   de estado. Se guarda en `BrandingJson` como `{ preset, useCustom, custom: { flow, money, neutral } }` conservando otras claves.
10. **Selector de moneda**: solo USD (como la maqueta) más la guardada si es otra; el servidor acepta cualquier código ISO.
11. **Zonas**: las 7 de la maqueta más la guardada si es otra.
12. `playwright.config.ts`: opción `PW_CHROMIUM_PATH` (Chromium ya instalado) y proyecto `escritorio-f9` que depende de
    `escritorio-f8a`, `escritorio-lote16` y `movil-lote16` (corre al final porque cambia los formatos de la demo).

## Qué quedó fuera / pendiente de backend

- **Validación de la marca en el servidor**: `PUT /tenant/settings` solo exige que `BrandingJson` sea JSON ≤ 200 KB; el contraste
  WCAG y la separación de matiz se validan solo en la pantalla. Debe repetirse en el servidor (pendiente de backend).
- **Logos por compañía** (lockup y marca cuadrada, fondo claro y oscuro): no hay dónde guardar los archivos (BrandingJson no
  alcanza: 4 logos de hasta 512 KB); la pestaña lo explica y se usan los logos de Teikem. Pendiente de backend (almacenamiento
  de archivos).
- **Feriado duplicado**: `POST /tenant/holidays` con una fecha existente la reemplaza sin avisar; la pantalla lo evita con
  "Ya hay un feriado en esa fecha". Un 409 en el servidor sería más seguro (pendiente de backend, opcional).
- **Conteo de recibos por almacén en una sola llamada** (hoy 2 consultas por almacén): un resumen en el API sería mejor con
  muchos almacenes.
- **App del Zebra (`app-almacen/`)**: no se tocó (otra sesión).
- **Servicios del backend que todavía cuentan "hoy" en UTC** (lista del lote 18): no son de este lote.
- Seguridad y auditoría (`/system/audit`) sigue pendiente: el botón "Abrir Seguridad y auditoría" lleva a su pantalla
  "pendiente".
