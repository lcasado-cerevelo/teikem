// Datos de «Reclamaciones» (Lote 111, módulo LTL_GROUND): faltantes por verificar, piezas perdidas y reclamaciones REC-#####.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type MissingOrderDto = Schemas['MissingOrderDto']
export type MissingPieceDto = Schemas['MissingPieceDto']
export type ClaimListItemDto = Schemas['ClaimListItemDto']
export type ClaimDto = Schemas['ClaimDto']
export type ClaimCreateRequest = Schemas['ClaimCreateRequest']
export type ClaimResolveRequest = Schemas['ClaimResolveRequest']

export const claimKeys = {
  all: ['claims'] as const,
  toVerify: (q: string, skip: number, take: number) => ['claims', 'to-verify', q, skip, take] as const,
  list: (status: string, q: string, skip: number, take: number) => ['claims', 'list', status, q, skip, take] as const,
  one: (id: string | null) => ['claims', 'one', id] as const,
}

export function useToVerify(q: string, skip: number, take: number) {
  return useQuery({
    queryKey: claimKeys.toVerify(q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/claims/to-verify', { params: { query: { search: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function toVerifyPageReader(q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/claims/to-verify', { params: { query: { search: q || undefined, skip, take } } }))
}

export function useClaims(status: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: claimKeys.list(status, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/claims', { params: { query: { status: status || undefined, search: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

export function claimsPageReader(status: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/claims', { params: { query: { status: status || undefined, search: q || undefined, skip, take } } }))
}

export function useClaim(publicId: string | null) {
  return useQuery({
    queryKey: claimKeys.one(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/claims/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useClaimMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: claimKeys.all })
  return {
    markLost: useMutation({
      mutationFn: (v: { orderPublicId: string; pieceNumbers?: string[] }) => unwrap(api.POST('/api/v1/claims/lost', { body: v })),
      onSuccess: refresh,
    }),
    create: useMutation({
      mutationFn: (body: ClaimCreateRequest) => unwrap(api.POST('/api/v1/claims', { body })),
      onSuccess: refresh,
    }),
    resolve: useMutation({
      mutationFn: (v: { id: string; body: ClaimResolveRequest }) => unwrap(api.POST('/api/v1/claims/{publicId}/resolve', { params: { path: { publicId: v.id } }, body: v.body })),
      onSuccess: refresh,
    }),
  }
}
