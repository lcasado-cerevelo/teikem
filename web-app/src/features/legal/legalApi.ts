// Datos del documento legal único (Lote 99): el texto vigente (sin sesión), el estado de aceptación del usuario, aceptar y la lista de quién aceptó (admin.audit).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type LegalDocumentDto = Schemas['LegalDocumentDto']
export type LegalSectionDto = Schemas['LegalSectionDto']
export type LegalStatusDto = Schemas['LegalStatusDto']
export type LegalAcceptanceRowDto = Schemas['LegalAcceptanceRowDto']

export const legalKeys = {
  document: (lang: string) => ['legal', 'document', lang] as const,
  status: ['legal', 'status'] as const,
  acceptances: ['legal', 'acceptances'] as const,
}

export function useLegalDocument(lang: string) {
  return useQuery({
    queryKey: legalKeys.document(lang),
    queryFn: () => unwrap(api.GET('/api/v1/legal/current', { params: { query: { lang } } })),
    staleTime: 10 * 60_000,
    meta: { handleAccessDenied: false },
  })
}

export function useLegalStatus(enabled: boolean) {
  return useQuery({
    queryKey: legalKeys.status,
    enabled,
    retry: false,   // si el estado no se puede leer, la compuerta deja pasar (el servidor igual lo exige) en lugar de quedarse cargando
    queryFn: () => unwrap(api.GET('/api/v1/legal/status')),
    meta: { handleAccessDenied: false },
  })
}

export function useAcceptLegal() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { version: string; lang: string }) => unwrap(api.POST('/api/v1/legal/accept', { body: v })),
    onSuccess: (data) => qc.setQueryData(legalKeys.status, data),
  })
}

export interface AcceptanceQuery {
  status?: 'accepted' | 'pending'
  q?: string
}

export function useLegalAcceptances(query: AcceptanceQuery, skip: number, take: number) {
  return useQuery({
    queryKey: [...legalKeys.acceptances, query, skip, take],
    queryFn: () => unwrap(api.GET('/api/v1/legal/acceptances', { params: { query: { ...query, skip, take } } })),
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

export function acceptancePageReader(query: AcceptanceQuery) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/legal/acceptances', { params: { query: { ...query, skip, take } } }))
}

export const fetchAllAcceptances = (query: AcceptanceQuery) => fetchAllPages<LegalAcceptanceRowDto>(acceptancePageReader(query))
