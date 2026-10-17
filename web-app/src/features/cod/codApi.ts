// Datos de «COD» (Lote 85, módulo COD): órdenes con COD, cobro, reconciliación por códigos y corridas.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type CodOrderListItemDto = Schemas['CodOrderListItemDto']
export type CodOrderDto = Schemas['CodOrderDto']
export type CodCollectionDto = Schemas['CodCollectionDto']
export type CodCollectionRequest = Schemas['CodCollectionRequest']
export type CodReconcileRequest = Schemas['CodReconcileRequest']
export type CodReconcileResultDto = Schemas['CodReconcileResultDto']
export type CodReconcileItemDto = Schemas['CodReconcileItemDto']
export type CodRunListItemDto = Schemas['CodRunListItemDto']
export type CodRunDto = Schemas['CodRunDto']

export const codKeys = {
  all: ['cod'] as const,
  orders: (status: string, q: string, skip: number, take: number) => ['cod', 'orders', status, q, skip, take] as const,
  order: (id: string | null) => ['cod', 'order', id] as const,
  runs: (skip: number, take: number) => ['cod', 'runs', skip, take] as const,
  run: (id: string | null) => ['cod', 'run', id] as const,
}

export function useCodOrders(status: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: codKeys.orders(status, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/cod/orders', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function codOrderPageReader(status: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/cod/orders', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } }))
}

export function useCodOrder(publicId: string | null) {
  return useQuery({
    queryKey: codKeys.order(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/cod/orders/{orderPublicId}', { params: { path: { orderPublicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useCodRuns(skip: number, take: number) {
  return useQuery({
    queryKey: codKeys.runs(skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/cod/runs', { params: { query: { skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

export function useCodRun(publicId: string | null) {
  return useQuery({
    queryKey: codKeys.run(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/cod/runs/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useCodMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: codKeys.all })
  return {
    collect: useMutation({
      mutationFn: (v: { id: string; body: CodCollectionRequest }) => unwrap(api.POST('/api/v1/cod/orders/{orderPublicId}/collections', { params: { path: { orderPublicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    /** Con `dryRun` solo simula (no cambia nada); sin él cuadra y guarda la corrida. */
    reconcile: useMutation({
      mutationFn: (body: CodReconcileRequest) => unwrap(api.POST('/api/v1/cod/reconcile', { body })),
      onSuccess: (_data, vars) => {
        if (vars.dryRun !== true) void refresh()
      },
    }),
  }
}
