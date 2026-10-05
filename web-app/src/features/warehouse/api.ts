// Datos del módulo de almacén (manual 06) y de la consulta de órdenes de solo lectura (Lote F6).
// Lecturas: `useQuery` con clave `[ruta, params]` (la ruta tal como está en schema.d.ts). Escrituras: `useMutation` que
// invalida la lista de su entidad (prefijo `[ruta]`) y, si cambia saldos, las consultas de inventario que dependen de ellos.
// Permisos y módulos (los aplica el API; aquí solo se documentan): lecturas con `inventory.view` + WMS_LOTSERIAL (compras:
// `purchasing.view` + PURCHASING; citas y cruce de muelle: `inventory.view` + CROSSDOCK; órdenes: `orders.view` + LTL_GROUND).
// Lote 11: cupo máximo en bloque (`useSetBinsCapacity`) y su vista previa (`useBinCapacityPreview`), al final del archivo.
import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import type { components, paths } from '../../kernel/api/schema'
import {
  BIN_CAPACITY_EXACT_LIMIT,
  capacityPreviewQuery,
  hasTextFilter,
  scopeReady,
  zonesWithoutCapacity,
  type BinCapacityRequest,
  type BinCapacityScope,
} from './binCapacity'

type Schemas = components['schemas']

/** Parámetros de consulta (query string) del GET de una ruta del API, tal como los declara el esquema generado. */
export type GetQuery<P extends keyof paths> = paths[P] extends { get: { parameters: { query?: infer Q } } } ? NonNullable<Q> : never

/** Opciones comunes de las lecturas. `handleAccessDenied: false` para consultas secundarias que no deben sacar al
 *  usuario de la pantalla ante un 403 (KIT.md). */
export interface WarehouseQueryOptions {
  enabled?: boolean
  handleAccessDenied?: boolean
}

function meta(options?: WarehouseQueryOptions) {
  return options?.handleAccessDenied === false ? { handleAccessDenied: false } : undefined
}

// ---------------------------------------------------------------------------------------------------------------------
// Tipos de DTO y de requests (del cliente generado; nunca a mano)
// ---------------------------------------------------------------------------------------------------------------------
export type WarehouseDto = Schemas['WarehouseDto']
export type WarehouseDetailDto = Schemas['WarehouseDetailDto']
export type WarehouseZoneDto = Schemas['WarehouseZoneDto']
export type WarehouseBinDto = Schemas['WarehouseBinDto']
export type WarehouseDockDto = Schemas['WarehouseDockDto']
export type ProductListItemDto = Schemas['ProductListItemDto']
export type ProductDetailDto = Schemas['ProductDetailDto']
export type ProductCategoryDto = Schemas['ProductCategoryDto']
export type LotDto = Schemas['LotDto']
export type SerialDto = Schemas['SerialDto']
export type BalanceDto = Schemas['BalanceDto']
export type BalancePageDto = Schemas['BalancePageDto']
export type KardexPageDto = Schemas['KardexPageDto']
export type KardexRowDto = Schemas['KardexRowDto']
export type ReconciliationDto = Schemas['ReconciliationDto']
export type GenealogyDto = Schemas['GenealogyDto']
export type SerialTraceDto = Schemas['SerialTraceDto']
export type AsnDto = Schemas['AsnDto']
export type ReceiptListItemDto = Schemas['ReceiptListItemDto']
export type ReceiptDetailDto = Schemas['ReceiptDetailDto']
export type ReceiptTargetSuggestionDto = Schemas['ReceiptTargetSuggestionDto']
export type WarehouseTaskDto = Schemas['WarehouseTaskDto']
export type PutawaySuggestionDto = Schemas['PutawaySuggestionDto']
export type CycleCountDto = Schemas['CycleCountDto']
export type CycleCountDetailDto = Schemas['CycleCountDetailDto']
export type CycleCountLineDto = Schemas['CycleCountLineDto']
export type CycleCountPageDto = Schemas['CycleCountPageDto']
export type CycleCountChangesPreviewDto = Schemas['CycleCountChangesPreviewDto']
export type CycleCountFromChangesRequest = Schemas['CycleCountFromChangesRequest']
export type CycleCountReviewItemDto = Schemas['CycleCountReviewItemDto']
export type CycleCountReviewPageDto = Schemas['CycleCountReviewPageDto']
export type ReconcilePreviewDto = Schemas['ReconcilePreviewDto']
export type ReconcilePreviewLineDto = Schemas['ReconcilePreviewLineDto']
export type CycleCountReconcileMatchingResultDto = Schemas['CycleCountReconcileMatchingResultDto']
export type CycleCountSkippedItemDto = Schemas['CycleCountSkippedItemDto']
export type CountReconcileMatchingRequest = Schemas['CountReconcileMatchingRequest']
export type PickBatchDto = Schemas['PickBatchDto']
export type SupplierDto = Schemas['SupplierDto']
export type PurchaseOrderDto = Schemas['PurchaseOrderDto']
export type PoShortageSummaryDto = Schemas['PoShortageSummaryDto']
export type ShortageLineDto = Schemas['ShortageLineDto']
export type ShortageResolveResultDto = Schemas['ShortageResolveResultDto']
export type DockAppointmentDto = Schemas['DockAppointmentDto']
export type CrossDockPlanDto = Schemas['CrossDockPlanDto']
export type CrossDockCandidateDto = Schemas['CrossDockCandidateDto']
export type OrderListItemDto = Schemas['OrderListItemDto']
export type OrderDetailDto = Schemas['OrderDetailDto']

/** "Code · Name" de un almacén (opciones de WarehousePicker y columnas de las pantallas). */
export function warehouseLabel(w: { code?: string | null; name?: string | null } | null | undefined): string {
  if (!w) return ''
  return [w.code, w.name].filter(Boolean).join(' · ')
}

/** "Código · Zona" de una posición (opciones de BinPicker). */
export function binLabel(b: { code?: string | null; zoneCode?: string | null } | null | undefined): string {
  if (!b) return ''
  return [b.code, b.zoneCode].filter(Boolean).join(' · ')
}

/** "SKU · Nombre" de un producto (opciones de ProductPicker). */
export function productLabel(p: { sku?: string | null; name?: string | null } | null | undefined): string {
  if (!p) return ''
  return [p.sku, p.name].filter(Boolean).join(' · ')
}

// ---------------------------------------------------------------------------------------------------------------------
// Claves raíz (prefijos) para invalidar: una consulta `[ruta, params]` se invalida con `[ruta]`.
// ---------------------------------------------------------------------------------------------------------------------
export const warehouseKeys = {
  warehouses: ['/api/v1/warehouses'],
  warehouse: ['/api/v1/warehouses/{publicId}'],
  zones: ['/api/v1/warehouses/{publicId}/zones'],
  bins: ['/api/v1/warehouses/{publicId}/bins'],
  docks: ['/api/v1/warehouses/{publicId}/docks'],
  products: ['/api/v1/products'],
  product: ['/api/v1/products/{publicId}'],
  productLots: ['/api/v1/products/{publicId}/lots'],
  productSerials: ['/api/v1/products/{publicId}/serials'],
  productBrands: ['/api/v1/products/brands'],
  productCategories: ['/api/v1/product-categories'],
  balances: ['/api/v1/inventory/balances'],
  transactions: ['/api/v1/inventory/transactions'],
  reconciliation: ['/api/v1/inventory/reconciliation'],
  // Lote 14: resumen y detalle del Kárdex, dueños, posiciones entre almacenes, descuadres y estado de la conciliación
  transactionSummary: ['/api/v1/inventory/transactions/summary'],
  transaction: ['/api/v1/inventory/transactions/{id}'],
  owners: ['/api/v1/inventory/owners'],
  binSearch: ['/api/v1/warehouses/bins/search'],
  discrepancies: ['/api/v1/inventory/discrepancies'],
  discrepancy: ['/api/v1/inventory/discrepancies/{publicId}'],
  reconciliationStatus: ['/api/v1/inventory/reconciliation/status'],
  // Lote 15: franja "Almacén hoy" del Pulso (recibido, salida y conteos con diferencia por día)
  pulseDays: ['/api/v1/inventory/pulse/days'],
  genealogy: ['/api/v1/inventory/lots/{lotId}/genealogy'],
  serialTrace: ['/api/v1/inventory/serials/trace'],
  asns: ['/api/v1/asns'],
  receipts: ['/api/v1/receipts'],
  receipt: ['/api/v1/receipts/{publicId}'],
  // Lote 16: posiciones destino sugeridas de una línea de un recibo directo
  receiptTargetSuggestions: ['/api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions'],
  tasks: ['/api/v1/warehouse-tasks'],
  putawaySuggestions: ['/api/v1/warehouse-tasks/putaway-suggestions'],
  cycleCounts: ['/api/v1/cycle-counts'],
  cycleCount: ['/api/v1/cycle-counts/{id}'],
  // Lote 14: lista paginada con el total y vista previa de "lo cambiado"
  cycleCountsPage: ['/api/v1/cycle-counts/page'],
  changesPreview: ['/api/v1/cycle-counts/changes-preview'],
  // Lote F12 (conteo por producto): "Por revisar" y la vista previa de reconciliar (miden contra la existencia ACTUAL)
  cycleCountReview: ['/api/v1/cycle-counts/review'],
  reconcilePreview: ['/api/v1/cycle-counts/{id}/reconcile-preview'],
  pickBatches: ['/api/v1/pick-batches'],
  pickBatch: ['/api/v1/pick-batches/{publicId}'],
  suppliers: ['/api/v1/suppliers'],
  purchaseOrders: ['/api/v1/purchase-orders'],
  purchaseOrder: ['/api/v1/purchase-orders/{publicId}'],
  purchaseOrderShortages: ['/api/v1/purchase-orders/shortages'],
  purchaseOrderShortageLines: ['/api/v1/purchase-orders/{publicId}/shortage-lines'],
  dockAppointments: ['/api/v1/dock-appointments'],
  crossDockPlans: ['/api/v1/cross-dock-plans'],
  crossDockPlan: ['/api/v1/cross-dock-plans/{id}'],
  crossDockCandidates: ['/api/v1/cross-dock-plans/{id}/candidates'],
  orders: ['/api/v1/orders'],
  order: ['/api/v1/orders/{publicId}'],
  orderLookup: ['/api/v1/orders/lookup'],
} as const

