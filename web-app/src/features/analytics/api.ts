// Datos de "Pulso del día" (P3): el GET de Pulso y la preferencia de rango de fecha de cada usuario por tarjeta.
// Lote F8a (P2): Pulso por paneles; guardar el orden (mío o de la compañía) y volver al de la compañía.
// Lote F6: tarjetas de almacén calculadas en cliente (saldo actual, sin rango de fecha) sobre los endpoints del módulo.
// Lote F7A: esas tarjetas aceptan el filtro de almacén y de categoría o producto (`WarehousePulseFilter`).
import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSession } from '../../app/session'
import { api, unwrap } from '../../kernel/api/client'
import { ApiError } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { isCategoryProductValue, type CategoryProductValue } from '../../kernel/ui/categoryTree'
import { useProduct, useProductCategories, useWarehouses, type GetQuery } from '../warehouse/api'
import type { ChartPreviewRequest } from './chartPreview'
import type { PulseLayoutRequest, PulseScope } from './pulseLayout'

export type DateRangeRequest = components['schemas']['DateRangeRequest']
/** Tipo de tarjeta de Pulso: indicador o gráfico. */
export type PulseItemKind = 'indicator' | 'chart'
type AnalyticsDefinition = components['schemas']['AnalyticsDefinitionDto']
type IndicatorUpsertRequest = components['schemas']['IndicatorUpsertRequest']
type ChartUpsertRequest = components['schemas']['ChartUpsertRequest']

export const PULSE_QUERY_KEY = ['/api/v1/analytics/pulse'] as const
export const PULSE_COMPANY_QUERY_KEY = ['/api/v1/analytics/pulse', 'company'] as const

/** `GET /api/v1/analytics/pulse` (Lote F8a): paneles del usuario (orden efectivo, incluidos los ocultos), indicadores y
 *  gráficos legibles (con su orden y visibilidad), `hasPersonalLayout` y `canOrganizeCompany`. Sin permiso ni módulo
 *  propios: el servidor devuelve solo lo que el usuario puede ver (sin nada, `panels` vacío). Es la pantalla de inicio: un
 *  403 no redirige (evita el ciclo '/' ↔ '/module-off'), Pulso muestra el error en su lugar. */
export function usePulse(enabled = true) {
  return useQuery({
    queryKey: PULSE_QUERY_KEY,
    queryFn: () => unwrap(api.GET('/api/v1/analytics/pulse')),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** `GET /api/v1/analytics/pulse?scope=company`: el Pulso de la compañía sin la capa personal de quien consulta — lo
 *  que usa "Organizar el de la compañía" para partir del estado real de la compañía, no del propio de quien lo abre. */
export function usePulseCompany(enabled: boolean) {
  return useQuery({
    queryKey: PULSE_COMPANY_QUERY_KEY,
    queryFn: () => unwrap(api.GET('/api/v1/analytics/pulse', { params: { query: { scope: 'company' } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** `PUT /api/v1/analytics/pulse/layout?scope=mine|company`: guarda orden y visibilidad de paneles y elementos (lo que no
 *  viene en el cuerpo no se toca). Devuelve el Pulso nuevo, que se pone en caché y se revalida. */
export function useSaveLayout(scope: PulseScope) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: PulseLayoutRequest) => unwrap(api.PUT('/api/v1/analytics/pulse/layout', { params: { query: { scope } }, body })),
    onSuccess: (data) => {
      qc.setQueryData(PULSE_QUERY_KEY, data)
      void qc.invalidateQueries({ queryKey: PULSE_QUERY_KEY })
    },
  })
}

/** `DELETE /api/v1/analytics/pulse/layout/mine`: vuelve al Pulso de la compañía (borra mi orden y mis ocultos; conserva
 *  mis rangos de fecha). */
export function useResetMyLayout() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/v1/analytics/pulse/layout/mine')),
    onSuccess: () => qc.invalidateQueries({ queryKey: PULSE_QUERY_KEY }),
  })
}

/**
 * Mi rango de fecha para un indicador o gráfico (`PUT /api/v1/analytics/{indicators|charts}/{id}/my-date-range`):
 * preferencia por usuario (no edita la definición; basta `analytics.view`). Al guardar se recalcula Pulso.
 */
export function useSetMyDateRange() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ kind, id, body }: { kind: PulseItemKind; id: number; body: DateRangeRequest }) =>
      kind === 'indicator'
        ? unwrap(api.PUT('/api/v1/analytics/indicators/{id}/my-date-range', { params: { path: { id } }, body }))
        : unwrap(api.PUT('/api/v1/analytics/charts/{id}/my-date-range', { params: { path: { id } }, body })),
    onSuccess: (_data, vars) => {
      void qc.invalidateQueries({ queryKey: PULSE_QUERY_KEY })
      void qc.invalidateQueries({ queryKey: vars.kind === 'indicator' ? INDICATORS_QUERY_KEY : CHARTS_QUERY_KEY })
      void qc.invalidateQueries({ queryKey: [vars.kind === 'indicator' ? '/api/v1/analytics/indicators/{id}/value' : '/api/v1/analytics/charts/{id}/data', vars.id] })
    },
  })
}

