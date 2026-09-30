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
- Pestañas enlazables: una lista con `Tabs` cuya pestaña deba poder abrirse desde un enlace la guarda en `?tab=` con
  `useSearchParams` (la primera pestaña = sin parámetro; valor desconocido = la primera), como `AccountPage`, Recibo
  (`asns|putaway`), Recolección (`replenish`), Conteo cíclico (`tasks`), Cruce de muelle (`appointments|tasks`), Productos e
  inventario (`categories`) y Kárdex de movimientos (`balances|reconciliation`).
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
  es una fila elegible sin estilo nativo y `.unrow.on` = la elegida, fondo `--flow-bg`) — lista de compras de 'Ajustes de
  inventario'. `.adj-cols` (`warehouse.css`): maestro-detalle de esa pantalla, 300 px + resto como la maqueta, una columna
  bajo 720 px con la lista arriba (alto máx. 240 px). `.rcp-cols` (Lote 13, `warehouse.css`): el de Recibo, 340 px + resto
  (`.cols 340px 1fr` de la maqueta), una columna bajo 720 px con la lista arriba (alto máx. 260 px).

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
| Íconos de pantalla (`screenIcons.tsx`) | `IconBox`, `IconLayers`, `IconCash`, `IconUsers`, `IconChart`, `IconGear` (grupos del menú, `NAV` de la maqueta; `app/icons.tsx` los reexporta) e `IconWarehouse`, `IconGrid`, `IconCart`, `IconCheckin`, `IconBasket`, `IconClip`, `IconSwap`, `IconDoc`, `IconClock`, `IconLock`, `IconPencil` (`ICONOF` de la maqueta), `IconTag` ('tag': KPI "Con número de serie") e `IconCheck` ('check' de la maqueta, de `icons.tsx`: estados vacíos "todo resuelto") | el `icon` de `Panel` es el que la maqueta da a la pantalla en el menú (Almacenes → `IconWarehouse`, Ubicaciones → `IconGrid`, Productos e inventario/Órdenes → `IconLayers`, Compras → `IconCart`, Recibo → `IconCheckin`, Ajustes de inventario → `IconPencil` (su nota y su vacío; sus paneles usan `IconCart` como la maqueta), Recolección → `IconBasket`, Conteo → `IconClip`, Cruce de muelle → `IconSwap`, Kárdex → `IconDoc`, Usuarios → `IconUsers`, Roles → `IconShield` de `actionIcons`); una pantalla que no está en la maqueta usa el ícono de su grupo. `import { IconWarehouse } from '../../kernel/ui'` |
| `DataTable<T>` | `columns: DataColumn<T>[]`, `rows`, `rowKey(row)`, `sort?`/`onSort?`, `defaultSort?`, `page?`/`pageSize?`/`total?`/`onPage?`, `onPageSize?(size)` (servidor: el usuario cambió "Filas por página"), `pagination?` (por defecto true; false = todas las filas, sin rango ni selector), `rowActions?`, `onRowClick?`, `rowClassName?(row)` (clase extra de la fila y de su tarjeta; `'dim'` = atenuada, opacidad .55 de la maqueta para inactivos: `rowClassName={(p) => (p.isActive ? undefined : 'dim')}`), `empty?`, `loading?`, `label?`, `dense?`, `exportable?` (por defecto true), `exportFileName?` (base del archivo; por defecto `label` y luego el título del `Panel`), `exportRows?()` (filas a exportar en vez de las cargadas: `Promise<T[] \| {items, truncated}>`), `forceCards?` (Lote 13: tarjetas aunque la ventana sea ancha —tabla dentro de un panel angosto de `SplitPane`, decidido con `useElementWidth`—; el pie no cambia) | tabla (TanStack Table v9) con orden por columna (flecha ▲/▼, `aria-sort`, primer clic ascendente, vacíos al final), paginación (local por defecto, 25 filas) con pie completo y tarjetas bajo 720 px (título + "etiqueta: valor" + acciones; selector "Ordenar por"). Sin scroll horizontal de página ni scrollbar propio: las celdas y encabezados parten el texto, los números no (todos los encabezados con la misma letra, también los de columnas `align: 'end'`: solo las celdas numéricas van en monoespaciada); entre 721 y 1100 px baja el padding y con `dense` (automático desde `DENSE_COLUMNS` = 8 columnas contando acciones) usa `.densetbl` (tipografía y padding menores) |
| `DataColumn<T>` | `id`, `header`, `cell(row)`, `sortValue?(row)` (ordenable en cliente), `sortable?` (ordenable en servidor), `align?: 'end'` (número), `card?: 'title' \| 'hidden'`, `exportValue?(row)` (valor exportado explícito), `exportable?` (false = no se exporta: casillas, columnas solo visuales) | definición de columna (la primera visible es el título de la tarjeta si ninguna dice `title`). Exportación: `exportValue`, si no el texto de `cell` ("—" = vacío; un número formateado igual a `sortValue` sale como número), si la celda no tiene texto (`StatusChip`, ícono) `sortValue` |
| `fetchAllPages(fetchPage, { pageSize?, max? })` (`kernel/api/fetchAllPages`) | `fetchPage(skip, take) → Promise<{ items, total }>` | recorre `skip/take` de a `EXPORT_PAGE_SIZE` = 200 hasta el `total` o `EXPORT_MAX_ROWS` = 10 000 y devuelve `{ items, truncated }`: es lo que recibe `exportRows` (DataTable avisa con un toast si `truncated`). `exportRows={() => fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/x', { params: { query: { ...query, skip, take } } })))}` |
| `reportPdf.ts` (Lote 12; importar de `kernel/ui/reportPdf`, no está en el barril) | `downloadReportPdf(spec)`, `renderReportPdf(spec, { logo?, compress? })` → `jsPDF` en memoria; `ReportSpec` = `{ title, subtitle?, company?, user?, generatedAt?, locale, filters: {label,value}[], columns: {header, format?}[], sections: {title?, rows, subtotal?}[], totals?, summary?: {label,value,tone?}[], notices?, emptyText?, orientation? }`; `format` = `text` \| `quantity` \| `signed` \| `money` \| `unitCost` | PDF "de presentación" en el cliente (jsPDF + autotable con import dinámico): banda azul marino con el símbolo de Teikem (SVG de `public/brand/` rasterizado a PNG; si no se puede, solo texto), lema, compañía y fecha; título, "Generado el … por …", recuadro "Filtros aplicados" (vacío = "Sin filtros"), tarjetas de resumen y avisos; tabla con encabezado oscuro repetido por página, cebra, números a la derecha con separadores del idioma, fila de grupo por sección, subtotal (la etiqueta —primer valor— se funde con los vacíos que la siguen) y total general en azul marino; pie "Generado con Teikem · compañía" y "Página X de Y"; banda delgada en las páginas siguientes. Horizontal con más de 6 columnas. Textos del kit en `ui.report.*`; todo pasa por `pdfSafeText`. Lógica pura: `formatReportValue`, `buildReportBody`, `reportOrientation`, `reportFileName` (`reporte-de-inventario-advance-depot-2026-09-30.pdf`). `await downloadReportPdf({ title, company, user, locale: lang, filters, columns, sections, totals })` |
| `RowAction<T>` | `key`, `label`, `onClick(row)`, `perm?` (guarda de permiso: sin él no se pinta), `visible?(row)` (guarda de estatus/`capabilities`), `disabled?(row)`, `tone?: 'flow' \| 'danger'`, `icon?` (de `kernel/ui/actionIcons`) | acciones por fila. Con `icon`, el botón es solo ícono (28×28, `.rowbtn`, como en la maqueta) y `label` pasa a ser su `aria-label`/`title`; sin `icon`, es el botón de texto de siempre. Toda acción de fila nueva lleva `icon` — solo se deja sin él cuando de verdad no hay un ícono claro para esa acción |
| `Filters` | `children`, `onClear?` (botón "Limpiar"), `label?` | fila `.filters` (flex que envuelve: los filtros se reparten todo el ancho del panel, sin huecos; uno por renglón a 480 px); "Limpiar" (`.filters-clear`) ocupa solo el ancho de su contenido |
| `SelectFilter` | `label`, `value` (`''` = todos), `onChange`, `options: {value,label}[]`, `allLabel?` (`null` = sin opción "Todos") | filtro de selección única |
| `DateRangeFilter` | `label`, `value: {from,to}` ('YYYY-MM-DD' o ''), `onChange` | rango de fechas (ocupa el doble que un filtro normal); filtrar lo cargado con `inDateRange(iso, range)`; `EMPTY_RANGE` |
| `SearchSelect` | `label`, `options`, `value: string[]` (vacío = todos), `onChange`, `placeholder?` | selección múltiple con buscador (`.msel`), botones Todos/Ninguno; cierra con Escape o clic fuera. La flecha va a la izquierda del resumen (también en `CategoryProductPicker`) |
| `SearchMultiSelect` | `options`, `value`, `onChange`, `placeholder?`, `id?` (del botón, para `<label htmlFor>`), `labelledBy?`, `disabled?`, `invalid?`, `describedBy?`, `onBlur?`, `buttonRef?` | el mismo control sin etiqueta propia, para un `.f` que ya tiene su `<label>` (formularios, campo personalizado MULTISELECT). Toda selección múltiple usa este control o `SearchSelect`, nunca una lista cruda de casillas |
| `ComboSelect` | `options: ComboOption[]` (`{ value, label, hint? }`; `hint` = texto tenue que también se busca), `value` (`''`/null = ninguno), `onChange(value, option)` (`''`, null al quitar), `placeholder?`, `clearable?` (por defecto true: ✕), `loading?`, `disabled?`, `invalid?`, `required?`, `id?`, `aria-*`, `onBlur?` | selección ÚNICA con buscador sobre una lista local (catálogos, zonas…): combobox como `ClientPicker` (↑/↓/Enter/Escape, clic para reabrir) que filtra con `matchesQ` (etiqueta, valor o `hint`); Enter con el texto igual al valor o a la etiqueta de una opción la elige (lector de código de barras; `exactComboMatch` en `comboMatch.ts`). Úsalo en vez de un `<select>` cuando la lista es larga o se busca. En un `Field`: `ComboSelectInput` (`options`, `placeholder?`, `loading?`, `onPicked?(option)`; valor del formulario = `value`, `''` = ninguno). `<Field name="zoneType" label={t('…type')}><ComboSelectInput options={types} /></Field>` |
| `QBox` + `matchesQ(q, ...texts)` | `value`, `onChange`, `placeholder?` | buscador libre sobre lo que se muestra: cada palabra de `q` debe aparecer en algún texto, sin mayúsculas ni acentos. Se aplica DESPUÉS de los filtros |
| `Chip` | `tone?: 'neutral'\|'cap'\|'wh'\|'disp'\|'route'\|'deliv'\|'cod'\|'fail'\|'warn'`, `color?` (hex del catálogo), `title?`, `children` | píldora que nunca envuelve. Para estatus usa `StatusChip` (catálogo del tenant) |
| `Modal` | `open`, `title`, `onClose`, `footer?`, `size?: 'sm'\|'md'\|'lg'`, `dismissible?` (false mientras guarda) | portal en `<body>`, `.scrim > .pal` con el padding del shell; Escape/clic fuera cierran; foco al primer control y vuelta al cerrar |
| `ConfirmDialog` | `open`, `title`, `message`, `confirmLabel?`, `tone?: 'flow'\|'danger'`, `onConfirm()` (async), `onClose` | confirma bajas o acciones con guarda de estatus. Si `onConfirm` lanza (422 `status_rule`, 409…), muestra el mensaje del servidor y no se cierra |
| `Form` | `form` (de `useForm({ resolver: zodResolver(schema) })`), `onSubmit(values)` (async), `onError?(problem)`, `id?` (para `<button type="submit" form={id}>` en el pie del Modal) | si `onSubmit` lanza, `applyProblemDetails(err, form)` pone cada error bajo su `Field`; el título y los errores sin campo van en un aviso arriba del formulario |
| `Field` | `name` (camelCase, como el DTO), `label`, `required?` (asterisco; la regla va en zod), `help?`, `hideLabel?` (Lote 13: la etiqueta queda solo para lectores de pantalla, `.sr-only`; el error y la ayuda se ven), `children` (un control) | etiqueta + control + ayuda + error (`.ferr`, `aria-invalid`, `aria-describedby`). `hideLabel` = campo dentro de una celda de una rejilla cuyo encabezado ya dice qué es; la etiqueta sigue siendo única por fila: `<Field name="qty" label={t('…qtyOfLine', { n: i + 1 })} hideLabel><NumberInput /></Field>` |
| Controles de `Field` | `TextInput` (`type?`), `NumberInput` (valor `number \| null`; vacío = null → `z.number().nullable()`), `Select` (`options`, `placeholder?`; valor string, '' = sin elegir), `DateInput` ('YYYY-MM-DD'), `Toggle` (`text?`; boolean), `TextArea` (`rows?`), `ClientPickerInput` (`includeInactive?`; valor publicId o null) | se registran solos en el formulario con el `name` del `Field`; aceptan los atributos nativos (`maxLength`, `min`, `placeholder`…) |
| `PhoneInput` + `formatPhone`/`isValidPhone`/`normalizeStoredPhone`/`phoneDigits` (`phone.ts`) | `placeholder?` | teléfono con máscara `(xxx)xxx-xxxx` mientras se escribe, dentro de un `Field` (valor con máscara; vacío = ''). Valida con `isValidPhone` (vacío o 10 dígitos) en el esquema zod; al editar un valor viejo, `normalizeStoredPhone` lo muestra con máscara solo si tiene 10 dígitos |
| `Tabs<K>` | `tabs: {key,label}[]`, `value`, `onChange`, `label?` | pestañas de una ficha (`.seg`, `role="tablist"`) |
| `EmptyState` | `title`, `body?`, `icon?`, `action?` | sin datos / sin resultados |
| `Spinner` | `label?`, `block?` (centrado) | carga (`role="status"`) |
| `toast` | `toast.success(msg)`, `toast.error(msg)`, `toast.info(msg)` | aviso abajo al centro (errores 6 s, resto 3.5 s). Se monta solo en `<body>` la primera vez; llamable desde `onSuccess` |
| `ClientPicker` | `value` (publicId \| null), `onChange(publicId, client)`, `includeInactive?` (por defecto false), `placeholder?`, `disabled?`, `invalid?`, `aria-label?` | combobox con buscador: `GET /api/v1/clients?search=&includeInactive=` (250 ms entre teclas; mantiene los resultados anteriores mientras busca), opciones "Code · Name" (marca "Inactivo"), teclado ↑/↓/Enter/Escape. Con un valor inicial pide `GET /api/v1/clients/{publicId}` para mostrar la etiqueta. Sin `clients.read`: "Su usuario no puede consultar clientes" (no saca de la pantalla) |
| `CategoryProductPicker` | `value: CategoryProductValue` (`{kind:'category', id}` \| `{kind:'product', publicId}` \| `null` = todos), `onChange(value, detail?)` (`detail.category`/`detail.product` = fila elegida), `categories` (árbol completo: `useProductCategories().data`), `categoriesLoading?`, `label?` (por defecto "Categoría o producto"), `id?`, `disabled?` | un solo combobox con buscador y dos secciones: 'Categorías' (árbol con sangría por nivel y "N productos" contando sus subcategorías —`categoryProductTotals`, como filtra el API—; el texto filtra por nombre o ruta) y 'Productos' (`GET /api/v1/products?search=&activeOnly=true&take=20`, 250 ms entre teclas, "SKU · Nombre"; sin texto no consulta). El valor se ve como píldora ("Categoría"/"Producto" + ruta o "SKU · Nombre", con elipsis) con ✕ para quitarlo; un producto que llega de fuera pide `GET /api/v1/products/{publicId}` para su etiqueta. Teclado ↑/↓ (recorre ambas secciones), Enter, Escape (cierra y devuelve el foco). A ≤ 480 px ocupa todo el ancho; por encima, su desplegable mide 340 px anclado a la izquierda, así que dale un contenedor de al menos 340 px (p. ej. `flex: 1 1 340px`) si un ancestro recorta con `overflow: hidden` (`.pal`). 403 de productos: aviso en su sección. Lógica pura en `categoryTree.ts`: `categoryTree(cats)` (aplanado padre→hijos con `level`; padre ausente = raíz), `filterCategoryTree`, `categoryLabel`, `isCategoryProductValue` (validar lo leído de localStorage), `sameCategoryProduct`, `categoryProductTotals(cats)` (id → productos del subárbol; `productCount` del DTO son solo los directos) |
| `useMediaQuery(q)`, `CARDS_QUERY` | | `true` mientras se cumpla la media query (`'(max-width: 720px)'` = modo tarjetas) |
| `useElementWidth(ref)` (Lote 13) | `ref: RefObject<Element \| null>` → `number` | ancho en px (entero) del elemento, al día con `ResizeObserver`; 0 sin medir (jsdom). La ref apunta a un elemento que se monta con el componente (no condicional). Para decidir por el ancho de un PANEL y no de la ventana: `const ref = useRef<HTMLDivElement>(null); const w = useElementWidth(ref)` → `<div ref={ref}><DataTable forceCards={w > 0 && w < 640} … /></div>` |
| `SplitPane` (Lote 13) | `storageKey` (estable; se guarda en localStorage `teikem.split.<storageKey>`), `defaultRatio?` (0.6), `minRatio?`/`maxRatio?` (0.35 / 0.75), `minPx?: [A, B]` ([420, 320]), `stackBelow?` (900: ventana ≤ ese ancho = una columna), `label?` (nombre accesible de la barra; por defecto `ui.split.resize` "Cambiar el ancho de los paneles"), `className?`, `children: [A, B]` | dos paneles lado a lado (grid `minmax(0, A) 10px minmax(0, 1fr)`, sin `overflow:hidden`: los desplegables salen) con una barra `role="separator"` (`aria-orientation="vertical"`, `aria-valuenow`/`min`/`max` en % del panel A, foco visible). Arrastre con Pointer Events + `setPointerCapture` (también con el dedo: `touch-action:none`), ←/→ 5 %, Home/End a los límites, Enter o doble clic = `defaultRatio` (quita lo guardado). Guarda al soltar y con cada tecla (localStorage en try/catch: si falla o lo guardado no es un número entre 0 y 1, vale el por defecto). La proporción se acota a `minRatio`/`maxRatio` y a que cada panel conserve `minPx`. Una sola columna sin barra (primero A, luego B) con la ventana ≤ `stackBelow` o si el contenedor no alcanza para `minPx[0] + minPx[1]` + la barra. Los hijos no se vuelven a montar al apilarse (el borrador de un formulario sobrevive). Puras en `splitRatio.ts`: `clampSplitRatio(r, anchoÚtil, minPx, min, max)`, `splitBounds`, `readSplitRatio(raw, def)`, `ratioFromPointer`, `splitStorageKey`. `<SplitPane storageKey="pickBatches"><CollectPanel /><PickBatchesPanel /></SplitPane>` |
| `ListPager<T>` (Lote 13) | `page` (base 1), `pageSize`, `total` (0 = no se pinta), `onPage?` (‹ › si hay más de una página), `onPageSize?` (selector "Filas por página" 10/25/50/100 + el tamaño actual), `showRange?` (true), `exportColumns?: DataColumn<T>[]` + `exportRows?()` (Exportar Excel/CSV/PDF con las reglas de exportación de `DataTable`; toast si viene `truncated`), `exportFileName?` (por defecto el título del `Panel`), `onExport?(format)` + `exportCount?` (exportación propia) | el pie de `DataTable` (mismas clases `.dt-pager` y textos `ui.table.*`; `DataTable` lo usa para su propio pie) para listas que no son `DataTable`, p. ej. la lista maestra `.unrow` de Recibo paginada en el servidor. `PAGE_SIZE_OPTIONS`, `pageSizeOptions(size)` en `pageSize.ts`. `<ListPager page={page} pageSize={size} total={data?.total ?? 0} onPage={setPage} onPageSize={(n) => { setSize(n); setPage(1) }} exportColumns={cols} exportRows={() => exportReceipts(query)} />` |

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
  rompe algo: filas con controles editables y estado propio (p. ej. la captura por línea de Ajustes de inventario: al cambiar
  de página se perdería lo escrito), o tablas con su propia carga incremental ("Ver más" de la Actividad reciente). Agrega
  `exportable={false}` en tablas de apoyo dentro de un `Modal` donde el archivo no tenga sentido. Si no, deja la paginación.
