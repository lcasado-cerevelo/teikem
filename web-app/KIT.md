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
- `createApiClient({ baseUrl, fetch })` solo para pruebas (cliente con la misma política sobre un `fetch` simulado).

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
El `src` cambia al cambiar idioma o tema sin recargar ni desmontar. Sin logo por compañía (no hay backend para eso).

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
- `login(email, password, tenantId?)` → `{ status: 'ok' } | { status: 'mfa_required', enrollmentRequired } |
  { status: 'tenant_selection', tenants }` (AuthResultDto). `selectTenant(id)` repite el login con la compañía elegida
  (credenciales solo en memoria). Pantallas: `/login`, `/mfa`, `/select-tenant` (`features/auth`).
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
- `base.css`: clases de la maqueta: `.head`, `.btn(.flow/.money/.sm/.block)`, `.panel .ph2 .pb`, `.pal .pi .pb .ft` (en línea
  ocupa el ancho; dentro de `.scrim.on` es modal), `.chip` + `.s-cap/.s-wh/.s-disp/.s-route/.s-deliv/.s-cod/.s-fail/.s-warn`,
  `.lst` (tabla), `.f` + `.ferr` + `.r2/.r3` (formularios), `.sw` (toggle), `.filters`, `.seg`, `.msel`, `.qrow .qbox`, `.empty`,
  `.note`, `.tag`, `.toast`, `.spin`. Breakpoints: 900 px (cajón), 720 px (tablas a tarjetas, filtros en 2 columnas), 480 px (una columna).
  `.md` (maestro-detalle a dos columnas, 280 px + resto; una columna bajo 720 px) + `.domlist`/`.domit(.on)` (lista de la
  izquierda: botón por fila, con chip de origen y contador opcional `.cnt`) — patrón de "Catálogos de valores" (F8a P6) y
  cualquier pantalla con una lista de la izquierda y el detalle a la derecha; bajo 720 px usa un `<select>` en vez de `.domlist`.

## Datos comunes (`src/kernel/catalogs`, `src/kernel/custom-fields`, `src/kernel/dsl`)
- `useLookups(domain, { includeDisabled?, enabled? })` → `useQuery` con `LookupOption[]` (`{ code, label, description, sortOrder,
  isEnabled }`) de `GET /api/v1/catalogs/{entity}` (caché 10 min; `code` = InternalCode que se envía al API).
  `const { data: regions = [] } = useLookups('Region')`. `includeDisabled` solo para pintar valores históricos.