/** `PUT /api/v1/analytics/{indicators|charts}/{id}/my-pulse`: "Mostrar en Pulso del día" mío (no toca la definición).
 *  Invalida mi Pulso y la lista de Indicadores/Gráficos (P3), que trae `effectiveShowInPulse`. */
export function useSetMyPulse() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ kind, id, showInPulse }: { kind: PulseItemKind; id: number; showInPulse: boolean }) =>
      kind === 'indicator'
        ? unwrap(api.PUT('/api/v1/analytics/indicators/{id}/my-pulse', { params: { path: { id } }, body: { showInPulse } }))
        : unwrap(api.PUT('/api/v1/analytics/charts/{id}/my-pulse', { params: { path: { id } }, body: { showInPulse } })),
    onSuccess: (_data, vars) => {
      void qc.invalidateQueries({ queryKey: PULSE_QUERY_KEY })
      void qc.invalidateQueries({ queryKey: vars.kind === 'indicator' ? INDICATORS_QUERY_KEY : CHARTS_QUERY_KEY })
    },
  })
}

// ---------------------------------------------------------------------------------------------------------------------
// Indicadores y Gráficos (Lote F8a, P3): lista, fuentes de datos, valor/datos por tarjeta y alta/edición/baja de la
// definición. La lista ya llega filtrada por lo que el usuario puede leer (§2.2 del plan); la pantalla no re-filtra.
// ---------------------------------------------------------------------------------------------------------------------

export const INDICATORS_QUERY_KEY = ['/api/v1/analytics/indicators'] as const
export const CHARTS_QUERY_KEY = ['/api/v1/analytics/charts'] as const

export function useIndicators() {
  return useQuery({ queryKey: INDICATORS_QUERY_KEY, queryFn: () => unwrap(api.GET('/api/v1/analytics/indicators')) })
}

export function useCharts() {
  return useQuery({ queryKey: CHARTS_QUERY_KEY, queryFn: () => unwrap(api.GET('/api/v1/analytics/charts')) })
}

/** `GET /api/v1/analytics/data-sources`: catálogo de fuentes (fuentes y sus campos casi no cambian; caché larga). */
export function useDataSources() {
  return useQuery({
    queryKey: ['/api/v1/analytics/data-sources'],
    queryFn: () => unwrap(api.GET('/api/v1/analytics/data-sources')),
    staleTime: 5 * 60 * 1000,
  })
}

/** Valor actual de un indicador (tarjeta, spinner propio). */
export function useIndicatorValue(id: number | null | undefined) {
  return useQuery({
    queryKey: ['/api/v1/analytics/indicators/{id}/value', id],
    queryFn: () => unwrap(api.GET('/api/v1/analytics/indicators/{id}/value', { params: { path: { id: id as number } } })),
    enabled: id != null,
  })
}

