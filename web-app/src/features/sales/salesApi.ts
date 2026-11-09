// Datos de «Órdenes de venta» (Lote 60; módulo SALES_ORDERS): lista, ficha, borrador, solicitar (con la comprobación de disponibilidad y crédito), modificar una
// línea, cancelar y los backorders (ver, surtir, cerrar con faltante).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components, paths } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type SalesOrderDto = Schemas['SalesOrderDto']
export type SalesOrderListItemDto = Schemas['SalesOrderListItemDto']
export type SalesOrderLineDto = Schemas['SalesOrderLineDto']
export type SalesOrderRequest = Schemas['SalesOrderRequest']
export type SalesOrderLineRequest = Schemas['SalesOrderLineRequest']
export type SalesAvailabilityDto = Schemas['SalesAvailabilityDto']
export type SalesBackorderLineDto = Schemas['SalesBackorderLineDto']
export type SalesFulfillResultDto = Schemas['SalesFulfillResultDto']
export type SalesDispatchInfoDto = Schemas['SalesDispatchInfoDto']
export type SalesDispatchRequest = Schemas['SalesDispatchRequest']
export type SalesDispatchResultDto = Schemas['SalesDispatchResultDto']

/** Consulta de GET /api/v1/sales-orders (sin skip/take). */
export type SalesOrderListQuery = Omit<NonNullable<paths['/api/v1/sales-orders']['get']['parameters']['query']>, 'skip' | 'take'>

/** Qué fecha filtra el rango: la de solicitud (por omisión) o la requerida. */
export type SalesDateField = 'REQUESTED' | 'REQUIRED'

/**
 * Filtros de la lista de órdenes de venta (2026-11-09): buscador libre, rango de fechas (solicitada o requerida), varios clientes, productos y estatus,
 * y «solo los asignados a mí». Vacío = sin ese filtro.
 */
export interface SalesOrderFilters {
  q?: string
  dateField?: SalesDateField
  from?: string
  to?: string
  clientPublicIds?: readonly string[]
  productPublicIds?: readonly string[]
  statuses?: readonly string[]
  mine?: boolean
}

const many = (v: readonly string[] | undefined) => (v && v.length > 0 ? [...v] : undefined)

/** Arma la consulta del API: los filtros repetibles van como `status=…&status=…`, `clientPublicId=…`, `productPublicId=…`. */
export function salesOrderListQuery(f: SalesOrderFilters): SalesOrderListQuery {
  const hasRange = Boolean(f.from || f.to)
  return {
    q: f.q || undefined,
    status: many(f.statuses),
    clientPublicId: many(f.clientPublicIds),
    productPublicId: many(f.productPublicIds),
    from: f.from || undefined,
    to: f.to || undefined,
    // la fecha requerida solo viaja si hay rango (sin rango no filtra nada)
    dateField: hasRange && f.dateField === 'REQUIRED' ? 'REQUIRED' : undefined,
    assignedToMe: f.mine || undefined,
  }
}

export const salesKeys = {
  all: ['sales'] as const,
  list: (query: SalesOrderListQuery, skip: number, take: number) => ['sales', '/api/v1/sales-orders', query, skip, take] as const,
  one: (id: string) => ['sales', 'one', id] as const,
  backorders: (q: string) => ['sales', '/api/v1/sales-orders/backorders', q] as const,
}