type KeyName = keyof typeof warehouseKeys

/** Invalida por prefijo las consultas indicadas. */
function invalidate(qc: QueryClient, ...names: KeyName[]) {
  return Promise.all(names.map((n) => qc.invalidateQueries({ queryKey: warehouseKeys[n] })))
}

/** Consultas que cambian cuando se mueve inventario (saldos, Kárdex, existencias en productos/almacenes/posiciones). */
const STOCK: KeyName[] = [
  'balances',
  'transactions',
  'reconciliation',
  'products',
  'product',
  'warehouses',
  'warehouse',
  'bins',
  'productLots',
  'productSerials',
  // Lote 14: el resumen del Kárdex y los descuadres (la revisión automática puede abrir o cerrar uno tras el movimiento)
  'transactionSummary',
  'discrepancies',
  'discrepancy',
  'reconciliationStatus',
  // Lote 15: la franja "Almacén hoy" del Pulso cuenta recepciones, salidas y conteos cerrados
  'pulseDays',
]

// =====================================================================================================================
// Almacenes, zonas, posiciones y muelles
// =====================================================================================================================

/** `GET /api/v1/warehouses?includeInactive=` (lista completa, sin paginar). */
export function useWarehouses(query: GetQuery<'/api/v1/warehouses'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.warehouses[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/warehouses', { params: { query } })),
    enabled: options?.enabled ?? true,
    meta: meta(options),
  })
}

/** `GET /api/v1/warehouses/{publicId}` (ficha). */
export function useWarehouse(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.warehouse[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/warehouses/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/warehouses/{publicId}/zones?includeInactive=`. */
export function useWarehouseZones(
  publicId: string | null | undefined,
  query: GetQuery<'/api/v1/warehouses/{publicId}/zones'> = {},
  options?: WarehouseQueryOptions,
) {
  return useQuery({
    queryKey: [warehouseKeys.zones[0], { publicId, ...query }],
    queryFn: () => unwrap(api.GET('/api/v1/warehouses/{publicId}/zones', { params: { path: { publicId: publicId ?? '' }, query } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/warehouses/{publicId}/bins?zoneId=&search=&includeInactive=&onlyWithStock=`. */
export function useWarehouseBins(
  publicId: string | null | undefined,
  query: GetQuery<'/api/v1/warehouses/{publicId}/bins'> = {},
  options?: WarehouseQueryOptions,
) {
  return useQuery({
    queryKey: [warehouseKeys.bins[0], { publicId, ...query }],
    queryFn: () => unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path: { publicId: publicId ?? '' }, query } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Lote F16: una tanda de `GET /api/v1/warehouses/{publicId}/bins` fuera de React (take ≤ 200), p. ej. las etiquetas de posición. */
export const fetchWarehouseBins = (publicId: string, query: GetQuery<'/api/v1/warehouses/{publicId}/bins'>, signal?: AbortSignal) =>
  unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path: { publicId }, query }, signal }))

/** `GET /api/v1/warehouses/{publicId}/docks?includeInactive=`. */
export function useWarehouseDocks(
  publicId: string | null | undefined,
  query: GetQuery<'/api/v1/warehouses/{publicId}/docks'> = {},
  options?: WarehouseQueryOptions,
) {
  return useQuery({
    queryKey: [warehouseKeys.docks[0], { publicId, ...query }],
    queryFn: () => unwrap(api.GET('/api/v1/warehouses/{publicId}/docks', { params: { path: { publicId: publicId ?? '' }, query } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `POST /api/v1/warehouses` (`warehouse.manage`). */
export function useCreateWarehouse() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['WarehouseCreateRequest']) => unwrap(api.POST('/api/v1/warehouses', { body })),
    onSuccess: () => invalidate(qc, 'warehouses'),
  })
}

/** `PATCH /api/v1/warehouses/{publicId}` (`warehouse.manage`; el código no cambia). */
export function useUpdateWarehouse() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: Schemas['WarehousePatchRequest'] }) =>
      unwrap(api.PATCH('/api/v1/warehouses/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: () => invalidate(qc, 'warehouses', 'warehouse'),
  })
}

/** `POST /api/v1/warehouses/{publicId}/deactivate` (409 si tiene inventario o documentos abiertos). */
export function useDeactivateWarehouse() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body = {} }: { publicId: string; body?: Schemas['WarehouseDeactivateRequest'] }) =>
      unwrap(api.POST('/api/v1/warehouses/{publicId}/deactivate', { params: { path: { publicId } }, body })),
    onSuccess: () => invalidate(qc, 'warehouses', 'warehouse'),
  })
}

/** Alta/edición/baja/reactivación de zonas del almacén (`warehouse.manage`). */
export function useSaveWarehouseZone() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { publicId: string; action: 'create'; body: Schemas['WarehouseZoneRequest'] }
        | { publicId: string; action: 'update'; zoneId: number; body: Schemas['WarehouseZonePatchRequest'] }
        | { publicId: string; action: 'deactivate' | 'reactivate'; zoneId: number },
    ) => {
      const path = { publicId: v.publicId }
      switch (v.action) {
        case 'create':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/zones', { params: { path }, body: v.body }))
        case 'update':
          return unwrap(api.PATCH('/api/v1/warehouses/{publicId}/zones/{zoneId}', { params: { path: { ...path, zoneId: v.zoneId } }, body: v.body }))
        case 'deactivate':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/zones/{zoneId}/deactivate', { params: { path: { ...path, zoneId: v.zoneId } } }))
        case 'reactivate':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/zones/{zoneId}/reactivate', { params: { path: { ...path, zoneId: v.zoneId } } }))
      }
    },
    onSuccess: () => invalidate(qc, 'zones', 'bins', 'warehouse', 'warehouses'),
  })
}

/** Alta/edición/baja/reactivación de posiciones (`warehouse.manage`). */
export function useSaveWarehouseBin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { publicId: string; action: 'create'; body: Schemas['WarehouseBinRequest'] }
        | { publicId: string; action: 'update'; binId: number; body: Schemas['WarehouseBinPatchRequest'] }
        | { publicId: string; action: 'deactivate' | 'reactivate'; binId: number },
    ) => {
      const path = { publicId: v.publicId }
      switch (v.action) {
        case 'create':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/bins', { params: { path }, body: v.body }))
        case 'update':
          return unwrap(api.PATCH('/api/v1/warehouses/{publicId}/bins/{binId}', { params: { path: { ...path, binId: v.binId } }, body: v.body }))
        case 'deactivate':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/bins/{binId}/deactivate', { params: { path: { ...path, binId: v.binId } } }))
        case 'reactivate':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/bins/{binId}/reactivate', { params: { path: { ...path, binId: v.binId } } }))
      }
    },
    onSuccess: () => invalidate(qc, 'bins', 'zones', 'warehouse', 'warehouses'),
  })
}

