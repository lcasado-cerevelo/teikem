// Datos del módulo de almacén (manual 06) y de la consulta de órdenes de solo lectura (Lote F6).
// Lecturas: `useQuery` con clave `[ruta, params]` (la ruta tal como está en schema.d.ts). Escrituras: `useMutation` que
// invalida la lista de su entidad (prefijo `[ruta]`) y, si cambia saldos, las consultas de inventario que dependen de ellos.
// Permisos y módulos (los aplica el API; aquí solo se documentan): lecturas con `inventory.view` + WMS_LOTSERIAL (compras:
// `purchasing.view` + PURCHASING; citas y cruce de muelle: `inventory.view` + CROSSDOCK; órdenes: `orders.view` + LTL_GROUND).
import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components, paths } from '../../kernel/api/schema'

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
export type ReconciliationDto = Schemas['ReconciliationDto']
export type GenealogyDto = Schemas['GenealogyDto']
export type SerialTraceDto = Schemas['SerialTraceDto']
export type AsnDto = Schemas['AsnDto']
export type ReceiptListItemDto = Schemas['ReceiptListItemDto']
export type ReceiptDetailDto = Schemas['ReceiptDetailDto']
export type WarehouseTaskDto = Schemas['WarehouseTaskDto']
export type PutawaySuggestionDto = Schemas['PutawaySuggestionDto']
export type CycleCountDto = Schemas['CycleCountDto']
export type CycleCountDetailDto = Schemas['CycleCountDetailDto']
export type PickBatchDto = Schemas['PickBatchDto']
export type SupplierDto = Schemas['SupplierDto']
export type PurchaseOrderDto = Schemas['PurchaseOrderDto']
export type PoShortageSummaryDto = Schemas['PoShortageSummaryDto']
export type ShortageLineDto = Schemas['ShortageLineDto']
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
  productCategories: ['/api/v1/product-categories'],
  balances: ['/api/v1/inventory/balances'],
  transactions: ['/api/v1/inventory/transactions'],
  reconciliation: ['/api/v1/inventory/reconciliation'],
  genealogy: ['/api/v1/inventory/lots/{lotId}/genealogy'],
  serialTrace: ['/api/v1/inventory/serials/trace'],
  asns: ['/api/v1/asns'],
  receipts: ['/api/v1/receipts'],
  receipt: ['/api/v1/receipts/{publicId}'],
  tasks: ['/api/v1/warehouse-tasks'],
  putawaySuggestions: ['/api/v1/warehouse-tasks/putaway-suggestions'],
  cycleCounts: ['/api/v1/cycle-counts'],
  cycleCount: ['/api/v1/cycle-counts/{id}'],
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
const STOCK: KeyName[] = ['balances', 'transactions', 'reconciliation', 'products', 'product', 'warehouses', 'warehouse', 'bins', 'productLots', 'productSerials']

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
    onSuccess: () => invalidate(qc, 'products'),
  })
}

/** `PATCH /api/v1/products/{publicId}` (`inventory.manage`; 409 si cambia seguimiento/UOM/dueño con movimientos). */
export function useUpdateProduct() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: Schemas['ProductPatchRequest'] }) =>
      unwrap(api.PATCH('/api/v1/products/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: () => invalidate(qc, 'products', 'product', 'balances'),
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

/** `POST /api/v1/receipts` (`warehouse.receive`; contra PO además `purchasing.receive`). */
export function useCreateReceipt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['ReceiptCreateRequest']) => unwrap(api.POST('/api/v1/receipts', { body })),
    onSuccess: () => invalidate(qc, 'receipts', 'asns', 'purchaseOrders', 'purchaseOrder'),
  })
}

/** Líneas del recibo abierto: agregar, capturar (`PUT`) y quitar (`warehouse.receive`). */
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
    onSuccess: () => invalidate(qc, 'receipt', 'receipts'),
  })
}

/** `POST /api/v1/receipts/{publicId}/confirm` (OPEN → RECEIVED: mueve inventario y genera tareas PUTAWAY). */
export function useConfirmReceipt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body = {} }: { publicId: string; body?: Schemas['ReceiptConfirmRequest'] }) =>
      unwrap(api.POST('/api/v1/receipts/{publicId}/confirm', { params: { path: { publicId } }, body })),
    onSuccess: () =>
      invalidate(qc, 'receipts', 'receipt', 'asns', 'tasks', 'purchaseOrders', 'purchaseOrder', 'purchaseOrderShortages', 'purchaseOrderShortageLines', 'crossDockCandidates', ...STOCK),
  })
}

/** `DELETE /api/v1/receipts/{publicId}` (solo OPEN sin cruce de muelle asignado). */
export function useDeleteReceipt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (publicId: string) => {
      await unwrap(api.DELETE('/api/v1/receipts/{publicId}', { params: { path: { publicId } } }))
    },
    onSuccess: () => invalidate(qc, 'receipts', 'receipt', 'asns', 'purchaseOrders', 'purchaseOrder'),
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

/** Acciones sobre una tarea: asignar (`warehouse.manage`; `userId` null desasigna), iniciar, completar y cancelar. */
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
    onSuccess: (_data, v) => (v.action === 'complete' ? invalidate(qc, 'tasks', 'putawaySuggestions', ...STOCK) : invalidate(qc, 'tasks')),
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

/** `POST /api/v1/cycle-counts` (`warehouse.count`; máx. 1000 líneas). */
export function useCreateCycleCount() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['CycleCountCreateRequest']) => unwrap(api.POST('/api/v1/cycle-counts', { body })),
    onSuccess: () => invalidate(qc, 'cycleCounts', 'tasks'),
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
    onSuccess: (_data, v) =>
      v.action === 'reconcile' ? invalidate(qc, 'cycleCounts', 'cycleCount', 'tasks', ...STOCK) : invalidate(qc, 'cycleCounts', 'cycleCount', 'tasks'),
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
