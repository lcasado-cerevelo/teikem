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
  reintento una vez. Los 401 de `/auth/login`, `/auth/mfa/verify`, `/auth/reauth`, `/auth/refresh`, `/auth/logout`,
  `/auth/switch-tenant` no refrescan (son la respuesta del propio paso).
- Lecturas con 403 `module_disabled`/`forbidden` → el shell navega a `/module-off` o `/forbidden`. Si una consulta secundaria no
  debe sacar al usuario de la pantalla: `useQuery({ ..., meta: { handleAccessDenied: false } })`. Las mutaciones no se interceptan.
- `createApiClient({ baseUrl, fetch })` solo para pruebas (cliente con la misma política sobre un `fetch` simulado).

## Shell (`src/app`)
- `AppShell`: barra lateral por grupos de la maqueta (`NAV_GROUPS` en `navigation.ts`: Operación, Catálogo, Almacén, Análisis,
  Administración), filtrada por módulos y permisos (un grupo sin entradas visibles no se pinta), colapsable en escritorio y
  cajón bajo 900 px; cabecera con compañía (selector si hay más de una membresía ACTIVE/PLATFORM: `switchableMemberships` en `app/memberships.ts`), idioma, usuario (→ `/account`) y salir.
- Rutas en `src/app/routes.tsx`: `AppRoute = { path, element, perm?, module?, nav? }`, `element` con carga diferida
  `lazy(() => import('../features/<modulo>/<Pantalla>'))` (la pantalla exporta `default`). `appRoutes` = internas (dentro del
  shell, con sesión); `publicRoutes` = sin sesión. `module`/`perm` se aplican solos (pantallas 'Módulo apagado' / 'Sin permiso').
  `nav: { group: 'ops' | 'catalog' | 'warehouse' | 'analytics' | 'admin', labelKey, order? }` la pone en el menú.
  Para agregar una pantalla solo se añade su entrada ahí. `/` es Pulso (`features/analytics/Pulse`): sin `analytics.view` o sin el
  módulo ANALYTICS muestra la bienvenida sin consultar el API (la pantalla de inicio nunca redirige a 'Módulo apagado'). `/account` es `features/account/AccountPage`
  (pestañas Perfil, Contraseña, MFA y Sesiones; `?tab=password|mfa|sessions` abre una pestaña directamente).
- `useSession()` (`app/session.tsx`) → `{ me, isAuthenticated, isLoading, error, tenantId, lang, setLang, logout, switchTenant,
  permissions, modules, reloadMe }`. `me` es `MeDto` de `GET /api/v1/me` (clave de consulta `ME_QUERY_KEY`).
  `setLang('en')` cambia diccionario y `Accept-Language` e invalida las consultas; no desmonta la pantalla ni el menú.
  `switchTenant(id)` → `POST /api/v1/auth/switch-tenant`, limpia la caché y vuelve a `/`. `logout()` revoca el refresh token y va a `/login`.
  `reloadMe()` vuelve a pedir `me` (p. ej. tras activar MFA).

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
- `<Can perm="orders.create" fallback?>…</Can>` (con arreglo exige todos), `useCan('a', 'b')`, `useModule('CATALOG')`,
  `useAccess()` → `{ permissions, modules }` (Sets).
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
  sea exacto. `/status/{entity}/validate` se consulta para avisar si la configuración del tenant tiene errores (no da
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
| `RowAction<T>` | `key`, `label`, `onClick(row)`, `perm?` (guarda de permiso: sin él no se pinta), `visible?(row)` (guarda de estatus/`capabilities`), `disabled?(row)`, `tone?: 'flow' \| 'danger'` | acciones por fila |
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
Playwright (`playwright.config.ts`, proyectos `escritorio` y `movil` = Pixel 7) contra el API real (`API_URL`, por defecto
http://localhost:5000, con db-init hecho) y Vite en :5173: `npm run e2e`. El recorrido de cada lote va en `e2e/loteFN.spec.ts`
(pasos en español, `test.use({ locale: 'es-PR' })`), comprueba `document.documentElement.scrollWidth <= window.innerWidth` en
móvil y no deja cambios persistentes que rompan otra corrida. El CI lo corre en el job del backend después del smoke.

## Antes de devolver una pieza
`npm run check` en verde (tipos generados, tsc, oxlint, vitest, build). Si tocaste el kit: prueba unitaria y línea en este archivo.