/** Alta/edición/estatus manual/baja/reactivación de muelles (`warehouse.manage`). */
export function useSaveWarehouseDock() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { publicId: string; action: 'create'; body: Schemas['WarehouseDockRequest'] }
        | { publicId: string; action: 'update'; dockId: number; body: Schemas['WarehouseDockPatchRequest'] }
        | { publicId: string; action: 'status'; dockId: number; body: Schemas['WarehouseDockStatusRequest'] }
        | { publicId: string; action: 'deactivate' | 'reactivate'; dockId: number },
    ) => {
      const path = { publicId: v.publicId }
      switch (v.action) {
        case 'create':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/docks', { params: { path }, body: v.body }))
        case 'update':
          return unwrap(api.PATCH('/api/v1/warehouses/{publicId}/docks/{dockId}', { params: { path: { ...path, dockId: v.dockId } }, body: v.body }))
        case 'status':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/docks/{dockId}/status', { params: { path: { ...path, dockId: v.dockId } }, body: v.body }))
        case 'deactivate':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/docks/{dockId}/deactivate', { params: { path: { ...path, dockId: v.dockId } } }))
        case 'reactivate':
          return unwrap(api.POST('/api/v1/warehouses/{publicId}/docks/{dockId}/reactivate', { params: { path: { ...path, dockId: v.dockId } } }))
      }
    },
    onSuccess: () => invalidate(qc, 'docks', 'warehouse', 'warehouses', 'dockAppointments'),
  })
}

// =====================================================================================================================
// Productos y categorías
// =====================================================================================================================

/** `GET /api/v1/products?search=&categoryIds=&…&skip=&take=` (paginado en servidor, `take` ≤ 200). */
export function useProducts(query: GetQuery<'/api/v1/products'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.products[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/products', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/**
 * KPIs de 'Productos e inventario' (maqueta `inventario()`), de todo el catálogo (no siguen los filtros de la tabla), todos
 * con `take=1` (solo interesa el `total` o la suma del servidor):
 * - `activeSkus`: `GET /products?activeOnly=true&take=1` → `total`.
 * - `totalUnits`: `GET /inventory/balances?includeZero=false&take=1` → `totalOnHand` (suma de todo, no solo la página).
 * - `belowMin`: `GET /products?belowMin=true&take=1` → `total` (activo, con mínimo y disponible < mínimo).
 * - `unavailable` (2026-10-05): `GET /products?unavailable=true&take=1` → `total` (activos con disponible = 0: sin existencia o con todo reservado).
 * - `serial`: `GET /products?serialOnly=true&activeOnly=true&take=1` → `total` (Lote 12: rastreo SERIAL o con series).
 * - `serialMissing`: `GET /products?serialMissing=true&take=1` → `total` (activos SERIAL con existencia mayor que sus series
 *   en stock: el KPI se pinta en naranja si es > 0).
 */
export function useProductInventoryKpis(options?: WarehouseQueryOptions) {
  const activeSkus = useProducts({ activeOnly: true, take: 1 }, options)
  // solo productos activos: la cifra coincide con lo que muestra la tabla al tocar el KPI (activos con existencia en mano)
  const totalUnits = useInventoryBalances({ includeZero: false, activeProductsOnly: true, take: 1 }, options)
  const belowMin = useProducts({ belowMin: true, take: 1 }, options)
  const unavailable = useProducts({ unavailable: true, take: 1 }, options)
  const serial = useProducts({ activeOnly: true, serialOnly: true, take: 1 }, options)
  const serialMissing = useProducts({ serialMissing: true, take: 1 }, options)
  return { activeSkus, totalUnits, belowMin, unavailable, serial, serialMissing }
}

/**
 * `GET /api/v1/products/brands?search=` (Lote 12): marcas distintas del tenant (activos e inactivos, ordenadas, hasta 500) para
 * el filtro Marca y las sugerencias del campo Marca del modal. Caché de 1 min; un 403 no saca de la pantalla.
 */
export function useProductBrands(search = '', options?: WarehouseQueryOptions) {
  const query = { search: search.trim() || undefined }
  return useQuery({
    queryKey: [warehouseKeys.productBrands[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/products/brands', { params: { query } })),
    enabled: options?.enabled ?? true,
    staleTime: 60 * 1000,
    meta: { handleAccessDenied: false },
  })
}

/** `GET /api/v1/products/{publicId}` (ficha: `ProductDetailDto`, datos de lista en `product`). */
export function useProduct(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.product[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** Varias fichas `GET /api/v1/products/{publicId}` a la vez (misma clave que `useProduct`: comparten caché). */
export function useProductsByPublicId(publicIds: readonly string[], options?: WarehouseQueryOptions) {
  return useQueries({
    queries: publicIds.map((publicId) => ({
      queryKey: [warehouseKeys.product[0], publicId],
      queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}', { params: { path: { publicId } } })),
      enabled: options?.enabled ?? true,
      meta: meta(options),
    })),
  })
}

/** `GET /api/v1/products/{publicId}/lots`. */
export function useProductLots(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.productLots[0], { publicId }],
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}/lots', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/products/{publicId}/serials?status=&search=`. */
export function useProductSerials(
  publicId: string | null | undefined,
  query: GetQuery<'/api/v1/products/{publicId}/serials'> = {},
  options?: WarehouseQueryOptions,
) {
  return useQuery({
    queryKey: [warehouseKeys.productSerials[0], { publicId, ...query }],
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}/serials', { params: { path: { publicId: publicId ?? '' }, query } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `POST /api/v1/products` (`inventory.manage`). */
export function useCreateProduct() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['ProductCreateRequest']) => unwrap(api.POST('/api/v1/products', { body })),
    onSuccess: () => invalidate(qc, 'products', 'productBrands'),
  })
}

/** `PATCH /api/v1/products/{publicId}` (`inventory.manage`; 409 si cambia seguimiento/UOM/dueño con movimientos). */
export function useUpdateProduct() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: Schemas['ProductPatchRequest'] }) =>
      unwrap(api.PATCH('/api/v1/products/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: () => invalidate(qc, 'products', 'product', 'balances', 'productBrands'),
  })
}

/** `POST /api/v1/products/{publicId}/deactivate|reactivate` (`inventory.manage`). */
export function useSetProductActive() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, active }: { publicId: string; active: boolean }) =>
      active
        ? unwrap(api.POST('/api/v1/products/{publicId}/reactivate', { params: { path: { publicId } } }))
        : unwrap(api.POST('/api/v1/products/{publicId}/deactivate', { params: { path: { publicId } } })),
    onSuccess: () => invalidate(qc, 'products', 'product'),
  })
}

/** `GET /api/v1/product-categories?includeInactive=` (lista plana; el árbol se arma con `parentId`). */
export function useProductCategories(query: GetQuery<'/api/v1/product-categories'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.productCategories[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/product-categories', { params: { query } })),
    enabled: options?.enabled ?? true,
    meta: meta(options),
  })
}

/** Alta/edición (mover/renombrar)/baja/reactivación de categorías (`inventory.manage`). */
export function useSaveProductCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { action: 'create'; body: Schemas['ProductCategoryRequest'] }
        | { action: 'update'; id: number; body: Schemas['ProductCategoryPatchRequest'] }
        | { action: 'deactivate' | 'reactivate'; id: number },
    ) => {
      switch (v.action) {
        case 'create':
          return unwrap(api.POST('/api/v1/product-categories', { body: v.body }))
        case 'update':
          return unwrap(api.PATCH('/api/v1/product-categories/{id}', { params: { path: { id: v.id } }, body: v.body }))
        case 'deactivate':
          return unwrap(api.POST('/api/v1/product-categories/{id}/deactivate', { params: { path: { id: v.id } } }))
        case 'reactivate':
          return unwrap(api.POST('/api/v1/product-categories/{id}/reactivate', { params: { path: { id: v.id } } }))
      }
    },
    onSuccess: () => invalidate(qc, 'productCategories', 'products'),
  })
}

// =====================================================================================================================
// Inventario: saldos, Kárdex, ajustes, transferencias, genealogía, rastro de serie y conciliación
// =====================================================================================================================

