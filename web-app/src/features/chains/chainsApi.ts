// Lote 245 — datos de las cadenas de clientes (/api/v1/client-chains y /sales-analysis/by-chain). Lecturas con `useQuery` (clave `[ruta, params]`) y
// escrituras con `useMutation` que invalidan la lista, la ficha, los clientes de la cadena y las listas que muestran la cadena (Clientes y Órdenes).
// Ver: sales.chain|clients.read|sales.view; administrar: sales.chain. Módulo Órdenes de venta.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { clientKeys } from '../clients/api'

type Schemas = components['schemas']
export type ChainListItem = Schemas['ClientChainListItemDto']
export type ChainDetail = Schemas['ClientChainDetailDto']
export type ChainRequest = Schemas['ClientChainRequest']
export type ChainClient = Schemas['ClientChainClientDto']
export type ChainAssignRequest = Schemas['ClientChainAssignRequest']
export type ChainAssignResult = Schemas['ClientChainAssignResultDto']
export type ChainSkipped = Schemas['ClientChainSkippedDto']
export type ChainMembership = Schemas['ClientChainMembershipDto']
export type ChainBulkRequest = Schemas['ClientChainBulkRequest']
export type ChainBulkPreview = Schemas['ClientChainBulkPreviewDto']
export type ChainBulkRow = Schemas['ClientChainBulkRowDto']
export type ChainBulkResult = Schemas['ClientChainBulkResultDto']
export type ChainSales = Schemas['ClientChainSalesDto']
export type ChainSalesRow = Schemas['ClientChainSalesRowDto']
export type ChainSalesClient = Schemas['ClientChainSalesClientDto']
export type ChainSeries = Schemas['ClientChainSeriesDto']

export const CHAIN_VIEW = 'sales.chain|clients.read|sales.view'
export const CHAIN_MANAGE = 'sales.chain'

export const chainKeys = {
  all: ['chains'] as const,
  list: (includeInactive: boolean, q: string) => ['chains', '/api/v1/client-chains', { includeInactive, q }] as const,
  one: (id: string) => ['chains', 'one', id] as const,
  clients: (id: string, q: string, includeInactive: boolean, skip: number, take: number) => ['chains', 'clients', id, { q, includeInactive, skip, take }] as const,
  clientChain: (clientPublicId: string) => ['chains', 'by-client', clientPublicId] as const,
  sales: (month: string, compare: string, chain: string) => ['chains', 'sales', { month, compare, chain }] as const,
  series: (chain: string) => ['chains', 'series', chain] as const,
}

// ---------------------------------------------------------------- lecturas