- Servidor (endpoints con `skip/take` y `total`): controla `sort`/`onSort`, `page`/`onPage` y el tamaño (`pageSize` +
  `onPageSize`, que vuelve a la página 1), pasa `total`, y pon todo en la clave de la consulta (`placeholderData:
  keepPreviousData` evita el parpadeo). `onSort` recibe `{ id, desc }` (id de la columna). Sin `onPageSize` no hay selector.
  Pasa `exportRows` para que Exportar saque **todo lo filtrado** (no solo la página que llegó): el mismo `query` con
  `fetchAllPages`; si el orden es local (sin `onSort`), DataTable reordena lo exportado como la tabla. En el almacén ya
  existen `exportProducts(query)`, `exportInventoryBalances`, `exportInventoryTransactions`, `exportReceipts`,
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

## Almacén (`src/features/warehouse`, Lote F6)
No es núcleo, pero lo comparten todas las pantallas del almacén, de compras, del cruce de muelle y la consulta de órdenes.
- `api.ts`: un hook por lectura con clave `[ruta, params]` (`useWarehouses(query?)`, `useWarehouse(publicId)`,
  `useWarehouseZones/Bins/Docks(publicId, query?)`, `useProducts`, `useProduct`, `useProductInventoryKpis()` (KPIs de
  'Productos e inventario': `{ activeSkus, totalUnits, belowMin, serial, serialMissing }`, todos con `take=1`, ver abajo),
  `useProductBrands(search?)` (Lote 12: `GET /products/brands` → `string[]` de marcas del tenant; lo invalidan alta y edición de
  producto), `useProductLots/Serials`, `useProductCategories`,
  `useInventoryBalances`, `useInventoryTransactions`, `useInventoryReconciliation`, `useLotGenealogy`, `useSerialTrace`, `useAsns`,
  `useReceipts`, `useReceipt` (Lote 13: `useCreateReceipt`, `useUpdateReceiptHeader` —PATCH del encabezado—,
  `useSaveReceiptLine` y `useConfirmReceipt` dejan en caché la ficha que devuelve el API e invalidan la lista), `useWarehouseTasks`, `usePutawaySuggestions`, `useCycleCounts`, `useCycleCount(id, query?)`,
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
  - `BinPicker` (posiciones de un almacén: `GET /api/v1/warehouses/{publicId}/bins?search=&includeInactive=false&take=50`
    —listado paginado desde el Lote 1—, 250 ms entre teclas —el API compara código de posición, zona, pasillo, rack, nivel y
    posición—, "Código · Zona"). Props: `warehousePublicId` (vacío = deshabilitado con "Elija primero un almacén"; al
    cambiarlo quita el valor con `onChange(null, null)`), `value` (id de la posición, `number | null`), `onChange(binId, fila)`,
    `zoneTypeCodes?` (p. ej. `['STAGING','CROSSDOCK']`: pide las zonas del almacén y manda sus ids como `zoneIds`; ninguna zona
    de esos tipos = sin opciones), `onlyWithStock?`, `suggestedBinIds?` (se piden aparte con `binIds` —mismo texto y filtros—
    y van primero con la marca "Sugerida"), `placeholder?` (p. ej. "Staging por defecto" cuando vacío = lo decide el
    servidor), `disabled?`, `invalid?`, `required?`, `aria-*`, `onBlur?`. Si el lector manda Enter antes de la pausa de
    250 ms, busca de inmediato y elige al llegar la respuesta (código exacto, o el único resultado). Un valor que no está en
    la lista (posición dada de baja, o fuera de la primera página) se muestra pidiéndolo por id (`binIds=<id>`,
    `includeInactive=true`, `take=1`).
    Listado paginado `{ total, skip, take, items }` (take por defecto 100, máx. 200): quien necesite TODAS las posiciones (el
    selector de posiciones del alta de un conteo) las lee con `fetchAllPages` y filtros de servidor (`zoneIds`); nunca asumas
    que una sola llamada trae el almacén completo.
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
- Ficha del almacén (`WarehouseDetailScreen`, Lote 1): Datos con `PostalLocalityPickerInput` + `DerivedLocalityFields`; Zonas con
  filtros Código/Nombre/Tipo en el cliente + "Incluir inactivas", clic en la fila = `ZoneModal`, baja/reactivación como íconos
  (`IconPower`/`IconRotateCcw`); Posiciones paginada en el servidor (`binsQuery` de `warehouseFilters.ts`: Código → `search`,
  Zona → `zoneIds`, Pasillo/Rack/Nivel/Posición con 300 ms de pausa, "Incluir inactivas", "Solo con existencia"; Exportar =
  `exportWarehouseBins(publicId, query)`), columnas Cupo y Ocupación, clic en la fila = `BinModal`, "Asignar cupo"
  (`BinCapacityModal` con la Zona y los textos de ubicación del filtro). Controles sueltos de
  filtro en `filterControls.tsx`: `TextFilter` (`label`, `value`, `onChange`, `placeholder?`) y `ToggleFilter` (`label`,
  `checked`, `onChange`).
- `ProductEditorModal` (`ProductEditorModal.tsx`, maqueta `renderProductModalHtml`): el ÚNICO alta/edición de producto (no hay
  pestaña "Datos"). Props `open`, `product: ProductDetailDto | null` (`null` = "Nuevo producto"), `onClose`, `onCreated?(publicId)`.
  Campos en el orden de la maqueta (SKU —bloqueado al editar— · Unidad, Nombre, Marca · Modelo (Lote 12), Categoría · Rastreo, Dueño
  del inventario, Costo de compra · Precio de venta, Almacén · Posición por defecto, Total —solo lectura, "usa Ajustar abajo"— · Punto
  de reorden). Unidad, Categoría (etiqueta = ruta "Raíz / Hija") y Rastreo son `ComboSelectInput`; Marca es texto libre con
  sugerencias (`<datalist>` de `useProductBrands`) y Modelo texto libre (máx. 100 cada uno; en PATCH solo se mandan si cambiaron: `''`
  = quitar). El Total sale de `useProduct` (se refresca solo tras un ajuste). Al editar, interruptor "Producto activo"
  (deactivate/reactivate al guardar; bloqueado con saldo en mano, con la nota de la maqueta), bloque "Ajustar inventario"
  (`inventory.adjust`; Lote 12: oculto tras "Añadir ajuste"; al abrirlo Cantidad, Motivo con buscador sin los reservados al sistema,
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
  MANUAL_ADJUSTMENT) con `useResolveShortage`; lo comparten la pestaña Faltantes de la ficha de la orden y 'Ajustes de
  inventario'. Props `open`, `onClose`, `po` (mínimo `{ publicId, warehousePublicId, rowVersion? }`: lo cumplen
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
- Ajustes de inventario (`/warehouse/inventory-adjustments`, `InventoryAdjustmentsScreen`; maqueta `ajustesAlmacen()`;
  purchasing.view + PURCHASING): maestro-detalle `.adj-cols`. Izquierda, `usePurchaseOrderShortages()` (compras con recibo
  parcial: `.unrow` con número, chip `fail` "N corto", proveedor y "N línea(s)"); derecha, `usePurchaseOrderShortageLines` de
  la elegida (una sola consulta; se muestran solo las de pendiente > 0) con SKU, Producto, Ordenado, Recibido, Faltante y
  Resolver (Cerrar `inventory.adjust`; Reordenar + `purchasing.manage`; Cant. + Motivo + Ajuste manual + WMS_LOTSERIAL: cada
  botón abre `ResolveShortageModal` con su acción). La elegida va en `?po=<publicId>` (sin él o si ya no está, la primera).
- Recibo (`/warehouse/receipts`, `ReceiptListScreen`; maqueta `recibo()`, Lote 13): maestro-detalle `.rcp-cols`. Estatus
  (colores del catálogo con `StatusChip`): Esperado → Recibiendo ⇄ Discrepancia → Completado / Completado con diferencia →
  Acomodado; los cambia el servidor (`isOpen` = los tres primeros). Pestañas `?tab=asns|putaway`; el recibo elegido va en
  `?receipt=<publicId>` (sin él, el primero; al cambiar de pestaña se quita; `/warehouse/receipts/:publicId` redirige aquí).
  | Pieza | Props / firma | Uso |
  |---|---|---|
  | `ReceiptFilterBar` (`ReceiptFilterBar.tsx`) | `value: ReceiptFilterState`, `onChange`, `statusCodes?` | Almacén (uno: `WarehousePicker`), Estatus y Tipo (`SearchSelect`), Creado, Producto (`ProductMultiFilter` con inactivos) y Diferencia (Faltante/Sobrante/Sin diferencia → `variance[]`); todo al API. No se llama `ReceiptFilters.tsx`: en Windows se confundiría con `receiptFilters.ts` |
  | `ReceiptMasterList` | `title`, `items`, `total`, `selectedId`, `onSelect(publicId)`, `onOpen(publicId)` (doble clic), `q`/`onQ` (`QBox` → `search`), `page`/`pageSize`/`onPage`/`onPageSize`, `exportRows` | filas `.unrow` (número + `StatusChip`; remitente o tipo; transporte · llegada esperada o alta · origen · documento · referencia), `ListPager` con Exportar |
  | `ReceiptDetailPanel` | `publicId`, `onEditHeader()` | "Detalle del recibo · REC-…", "Origen · …", lápiz (modal del encabezado) e Historial; rejilla de líneas, notas y "Confirmar recibo" (deshabilitado con el motivo debajo); confirmado, `ReceiptPutawayTasks` |
  | `ReceiptLinesEditor` + `useReceiptLineRows(receipt, { manual, editable })` | `receipt`, `state` | con documento lo esperado es de solo lectura y no hay fila para añadir; sin documento producto/esperado/recibido, papelera y siempre una fila vacía al final; guarda por fila (salir del campo, Enter o elegir producto) en fila única; `pagination={false}` y `exportable={false}` (rejilla de captura: excepción a la regla de Exportar); tarjetas si el panel mide < 600 px. Sus columnas no dependen de las filas (las celdas leen un contexto): una columna nueva por tecla volvería a montar el campo y perdería el foco |
  | `ReceiptHeaderModal` | `publicId` (null = alta), `preset?` (aviso), `onClose`, `onCreated?(dto)`, `onDeleted?(publicId)` | alta, edición (`PATCH`, solo lo que cambió, `rowVersion` de la caché) y "Borrar recibo" (`canDelete`); confirmado o sin `warehouse.receive`: solo lectura |
  | `ReceiptLineCaptureModal` | `receipt`, `line`, `onClose`, `onSaved?` | lote (fabricación/vencimiento), series (SERIAL: lo recibido = número de series) y posición de la línea |
  | `ReceiptPutawayTasks` | `tasks`, `title?`, `queueLink?` | tareas PUTAWAY con `useTaskRowActions` |
  | `AsnsTab` / `AsnCreateModal` | `onReceive(asn)` / `onClose` | filtros Almacén, Cliente dueño, Referencia (300 ms) y Llegada esperada al API, sin `QBox`; alta con las líneas en una rejilla |
  | `PutawayPendingTab` | `selectedParam`, `onSelect`, `onOpenHeader` | recibos con `phase=PENDING_PUTAWAY` (Estatus limitado a Completado y Completado con diferencia) y sus tareas a la derecha |
  Lógica pura: `receiptFilters.ts` (`receiptListQuery`, `withPinned` —el recibo recién creado primero aunque los filtros lo
  excluyan, hasta cambiar un filtro—, `selectedReceiptId`, `hasDocument`, `receiptOriginText`) y `receiptLineEdit.ts` (filas
  como texto; `onReceivedInput` copia al esperado mientras se teclea si estaba vacío o en 0 y el recibo no tiene documento;
  `linePayload`, `reconcileRows`, `mergeSaved`, `confirmBlockers`, `rowErrorsFromProblem`).
  ```tsx
  const lines = useReceiptLineRows(receipt, { manual: !hasDocument(receipt.header?.origin), editable: isOpen && canReceive })
  <ReceiptLinesEditor receipt={receipt} state={lines} />
  <button className="btn flow block" disabled={confirmBlockers(lines.rows, isOpen, manual) !== null}>…</button>
  ```
- Tareas de almacén (sin pantalla ni ítem de menú: la maqueta no los tiene). Cada tipo vive en la pantalla de su flujo:
  PUTAWAY → Recibo: pestaña 'Acomodo pendiente' (`?tab=putaway`, Lote 13: lista de recibos con acomodo por cerrar y las
  tareas del elegido a la derecha) y 'Tareas de acomodo' debajo del detalle de un recibo confirmado (`ReceiptPutawayTasks`);
  REPLENISH → Recolección y empaque, pestaña 'Reabasto' (`?tab=replenish`, con 'Correr reabasto'); COUNT → Conteo cíclico,
  pestaña 'Tareas de conteo' (se completan desde la ficha del conteo); CROSSDOCK → Cruce de muelle, pestaña 'Tareas de
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
  `/inventory/balances`—; `belowMin`; `serialOnly`); otro clic o "Limpiar" lo quita. El KPI viaja al Reporte de inventario con
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
- Kárdex de movimientos (`/warehouse/kardex`, `InventoryScreen`; maqueta `ledger()`): pestañas Kárdex (primera, sin
  parámetro), Saldos (`?tab=balances`) y Conciliación (`?tab=reconciliation`), con Ajustar/Transferir en la cabecera. Lee la
  URL al montar: `warehousePublicIds=<publicId>` y `product=<publicId>` filtran Saldos o Kárdex (el SKU de cada producto se
  resuelve con su ficha, `useProductsByPublicId`; si la ficha no se puede leer la píldora dice 'Producto no disponible');
  `categoryIds=<id>` filtra Saldos y `types=<InternalCode>` el Kárdex (todos repetibles o separados por comas). Esos filtros
  son solo de la pestaña con que se abrió: al cambiar de pestaña se descartan y la URL queda solo con la pestaña. Es lo que
  usan los enlaces de Pulso (`?tab=balances&categoryIds=…` y `?product=…`, con el almacén elegido para que el destino cuadre
  con la cifra) y los reportes de Productos e inventario. `/warehouse/inventory` redirige aquí (`legacyInventorySearch`). No
  hay parámetro de búsqueda: la búsqueda del Kárdex no compara el documento de origen, así que Actividad reciente no enlaza
  movimientos al Kárdex.
