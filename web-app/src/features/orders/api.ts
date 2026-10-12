// Datos de Órdenes de transporte (manual 03). Lectura: `orders.view` + LTL_GROUND (el módulo lo aplica la ruta). Alta
// `orders.create` (con `confirmNow`: cotiza, revisa crédito y confirma en la misma transacción; `overrideCredit` exige
// `orders.credit_override`), eliminación `orders.cancel` (baja lógica solo en el estatus inicial). `quotedAmount` solo llega con
// `orders.view_price`; sin él viene null. Lecturas con `useQuery` (clave `[ruta, params]`); escrituras con `useMutation`.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import type { components, paths } from '../../kernel/api/schema'

type Schemas = components['schemas']

export type OrderListItemDto = Schemas['OrderListItemDto']
export type OrderDetailDto = Schemas['OrderDetailDto']
export type OrderCreateRequest = Schemas['OrderCreateRequest']
export type OrderPackageLineDto = Schemas['OrderPackageLineDto']
export type OrderListQuery = NonNullable<paths['/api/v1/orders']['get']['parameters']['query']>

/** Prefijos de consulta (los mismos que usa Almacén para sus órdenes: así una invalida a la otra). */
export const orderKeys = {
  list: ['/api/v1/orders'] as const,
  detail: ['/api/v1/orders/{publicId}'] as const,
  lookup: ['/api/v1/orders/lookup'] as const,
}

export interface OrderQueryOptions {
  enabled?: boolean
  /** false = un 403 no saca de la pantalla (consulta secundaria). */
  handleAccessDenied?: boolean
}

function meta(options?: OrderQueryOptions) {
  return options?.handleAccessDenied === false ? { handleAccessDenied: false } : undefined
}

/** `GET /api/v1/orders` (paginado; filtros repetibles `serviceTypes`, `packageTypes`, `cities`, `clientIds`, `statuses`). */
export function useOrders(query: OrderListQuery = {}, options?: OrderQueryOptions) {
  return useQuery({
    queryKey: [orderKeys.list[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/orders', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** `GET /api/v1/orders/{publicId}` (`OrderDetailDto`). */
export function useOrder(publicId: string | null | undefined, options?: OrderQueryOptions) {
  return useQuery({
    queryKey: [orderKeys.detail[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/orders/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** Todas las órdenes de la consulta (mismos filtros, de a 200, sin tope de cantidad): `DataTable.exportRows`. */
export const exportOrders = (query: OrderListQuery) =>
  fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/orders', { params: { query: { ...query, skip, take } } })))

/**
 * `POST /api/v1/orders` con `Idempotency-Key` (la misma clave en un reintento del mismo intento no duplica). Devuelve la ficha
 * de la orden creada; con `confirmNow` ya viene confirmada. Invalida la lista.
 */
export function useCreateOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ body, idempotencyKey }: { body: OrderCreateRequest; idempotencyKey: string }) =>
      unwrap(api.POST('/api/v1/orders', { body, headers: { 'Idempotency-Key': idempotencyKey } })),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: orderKeys.list })
      void qc.invalidateQueries({ queryKey: orderKeys.lookup })
    },
  })
}

/** `DELETE /api/v1/orders/{publicId}` (`orders.cancel`): baja lógica, solo en el estatus inicial; el servidor responde 204. */
export function useDeleteOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (publicId: string) => {
      await unwrap(api.DELETE('/api/v1/orders/{publicId}', { params: { path: { publicId } } }))
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: orderKeys.list })
      void qc.invalidateQueries({ queryKey: orderKeys.detail })
      void qc.invalidateQueries({ queryKey: orderKeys.lookup })
    },
  })
}
