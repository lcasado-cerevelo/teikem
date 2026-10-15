// Datos de «Órdenes de venta» (Lote 60; módulo SALES_ORDERS): lista, ficha, borrador, solicitar (con la comprobación de disponibilidad y crédito), modificar una
// línea, cancelar y los backorders (ver, surtir, cerrar con faltante).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

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

export const salesKeys = {
  all: ['sales'] as const,
  list: (status: string, q: string, skip: number, take: number) => ['sales', '/api/v1/sales-orders', status, q, skip, take] as const,
  one: (id: string) => ['sales', 'one', id] as const,
  backorders: (q: string) => ['sales', '/api/v1/sales-orders/backorders', q] as const,
}

export function useSalesOrders(status: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: salesKeys.list(status, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function salesOrderPageReader(status: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/sales-orders', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } }))
}

export function useSalesOrder(publicId: string | null) {
  return useQuery({
    queryKey: salesKeys.one(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
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
    fulfillAll: useMutation({ mutationFn: () => unwrap(api.POST('/api/v1/sales-orders/backorders/fulfill')), onSettled: refresh }),
    closeLine: useMutation({
      mutationFn: (v: { id: string; lineId: number; comment?: string }) =>
        unwrap(api.POST('/api/v1/sales-orders/{publicId}/lines/{lineId}/close', { params: { path: { publicId: v.id, lineId: v.lineId } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
  }
}