/** Datos de un gráfico (tarjeta, spinner propio). */
export function useChartData(id: number | null | undefined) {
  return useQuery({
    queryKey: ['/api/v1/analytics/charts/{id}/data', id],
    queryFn: () => unwrap(api.GET('/api/v1/analytics/charts/{id}/data', { params: { path: { id: id as number } } })),
    enabled: id != null,
  })
}

/** También invalida el valor/los datos ya calculados de ESE elemento (`id`): sin esto, editar o cambiar de rango deja
 *  la tarjeta con el valor calculado con los datos o el rango anteriores hasta que algo más la refresque. */
function invalidateDefinitions(qc: ReturnType<typeof useQueryClient>, kind: PulseItemKind, id?: number | null) {
  void qc.invalidateQueries({ queryKey: kind === 'indicator' ? INDICATORS_QUERY_KEY : CHARTS_QUERY_KEY })
  void qc.invalidateQueries({ queryKey: PULSE_QUERY_KEY })
  if (id != null) void qc.invalidateQueries({ queryKey: [kind === 'indicator' ? '/api/v1/analytics/indicators/{id}/value' : '/api/v1/analytics/charts/{id}/data', id] })
}

/** Alta (`POST indicators`) o edición (`PUT indicators/{id}`) de un indicador. */
export function useSaveIndicator() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: number | null; body: IndicatorUpsertRequest }) =>
      id != null
        ? unwrap(api.PUT('/api/v1/analytics/indicators/{id}', { params: { path: { id } }, body }))
        : unwrap(api.POST('/api/v1/analytics/indicators', { body })),
    onSuccess: (data, vars) => invalidateDefinitions(qc, 'indicator', vars.id ?? data.id),
  })
}

/** Alta (`POST charts`) o edición (`PUT charts/{id}`) de un gráfico. */
export function useSaveChart() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: number | null; body: ChartUpsertRequest }) =>
      id != null
        ? unwrap(api.PUT('/api/v1/analytics/charts/{id}', { params: { path: { id } }, body }))
        : unwrap(api.POST('/api/v1/analytics/charts', { body })),
    onSuccess: (data, vars) => invalidateDefinitions(qc, 'chart', vars.id ?? data.id),
  })
}

export function useDeleteIndicator() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/v1/analytics/indicators/{id}', { params: { path: { id } } })),
    onSuccess: (_data, id) => invalidateDefinitions(qc, 'indicator', id),
  })
}

export function useDeleteChart() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/v1/analytics/charts/{id}', { params: { path: { id } } })),
    onSuccess: (_data, id) => invalidateDefinitions(qc, 'chart', id),
  })
}

/** Usuarios de la compañía para el buscador de "Personas específicas" del editor (`admin.users`; 403 no saca de la
 *  pantalla, el editor ofrece solo "Toda la compañía"/"Solo yo"). */
export function useAnalyticsShareUsers(enabled: boolean) {
  return useQuery({
    queryKey: ['/api/v1/users'],
    queryFn: () => unwrap(api.GET('/api/v1/users')),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/**
 * Vista previa del editor de gráficos (Fase 10b): `POST /api/v1/analytics/reports/{fuente}/preview` con la petición
 * que arma `planChartPreview` (`chartPreview.ts`); `null` = configuración incompleta, no consulta. Mantiene el resultado
 * anterior mientras recalcula (sin parpadeo) y un 403 no saca al usuario del editor.
 */
export function useChartPreview(req: ChartPreviewRequest | null) {
  return useQuery({
    queryKey: ['/api/v1/analytics/reports/{baseEntityType}/preview', req?.baseEntityType ?? null, req?.query ?? null, req?.body ?? null],
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/analytics/reports/{baseEntityType}/preview', {
          params: { path: { baseEntityType: (req as ChartPreviewRequest).baseEntityType }, query: (req as ChartPreviewRequest).query },
          body: (req as ChartPreviewRequest).body,
        }),
      ),
    enabled: req != null,
    placeholderData: keepPreviousData,
    staleTime: 30 * 1000,
    meta: { handleAccessDenied: false },
  })
}

export type { AnalyticsDefinition }