- `useStatuses(domain, { includeDisabled? })` → `StatusOption[]` (`{ code, label, color, stageKind, isInitial, isEnabled,
  sortOrder, icon }`) de `GET /api/v1/status/{entity}`, ordenados. `color` es `#hex` validado o null. `StageKinds` =
  `PIPELINE | LATERAL | TERMINAL`. También: `useStatusHistory(entityType, id)`, `useLateralEntries(entityType)`,
  `usePipelineValidation(domain)`, `catalogKeys` (claves de consulta para invalidar).
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
| `Panel` | `title?`, `subtitle?`, `actions?`, `footer?`, `flush?` (cuerpo sin padding, para tablas), `children` | contenedor estándar (`.pal/.pi/.pb/.ft`) |
| `DataTable<T>` | `columns: DataColumn<T>[]`, `rows`, `rowKey(row)`, `sort?`/`onSort?`, `defaultSort?`, `page?`/`pageSize?`/`total?`/`onPage?`, `rowActions?`, `onRowClick?`, `empty?`, `loading?`, `label?`, `dense?` | tabla (TanStack Table v9) con orden por columna (flecha ▲/▼, `aria-sort`, primer clic ascendente, vacíos al final), paginación y tarjetas bajo 720 px (título + "etiqueta: valor" + acciones; selector "Ordenar por"). Sin scroll horizontal de página ni scrollbar propio: las celdas y encabezados parten el texto, los números no; entre 721 y 1100 px baja el padding y con `dense` (automático desde `DENSE_COLUMNS` = 8 columnas contando acciones) usa `.densetbl` (tipografía y padding menores) |
| `DataColumn<T>` | `id`, `header`, `cell(row)`, `sortValue?(row)` (ordenable en cliente), `sortable?` (ordenable en servidor), `align?: 'end'` (número), `card?: 'title' \| 'hidden'` | definición de columna (la primera visible es el título de la tarjeta si ninguna dice `title`) |
| `RowAction<T>` | `key`, `label`, `onClick(row)`, `perm?` (guarda de permiso: sin él no se pinta), `visible?(row)` (guarda de estatus/`capabilities`), `disabled?(row)`, `tone?: 'flow' \| 'danger'`, `icon?` (de `kernel/ui/actionIcons`) | acciones por fila. Con `icon`, el botón es solo ícono (28×28, `.rowbtn`, como en la maqueta) y `label` pasa a ser su `aria-label`/`title`; sin `icon`, es el botón de texto de siempre. Toda acción de fila nueva lleva `icon` — solo se deja sin él cuando de verdad no hay un ícono claro para esa acción |
| `Filters` | `children`, `onClear?` (botón "Limpiar"), `label?` | fila `.filters` (8 → 4 → 2 → 1 columnas); "Limpiar" (`.filters-clear`) ocupa solo el ancho de su contenido |
| `SelectFilter` | `label`, `value` (`''` = todos), `onChange`, `options: {value,label}[]`, `allLabel?` (`null` = sin opción "Todos") | filtro de selección única |
| `DateRangeFilter` | `label`, `value: {from,to}` ('YYYY-MM-DD' o ''), `onChange` | rango de fechas (ocupa 2 columnas); filtrar lo cargado con `inDateRange(iso, range)`; `EMPTY_RANGE` |
| `SearchSelect` | `label`, `options`, `value: string[]` (vacío = todos), `onChange`, `placeholder?` | selección múltiple con buscador (`.msel`), botones Todos/Ninguno; cierra con Escape o clic fuera |
| `SearchMultiSelect` | `options`, `value`, `onChange`, `placeholder?`, `id?` (del botón, para `<label htmlFor>`), `labelledBy?`, `disabled?`, `invalid?`, `describedBy?`, `onBlur?`, `buttonRef?` | el mismo control sin etiqueta propia, para un `.f` que ya tiene su `<label>` (formularios, campo personalizado MULTISELECT). Toda selección múltiple usa este control o `SearchSelect`, nunca una lista cruda de casillas |
| `QBox` + `matchesQ(q, ...texts)` | `value`, `onChange`, `placeholder?` | buscador libre sobre lo que se muestra: cada palabra de `q` debe aparecer en algún texto, sin mayúsculas ni acentos. Se aplica DESPUÉS de los filtros |
| `Chip` | `tone?: 'neutral'\|'cap'\|'wh'\|'disp'\|'route'\|'deliv'\|'cod'\|'fail'\|'warn'`, `color?` (hex del catálogo), `title?`, `children` | píldora que nunca envuelve. Para estatus usa `StatusChip` (catálogo del tenant) |
| `Modal` | `open`, `title`, `onClose`, `footer?`, `size?: 'sm'\|'md'\|'lg'`, `dismissible?` (false mientras guarda) | portal en `<body>`, `.scrim > .pal` con el padding del shell; Escape/clic fuera cierran; foco al primer control y vuelta al cerrar |
| `ConfirmDialog` | `open`, `title`, `message`, `confirmLabel?`, `tone?: 'flow'\|'danger'`, `onConfirm()` (async), `onClose` | confirma bajas o acciones con guarda de estatus. Si `onConfirm` lanza (422 `status_rule`, 409…), muestra el mensaje del servidor y no se cierra |
| `Form` | `form` (de `useForm({ resolver: zodResolver(schema) })`), `onSubmit(values)` (async), `onError?(problem)`, `id?` (para `<button type="submit" form={id}>` en el pie del Modal) | si `onSubmit` lanza, `applyProblemDetails(err, form)` pone cada error bajo su `Field`; el título y los errores sin campo van en un aviso arriba del formulario |
| `Field` | `name` (camelCase, como el DTO), `label`, `required?` (asterisco; la regla va en zod), `help?`, `children` (un control) | etiqueta + control + ayuda + error (`.ferr`, `aria-invalid`, `aria-describedby`) |
| Controles de `Field` | `TextInput` (`type?`), `NumberInput` (valor `number \| null`; vacío = null → `z.number().nullable()`), `Select` (`options`, `placeholder?`; valor string, '' = sin elegir), `DateInput` ('YYYY-MM-DD'), `Toggle` (`text?`; boolean), `TextArea` (`rows?`), `ClientPickerInput` (`includeInactive?`; valor publicId o null) | se registran solos en el formulario con el `name` del `Field`; aceptan los atributos nativos (`maxLength`, `min`, `placeholder`…) |
| `Tabs<K>` | `tabs: {key,label}[]`, `value`, `onChange`, `label?` | pestañas de una ficha (`.seg`, `role="tablist"`) |
| `EmptyState` | `title`, `body?`, `icon?`, `action?` | sin datos / sin resultados |
| `Spinner` | `label?`, `block?` (centrado) | carga (`role="status"`) |
| `toast` | `toast.success(msg)`, `toast.error(msg)`, `toast.info(msg)` | aviso abajo al centro (errores 6 s, resto 3.5 s). Se monta solo en `<body>` la primera vez; llamable desde `onSuccess` |
| `ClientPicker` | `value` (publicId \| null), `onChange(publicId, client)`, `includeInactive?` (por defecto false), `placeholder?`, `disabled?`, `invalid?`, `aria-label?` | combobox con buscador: `GET /api/v1/clients?search=&includeInactive=` (250 ms entre teclas; mantiene los resultados anteriores mientras busca), opciones "Code · Name" (marca "Inactivo"), teclado ↑/↓/Enter/Escape. Con un valor inicial pide `GET /api/v1/clients/{publicId}` para mostrar la etiqueta. Sin `clients.read`: "Su usuario no puede consultar clientes" (no saca de la pantalla) |
| `CategoryProductPicker` | `value: CategoryProductValue` (`{kind:'category', id}` \| `{kind:'product', publicId}` \| `null` = todos), `onChange(value, detail?)` (`detail.category`/`detail.product` = fila elegida), `categories` (árbol completo: `useProductCategories().data`), `categoriesLoading?`, `label?` (por defecto "Categoría o producto"), `id?`, `disabled?` | un solo combobox con buscador y dos secciones: 'Categorías' (árbol con sangría por nivel y "N productos" contando sus subcategorías —`categoryProductTotals`, como filtra el API—; el texto filtra por nombre o ruta) y 'Productos' (`GET /api/v1/products?search=&activeOnly=true&take=20`, 250 ms entre teclas, "SKU · Nombre"; sin texto no consulta). El valor se ve como píldora ("Categoría"/"Producto" + ruta o "SKU · Nombre", con elipsis) con ✕ para quitarlo; un producto que llega de fuera pide `GET /api/v1/products/{publicId}` para su etiqueta. Teclado ↑/↓ (recorre ambas secciones), Enter, Escape (cierra y devuelve el foco). A ≤ 480 px ocupa todo el ancho; por encima, su desplegable mide 340 px anclado a la izquierda, así que dale un contenedor de al menos 340 px (p. ej. `flex: 1 1 340px`) si un ancestro recorta con `overflow: hidden` (`.pal`). 403 de productos: aviso en su sección. Lógica pura en `categoryTree.ts`: `categoryTree(cats)` (aplanado padre→hijos con `level`; padre ausente = raíz), `filterCategoryTree`, `categoryLabel`, `isCategoryProductValue` (validar lo leído de localStorage), `sameCategoryProduct`, `categoryProductTotals(cats)` (id → productos del subárbol; `productCount` del DTO son solo los directos) |
| `useMediaQuery(q)`, `CARDS_QUERY` | | `true` mientras se cumpla la media query (`'(max-width: 720px)'` = modo tarjetas) |

