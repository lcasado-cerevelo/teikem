// Datos del «Aviso de salida» (Lote 144, 2026-10-20; módulo WMS_LOTSERIAL): lista, ficha, alta, cantidad de una línea, cancelar y el pendiente (reservar una línea o todo, cerrar con
// faltante). El servidor es `ExitNoticesController` (manual 40). Cualquier cambio refresca los avisos y los saldos/opciones de salida (la reserva cambia lo disponible).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { warehouseKeys } from './api'

type Schemas = components['schemas']
export type ExitNoticeDto = Schemas['ExitNoticeDto']
export type ExitNoticeListItemDto = Schemas['ExitNoticeListItemDto']
export type ExitNoticeLineDto = Schemas['ExitNoticeLineDto']
export type ExitNoticeRequest = Schemas['ExitNoticeRequest']
export type ExitNoticeBackorderLineDto = Schemas['ExitNoticeBackorderLineDto']
export type ExitNoticeDispatchRequest = Schemas['ExitNoticeDispatchRequest']

export const exitNoticeKeys = {
  all: ['exit-notices'] as const,
  list: (f: ListQuery, skip: number, take: number) => ['exit-notices', 'list', f, skip, take] as const,
  one: (id: string) => ['exit-notices', 'one', id] as const,
}

/** `status`: códigos separados por coma (selección múltiple). */
export interface ListQuery {
  status: string
  q: string
  backorderOnly: boolean
  clientPublicId?: string | null
  from?: string
  to?: string
  /** Lote 171: solo los asignados a mí. */
  assignedToMe?: boolean
}

const queryOf = (f: ListQuery, skip: number, take: number) => ({
  status: f.status || undefined,
  clientPublicId: f.clientPublicId || undefined,
  q: f.q || undefined,
  backorderOnly: f.backorderOnly || undefined,
  from: f.from || undefined,
  to: f.to || undefined,
  assignedToMe: f.assignedToMe || undefined,
  skip,
  take,
})

export function useExitNotices(f: ListQuery, skip: number, take: number) {
  return useQuery({
    queryKey: exitNoticeKeys.list(f, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/exit-notices', { params: { query: queryOf(f, skip, take) } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function exitNoticePageReader(f: ListQuery) {
  return (skip: number, take: number) =>
    unwrap(api.GET('/api/v1/exit-notices', { params: { query: queryOf(f, skip, take) } }))
}

export function useExitNotice(publicId: string | null) {
  return useQuery({
    queryKey: exitNoticeKeys.one(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/exit-notices/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useExitNoticeMutations() {
  const qc = useQueryClient()
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: exitNoticeKeys.all })
    void qc.invalidateQueries({ queryKey: [warehouseKeys.balances[0]] })   // la reserva cambia lo disponible y el orden de salida
  }
  return {
    create: useMutation({ mutationFn: (body: ExitNoticeRequest) => unwrap(api.POST('/api/v1/exit-notices', { body })), onSettled: refresh }),
    modifyLine: useMutation({
      mutationFn: (v: { id: string; lineId: number; quantity: number }) =>
        unwrap(api.PATCH('/api/v1/exit-notices/{publicId}/lines/{lineId}', { params: { path: { publicId: v.id, lineId: v.lineId } }, body: { quantity: v.quantity } })),
      onSettled: refresh,
    }),
    cancel: useMutation({
      mutationFn: (v: { id: string; comment?: string }) => unwrap(api.POST('/api/v1/exit-notices/{publicId}/cancel', { params: { path: { publicId: v.id } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
    fulfillLine: useMutation({
      mutationFn: (v: { id: string; lineId: number }) => unwrap(api.POST('/api/v1/exit-notices/{publicId}/lines/{lineId}/fulfill', { params: { path: { publicId: v.id, lineId: v.lineId } } })),
      onSettled: refresh,
    }),
    fulfillAll: useMutation({ mutationFn: () => unwrap(api.POST('/api/v1/exit-notices/backorders/fulfill')), onSettled: refresh }),
    closeLine: useMutation({
      mutationFn: (v: { id: string; lineId: number; comment?: string }) =>
        unwrap(api.POST('/api/v1/exit-notices/{publicId}/lines/{lineId}/close', { params: { path: { publicId: v.id, lineId: v.lineId } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
    assign: useMutation({
      mutationFn: (v: { id: string; userId: number | null }) => unwrap(api.POST('/api/v1/exit-notices/{publicId}/assign', { params: { path: { publicId: v.id } }, body: { userId: v.userId } })),
      onSettled: refresh,
    }),
    take: useMutation({
      mutationFn: (id: string) => unwrap(api.POST('/api/v1/exit-notices/{publicId}/take', { params: { path: { publicId: id } } })),
      onSettled: refresh,
    }),
    dispatch: useMutation({
      mutationFn: (v: { id: string; body: ExitNoticeDispatchRequest }) =>
        unwrap(api.POST('/api/v1/exit-notices/{publicId}/dispatch', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
  }
}