// ---------------------------------------------------------------------------------------------------------------------
// Tarjetas de almacén (Lote F6). No son indicadores del motor de analítica: se calculan en cliente con los totales que
// ya devuelven los endpoints de almacén (`take=1` para no paginar todo). Son saldo actual, no actividad de un período:
// no llevan rango de fecha. Las claves son las mismas `[ruta, params]` que usa `features/warehouse/api.ts`, así que las
// mutaciones del almacén (que invalidan por prefijo de ruta) las refrescan solas. Pulso es la pantalla de inicio: un 403
// no redirige (`handleAccessDenied: false`); la tarjeta muestra '—'.
// ---------------------------------------------------------------------------------------------------------------------

/** Tipos de tarea de almacén con tarjeta propia en Pulso (códigos de WarehouseTaskType). */
export const PULSE_TASK_TYPES = ['PUTAWAY', 'REPLENISH', 'COUNT', 'CROSSDOCK'] as const
export type PulseTaskType = (typeof PULSE_TASK_TYPES)[number]

const NO_REDIRECT = { handleAccessDenied: false } as const

/** Filtro del panel 'Almacén' (Lote F7A): almacén (null = todos) y categoría o producto (null = todos). */
export interface WarehousePulseFilter {
  warehousePublicId: string | null
  item: CategoryProductValue
}

export const NO_WAREHOUSE_FILTER: WarehousePulseFilter = { warehousePublicId: null, item: null }

/** Productos que se revisan para decidir si EL producto elegido está bajo mínimo (búsqueda por su SKU). */
const BELOW_MIN_PRODUCT_TAKE = 100

/**
 * Datos del panel 'Almacén' de Pulso (`inventory.view` + WMS_LOTSERIAL; `enabled` en false no consulta nada). El almacén
 * del filtro aplica a las seis tarjetas; la categoría o el producto solo a las tres de saldo:
 * - `balances`: `GET /api/v1/inventory/balances?includeZero=false&take=1[&warehousePublicIds=][&categoryIds=|&productPublicIds=]`
 *   → `totalOnHand` / `totalAvailable`.
 * - `belowMin`: `GET /api/v1/products?belowMin=true&take=1[&warehousePublicId=][&categoryIds=]` → `total`. Con un producto el
 *   endpoint no filtra por publicId: se busca por su SKU (`productSku`, de la ficha) con `belowMin=true` y se mira si el
 *   producto viene en la respuesta (`productBelowMin`: true/false; undefined mientras no se sepa).
 * - `openReceipts`: `GET /api/v1/receipts?status=OPEN&take=1[&warehousePublicId=]` → `total`.
 * - `pendingTasks`: `GET /api/v1/warehouse-tasks?includeClosed=false&take=1[&warehousePublicId=]` → `total`; `tasksByType`:
 *   lo mismo con `types=<tipo>` por cada tipo de `PULSE_TASK_TYPES` (en ese orden).
 * - `openCounts`: `GET /api/v1/cycle-counts?status=OPEN[&warehousePublicIds=]` → largo del arreglo.
 */