**Orden y paginación de `DataTable`**
- Lista completa (la mayoría de endpoints de catálogo): no pases `onSort` ni `onPage`; DataTable ordena con `sortValue` (orden
  natural: `A-2` antes que `A-10`) y pagina con `pageSize`. Pasa `rows` memorizadas (`useMemo`): una lista nueva vuelve a la página 1.
- Servidor (endpoints con `skip/take` y `total`, p. ej. auditoría): controla `sort`/`onSort` y `page`/`onPage`, pasa `total`, y pon
  ambos en la clave de la consulta (`placeholderData: keepPreviousData` evita el parpadeo). `onSort` recibe `{ id, desc }` (id de la columna).
  ```tsx
  const [page, setPage] = useState(1)
  const query = { skip: (page - 1) * 25, take: 25 }
  const { data } = useQuery({ queryKey: ['/api/v1/audit/changes', query], queryFn: () => unwrap(api.GET('/api/v1/audit/changes', { params: { query } })), placeholderData: keepPreviousData })
  <DataTable columns={cols} rows={data?.items ?? NO_ROWS} rowKey={(r) => r.id!} page={page} pageSize={25} total={data?.total ?? 0} onPage={setPage} />
  ```

## Almacén (`src/features/warehouse`, Lote F6)
No es núcleo, pero lo comparten todas las pantallas del almacén, de compras, del cruce de muelle y la consulta de órdenes.
- `api.ts`: un hook por lectura con clave `[ruta, params]` (`useWarehouses(query?)`, `useWarehouse(publicId)`,
  `useWarehouseZones/Bins/Docks(publicId, query?)`, `useProducts`, `useProduct`, `useProductLots/Serials`, `useProductCategories`,
  `useInventoryBalances`, `useInventoryTransactions`, `useInventoryReconciliation`, `useLotGenealogy`, `useSerialTrace`, `useAsns`,
  `useReceipts`, `useReceipt`, `useWarehouseTasks`, `usePutawaySuggestions`, `useCycleCounts`, `useCycleCount(id, query?)`,
  `usePickBatches`, `usePickBatch`, `useSuppliers`, `usePurchaseOrders`, `usePurchaseOrder`, `usePurchaseOrderShortages`,
  `usePurchaseOrderShortageLines`, `useDockAppointments`, `useCrossDockPlans`, `useCrossDockPlan`, `useCrossDockCandidates`,
  `useOrdersReadonly`, `useOrderReadonly`, `useOrderLookup`). `query` es el tipo del esquema (`GetQuery<'/api/v1/…'>`); el último
  argumento `{ enabled?, handleAccessDenied? }`. Las listas paginadas usan `keepPreviousData`. Escrituras: `useCreateX`/`useUpdateX`
  o un hook de acciones con unión discriminada por `action` (`useSaveWarehouseZone`, `useWarehouseTaskAction`, `useCycleCountAction`,
  `usePurchaseOrderAction`, `useCrossDockAction`…); cada una invalida por prefijo su lista, su ficha y, si mueve inventario,
  saldos/Kárdex/existencias (`warehouseKeys` tiene los prefijos). `warehouseLabel` ("Code · Name"), `binLabel` ("Código · Zona")
  y `productLabel` ("SKU · Nombre").
