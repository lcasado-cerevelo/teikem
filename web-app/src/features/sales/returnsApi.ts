// Datos de «Devoluciones de venta» (Lote 82, módulo SALES_ORDERS): lista, ficha, lo devolvible de una orden, pedir, autorizar, rechazar, cancelar, recibir (con lo bueno y lo dañado), acreditar y la ventana de devolución.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type SalesReturnDto = Schemas['SalesReturnDto']
export type SalesReturnListItemDto = Schemas['SalesReturnListItemDto']
export type SalesReturnLineDto = Schemas['SalesReturnLineDto']
export type SalesReturnRequest = Schemas['SalesReturnRequest']
export type SalesReturnReceiveRequest = Schemas['SalesReturnReceiveRequest']
export type ReturnableDto = Schemas['ReturnableDto']
export type ReturnableLineDto = Schemas['ReturnableLineDto']

export const returnKeys = {
  all: ['sales-returns'] as const,
  list: (status: string, q: string, skip: number, take: number) => ['sales-returns', 'list', status, q, skip, take] as const,
  one: (id: string | null) => ['sales-returns', 'one', id] as const,
  returnable: (orderId: string | null) => ['sales-returns', 'returnable', orderId] as const,
  settings: ['sales-returns', 'settings'] as const,
}

export function useSalesReturns(status: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: returnKeys.list(status, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/sales-returns', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function salesReturnPageReader(status: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/sales-returns', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } }))
}

export function useSalesReturn(publicId: string | null) {
  return useQuery({
    queryKey: returnKeys.one(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/sales-returns/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useReturnable(orderPublicId: string | null) {
  return useQuery({
    queryKey: returnKeys.returnable(orderPublicId),
    queryFn: () => unwrap(api.GET('/api/v1/sales-returns/returnable', { params: { query: { salesOrderPublicId: orderPublicId! } } })),
    enabled: orderPublicId !== null,
    gcTime: 0,
  })
}

export function useReturnSettings() {
  return useQuery({ queryKey: returnKeys.settings, queryFn: () => unwrap(api.GET('/api/v1/sales-returns/settings')) })
}

export function useReturnMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: returnKeys.all })
  const refreshBilling = () => qc.invalidateQueries({ queryKey: ['billing'] })
  return {
    create: useMutation({ mutationFn: (body: SalesReturnRequest) => unwrap(api.POST('/api/v1/sales-returns', { body })), onSettled: refresh }),
    authorize: useMutation({
      mutationFn: (v: { id: string; note?: string }) => unwrap(api.POST('/api/v1/sales-returns/{publicId}/authorize', { params: { path: { publicId: v.id } }, body: { note: v.note } })),
      onSettled: refresh,
    }),
    reject: useMutation({
      mutationFn: (v: { id: string; reason: string }) => unwrap(api.POST('/api/v1/sales-returns/{publicId}/reject', { params: { path: { publicId: v.id } }, body: { reason: v.reason } })),
      onSettled: refresh,
    }),
    cancel: useMutation({
      mutationFn: (v: { id: string; comment?: string }) => unwrap(api.POST('/api/v1/sales-returns/{publicId}/cancel', { params: { path: { publicId: v.id } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
    receive: useMutation({
      mutationFn: (v: { id: string; body: SalesReturnReceiveRequest }) => unwrap(api.POST('/api/v1/sales-returns/{publicId}/receive', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: () => {
        refresh()
        void qc.invalidateQueries({ queryKey: ['sales'] })
      },
    }),
    credit: useMutation({
      mutationFn: (id: string) => unwrap(api.POST('/api/v1/sales-returns/{publicId}/credit', { params: { path: { publicId: id } } })),
      onSettled: () => {
        refresh()
        refreshBilling()
      },
    }),
    updateSettings: useMutation({
      mutationFn: (windowDays: number) => unwrap(api.PUT('/api/v1/sales-returns/settings', { body: { windowDays } })),
      onSuccess: (data) => qc.setQueryData(returnKeys.settings, data),
    }),
  }
}
