// Datos de Órdenes de transporte (manual 03). Lectura: `orders.view` + LTL_GROUND (el módulo lo aplica la ruta). Alta
// `orders.create` (con `confirmNow`: cotiza, revisa crédito y confirma en la misma transacción; `overrideCredit` exige
// `orders.credit_override`), eliminación `orders.cancel` (baja lógica solo en el estatus inicial). `quotedAmount` solo llega con
// `orders.view_price`; sin él viene null. Lecturas con `useQuery` (clave `[ruta, params]`); escrituras con `useMutation`.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { catalogKeys } from '../../kernel/catalogs'
import type { components, paths } from '../../kernel/api/schema'

type Schemas = components['schemas']

export type OrderListItemDto = Schemas['OrderListItemDto']
export type OrderDetailDto = Schemas['OrderDetailDto']
export type OrderCreateRequest = Schemas['OrderCreateRequest']
export type OrderPackageLineDto = Schemas['OrderPackageLineDto']
export type OrderPatchRequest = Schemas['OrderPatchRequest']
export type OrderQuotePreviewDto = Schemas['OrderQuotePreviewDto']
export type OrderPackageLineRequest = Schemas['OrderPackageLineRequest']
export type DriverListItemDto = Schemas['DriverListItemDto']
export type OrderListQuery = NonNullable<paths['/api/v1/orders']['get']['parameters']['query']>

/** Prefijos de consulta (los mismos que usa Almacén para sus órdenes: así una invalida a la otra). */
export const orderKeys = {
  list: ['/api/v1/orders'] as const,
  detail: ['/api/v1/orders/{publicId}'] as const,
  lookup: ['/api/v1/orders/lookup'] as const,
  quote: ['/api/v1/orders/{publicId}/quote'] as const,
  drivers: ['/api/v1/drivers'] as const,
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

const ORDER_ENTITY_TYPE = 'TRANSPORT_ORDER'

/** Lo que cambia tras cualquier acción sobre una orden: ficha (con sus capacidades), listas, búsqueda, cotización e historial de estatus. */
function invalidateOrder(qc: ReturnType<typeof useQueryClient>, orderId?: number | null) {
  void qc.invalidateQueries({ queryKey: orderKeys.detail })
  void qc.invalidateQueries({ queryKey: orderKeys.list })
  void qc.invalidateQueries({ queryKey: orderKeys.lookup })
  void qc.invalidateQueries({ queryKey: orderKeys.quote })
  if (orderId) void qc.invalidateQueries({ queryKey: catalogKeys.history(ORDER_ENTITY_TYPE, orderId) })
}

/** Cada intento de una acción lleva su propia `Idempotency-Key` (un reintento con otro cuerpo no debe confundirse con el primero). */
const idempotencyHeaders = () => ({ 'Idempotency-Key': crypto.randomUUID() })

/** `PATCH /api/v1/orders/{publicId}` (`orders.edit`): solo lo que cambió (null = no cambiar); `rowVersion` para la edición concurrente. */
export function useUpdateOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: OrderPatchRequest }) =>
      unwrap(api.PATCH('/api/v1/orders/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: (order) => invalidateOrder(qc, order.id),
  })
}

/**
 * `GET /api/v1/orders/{publicId}/quote` (`orders.view_price`): vista previa de la cotización y del crédito, sin guardar nada. Solo se
 * pide cuando `enabled` (al pulsar «Cotizar»); sin el permiso la consulta no se hace nunca (el 403 no debe verse en pantalla).
 */
export function useOrderQuote(publicId: string | null | undefined, enabled: boolean) {
  return useQuery({
    queryKey: [orderKeys.quote[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/orders/{publicId}/quote', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && enabled,
    retry: false,
    gcTime: 0,
    meta: { handleAccessDenied: false },
  })
}

/** `POST /orders/{id}/confirm` (`orders.edit`; con `overrideCredit`, `orders.credit_override`): cotiza, revisa el crédito y confirma. */
export function useConfirmOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, rowVersion, overrideCredit }: { publicId: string; rowVersion?: string | null; overrideCredit?: boolean }) =>
      unwrap(
        api.POST('/api/v1/orders/{publicId}/confirm', {
          params: { path: { publicId } },
          body: { rowVersion: rowVersion ?? null, ...(overrideCredit ? { overrideCredit: true } : {}) },
          headers: idempotencyHeaders(),
        }),
      ),
    onSuccess: (order) => invalidateOrder(qc, order.id),
  })
}

/** `POST /orders/{id}/reprice` (`orders.edit`): vuelve a cotizar con las tarifas vigentes, sin cambiar de estatus. */
export function useRepriceOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/orders/{publicId}/reprice', { params: { path: { publicId } }, headers: idempotencyHeaders() })),
    onSuccess: (order) => invalidateOrder(qc, order.id),
  })
}

/** `POST /orders/{id}/cancel` (`orders.cancel`): cancela con un comentario opcional (máx. 500). */
export function useCancelOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, comment, rowVersion }: { publicId: string; comment?: string; rowVersion?: string | null }) =>
      unwrap(api.POST('/api/v1/orders/{publicId}/cancel', { params: { path: { publicId } }, body: { comment: comment ?? null, rowVersion: rowVersion ?? null }, headers: idempotencyHeaders() })),
    onSuccess: (order) => invalidateOrder(qc, order.id),
  })
}

/** `POST /orders/{id}/status` (`orders.edit`): estatus laterales (en espera, parcial, fallida) y el regreso a la etapa de la que salió. */
export function useOrderStatusChange() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, toCode, comment }: { publicId: string; toCode: string; comment?: string }) =>
      unwrap(api.POST('/api/v1/orders/{publicId}/status', { params: { path: { publicId } }, body: { toCode, comment: comment ?? null }, headers: idempotencyHeaders() })),
    onSuccess: (order) => invalidateOrder(qc, order.id),
  })
}

/** `POST /orders/{id}/driver` (`trips.dispatch`, módulo CATALOG): asigna o reasigna el chofer de una entrega especial. */
export function useAssignOrderDriver() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, driverPublicId, comment, overrideCredit, rowVersion }: { publicId: string; driverPublicId: string; comment?: string; overrideCredit?: boolean; rowVersion?: string | null }) =>
      unwrap(
        api.POST('/api/v1/orders/{publicId}/driver', {
          params: { path: { publicId } },
          body: { driverPublicId, comment: comment || null, rowVersion: rowVersion ?? null, ...(overrideCredit ? { overrideCredit: true } : {}) },
          headers: idempotencyHeaders(),
        }),
      ),
    onSuccess: (order) => invalidateOrder(qc, order.id),
  })
}

/**
 * `GET /api/v1/drivers` (activos; `fleet.view`): para elegir el chofer de una entrega especial. Consulta secundaria: un 403 no saca de la
 * pantalla (el diálogo avisa que no puede cargar la lista).
 */
export function useDrivers(enabled: boolean) {
  return useQuery({
    queryKey: [orderKeys.drivers[0], { includeInactive: false }],
    queryFn: () => unwrap(api.GET('/api/v1/drivers', { params: { query: { includeInactive: false } } })),
    enabled,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}
