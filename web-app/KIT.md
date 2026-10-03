# Kit del frontend Teikem — contrato para quien construye pantallas

Este archivo es lo único que un implementador de pantallas necesita leer del núcleo. Si algo no está aquí, no existe:
agrégalo en `src/kernel` con una prueba y anótalo aquí en la misma pieza.

## Reglas de oro
1. **Cliente generado.** Todo acceso al API pasa por `api` (`src/kernel/api/client.ts`, `openapi-fetch` sobre `schema.d.ts`
   generado con `npm run api:types`). Nunca `fetch` directo ni DTOs a mano. `api.GET('/api/v1/clients', { params: { query } })`.
2. **Lecturas con TanStack Query** (`useQuery` con clave `[ruta, params]`); **escrituras con `useMutation`** e invalidación de la
   clave de la lista. Hooks por módulo en `features/<modulo>/api.ts`.
3. **Errores del servidor**: `applyProblemDetails(error, form)` pone `errors` por campo en el formulario y devuelve `title` para el
   toast. `module_disabled` y `forbidden` los intercepta el shell (pantalla propia). 401 → el cliente refresca el token una vez y
   reintenta; si falla, vuelve al login.
4. **Permisos y módulos**: `<Can perm="orders.create">…</Can>`, `useCan('orders.create')`, `<ModuleGate module="CATALOG">…</ModuleGate>`.
   Los códigos son los del API (`/api/v1/me` → `permissions`, `enabledModules`). Sin permiso, la acción no se pinta.
5. **Textos**: `t('clients.title')` con claves en `src/kernel/i18n/es.json` y `en.json`. Sin texto suelto en JSX. Las etiquetas de
   catálogo vienen del API ya traducidas (`Accept-Language` lo pone el cliente).
6. **Responsive**: toda pantalla funciona a 360 px. `DataTable` pasa a tarjetas bajo 720 px. Nada genera scroll horizontal de página.
7. **Identificadores en inglés, textos y comentarios en español.** Un archivo por pantalla en `features/<modulo>/<Pantalla>.tsx`.

## Cliente del API (`src/kernel/api`)
- `api` (`client.ts`): cliente `openapi-fetch` tipado con `schema.d.ts`, base `VITE_API_URL` (vacío = mismo origen). Pone
  `Authorization: Bearer <access>` (salvo que la llamada traiga su propio `Authorization`) y `Accept-Language` con el idioma actual.
- `unwrap(api.GET(...))` → devuelve `data` o lanza `ApiError`. Úsalo en `queryFn`/`mutationFn`:
  `useQuery({ queryKey: ['/api/v1/clients', query], queryFn: () => unwrap(api.GET('/api/v1/clients', { params: { query } })) })`.
- `ApiError` (`problem.ts`): `{ status, code, title, errors, correlationId }` del ProblemDetails del API (`code`: `validation`,
  `not_found`, `conflict`, `status_rule`, `forbidden`, `module_disabled`, `aal2_required`, `unauthorized`, `internal`; si el
  API no manda `code` se deduce del estatus).
- `applyProblemDetails(error, form?)` → `{ title, code, errors }`. Con `form` (react-hook-form) hace `setError` por campo
  (nombres del servidor normalizados a camelCase: `$.Email` → `email`). Error de red → `code: 'network'`.
- Política automática (no la repitas en pantallas): 401 → un solo `POST /api/v1/auth/refresh` compartido por las peticiones que
  fallen a la vez, rota tokens y reintenta una vez; si el refresh falla → login. 403 `aal2_required` → modal de reautenticación y
  reintento una vez. Los 401 de `/auth/login`, `/auth/mfa/verify`, `/auth/refresh`, `/auth/logout`,
  `/auth/switch-tenant` no refrescan (son la respuesta del propio paso). En `/auth/reauth` solo se refresca el 401 del esquema
  (access token vencido: sin cuerpo ni `code`); el 401 del servicio ('Contraseña incorrecta.', con `code`) no.
- Lecturas con 403 `module_disabled`/`forbidden` → el shell navega a `/module-off` o `/forbidden`. Si una consulta secundaria no
  debe sacar al usuario de la pantalla: `useQuery({ ..., meta: { handleAccessDenied: false } })`. Las mutaciones no se interceptan.
- **Fechas del API**: el backend manda las marcas UTC sin zona (`"2026-09-27T14:00:00.123"`) y `new Date()` las leería como hora
  local. Léelas siempre con `parseApiDate(iso)` (`src/kernel/api/dates.ts`: sin zona = UTC, como la DSL) antes de formatear.
- **Hora de la compañía** (un solo punto para toda la web, espejo de `TenantClock`/`LocalDay`): `src/kernel/api/tenantZone.ts`.
  La zona es la de la compañía (`tenantTimeZone()`, de Región y formatos; sin ajustes, `TENANT_TIME_ZONE` = 'America/Puerto_Rico',
  que queda solo como el valor por defecto). `localDayOf(value)` → 'YYYY-MM-DD' del día LOCAL de un instante del API (un día
  de calendario 'YYYY-MM-DD' queda igual; no fecha → null), como `AnalyticsEngine.GroupKey`; `tenantToday()` ("hoy" local);
  `zonedInputFromUtc(iso)` / `utcFromZonedInput('YYYY-MM-DDTHH:mm')` para inputs `datetime-local` en hora de la compañía
  (también los usan las citas de muelle y los campos personalizados DATETIME). Todas aceptan una zona explícita al final.
  ```ts
  const day = localDayOf('2026-09-30T03:59:00')   // '2026-09-29' (23:59 en Puerto Rico)
  const today = tenantToday()                    // 'YYYY-MM-DD' de hoy en la zona de la compañía
  const input = zonedInputFromUtc(dto.fromUtc)   // '2026-09-30T00:00'
  ```

## Región y formatos de la compañía (`src/kernel/format`, lote F9)
Toda fecha, hora, número, dinero y teléfono que se pinta pasa por aquí (espejo de `money/fmtMoney/fmtDate/fmtDayMonth/fmtTime/
fmtDateLong/fmtPhone/todayISO` de la maqueta). Los valores salen de `GET /api/v1/tenant/settings` (los 13 campos de formato de
`dbo.Tenant`) y, mientras cargan o sin sesión, son los de Puerto Rico (`PR_FORMAT`: `America/Puerto_Rico`, USD `$` antes, 2
decimales, `MDY` con `/`, 12 h, domingo, `,` miles, `.` decimal, `+1`, `(###) ###-####`). **El idioma es por usuario y solo
decide los nombres de meses y días y el texto "a. m."/"AM"; la región decide el orden y el separador de la fecha, los
separadores de números, la hora de 12/24, la zona y la moneda.** Nunca `Intl.DateTimeFormat`/`toLocale…` sueltos en una pantalla.
- `FormatProvider` (`enabled`): lo monta `SessionProvider` (con sesión); deja los ajustes en un estado global (`store.ts`:
  `getFormatSettings`, `setFormatSettings`, `resetFormatSettings`, `subscribeFormat`, `tenantTimeZone`). Al guardar Ajustes →
  Región y formatos (o invalidar `catalogKeys.tenantSettings`) cambia ahí y **toda la app se vuelve a pintar sin recargar**:
  `useT()` y `useLang()` también se suscriben (la función `t` cambia de identidad, así que lo memorizado con `[t]` —columnas,
  esquemas— se recalcula). Algo memorizado SOLO con `[lang]` que formatee debe usar `useFormat()`.
- `useFormat()` → `{ settings, lang, number(n, opts?), money(n, opts?), date(v), dayMonth(v), time(v, { seconds? }), timeOfDay('HH:mm'),
  dateTime(v), dateLong(v, opts?), phone(v), phoneInput(v), normalizePhone(v), isValidPhone(v), phonePlaceholder(), today() }`;
  `useFormatSettings()` → `FormatSettings`.
  ```tsx
  const f = useFormat()
  <td>{f.date(r.createdAtUtc)}</td><td>{f.time(r.createdAtUtc)}</td><td className="mono">{f.money(r.total)}</td>
  ```
- Funciones puras (los ajustes son el ÚLTIMO parámetro y por defecto los vigentes; así se prueban sin React):
  | Función | Resultado (Puerto Rico) |
  |---|---|
  | `formatNumber(n, intlOpts?, s?)` | `61,023.125` (miles siempre, hasta 3 decimales solo si los tiene; acepta signo, compacto, decimales fijos) |
  | `formatMoney(n, lang?, { currency?, unitPrice?, signed?, decimals? }?, s?)` | `$1,234.50`, `-$12.00`, `+$5.00`; símbolo después: `1.234,50 €` (espacio duro); otra moneda con su símbolo corto |
  | `formatDate(v, s?)` | `10/02/2026` (`DMY` → `02/10/2026`, `YMD` → `2026/10/02`); un 'YYYY-MM-DD' no se corre; un instante, su día en la zona |
  | `formatDayMonth(v, s?)` | `10/02` (`DMY` → `02/10`) |
  | `formatTime(v, lang, { seconds? }?, s?)` / `formatTimeOfDay('14:05', lang, s?)` | `9:30 p. m.` / `9:30 PM` (24 h: `21:30`), en la zona |
  | `formatDateTime(v, lang, s?)` | `10/02/2026 9:30 PM` (un día sin hora → solo la fecha) |
  | `formatDateLong(v, lang, intlOpts?, s?)` / `formatDateLongTime(v, lang, s?)` | `viernes, 2 de octubre de 2026` / `2 de octubre de 2026, 9:30 p. m.` (nombres del idioma, zona de la compañía) |
  | `todayIso(now?, s?)`, `utcNowText(now?)` | "hoy" 'YYYY-MM-DD' en la zona (1:30 UTC del 3-oct = '2026-10-02') / `2026-10-03 01:30 UTC` |
  | `parseNumber(text, s?)`, `applySeparators(textEnUs, s?)`, `currencySymbol(code, s?)`, `withCurrencySymbol(body, sign, sym, s?)` | leer/escribir con los separadores y el símbolo de la compañía |
  | `formatPhone(v, s?)` / `formatPhoneInput(v, s?)` / `normalizePhone(v, s?)` / `isValidPhone(v, s?)` / `phonePlaceholder(s?)` | `(787) 555-0142` (con los dígitos exactos; si no, tal cual) / máscara mientras se escribe / solo dígitos para guardar / vacío o los dígitos de la máscara (con o sin `+1`) / `(000) 000-0000` |
- Ajustes: `FormatSettings`, `PR_FORMAT`, `US_FORMAT`, `DEFAULT_FORMAT`, `FORMAT_FIELDS` (los 13 de una región),
  `toFormatSettings(dto, fallback?)` (campo por campo; inválido → respaldo), `regionDefaults(code, formatOptions?)`,
  `isRegionCustom(s, region)` ("Personalizada"), `withSeparator(s, campo, valor)` (intercambio automático si los separadores
  chocan), `sameFormat`, `isKnownTimeZone`.
- Compatibilidad: `formatQuantity(n, lang)`/`formatMoney(n, lang)` de `kernel/i18n` y los `formatDate/formatDateTime(iso, lang)`
  de `lineRules.ts`, `account/format.ts` y las pantallas siguen con su firma y ya usan estos formatos (`lang` solo pone "a. m.").
  `numberLocale(lang)` queda solo para nombres de meses; no lo use para números. Los PDF/Excel (`exportTable`, `reportPdf`,
  `exportGrouped`) formatean y leen números y fechas con los ajustes de la compañía; el nombre del archivo lleva el día de la
  compañía.
- `createApiClient({ baseUrl, fetch })` solo para pruebas (cliente con la misma política sobre un `fetch` simulado).