/** `GET /api/v1/inventory/balances` (paginado; `totalOnHand`/`totalAvailable` suman todo lo filtrado, no solo la página). */
export function useInventoryBalances(query: GetQuery<'/api/v1/inventory/balances'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.balances[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/balances', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/inventory/transactions` (Kárdex paginado). */
export function useInventoryTransactions(query: GetQuery<'/api/v1/inventory/transactions'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.transactions[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/transactions', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/inventory/reconciliation?productPublicId=` (`inventory.adjust`). Se ejecuta a pedido: pase
 *  `enabled: false` y llame `refetch()` desde el botón 'Ejecutar'. */
export function useInventoryReconciliation(query: GetQuery<'/api/v1/inventory/reconciliation'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.reconciliation[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/reconciliation', { params: { query } })),
    enabled: options?.enabled ?? true,
    meta: meta(options),
  })
}

/** `GET /api/v1/inventory/lots/{lotId}/genealogy`. */
export function useLotGenealogy(lotId: number | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.genealogy[0], { lotId }],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/lots/{lotId}/genealogy', { params: { path: { lotId: lotId ?? 0 } } })),
    enabled: lotId != null && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/inventory/serials/trace?productPublicId=&serialNumber=`. */
export function useSerialTrace(query: GetQuery<'/api/v1/inventory/serials/trace'>, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.serialTrace[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/serials/trace', { params: { query } })),
    enabled: Boolean(query.productPublicId && query.serialNumber) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `POST /api/v1/inventory/adjustments` (`inventory.adjust`; 409 `insufficient_stock`). */
export function useInventoryAdjustment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['AdjustmentRequest']) => unwrap(api.POST('/api/v1/inventory/adjustments', { body })),
    onSuccess: () => invalidate(qc, ...STOCK),
  })
}

/** `POST /api/v1/inventory/transfers` (`inventory.adjust`; 409 `insufficient_stock`). */
export function useInventoryTransfer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['TransferRequest']) => unwrap(api.POST('/api/v1/inventory/transfers', { body })),
    onSuccess: () => invalidate(qc, ...STOCK),
  })
}

// ---------------------------------------------------------------------------------------------------------------------
// Lote 14: resumen y detalle del Kárdex, dueños, búsqueda de posiciones, conciliación y descuadres
// ---------------------------------------------------------------------------------------------------------------------

export type KardexSummaryDto = Schemas['KardexSummaryDto']
export type KardexDetailDto = Schemas['KardexDetailDto']
export type KardexDocumentDto = Schemas['KardexDocumentDto']
export type InventoryOwnerDto = Schemas['InventoryOwnerDto']
export type BinSearchItemDto = Schemas['BinSearchItemDto']
export type InventoryDiscrepancyDto = Schemas['InventoryDiscrepancyDto']
export type InventoryDiscrepancyDetailDto = Schemas['InventoryDiscrepancyDetailDto']
export type ReconciliationRunDto = Schemas['ReconciliationRunDto']
export type ReconciliationStatusDto = Schemas['ReconciliationStatusDto']

/** `GET /api/v1/inventory/transactions/summary` (mismos filtros que la lista, sin skip/take): movimientos, entradas y salidas. */
export function useKardexSummary(query: GetQuery<'/api/v1/inventory/transactions/summary'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.transactionSummary[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/transactions/summary', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/inventory/transactions/{id}`: detalle de un movimiento (documento de origen y relacionados; 404 'Movimiento no encontrado.'). */
export function useInventoryTransaction(id: number | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.transaction[0], { id }],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/transactions/{id}', { params: { path: { id: id ?? 0 } } })),
    enabled: id != null && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/inventory/owners`: dueños del inventario ("Propio" primero con `isOwn`, luego los clientes dueños). */
export function useInventoryOwners(options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.owners[0]],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/owners')),
    enabled: options?.enabled ?? true,
    staleTime: 60_000,
    meta: meta(options),
  })
}

/** `GET /api/v1/warehouses/bins/search?search=&warehousePublicIds=&take=` (posiciones entre almacenes; take ≤ 50). */
export function useBinSearch(query: GetQuery<'/api/v1/warehouses/bins/search'>, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.binSearch[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/warehouses/bins/search', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/inventory/discrepancies` (paginado; `openCount` = pendientes con los mismos filtros, sin el de estatus). */
export function useInventoryDiscrepancies(query: GetQuery<'/api/v1/inventory/discrepancies'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.discrepancies[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/discrepancies', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/inventory/discrepancies/{publicId}`: el descuadre, lo reservado, los últimos movimientos y el historial. */
export function useInventoryDiscrepancy(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.discrepancy[0], { publicId }],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/discrepancies/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `POST /api/v1/inventory/discrepancies/{publicId}/resolve` (`inventory.adjust`): REBUILD_BALANCE o DISMISS (nota obligatoria). */
export function useResolveDiscrepancy() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { publicId: string; body: Schemas['DiscrepancyResolveRequest'] }) =>
      unwrap(api.POST('/api/v1/inventory/discrepancies/{publicId}/resolve', { params: { path: { publicId: v.publicId } }, body: v.body })),
    onSuccess: (data, v) => {
      qc.setQueryData([warehouseKeys.discrepancy[0], { publicId: v.publicId }], data)
      return invalidate(qc, ...STOCK)
    },
  })
}

/** `POST /api/v1/inventory/reconciliation/run` (`inventory.adjust`): conciliación manual (todo el tenant o hasta 200 productos). */
export function useRunReconciliation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['ReconciliationRunRequest']) => unwrap(api.POST('/api/v1/inventory/reconciliation/run', { body })),
    onSuccess: () => invalidate(qc, 'discrepancies', 'discrepancy', 'reconciliation', 'reconciliationStatus'),
  })
}

/** `GET /api/v1/inventory/reconciliation/status` (`inventory.adjust`): revisión automática (pendientes, procesados, último error). */
export function useReconciliationStatus(options?: WarehouseQueryOptions & { refetchInterval?: number | false }) {
  return useQuery({
    queryKey: [warehouseKeys.reconciliationStatus[0]],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/reconciliation/status')),
    enabled: options?.enabled ?? true,
    refetchInterval: options?.refetchInterval ?? false,
    meta: meta(options),
  })
}

export const exportInventoryDiscrepancies = (query: GetQuery<'/api/v1/inventory/discrepancies'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/inventory/discrepancies', { params: { query: { ...query, skip, take } } })))

// =====================================================================================================================
// Recepción: avisos de llegada (ASN) y recibos
// =====================================================================================================================

/** `GET /api/v1/asns?warehousePublicId=&status=&clientPublicId=&search=`. */
export function useAsns(query: GetQuery<'/api/v1/asns'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.asns[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/asns', { params: { query } })),
    enabled: options?.enabled ?? true,
    meta: meta(options),
  })
}

/** `POST /api/v1/asns` y `POST /api/v1/asns/{id}/cancel` (`warehouse.receive`). */
export function useSaveAsn() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { action: 'create'; body: Schemas['AsnCreateRequest'] } | { action: 'cancel'; id: number }) =>
      v.action === 'create'
        ? unwrap(api.POST('/api/v1/asns', { body: v.body }))
        : unwrap(api.POST('/api/v1/asns/{id}/cancel', { params: { path: { id: v.id } } })),
    onSuccess: () => invalidate(qc, 'asns', 'dockAppointments'),
  })
}

/** `GET /api/v1/receipts` (paginado). */
export function useReceipts(query: GetQuery<'/api/v1/receipts'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.receipts[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/receipts', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/receipts/{publicId}` (ficha con líneas). */
export function useReceipt(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.receipt[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/receipts/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** Pone en caché la ficha que devolvió una escritura del recibo (el detalle se pinta sin volver a pedirla). */
function cacheReceipt(qc: QueryClient, dto: ReceiptDetailDto) {
  const publicId = dto.header?.publicId
  if (publicId) qc.setQueryData([warehouseKeys.receipt[0], publicId], dto)
}

/** `POST /api/v1/receipts` (`warehouse.receive`; contra PO además `purchasing.receive`). Lote 13: sin líneas y sin
 *  `confirm` crea solo el encabezado (Esperado); la ficha devuelta queda en caché. */
export function useCreateReceipt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['ReceiptCreateRequest']) => unwrap(api.POST('/api/v1/receipts', { body })),
    onSuccess: (dto) => {
      cacheReceipt(qc, dto)
      return invalidate(qc, 'receipts', 'asns', 'purchaseOrders', 'purchaseOrder')
    },
  })
}

/** Lote 13 — `PATCH /api/v1/receipts/{publicId}` (encabezado de un recibo abierto, `warehouse.receive`): la ficha devuelta
 *  queda en caché e invalida la lista y la ficha. El `rowVersion` se toma de la caché al enviar (cambia con cada línea). */
export function useUpdateReceiptHeader() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: Schemas['ReceiptHeaderUpdateRequest'] }) =>
      unwrap(api.PATCH('/api/v1/receipts/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: (dto) => {
      cacheReceipt(qc, dto)
      return invalidate(qc, 'receipts', 'receipt', 'dockAppointments', 'receiptTargetSuggestions')
    },
  })
}

/** Líneas del recibo abierto: agregar, capturar (`PUT`) y quitar (`warehouse.receive`). Lote 13: la ficha devuelta queda
 *  en caché (la rejilla no espera otra consulta) y se invalida la lista (estatus y diferencia de la fila). */