export function useWarehousePulse(enabled: boolean, filter: WarehousePulseFilter = NO_WAREHOUSE_FILTER, productSku?: string | null) {
  const wh = filter.warehousePublicId
  const item = filter.item

  const balancesQuery: GetQuery<'/api/v1/inventory/balances'> = { includeZero: false, take: 1 }
  if (wh) balancesQuery.warehousePublicIds = [wh]
  if (item?.kind === 'category') balancesQuery.categoryIds = [item.id]
  if (item?.kind === 'product') balancesQuery.productPublicIds = [item.publicId]
  const balances = useQuery({
    queryKey: ['/api/v1/inventory/balances', balancesQuery],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/balances', { params: { query: balancesQuery } })),
    enabled,
    meta: NO_REDIRECT,
  })

  const isProduct = item?.kind === 'product'
  const belowMinQuery: GetQuery<'/api/v1/products'> = { belowMin: true, take: isProduct ? BELOW_MIN_PRODUCT_TAKE : 1 }
  if (wh) belowMinQuery.warehousePublicId = wh
  if (item?.kind === 'category') belowMinQuery.categoryIds = [item.id]
  if (isProduct && productSku) belowMinQuery.search = productSku
  const belowMin = useQuery({
    queryKey: ['/api/v1/products', belowMinQuery],
    queryFn: () => unwrap(api.GET('/api/v1/products', { params: { query: belowMinQuery } })),
    enabled: enabled && (!isProduct || Boolean(productSku)),
    meta: NO_REDIRECT,
  })
  const productBelowMin =
    isProduct && belowMin.data ? (belowMin.data.items ?? []).some((p) => p.publicId === item.publicId) : undefined

  const receiptsQuery: GetQuery<'/api/v1/receipts'> = { status: ['OPEN'], take: 1 }
  if (wh) receiptsQuery.warehousePublicId = wh
  const openReceipts = useQuery({
    queryKey: ['/api/v1/receipts', receiptsQuery],
    queryFn: () => unwrap(api.GET('/api/v1/receipts', { params: { query: receiptsQuery } })),
    enabled,
    meta: NO_REDIRECT,
  })

  const tasksQuery: GetQuery<'/api/v1/warehouse-tasks'> = { includeClosed: false, take: 1 }
  if (wh) tasksQuery.warehousePublicId = wh
  const pendingTasks = useQuery({
    queryKey: ['/api/v1/warehouse-tasks', tasksQuery],
    queryFn: () => unwrap(api.GET('/api/v1/warehouse-tasks', { params: { query: tasksQuery } })),
    enabled,
    meta: NO_REDIRECT,
  })

  const tasksByType = useQueries({
    queries: PULSE_TASK_TYPES.map((type) => {
      const query: GetQuery<'/api/v1/warehouse-tasks'> = { ...tasksQuery, types: [type] }
      return {
        queryKey: ['/api/v1/warehouse-tasks', query],
        queryFn: () => unwrap(api.GET('/api/v1/warehouse-tasks', { params: { query } })),
        enabled,
        meta: NO_REDIRECT,
      }
    }),
  })

  const countsQuery: GetQuery<'/api/v1/cycle-counts'> = { status: ['OPEN'] }
  if (wh) countsQuery.warehousePublicIds = [wh]
  const openCounts = useQuery({
    queryKey: ['/api/v1/cycle-counts', countsQuery],
    queryFn: () => unwrap(api.GET('/api/v1/cycle-counts', { params: { query: countsQuery } })),
    enabled,
    meta: NO_REDIRECT,
  })

  return { balances, belowMin, productBelowMin, openReceipts, pendingTasks, tasksByType, openCounts }
}

// ---------------------------------------------------------------------------------------------------------------------
// Última selección del filtro del panel 'Almacén' (Lote F7A): localStorage con clave por compañía y usuario. Lectura
// tolerante: un valor corrupto, de otra forma o un localStorage inaccesible equivalen a "sin filtro".
// ---------------------------------------------------------------------------------------------------------------------

/** Clave de localStorage de la selección; null si aún no se conoce la compañía o el usuario (no se guarda nada). */
export function warehouseFilterStorageKey(tenantId: number | null | undefined, userId: number | null | undefined): string | null {
  if (tenantId == null || userId == null) return null
  return `teikem.pulse.warehouseFilter.${tenantId}.${userId}`
}

/** Selección guardada (o sin filtro si no hay, está corrupta o no se puede leer). */
export function readWarehouseFilter(key: string | null): WarehousePulseFilter {
  if (!key) return NO_WAREHOUSE_FILTER
  try {
    const raw = window.localStorage.getItem(key)
    if (!raw) return NO_WAREHOUSE_FILTER
    const parsed = JSON.parse(raw) as Record<string, unknown> | null
    if (!parsed || typeof parsed !== 'object') return NO_WAREHOUSE_FILTER
    const wh = typeof parsed.warehousePublicId === 'string' && parsed.warehousePublicId ? parsed.warehousePublicId : null
    const item = isCategoryProductValue(parsed.item ?? null) ? ((parsed.item ?? null) as CategoryProductValue) : null
    return { warehousePublicId: wh, item }
  } catch {
    return NO_WAREHOUSE_FILTER
  }
}