export function useSalesOrders(filters: SalesOrderFilters, skip: number, take: number) {
  const query = salesOrderListQuery(filters)
  return useQuery({
    queryKey: salesKeys.list(query, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders', { params: { query: { ...query, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla y el orden de TODO lo filtrado). */
export function salesOrderPageReader(filters: SalesOrderFilters) {
  const query = salesOrderListQuery(filters)
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/sales-orders', { params: { query: { ...query, skip, take } } }))
}

export function useSalesOrder(publicId: string | null) {
  return useQuery({
    queryKey: salesKeys.one(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

/**
 * Lote 230 — `GET /sales-orders/kits/{kitPublicId}/substitutes` (`sales.create`, módulo SALES_ORDERS): cada pieza del catálogo del kit con «no
 * sustituible» y sus sustitutos válidos (misma categoría y mismo precio). Sirve antes de guardar la línea; un 403 no saca de la pantalla.
 */
export function useKitSubstitutes(kitPublicId: string | null | undefined, enabled = true) {
  return useQuery({
    queryKey: ['/api/v1/sales-orders/kits/{kitPublicId}/substitutes', kitPublicId ?? ''] as const,
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders/kits/{kitPublicId}/substitutes', { params: { path: { kitPublicId: kitPublicId ?? '' } } })),
    enabled: Boolean(kitPublicId) && enabled,
    staleTime: 60 * 1000,
    meta: { handleAccessDenied: false },
  })
}

export function useBackorders(q: string) {
  return useQuery({ queryKey: salesKeys.backorders(q), queryFn: () => unwrap(api.GET('/api/v1/sales-orders/backorders', { params: { query: { q: q || undefined } } })) })
}

export function useSalesMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: salesKeys.all })
  return {
    create: useMutation({ mutationFn: (body: SalesOrderRequest) => unwrap(api.POST('/api/v1/sales-orders', { body })), onSettled: refresh }),
    update: useMutation({
      mutationFn: (v: { id: string; body: SalesOrderRequest }) => unwrap(api.PATCH('/api/v1/sales-orders/{publicId}', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    check: useMutation({ mutationFn: (id: string) => unwrap(api.GET('/api/v1/sales-orders/{publicId}/availability', { params: { path: { publicId: id } } })) }),
    request: useMutation({
      mutationFn: (v: { id: string; allowBackorder?: boolean; overrideCredit?: boolean }) =>
        unwrap(api.POST('/api/v1/sales-orders/{publicId}/request', { params: { path: { publicId: v.id } }, body: { allowBackorder: v.allowBackorder, overrideCredit: v.overrideCredit } })),
      onSettled: refresh,
    }),
    cancel: useMutation({
      mutationFn: (v: { id: string; comment?: string }) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/cancel', { params: { path: { publicId: v.id } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
    modifyLine: useMutation({
      mutationFn: (v: { id: string; lineId: number; quantity?: number; unitPrice?: number; allowBackorder?: boolean }) =>
        unwrap(
          api.PATCH('/api/v1/sales-orders/{publicId}/lines/{lineId}', {
            params: { path: { publicId: v.id, lineId: v.lineId } },
            body: { quantity: v.quantity, unitPrice: v.unitPrice, allowBackorder: v.allowBackorder },
          }),
        ),
      onSettled: refresh,
    }),
    fulfillLine: useMutation({
      mutationFn: (v: { id: string; lineId: number }) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/lines/{lineId}/fulfill', { params: { path: { publicId: v.id, lineId: v.lineId } } })),
      onSettled: refresh,
    }),
    dispatch: useMutation({
      mutationFn: (v: { id: string; body: SalesDispatchRequest }) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/dispatch', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    deliver: useMutation({
      mutationFn: (v: { id: string; pickBatchPublicId: string; order: NonNullable<Schemas['SalesDeliveryRequest']['order']> }) =>
        unwrap(api.POST('/api/v1/sales-orders/{publicId}/delivery', { params: { path: { publicId: v.id } }, body: { pickBatchPublicId: v.pickBatchPublicId, order: v.order } })),
      onSettled: refresh,
    }),
    // Lote 195: asignar la orden de venta a quien la va a despachar (warehouse.manage) o tomarla (warehouse.pick)
    assign: useMutation({
      mutationFn: (v: { id: string; userId: number | null }) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/assign', { params: { path: { publicId: v.id } }, body: { userId: v.userId } })),
      onSettled: refresh,
    }),
    take: useMutation({ mutationFn: (id: string) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/take', { params: { path: { publicId: id } } })), onSettled: refresh }),
    fulfillAll: useMutation({ mutationFn: () => unwrap(api.POST('/api/v1/sales-orders/backorders/fulfill')), onSettled: refresh }),
    closeLine: useMutation({
      mutationFn: (v: { id: string; lineId: number; comment?: string }) =>
        unwrap(api.POST('/api/v1/sales-orders/{publicId}/lines/{lineId}/close', { params: { path: { publicId: v.id, lineId: v.lineId } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
  }
}
