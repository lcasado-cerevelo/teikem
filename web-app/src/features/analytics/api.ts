// Datos de "Pulso del día" (P3): el GET de Pulso y la preferencia de rango de fecha de cada usuario por tarjeta.
// Lote F6: tarjetas de almacén calculadas en cliente (saldo actual, sin rango de fecha) sobre los endpoints del módulo.
// Lote F7A: esas tarjetas aceptan el filtro de almacén y de categoría o producto (`WarehousePulseFilter`).
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSession } from '../../app/session'
import { api, unwrap } from '../../kernel/api/client'
import { ApiError } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { isCategoryProductValue, type CategoryProductValue } from '../../kernel/ui/categoryTree'
import { useProduct, useProductCategories, useWarehouses, type GetQuery } from '../warehouse/api'

export type DateRangeRequest = components['schemas']['DateRangeRequest']
/** Tipo de tarjeta de Pulso: indicador o gráfico. */
export type PulseItemKind = 'indicator' | 'chart'

export const PULSE_QUERY_KEY = ['/api/v1/analytics/pulse'] as const

/** `GET /api/v1/analytics/pulse`: indicadores y gráficos marcados para Pulso, ya calculados por el servidor.
 *  `enabled` en false evita la llamada cuando el usuario no tiene `analytics.view` o el módulo ANALYTICS está apagado
 *  (bienvenida sin datos). Es la pantalla de inicio: un 403 no redirige (evita el ciclo '/' ↔ '/module-off'),
 *  Pulso muestra el error en su lugar. */
export function usePulse(enabled: boolean) {
  return useQuery({
    queryKey: PULSE_QUERY_KEY,
    queryFn: () => unwrap(api.GET('/api/v1/analytics/pulse')),
    enabled,
    meta: { handleAccessDenied: false },
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
    onSuccess: () => qc.invalidateQueries({ queryKey: PULSE_QUERY_KEY }),
  })
}

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