/** Guarda la selección; sin filtro borra la entrada. Errores de almacenamiento (cuota, modo privado) se ignoran. */
export function writeWarehouseFilter(key: string | null, filter: WarehousePulseFilter): void {
  if (!key) return
  try {
    if (!filter.warehousePublicId && !filter.item) window.localStorage.removeItem(key)
    else window.localStorage.setItem(key, JSON.stringify(filter))
  } catch {
    // sin persistencia: el filtro sigue funcionando en la sesión
  }
}

/** Datos contra los que se valida la selección guardada (undefined = aún no llegan: no se limpia nada). */
export interface WarehouseFilterCatalogs {
  warehouses?: readonly { publicId?: string; isActive?: boolean }[]
  categories?: readonly { id?: number; isActive?: boolean }[]
  /** La ficha del producto guardado respondió 404 o el producto está dado de baja. */
  productGone?: boolean
}

/**
 * Quita de la selección lo que ya no existe (almacén o categoría que no está entre los activos, producto 404 o inactivo).
 * Devuelve el mismo objeto si no cambia nada.
 */
export function sanitizeWarehouseFilter(filter: WarehousePulseFilter, catalogs: WarehouseFilterCatalogs): WarehousePulseFilter {
  let next = filter
  const wh = filter.warehousePublicId
  if (wh && catalogs.warehouses && !catalogs.warehouses.some((w) => w.publicId === wh && w.isActive !== false))
    next = { ...next, warehousePublicId: null }
  const item = filter.item
  if (item?.kind === 'category' && catalogs.categories && !catalogs.categories.some((c) => c.id === item.id && c.isActive !== false))
    next = { ...next, item: null }
  if (item?.kind === 'product' && catalogs.productGone) next = { ...next, item: null }
  return next
}

/**
 * Estado del filtro del panel 'Almacén' con persistencia por compañía y usuario, y los datos que el panel reutiliza:
 * almacenes activos, árbol de categorías, la categoría elegida y la ficha del producto elegido. Lo guardado que ya no
 * existe se descarta al llegar los catálogos (se deriva al pintar y se borra de localStorage), sin mostrar error.
 */
export function useWarehouseFilter() {
  const { me, tenantId } = useSession()
  const storageKey = warehouseFilterStorageKey(tenantId, me?.userId)
  const [state, setState] = useState(() => ({ key: storageKey, filter: readWarehouseFilter(storageKey) }))
  // Otra compañía u otro usuario sin desmontar: se usa la selección guardada con esa clave.
  const saved = useMemo(() => (state.key === storageKey ? state.filter : readWarehouseFilter(storageKey)), [state, storageKey])

  const warehouses = useWarehouses({ includeInactive: false }, NO_REDIRECT)
  const categories = useProductCategories({ includeInactive: false }, NO_REDIRECT)
  const savedProductId = saved.item?.kind === 'product' ? saved.item.publicId : null
  const product = useProduct(savedProductId, NO_REDIRECT)
  const productGone =
    (product.error instanceof ApiError && product.error.status === 404) || product.data?.product?.isActive === false

  const filter = useMemo(
    () => sanitizeWarehouseFilter(saved, { warehouses: warehouses.data, categories: categories.data, productGone }),
    [saved, warehouses.data, categories.data, productGone],
  )

  // Lo descartado deja de recordarse (localStorage es externo: se sincroniza en un efecto).
  useEffect(() => {
    if (filter !== saved) writeWarehouseFilter(storageKey, filter)
  }, [filter, saved, storageKey])

  const setFilter = useCallback(
    (next: WarehousePulseFilter) => {
      setState({ key: storageKey, filter: next })
      writeWarehouseFilter(storageKey, next)
    },
    [storageKey],
  )

  const item = filter.item
  const category = item?.kind === 'category' ? categories.data?.find((c) => c.id === item.id) : undefined

  return {
    filter,
    setFilter,
    warehouses,
    categories,
    category,
    product: item?.kind === 'product' ? product.data?.product : undefined,
    productError: product.isError,
  }
}