- `productRules.ts`: esquemas zod de peso (`weightKgSchema`), volumen (`volumeM3Schema`) y costo/precio (`moneySchema(t, 'cost' |
  'price')`: mensaje de decimales por campo y tope `< 10¹⁴`) con los mensajes del manual 06, y la cantidad de un ajuste manual
  (`adjustQuantitySchema(t)`: obligatoria, ≠ 0, ≤ 3 decimales; la usan `InventoryAdjustModal` y el bloque de ajuste de
  `ProductEditorModal`); Lote 12: `brandModelSchema(t, 'brand' | 'model')` (opcional, recortado, ≤ 100, mensaje del API) y
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
    disp."), Cantidad, Posición (FEFO primero con `suggestedBinIds`, pista "FEFO: A-01"; vacía = FEFO del servidor), Lote (solo
    LOT/SERIAL; la columna aparece si alguna fila lo necesita), Series (solo SERIAL: botón "Series (n)" con modal), papelera.
    Siempre una fila vacía al final (hasta 100 líneas); "Recolectar (bajar de inventario)" = un solo POST sin las filas vacías;
    "Limpiar" deja una fila vacía (el almacén se queda). Al grabar: toast, líneas limpias y la nueva resaltada en la lista (no navega).
  - `collectForm.ts` (puro): `CollectLine`, `EMPTY_COLLECT_LINE`, `isBlankLine`, `needsTrailingBlank`, `compactPickLines(lines)` →
    `{ lines, indexMap }`, `buildCollectBody(values)` → `{ body, indexMap }`, `remapCollectErrors(err, indexMap)` (`lines[k].x` del
    servidor → `lines.<fila>.x`; `lines[k]` sin campo → la cantidad de la fila), `ownerFilterFor(lines, i)`, `collectSchema(t,
    issueText)` (con `pickLineIssues`, `pickDuplicateAcrossLines`, `firstOtherOwner`), `fefoCandidates`/`fefoBinSuggestions`/
    `fefoAvailable(balances, lotId?)` (réplica de `PickBatchRules.Eligible` sobre `useInventoryBalances({ onlyAvailable: true })`).
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
- Estilos propios en `pulse.css`, todos bajo `.pulse` (`.streamlabel`, `.river`, `.node.flow|.money`, `.node .spark` —barra de
  segmentos `<i style={{ height: '40%' }} />` del color del nodo—, `.pipe`, `.pulse-charts`,
  `.orgbar`, `.orgrow`…): río que envuelve bajo 980 px y una columna a 480 px; grilla `minmax(min(100%, 380px), 1fr)`.
- Indicadores y Gráficos (Fase 10): el editor (`DefinitionEditor`) comparte en "Compartido" con usuarios (`admin.users`) y
  con roles (`useRoles()` de `features/system/api`); cada `ShareDto` lleva `userId` o `roleId`. Solo en gráficos: sin
  selector de módulo (sale de la fuente) y con `<ChartPreview …valores del formulario isMoney />` (`ChartPreviewPanel.tsx`):
  `planChartPreview` + `chartPreviewPoints` (`chartPreview.ts`, lógica pura) sobre `POST /analytics/reports/{fuente}/preview`
  vía `useChartPreview(req)`. La tarjeta de gráfico lleva el rango en línea (`my-date-range`) y un solo switch (`my-pulse`);
  la de indicador sigue con el diálogo `DefinitionRangeModal` y sus dos switches.

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