export function useSaveReceiptLine() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { publicId: string; action: 'add'; body: Schemas['ReceiptLineRequest'] }
        | { publicId: string; action: 'update'; lineId: number; body: Schemas['ReceiptLineUpdateRequest'] }
        | { publicId: string; action: 'remove'; lineId: number },
    ) => {
      switch (v.action) {
        case 'add':
          return unwrap(api.POST('/api/v1/receipts/{publicId}/lines', { params: { path: { publicId: v.publicId } }, body: v.body }))
        case 'update':
          return unwrap(
            api.PUT('/api/v1/receipts/{publicId}/lines/{lineId}', { params: { path: { publicId: v.publicId, lineId: v.lineId } }, body: v.body }),
          )
        case 'remove':
          return unwrap(api.DELETE('/api/v1/receipts/{publicId}/lines/{lineId}', { params: { path: { publicId: v.publicId, lineId: v.lineId } } }))
      }
    },
    onSuccess: (dto) => {
      cacheReceipt(qc, dto)
      // Lote 16: lo recibido y los destinos de las líneas cambian las sugerencias (cupo reservado por otras líneas)
      return invalidate(qc, 'receipts', 'receiptTargetSuggestions')
    },
  })
}

/** Lote 16 — `GET /api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions?take=` (inventory.view): posiciones destino
 *  sugeridas para una línea de un recibo directo (las que caben primero; las que exceden el cupo al final con `fits=false`). */
export function useReceiptTargetSuggestions(
  publicId: string | null | undefined,
  lineId: number | null | undefined,
  take = 3,
  options?: WarehouseQueryOptions,
) {
  return useQuery({
    queryKey: [warehouseKeys.receiptTargetSuggestions[0], { publicId, lineId, take }],
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions', {
          params: { path: { publicId: publicId ?? '', lineId: lineId ?? 0 }, query: { take } },
        }),
      ),
    enabled: Boolean(publicId) && lineId != null && (options?.enabled ?? true),
    staleTime: 30_000,
    meta: meta(options),
  })
}

/** Lote 16 — "Usar posiciones sugeridas": `POST /api/v1/receipts/{publicId}/targets/suggest` (warehouse.receive, con el
 *  `rowVersion` de la caché). La ficha devuelta queda en caché; devuelve `{ receipt, assigned, withoutSuggestion }`. */
export function useApplyReceiptTargetSuggestions() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: Schemas['ReceiptApplySuggestionsRequest'] }) =>
      unwrap(api.POST('/api/v1/receipts/{publicId}/targets/suggest', { params: { path: { publicId } }, body })),
    onSuccess: (res) => {
      if (res.receipt) cacheReceipt(qc, res.receipt)
      return invalidate(qc, 'receipts', 'receiptTargetSuggestions')
    },
  })
}

/** `POST /api/v1/receipts/{publicId}/confirm` (Recibiendo/Discrepancia → Completado o Completado con diferencia: mueve
 *  inventario y genera tareas PUTAWAY). */
export function useConfirmReceipt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body = {} }: { publicId: string; body?: Schemas['ReceiptConfirmRequest'] }) =>
      unwrap(api.POST('/api/v1/receipts/{publicId}/confirm', { params: { path: { publicId } }, body })),
    onSuccess: (dto) => {
      cacheReceipt(qc, dto)
      return invalidate(qc, 'receipts', 'receipt', 'asns', 'tasks', 'purchaseOrders', 'purchaseOrder', 'purchaseOrderShortages', 'purchaseOrderShortageLines', 'crossDockCandidates', ...STOCK)
    },
  })
}

/** `DELETE /api/v1/receipts/{publicId}` (abierto —Esperado, Recibiendo o Discrepancia— sin cruce de muelle asignado). */
export function useDeleteReceipt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (publicId: string) => {
      await unwrap(api.DELETE('/api/v1/receipts/{publicId}', { params: { path: { publicId } } }))
    },
    // la ficha borrada no se invalida (se volvería a pedir mientras se pinta y daría 404): la pantalla deja de elegirla
    onSuccess: () => invalidate(qc, 'receipts', 'asns', 'purchaseOrders', 'purchaseOrder'),
  })
}

// =====================================================================================================================
// Tareas de almacén
// =====================================================================================================================

/** `GET /api/v1/warehouse-tasks` (cola paginada; sin `status` solo abiertas salvo `includeClosed`). */
export function useWarehouseTasks(query: GetQuery<'/api/v1/warehouse-tasks'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.tasks[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/warehouse-tasks', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/warehouse-tasks/putaway-suggestions?taskId=&…`. */
export function usePutawaySuggestions(query: GetQuery<'/api/v1/warehouse-tasks/putaway-suggestions'>, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.putawaySuggestions[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/warehouse-tasks/putaway-suggestions', { params: { query } })),
    enabled: options?.enabled ?? true,
    meta: meta(options),
  })
}

/** Acciones sobre una tarea: asignar (`warehouse.manage`; `userId` null desasigna), iniciar, completar y cancelar (UI en `taskQueue.tsx`). */
export function useWarehouseTaskAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { id: number; action: 'assign'; body: Schemas['TaskAssignRequest'] }
        | { id: number; action: 'start' }
        | { id: number; action: 'complete'; body: Schemas['TaskCompleteRequest'] }
        | { id: number; action: 'cancel'; body: Schemas['TaskCancelRequest'] },
    ) => {
      const path = { id: v.id }
      switch (v.action) {
        case 'assign':
          return unwrap(api.POST('/api/v1/warehouse-tasks/{id}/assign', { params: { path }, body: v.body }))
        case 'start':
          return unwrap(api.POST('/api/v1/warehouse-tasks/{id}/start', { params: { path } }))
        case 'complete':
          return unwrap(api.POST('/api/v1/warehouse-tasks/{id}/complete', { params: { path }, body: v.body }))
        case 'cancel':
          return unwrap(api.POST('/api/v1/warehouse-tasks/{id}/cancel', { params: { path }, body: v.body }))
      }
    },
    // La ficha del recibo pinta sus tareas de acomodo (y pasa a PUTAWAY al completar la última) y la del plan de cruce sus
    // asignaciones (completar la tarea CROSSDOCK = mover la asignación): se invalidan junto con la cola.
    // Lote 14 (D10): la lista de conteos muestra a quién está asignada la tarea COUNT de cada conteo.
    onSuccess: (_data, v) =>
      v.action === 'complete'
        ? invalidate(qc, 'tasks', 'putawaySuggestions', 'receipt', 'receipts', 'crossDockPlan', 'crossDockPlans', 'cycleCountsPage', ...STOCK)
        : invalidate(qc, 'tasks', 'receipt', 'cycleCountsPage'),
  })
}

/** `POST /api/v1/warehouse-tasks/replenishment/run` (`warehouse.pick`): genera tareas REPLENISH. */
export function useRunReplenishment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['ReplenishmentRunRequest']) => unwrap(api.POST('/api/v1/warehouse-tasks/replenishment/run', { body })),
    onSuccess: () => invalidate(qc, 'tasks'),
  })
}

// =====================================================================================================================
// Conteo cíclico
// =====================================================================================================================

