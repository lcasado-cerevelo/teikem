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

## Shell (`src/app`)
- `AppShell`: barra lateral por grupos (los del menú de la maqueta), colapsable; cabecera con tenant, idioma, usuario, reautenticación.
- Rutas en `src/app/routes.tsx`: `{ path, element, perm?, module? }`. Una ruta por pantalla; carga diferida por módulo.
- `useSession()` → `{ me, tenantId, lang, setLang, logout, switchTenant }`. `me` es `MeDto` del API.

## Autenticación (`src/kernel/auth`)
- `login(email, password, tenantId?)` maneja `ok | mfa_required | tenant_selection` (AuthResultDto).
- `useReauth()` → abre el modal de reautenticación (contraseña o TOTP) y reintenta la acción que exigió AAL2 (403 `aal2_required`).
- Tokens en memoria + `sessionStorage`; refresh con rotación.

## Datos comunes (`src/kernel/catalogs`)
- `useLookups(domain)` → opciones `{ code, label }` de `/api/v1/catalogs/{entity}` (caché 10 min).
- `useStatuses(domain)` → `{ code, label, color, stageKind, isInitial }` de `/api/v1/status/{entity}`.
- `<StatusChip code status={dominio}>`; `<StatusPipeline domain entityType currentCode onTransition>` dibuja pipeline/lateral/terminal y
  ofrece las transiciones que `/api/v1/status/{entity}/validate` permite.
- `<CustomFieldsForm entityType entityId form>` renderiza definiciones de `/api/v1/custom-fields/definitions` y guarda en `/values`.
- `evaluateRule(rule, row)` (`src/kernel/dsl`): misma semántica que el servidor; casos de prueba compartidos.

## Componentes UI (`src/kernel/ui`)
| Componente | Uso |
|---|---|
| `Panel` (`title`, `actions`, `children`) | contenedor estándar de la maqueta (`.pal/.pi/.pb/.ft`) |
| `DataTable<T>` (`columns`, `rows`, `total`, `page`, `onPage`, `sort`, `onSort`, `rowActions`, `empty`) | tabla con orden por columna, paginación del servidor, tarjetas en móvil |
| `Filters` (`children`) + `SelectFilter`, `DateRangeFilter`, `SearchSelect` (múltiple con buscador) | fila de filtros estructurados |
| `QBox` (`value`, `onChange`) + `matchesQ(q, ...texts)` | buscador libre sobre lo que se muestra; se aplica después de los filtros |
| `Chip` (`tone`, `children`) | píldora que nunca envuelve texto |
| `Modal` (`open`, `title`, `onClose`, `footer`) | mismo padding y tipografía del shell |
| `ConfirmDialog` | confirmar acciones destructivas o con guarda de estatus |
| `Form` + `Field` (`name`, `label`, `required`, `help`) + `TextInput`, `NumberInput`, `Select`, `DateInput`, `Toggle`, `TextArea` | formularios con react-hook-form + zod; el error del servidor por campo se muestra bajo el campo |
| `EmptyState`, `Spinner`, `Toast` (`toast.success/error`) | estados |
| `ClientPicker` (`value`, `onChange`, `allowInactive?`) | selector de cliente con buscador (`/api/v1/clients?search=`) |

## Patrones de pantalla (copiar de `src/kernel/ui/templates`)
- `ListScreen.example.tsx`: Panel + Filters + QBox + DataTable + botón "Nuevo" (con `Can`) + modal de alta.
- `DetailScreen.example.tsx`: cabecera con `StatusPipeline` y acciones por `capabilities` del DTO; pestañas; formulario por pestaña.

## Antes de devolver una pieza
`npm run check` en verde (tipos generados, tsc, oxlint, vitest, build). Si tocaste el kit: prueba unitaria y línea en este archivo.
