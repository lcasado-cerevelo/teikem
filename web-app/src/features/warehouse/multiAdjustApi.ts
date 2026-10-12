// Ajuste múltiple AJM-###### (2026-10-12): datos de la pantalla. Todas las rutas exigen `inventory.adjust.multi` (+ WMS_LOTSERIAL).
// - `GET /inventory/multi-adjustments/stock?warehouse=&products=`: posiciones con existencia (llena los bloques).
// - `POST /inventory/multi-adjustments`: aplica el documento; 409 `stale_system_qty` / `insufficient_stock` traen `lines[i]`.
// - `GET /inventory/multi-adjustments` y `/{publicId}`: historial y detalle.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components, paths } from '../../kernel/api/schema'
import { invalidateStock } from './api'

type Schemas = components['schemas']

export type MultiAdjustmentDetailDto = Schemas['MultiAdjustmentDetailDto']
export type MultiAdjustmentDto = Schemas['MultiAdjustmentDto']
export type MultiAdjustmentLineDto = Schemas['MultiAdjustmentLineDto']
export type MultiAdjustmentRequest = Schemas['MultiAdjustmentRequest']
export type MultiAdjustmentQuery = NonNullable<paths['/api/v1/inventory/multi-adjustments']['get']['parameters']['query']>

export const multiAdjustKeys = {
  stock: ['/api/v1/inventory/multi-adjustments/stock'] as const,
  list: ['/api/v1/inventory/multi-adjustments'] as const,
  detail: ['/api/v1/inventory/multi-adjustments/{publicId}'] as const,
}

/** Existencias por posición de los productos en el almacén (`stock`); se pide de nuevo con `refetch` tras un 409. */
export function fetchMultiAdjustStock(warehouse: string, products: readonly string[], signal?: AbortSignal) {
  return unwrap(api.GET('/api/v1/inventory/multi-adjustments/stock', { params: { query: { warehouse, products: [...products] } }, signal }))
}

/** `POST /api/v1/inventory/multi-adjustments`: invalida saldos, Kárdex y el historial. */
export function useCreateMultiAdjustment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: MultiAdjustmentRequest) => unwrap(api.POST('/api/v1/inventory/multi-adjustments', { body })),
    onSuccess: () => Promise.all([invalidateStock(qc), qc.invalidateQueries({ queryKey: multiAdjustKeys.list })]),
  })
}

/** Historial paginado por el servidor. */
export function useMultiAdjustments(query: MultiAdjustmentQuery) {
  return useQuery({
    queryKey: [multiAdjustKeys.list[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/multi-adjustments', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

/** Detalle de un documento: cada línea con En sistema / Hay ahora / Diferencia. */
export function useMultiAdjustment(publicId: string | null | undefined) {
  return useQuery({
    queryKey: [multiAdjustKeys.detail[0], { publicId }],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/multi-adjustments/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId),
  })
}