- `pickers.tsx`: todos son combobox como `ClientPicker` (↑/↓/Enter/Escape, ✕ para quitar, clic para reabrir) y se pueden llenar
  con un **lector de código de barras**: el código completo + Enter elige la opción cuyo código es exactamente ese (sin
  mayúsculas ni acentos), aunque otra esté resaltada. Lógica pura en `pickerMatch.ts` (`foldText`, `exactCodeMatch`,
  `filterWarehouses`, `orderBins`).
  - `WarehousePicker` (almacenes activos, `GET /api/v1/warehouses?includeInactive=false` sin `search`: filtra EN EL CLIENTE
    por código o nombre, subcadena; la coincidencia exacta de código va primero; `value` publicId, `onChange(publicId, dto)`,
    `placeholder?` —texto sin valor; `null` = no se ofrece quitar—; conserva con su etiqueta un valor inactivo pidiendo su ficha).
  - `ProductPicker` (`GET /api/v1/products?search=&activeOnly=true`, 250 ms entre teclas, "SKU · Nombre", marca el dueño
    cliente; `ownOnly?`, `ownerClientPublicId?`, `warehousePublicId?`, `onlyAvailable?`, `includeInactive?` (omite `activeOnly`
    y marca "Inactivo"); `onChange(publicId, fila)` con `trackingTypeCode`).
  - `BinPicker` (posiciones de un almacén: `GET /api/v1/warehouses/{publicId}/bins?search=&includeInactive=false`, 250 ms
    entre teclas —el API compara código de posición y de zona—, "Código · Zona"). Props: `warehousePublicId` (vacío =
    deshabilitado con "Elija primero un almacén"; al cambiarlo quita el valor con `onChange(null, null)`), `value` (id de la
    posición, `number | null`), `onChange(binId, fila)`, `zoneTypeCodes?` (filtro en cliente, p. ej. `['STAGING','CROSSDOCK']`),
    `onlyWithStock?`, `suggestedBinIds?` (van primero con la marca "Sugerida"), `placeholder?` (p. ej. "Staging por defecto"
    cuando vacío = lo decide el servidor), `disabled?`, `invalid?`, `required?`, `aria-*`, `onBlur?`. Si el lector manda Enter
    antes de la pausa de 250 ms, busca de inmediato y elige al llegar la respuesta (código exacto, o el único resultado). Un
    valor fuera de la lista (posición dada de baja) se muestra buscando en la lista del almacén con `includeInactive=true`.
  Dentro de un `Field`: `WarehousePickerInput` (valor publicId o null), `ProductPickerInput` (`onPicked?(fila)` para condicionar
  lote/series) y `BinPickerInput` (mismas props que `BinPicker` salvo value/onChange, + `onPicked?(fila)`; el valor del
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
  `isAccessDenied(error)` (`accessDenied.ts`) para avisar junto a un campo cuando una consulta secundaria da 403.
- Inventario (`/warehouse/inventory`) lee la URL al montar: `tab=kardex` abre el Kárdex; `warehousePublicIds=<publicId>` y
  `product=<publicId>` filtran Saldos o Kárdex (el SKU de cada producto se resuelve con su ficha, `useProductsByPublicId`; si
  la ficha no se puede leer la píldora dice 'Producto no disponible'); `categoryIds=<id>` filtra Saldos (los tres repetibles o
  separados por comas). Esos filtros son solo de la pestaña abierta: al cambiar de pestaña se descartan. Es lo que usan los
  enlaces de Pulso, que llevan el almacén elegido para que el destino cuadre con la cifra. No hay parámetro de búsqueda: la
  búsqueda del Kárdex no compara el documento de origen, así que Actividad reciente no enlaza movimientos al Kárdex.
- `productRules.ts`: esquemas zod de peso (`weightKgSchema`), volumen (`volumeM3Schema`) y costo/precio (`moneySchema(t, 'cost' |
  'price')`: mensaje de decimales por campo y tope `< 10¹⁴`) con los mensajes del manual 06; pruebas en `productRules.test.ts`.
- Orden de las listas paginadas del almacén (Saldos, Kárdex, Productos, Órdenes, Órdenes de compra, Recibos, Recolecciones,
  Tareas): sus endpoints solo aceptan `skip/take`, sin parámetro de orden, así que sus columnas NO llevan `sortValue` (ordenarían
  solo la página visible). Llega el orden del servidor; si un endpoint gana `sort`, se agrega `sortable` + `onSort`.
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
- Registro `PULSE_PANELS` (`pulsePanels.tsx`): `{ key, titleKey, render(ctx), hasContent(ctx), items? }` para `INDICATORS`
  (río, `PulseSections.tsx`), `CHARTS` (grilla), `WAREHOUSE` (`WarehousePulsePanel`) y `ACTIVITY` (`ActivityPanel`).
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
- `<PulseOrganizer scope pulse onClose />`: modo Organizar (copia local; ▲ ▼, ojo "Ocultar/Mostrar", asa con ↑/↓ y arrastre
  HTML5 nativo con ratón —con `(pointer: coarse)` solo botones—; "Listo" = un solo PUT y toast "Pulso guardado"; error → `title`
  del ProblemDetails en el toast). Lógica pura en `pulseLayout.ts`: `sortPanels`, `sortItems`, `shownPanels`, `shownItems`,
  `initOrganizer`, `moveEntry`, `toggleEntry`, `buildLayoutRequest`, `moduleGroup` (BusinessModule → grupo del menú e ícono).
- Estilos propios en `pulse.css`, todos bajo `.pulse` (`.streamlabel`, `.river`, `.node.flow|.money`, `.pipe`, `.pulse-charts`,
  `.orgbar`, `.orgrow`…): río que envuelve bajo 980 px y una columna a 480 px; grilla `minmax(min(100%, 380px), 1fr)`.

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