/** `GET /api/v1/cycle-counts` (lista completa, arreglo). */
export function useCycleCounts(query: GetQuery<'/api/v1/cycle-counts'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.cycleCounts[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/cycle-counts', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/cycle-counts/{id}` (`warehouse.count`), con filtros de líneas opcionales. */
export function useCycleCount(id: number | null | undefined, query: GetQuery<'/api/v1/cycle-counts/{id}'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.cycleCount[0], { id, ...query }],
    queryFn: () => unwrap(api.GET('/api/v1/cycle-counts/{id}', { params: { path: { id: id ?? 0 }, query } })),
    enabled: id != null && (options?.enabled ?? true),
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Consultas del conteo que cambian con cualquier escritura sobre un conteo (lista, página, ficha, vista previa, cola; Lote F12:
 *  "Por revisar" y la vista previa de reconciliar, que se recalcula tras corregir una cantidad). */
const COUNTS: KeyName[] = ['cycleCounts', 'cycleCountsPage', 'cycleCount', 'changesPreview', 'tasks', 'cycleCountReview', 'reconcilePreview']

/**
 * Lote F12 — `GET /api/v1/cycle-counts/review` (warehouse.count): conteos Contados para revisar, más recientes primero, con quién
 * contó, primer producto y cuántos más, posiciones, líneas, cuántas difieren contra la existencia ACTUAL, correcciones, errores,
 * pendientes y `matches`. Filtros `warehousePublicId`, `countedByUserId`, `search`, `includeOpen`; `skip`/`take` (1..200).
 */
export function useCycleCountReview(query: GetQuery<'/api/v1/cycle-counts/review'>, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.cycleCountReview[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/cycle-counts/review', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Exportar "Por revisar": todo lo filtrado (de a 200, hasta 10 000). */
export const exportCycleCountReview = (query: GetQuery<'/api/v1/cycle-counts/review'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/cycle-counts/review', { params: { query: { ...query, skip, take } } })))

/**
 * Lote F12 — `GET /api/v1/cycle-counts/{id}/reconcile-preview` (warehouse.count): lo que asentaría confirmar, por línea (existencia
 * actual, reservado, contado con su evidencia, ajuste, saldo resultante y error) y los totales; no escribe. 422 si ya se
 * reconcilió. Mismo cálculo que `POST .../reconcile`. Sin `keepPreviousData`: la vista previa de otro conteo no se muestra.
 */
export function useReconcilePreview(id: number | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.reconcilePreview[0], { id }],
    queryFn: () => unwrap(api.GET('/api/v1/cycle-counts/{id}/reconcile-preview', { params: { path: { id: id ?? 0 } } })),
    enabled: id != null && (options?.enabled ?? true),
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

/**
 * Lote F12 — `POST /api/v1/cycle-counts/reconcile-matching` (warehouse.count): cierra en Concordancia, cada uno en su transacción,
 * los conteos que cuadran contra la existencia actual; devuelve `closed` y `skipped` (con `reasonCode`, `reason` y `count`).
 * Invalida todo lo del conteo y el inventario (un cierre no mueve saldos, pero sí la franja del Pulso y el Kárdex por documento).
 */
export function useReconcileMatching() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: CountReconcileMatchingRequest) => unwrap(api.POST('/api/v1/cycle-counts/reconcile-matching', { body })),
    onSuccess: () => invalidate(qc, ...COUNTS, ...STOCK),
  })
}

/**
 * Lote F12 — `POST /api/v1/warehouses/{publicId}/bins/{binId}/confirm-provisional` (warehouse.manage): quita la marca "pendiente de
 * revisión" de una posición creada desde un conteo. 409 'La posición no está pendiente de revisión.'.
 */
export function useConfirmProvisionalBin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { publicId: string; binId: number }) =>
      unwrap(api.POST('/api/v1/warehouses/{publicId}/bins/{binId}/confirm-provisional', { params: { path: { publicId: v.publicId, binId: v.binId } } })),
    onSuccess: () => invalidate(qc, 'bins', 'binSearch', 'zones', 'warehouse', ...COUNTS),
  })
}

/**
 * Lote 14 (hallazgo 14) — `GET /api/v1/cycle-counts/page` (inventory.view): página de conteos con el total (take 1..200,
 * por defecto 50). Filtros: almacenes, zonas, posiciones, productos, categorías, estatus, orígenes (MANUAL/CHANGES), fechas
 * de alta en días locales y `search`. Cada fila trae su posición o `binCount`, el origen, `taskId` y el asignado.
 */
export function useCycleCountsPage(query: GetQuery<'/api/v1/cycle-counts/page'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.cycleCountsPage[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/cycle-counts/page', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Exportar la lista de conteos: todo lo filtrado (de a 200, hasta 10 000). */
export const exportCycleCounts = (query: GetQuery<'/api/v1/cycle-counts/page'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/cycle-counts/page', { params: { query: { ...query, skip, take } } })))

/**
 * Lote 14 (D2–D4) — `GET /api/v1/cycle-counts/changes-preview` (warehouse.count): ventana efectiva, movimientos, posiciones
 * que se contarían (vacías incluidas), saltadas y `problem` (el 400 que daría el alta). 400 si el rango está invertido o
 * pasa de 31 días; 422 almacén inactivo; 404 zona. Sin reintentos: un 400 es la respuesta.
 */
export function useChangesPreview(query: GetQuery<'/api/v1/cycle-counts/changes-preview'>, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.changesPreview[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/cycle-counts/changes-preview', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

/** Lote 14 — `POST /api/v1/cycle-counts/from-changes` (warehouse.count): un conteo Pendiente por posición cambiada (tope 200). */
export function useCreateCountsFromChanges() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: CycleCountFromChangesRequest) => unwrap(api.POST('/api/v1/cycle-counts/from-changes', { body })),
    onSuccess: () => invalidate(qc, ...COUNTS),
  })
}

/** `POST /api/v1/cycle-counts` (`warehouse.count`; máx. 1000 líneas). */
export function useCreateCycleCount() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['CycleCountCreateRequest']) => unwrap(api.POST('/api/v1/cycle-counts', { body })),
    onSuccess: () => invalidate(qc, ...COUNTS),
  })
}

/** Acciones del conteo (`warehouse.count`): capturar, agregar línea, terminar, refrescar foto, reconciliar y eliminar. */
export function useCycleCountAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (
      v:
        | { id: number; action: 'capture'; body: Schemas['CountCaptureRequest'] }
        | { id: number; action: 'addLine'; body: Schemas['CountAddLineRequest'] }
        | { id: number; action: 'finish' | 'reconcile'; body?: Schemas['CountReconcileRequest'] }
        | { id: number; action: 'refresh' }
        | { id: number; action: 'delete' },
    ): Promise<CycleCountDetailDto | null> => {
      const path = { id: v.id }
      switch (v.action) {
        case 'capture':
          return unwrap(api.PUT('/api/v1/cycle-counts/{id}/lines', { params: { path }, body: v.body }))
        case 'addLine':
          return unwrap(api.POST('/api/v1/cycle-counts/{id}/lines', { params: { path }, body: v.body }))
        case 'finish':
          return unwrap(api.POST('/api/v1/cycle-counts/{id}/finish', { params: { path }, body: v.body ?? {} }))
        case 'reconcile':
          return unwrap(api.POST('/api/v1/cycle-counts/{id}/reconcile', { params: { path }, body: v.body ?? {} }))
        case 'refresh':
          return unwrap(api.POST('/api/v1/cycle-counts/{id}/refresh', { params: { path } }))
        case 'delete':
          await unwrap(api.DELETE('/api/v1/cycle-counts/{id}', { params: { path } }))
          return null
      }
    },
    // Lote 14: la ficha que devuelve la escritura queda en caché (la del panel del conteo, sin filtros de líneas) para que
    // la siguiente captura en la fila use su rowVersion al día; luego se invalida todo lo del conteo.
    onSuccess: (data, v) => {
      if (data) qc.setQueryData([warehouseKeys.cycleCount[0], { id: v.id }], data)
      if (v.action === 'delete') qc.removeQueries({ queryKey: [warehouseKeys.cycleCount[0], { id: v.id }] })
      return v.action === 'reconcile' ? invalidate(qc, ...COUNTS, ...STOCK) : invalidate(qc, ...COUNTS)
    },
  })
}

// =====================================================================================================================
// Recolección y empaque
// =====================================================================================================================

/** `GET /api/v1/pick-batches` (paginado). */
export function usePickBatches(query: GetQuery<'/api/v1/pick-batches'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.pickBatches[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/pick-batches', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/pick-batches/{publicId}`. */
export function usePickBatch(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.pickBatch[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/pick-batches/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `POST /api/v1/pick-batches` (`warehouse.pick`): recolecta y reserva/descuenta inventario. */
export function useCreatePickBatch() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['PickBatchCreateRequest']) => unwrap(api.POST('/api/v1/pick-batches', { body })),
    onSuccess: () => invalidate(qc, 'pickBatches', ...STOCK),
  })
}

/** `POST /api/v1/pick-batches/{publicId}/pack` (`warehouse.pick` + `orders.create`): genera la orden. */
export function usePackPickBatch() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: Schemas['PickBatchPackRequest'] }) =>
      unwrap(api.POST('/api/v1/pick-batches/{publicId}/pack', { params: { path: { publicId } }, body })),
    onSuccess: () => invalidate(qc, 'pickBatches', 'pickBatch', 'orders', ...STOCK),
  })
}

/** `DELETE /api/v1/pick-batches/{publicId}` (`warehouse.pick`; si ya está PACKED además `orders.cancel`). */
export function useDeletePickBatch() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ publicId, body = {} }: { publicId: string; body?: Schemas['PickBatchDeleteRequest'] }) => {
      await unwrap(api.DELETE('/api/v1/pick-batches/{publicId}', { params: { path: { publicId } }, body }))
    },
    onSuccess: () => invalidate(qc, 'pickBatches', 'pickBatch', 'orders', 'order', ...STOCK),
  })
}

// =====================================================================================================================
// Compras: proveedores, órdenes de compra y faltantes
// =====================================================================================================================

/** `GET /api/v1/suppliers?includeInactive=&search=`. */
export function useSuppliers(query: GetQuery<'/api/v1/suppliers'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.suppliers[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/suppliers', { params: { query } })),
    enabled: options?.enabled ?? true,
    meta: meta(options),
  })
}