## Marca de la compañía: colores y logos (`src/kernel/ui`, lotes F8a/F9/19)
- **Colores** (`brandTheme.ts`, `brandPreview.ts`, `TenantBrand`): `Tenant.BrandingJson` (`{ preset, useCustom, custom: { flow, money,
  neutral } }`) sobrescribe las variables de `tokens.css` en `<html>`. Lógica pura: `BRAND_PRESETS` (13), `brandChecks`,
  `parseBranding`, `serializeBranding` (solo las tres claves: el servidor rechaza campos desconocidos) y `validateBrandingJson(json)`,
  espejo de `BrandingRules` (C#) con los mismos mensajes. **La paridad con el servidor se asegura con `tests/shared/brand-vectors.json`**
  (lo leen `brandVectors.test.ts` y `BrandingRulesTests`): si cambia un número o un mensaje, cambia en las dos implementaciones y se
  regenera ese archivo. Los colores de estado no se personalizan.
- **Logos** (`brandLogos.ts`, `brandLogosApi.ts`): cuatro ranuras `LOGO_SLOTS` (`lockup`, `lockup-inverted`, `mark`, `mark-inverted`;
  las `-inverted` son para fondo oscuro), SVG/PNG/JPG/WebP hasta `LOGO_MAX_BYTES` (512 KB). `GET /tenant/brand/logos` (lista) y
  `.../{slot}` (archivo; exige sesión, así que no va directo en `<img src>`): `TenantBrand` baja cada archivo con el token y deja una
  URL de objeto por ranura en un almacén (`setCompanyLogos`/`useCompanyLogos`). `BrandLockup` y `BrandMark` lo usan solos
  (`pickLogoUrl(urls, 'lockup'|'mark', theme)`: la variante del tema o, si falta, la otra; sin ninguna, el logo de Teikem). Escritura
  (`admin.tenant`): `useUploadBrandLogo()` (multipart, campo `file`) y `useRemoveBrandLogo()` en `features/system/tenantSettingsApi.ts`;
  los 400/413/415 llegan como `ApiError` y la pestaña Marca los muestra junto a la ranura (`problemText`). En pruebas con jsdom,
  `FormData` con archivos no se puede enviar por el `Request` de Node: sustitúyalo con `vi.stubGlobal('FormData', …)` (ver
  `TenantSettingsPage.test.tsx`).

## Shell (`src/app`)
- `AppShell`: barra lateral con los 7 grupos de la maqueta (`NAV_GROUPS` en `navigation.ts`, en este orden: `ops` Operación,
  `warehouse` Almacén, `money` Contabilidad, `catalog` Catálogo, `analytics` Análisis, `system` Sistema, `portal` Portal de
  clientes), filtrada por módulos y permisos con `visibleNav(routes, permissions, modules)` (lógica pura en
  `navigation.ts`; un grupo sin entradas visibles no se pinta), colapsable en escritorio y
  cajón bajo 900 px; cabecera: compañía (selector si hay más de una membresía ACTIVE/PLATFORM: `switchableMemberships` en
  `app/memberships.ts`), "Buscar o ejecutar…" (abre la paleta; bajo 720 px es una lupa), reloj "● en vivo · HH:MM:SS"
  (`Intl.DateTimeFormat` del idioma, 24 h; bajo 1100 px sin la palabra, oculto bajo 600 px), tema ☀/🌙, idioma, usuario
  (→ `/account`) y salir.
- Rutas en `src/app/routes.tsx`: `AppRoute = { path, element, perm?, module?, nav?, pending? }`, `element` con carga diferida
  `lazy(() => import('../features/<modulo>/<Pantalla>'))` (la pantalla exporta `default`). `appRoutes` = internas (dentro del
  shell, con sesión); `publicRoutes` = sin sesión. `module`/`perm` se aplican solos (pantallas 'Módulo apagado' / 'Sin permiso');
  `perm: 'a|b'` = cualquiera de los dos (como `[RequirePermission("a|b")]` del API; lo dividen `RouteGate`, `ModuleGate` y
  `visibleNav`). `nav: { group: NavGroupKey, key, order? }` la pone en el menú con el título `nav.<key>.title`
  (`navTitleKey(key)`) y el subtítulo `nav.<key>.subtitle` (`navSubtitleKey(key)`, se ve en la paleta y en la pantalla
  pendiente); `order` = posición en la maqueta × 10. Para agregar una pantalla solo se añade su entrada ahí. `/` es Pulso
  (`features/analytics/Pulse`, por paneles desde F8a: ver "Pulso del día por paneles" abajo; siempre consulta `GET /pulse`, que
  no exige permiso ni módulo, y un 403 no redirige a 'Módulo apagado'); cada indicador o gráfico tiene "Rango" (mi rango
  de fecha, `PUT .../my-date-range`: preferencia por usuario con solo `analytics.view`; en CUSTOM el `toUtc` del DTO es exclusivo y
  se muestra el día anterior). El panel WAREHOUSE es 'Almacén' (`WarehousePulsePanel`; F6: saldo en mano/disponible,
  recibos abiertos, tareas pendientes por tipo, conteos abiertos; calculado en cliente con `take=1`, sin rango). F7A: filtro de
  almacén (a las seis tarjetas) y `CategoryProductPicker` (solo a En mano, Disponible —con 'Reservado'— y Bajo mínimo:
  `GET /api/v1/products?belowMin=true&take=1` → `total`; con producto, 'Sí/No' buscando su SKU con `belowMin=true`); los
  documentos llevan la marca 'almacén'. La selección se guarda en localStorage (`teikem.pulse.warehouseFilter.<tenant>.<usuario>`,
  `useWarehouseFilter` en `features/analytics/api.ts`) y lo que ya no existe se descarta sin error (`sanitizeWarehouseFilter`). `/account` es `features/account/AccountPage`
  (pestañas Perfil, Contraseña, MFA y Sesiones; `?tab=password|mfa|sessions` abre una pestaña directamente).
- `useSession()` (`app/session.tsx`) → `{ me, isAuthenticated, isLoading, error, tenantId, lang, setLang, logout, switchTenant,
  permissions, modules, reloadMe }`. `me` es `MeDto` de `GET /api/v1/me` (clave de consulta `ME_QUERY_KEY`).
  `setLang('en')` cambia diccionario y `Accept-Language` e invalida las consultas; no desmonta la pantalla ni el menú.
  `switchTenant(id)` → `POST /api/v1/auth/switch-tenant`, limpia la caché y vuelve a `/`. `logout()` revoca el refresh token y va a `/login`.
  `reloadMe()` vuelve a pedir `me` (p. ej. tras activar MFA).

## Menú y pantallas pendientes (`src/app/routes.tsx`, `src/app/Placeholder.tsx`)
El menú tiene los 41 ítems de la maqueta aunque la pantalla no exista: un ítem sin pantalla se declara con `pending({...})`,
que pone `Placeholder` (título y subtítulo del ítem, "Esta pantalla llega en un lote posterior." y "Abrir otra pantalla", que
abre la paleta) con los mismos `perm`/`module` que tendrá la pantalla real — el menú ya se filtra como al final.
```tsx
pending({ path: '/system/audit', perm: 'admin.audit', module: ModuleKeys.System, nav: { group: 'system', key: 'audit', order: 60 } }),
// al llegar la pantalla real, la misma fila pasa a:
{ path: '/system/audit', element: lazy(() => import('../features/system/AuditScreen')), perm: 'admin.audit', module: ModuleKeys.System, nav: { group: 'system', key: 'audit', order: 60 } },
```
- Textos del ítem: `nav.<key>.title` y `nav.<key>.subtitle` en `es.json`/`en.json` (los `nav.*` de la maqueta); grupo:
  `nav.groups.<group>`. Un ítem nuevo = su fila aquí + sus dos textos.
- `redirectTo(to)`: `element` de una ruta que dejó de ser pantalla propia (sin `perm`/`module`/`nav`: la guarda es la del
  destino); `<Navigate replace>`. Hoy `/warehouse/tasks` → `/warehouse/receipts?tab=putaway` y `/warehouse/dock-appointments` →
  `/warehouse/cross-dock-plans?tab=appointments`.
  `{ path: '/warehouse/tasks', element: redirectTo('/warehouse/receipts?tab=putaway') },`
- `redirectKeepingQuery(pathname, mapSearch?)`: igual, pero conserva la consulta de la dirección anterior (`mapSearch` la
  ajusta, lógica pura). Hoy `/warehouse/inventory` → `/warehouse/kardex` con `legacyInventorySearch` (sin `tab` era Saldos →
  `tab=balances`; `tab=kardex` → sin parámetro; los filtros se quedan).
  `{ path: '/warehouse/inventory', element: redirectKeepingQuery('/warehouse/kardex', legacyInventorySearch) },`
- `redirectWithParams(pathname, mapSearch(search, params))` (Lote 13): igual, pero la consulta nueva se arma también con los
  parámetros de la RUTA vieja (`:publicId`); conserva el `#hash`. Para una ficha que pasó a elegirse en una lista con un
  parámetro. Hoy `/warehouse/receipts/:publicId` → `/warehouse/receipts?receipt=<publicId>` (`legacyReceiptSearch`:
  `receipt` primero, los demás parámetros se quedan). Enlaces nuevos a un recibo: directo a `?receipt=` (así lo genera la
  Actividad reciente).
  `{ path: '/warehouse/receipts/:publicId', element: redirectWithParams('/warehouse/receipts', legacyReceiptSearch) },`
  Lote 14 (P8): igual `/warehouse/cycle-counts/:id` → `/warehouse/cycle-counts?count=<id>` (`legacyCountSearch`); la ficha
  propia del conteo se borró y los enlaces (Kárdex, Actividad reciente, descuadres) van directo a `?count=`.
- Pestañas enlazables: una lista con `Tabs` cuya pestaña deba poder abrirse desde un enlace la guarda en `?tab=` con
  `useSearchParams` (la primera pestaña = sin parámetro; valor desconocido = la primera), como `AccountPage`, Recibo
  (`asns|putaway`), Recolección (`replenish`), Cruce de muelle (`appointments|tasks`), Productos e
  inventario (`categories`), Transferencias y ajustes (`transfers`, Lote 14) y Kárdex de movimientos
  (`balances|reconciliation`).
- Lote 14 (D1): `/warehouse/inventory-adjustments` (la vieja 'Ajustes de inventario', faltantes de compra) salió del menú y es
  `redirectTo('/warehouse/purchase-orders')`: los faltantes se resuelven solo desde la pestaña Faltantes de cada orden.
- `<Placeholder navKey="audit" icon? brand? />`: `icon` = ícono del grupo (lo pone `pending`); `brand` = marca pequeña de Teikem
  arriba del aviso (por defecto `<BrandMark size={40} alt="" />`; `brand={null}` la quita).

## Paleta de comandos (`src/kernel/ui/CommandPalette.tsx`, `commandPaletteStore.ts`)
El shell la monta una vez con los ítems visibles del menú (mismo orden y grupos); se abre con "Buscar o ejecutar…", la lupa
(bajo 720 px, a pantalla completa) o los atajos `/` y Ctrl/⌘+K fuera de un campo de texto y sin otro diálogo abierto. Filtro
libre por título y subtítulo (`matchesQ`: sin mayúsculas ni acentos, cada palabra), ↑/↓ + Enter abre, Esc o clic fuera cierra.

| Pieza | Props / firma | Uso |
|---|---|---|
| `CommandPalette<T extends CommandItem>` | `open`, `items: T[]`, `onSelect(item)`, `onClose()` | diálogo `.scrim.cmdp > .pal` (combobox + listbox con `aria-activedescendant`); no se cierra solo al elegir |
| `CommandItem` | `{ id, group, groupLabel, title, subtitle?, icon? }` (textos ya traducidos) | un destino; los de un grupo, juntos |
| `openCommandPalette()` / `closeCommandPalette()` / `useCommandPaletteOpen()` | estado global | abrirla desde cualquier pantalla: `<button onClick={openCommandPalette}>` |
| `useCommandPaletteShortcut(onOpen?)` | hook | atajos `/` y Ctrl/⌘+K (lo usa el shell) |
| `filterCommands(items, q)`, `groupCommands(items)` | lógica pura | filtro y agrupación de la lista |

## Tema claro/oscuro (`src/kernel/ui/theme.ts`)
`data-theme="light" | "dark"` en `<html>` (tokens de `styles/tokens.css`), guardado en localStorage `teikem.theme`; sin elección
guardada, el del sistema (`prefers-color-scheme`). `initTheme()` en `main.tsx` antes de pintar; `setTheme('light')` cambia solo el
atributo (no desmonta nada); `useTheme()` → tema actual, para lo que dependa de él (p. ej. la variante de la marca).
```tsx
const theme = useTheme()
<button onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>{t('shell.theme.light')}</button>
```

## Marca (`src/kernel/ui/Brand.tsx`, archivos en `public/brand/`)
Los archivos son copia del paquete `Logos/` (no se editan aquí: se cambian en `Logos/` y se vuelven a copiar). Regla de la
maqueta (`brandLogoFor`): tema oscuro → variante `-inv` (trae fondo `#0B2C66`); claro → la normal; el idioma elige el lema.
El `src` cambia al cambiar idioma o tema sin recargar ni desmontar. Sin logo por compañía (no hay backend para guardar los
archivos: pendiente). **Colores por compañía** (lote F9, Ajustes → Marca; `Tenant.BrandingJson`):
- `brandTheme.ts` (puro, portado de la maqueta): `BRAND_PRESETS` (13 temas), `DEFAULT_BRAND`, `BrandSettings` (`{ preset,
  useCustom, custom: { flow, money, neutral } }`), `parseBranding(json)` / `serializeBranding(b, jsonAnterior)` (conserva otras
  claves), `brandColors`, `deriveAccent`, `deriveSurfaces`, `brandChecks(b)` (contraste WCAG contra el panel en los dos modos:
  texto ≥ 7, atenuado y acentos ≥ 4.5; matiz entre acentos ≥ 40°) / `brandChecksPass`, `brandCssVars(b, modo)` (vacío con la
  marca de siempre: no se toca `tokens.css`; nunca los colores de estado), `contrastRatio`, `hueDistance`, `isValidHex`.
- `<TenantBrand enabled />` (lo monta `SessionProvider`): escribe esas variables en `<html>` según el tema activo.
  `setBrandPreview(b | null)` (`brandPreview.ts`) = vista previa sin guardar (Ajustes → Marca); `applyBrandVars(el, b, modo)`.
  ```tsx
  setBrandPreview(draft)                       // toda la app se ve con el borrador
  await save.mutateAsync({ brandingJson: serializeBranding(draft, settings.brandingJson) })
  ```
  La validación de contraste y matiz es solo de pantalla: el servidor debe repetirla (pendiente de backend).

| Pieza | Props / firma | Uso |
|---|---|---|
| `BrandLockup` | `tagline?` (por defecto true; false = `teikem-2-horizontal-notagline`), `className?` | `<img alt="Teikem">` símbolo + TEIKEM + lema, ancho del contenedor (`.brand-lockup`, alto por proporción) |
| `BrandMark` | `size?` (px, cuadrado; 44 por defecto), `alt?` (`''` = decorativa), `className?` | solo el símbolo (`teikem-symbol.svg`, igual en ambos temas) para espacios chicos |
| `brandLockupSrc(lang, theme, tagline?)`, `BRAND_SYMBOL_SRC` (`brandAssets.ts`) | lógica pura | ruta del archivo (p. ej. para una prueba o un `<link>`) |

```tsx
<div className="auth-brand"><BrandLockup /></div>          {/* lockup por idioma y tema */}
<BrandMark size={44} alt="" />                              {/* barra colapsada */}
<Placeholder navKey="audit" brand={null} />                 {/* pendiente sin marca */}
```
Dónde va: barra lateral (lockup a todo el ancho con `drop-shadow(0 4px 14px var(--brand-glow))`; colapsada, `BrandMark` de
44 px; en el cajón móvil, siempre el lockup), `AuthLayout` (login, MFA, selección de compañía: lockup centrado arriba del
título), `<Splash full />` (lockup centrado sobre el indicador) y `Placeholder`. `index.html`: `favicon.ico`, PNG 256,
`apple-touch-icon` 180, `theme-color #0B2C66`, título "Teikem".

## Autenticación (`src/kernel/auth`)
- `login(email, password)` → `{ status: 'ok' } | { status: 'mfa_required', enrollmentRequired }` (AuthResultDto). La
  compañía nunca se pregunta (2026-09-30): el API entra a la predeterminada (o la primera por nombre) y se cambia con el
  selector de la cabecera (`switchTenant`). Pantallas: `/login`, `/mfa` (`features/auth`).
- `verifyMfa(code)` (challenge como Bearer → `/api/v1/auth/mfa/verify`); si el tenant exige MFA y el usuario no lo tiene:
  `enrollMfaWithChallenge()` → `confirmMfaWithChallenge(code)` (códigos de recuperación) → `verifyMfa`.
- `useReauth()` → `{ reauth(): Promise<boolean> }`: abre el modal (contraseña + código MFA si `me.mfaEnabled`,
  `POST /api/v1/auth/reauth`) y actualiza el access token. El cliente ya lo invoca solo ante 403 `aal2_required` y reintenta la
  acción; llama `reauth()` tú solo para pedirlo antes de una acción sensible. Lo monta `ReauthProvider` en la raíz.
- Tokens en memoria + `sessionStorage` (`tokens.ts`: `getTokens`, `setTokens`, `clearTokens`, `subscribeTokens`); refresh con rotación.

## Acceso (`src/kernel/access`)
- `<Can perm="orders.create" fallback?>…</Can>` (con arreglo exige todos; `perm="a|b"` = cualquiera), `useCan('a', 'b')` (todos),
  `useCanAny('admin.users', 'admin.roles')` (al menos uno), `useModule('CATALOG')`, `useAccess()` → `{ permissions, modules }`
  (Sets). Lógica pura: `permAllowed('a|b', permissions)`.
- `<ModuleGate module="CATALOG" perm?>…</ModuleGate>`: 'Módulo apagado' si el módulo no está encendido; 'Sin permiso' si falta `perm`.
- `ModuleKeys` (espejo de `ModuleKeys` del dominio: `LTL_GROUND`, `COD`, `WMS_LOTSERIAL`, `CROSSDOCK`, `CATALOG`, `ANALYTICS`, `SYSTEM`…).
- `ForbiddenScreen`, `ModuleOffScreen`: pantallas propias. En pruebas: `<AccessProvider permissions={[...]} modules={[...]}>`
  dentro de un `MemoryRouter`.

## Idioma (`src/kernel/i18n`)
- En componentes: `const t = useT()` → `t('clave.anidada', { param })`; `useLang()` → `'es' | 'en'`. Fuera de React: `t()`.
- Diccionarios `es.json` / `en.json` anidados por sección (`common`, `errors`, `auth`, `access`, `shell`, `nav`, y una sección
  por módulo: `clients.*`, `orders.*`…). Falta en inglés → se usa el español; falta en ambos → se muestra la clave (y aviso en
  consola en desarrollo). `{nombre}` se sustituye con los parámetros.

## Estilos (`src/styles`)
- `tokens.css`: variables de la maqueta (paleta oscura por defecto, `data-theme="light"`), `--flow` (operación) y `--money` (dinero).
- `base.css`: clases de la maqueta: `.head`, `.btn(.flow/.money/.sm/.block)`, `.panel .ph2 (.r = contador) .pb` (panel de
  contenido en pantalla: lo pinta `Panel`), `.pal .pi .pb .ft` (solo modales y la paleta de comandos, dentro de `.scrim.on`), `.chip` + `.s-cap/.s-wh/.s-disp/.s-route/.s-deliv/.s-cod/.s-fail/.s-warn`,
  `.lst` (tabla), `.f` + `.ferr` + `.r2/.r3` (formularios), `.sw` (toggle), `.filters`, `.seg`, `.msel`, `.qrow .qbox`, `.empty`,
  `.note`, `.tag`, `.toast`, `.spin`. Breakpoints: 900 px (cajón), 720 px (tablas a tarjetas), 480 px (una columna; un filtro
  por renglón). `.filters` es flex que envuelve (cada `.f` desde 140 px, crece; `.span2` el doble): sin columnas vacías.
  Todo `<select>` de una opción (sin `multiple`/`size`) pierde la flecha nativa y lleva un chevron SVG a la IZQUIERDA
  (`--select-chev`, 9 px del borde, `padding-left:29px`), igual que `SearchSelect`/`CategoryProductPicker`. Un `<select>`
  con estilo en línea no debe usar el shorthand `background` (borra el chevron) ni fijar el padding izquierdo por debajo de 29 px.
  `.md` (maestro-detalle a dos columnas, 280 px + resto; una columna bajo 720 px) + `.domlist`/`.domit(.on)` (lista de la
  izquierda: botón por fila, con chip de origen y contador opcional `.cnt`) — patrón de "Catálogos de valores" (F8a P6) y
  cualquier pantalla con una lista de la izquierda y el detalle a la derecha; bajo 720 px usa un `<select>` en vez de `.domlist`.
  `.unrow` (fila de lista de la maqueta: ancho completo, borde inferior, `.meta` en tono tenue; como `<button className="unrow">`
  es una fila elegible sin estilo nativo y `.unrow.on` = la elegida, fondo `--flow-bg`) — lista maestra de Recibo.
  `.rcp-cols` (Lote 13, `warehouse.css`): el de Recibo, 340 px + resto
  (`.cols 340px 1fr` de la maqueta), una columna bajo 720 px con la lista arriba (alto máx. 260 px).
  Lote 14 (`warehouse.css`): `.qty-in`/`.qty-out`/`.qty-zero` (cantidad que entra, sale o neutra), `.seg.adj-dir` (Subir/Bajar),
  `.filters > .f-na` (filtro que la pestaña no aplica: atenuado, `display: contents`; `.f-na.empty` = sin valor, se oculta
  bajo 720 px) + `.filters-note` (ayuda bajo la barra),
  `.kx-facts` (rejilla `dl` de datos de solo lectura que envuelve a 360 px), `.kx-doc` (recuadro del documento de origen).

## Datos comunes (`src/kernel/catalogs`, `src/kernel/custom-fields`, `src/kernel/dsl`)
- `useLookups(domain, { includeDisabled?, enabled? })` → `useQuery` con `LookupOption[]` (`{ code, label, description, sortOrder,
  isEnabled }`) de `GET /api/v1/catalogs/{entity}` (caché 10 min; `code` = InternalCode que se envía al API).
  `const { data: regions = [] } = useLookups('Region')`. `includeDisabled` solo para pintar valores históricos.
- `useStatuses(domain, { includeDisabled? })` → `StatusOption[]` (`{ code, label, color, stageKind, isInitial, isEnabled,
  sortOrder, icon }`) de `GET /api/v1/status/{entity}`, ordenados. `color` es `#hex` validado o null. `StageKinds` =
  `PIPELINE | LATERAL | TERMINAL`. También: `useStatusHistory(entityType, id)`, `useLateralEntries(entityType)`,
  `usePipelineValidation(domain)`, `catalogKeys` (claves de consulta para invalidar).
- `useTenantSettings(enabled = true)` → `useQuery` con `TenantSettingsDto` de `GET /api/v1/tenant/settings` (compañía activa; solo
  exige sesión, sin permiso ni módulo; caché 10 min, 403 sin sacar de la pantalla; clave `catalogKeys.tenantSettings`): p. ej.
  `defaultServiceType`/`defaultPackageType` (código o null). `lookupLabelOrCode(code, options)` → etiqueta del código entre las
  opciones de `useLookups` (sin distinguir mayúsculas), el código si no está, null sin código. Lo usa Empacar:
  ```tsx
  const { data: settings } = useTenantSettings()
  const { data: serviceTypes = [] } = useLookups('ServiceType')
  const defaultService = lookupLabelOrCode(settings?.defaultServiceType, serviceTypes)   // 'Estándar' | 'OLD' | null
  ```
- `<StatusChip domain="OrderStatus" code={dto.statusCode} label?={dto.statusLabel} />`: píldora con etiqueta y color del tenant.
- `<StatusPipeline domain="OrderStatus" entityType="TRANSPORT_ORDER" entityId={id} currentCode={dto.statusCode}
  onTransition={(toCode, comment) => mutation.mutateAsync({ toCode, comment })} disabled={!canChange} />`: etapas del
  pipeline (hechas/actual/pendientes), laterales y terminales como píldoras, y botones con las transiciones que el servidor
  aceptaría (réplica de `StatusService`: siguiente etapa; laterales/terminales según `/status/lateral-entries/{entityType}`;
  desde un lateral, volver a la última etapa del historial o la siguiente). Abre un diálogo con comentario opcional (máx. 500);
  si `onTransition` lanza, muestra el error del servidor sin cerrar. Pasa `entityId` para que el regreso desde un lateral
  sea exacto. `manualTargets={['SENT', 'CANCELLED']}` limita los botones a esos códigos cuando las demás transiciones las
  dispara el sistema (p. ej. PARTIAL/RECEIVED de una orden de compra al confirmar el recibo). `/status/{entity}/validate` se consulta para avisar si la configuración del tenant tiene errores (no da
  transiciones). Sin `onTransition` (o con `disabled`) es solo lectura. Lógica pura: `allowedTransitions`, `stepStates`.
- `<StatusHistory entityType entityId domain? />`: historial (más reciente arriba) con quién, cuándo y comentario.
- `<CustomFieldsForm entityType="CLIENT" entityId={id} form={form} name?="customFields" disabled? />`: pinta dentro del formulario
  de la pantalla los campos de `GET /api/v1/custom-fields/definitions?entityType=` por tipo (TEXT, NUMBER, DATE, DATETIME,
  BOOL, SELECT, MULTISELECT, LOOKUP_REF; SELECT con `refEntity` usa el catálogo; MULTISELECT usa `SearchMultiSelect`, con buscador), con el valor de
  `GET /values/{entityType}/{id}` (o el `defaultValue` en el alta). Valida en cliente obligatorio, tipo y `validationJson` (DSL).
  Los valores viven en `form` bajo `customFields.<fieldKey>`. Guardar: `const { save } = useSaveCustomFields('CLIENT')` y en el
  submit (tras crear el registro) `const problem = await save(id, form)` → `PUT /values/{entityType}/{id}`; null = guardado;
  si no, los errores quedan bajo cada campo y `problem.title` va al toast. Conversión pura: `toFormValue`, `fromFormValue`,
  `validateCustomField`.
- `evaluateRule(rule, row)` / `matches(row, rule)` / `compileFilter(rule)` / `referencedFields(rule)` / `validateValue(value,
  validationJson)` (`src/kernel/dsl`): misma semántica que `RuleEvaluator.cs` (and/or/not y arreglo = and; eq, ne, gt, gte, lt,
  lte, contains, startsWith, endsWith, in, notIn, between, isNull, notNull, isTrue, isFalse; números como números, fechas como
  fechas —sin zona = UTC—, texto sin distinguir mayúsculas; campo sin distinguir mayúsculas). `validateValue` devuelve motivos
  con código (`minLength`, `maxLength`, `pattern`, `patternInvalid`, `min`, `max`, `minDate`, `maxDate`) para traducir con
  `customFields.errors.<code>`. Casos de `RuleEvaluatorTests.cs` portados en `RuleEvaluator.test.ts`.

## Componentes UI (`src/kernel/ui`)
Importa de `../../kernel/ui` (barril `index.ts`). Estilos propios en `ui.css` (cada componente lo importa; no toca `base.css`).
Todos los textos que reciben (`label`, `header`, `title`…) llegan ya traducidos con `t()`.

| Componente | Props | Uso |
|---|---|---|
| `Panel` | `title?`, `icon?` (ícono antes del título), `badge?: string \| number` (contador a la derecha, misma línea), `subtitle?` (solo texto descriptivo, en su propia línea; un conteo va en `badge`), `actions?`, `footer?`, `flush?` (cuerpo sin padding, para tablas), `children` | panel de contenido en pantalla de la maqueta: `.panel` (radio 13 px, sin sombra ni recorte: los desplegables salen) con cabecera `.ph2` de una sola línea (ícono + título `h2` + contador `.r` + acciones), cuerpo `.pb` y pie `.ft`. No es un modal: los modales son `Modal`/`ConfirmDialog` (`.scrim > .pal`, radio 15 px con sombra); nunca uses `.pal` para contenido en pantalla. `<Panel flush icon={<IconWarehouse />} title={t('warehouse.list.title')} badge={data ? rows.length : undefined}>` |
| Íconos de pantalla (`screenIcons.tsx`) | `IconBox`, `IconLayers`, `IconCash`, `IconUsers`, `IconChart`, `IconGear` (grupos del menú, `NAV` de la maqueta; `app/icons.tsx` los reexporta), `IconPin` ('pin'), `IconRoute` ('route') e `IconPhoneFormat` ('print': panel Teléfono) de Ajustes de la compañía (lote F9), e `IconWarehouse`, `IconGrid`, `IconCart`, `IconCheckin`, `IconBasket`, `IconClip`, `IconSwap`, `IconDoc`, `IconClock`, `IconLock`, `IconPencil` (`ICONOF` de la maqueta), `IconTag` ('tag': KPI "Con número de serie") e `IconCheck` ('check' de la maqueta, de `icons.tsx`: estados vacíos "todo resuelto") | el `icon` de `Panel` es el que la maqueta da a la pantalla en el menú (Almacenes → `IconWarehouse`, Ubicaciones → `IconGrid`, Productos e inventario/Órdenes → `IconLayers`, Compras → `IconCart`, Recibo → `IconCheckin`, Transferencias y ajustes → `IconPencil` (Lote 14: el lugar y el ícono de la vieja 'Ajustes de inventario'), Recolección → `IconBasket`, Conteo → `IconClip`, Cruce de muelle → `IconSwap`, Kárdex → `IconDoc`, Usuarios → `IconUsers`, Roles → `IconShield` de `actionIcons`); una pantalla que no está en la maqueta usa el ícono de su grupo. `import { IconWarehouse } from '../../kernel/ui'` |
| `DataTable<T>` | `columns: DataColumn<T>[]`, `rows`, `rowKey(row)`, `sort?`/`onSort?`, `defaultSort?`, `page?`/`pageSize?`/`total?`/`onPage?`, `onPageSize?(size)` (servidor: el usuario cambió "Filas por página"), `pagination?` (por defecto true; false = todas las filas, sin rango ni selector), `rowActions?`, `onRowClick?`, `rowClassName?(row)` (clase extra de la fila y de su tarjeta; `'dim'` = atenuada, opacidad .55 de la maqueta para inactivos: `rowClassName={(p) => (p.isActive ? undefined : 'dim')}`), `empty?`, `loading?`, `label?`, `dense?`, `exportable?` (por defecto true), `exportFileName?` (base del archivo; por defecto `label` y luego el título del `Panel`), `exportRows?()` (filas a exportar en vez de las cargadas: `Promise<T[] \| {items, truncated}>`), `exportChildren?` (exportación agrupada con filas hijas, ver `exportChildren`), `forceCards?` (Lote 13: tarjetas aunque la ventana sea ancha —tabla dentro de un panel angosto de `SplitPane`, decidido con `useElementWidth`—; el pie no cambia) | tabla (TanStack Table v9) con orden por columna (flecha ▲/▼, `aria-sort`, primer clic ascendente, vacíos al final), paginación (local por defecto, 25 filas) con pie completo y tarjetas bajo 720 px (título + "etiqueta: valor" + acciones; selector "Ordenar por"). Sin scroll horizontal de página ni scrollbar propio: las celdas y encabezados parten el texto, los números no (todos los encabezados con la misma letra, también los de columnas `align: 'end'`: solo las celdas numéricas van en monoespaciada); entre 721 y 1100 px baja el padding y con `dense` (automático desde `DENSE_COLUMNS` = 8 columnas contando acciones) usa `.densetbl` (tipografía y padding menores) |
| `DataColumn<T>` | `id`, `header`, `cell(row)`, `sortValue?(row)` (ordenable en cliente), `sortable?` (ordenable en servidor), `align?: 'end'` (número), `card?: 'title' \| 'hidden'`, `exportValue?(row)` (valor exportado explícito), `exportable?` (false = no se exporta: casillas, columnas solo visuales), `signed?` (número con signo +5 / -3 en el PDF agrupado de `exportChildren`) | definición de columna (la primera visible es el título de la tarjeta si ninguna dice `title`). Exportación: `exportValue`, si no el texto de `cell` ("—" = vacío; un número formateado igual a `sortValue` sale como número), si la celda no tiene texto (`StatusChip`, ícono) `sortValue` |
| `fetchAllPages(fetchPage, { pageSize?, max? })` (`kernel/api/fetchAllPages`) | `fetchPage(skip, take) → Promise<{ items, total }>` | recorre `skip/take` de a `EXPORT_PAGE_SIZE` = 200 hasta el `total` o `EXPORT_MAX_ROWS` = 10 000 y devuelve `{ items, truncated }`: es lo que recibe `exportRows` (DataTable avisa con un toast si `truncated`). `exportRows={() => fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/x', { params: { query: { ...query, skip, take } } })))}` |
| `reportPdf.ts` (Lote 12; importar de `kernel/ui/reportPdf`, no está en el barril) | `downloadReportPdf(spec)`, `renderReportPdf(spec, { logo?, compress? })` → `jsPDF` en memoria; `ReportSpec` = `{ title, subtitle?, company?, user?, generatedAt?, locale, filters: {label,value}[], columns: {header, format?}[], sections: {title?, rows, subtotal?}[], totals?, summary?: {label,value,tone?}[], notices?, emptyText?, orientation? }`; `format` = `text` \| `quantity` \| `signed` \| `money` \| `unitCost` | PDF "de presentación" en el cliente (jsPDF + autotable con import dinámico): banda azul marino con el símbolo de Teikem (SVG de `public/brand/` rasterizado a PNG; si no se puede, solo texto), lema, compañía y fecha; título, "Generado el … por …", recuadro "Filtros aplicados" (vacío = "Sin filtros"), tarjetas de resumen y avisos; tabla con encabezado oscuro repetido por página, cebra, números a la derecha con separadores del idioma, fila de grupo por sección, subtotal (la etiqueta —primer valor— se funde con los vacíos que la siguen) y total general en azul marino; pie "Generado con Teikem · compañía" y "Página X de Y"; banda delgada en las páginas siguientes. Horizontal con más de 6 columnas. Textos del kit en `ui.report.*`; todo pasa por `pdfSafeText`. Lógica pura: `formatReportValue`, `buildReportBody`, `reportOrientation`, `reportFileName` (`reporte-de-inventario-advance-depot-2026-09-30.pdf`). `await downloadReportPdf({ title, company, user, locale: lang, filters, columns, sections, totals })` |
| `exportChildren({ children, columns, emptyText, titleColumns? })` (`kernel/ui/exportChildren`, en el barril; armado y PDF en `exportGrouped.ts`, que `exportTable` carga bajo demanda) | `children(row) → C[] \| null` (hijas de la fila), `columns: DataColumn<C>[]` (mismas reglas de exportación: `exportValue`, texto de `cell`, `sortValue`, `exportable`, `align: 'end'`, `signed`), `emptyText` (PDF de una madre sin hijas), `titleColumns` (cuántas de las primeras columnas de la madre forman el título de la banda; 1) → `ExportChildren<T>` (el tipo de la hija queda encapsulado) | **exportación agrupada opcional** (madre con hijas, p. ej. recibos con sus líneas) para `ListPager.exportChildren` / `DataTable.exportChildren` / `exportTable(…, { children })`; sin ella todo sale igual que siempre. Excel/CSV: una fila **por hija** repitiendo las columnas de la madre (madre sin hijas = una fila con las de hija vacías). PDF (carta horizontal): el encabezado de las exportaciones (compañía, título, "Generado el …" y línea de filtros), un bloque por madre con banda (título = primeras `titleColumns` columnas; el resto como "etiqueta: valor" en 4 pares por renglón con anchos fijos; vacío = "-") y debajo la tablita de hijas (encabezado oscuro que se repite si cruza de página, cebra, números a la derecha con separadores del idioma, signo en `signed`); la banda no queda huérfana al pie; `emptyText` si no hay hijas; pie con el título y "Página X de Y". Las filas madre deben traer sus hijas (pide al API la lista con sus hijas en lote, p. ej. `includeLines=true`, nunca el detalle una por una). Lógica pura: `buildGroupedExportData`, `flattenGroupedExport`, `formatExportNumber`; `renderGroupedPdf(data, { title, locale, compress })` → `jsPDF` en memoria. Ejemplo: Recibo (`features/warehouse/receiptExport.ts`). `exportChildren={exportChildren({ children: (r) => r.lines, columns: lineCols, emptyText: t('…noLines'), titleColumns: 2 })}` |
| `RowAction<T>` | `key`, `label`, `onClick(row)`, `perm?` (guarda de permiso: sin él no se pinta), `visible?(row)` (guarda de estatus/`capabilities`), `disabled?(row)`, `tone?: 'flow' \| 'danger'`, `icon?` (de `kernel/ui/actionIcons`) | acciones por fila. Con `icon`, el botón es solo ícono (28×28, `.rowbtn`, como en la maqueta) y `label` pasa a ser su `aria-label`/`title`; sin `icon`, es el botón de texto de siempre. Toda acción de fila nueva lleva `icon` — solo se deja sin él cuando de verdad no hay un ícono claro para esa acción |
| `Filters` | `children`, `onClear?` (botón "Limpiar"), `label?` | fila `.filters` (flex que envuelve: los filtros se reparten todo el ancho del panel, sin huecos; uno por renglón a 480 px); "Limpiar" (`.filters-clear`) ocupa solo el ancho de su contenido. `SelectFilter`, `SearchSelect`, `DateRangeFilter` y `QBox` se anotan solos en la línea de filtros de las exportaciones (ver "Encabezado de las exportaciones y línea de filtros") |
| `FilterScope` / `ExportCompanyProvider` / `useRegisterFilter` / `useExportHeading` | `off?` / `company` / `(label, value \| null, ref?, enabled?)` / `() => () => { company, filters }` | ámbito de la línea de filtros y compañía de las exportaciones (los pone el shell; `Modal` = `off`). `<FilterScope><DetallePanel /></FilterScope>` |
| `SelectFilter` | `label`, `value` (`''` = todos), `onChange`, `options: {value,label}[]`, `allLabel?` (`null` = sin opción "Todos") | filtro de selección única |
| `DateRangeFilter` | `label`, `value: {from,to}` ('YYYY-MM-DD' o ''), `onChange` | rango de fechas (ocupa el doble que un filtro normal); filtrar lo cargado con `inDateRange(iso, range)`; `EMPTY_RANGE` |
| `SearchSelect` | `label`, `options`, `value: string[]` (vacío = todos), `onChange`, `placeholder?` | selección múltiple con buscador (`.msel`), botones Todos/Ninguno; cierra con Escape o clic fuera. La flecha va a la izquierda del resumen (también en `CategoryProductPicker`) |
| `SearchMultiSelect` | `options`, `value`, `onChange`, `placeholder?`, `id?` (del botón, para `<label htmlFor>`), `labelledBy?`, `disabled?`, `invalid?`, `describedBy?`, `onBlur?`, `buttonRef?` | el mismo control sin etiqueta propia, para un `.f` que ya tiene su `<label>` (formularios, campo personalizado MULTISELECT). Toda selección múltiple usa este control o `SearchSelect`, nunca una lista cruda de casillas |
| `ComboSelect` | `options: ComboOption[]` (`{ value, label, hint? }`; `hint` = texto tenue que también se busca), `value` (`''`/null = ninguno), `onChange(value, option)` (`''`, null al quitar), `placeholder?`, `clearable?` (por defecto true: ✕), `loading?`, `disabled?`, `invalid?`, `required?`, `id?`, `aria-*`, `onBlur?` | selección ÚNICA con buscador sobre una lista local (catálogos, zonas…): combobox como `ClientPicker` (↑/↓/Enter/Escape, clic para reabrir) que filtra con `matchesQ` (etiqueta, valor o `hint`); Enter con el texto igual al valor o a la etiqueta de una opción la elige (lector de código de barras; `exactComboMatch` en `comboMatch.ts`). Úsalo en vez de un `<select>` cuando la lista es larga o se busca. En un `Field`: `ComboSelectInput` (`options`, `placeholder?`, `loading?`, `onPicked?(option)`; valor del formulario = `value`, `''` = ninguno). `<Field name="zoneType" label={t('…type')}><ComboSelectInput options={types} /></Field>` |
| `QBox` + `matchesQ(q, ...texts)` | `value`, `onChange`, `placeholder?` | buscador libre sobre lo que se muestra: cada palabra de `q` debe aparecer en algún texto, sin mayúsculas ni acentos. Se aplica DESPUÉS de los filtros |
| `Chip` | `tone?: 'neutral'\|'cap'\|'wh'\|'disp'\|'route'\|'deliv'\|'cod'\|'fail'\|'warn'`, `color?` (hex del catálogo), `title?`, `children` | píldora que nunca envuelve. Para estatus usa `StatusChip` (catálogo del tenant) |
| `Modal` | `open`, `title`, `onClose`, `footer?`, `size?: 'sm'\|'md'\|'lg'`, `dismissible?` (false mientras guarda) | portal en `<body>`, `.scrim > .pal` con el padding del shell; Escape/clic fuera cierran; foco al primer control y vuelta al cerrar; su cuerpo va en `<FilterScope off>` (sus tablas exportan sin línea de filtros) |
| `ConfirmDialog` | `open`, `title`, `message`, `confirmLabel?`, `tone?: 'flow'\|'danger'`, `onConfirm()` (async), `onClose` | confirma bajas o acciones con guarda de estatus. Si `onConfirm` lanza (422 `status_rule`, 409…), muestra el mensaje del servidor y no se cierra |
| `Form` | `form` (de `useForm({ resolver: zodResolver(schema) })`), `onSubmit(values)` (async), `onError?(problem)`, `id?` (para `<button type="submit" form={id}>` en el pie del Modal) | si `onSubmit` lanza, `applyProblemDetails(err, form)` pone cada error bajo su `Field`; el título y los errores sin campo van en un aviso arriba del formulario |
| `Field` | `name` (camelCase, como el DTO), `label`, `required?` (asterisco; la regla va en zod), `help?`, `hideLabel?` (Lote 13: la etiqueta queda solo para lectores de pantalla, `.sr-only`; el error y la ayuda se ven), `children` (un control) | etiqueta + control + ayuda + error (`.ferr`, `aria-invalid`, `aria-describedby`). `hideLabel` = campo dentro de una celda de una rejilla cuyo encabezado ya dice qué es; la etiqueta sigue siendo única por fila: `<Field name="qty" label={t('…qtyOfLine', { n: i + 1 })} hideLabel><NumberInput /></Field>` |
| Controles de `Field` | `TextInput` (`type?`), `NumberInput` (valor `number \| null`; vacío = null → `z.number().nullable()`), `Select` (`options`, `placeholder?`; valor string, '' = sin elegir), `DateInput` ('YYYY-MM-DD'), `Toggle` (`text?`; boolean), `TextArea` (`rows?`), `ClientPickerInput` (`includeInactive?`; valor publicId o null) | se registran solos en el formulario con el `name` del `Field`; aceptan los atributos nativos (`maxLength`, `min`, `placeholder`…) |
| `PhoneInput` + `formatPhone`/`formatPhoneInput`/`isValidPhone`/`normalizePhone`/`phonePlaceholder`/`phoneDigits` (`phone.ts`, reexporta `kernel/format/phone`) | `placeholder?` (por defecto la máscara con ceros) | teléfono con la máscara de la COMPAÑÍA (Región y formatos; Puerto Rico `(###) ###-####`) mientras se escribe, dentro de un `Field` (valor con máscara; vacío = ''). Valida con `useFormat().isValidPhone` (vacío o los dígitos de la máscara) en el esquema zod; al editar, `formatPhone` (alias viejo `normalizeStoredPhone`) muestra con máscara un valor con los dígitos exactos; al guardar, `normalizePhone` deja solo los dígitos (si no calza, tal cual). Ejemplo: Proveedores |
| `Tabs<K>` | `tabs: {key,label}[]`, `value`, `onChange`, `label?` | pestañas de una ficha (`.seg`, `role="tablist"`) |
| `EmptyState` | `title`, `body?`, `icon?`, `action?` | sin datos / sin resultados |
| `Spinner` | `label?`, `block?` (centrado) | carga (`role="status"`) |
| `toast` | `toast.success(msg)`, `toast.error(msg)`, `toast.info(msg)` | aviso abajo al centro (errores 6 s, resto 3.5 s). Se monta solo en `<body>` la primera vez; llamable desde `onSuccess` |
| `ClientPicker` | `value` (publicId \| null), `onChange(publicId, client)`, `includeInactive?` (por defecto false), `placeholder?`, `disabled?`, `invalid?`, `aria-label?`, `filterLabel?` (como filtro de una lista: se anota en la línea de filtros de las exportaciones) | combobox con buscador: `GET /api/v1/clients?search=&includeInactive=` (250 ms entre teclas; mantiene los resultados anteriores mientras busca), opciones "Code · Name" (marca "Inactivo"), teclado ↑/↓/Enter/Escape. Con un valor inicial pide `GET /api/v1/clients/{publicId}` para mostrar la etiqueta. Sin `clients.read`: "Su usuario no puede consultar clientes" (no saca de la pantalla) |
| `CategoryProductPicker` | `value: CategoryProductValue` (`{kind:'category', id}` \| `{kind:'product', publicId}` \| `null` = todos), `onChange(value, detail?)` (`detail.category`/`detail.product` = fila elegida), `categories` (árbol completo: `useProductCategories().data`), `categoriesLoading?`, `label?` (por defecto "Categoría o producto"), `id?`, `disabled?` | un solo combobox con buscador y dos secciones: 'Categorías' (árbol con sangría por nivel y "N productos" contando sus subcategorías —`categoryProductTotals`, como filtra el API—; el texto filtra por nombre o ruta) y 'Productos' (`GET /api/v1/products?search=&activeOnly=true&take=20`, 250 ms entre teclas, "SKU · Nombre"; sin texto no consulta). El valor se ve como píldora ("Categoría"/"Producto" + ruta o "SKU · Nombre", con elipsis) con ✕ para quitarlo; un producto que llega de fuera pide `GET /api/v1/products/{publicId}` para su etiqueta. Teclado ↑/↓ (recorre ambas secciones), Enter, Escape (cierra y devuelve el foco). A ≤ 480 px ocupa todo el ancho; por encima, su desplegable mide 340 px anclado a la izquierda, así que dale un contenedor de al menos 340 px (p. ej. `flex: 1 1 340px`) si un ancestro recorta con `overflow: hidden` (`.pal`). 403 de productos: aviso en su sección. Lógica pura en `categoryTree.ts`: `categoryTree(cats)` (aplanado padre→hijos con `level`; padre ausente = raíz), `filterCategoryTree`, `categoryLabel`, `isCategoryProductValue` (validar lo leído de localStorage), `sameCategoryProduct`, `categoryProductTotals(cats)` (id → productos del subárbol; `productCount` del DTO son solo los directos) |
| `useMediaQuery(q)`, `CARDS_QUERY` | | `true` mientras se cumpla la media query (`'(max-width: 720px)'` = modo tarjetas) |
| `useElementHeight(element)` (Lote 15) | `element: Element \| null` → `number` | alto en px (entero) del ELEMENTO (no de una ref: sirve para uno que se monta después o cambia), al día con `ResizeObserver`; 0 sin elemento o sin medir (jsdom). Para desplazar filas fijas: `const [el, setEl] = useState<HTMLDivElement \| null>(null); const h = useElementHeight(el)` → `<div ref={setEl}>…</div>` y `style={{ '--alto': `${h}px` } as CSSProperties}` |
| `useElementWidth(ref)` (Lote 13) | `ref: RefObject<Element \| null>` → `number` | ancho en px (entero) del elemento, al día con `ResizeObserver`; 0 sin medir (jsdom). La ref apunta a un elemento que se monta con el componente (no condicional). Para decidir por el ancho de un PANEL y no de la ventana: `const ref = useRef<HTMLDivElement>(null); const w = useElementWidth(ref)` → `<div ref={ref}><DataTable forceCards={w > 0 && w < 640} … /></div>` |
| `SplitPane` (Lote 13) | `storageKey` (estable; se guarda en localStorage `teikem.split.<storageKey>`), `defaultRatio?` (0.6), `minRatio?`/`maxRatio?` (0.35 / 0.75), `minPx?: [A, B]` ([420, 320]), `stackBelow?` (900: ventana ≤ ese ancho = una columna), `label?` (nombre accesible de la barra; por defecto `ui.split.resize` "Cambiar el ancho de los paneles"), `className?`, `children: [A, B]` | dos paneles lado a lado (grid `minmax(0, A) 10px minmax(0, 1fr)`, sin `overflow:hidden`: los desplegables salen) con una barra `role="separator"` (`aria-orientation="vertical"`, `aria-valuenow`/`min`/`max` en % del panel A, foco visible). Arrastre con Pointer Events + `setPointerCapture` (también con el dedo: `touch-action:none`), ←/→ 5 %, Home/End a los límites, Enter o doble clic = `defaultRatio` (quita lo guardado). Guarda al soltar y con cada tecla (localStorage en try/catch: si falla o lo guardado no es un número entre 0 y 1, vale el por defecto). La proporción se acota a `minRatio`/`maxRatio` y a que cada panel conserve `minPx`. Una sola columna sin barra (primero A, luego B) con la ventana ≤ `stackBelow` o si el contenedor no alcanza para `minPx[0] + minPx[1]` + la barra. Los hijos no se vuelven a montar al apilarse (el borrador de un formulario sobrevive). Puras en `splitRatio.ts`: `clampSplitRatio(r, anchoÚtil, minPx, min, max)`, `splitBounds`, `readSplitRatio(raw, def)`, `ratioFromPointer`, `splitStorageKey`. `<SplitPane storageKey="pickBatches"><CollectPanel /><PickBatchesPanel /></SplitPane>` |
| `SummaryBar` (Lote 14) | `items: SummaryItem[]` (`{ key?, label, value, tone?: 'in' \| 'out' \| 'money' \| 'muted', title? }`), `label?` (nombre accesible del grupo), `loading?` (cifras atenuadas y `aria-busy`), `aside?` (leyenda o aviso a la derecha) | franja `.burst` de la maqueta (`ledger()`): etiqueta pequeña en mayúsculas y el número en monoespaciada, separadas por una raya vertical; `in` = color de flujo, `out` = peligro. Envuelve en renglones a 360 px (dos por renglón bajo 480 px). La usan el Kárdex (Movimientos · Entradas/Salidas en movimientos y unidades · Internos; en Saldos además En mano y Disponible), Transferencias y ajustes y los descuadres. `<SummaryBar label={t('…label')} items={[{ label: 'Movimientos', value: 126 }, { label: 'Entradas (uds)', value: '+132', tone: 'in' }]} />` |
| `ListPager<T>` (Lote 13) | `page` (base 1), `pageSize`, `total` (0 = no se pinta), `onPage?` (‹ › si hay más de una página), `onPageSize?` (selector "Filas por página" 10/25/50/100 + el tamaño actual), `showRange?` (true), `exportColumns?: DataColumn<T>[]` + `exportRows?()` (Exportar Excel/CSV/PDF con las reglas de exportación de `DataTable`; toast si viene `truncated`), `exportFileName?` (por defecto el título del `Panel`), `exportChildren?` (exportación agrupada: filas hijas de cada fila, ver `exportChildren`), `onExport?(format)` + `exportCount?` (exportación propia) | el pie de `DataTable` (mismas clases `.dt-pager` y textos `ui.table.*`; `DataTable` lo usa para su propio pie) para listas que no son `DataTable`, p. ej. la lista maestra `.unrow` de Recibo paginada en el servidor. `PAGE_SIZE_OPTIONS`, `pageSizeOptions(size)` en `pageSize.ts`. `<ListPager page={page} pageSize={size} total={data?.total ?? 0} onPage={setPage} onPageSize={(n) => { setSize(n); setPage(1) }} exportColumns={cols} exportRows={() => exportReceipts(query)} />` |

**Orden y paginación de `DataTable`**
- Toda columna que muestre un dato lleva `sortValue` (o `sortable` con orden del servidor); solo se quedan sin orden las
  columnas sin dato propio (acciones, casillas de selección). Columna compuesta ("Cliente · Ciudad") → el campo principal;
  chip de estatus → la etiqueta (`status ?? statusCode`), nunca el color.
- **Pie de toda tabla** (con filas): rango y total ("1–25 de 551", también con una sola página), selector "Filas por página"
  (10/25/50/100; el `pageSize` inicial se agrega si no es uno de ellos), botón Exportar (Excel/CSV/PDF, nota "Filas: N" con lo
  que saldrá en el archivo) y ‹ › solo si hay más de una página. Envuelve en renglones a 360 px (sin scroll horizontal).
- Lista completa (la mayoría de endpoints de catálogo): no pases `onSort` ni `onPage`; DataTable ordena con `sortValue` (orden
  natural: `A-2` antes que `A-10`) y **pagina sola** (tamaño inicial `pageSize ?? 25`, cambiable con el selector; al cambiarlo
  vuelve a la página 1). Pasa `rows` memorizadas (`useMemo`): una lista nueva vuelve a la página 1. Exporta todas las filas
  cargadas en el orden actual, no solo la página visible.
- `pagination={false}` (todas las filas, sin rango ni selector; Exportar sigue si `exportable`) solo cuando paginar esconde o
  rompe algo: filas con controles editables y estado propio (p. ej. la rejilla de captura de Recibo o de Recolección: al
  cambiar de página se perdería lo escrito), o tablas con su propia carga incremental ("Ver más" de la Actividad reciente). Agrega
  `exportable={false}` en tablas de apoyo dentro de un `Modal` donde el archivo no tenga sentido. Si no, deja la paginación.
- Servidor (endpoints con `skip/take` y `total`): controla `sort`/`onSort`, `page`/`onPage` y el tamaño (`pageSize` +
  `onPageSize`, que vuelve a la página 1), pasa `total`, y pon todo en la clave de la consulta (`placeholderData:
  keepPreviousData` evita el parpadeo). `onSort` recibe `{ id, desc }` (id de la columna). Sin `onPageSize` no hay selector.
  Pasa `exportRows` para que Exportar saque **todo lo filtrado** (no solo la página que llegó): el mismo `query` con
  `fetchAllPages`; si el orden es local (sin `onSort`), DataTable reordena lo exportado como la tabla. En el almacén ya
  existen `exportProducts(query)`, `exportInventoryBalances`, `exportInventoryTransactions`, `exportReceipts` (y `exportReceiptsWithLines`: con `includeLines=true`, para la exportación agrupada),
  `exportWarehouseTasks`, `exportPickBatches`, `exportPurchaseOrders` y `exportOrders` (`features/warehouse/api.ts`).
  ```tsx
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const query = useMemo(() => ({ ...filtros, skip: (page - 1) * pageSize, take: pageSize }), [filtros, page, pageSize])
  const { data } = useQuery({ queryKey: ['/api/v1/audit/changes', query], queryFn: () => unwrap(api.GET('/api/v1/audit/changes', { params: { query } })), placeholderData: keepPreviousData })
  <DataTable columns={cols} rows={data?.items ?? NO_ROWS} rowKey={(r) => r.id!} page={page} pageSize={pageSize} total={data?.total ?? 0}
    onPage={setPage} onPageSize={(n) => { setPageSize(n); setPage(1) }}
    exportRows={() => fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/audit/changes', { params: { query: { ...query, skip, take } } })))} />
  ```

**Encabezado de las exportaciones y línea de filtros** (pedido del dueño del producto, 2026-09-30; `FilterScope.tsx`,
`filterScopeContext.ts`, `filterRegistry.ts`, `exportTable.ts`)
- Todo PDF de tabla (`DataTable`/`ListPager`, también el agrupado de `exportChildren`) y todo Excel llevan arriba, en este
  orden: **compañía activa** → título → "Generado el …" → **oración de filtros** (misma letra que la fecha; parte renglón
  si es larga). El Excel pone cada una en su fila (columna A), una fila en blanco y luego la tabla (autofiltro en la fila
  de encabezados; SheetJS comunidad no escribe negrita ni paneles inmovilizados). **El CSV no cambia** (solo la tabla).
- Nadie lo pasa a mano: el shell envuelve la pantalla en `<ExportCompanyProvider company={me?.tenantName}>` +
  `<FilterScope>`, y `DataTable`/`ListPager` leen `useExportHeading()` al exportar (van a `exportTable` como `company` y
  `filters`).
- La oración la arman los **controles de filtro del kit**, que se anotan solos en el `FilterScope` más cercano mientras
  están montados y SOLO con un valor distinto de vacío/"Todos": `SelectFilter` (etiqueta de la opción), `SearchSelect`
  (etiquetas elegidas; más de 4 → "A, B, C, D y 3 más"), `DateRangeFilter` ("del 01/09/2026 al 30/09/2026", "desde …",
  "hasta …"; en inglés mes/día), `QBox` ('Buscar "texto"'), y en el almacén `TextFilter`, `ToggleFilter` (encendido = solo
  su etiqueta), `ProductMultiFilter` (SKU), `BinMultiFilter`, `OwnerFilter`, `OrderNumberFilter`; `WarehousePicker` y
  `ClientPicker` solo con `filterLabel="Almacén"` (sin él —formularios— no se anotan). Un valor "Código · Nombre" se
  escribe "Código (Nombre)" (`filterItemText`): el " · " separa filtros. Orden = el del documento (el `.f` de cada control).
  Resultado: `Filtros: Almacén ALM-01 (Almacén principal) · Estatus Recibiendo · Creado del 01/09/2026 al 30/09/2026`.
- Barra sin nada elegido → **"Sin filtros"**; tabla sin controles de filtro en su ámbito o dentro de un `Modal` (que pone
  `<FilterScope off>`) → **sin línea**. `SearchMultiSelect` (sin etiqueta, de formularios) no se anota.
- Un filtro hecho a mano (sin esos controles) se anota con `useRegisterFilter(label, valorLegible | null, ref?, enabled?)`
  (`''` = solo la etiqueta; `ref` = su elemento, para el orden):
  ```tsx
  const ref = useRef<HTMLLabelElement>(null)
  useRegisterFilter(t('…includeSuspended'), includeSuspended ? '' : null, ref)
  <label className="sw" ref={ref}>…</label>
  ```
- `<FilterScope>` propio para una zona cuyas tablas NO dependen de la barra de la pantalla (detalle de un maestro-detalle,
  un panel con sus propios filtros): dentro, solo cuentan sus controles. `<FilterScope off>` = no se anota nada dentro
  (p. ej. un filtro que la pestaña no aplica, `.f-na` del Kárdex).
  ```tsx
  <div className="rcp-side"><FilterScope><ReceiptDetailPanel publicId={id} … /></FilterScope></div>
  ```
- Puras: `createFilterRegistry`, `joinFilterValues(values, t)`, `filterItemText`, `dateRangeFilterText(range, lang, t)`,
  `formatFilterDate`, `textFilterValue`, `filtersSentence(snapshot, t)` (`filterRegistry.ts`); `exportHeadingLines`,
  `exportGeneratedText`, `xlsxSheetRows`, `buildXlsxWorkbook(XLSX, data, heading)`, `renderTablePdf(data, heading)`,
  `drawPdfHeading(doc, heading, x, y, ancho)` (`exportTable.ts`). Textos en `ui.filters.applied.*`. Los reportes de marca
  (`reportPdf.ts`) siguen con su propio recuadro "Filtros aplicados".

## Almacén (`src/features/warehouse`, Lote F6)
No es núcleo, pero lo comparten todas las pantallas del almacén, de compras, del cruce de muelle y la consulta de órdenes.
- `api.ts`: un hook por lectura con clave `[ruta, params]` (`useWarehouses(query?)`, `useWarehouse(publicId)`,
  `useWarehouseZones/Bins/Docks(publicId, query?)`, `useProducts`, `useProduct`, `useProductInventoryKpis()` (KPIs de
  'Productos e inventario': `{ activeSkus, totalUnits, belowMin, serial, serialMissing }`, todos con `take=1`, ver abajo),
  `useProductBrands(search?)` (Lote 12: `GET /products/brands` → `string[]` de marcas del tenant; lo invalidan alta y edición de
  producto), `useProductLots/Serials`, `useProductCategories`,
  `useInventoryBalances`, `useInventoryTransactions`, `useInventoryReconciliation`, `useLotGenealogy`, `useSerialTrace`, `useAsns`,
  `useReceipts`, `useReceipt` (Lote 13: `useCreateReceipt`, `useUpdateReceiptHeader` —PATCH del encabezado—,
  `useSaveReceiptLine` y `useConfirmReceipt` dejan en caché la ficha que devuelve el API e invalidan la lista; Lote 16:
  `useReceiptTargetSuggestions(publicId, lineId, take = 3, options?)` —`GET /receipts/{id}/lines/{lineId}/target-suggestions`,
  `ReceiptTargetSuggestionDto[]` (las que caben primero; `fits=false` al final), caché 30 s, la invalidan el guardado de líneas,
  el encabezado y "usar sugeridas"— y `useApplyReceiptTargetSuggestions()` → `mutateAsync({ publicId, body: { rowVersion } })`
  → `{ receipt, assigned, withoutSuggestion }` con la ficha en caché), `useWarehouseTasks`, `usePutawaySuggestions`, `useCycleCounts`, `useCycleCount(id, query?)` (Lote 14: las escrituras de
  `useCycleCountAction` dejan en caché la ficha devuelta bajo `[…, { id }]`), `useCycleCountsPage(query)` (Lote 14:
  `GET /cycle-counts/page`, con `total`), `useChangesPreview(query)` y `useCreateCountsFromChanges()` ("lo cambiado"),
  `exportCycleCounts(query)`,
  `usePickBatches`, `usePickBatch`, `useSuppliers`, `usePurchaseOrders`, `usePurchaseOrder`, `usePurchaseOrderShortages`,
  `usePurchaseOrderShortageLines`, `useDockAppointments`, `useCrossDockPlans`, `useCrossDockPlan`, `useCrossDockCandidates`,
  `useOrdersReadonly`, `useOrderReadonly`, `useOrderLookup`). `query` es el tipo del esquema (`GetQuery<'/api/v1/…'>`); el último
  argumento `{ enabled?, handleAccessDenied? }`. Las listas paginadas usan `keepPreviousData`. Escrituras: `useCreateX`/`useUpdateX`
  o un hook de acciones con unión discriminada por `action` (`useSaveWarehouseZone`, `useWarehouseTaskAction`, `useCycleCountAction`,
  `usePurchaseOrderAction`, `useCrossDockAction`…); cada una invalida por prefijo su lista, su ficha y, si mueve inventario,
  saldos/Kárdex/existencias (`warehouseKeys` tiene los prefijos; Lote 14: también el resumen del Kárdex, los descuadres y el
  estado de la conciliación). Lote 14: `useKardexSummary(query)` (`GET /inventory/transactions/summary`, mismos filtros que la
  lista → `{ movements, inCount, inQty, outCount, outQty, internalCount }`), `useInventoryTransaction(id)` (detalle, 404
  'Movimiento no encontrado.'), `useInventoryOwners()` ("Propio" con `isOwn` + clientes dueños), `useBinSearch(query)`
  (posiciones entre almacenes), `useInventoryDiscrepancies(query)` (paginado, `openCount`), `useInventoryDiscrepancy(publicId)`,
  `useResolveDiscrepancy()` → `mutateAsync({ publicId, body: { action: 'REBUILD_BALANCE' | 'DISMISS', notes, rowVersion } })`,
  `useRunReconciliation()` (`POST /inventory/reconciliation/run`, `{ productPublicIds? }` ≤ 200), `useReconciliationStatus({
  refetchInterval? })` y `exportInventoryDiscrepancies(query)`. `warehouseLabel` ("Code · Name"), `binLabel` ("Código · Zona")
  y `productLabel` ("SKU · Nombre").
- `pickers.tsx`: todos son combobox como `ClientPicker` (↑/↓/Enter/Escape, ✕ para quitar, clic para reabrir) y se pueden llenar
  con un **lector de código de barras**: el código completo + Enter elige la opción cuyo código es exactamente ese (sin
  mayúsculas ni acentos), aunque otra esté resaltada. Lógica pura en `pickerMatch.ts` (`foldText`, `exactCodeMatch`,
  `filterWarehouses`, `orderBins`).
  - `WarehousePicker` (almacenes activos, `GET /api/v1/warehouses?includeInactive=false` sin `search`: filtra EN EL CLIENTE
    por código o nombre, subcadena; la coincidencia exacta de código va primero; `value` publicId, `onChange(publicId, dto)`,
    `placeholder?` —texto sin valor; `null` = no se ofrece quitar—; conserva con su etiqueta un valor inactivo pidiendo su ficha;
    `filterLabel?` —usado como filtro de una lista: se anota en la línea de filtros de las exportaciones con esa etiqueta—).
  - `ProductPicker` (`GET /api/v1/products?search=&activeOnly=true`, 250 ms entre teclas, "SKU · Nombre", marca el dueño
    cliente; `ownOnly?`, `ownerClientPublicId?`, `warehousePublicId?`, `onlyAvailable?`, `includeInactive?` (omite `activeOnly`
    y marca "Inactivo"); `onChange(publicId, fila)` con `trackingTypeCode`).
  - `BinPicker` (posiciones de un almacén: `GET /api/v1/warehouses/{publicId}/bins?search=&includeInactive=false&take=50`
    —listado paginado desde el Lote 1—, 250 ms entre teclas —el API compara código de posición, zona, pasillo, rack, nivel y
    posición—, "Código · Zona"). Props: `warehousePublicId` (vacío = deshabilitado con "Elija primero un almacén"; al
    cambiarlo quita el valor con `onChange(null, null)`), `value` (id de la posición, `number | null`), `onChange(binId, fila)`,
    `zoneTypeCodes?` (p. ej. `['STAGING','CROSSDOCK']`: pide las zonas del almacén y manda sus ids como `zoneIds`; ninguna zona
    de esos tipos = sin opciones), `excludeZoneTypeCodes?` (Lote 16: lo contrario —SIN las posiciones de zonas de esos tipos,
    p. ej. la posición destino de un recibo directo, `TARGET_EXCLUDED_ZONE_TYPES` = `['STAGING','CROSSDOCK']`—: manda como
    `zoneIds` las zonas de los DEMÁS tipos (las sin tipo cuentan como permitidas) y filtra también en el cliente; si el almacén
    no tiene zonas de esos tipos no manda `zoneIds`; se combina con `zoneTypeCodes`), `onlyWithStock?`, `suggestedBinIds?` (se piden aparte con `binIds` —mismo texto y filtros—
    y van primero con la marca "Sugerida"), `placeholder?` (p. ej. "Staging por defecto" cuando vacío = lo decide el
    servidor), `disabled?`, `invalid?`, `required?`, `aria-*`, `onBlur?`. Si el lector manda Enter antes de la pausa de
    250 ms, busca de inmediato y elige al llegar la respuesta (código exacto, o el único resultado). Un valor que no está en
    la lista (posición dada de baja, o fuera de la primera página) se muestra pidiéndolo por id (`binIds=<id>`,
    `includeInactive=true`, `take=1`).
    **Lista dada** (`options?: BinPickerOption[]` —`{ id, code?, zoneCode?, zoneTypeCode?, hint? }`—, `optionsLoading?`): cuando
    las posiciones válidas dependen de otra cosa que el API de posiciones no filtra (p. ej. **dónde tiene disponible un
    producto**), quien llama las arma de los saldos (`useInventoryBalances`) y el selector NO consulta el listado: filtra esa
    lista en el cliente por código o zona (Enter con el código exacto la elige al instante), la ofrece en ese orden (exacta y
    `suggestedBinIds` primero) con su pista ("PISO · PISO · 2 disp.", `ui.binPicker.available`) y **quita solo** un valor que
    ya no está en ella (`onChange(null, null)`: cambió el producto o el lote), salvo mientras `optionsLoading` ("Cargando…").
    `options` ausente = listado del API de siempre (p. ej. subir inventario o destino de una transferencia: cualquier posición).
    Sin el dato que la decide, deshabilítelo con `disabled` y `placeholder={t('ui.binPicker.pickProductFirst')}`.
    ```tsx
    const options = fefoBinOptions(stock, lot).map((b) => ({ id: b.binId, code: b.binCode, zoneCode: b.zoneCode, hint: t('ui.binPicker.available', { qty: formatNumber(b.qtyAvailable, lang) }) }))
    <BinPickerInput warehousePublicId={wh} options={options} optionsLoading={loading} suggestedBinIds={options.slice(0, 1).map((o) => o.id)}
      disabled={!productPublicId} placeholder={productPublicId ? t('warehouse.pickBatches.fefo') : t('ui.binPicker.pickProductFirst')} />
    ```
    Destino de un recibo directo (Lote 16), controlado y con las sugeridas del API primero:
    ```tsx
    <BinPicker warehousePublicId={header.warehousePublicId} value={row.targetBinId} onChange={(_id, bin) => state.pickTarget(row.key, bin)}
      excludeZoneTypeCodes={TARGET_EXCLUDED_ZONE_TYPES} suggestedBinIds={suggestions.map((s) => s.binId)} aria-label={t('…targetOf', { n })} />
    ```
    Listado paginado `{ total, skip, take, items }` (take por defecto 100, máx. 200): quien necesite TODAS las posiciones (el
    selector de posiciones del alta de un conteo) las lee con `fetchAllPages` y filtros de servidor (`zoneIds`); nunca asumas
    que una sola llamada trae el almacén completo.
  Dentro de un `Field`: `WarehousePickerInput` (valor publicId o null), `ProductPickerInput` (`onPicked?(fila)` para condicionar
  lote/series) y `BinPickerInput` (mismas props que `BinPicker` salvo value/onChange —también `excludeZoneTypeCodes`—, + `onPicked?(fila)`; el valor del
  formulario es el id **como texto** —`''` = ninguna—, igual que un `Select`: el request hace `Number(v.binId)`):
  ```tsx
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  <Field name="warehousePublicId" label={t('…warehouse')} required><WarehousePickerInput /></Field>
  <Field name="binId" label={t('…bin')} required><BinPickerInput warehousePublicId={warehousePublicId} /></Field>
  ```
  Sin acceso (403) muestran un aviso y no sacan de la pantalla. `ProductMultiFilter` (`label`, `value: ProductFilterItem[]`,
  `onChange`, `includeInactive?`): filtro "Producto" (multi-select buscable) de listas —Saldos, Kárdex, Recibos, Recolecciones— que
  busca en el API por SKU o nombre y pinta una píldora por producto elegido (recortada con elipsis: un SKU de 60 caracteres no
  desborda a 360 px). En listas de historial (Kárdex, Recibos, Recolecciones) va con `includeInactive` para poder filtrar
  productos dados de baja.
  Lote 14: `BinMultiFilter` (`label`, `value: BinFilterItem[]` —`{ id, label }` de `kardexView.ts`—, `onChange`,
  `warehousePublicIds?` —acota la búsqueda—, `includeInactive?`): filtro "Posición" que busca ENTRE almacenes
  (`GET /api/v1/warehouses/bins/search?search=&warehousePublicIds=&take=50`, 250 ms entre teclas, opciones "Código · Zona ·
  Almacén", marca "Inactiva"); una píldora por posición con ✕; Enter con el código exacto la elige aunque la búsqueda no haya
  llegado (lector de código de barras). `OwnerFilter` (`label`, `value: string[]`, `onChange`): "Dueño" (`SearchSelect` sobre
  `useInventoryOwners`: "Propio" = `OWN_OWNER` ('OWN'), los clientes por `clientPublicId`); la consulta la arma `ownerQuery`
  (`ownerClientPublicIds` + `includeOwn`).
  ```tsx
  <BinMultiFilter label={t('…bin')} value={bins} onChange={setBins} warehousePublicIds={warehouses} includeInactive />
  <OwnerFilter label={t('…owner')} value={owners} onChange={setOwners} />
  const q = { ...ownerQuery(owners), binIds: bins.map((b) => b.id) }
  ```
  `isAccessDenied(error)` (`accessDenied.ts`) para avisar junto a un campo cuando una consulta secundaria da 403.
  `problemText(err)` (`problemText.ts`, Lote 14): texto de un error del API para mostrarlo tal cual fuera de un `Form` (los
  mensajes por campo de un 400 o el título de un 404/409/422).
- Ubicaciones (`/warehouse/locations`, `LocationsScreen`; maqueta `ubicaciones()`): almacén arriba (`?warehouse=<publicId>`;
  sin él, el primero activo), "Asignar cupo" (`BinCapacityModal`) y "Nueva posición" (ambos `warehouse.manage`); río con un recuadro por zona (`.river`/`.node`/`.pipe` de
  `analytics/pulse.css` dentro de un contenedor `.pulse`) con lo ocupado vs. la capacidad de la zona (`capacityQty` y
  `qtyOnHandInCapacityBins` de `WarehouseZoneDto`: Σ cupo de sus posiciones y su existencia), "N posiciones sin cupo" si hay
  posiciones sin cupo y, si ninguna tiene cupo, la existencia total "sin cupo configurado" (nunca un % inventado). Cada
  recuadro es un botón (`.loc-node`, `aria-pressed`, activo `.on`) que filtra la tabla por su zona: `?zone=<id>` (lo lee
  también el filtro Zona; otro clic o "Limpiar" lo quita). Sin barras de 7 días (no hay historial diario). Filtros al
  servidor: Zona (`zoneIds`), Tipo (→ ids de las zonas de ese tipo), Producto (`ProductMultiFilter` → `productPublicIds`) y
  Estatus (`occupancy`: EMPTY/PARTIAL/FULL/NO_CAPACITY); tabla paginada en el servidor (Exportar = todo lo filtrado con
  `fetchAllPages`) con Posición, Zona, Cantidad, Producto (nombre si hay uno, "N productos" si varios), Cupo, Ocupación
  (barra con el % real si hay cupo) y Estatus (Vacía/Parcial/Llena/Sin cupo). Lógica pura en `locations.ts`
  (`zoneCapacities`, `binOccupancy`, `binFillPct`, `binProductCell`, `toggleZoneSelection`, `parseZoneParam`,
  `buildBinListQuery`).
- `BinModal` (`BinModal.tsx`): alta/edición de una posición, compartido por la ficha del almacén (pestaña Posiciones) y
  Ubicaciones. Props `publicId` (almacén), `zones` (las del almacén; el alta ofrece solo las activas, en un `ComboSelect` con
  buscador "Código · Nombre" y el tipo como `hint`: `zoneOptions` de `warehouseFilters.ts`), `bin` (`null` = alta), `open`,
  `onClose`; guarda con `useSaveWarehouseBin` (invalida posiciones, zonas y almacenes). "Cupo máximo" (`maxCapacityQty`,
  entero > 0, opcional; en edición, vaciarlo manda `clearMaxCapacity`). Muéstralo dentro de `<Can perm="warehouse.manage">`.
  `ReadOnlyField` (mismo archivo) = campo inmutable o derivado (código, zona, estado, país), con su `<label htmlFor>`.
  ```tsx
  <Can perm="warehouse.manage"><button className="btn flow" onClick={() => setOpen(true)}>{t('warehouse.bins.new')}</button></Can>
  <BinModal publicId={warehousePublicId} zones={zones} bin={null} open={open} onClose={() => setOpen(false)} />
  ```
- `BinCapacityModal` (`BinCapacityModal.tsx`, Lote 11): "Asignar cupo", cupo máximo EN BLOQUE (`POST /warehouses/{publicId}/bins/
  capacity`, `warehouse.manage`). Props `publicId` (almacén), `open`, `onClose`, `initial?: BinCapacityInitial` (`zoneIds`,
  `aisle`, `rack`, `level`, `position` ya elegidos en la pantalla; se leen al abrir y las zonas ajenas se descartan). Pide sus
  propias zonas activas. Alcance: Zona (`SearchSelect`), Pasillo/Rack/Nivel/Posición ("contiene"), "Solo posiciones sin cupo"
  (`onlyWithoutCapacity`; deshabilitada al quitar) y "Todo el almacén" (solo visible sin filtros → `allBins: true`); siempre
  solo activas. Acción por radio, exactamente una: "Cupo máximo" (entero > 0 → `maxCapacityQty`) o "Quitar cupo" (`clear`).
  Vista previa en vivo (300 ms) "Se aplicará a N posiciones" con `useBinCapacityPreview` (0 → aplicar deshabilitado); más de
  100 posiciones o "Todo el almacén" → `ConfirmDialog`. Al aplicar, toast "Cupo aplicado a {changed} de {matched} posiciones" y
  cierra; los errores del API quedan en el modal (título arriba, el de `maxCapacityQty` bajo el campo). Lo usan Posiciones
  (zona de `?zone=`) y la pestaña Posiciones de la ficha (Zona + textos de ubicación del filtro). Muéstralo dentro de `<Can>`:
  ```tsx
  <Can perm="warehouse.manage"><button className="btn" onClick={() => setOpen(true)}>{t('warehouse.binCapacity.open')}</button></Can>
  <BinCapacityModal publicId={warehousePublicId} open={open} onClose={() => setOpen(false)} initial={{ zoneIds: ['3'] }} />
  ```
  Hooks (`api.ts`): `useSetBinsCapacity()` → `mutateAsync({ publicId, body })` → `{ matched, changed }` (invalida posiciones,
  zonas y almacenes); `useBinCapacityPreview(publicId, scope, zones, zonesLoading)` → `{ count, exact, loading, error }`: `GET
  .../bins?take=1` con los mismos filtros (`total` = `matched`); como el GET no tiene `onlyWithoutCapacity`, con esa casilla y
  sin texto suma `binsWithoutCapacity` de las zonas, y con texto cuenta las posiciones sin cupo del listado si son ≤ 1000
  (si no, `exact: false` = el total es un tope, "como máximo N"). Lógica pura en `binCapacity.ts` (`capacityPreviewQuery`,
  `capacityRequestBody`, `scopeReady`, `sendsAllBins`, `parseCapacityQty`, `needsCapacityConfirmation`, `zonesWithoutCapacity`).
- `ZoneModal` (`ZoneModal.tsx`, Lote 1): alta/edición de una zona (`publicId`, `zone` —null = alta—, `open`, `onClose`), la
  comparten la lista de almacenes y la pestaña Zonas de la ficha. Código editable también al editar (obligatorio, formato de
  código; el 409 'Ya existe una zona con ese código en el almacén.' se pone bajo el campo), Nombre y Tipo (`ComboSelectInput`
  sobre `useLookups('ZoneType')`; en edición, quitarlo manda `zoneType: ''` = sin tipo).
  `<ZoneModal publicId={w.publicId} zone={editing} open={editing !== null} onClose={() => setEditing(null)} />`
- `PostalLocalityPicker` (`PostalLocalityPicker.tsx`, Lote 1): Ciudad y código postal del almacén en UN combobox sobre
  `usePostalLocalities(search)` (`GET /api/v1/postal-localities?search=&take=30`: catálogo USPS de EE. UU. y PR —PR primero—
  por ciudad postal, municipio o prefijo de ZIP; 250 ms entre teclas; 403 = aviso sin sacar de la pantalla); opciones
  `localityOptionLabel` ("00952 · SABANA SECA (Toa Baja), PR": municipio solo si difiere de la ciudad postal; fuera de PR
  " · País": "10001 · NEW YORK, NY · Estados Unidos"); Enter con un ZIP completo lo elige. El campo muestra "Ciudad · ZIP" con
  lo guardado (aunque no venga del catálogo). Props `city`, `postalCode`, `onChange(localidad | null)`, `placeholder?`,
  `disabled?`, `invalid?`, `aria-*`. En un formulario: `<Field name="city">` + `PostalLocalityPickerInput` (`fields?` = nombres
  de `postalCode`/`state`/`country` si no son esos): al elegir pone Ciudad = `localityCity` (el municipio con acentos, o la
  ciudad postal) y llena ZIP, Estado y País (`countryCode` del lookup `Country`) con `setValue`; ✕ vacía ciudad, ZIP y
  estado. `DerivedLocalityFields` pinta Estado y País (etiqueta del catálogo) de solo lectura.
  ```tsx
  <Field name="city" label={t('warehouse.postalPicker.label')}><PostalLocalityPickerInput /></Field>
  <DerivedLocalityFields />
  ```
- Almacenes (`/warehouse/warehouses`, `WarehouseListScreen`; maqueta `almacenes()`, Lote 1): filtros arriba en el cliente
  (Código, Nombre, Tipo de zona —`zoneTypeCodes`—, Estatus como `SearchSelect`; Dirección como texto sobre dirección, ciudad,
  estado y ZIP: `filterWarehouseRows` en `warehouseFilters.ts`), sin `QBox`; maestro-detalle `.whs-cols` (`warehouse.css`:
  tabla + panel de 300–420 px; una columna bajo 720 px): tabla Código, Nombre, Dirección, Zonas, Estatus y el elegido
  (`?warehouse=<publicId>`, por defecto el primero por código; fila `.sel`) con dirección, estatus, lápiz (abre la ficha) y
  "Zonas de este almacén" (fila `.unrow` por zona activa: el cuerpo abre `ZoneModal`; papelera = baja, con `aria-disabled` y
  el motivo en el tooltip si la zona tiene posiciones). Alta con `PostalLocalityPickerInput` y la casilla "Activo" informativa.
- Ficha del almacén (`WarehouseDetailScreen`, Lote 1): Datos con `PostalLocalityPickerInput` + `DerivedLocalityFields` y (Lote 16)
  la sección "Recepción" (`<fieldset>` con leyenda): "Modo de recepción" (`Select` con `useReceivingModeOptions()`) y "Posición de
  recepción por defecto" (D12: `BinPickerInput` con `zoneTypeCodes={RECEIVING_ZONE_TYPES}`; vaciarla manda
  `clearDefaultReceivingBin`); el PATCH lleva solo lo que cambió (`warehouseReceivingPatch`). Cambiar el modo abre un
  `ConfirmDialog` "¿Cambiar el modo de recepción?" con los conteos del almacén (`useReceipts({ warehousePublicId, phase: 'OPEN' |
  'PENDING_PUTAWAY', take: 1 })` → `total`, pedidos solo con el diálogo abierto) y guarda al confirmar. La lista de almacenes
  tiene la columna "Recepción" (chip con la etiqueta del modo) y el alta el "Modo de recepción" (por defecto Con acomodo); Zonas con
  filtros Código/Nombre/Tipo en el cliente + "Incluir inactivas", clic en la fila = `ZoneModal`, baja/reactivación como íconos
  (`IconPower`/`IconRotateCcw`); Posiciones paginada en el servidor (`binsQuery` de `warehouseFilters.ts`: Código → `search`,
  Zona → `zoneIds`, Pasillo/Rack/Nivel/Posición con 300 ms de pausa, "Incluir inactivas", "Solo con existencia"; Exportar =
  `exportWarehouseBins(publicId, query)`), columnas Cupo y Ocupación, clic en la fila = `BinModal`, "Asignar cupo"
  (`BinCapacityModal` con la Zona y los textos de ubicación del filtro). Controles sueltos de
  filtro en `filterControls.tsx`: `TextFilter` (`label`, `value`, `onChange`, `placeholder?`, `maxLength?`, `type?:
  'search' | 'text'`) y `ToggleFilter` (`label`, `checked`, `onChange`); ambos se anotan en la línea de filtros de las
  exportaciones (ver "Encabezado de las exportaciones y línea de filtros").
- `ProductEditorModal` (`ProductEditorModal.tsx`, maqueta `renderProductModalHtml`): el ÚNICO alta/edición de producto (no hay
  pestaña "Datos"). Props `open`, `product: ProductDetailDto | null` (`null` = "Nuevo producto"), `onClose`, `onCreated?(publicId)`.
  Campos en el orden de la maqueta (SKU —bloqueado al editar— · Unidad, Nombre, Marca · Modelo (Lote 12), Categoría · Rastreo, Dueño
  del inventario, Costo de compra · Precio de venta, Almacén · Posición por defecto, Total —solo lectura, "usa Ajustar abajo"— · Punto
  de reorden). Unidad, Categoría (etiqueta = ruta "Raíz / Hija") y Rastreo son `ComboSelectInput`; Marca es texto libre con
  sugerencias (`<datalist>` de `useProductBrands`) y Modelo texto libre (máx. 100 cada uno; en PATCH solo se mandan si cambiaron: `''`
  = quitar). El Total sale de `useProduct` (se refresca solo tras un ajuste). Al editar, interruptor "Producto activo"
  (deactivate/reactivate al guardar; bloqueado con saldo en mano, con la nota de la maqueta), bloque "Ajustar inventario"
  (`inventory.adjust`; Lote 12: oculto tras "Añadir ajuste"; Lote 14: las mismas piezas del modal de ajuste —`useAdjustmentForm`
  con `fixedProduct` + `AdjustmentFields`: Subir/Bajar, cantidad positiva y motivos según la dirección—; al abrirlo Cantidad, Motivo con buscador sin los reservados al sistema,
  Almacén · Posición —por defecto los del producto— y Nota obligatoria que va en `notes`; "Aplicar ajuste" con el producto fijo
  refresca el Total y vuelve a ocultar el bloque limpio; un 409 `insufficient_stock` queda en el aviso del propio bloque, que sigue
  abierto; "Cancelar ajuste" lo cierra) y "Ver lotes"/"Ver series" (según el rastreo) → `/warehouse/products/{publicId}?tab=lots|serials`.
  Código de barras, peso, volumen, mínimo/máximo de picking y campos personalizados van plegados en "Más datos del producto" (se
  abre solo si alguno trae error). Sin `inventory.manage` es de solo lectura ("Ver datos del producto", botón "Cerrar").
  `ProductEditorByIdModal` (`publicId: string | null`, `onClose`) pide la ficha y abre el mismo modal (clic en una fila de Productos).
  La ruta `/warehouse/products/:publicId` es la vista de solo lectura de Lotes/Series: sin `?tab=` abre el modal al entrar.
  ```tsx
  <Can perm="inventory.manage"><button className="btn flow" onClick={() => setCreating(true)}>{t('warehouse.products.new')}</button></Can>
  <ProductEditorModal open={creating} product={null} onClose={() => setCreating(false)} />
  <ProductEditorByIdModal publicId={editingPublicId} onClose={() => setEditingPublicId(null)} />
  ```
- `ResolveShortageModal` (`ResolveShortageModal.tsx`): resolver el faltante de una línea de compra (CLOSE, REORDER,
  MANUAL_ADJUSTMENT) con `useResolveShortage`; lo usa la pestaña Faltantes de la ficha de la orden (Lote 14: el único lugar
  donde se resuelven los faltantes). Props `open`, `onClose`, `po` (mínimo `{ publicId, warehousePublicId, rowVersion? }`: lo cumplen
  `PurchaseOrderDto` y `PoShortageSummaryDto`), `line: ShortageLineDto | null`, `initialAction?` (por defecto CLOSE),
  `initial?: { quantity?, notes? }` (precarga desde la fila), `onResolved?(result)` (con él, quien lo abre pone su aviso; sin
  él, 'Faltante resuelto.'). Pinta arriba "SKU · Producto — Faltante: N". REORDER solo con `purchasing.manage`;
  MANUAL_ADJUSTMENT solo con WMS_LOTSERIAL (posición con `BinPickerInput` del almacén de la orden, lote o series según el
  producto, que se consulta solo en esa acción). Cerrar/Reordenar mandan `quantity` = el pendiente que se ve: si cambió en el
  servidor, el 400 ('Cerrar y Reordenar resuelven el faltante completo (N)…') sale en el aviso de arriba del `Form`. Notas:
  obligatorias solo en MANUAL_ADJUSTMENT (`adjustNotesSchema`, asterisco solo con esa acción), opcionales en Cerrar/Reordenar
  (máx. 300 en todas); el 400 del API en `errors.notes` queda bajo el campo.
  ```tsx
  <ResolveShortageModal open onClose={() => setResolving(null)} po={summary} line={line} initialAction="MANUAL_ADJUSTMENT"
    initial={{ quantity: 1, notes: 'Apareció en muelle' }} onResolved={(r) => toast.success(…)} />
  ```
- Ajuste y transferencia de inventario (Lote 14, D11 y hallazgo 3). Los usan 'Transferencias y ajustes', el Kárdex y (el
  ajuste) la ficha del producto:
  | Pieza | Props / firma | Uso |
  |---|---|---|
  | `InventoryAdjustModal` | `open`, `onClose`, `initial?: AdjustInitial` (`{ productPublicId?, warehousePublicId?, binId?, direction?: 'up' \| 'down' }`) | "Ajuste de inventario": **Subir / Bajar** (`AdjustDirectionInput`, radios `.seg.adj-dir`; obligatorio: 'Elija si el ajuste sube o baja el inventario.') + cantidad POSITIVA ('La cantidad debe ser mayor que cero.'); la pantalla pone el signo (el API sigue recibiendo `quantity` con signo). Motivo con buscador (`ComboSelectInput`) según la dirección (`reasonsForDirection`: Encontrado solo al subir; Daño, Pérdida y Vencido solo al bajar; nunca los del sistema); al cambiar de dirección se quita un motivo que ya no vale. Pista "Disponible en la posición: N" y, al bajar, 'No puede bajar más de lo disponible en la posición ({qty}).'. LOT: al subir, número de lote; al bajar, lote con saldo en la posición (`lotId`). SERIAL: al subir, series nuevas; al bajar, series disponibles en la posición (`SerialsPickInput`); la cantidad es el número de series. Nota obligatoria (`adjustNotesSchema`). Se monta limpio al abrir. |
  | `useAdjustmentForm({ initial?, fixedProduct? })` + `<AdjustmentFields state />` (`adjustmentForm.ts`, `InventoryAdjustModal.tsx`) | `state = { form, tracking, direction, available, balances, serials, picked… }`; `useSubmitAdjustment()` → `(values, tracking) => cantidad con signo` | los campos del ajuste dentro de cualquier `<Form form={state.form}>`; con `fixedProduct` no se elige producto (bloque "Ajustar inventario" de `ProductEditorModal`). Posición: al **Subir**, cualquiera del almacén; al **Bajar**, solo donde el producto tiene disponible (`state.downBins`/`downBinsLoading`: saldos del almacén con `onlyAvailable`, `downBinOptions` por código, "A-01 · A · 5 disp."), deshabilitada sin producto ("Elija primero un producto"); una posición que deja de aplicar al cambiar producto, almacén o dirección se quita sola; si los saldos no se pueden leer se ofrecen todas (el servidor valida). El esquema (`adjustFormSchema(t, lang, { tracking, direction, available })`) se arma con lo que avisan los controles (`onPicked`). |
  | `InventoryTransferModal` | `open`, `onClose` | "Transferencia de inventario" en el orden **origen → ítem → destino**: almacén y posición de origen (`onlyWithStock`) → ítem (`ComboSelect` sobre los saldos disponibles de esa posición: producto + lote, "disponible N") → series si es SERIAL (las disponibles en la posición; cantidad = número de series) → almacén de destino (arranca en el de origen) y posición → cantidad (tope: lo disponible del ítem, 'No puede transferir más de lo disponible en la posición ({qty}).') → notas. Manda `lotId` y `serialNumbers`. |
  Lógica pura: `adjustmentReasons.ts` (`reasonsForDirection`, `reasonAllowed`, `signedAdjustQuantity`, `UP_ONLY_REASONS`,
  `DOWN_ONLY_REASONS`), `movementForms.ts` (`adjustmentBody`, `availableAt`, `binStockOptions(balances)` —una fila por posición con disponible > 0, sumando lotes, en el orden recibido—, `downBinOptions`, `lotOptions`, `transferItemOptions`,
  `transferItemKey`/`parseTransferItemKey`, `serialsAt`) y `productRules.ts` (`adjustDirectionSchema`, `adjustMagnitudeSchema`).
  ```tsx
  <Can perm="inventory.adjust"><button className="btn" onClick={() => setAdjusting(true)}>{t('…adjust')}</button></Can>
  <InventoryAdjustModal open={adjusting} onClose={() => setAdjusting(false)} initial={{ warehousePublicId, binId }} />
  <InventoryTransferModal open={transferring} onClose={() => setTransferring(false)} />
  ```
- `KardexTransactionModal` (Lote 14; `txnId: number | null`, `onClose`): detalle de SOLO LECTURA de un movimiento
  (`GET /inventory/transactions/{id}`): fecha y hora, usuario ("Sistema" sin usuario), tipo en chip de color, producto, dueño,
  categoría, cantidad con signo y color (una transferencia interna, sin signo), de → a, lote (vence), serie, motivo, origen y
  nota; "Documento de origen" (número, estatus, fecha, parte, referencia; la tarea de almacén con su documento padre) con
  "Abrir" solo si el usuario tiene el permiso y el módulo de esa pantalla (`documentLink`: recibo `?receipt=`, recolección,
  conteo `?count=`, compra, orden, producto, plan de cruce); "Movimientos relacionados" (mismo documento o mismo asiento, tope
  200, `exportable={false}`, el propio resaltado). El 404 'Movimiento no encontrado.' se muestra tal cual.
  `<KardexTransactionModal txnId={txnId} onClose={() => setTxnId(null)} />`
- `kardexView.ts` (Lote 14, puro): `InventoryFilterState` (filtros compartidos del Kárdex) con `EMPTY_INVENTORY_FILTERS`,
  `kardexQuery`/`summaryQuery`/`balancesQuery`/`discrepancyQuery`, `TAB_FILTERS`/`filterApplies`/`inactiveFilters`,
  `filtersFromUrl`, `tabFromParam`, `txnParam`, `ownerQuery`, `rangeInverted`; y cómo se pinta un movimiento: `txnTypeTone`
  (RECEIPT `deliv`, ISSUE `route`, TRANSFER `disp`, ADJUSTMENT `fail`, CROSSDOCK `cod`), `movementQtyView`, `formatSignedQty`,
  `qtyClass`, `splitDateTime` (fecha y hora por separado), `lotSerialText`, `fromToText`, `movementOrigin` (manual, conteo,
  acomodo o reabasto…), `documentLink`, `binFilterLabel`, `summaryItems` (cifras de `SummaryBar`). `kardexColumns.tsx`:
  `useKardexColumns(ids)` con las columnas de un Kárdex (`date`, `time`, `type`, `sku`, `product`, `owner`, `category`,
  `qty`, `amount`, `position`, `from`, `to`, `lotSerial`, `reason`, `notes`, `origin`, `ref`, `user`; `KARDEX_COLUMNS` = las
  del Kárdex). `const columns = useKardexColumns(['date', 'time', 'type', 'sku', 'qty', 'position'])`
- Transferencias y ajustes (`/warehouse/transfers-adjustments`, `TransfersAdjustmentsScreen`; Lote 14; inventory.view +
  WMS_LOTSERIAL; en el menú después de Recolección y empaque): pestañas Ajustes (sin parámetro) y Transferencias
  (`?tab=transfers`), cada una con sus propios filtros (se conservan al cambiar de pestaña; lógica en `movementFilters.ts`:
  `adjustmentsQuery`, `transfersQuery`, `describeMovementFilters`) y la lista paginada en el servidor del Kárdex con TODOS los
  movimientos de su tipo, también los del sistema (D12), con "Solo manuales" (`manualOnly`). Ajustes: Fecha, Almacén, Producto,
  Dueño, Tipo de ajuste (Subir/Bajar → `direction` IN/OUT), Motivo; columnas Fecha, Hora, SKU, Producto, Dueño,
  Almacén/Posición, Lote/Serie, Cantidad ±, Motivo, Nota, Origen del movimiento y Usuario. Transferencias: Fecha, Almacén de
  origen/destino (`fromWarehousePublicIds`/`toWarehousePublicIds`), Producto, Dueño; columnas con Origen y Destino. Resumen
  (`SummaryBar`) con los mismos filtros; "Reporte de ajustes" con los filtros de la pestaña Ajustes; "Ajustar" y "Transferir"
  (`inventory.adjust`); clic en una fila = `KardexTransactionModal`. Sin `QBox`.
- Recibo (`/warehouse/receipts`, `ReceiptListScreen`; maqueta `recibo()`, Lote 13): maestro-detalle `.rcp-cols`. Estatus
  (colores del catálogo con `StatusChip`): Esperado → Recibiendo ⇄ Discrepancia → Completado / Completado con diferencia →
  Acomodado; los cambia el servidor (`isOpen` = los tres primeros). Pestañas `?tab=asns|putaway`; el recibo elegido va en
  `?receipt=<publicId>` (sin él, el primero; al cambiar de pestaña se quita; `/warehouse/receipts/:publicId` redirige aquí).
  | Pieza | Props / firma | Uso |
  |---|---|---|
  | `ReceiptFilterBar` (`ReceiptFilterBar.tsx`) | `value: ReceiptFilterState`, `onChange`, `statusCodes?` | Almacén (uno: `WarehousePicker`), Estatus y Tipo (`SearchSelect`), Creado, Producto (`ProductMultiFilter` con inactivos) y Diferencia (Faltante/Sobrante/Sin diferencia → `variance[]`); todo al API. No se llama `ReceiptFilters.tsx`: en Windows se confundiría con `receiptFilters.ts` |
  | `ReceiptMasterList` | `title`, `items`, `total`, `selectedId`, `onSelect(publicId)`, `onOpen(publicId)` (doble clic), `q`/`onQ` (`QBox` → `search`), `page`/`pageSize`/`onPage`/`onPageSize`, `exportRows` | filas `.unrow` (número + `StatusChip`; remitente o tipo; transporte · llegada esperada o alta · origen · documento · referencia), `ListPager` con Exportar (con `exportChildren`: cada recibo con sus líneas —`receiptExport.ts`, `exportReceiptsWithLines(query)`—; Excel/CSV una fila por línea, PDF un bloque por recibo con banda y líneas; sin columna "Líneas"; la usan Recibos y 'Acomodo pendiente') |
  | `ReceiptDetailPanel` | `publicId`, `onEditHeader()` | "Detalle del recibo · REC-…", "Origen · …", lápiz (modal del encabezado) e Historial; rejilla de líneas, notas y "Confirmar recibo" (deshabilitado con el motivo debajo); confirmado, `ReceiptPutawayTasks`. Lote 16 (modo del encabezado `receivingModeCode` = DIRECT): chip "Directo a posición" junto al estatus, "Usar posiciones sugeridas" (`warehouse.receive`, abierto y con líneas sin destino; toast "Se asignó posición a {n} línea(s); {m} sin sugerencia.", errores del API tal cual) y el motivo 'noTarget' "Falta la posición destino en {n} línea(s)." |
  | `ReceiptLinesEditor` + `useReceiptLineRows(receipt, { manual, editable, direct? })` | `receipt`, `state` (Lote 16: `state.pickTarget(key, bin \| null)` pone el destino y lo guarda al elegir; `state.mode.direct`) | Lote 16, `direct`: columna "Posición destino" (`BinPicker` controlado con `excludeZoneTypeCodes`, las sugeridas de `useReceiptTargetSuggestions` primero —solo se piden para líneas guardadas que reciben algo—, pista "Sugerida: {bin} · {motivo}" si la elegida no es esa y el aviso naranja `.rcp-over` "Excede el cupo de {bin}: caben {free}" con `targetFreeQty`; errores `targetBinId`/`targetBinCode` bajo el selector). Además: con documento lo esperado es de solo lectura y no hay fila para añadir; sin documento producto/esperado/recibido, papelera y siempre una fila vacía al final; guarda por fila (salir del campo, Enter o elegir producto) en fila única; `pagination={false}` y `exportable={false}` (rejilla de captura: excepción a la regla de Exportar); tarjetas si el panel mide < 600 px. Sus columnas no dependen de las filas (las celdas leen un contexto): una columna nueva por tecla volvería a montar el campo y perdería el foco |
  | `ReceiptHeaderModal` | `publicId` (null = alta), `preset?` (aviso), `onClose`, `onCreated?(dto)`, `onDeleted?(publicId)` | alta, edición (`PATCH`, solo lo que cambió, `rowVersion` de la caché) y "Borrar recibo" (`canDelete`); confirmado o sin `warehouse.receive`: solo lectura. Lote 16: "Modo de recepción" (alta: sigue al del almacén elegido mientras no se toque; '' = "El del almacén"; edición: el del recibo, `receivingMode` en el PATCH solo si cambió); en directo se oculta "Posición de recepción" |
  | `ReceiptLineCaptureModal` | `receipt`, `line`, `onClose`, `onSaved?` | lote (fabricación/vencimiento), series (SERIAL: lo recibido = número de series) y posición de la línea (Lote 16: oculta en un recibo directo) |
  | `ReceiptPutawayTasks` | `tasks`, `title?`, `queueLink?` | tareas PUTAWAY con `useTaskRowActions` |
  | `AsnsTab` / `AsnCreateModal` | `onReceive(asn)` / `onClose` | filtros Almacén, Cliente dueño, Referencia (300 ms) y Llegada esperada al API, sin `QBox`; alta con las líneas en una rejilla |
  | `PutawayPendingTab` | `selectedParam`, `onSelect`, `onOpenHeader` | recibos con `phase=PENDING_PUTAWAY` (Estatus limitado a Completado y Completado con diferencia) y sus tareas a la derecha; Lote 16: con un almacén DIRECTO en el filtro, aviso "{code} recibe directo a posición: aquí solo aparecen recibos anteriores al cambio o con cruce de muelle." |
  Lógica pura: `receiptFilters.ts` (`receiptListQuery`, `withPinned` —el recibo recién creado primero aunque los filtros lo
  excluyan, hasta cambiar un filtro—, `selectedReceiptId`, `hasDocument`, `receiptOriginText`) y `receiptLineEdit.ts` (filas
  como texto; `onReceivedInput` copia al esperado mientras se teclea si estaba vacío o en 0 y el recibo no tiene documento;
  `linePayload`, `reconcileRows`, `mergeSaved`, `confirmBlockers`, `rowErrorsFromProblem`; Lote 16: `onTargetPicked`,
  `rowNeedsTarget`, `missingTargets`, `confirmBlockers(rows, isOpen, manual, direct)` → 'noTarget').
  Lote 16, `receivingMode.ts` (puro, espejo de `ReceivingModeRules.cs`): `RECEIVING_MODE_DOMAIN`, `RECEIVING_MODES`,
  `TARGET_EXCLUDED_ZONE_TYPES`, `RECEIVING_ZONE_TYPES`, `normalizeReceivingMode`, `isDirectMode`, `effectiveReceivingMode`,
  `receivingModeLabel`, `inSentence`, `receivingModeChanged`, `warehouseReceivingPatch`, `exceedsCapacity`, `needsTarget`
  (recibido > 0, salvo LOT sin lote o con cruce de muelle), `topSuggestion`. `useReceivingModeOptions()`
  (`useReceivingModeOptions.ts`): las dos opciones SIEMPRE, con la etiqueta del catálogo o la de `warehouse.receivingModes.*`.
  En la lista maestra, los recibos directos llevan la etiqueta "Directo".
  ```tsx
  const lines = useReceiptLineRows(receipt, { manual: !hasDocument(receipt.header?.origin), editable: isOpen && canReceive })
  <ReceiptLinesEditor receipt={receipt} state={lines} />
  <button className="btn flow block" disabled={confirmBlockers(lines.rows, isOpen, manual) !== null}>…</button>
  // Lote 16: recibo directo a posición
  const direct = isDirectMode(receipt.header?.receivingModeCode)
  const lines = useReceiptLineRows(receipt, { manual, editable, direct })
  t(`warehouse.receipts.detail.blockers.${confirmBlockers(lines.rows, isOpen, manual, direct)}`, { n: missingTargets(lines.rows) })
  ```
- Conteo cíclico (`/warehouse/cycle-counts`, `CycleCountListScreen`; maqueta `conteo()`, Lote 14 P8, D2-D4 y D7-D10): filtros
  arriba y DOS paneles en `SplitPane storageKey="cycle-counts"` (34/66; bajo 900 px apilados). El conteo elegido va en
  `?count=<id>` (sin él, el primero de la lista). Filtros iniciales de la URL, leídos una vez al montar (Lote 15,
  `countFiltersFromUrl`): `warehousePublicIds`, `status`, `origins` (repetibles o con comas) y `from`/`to` (alta, 'YYYY-MM-DD');
  la franja "Almacén hoy" del Pulso manda `?status=RECONCILED_VARIANCE&warehousePublicIds=`. Estatus (colores del catálogo `CycleCountStatus`): Pendiente → Contado
  (solo a ciegas, desde la app) → Concordancia / Diferencia. "Conteo de lo cambiado" y "Nuevo conteo" con `warehouse.count`.
  | Pieza | Props / firma | Uso |
  |---|---|---|
  | `CycleCountFilterBar` | `value: CountFilterState`, `onChange`, `q`, `onQ` | Almacén, Zona (de los almacenes elegidos), Posición (`BinMultiFilter`), Producto, Estatus, Origen, Creado (días locales) y Buscar; todo al API |
  | `CountTaskList` | `items`, `total`, `selectedId`, `onSelect(id)`, `onDeleted?(id)`, `page`/`pageSize`/`onPage`/`onPageSize`, `exportRows` | filas `.unrow` (posición o "N posiciones" + `StatusChip`; "CC-… · Zona · N líneas · fecha"; origen y asignado) con íconos Asignar (`warehouse.manage`, abiertos, `taskId`) y Eliminar (Pendiente, `warehouse.count`, `ConfirmDialog`); sin `QBox`; `ListPager` con Exportar |
  | `CountDetailPanel` | `id: number \| null` | el conteo elegido: cabecera (estatus, origen, ventana de lo cambiado, asignado), Historial, "Refrescar foto", "Agregar lo encontrado", `CountScanBox`, rejilla con TODAS las líneas (Esperado, Contado editable en la fila, Varianza, Ajustado al cerrar) y "Confirmar conteo y ajustar" (un paso, `POST /reconcile`; deshabilitado con 'Faltan {n} línea(s) por contar.'). A ciegas: solo lectura |
  | `CountScanBox` | `lines`, `onPick(match)`, `disabled?` | combobox sobre las líneas; Enter con SKU, código de barras, lote o serie exactos (`matchCountLine`) elige la línea; varias → se elige; ninguna → 'Ese código no está en este conteo…' |
  | `CountQtyModal` / `AddFoundLineModal` (`CountLineModals.tsx`) | `line`, `scannedSerial?`, `isBlind?`, `onSave(body)`, `onClose` / `detail`, `onClose` | cantidad (foco puesto, Enter guarda) o series de una línea; línea nueva (lo encontrado) |
  | `CreateCountModal` / `ChangedCountModal` | `onClose`, `onCreated?` / `onClose`, `initialWarehousePublicId?`, `onCreated?(counts)` | alta manual (almacén, zonas, posiciones) / "lo cambiado": almacén (de solo lectura si hay uno), Desde/Hasta en hora de la compañía (por defecto la ventana del servidor; tocadas se mandan en UTC), zonas, "Incluir posiciones vacías", vista previa en vivo y su `problem` tal cual (sin crear) |
  | `useCountDrafts(detail)` | → `{ drafts, errors, saving, input, save, capture, flush, rowVersion }` | captura en la fila: texto por línea, guardado al salir o con Enter en FILA ÚNICA con el rowVersion más reciente; `flush()` antes de confirmar |
  Lógica pura en `countView.ts`: `CountFilterState`/`EMPTY_COUNT_FILTERS`, `countFilterQuery`, `countListQuery`, `countParam`, `countFiltersFromUrl`,
  `selectedCountId`, `countWhere`, `isCountClosed`/`isCountEditable`, `matchCountLine`, `scanOptions`, `lineVariance`,
  `pendingLines`, `confirmBlocker`. La hora de la compañía (`tenantTimeZone()`, `zonedInputFromUtc`, `utcFromZonedInput`) vive
  desde el Lote 15 en `src/kernel/api/tenantZone.ts` (ver "Hora de la compañía"). Estilos `.cc-*` en `warehouse.css`.
  ```tsx
  <SplitPane storageKey="cycle-counts" defaultRatio={0.34} minPx={[300, 480]}>
    <CountTaskList items={items} total={total} selectedId={id} onSelect={select} … /><CountDetailPanel id={id} />
  </SplitPane>
  ```
  **Lote F12 — conteo por producto: revisión rápida y corrección del supervisor** (contrato del servidor en `docs/lote21-decisiones.md`).
  Pestañas `Conteos` (sin parámetro) y `Por revisar` (`?tab=review`, solo con `warehouse.count`: sin él no se pinta y `?tab=review`
  abre la lista de siempre; `countTabFromParam(raw, canReview)`). Al cambiar de pestaña se quita `?count=`.
  | Pieza | Props / firma | Uso |
  |---|---|---|
  | `CountReviewTab` (`CountReviewTab.tsx`) | `countId` (`?count=`; null = el primero de la página), `onSelect(id)` | la pestaña completa: `SplitPane storageKey="cycle-count-review"` (50/50) con la lista "Por revisar" (`GET /cycle-counts/review`, paginada en el servidor, orden local de la página; columnas Elegir, Conteo, Contó, Producto "y N más", Posiciones, Líneas, Con diferencia, Correcciones, Estado; tarjetas con el panel < 720 px; filtros Almacén (uno), Contó y `QBox` → `search`) y `CountDetailPanel` a la derecha. "Cerrar los que cuadran": los elegidos que cuadran o, sin elegir, todos los que cuadran de la página → `POST /cycle-counts/reconcile-matching { ids, comment }` con confirmación (cuántos y cuáles, comentario ≤ 500) y un modal de resultado (cerrados; omitidos con su motivo en español y botón para abrirlos) |
  | `ReconcilePreviewModal` (`ReconcilePreviewModal.tsx`) | `count: { id, number? }`, `onClose`, `onConfirmed?(dto)` | "Confirmar conteo y ajustar" pasa por aquí: `GET /cycle-counts/{id}/reconcile-preview` → `SummaryBar` (Líneas, Por contar, Con diferencia, Movimientos, Con error), avisos (faltan líneas, Concordancia, saldo movido, `blockingError`) y tabla por posición (Posición con chip provisional, Producto, Lote, Existencia actual, Reservado, Contada con chip "Corrección", Ajuste ± con `qty-in/qty-out`, Saldo resultante, Error). Con algo que bloquea (`previewBlocker`), Confirmar se deshabilita con el motivo. Confirmar = `reconcile` con el `rowVersion` DE LA VISTA PREVIA (409 → recalcula ficha y vista previa) |
  | `CountDetailPanel` (cambios) | igual | con captura en la web pide la vista previa y agrega SIEMPRE las columnas Ajuste (contra la existencia actual, con el error de la línea) y Evidencia (*Contó X (quién, cuándo) · Corrección · Corregido a Y (quién, cuándo)*; en solo lectura solo si alguna línea la tiene). Un conteo Contado abre con **solo las líneas que fallan** (interruptor "Ver todas"); la línea tocada en la sesión se queda a la vista. Posición provisional: chip "Posición pendiente de revisión" y, con `warehouse.manage`, "Confirmar posición". Las columnas no dependen de datos que llegan después de montar: `FlexRender` volvería a montar las celdas y el campo perdería el foco |
  Hooks (`api.ts`): `useCycleCountReview(query)`, `exportCycleCountReview(query)`, `useReconcilePreview(id, { enabled })` (sin
  `keepPreviousData`; 403 sin sacar de la pantalla), `useReconcileMatching()` → `mutateAsync({ ids?, warehousePublicId?, comment?,
  includeOpen? })`, `useConfirmProvisionalBin()` → `mutateAsync({ publicId, binId })`. Toda escritura del conteo invalida también
  "Por revisar" y la vista previa (`warehouseKeys.cycleCountReview`, `warehouseKeys.reconcilePreview`).
  Lógica pura en `countReview.ts`: `reviewState(item)` (`errors` > `pending` > `difference` > `matches`; nunca "Cuadra" si el servidor
  no dijo `matches`), `REVIEW_STATE_TONE`/`REVIEW_STATE_ORDER`, `reviewProduct`, `ReviewFilters`/`reviewFilterQuery`/`reviewListQuery`,
  `counterOptions(seen, users)` + `rememberCounters` (opciones de "Contó": los vistos en las páginas y, con `admin.users`, los usuarios),
  `idsToClose(items, selected)`, `keepSelection`, `skipReasonText(skip, t)` / `bulkSummary(result, t)` (motivos `WouldPost`, `Errors`,
  `Pending`, `Stale`, `NotCounted`, `AlreadyReconciled`, `NotFound`, `NoLines`, `Failed`; desconocido → el `reason` del servidor),
  `previewLineFails`, `previewByLine`, `failingLines(lines, preview, variance, pinned)`, `lineEvidence(line)` + `evidenceText(ev, t, num,
  when)`, `previewBlocker(preview)`, `adjustmentClass(n)`, `signedQty(n, num)`, `BULK_COMMENT_MAX`.
  ```tsx
  {tab === 'review' ? <CountReviewTab countId={countId} onSelect={select} /> : <CountsTab … />}
  {confirming && <ReconcilePreviewModal count={{ id, number: count.number }} onClose={() => setConfirming(false)} />}
  const rows = showAll ? lines : failingLines(lines, previewByLine(preview.data), (l) => lineVariance(l), pinned)
  ```
  Posiciones provisionales en el listado del almacén: `binsQuery(text, zoneIds, includeInactive, onlyWithStock, onlyProvisional?)`
  (→ `isProvisional=true`); la pestaña Posiciones de la ficha tiene el chip, el filtro "Solo pendientes de revisión" y la acción de
  fila "Confirmar posición" (`warehouse.manage`); Ubicaciones, solo el chip.
- Tareas de almacén (sin pantalla ni ítem de menú: la maqueta no los tiene). Cada tipo vive en la pantalla de su flujo:
  PUTAWAY → Recibo: pestaña 'Acomodo pendiente' (`?tab=putaway`, Lote 13: lista de recibos con acomodo por cerrar y las
  tareas del elegido a la derecha) y 'Tareas de acomodo' debajo del detalle de un recibo confirmado (`ReceiptPutawayTasks`);
  REPLENISH → Recolección y empaque, pestaña 'Reabasto' (`?tab=replenish`, con 'Correr reabasto'); COUNT → Conteo cíclico
  (Lote 14, D10: sin pestaña propia; se asigna con el ícono de la lista —`AssignTaskModal` con `{ id: taskId,
  assignedToUserId }`— y se cierra al confirmar el conteo); CROSSDOCK → Cruce de muelle, pestaña 'Tareas de
  cruce' (solo con WMS_LOTSERIAL, 403 sin sacar de la pantalla). PICK/PACK/LOAD no tienen handler en el API (D41).
  | Pieza | Props / firma | Uso |
  |---|---|---|
  | `TaskQueue` (`taskQueue.tsx`) | `types: WarehouseTaskType[]`, `title`, `icon?`, `actions?` (cabecera del panel), `handleAccessDenied?` (false = 403 sin redirigir) | filtros (almacén, estatus, asignadas a mí, incluir cerradas) + `Panel` con la cola paginada en el servidor (`GET /warehouse-tasks?types=`); columna Tipo solo con más de un tipo; acciones de `useTaskRowActions` |
  | `ReplenishButton` (`taskQueue.tsx`) | `className?` (por defecto `btn flow`) | 'Correr reabasto' con su diálogo (almacén opcional); solo con `warehouse.pick` |
  | `useTaskRowActions()` (`taskActions.tsx`) | → `{ rowActions: RowAction<WarehouseTaskDto>[], dialogs }` | Asignar (`warehouse.manage`), Iniciar y Completar (permiso del handler: PUTAWAY `warehouse.receive`, REPLENISH `warehouse.pick`, COUNT `warehouse.count`, CROSSDOCK `warehouse.crossdock`; Completar solo con `completableFromQueue`, con `BinPicker` y las `suggestedBinIds` del acomodo primero) y Cancelar (PUTAWAY/REPLENISH, `warehouse.manage`); pinta `dialogs` una vez |
  | `AssignTaskModal`, `CompleteTaskModal` (`taskDialogs.tsx`) | `task`, `open`, `onClose` | los diálogos que abre `useTaskRowActions` |
  ```tsx
  <TaskQueue types={['REPLENISH']} title={t('warehouse.pickBatches.replenishTitle')} icon={<IconBasket />} />
  const { rowActions, dialogs } = useTaskRowActions()
  <DataTable columns={cols} rows={receipt.putawayTasks ?? []} rowKey={(r) => r.id ?? 0} rowActions={rowActions} />{dialogs}
  ```
  `useWarehouseTaskAction` invalida la cola, la ficha del recibo y, al completar, recibos, planes de cruce y el inventario.
- Productos e inventario (`/warehouse/products`, `ProductListScreen`; maqueta `inventario()`, Fase 8, Lote 12): un solo ítem de
  menú. Cabecera con "Reporte de inventario" y "Reporte de ajustes" (PDF, ver abajo) y "Nuevo producto" (`inventory.manage`). Río de
  KPIs de todo el catálogo (`useProductInventoryKpis`, todos `take=1`: SKUs activos = `products?activeOnly=true` → `total`;
  Unidades totales = `inventory/balances?includeZero=false` → `totalOnHand`; Bajo mínimo = `products?belowMin=true`; Con número de
  serie = `products?activeOnly=true&serialOnly=true`, y `products?serialMissing=true` para el aviso). Cada KPI es un botón
  (`.inv-kpi`, `aria-pressed`, activo `.on`) que filtra la tabla con `?kpi=active|available|low|serial` (activos; Unidades
  totales = activos con existencia EN MANO > 0 = `activeOnly`+`onlyOnHand` —decisión del 2026-09-30; su vista es "Activos con
  existencia" y su aviso (`title`) dice que la cifra suma también la existencia de los inactivos, porque sale de
  `/inventory/balances`—; `belowMin`; `serialOnly`); otro clic o "Limpiar" lo quita. Lote 15: `?warehousePublicIds=` (repetible)
  elige los almacenes al abrir (una vez; lo manda la franja "Almacén hoy" con `?kpi=low`). El KPI viaja al Reporte de inventario con
  `productListQuery` (mismo filtro que la tabla). Bajo mínimo va en
  naranja (`.money`: número y borde al pasar el mouse) solo si es > 0; Con número de serie, solo si hay productos SERIAL con series
  incompletas, con "N sin series completas". Filtros encima del panel, todos al API y a la página 1: Almacén (`SearchSelect` →
  `warehousePublicIds`: acotan las cantidades de cada fila a esos almacenes, no quitan productos), SKU (`ProductMultiFilter` con
  inactivos → `productPublicIds`), Nombre (`TextFilter`, 300 ms → `name`), Categoría (`categoryIds`, con subcategorías) y Marca
  (`SearchSelect` sobre `useProductBrands` → `brands`); sin `QBox`. Tabla paginada en el servidor: SKU, Producto, Categoría, Marca
  (modelo tenue debajo; exporta "Marca · Modelo"), Dueño ("Propio" tenue), Disponible, Reservado ('—' en cero), Total, Rastreo
  (etiqueta de `TrackingType`), Estado (Inactivo / Bajo mínimo / OK; fila inactiva atenuada). Exportar = todo lo filtrado. Clic =
  `ProductEditorByIdModal`. Pestaña Categorías en `?tab=categories`.
  - `productFilters.ts` (puro): `ProductFilterState` (`warehouses`, `products`, `name`, `categoryIds`, `brands`, `kpi`),
    `EMPTY_PRODUCT_FILTERS`, `productListQuery(f)` (tabla, Exportar y Reporte de inventario), `adjustmentsKardexQuery(f)`
    (`types=[ADJUSTMENT]` + almacenes, productos, categorías, marcas y nombre; el KPI no aplica a movimientos), `kpiQuery`,
    `parseKpiParam`, `toggleKpi`, `describeProductFilters(f, names, t, 'inventory' | 'adjustments')` ("Filtros aplicados" con
    nombres; un id sin nombre se muestra tal cual).
  - `inventoryReports.ts`: `generateInventoryReport(ctx)` (todos los productos con `productListQuery` de a 200 hasta 10 000,
    agrupados por categoría con subtotal de unidades y valor, total general; Valor = Total × costo de compra —sin costo, '—' y
    aviso—; nota de que no hay costo promedio ni por lote; aviso si se truncó) y `generateAdjustmentsReport(ctx)` (movimientos
    ADJUSTMENT del Kárdex con los filtros trasladados, del más reciente al más antiguo: Fecha y hora, SKU, Producto —con lote o
    serie debajo—, Almacén / Posición —destino si entra, origen si sale—, Cantidad ±, Motivo, Nota, Usuario; entradas, salidas y
    neto; aviso si hay KPI elegido o si se truncó). `ctx` = `{ t, lang, company, user, filters, names }`. Armadores puros:
    `buildInventoryReport`, `buildAdjustmentsReport`, `groupInventoryByCategory`, `inventoryValue`, `adjustmentLocation`,
    `adjustmentTotals`.
  - `InventoryReportButtons.tsx`: `<InventoryReportButton filters className? />` y `<AdjustmentsReportButton filters className? />`
    (resuelven nombres de almacenes/categorías, compañía y usuario; "Generando…" mientras tanto; error → toast). Para repetir el
    reporte de ajustes en otra pantalla:
    ```tsx
    <AdjustmentsReportButton filters={{ ...EMPTY_PRODUCT_FILTERS, warehouses: [warehousePublicId] }} />
    ```
    Lote 14 (hallazgo 22): `<AdjustmentsReportButton query={{ kardexQuery, filterLabels }} />` = con la consulta del Kárdex ya
    armada (fechas, dueño, dirección, motivo, "Solo manuales"…; se fuerza `types=[ADJUSTMENT]` y se quita la página:
    `adjustmentsReportKardexQuery`) y sus "Filtros aplicados" ya traducidos; manda sobre `filters` (sin aviso de KPI). Así lo
    usa 'Transferencias y ajustes' con `adjustmentsQuery(f)` y `describeMovementFilters(f, names, t)`.
- Kárdex de movimientos (`/warehouse/kardex`, `InventoryScreen`; maqueta `ledger()`): pestañas Kárdex (primera, sin
  parámetro), Saldos (`?tab=balances`) y Conciliación (`?tab=reconciliation`), con Ajustar/Transferir en la cabecera (modales
  del Lote 14). **Lote 14 (hallazgo 6): los tres tabs COMPARTEN los filtros** (`InventoryFilterState` en la pantalla,
  `InventoryFilterBar.tsx`): Fecha, Tipo, Almacén, Posición (`BinMultiFilter`), Producto (con dados de baja), Categoría, Dueño,
  Motivo, Dirección (entradas/salidas), Lote, Serie y "Solo manuales"; más "Incluir en cero"/"Solo con disponible" en Saldos y
  Estatus (por defecto Pendiente) en Conciliación. Cambiar de pestaña NO descarta nada; un filtro que la pestaña no aplica
  (`TAB_FILTERS`: Saldos no filtra por fechas, tipo, dueño, motivo, dirección ni "Solo manuales"; Conciliación, solo almacén,
  posición, producto, categoría, fechas y estatus) se atenúa y, si tiene valor, se nombra en la ayuda bajo la barra. Arriba de
  Kárdex y Saldos, `SummaryBar` con `GET /inventory/transactions/summary` y los MISMOS filtros (en Saldos además En mano y
  Disponible del `BalancePageDto`, D13). Columnas del Kárdex (`KARDEX_COLUMNS`): Fecha, Hora, Tipo, SKU, Producto, Dueño,
  Categoría, Cantidad, Almacén/Posición, Lote/Serie, Motivo, Origen y Usuario; clic en una fila = `KardexTransactionModal`.
  Conciliación = `DiscrepanciesPanel` (descuadres paginados, "Ejecutar conciliación" —los productos del filtro o todos— y
  "Revisión automática: al día / N en cola") + `DiscrepancyModal` (datos, reservado, últimos movimientos e historial;
  "Corregir el saldo según el Kárdex" solo en descuadres por posición y "Descartar" con nota obligatoria, `inventory.adjust`;
  tras corregir, "Crear conteo de esa posición" con `POST /cycle-counts` y `binIds`, `warehouse.count.capture`; los mensajes
  del API tal cual).
  **Contrato de la URL** (se lee UNA vez al montar; los filtros valen para las tres pestañas): `tab`, `warehousePublicIds`,
  `product` (publicId; el SKU se resuelve con su ficha, `useProductsByPublicId`; si no se puede leer, 'Producto no
  disponible'), `categoryIds`, `types`, `reasons`, `direction` (IN/OUT), `manualOnly=true`, `from`/`to` (YYYY-MM-DD),
  `refEntity`+`refId` (documento de origen: píldora "Documento: CYCLE_COUNT #3" que se puede quitar), `status` (descuadres),
  `txn=<id>` (abre el detalle de un movimiento; se pone y se quita al abrir/cerrar) y `discrepancy=<publicId>` (abre un
  descuadre; sin `tab` implica Conciliación: así llega el "Revisar" de "Necesita tu atención"). Al cambiar de pestaña la URL
  queda solo con la pestaña (los filtros se quedan en pantalla). Lo usan los enlaces de Pulso (`?tab=balances&categoryIds=…`,
  `?product=…`), los reportes de Productos e inventario (`?types=ADJUSTMENT`), el conteo cerrado (`?refEntity=CYCLE_COUNT&refId=`)
  y el Pulso (`?tab=reconciliation&discrepancy=`). `/warehouse/inventory` redirige aquí (`legacyInventorySearch`).
  ```tsx
  <Link to={`/warehouse/kardex?tab=reconciliation&discrepancy=${publicId}`}>{t('…review')}</Link>
  <Link to={`/warehouse/kardex?refEntity=CYCLE_COUNT&refId=${count.id}`}>{t('…seeAdjustments')}</Link>
  ```
- `productRules.ts`: esquemas zod de peso (`weightKgSchema`), volumen (`volumeM3Schema`) y costo/precio (`moneySchema(t, 'cost' |
  'price')`: mensaje de decimales por campo y tope `< 10¹⁴`) con los mensajes del manual 06, y la cantidad de un ajuste manual
  (`adjustQuantitySchema(t)`: obligatoria, ≠ 0, ≤ 3 decimales, con signo; desde el Lote 14 los ajustes usan Subir/Bajar con
  `adjustDirectionSchema(t)` —'Elija si el ajuste sube o baja el inventario.'— y `adjustMagnitudeSchema(t)` —positiva: 'La
  cantidad debe ser mayor que cero.'—); Lote 12: `brandModelSchema(t, 'brand' | 'model')` (opcional, recortado, ≤ 100, mensaje del API) y
  `adjustNotesSchema(t)` (nota obligatoria de TODO ajuste manual, ≤ 300, mensajes exactos del API —400 en `errors.notes`—: la
  usan el bloque de ajuste de `ProductEditorModal`, `InventoryAdjustModal` del Kárdex y la acción MANUAL_ADJUSTMENT de
  `ResolveShortageModal`); pruebas en `productRules.test.ts`.
- Ficha de la orden de compra (`PurchaseOrderDetailScreen`, decisión del 2026-09-30): Proveedor (`ComboSelectInput` sobre
  `useSuppliers({ includeInactive: false })`, el actual dado de baja se agrega con la marca "Inactivo") y Almacén
  (`WarehousePickerInput`) editables y obligatorios solo con la orden en DRAFT y `canEdit`; fuera de DRAFT, de solo lectura. El
  PATCH lleva solo lo que cambió. Lógica pura en `purchaseOrderEdit.ts`: `isDraftPurchaseOrder`, `supplierOptionsWithCurrent`,
  `purchaseOrderPartyChanges` y `partyErrorField` (404/422 sin campo → bajo Proveedor o Almacén si cambió uno solo; 409 y
  los demás, al aviso del `Form`).
- Empacar (`PackModal.tsx`, Lote 13: archivo propio; `batch` = `publicId`, `number`, `clientName`, `rowVersion`): Tipo de servicio
  y de paquete ofrecen "Predeterminado de la compañía ({etiqueta})" solo si `useTenantSettings` trae `defaultServiceType`/
  `defaultPackageType`; si no (o mientras carga), la opción vacía es "Elija el tipo de …" y el campo es obligatorio ('Elija el
  tipo de servicio.'/'Elija el tipo de paquete.'). Lo abren la acción de fila "Empacar" de la lista y la ficha.
  `{packing && <PackModal batch={packing} onClose={() => setPacking(null)} />}`
- Recolección y empaque (`/warehouse/pick-batches`, `PickBatchListScreen`; maqueta `picking()`, Lote 13): pestaña Recolecciones
  (primera) y Reabasto (`?tab=replenish`, sin cambios). Con `warehouse.pick`, dos paneles en `SplitPane storageKey="pick-batches"`
  (60/40; bajo 900 px, Recolección arriba y la lista abajo); sin él, solo la lista a todo el ancho.
  - `CollectPanel` (`onCollected?(batch)`): el antiguo modal "Nueva recolección" llevado a la pantalla. Almacén (preelegido si la
    compañía tiene uno solo) y rejilla de líneas en `DataTable` (`pagination={false}`, `exportable={false}`, tarjetas con el panel
    < 560 px; sin orden por columna: es captura): Producto (del almacén, con disponible, del dueño de las otras filas; pista "N
    disp."), Cantidad, Posición (`BinPickerInput` con `options`: SOLO las posiciones donde el producto —y el lote elegido— tiene disponible
    recolectable, "A-01 · A · 8 disp.", en el orden FEFO del servidor y la primera "Sugerida"; deshabilitada sin producto; se quita
    sola si deja de aplicar al cambiar producto o lote; pista "FEFO: A-01"; vacía = "Automático (FEFO)" del servidor), Lote (solo
    LOT/SERIAL; la columna aparece si alguna fila lo necesita), Series (solo SERIAL: botón "Series (n)" con modal), papelera.
    Siempre una fila vacía al final (hasta 100 líneas); "Recolectar (bajar de inventario)" = un solo POST sin las filas vacías;
    "Limpiar" deja una fila vacía (el almacén se queda). Al grabar: toast, líneas limpias y la nueva resaltada en la lista (no navega).
  - `collectForm.ts` (puro): `CollectLine`, `EMPTY_COLLECT_LINE`, `isBlankLine`, `needsTrailingBlank`, `compactPickLines(lines)` →
    `{ lines, indexMap }`, `buildCollectBody(values)` → `{ body, indexMap }`, `remapCollectErrors(err, indexMap)` (`lines[k].x` del
    servidor → `lines.<fila>.x`; `lines[k]` sin campo → la cantidad de la fila), `ownerFilterFor(lines, i)`, `collectSchema(t,
    issueText)` (con `pickLineIssues`, `pickDuplicateAcrossLines`, `firstOtherOwner`), `fefoCandidates`/`fefoBinSuggestions`/
    `fefoBinOptions`/`fefoAvailable(balances, lotId?)` (réplica de `PickBatchRules.Eligible` sobre `useInventoryBalances({ onlyAvailable: true })`).
    ```tsx
    const { body, indexMap } = buildCollectBody(values)
    try { await create.mutateAsync(body) } catch (err) { throw remapCollectErrors(err, indexMap) }
    ```
  - `PickBatchesPanel` (`highlight?` = publicId recién creado: fondo de flujo y página 1): filtros dentro del panel (Recolectada,
    Estatus, Producto, No. de orden, No. de factura, Incluir eliminadas) + `QBox`; tabla de servidor Número (con chip), Productos
    ("SKU ×cant"), Orden y factura, Cliente, Recolectada (tarjetas con el panel < 640 px); acciones de fila con ícono Empacar
    (`IconBox`) y Eliminar (`IconTrash`); clic en la fila = `PickBatchDetailModal`.
  - `OrderNumberFilter` (`label`, `value`, `onChange`): texto libre con sugerencias de números de orden existentes
    (`GET /pick-batches?orderNumber=&take=20`, 250 ms). `<OrderNumberFilter label={t('…orderNumber')} value={v} onChange={setV} />`
  - Ficha: `PickBatchDetailBody` (`batch`, `variant?: 'screen' | 'modal'`: etapas, resumen y líneas) + `PickBatchDetailActions`
    (`batch`, `onPack`, `onDelete`: Eliminar y Empacar con sus guardas), usados por `PickBatchDetailScreen` (la ruta
    `/warehouse/pick-batches/:publicId` se conserva) y `PickBatchDetailModal` (`publicId | null`, `onClose`; no se cierra mientras
    Empacar o Eliminar están abiertos encima). `DeletePickBatchDialog` (`batch | null`, `onClose`, `onDeleted?`): confirmación con
    el aviso de la orden si está empacada; 409 recarga ficha y lista. Puras en `pickBatchView.ts`: `PICK_BATCH_STATUS_DOMAIN`,
    `canDeletePickBatch`/`usePickBatchCanDelete` (empacada = además orders.cancel), `batchProductsText`, `orderNumberSuggestions`.
    `<PickBatchDetailModal publicId={detail} onClose={() => setDetail(null)} />`
- Orden de las listas paginadas del almacén (Saldos, Kárdex, Productos, Órdenes, Órdenes de compra, Recibos, Recolecciones,
  colas de tareas): sus endpoints solo aceptan `skip/take`, sin parámetro de orden. Llegan en el orden del servidor y, como
  toda tabla de la app, sus columnas llevan `sortValue` (Fase 11: toda columna con dato se ordena por clic en el encabezado);
  ahí el orden es en el cliente y reacomoda solo la página visible. Si un endpoint gana `sort`, se cambia a `sortable` + `onSort`.
- `lineRules.ts`: reglas puras de captura de líneas (réplica de `ReceiptRules`/`PickBatchRules`/`CycleCountRules`):
  `receiptLineIssues`, `countLineIssues`, `countLotIssue`, `pickLineIssues`, `pickDuplicateAcrossLines`, `firstOtherOwner`
  devuelven `{ field, code, params }` que se traducen con `t('warehouse.lineRules.<code>', params)` (mensaje exacto del manual 06)
  y se ponen bajo el campo con `ctx.addIssue` en zod. `parseSerials` (una serie por renglón o separadas por coma),
  `remapProblemFields(err, rename)` (renombra campos de un `ApiError`, p. ej. `lines[0].countedQty` → `countedQty`),
  `lineErrorsByIndex(err)`, `formatNumber/formatDate/formatDateTime` y `useDebounced` (búsqueda libre que va al API). Pruebas en `lineRules.test.ts`.

## Pulso del día por paneles (`src/features/analytics`, Lote F8a)
No es núcleo, pero es el contrato para que un lote posterior (F3, F5, 7C) agregue un panel sin tocar la pantalla.
- El servidor decide qué ve cada quien: `GET /api/v1/analytics/pulse` → `PulseDto { panels, indicators, charts, hasPersonalLayout,
  canOrganizeCompany }` solo con los paneles cuyo permiso `pulse.*`, permisos de datos y módulo tiene el usuario (incluidos los
  ocultos, `isVisible=false`, para el modo Organizar). La pantalla pinta EXACTAMENTE `shownPanels(panels)` (orden `sortOrder`, sin
  ocultos ni claves desconocidas) con el registro; nunca monta un panel a mano ni vuelve a mirar permisos del panel.
- Registro `PULSE_PANELS` (`pulsePanels.tsx`): `{ key, titleKey, render(ctx), hasContent(ctx), items?, pinnable? }` para `INDICATORS`
  (río, `PulseSections.tsx`), `CHARTS` (grilla), `WAREHOUSE` (`WarehousePulsePanel`), `ACTIVITY` (`ActivityPanel`),
  `ATTENTION` (Lote 14, `AttentionPanel`: "Necesita tu atención", `pulse.attention`, orden 5) y `WAREHOUSE_DAY` (Lote 15,
  `WarehouseDayBand`: franja "Almacén hoy", `pulse.warehouse` + `inventory.view` + WMS_LOTSERIAL, orden −10, `pinnable`).
  `ctx = { pulse, indicators, charts }` con los elementos ya visibles y ordenados (`shownItems`). Un panel nuevo = su clave en
  `PULSE_PANEL_KEYS` (`pulseLayout.ts`, espejo de `PulsePanels` del dominio) + su entrada aquí + su título en i18n:
  ```tsx
  ORDERS_RIVER: { key: 'ORDERS_RIVER', titleKey: 'analytics.pulse.panels.ORDERS_RIVER', hasContent: () => true,
    render: () => <OrdersRiverPanel /> },
  ```
- Hooks (`api.ts`): `usePulse(enabled = true)` (clave `PULSE_QUERY_KEY`, 403 sin redirigir), `useSaveLayout(scope: 'mine' | 'company')`
  → `mutateAsync(PulseLayoutRequest)` = `PUT .../pulse/layout?scope=` (pone el Pulso devuelto en caché e invalida), y
  `useResetMyLayout()` = `DELETE .../pulse/layout/mine` (invalida).
  ```tsx
  const save = useSaveLayout('mine')
  await save.mutateAsync(buildLayoutRequest(state))   // todos los paneles y elementos, orden = índice × 10
  ```
- "Necesita tu atención" (Lote 14, D6): `useAttention()` = `GET /analytics/attention` (`staleTime: 0`, 403 sin redirigir: el
  panel no se pinta) → `{ total, items (5 más antiguos), groups }`. Cada ítem trae `code`, `tone`, `params` (cadenas), `route` +
  `query` ("Revisar") y `sinceUtc`; cada grupo, su `total`, `route` + `query` ("Ver todos (N)"). Sin pendientes: "Todo en orden".
  El texto de un tipo nuevo de aviso = `analytics.attention.items.<code>.*` en i18n + su rama en `attentionRowText`
  (`attention.ts`, pura: `attentionHref`, `attentionToneClass`, `formatSigned`); uno desconocido se pinta con su código.
  Estilos `.inbox`/`.work.tone-*` de la maqueta en `pulse.css` ("Revisar" baja bajo el texto a 480 px).
  ```tsx
  const text = attentionRowText(item, t, lang)        // { title, detail, figures, since, reviewLabel }
  <Link className="btn sm" to={attentionHref(item.route, item.query)!} aria-label={text.reviewLabel}>{t('analytics.attention.review')}</Link>
  ```
- `<PulseOrganizer scope pulse onClose />`: modo Organizar (copia local; ▲ ▼, ojo "Ocultar/Mostrar", asa con ↑/↓ y arrastre
  HTML5 nativo con ratón —con `(pointer: coarse)` solo botones—; "Listo" = un solo PUT y toast "Pulso guardado"; error → `title`
  del ProblemDetails en el toast). Los indicadores se ofrecen en una lista por línea de módulo (h3 + ayuda
  `organizer.linesHint`) y solo se mueven dentro de su línea. Lógica pura en `pulseLayout.ts`: `sortPanels`, `sortItems`,
  `shownPanels`, `shownItems`, `initOrganizer` (indicadores ya agrupados por línea), `moveEntry` (no cruza de línea),
  `sameLine`, `toggleEntry`, `buildLayoutRequest`, `moduleGroup` (BusinessModule → grupo del menú e ícono).
- **Indicadores por línea** (Lote 15, D8): `indicatorLines(items)` → `[{ group: 'ops' | 'warehouse' | 'money', items }]` en el
  orden del menú (`INDICATOR_LINE_ORDER`: Operación, Almacén, Contabilidad), solo las líneas con algo y dentro el orden recibido.
  `IndicatorsRiver` conserva el h2 "Tus indicadores" y pinta un `div.pulse-line` por línea con su `StreamLabel level={3}`
  (nunca un h2 "Almacén": los recorridos buscan la sección con h2 "Almacén" del panel Almacén) y su `.river`; el nodo ya no
  repite el módulo (solo el rango).
  ```tsx
  {indicatorLines(indicators).map((l) => (
    <div key={l.group} className="pulse-line"><StreamLabel icon={<Icon />} level={3} tone="wh">{t(`nav.groups.${l.group}`)}</StreamLabel>…</div>
  ))}
  ```
  `StreamLabel` props: `icon`, `children`, `id?`, `level?: 2 | 3` (2 por defecto), `tone?: 'flow' | 'wh' | 'money'` (color del ícono).
- **`<ChartVisual chartType points isMoney name? emptyText? size? unit? tooltipLabel? />`** (`ChartVisual.tsx`): el ÚNICO
  dibujo de gráficos (Pulso, Análisis → Gráficos, vista previa del editor). **Siempre gráfico** (Lote 15: no hay respaldo a lista
  con pocos puntos); sin puntos, `emptyText` (por defecto `analytics.charts.noData`). Barra, línea (puntos visibles con ≤ 12
  puntos: un solo día se ve), dona (total al centro, compacto si es largo: `donutCenter`) y pastel (una rebanada sola sin hueco).
  Tooltip con la fecha larga si la etiqueta es un día 'YYYY-MM-DD' (eje en corto) y el valor formateado; `unit` = nombre de la
  serie en el tooltip. `size="mini"`: sin ejes, rejilla ni leyenda, 44 px de alto por CSS, `minPointSize` 2 (un día en 0 se ve).
  Cada punto `{ label, key?, value, color? }` puede traer su color. Envoltorio `div.pulse-chartbox[role=img]` con
  `aria-label` "{nombre}. {etiqueta}: {valor}; …" y `data-chart-kind`/`data-points` para pruebas.
  ```tsx
  <ChartVisual chartType={chart.chartType} points={chart.points} isMoney={chart.isMoney ?? false} name={chart.name} />
  <ChartVisual chartType="BAR" size="mini" points={days.map((d) => ({ label: d.date, value: d.units }))} isMoney={false} unit={t('…')} />
  ```
- "Tus gráficos": como mucho **2 por fila** (`PULSE_CHART_COLUMNS` en línea sobre `.pulse-charts`:
  `minmax(min(100%, max(380px, calc(50% - 8px))), 1fr)`), una columna por debajo de 776 px; Análisis → Gráficos conserva su grilla.
- **Franja "Almacén hoy"** (Lote 15, D1–D5; `WarehouseDayBand.tsx`, sin props): h2 "Almacén hoy · últimos 7 días" (nunca
  "Almacén" a secas: los recorridos buscan ese h2 del panel Almacén) con el selector de almacén, y 4 `.node.wh` unidas por
  `.pipe.wh` (violeta): Unidades recibidas, Unidades de salida, Conteos con diferencia (número = HOY, texto "7 días: N",
  `ChartVisual size="mini"` con 7 barritas —la última es hoy; tooltip "Hoy, …"/fecha larga y cantidad—) y Productos bajo mínimo
  (al final, "en este momento", sin gráfico). Naranja (`.node.wh.alert`: borde y número) según `countsAlert`/`belowMinAlert` del
  API. La tarjeta es un enlace (`.node-link`) al detalle filtrado; las barritas quedan encima (`.wh-mini`, z-index) y solo
  muestran. Datos: `useWarehousePulseDays(warehousePublicId)` = `GET /inventory/pulse/days?days=7[&warehousePublicIds=]`
  (`WAREHOUSE_DAY_DAYS` = 7 fijo; 403 sin redirigir → "—"; `staleTime` 60 s, se repite cada 5 min; lo invalidan las mutaciones
  de inventario: `warehouseKeys.pulseDays` en `STOCK`). Lógica pura en `warehouseDay.ts`: `warehouseDayCards(dto, wh)` →
  `[{ key, today, total, points, alert, href }]`, `warehouseDayHref(key, {from,to}, wh)` (Kárdex `types=RECEIPT` o
  `types=ISSUE&types=CROSSDOCK` con `from`/`to`; Conteo `status=RECONCILED_VARIANCE`; Productos `kpi=low`; todos con
  `warehousePublicIds` si hay almacén) y `warehouseDayRange(dto)`.
  ```tsx
  const cards = warehouseDayCards(useWarehousePulseDays(wh).data, wh)
  <Link className="node-link" to={cards[0].href}>…</Link>
  ```
- **Almacén compartido** (D5): `useWarehouseFilter()` (panel Almacén y franja) lee y cambia UN valor por compañía y usuario con
  `useStoredWarehouseFilter(key)` (`warehouseFilterStore.ts`: localStorage `teikem.pulse.warehouseFilter.{tenant}.{user}` +
  `useSyncExternalStore`; misma referencia mientras no cambie; si no se puede escribir, vale en la sesión). Cambiar el almacén en
  uno lo cambia en el otro; la franja solo toca `warehousePublicId` (conserva la categoría o el producto del panel).
  ```tsx
  const { filter, setFilter } = useWarehouseFilter()
  setFilter({ ...filter, warehousePublicId: e.target.value || null })
  ```
- **Filas fijas** (D6/D7, solo en "Pulso del día": raíz `wrap pulse pulse-home`; Indicadores y Gráficos usan `.wrap.pulse` sin
  filas fijas): la cabecera se parte en `.pulse-pin-head` (h1 con la fecha + botones "Organizar"; fija con `.pinned`, que falta
  al organizar porque la `.orgbar` ya es fija) y `.pulse-greet` (saludo y chip, se desplaza). La primera sección pintada queda
  fija justo debajo (`data-pinned`) solo si su entrada es `pinnable` y no se organiza (`pinnedPanelKey(sections, organizing)`);
  oculta o más abajo, solo queda la fecha. Altos medidos con `useElementHeight` en `--pulse-head-h` (desplazamiento de la franja)
  y `--pulse-pin-h` (`scroll-margin-top` de lo que se desplaza: el foco no queda tapado). Fondo = `var(--app-bg)` fijo al
  viewport (opaco, sin corte). Celular (≤ 720 px): franja compacta 2×2 (número y barritas, sin texto pequeño ni "· últimos 7
  días"); alto ≤ 560 px (acostado): solo la fecha. Un panel nuevo que sea una franja de números: `pinnable: true`; nunca un
  panel alto.
- Gráficos "de la compañía" (Lote 15, D9/D14): `isCompanyChart(dto)` (`definitions.ts`: no es de sistema y no tiene dueño) →
  chip `analytics.charts.companyBadge` en Análisis → Gráficos; Editar/Eliminar salen con `canEdit` del API (`analytics.manage`)
  y la confirmación avisa que, borrado, no vuelve (`deleteCompanyBody`).
- Vista previa del editor (`chartPreview.ts`) replica el motor del Lote 15: barra/dona con SUM o COUNT piden todos los grupos
  y juntan el resto en "Otras" (`foldOthers`: con más de 8, los 7 mayores + "Otras", clave `OTHERS_KEY` = '$others'; la
  etiqueta la pasa la pantalla con `analytics.charts.others`); agrupar por fecha usa el día LOCAL (`localDayOf`).
- Estilos propios en `pulse.css`, todos bajo `.pulse` (`.streamlabel` —`.line` para el h3 de línea, tonos `.flow|.wh|.money`—,
  `.pulse-line`, `.river`, `.node.flow|.money`, `.node .spark` —barra de segmentos `<i style={{ height: '40%' }} />` del color
  del nodo—, `.pipe`, `.pulse-charts`, `.pulse-chartbox(.mini)`, `.orgbar`, `.orgrow`, `.orghint`, `.orgline-h`, la franja
  `.wh-band`/`.wh-river`/`.node.wh(.alert)`/`.pipe.wh`/`.wh-mini` y las filas fijas bajo `.pulse-home`): río que envuelve bajo
  980 px y una columna a 480 px (la franja, 2×2).
- Indicadores y Gráficos (Fase 10): el editor (`DefinitionEditor`) comparte en "Compartido" con usuarios (`admin.users`) y
  con roles (`useRoles()` de `features/system/api`); cada `ShareDto` lleva `userId` o `roleId`. Solo en gráficos: sin
  selector de módulo (sale de la fuente) y con `<ChartPreview …valores del formulario isMoney />` (`ChartPreviewPanel.tsx`):
  `planChartPreview` + `chartPreviewPoints` (`chartPreview.ts`, lógica pura) sobre `POST /analytics/reports/{fuente}/preview`
  vía `useChartPreview(req)`. La tarjeta de gráfico lleva el rango en línea (`my-date-range`) y un solo switch (`my-pulse`);
  la de indicador sigue con el diálogo `DefinitionRangeModal` y sus dos switches.

## Ajustes de la compañía (`src/features/system`, lote F9)
`/system/settings` (`TenantSettingsPage`, `admin.tenant` + SYSTEM; maqueta `ajustesScreen`): pestañas `.seg` en la cabecera con
`?tab=general|region|calendar|modules|ops|brand` (General = sin parámetro; `settingsTabFromParam`). Sin `admin.tenant` todo
queda de solo lectura (`<fieldset disabled>`, sin botones de guardar). Hooks en `tenantSettingsApi.ts`: `useFormatOptions`,
`useSaveTenantSettings` (deja el DTO en `catalogKeys.tenantSettings`: el proveedor de formatos y la marca cambian al instante),
`useHolidays`/`useHolidayAction`, `useModulesCatalog`/`useSetModule` (AAL2 lo resuelve el cliente), `useCapabilities`/
`useSetCapabilities`. Puras: `regionForm.ts` (`toRegionForm`, `previewSettings`, `regionRequestBody` —siempre el juego completo
de 13 campos—, `formIsCustom`, `allowedLists`, `TIME_ZONES`, `PHONE_MASKS`), `tenantCalendar.ts` (máscara de días
laborables, `nextWorkDay`, feriados "cada año"), `tenantModules.ts` (`dependencyOff`, `enabledDependents`) y
`settings/operations.ts` (`capabilityAllowed`, `recvSummaryRows`). Para abrir una pestaña desde otra pantalla:
`<Link to="/system/settings?tab=region">…</Link>`.

## Seguridad y auditoría (`src/features/system`, lote F10)
`/system/audit` (`AuditScreen`, `admin.audit` + SYSTEM; maqueta `auditoriaScreen`): pestañas `.seg` en la cabecera, Actividad
(sin parámetro) y Sesiones y MFA (`?tab=sessions`, `auditTabFromParam`; Ajustes → General abre `/system/audit?tab=sessions`).
- Hooks en `auditApi.ts`: `useActivity(query)` (`GET /audit/activity`, `keepPreviousData`), `fetchAllActivity(query)` (todo lo
  filtrado con `fetchAllPages`), `useCompanySessions()` (`GET /audit/sessions`: todas las sesiones de la compañía, `isCurrent`),
  `useRevokeCompanySession()` → `mutateAsync(id)` y `useRevokeOtherSessions()` → `{ revoked }` (`admin.users`; la segunda con
  AAL2, la resuelve el cliente); ambas invalidan sesiones y actividad (`auditKeys`). La política usa `useTenantSettings` +
  `useSaveTenantSettings` (PUT parcial).
- Puras en `audit/auditView.ts`: `activityQuery(filtros, skip, take, zona?)` (días locales → `from`/`to` UTC, `to` = medianoche
  del día siguiente), `activityBadge(row)` (`change` | `event` | `alert` por `typeCode`/`outcomeCode`, nunca por la etiqueta),
  `activityDetailParts(json, textos)` / `activityDetailText(row, textos)` ("Campo: antes → después"; sin `$key`),
  `sortRows(rows, sort, valor)` (orden local con vacíos al final: con paginación del servidor la tabla controla `sort`/`onSort`
  y ordena la página; la exportación ordena todo igual), `sessionLocation`, `reauthOptions`, `toPolicyValues`, `policyErrors`,
  `policyRequestBody` (solo lo que cambió).
- Un segmento usado como FILTRO (no como pestañas) va en `<fieldset className="aud-kind"><legend>` con botones `aria-pressed` y se
  anota con `useRegisterFilter`; "Exportar CSV" propio = `exportTable('csv', columns, filas, { title })` con las mismas columnas.
  ```tsx
  const { items } = await fetchAllActivity(activityQuery(filtros, 0, 200))
  await exportTable('csv', columns, sortRows(items, sort, sortValue), { title: t('system.audit.activity.fileName') })
  ```

## Patrones de pantalla (copiar de `src/kernel/ui/templates`)
Plantillas completas y compilables (no montadas en rutas) sobre clientes; textos en `examples.clients.*` (una pantalla real usa su
propia sección `clients.*`).
- `ListScreen.example.tsx`: cabecera `.head` con "Nuevo" (`<Can perm="clients.create">`) → `Filters` (uno va al API:
  `includeInactive`; otro filtra lo cargado: estatus de `useStatuses`) → `Panel flush` con `QBox` y `DataTable` (orden local,
  25 por página, `StatusChip`, acción "Dar de baja" con `perm` + `visible` por estatus) → `Modal` de alta con `Form` (zod, errores
  del servidor bajo el campo, strings vacíos → null en el request) → `ConfirmDialog` + `toast.success` + invalidación de la lista.
- `DetailScreen.example.tsx`: carga con `Spinner`, 404 con `EmptyState`; cabecera con identidad y acciones guardadas por permiso
  y por estado del DTO; `StatusPipeline` (transición por `POST .../status`); `Tabs`; pestaña con `Form` que guarda con
  `rowVersion` (409 → recarga la ficha) y campos deshabilitados sin permiso; pestaña con `DataTable` de hijos.
  En un PATCH el API lee `null` como "no cambiar": los textos se envían tal cual (`''` borra el valor; nunca `|| null`), y un
  campo que el API no permite borrar (p. ej. `creditLimit`) se valida en zod para no quedar vacío.

## Pruebas de extremo a extremo (`e2e/`)
Playwright (`playwright.config.ts`, proyectos `escritorio` y `movil` = Pixel 7 a 360 px de ancho) contra el API real (`API_URL`, por defecto
http://localhost:5000, con db-init hecho) y Vite en :5173: `npm run e2e`. El recorrido de cada lote va en `e2e/loteFN.spec.ts`
(pasos en español, `test.use({ locale: 'es-PR' })`), comprueba en móvil con
`expectNoHorizontalScroll(page)` que nada se sale del viewport (el shell recorta con overflow hidden, así que el `scrollWidth` del
documento no basta: se miden `.stage`/`.main`/`.bar` y cada elemento visible dentro; los contenedores con scroll horizontal propio,
`.seg`, se miden ellos mismos) y no deja cambios persistentes que rompan otra corrida. El CI lo corre en el job del backend después del smoke.

## Antes de devolver una pieza
`npm run check` en verde (tipos generados, tsc, oxlint, vitest, build). Si tocaste el kit: prueba unitaria y línea en este archivo.