/** Lista de cadenas. `enabled` = false evita la consulta (p. ej. sin el módulo). */
export function useChains(includeInactive = false, q = '', enabled = true) {
  return useQuery({
    queryKey: chainKeys.list(includeInactive, q),
    queryFn: () => unwrap(api.GET('/api/v1/client-chains', { params: { query: { includeInactive, q: q || undefined } } })),
    enabled,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

export function useChain(publicId: string | null) {
  return useQuery({
    queryKey: chainKeys.one(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/client-chains/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
  })
}

export interface ChainClientsQuery {
  q: string
  includeInactive: boolean
}

/** Clientes de la cadena, paginados por el servidor. */
export function useChainClients(publicId: string, query: ChainClientsQuery, skip: number, take: number) {
  return useQuery({
    queryKey: chainKeys.clients(publicId, query.q, query.includeInactive, skip, take),
    queryFn: () =>
      unwrap(api.GET('/api/v1/client-chains/{publicId}/clients', { params: { path: { publicId }, query: { q: query.q || undefined, includeInactive: query.includeInactive, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** TODOS los clientes de la cadena que cumplen el filtro (para ordenar y exportar la consulta completa: `all = true`, sin tope). */
export async function fetchAllChainClients(publicId: string, query: ChainClientsQuery): Promise<ChainClient[]> {
  const page = await unwrap(
    api.GET('/api/v1/client-chains/{publicId}/clients', { params: { path: { publicId }, query: { q: query.q || undefined, includeInactive: query.includeInactive, all: true } } }),
  )
  return page.items ?? []
}

/** Vista «Por cadena» del análisis de ventas (mes contra mes; con `chainPublicId`, solo esa cadena). */
export function useChainSales(month: string, compare: string, chainPublicId = '') {
  return useQuery({
    queryKey: chainKeys.sales(month, compare, chainPublicId),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/sales-analysis/by-chain', {
          params: { query: { month: month || undefined, compareMonth: compare || undefined, chainPublicId: chainPublicId || undefined } },
        }),
      ),
    placeholderData: keepPreviousData,
  })
}

export function useChainSeries(chainPublicId: string) {
  return useQuery({
    queryKey: chainKeys.series(chainPublicId),
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/by-chain/series', { params: { query: { chainPublicId } } })),
    enabled: !!chainPublicId,
  })
}

// ---------------------------------------------------------------- escrituras

/** Lo que cambia la pertenencia de clientes: la lista, la ficha, los clientes, Clientes, Órdenes y el análisis. */
function useRefreshAll() {
  const qc = useQueryClient()
  return () => {
    void qc.invalidateQueries({ queryKey: chainKeys.all })
    void qc.invalidateQueries({ queryKey: clientKeys.list })
    void qc.invalidateQueries({ queryKey: ['/api/v1/clients/{publicId}'] })
    void qc.invalidateQueries({ queryKey: ['sales'] })
  }
}

export function useCreateChain() {
  const refresh = useRefreshAll()
  return useMutation({ mutationFn: (body: ChainRequest) => unwrap(api.POST('/api/v1/client-chains', { body })), onSuccess: refresh })
}

export function useUpdateChain(publicId: string) {
  const refresh = useRefreshAll()
  return useMutation({ mutationFn: (body: ChainRequest) => unwrap(api.PATCH('/api/v1/client-chains/{publicId}', { params: { path: { publicId } }, body })), onSuccess: refresh })
}

export function useSetChainActive(publicId: string) {
  const refresh = useRefreshAll()
  return useMutation({
    mutationFn: (active: boolean) =>
      unwrap(active ? api.POST('/api/v1/client-chains/{publicId}/reactivate', { params: { path: { publicId } } }) : api.POST('/api/v1/client-chains/{publicId}/deactivate', { params: { path: { publicId } } })),
    onSuccess: refresh,
  })
}

export function useAssignChainClients(publicId: string) {
  const refresh = useRefreshAll()
  return useMutation({
    mutationFn: (body: ChainAssignRequest) => unwrap(api.POST('/api/v1/client-chains/{publicId}/clients', { params: { path: { publicId } }, body })),
    onSuccess: refresh,
  })
}

export function useRemoveChainClients(publicId: string) {
  const refresh = useRefreshAll()
  return useMutation({
    mutationFn: (clientPublicIds: string[]) => unwrap(api.POST('/api/v1/client-chains/{publicId}/clients/remove', { params: { path: { publicId } }, body: { clientPublicIds } })),
    onSuccess: refresh,
  })
}

/** Cambia (o quita, null) la cadena de un cliente desde su ficha. */
export function useSetClientChain(clientPublicId: string) {
  const refresh = useRefreshAll()
  return useMutation({
    mutationFn: (chainPublicId: string | null) =>
      unwrap(api.PUT('/api/v1/client-chains/by-client/{clientPublicId}', { params: { path: { clientPublicId } }, body: { chainPublicId } })),
    onSuccess: (m) => {
      refresh()
      return m
    },
  })
}

/** Simula el alta masiva (no escribe nada). */
export function usePreviewBulk(publicId: string) {
  return useMutation({
    mutationFn: (body: ChainBulkRequest) => unwrap(api.POST('/api/v1/client-chains/{publicId}/bulk-assign/preview', { params: { path: { publicId } }, body })),
  })
}

export function useApplyBulk(publicId: string) {
  const refresh = useRefreshAll()
  return useMutation({
    mutationFn: (body: ChainBulkRequest) => unwrap(api.POST('/api/v1/client-chains/{publicId}/bulk-assign/apply', { params: { path: { publicId } }, body })),
    onSuccess: refresh,
  })
}