/** Alta/edición/baja/reactivación de proveedores (`purchasing.manage`). */
export function useSaveSupplier() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { action: 'create'; body: Schemas['SupplierRequest'] }
        | { action: 'update'; id: number; body: Schemas['SupplierPatchRequest'] }
        | { action: 'deactivate' | 'reactivate'; id: number },
    ) => {
      switch (v.action) {
        case 'create':
          return unwrap(api.POST('/api/v1/suppliers', { body: v.body }))
        case 'update':
          return unwrap(api.PATCH('/api/v1/suppliers/{id}', { params: { path: { id: v.id } }, body: v.body }))
        case 'deactivate':
          return unwrap(api.POST('/api/v1/suppliers/{id}/deactivate', { params: { path: { id: v.id } } }))
        case 'reactivate':
          return unwrap(api.POST('/api/v1/suppliers/{id}/reactivate', { params: { path: { id: v.id } } }))
      }
    },
    onSuccess: () => invalidate(qc, 'suppliers', 'purchaseOrders'),
  })
}

/** `GET /api/v1/purchase-orders` (paginado). */
export function usePurchaseOrders(query: GetQuery<'/api/v1/purchase-orders'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.purchaseOrders[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/purchase-orders', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/purchase-orders/{publicId}`. */
export function usePurchaseOrder(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.purchaseOrder[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/purchase-orders/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/purchase-orders/shortages` (resumen global de faltantes por orden de compra). */
export function usePurchaseOrderShortages(options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.purchaseOrderShortages[0], {}],
    queryFn: () => unwrap(api.GET('/api/v1/purchase-orders/shortages')),
    enabled: options?.enabled ?? true,
    meta: meta(options),
  })
}

/** `GET /api/v1/purchase-orders/{publicId}/shortage-lines`. */
export function usePurchaseOrderShortageLines(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.purchaseOrderShortageLines[0], { publicId }],
    queryFn: () => unwrap(api.GET('/api/v1/purchase-orders/{publicId}/shortage-lines', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `POST /api/v1/purchase-orders` (`purchasing.manage`; nace DRAFT). */
export function useCreatePurchaseOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['PurchaseOrderCreateRequest']) => unwrap(api.POST('/api/v1/purchase-orders', { body })),
    onSuccess: () => invalidate(qc, 'purchaseOrders'),
  })
}

/** Edición, envío, cancelación y eliminación de una orden de compra (`purchasing.manage`). */
export function usePurchaseOrderAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (
      v:
        | { publicId: string; action: 'update'; body: Schemas['PurchaseOrderPatchRequest'] }
        | { publicId: string; action: 'send' | 'cancel'; body?: Schemas['PurchaseOrderStatusRequest'] }
        | { publicId: string; action: 'delete' },
    ): Promise<PurchaseOrderDto | null> => {
      const path = { publicId: v.publicId }
      switch (v.action) {
        case 'update':
          return unwrap(api.PATCH('/api/v1/purchase-orders/{publicId}', { params: { path }, body: v.body }))
        case 'send':
          return unwrap(api.POST('/api/v1/purchase-orders/{publicId}/send', { params: { path }, body: v.body ?? {} }))
        case 'cancel':
          return unwrap(api.POST('/api/v1/purchase-orders/{publicId}/cancel', { params: { path }, body: v.body ?? {} }))
        case 'delete':
          await unwrap(api.DELETE('/api/v1/purchase-orders/{publicId}', { params: { path } }))
          return null
      }
    },
    onSuccess: () => invalidate(qc, 'purchaseOrders', 'purchaseOrder', 'purchaseOrderShortages', 'purchaseOrderShortageLines'),
  })
}

/** `POST /api/v1/purchase-orders/{publicId}/lines/{lineId}/resolve` (`inventory.adjust`; REORDER además `purchasing.manage`). */
export function useResolveShortage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, lineId, body }: { publicId: string; lineId: number; body: Schemas['ShortageResolveRequest'] }) =>
      unwrap(api.POST('/api/v1/purchase-orders/{publicId}/lines/{lineId}/resolve', { params: { path: { publicId, lineId } }, body })),
    onSuccess: () => invalidate(qc, 'purchaseOrders', 'purchaseOrder', 'purchaseOrderShortages', 'purchaseOrderShortageLines', ...STOCK),
  })
}

// =====================================================================================================================
// Cruce de muelle (demo): citas de muelle y planes
// =====================================================================================================================

/** `GET /api/v1/dock-appointments?warehousePublicId=&dockId=&fromUtc=&toUtc=&status=`. */
export function useDockAppointments(query: GetQuery<'/api/v1/dock-appointments'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.dockAppointments[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/dock-appointments', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Agendar, reprogramar y cambiar estatus de una cita (`warehouse.crossdock`). */
export function useSaveDockAppointment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { action: 'create'; body: Schemas['DockAppointmentRequest'] }
        | { action: 'update'; id: number; body: Schemas['DockAppointmentPatchRequest'] }
        | { action: 'status'; id: number; body: Schemas['DockAppointmentStatusRequest'] },
    ) => {
      switch (v.action) {
        case 'create':
          return unwrap(api.POST('/api/v1/dock-appointments', { body: v.body }))
        case 'update':
          return unwrap(api.PATCH('/api/v1/dock-appointments/{id}', { params: { path: { id: v.id } }, body: v.body }))
        case 'status':
          return unwrap(api.POST('/api/v1/dock-appointments/{id}/status', { params: { path: { id: v.id } }, body: v.body }))
      }
    },
    onSuccess: () => invalidate(qc, 'dockAppointments', 'docks'),
  })
}

/** `GET /api/v1/cross-dock-plans?warehousePublicId=&status=`. */
export function useCrossDockPlans(query: GetQuery<'/api/v1/cross-dock-plans'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.crossDockPlans[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/cross-dock-plans', { params: { query } })),
    enabled: options?.enabled ?? true,
    meta: meta(options),
  })
}

/** `GET /api/v1/cross-dock-plans/{id}`. */
export function useCrossDockPlan(id: number | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.crossDockPlan[0], id],
    queryFn: () => unwrap(api.GET('/api/v1/cross-dock-plans/{id}', { params: { path: { id: id ?? 0 } } })),
    enabled: id != null && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/cross-dock-plans/{id}/candidates` (líneas de recibo disponibles para asignar). */
export function useCrossDockCandidates(id: number | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.crossDockCandidates[0], { id }],
    queryFn: () => unwrap(api.GET('/api/v1/cross-dock-plans/{id}/candidates', { params: { path: { id: id ?? 0 } } })),
    enabled: id != null && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** Crear plan, asignar, cancelar asignación, mover y completar (`warehouse.crossdock`). */
export function useCrossDockAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (
      v:
        | { action: 'create'; body: Schemas['CrossDockPlanRequest'] }
        | { action: 'allocate'; id: number; body: Schemas['CrossDockAllocationRequest'] }
        | { action: 'cancelAllocation'; id: number; allocationId: number; comment?: string }
        | { action: 'move'; id: number; allocationId: number; body: Schemas['CrossDockMoveRequest'] }
        | { action: 'complete'; id: number },
    ) => {
      switch (v.action) {
        case 'create':
          return unwrap(api.POST('/api/v1/cross-dock-plans', { body: v.body }))
        case 'allocate':
          return unwrap(api.POST('/api/v1/cross-dock-plans/{id}/allocations', { params: { path: { id: v.id } }, body: v.body }))
        case 'cancelAllocation':
          return unwrap(
            api.DELETE('/api/v1/cross-dock-plans/{id}/allocations/{allocationId}', {
              params: { path: { id: v.id, allocationId: v.allocationId }, query: { comment: v.comment } },
            }),
          )
        case 'move':
          return unwrap(
            api.POST('/api/v1/cross-dock-plans/{id}/allocations/{allocationId}/move', {
              params: { path: { id: v.id, allocationId: v.allocationId } },
              body: v.body,
            }),
          )
        case 'complete':
          return unwrap(api.POST('/api/v1/cross-dock-plans/{id}/complete', { params: { path: { id: v.id } } }))
      }
    },
    onSuccess: (_data, v) =>
      v.action === 'move' || v.action === 'complete'
        ? invalidate(qc, 'crossDockPlans', 'crossDockPlan', 'crossDockCandidates', 'tasks', ...STOCK)
        : invalidate(qc, 'crossDockPlans', 'crossDockPlan', 'crossDockCandidates', 'tasks'),
  })
}

// =====================================================================================================================
// Consulta de órdenes (solo lectura)
// =====================================================================================================================

/** `GET /api/v1/orders` (paginado, `orders.view` + LTL_GROUND). Solo lectura: este lote no da de alta ni edita órdenes. */
export function useOrdersReadonly(query: GetQuery<'/api/v1/orders'> = {}, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.orders[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/orders', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/orders/{publicId}` (`OrderDetailDto`). */
export function useOrderReadonly(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [warehouseKeys.order[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/orders/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/orders/lookup?code=` (número de orden, factura o lote de empaque → coincidencias). */
export function useOrderLookup(code: string | null | undefined, options?: WarehouseQueryOptions) {
  const query = { code: code ?? undefined }
  return useQuery({
    queryKey: [warehouseKeys.orderLookup[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/orders/lookup', { params: { query } })),
    enabled: Boolean(code) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

// ---------------------------------------------------------------------------------------------------------------------
// Exportación de las listas paginadas por el servidor (`DataTable.exportRows`): todas las filas con los filtros de la
// pantalla (su mismo `query`, reemplazando `skip`/`take`), de a 200 hasta 10 000 (`fetchAllPages`).
// ---------------------------------------------------------------------------------------------------------------------
export const exportProducts = (query: GetQuery<'/api/v1/products'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/products', { params: { query: { ...query, skip, take } } })))

export const exportInventoryBalances = (query: GetQuery<'/api/v1/inventory/balances'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/inventory/balances', { params: { query: { ...query, skip, take } } })))

export const exportInventoryTransactions = (query: GetQuery<'/api/v1/inventory/transactions'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/inventory/transactions', { params: { query: { ...query, skip, take } } })))

export const exportReceipts = (query: GetQuery<'/api/v1/receipts'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/receipts', { params: { query: { ...query, skip, take } } })))

/** Recibos con sus líneas (`includeLines=true`: cada recibo de la página trae `lines`, leídas en lote por el API) para la
 *  exportación agrupada de Recibo y 'Acomodo pendiente'. */
export const exportReceiptsWithLines = (query: GetQuery<'/api/v1/receipts'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/receipts', { params: { query: { ...query, includeLines: true, skip, take } } })))

export const exportWarehouseTasks = (query: GetQuery<'/api/v1/warehouse-tasks'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/warehouse-tasks', { params: { query: { ...query, skip, take } } })))

export const exportPickBatches = (query: GetQuery<'/api/v1/pick-batches'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/pick-batches', { params: { query: { ...query, skip, take } } })))

export const exportPurchaseOrders = (query: GetQuery<'/api/v1/purchase-orders'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/purchase-orders', { params: { query: { ...query, skip, take } } })))

export const exportOrders = (query: GetQuery<'/api/v1/orders'>) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/orders', { params: { query: { ...query, skip, take } } })))

// =====================================================================================================================
// Lote 1 (cambios de Almacén): localidades postales (Ciudad/ZIP del almacén) y exportación de posiciones
// =====================================================================================================================

export type PostalLocalityDto = Schemas['PostalLocalityDto']

/** Resultados por búsqueda de `usePostalLocalities` (el API da 20 por defecto, máximo 100). */
export const POSTAL_LOCALITIES_TAKE = 30

/**
 * `GET /api/v1/postal-localities?search=&take=` (catálogo global de ciudades y códigos postales; cualquier usuario autenticado):
 * busca por ciudad (sin acentos) o por prefijo de código postal. Caché de 10 min (el catálogo casi no cambia) y
 * `keepPreviousData` para que la lista no parpadee entre teclas. Un 403 no saca de la pantalla.
 */
export function usePostalLocalities(search: string, options?: WarehouseQueryOptions) {
  const query = { search: search.trim() || undefined, take: POSTAL_LOCALITIES_TAKE }
  return useQuery({
    queryKey: ['/api/v1/postal-localities', query],
    queryFn: () => unwrap(api.GET('/api/v1/postal-localities', { params: { query } })),
    enabled: options?.enabled ?? true,
    staleTime: 10 * 60 * 1000,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

/** Exportación de la pestaña Posiciones: todas las posiciones del almacén con los filtros de la pantalla (de a 200). */
export const exportWarehouseBins = (publicId: string, query: GetQuery<'/api/v1/warehouses/{publicId}/bins'>) =>
  fetchAllPages((skip, take) =>
    unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path: { publicId }, query: { ...query, skip, take } } })),
  )

// =====================================================================================================================
// Lote 11: cupo máximo en bloque ("Asignar cupo", BinCapacityModal)
// =====================================================================================================================

/**
 * `POST /api/v1/warehouses/{publicId}/bins/capacity` (`warehouse.manage`): fija (`maxCapacityQty`) o quita (`clear`) el
 * cupo de todas las posiciones que cumplen los filtros → `{ matched, changed }`. Invalida por prefijo posiciones, zonas
 * (los recuadros de ocupación de Posiciones) y almacenes, como `useSaveWarehouseBin`.
 */
export function useSetBinsCapacity() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: BinCapacityRequest }) =>
      unwrap(api.POST('/api/v1/warehouses/{publicId}/bins/capacity', { params: { path: { publicId } }, body })),
    onSuccess: () => invalidate(qc, 'bins', 'zones', 'warehouse', 'warehouses'),
  })
}

/** Resultado de la vista previa: `count` null = sin alcance o sin dato todavía; `exact` false = `count` es un tope. */
export interface BinCapacityPreview {
  count: number | null
  exact: boolean
  loading: boolean
  error: Error | null
}

/**
 * Vista previa de "Asignar cupo": cuántas posiciones cambiaría el POST con este alcance (`binCapacity.ts`).
 * - Sin "Solo posiciones sin cupo": `GET .../bins?take=1` con los mismos filtros → `total` (= `matched`).
 * - Con ella y sin filtros de texto: Σ `binsWithoutCapacity` de las zonas elegidas (o de todas), sin consultar posiciones.
 * - Con ella y con texto: el GET no tiene ese filtro; si el total es ≤ `BIN_CAPACITY_EXACT_LIMIT` se recorren las
 *   posiciones (de a 200) y se cuentan las que no tienen cupo; si no, el total se da como tope (`exact: false`).
 * La clave cuelga del prefijo de posiciones (se invalida con ellas) y lleva `capacityPreview` para no mezclarse con el
 * caché de las páginas del listado (aquí se guarda `{ count, exact }`, no una página).
 */
export function useBinCapacityPreview(
  publicId: string,
  scope: BinCapacityScope,
  zones: readonly WarehouseZoneDto[],
  zonesLoading: boolean,
): BinCapacityPreview {
  const ready = Boolean(publicId) && scopeReady(scope)
  const byZones = scope.onlyWithoutCapacity && !hasTextFilter(scope)
  const query = capacityPreviewQuery(scope)
  const q = useQuery({
    queryKey: [warehouseKeys.bins[0], { publicId, capacityPreview: true, onlyWithoutCapacity: scope.onlyWithoutCapacity, ...query }],
    queryFn: async () => {
      const path = { publicId }
      const page = await unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path, query } }))
      const total = page.total ?? 0
      if (!scope.onlyWithoutCapacity || total === 0) return { count: total, exact: true }
      if (total > BIN_CAPACITY_EXACT_LIMIT) return { count: total, exact: false }
      const all = await fetchAllPages((skip, take) =>
        unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path, query: { ...query, skip, take } } })),
      )
      return { count: all.items.filter((b) => b.maxCapacityQty == null).length, exact: true }
    },
    enabled: ready && !byZones,
    meta: { handleAccessDenied: false },
  })
  if (!ready) return { count: null, exact: true, loading: false, error: null }
  if (byZones) {
    return zonesLoading
      ? { count: null, exact: true, loading: true, error: null }
      : { count: zonesWithoutCapacity(zones, scope.zoneIds), exact: true, loading: false, error: null }
  }
  return { count: q.data?.count ?? null, exact: q.data?.exact ?? true, loading: q.isFetching, error: q.error }
}

// =====================================================================================================================
// Informe "Productos por posición" (servidor: `GET .../bin-products`). Lectura por tandas para el PDF (una posición por
// página con el código de barras de cada producto); lo usa `printBinProducts` (binProducts.ts) desde el modal de
// Ubicaciones. No va en caché: cada impresión lee los datos del momento.
// =====================================================================================================================
/** Lote 24: producto por código escaneado (código de barras exacto o SKU exacto): `GET /api/v1/products/by-barcode/{code}`. */
export const fetchProductByCode = (code: string) => unwrap(api.GET('/api/v1/products/by-barcode/{code}', { params: { path: { code } } }))

export type BinProductsPageDto = Schemas['BinProductsPageDto']
export type BinProductsDto = Schemas['BinProductsDto']
/** Filtros de `GET .../bin-products` (los mismos del listado de posiciones, con `skip`/`take` ≤ 200). */
export type BinProductsQuery = GetQuery<'/api/v1/warehouses/{publicId}/bin-products'>

/** `GET /api/v1/warehouses/{publicId}/bin-products`: una tanda de posiciones con sus productos (take ≤ 200; más de 200 = 400). */
export const fetchBinProducts = (publicId: string, query: BinProductsQuery, signal?: AbortSignal) =>
  unwrap(api.GET('/api/v1/warehouses/{publicId}/bin-products', { params: { path: { publicId }, query }, signal }))
