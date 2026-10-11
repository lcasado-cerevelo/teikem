// Datos de los contratos del cliente (F-A2, manual 02 caps. 4-6). Lecturas con `useQuery` y escrituras con `useMutation`.
// Permisos: lectura `contracts.read`; alta `contracts.create`; datos, modelo de facturación, SLA, estatus, tarifas, tramos y
// servicios especiales `contracts.update` (más la capacidad EDIT_CONTRACT del estatus: `canEdit`). Módulo CATALOG.
// Cada escritura del contrato deja la ficha devuelta en caché y refresca la ficha del cliente (su `billingSummary` cambia).
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { catalogKeys } from '../../kernel/catalogs'
import { clientKeys } from './api'
import type { BillingModelRequest, CodFeeRequest, ContractCreateRequest, ContractDetail, ContractPatchRequest, DispatchFeeRequest, ServiceLevelUpsert } from './contractRules'

type Schemas = components['schemas']

export const contractKeys = {
  detail: (publicId: string) => ['/api/v1/contracts/{publicId}', publicId] as const,
  /** Prefijo de las tarifas de un contrato (con y sin historial). */
  rates: (publicId: string) => ['/api/v1/contracts/{publicId}/rate-components', publicId] as const,
  ratesOf: (publicId: string, includeHistory: boolean) => ['/api/v1/contracts/{publicId}/rate-components', publicId, { includeHistory }] as const,
  specials: (clientPublicId: string) => ['/api/v1/clients/{publicId}/special-services', clientPublicId] as const,
  specialsOf: (clientPublicId: string, includeHistory: boolean) => ['/api/v1/clients/{publicId}/special-services', clientPublicId, { includeHistory }] as const,
  specialTypes: ['/api/v1/special-service-types'] as const,
}

// ---- Lecturas ----
export function useContract(publicId: string | null) {
  return useQuery({
    queryKey: contractKeys.detail(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/contracts/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
    // una lectura secundaria de la ficha: un 403 no saca al usuario de la pantalla
    meta: { handleAccessDenied: false },
  })
}

export function useRateComponents(publicId: string | null, includeHistory: boolean) {
  return useQuery({
    queryKey: contractKeys.ratesOf(publicId ?? '', includeHistory),
    queryFn: () => unwrap(api.GET('/api/v1/contracts/{publicId}/rate-components', { params: { path: { publicId: publicId ?? '' }, query: { includeHistory } } })),
    enabled: !!publicId,
    meta: { handleAccessDenied: false },
  })
}

export function useSpecialServices(clientPublicId: string, includeHistory: boolean) {
  return useQuery({
    queryKey: contractKeys.specialsOf(clientPublicId, includeHistory),
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}/special-services', { params: { path: { publicId: clientPublicId }, query: { includeHistory } } })),
    meta: { handleAccessDenied: false },
  })
}

export function useSpecialServiceTypes(enabled = true) {
  return useQuery({
    queryKey: contractKeys.specialTypes,
    queryFn: () => unwrap(api.GET('/api/v1/special-service-types', { params: { query: { includeInactive: false } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

// ---- Escrituras del contrato ----
/** Deja la ficha devuelta en caché y refresca la ficha y la lista del cliente (billingSummary, contratos). */
function settleContract(qc: QueryClient, clientPublicId: string, detail: ContractDetail) {
  if (detail.publicId) qc.setQueryData(contractKeys.detail(detail.publicId), detail)
  void qc.invalidateQueries({ queryKey: clientKeys.detail(clientPublicId) })
  void qc.invalidateQueries({ queryKey: clientKeys.list })
}

export function useCreateContract(clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: ContractCreateRequest) => unwrap(api.POST('/api/v1/contracts', { body })),
    onSuccess: (d) => settleContract(qc, clientPublicId, d),
  })
}

export function useUpdateContract(publicId: string, clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: ContractPatchRequest) => unwrap(api.PATCH('/api/v1/contracts/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: (d) => settleContract(qc, clientPublicId, d),
  })
}

export function useBillingMutations(publicId: string, clientPublicId: string) {
  const qc = useQueryClient()
  const onSuccess = (d: ContractDetail) => settleContract(qc, clientPublicId, d)
  const params = { params: { path: { publicId } } }
  return {
    flags: useMutation({ mutationFn: (body: BillingModelRequest) => unwrap(api.PATCH('/api/v1/contracts/{publicId}/billing-model', { ...params, body })), onSuccess }),
    dispatch: useMutation({ mutationFn: (body: DispatchFeeRequest) => unwrap(api.PATCH('/api/v1/contracts/{publicId}/dispatch-fee', { ...params, body })), onSuccess }),
    cod: useMutation({ mutationFn: (body: CodFeeRequest) => unwrap(api.PATCH('/api/v1/contracts/{publicId}/cod-fee', { ...params, body })), onSuccess }),
  }
}

/** SLA: PUT reemplaza la lista completa (los tipos ausentes se dan de baja). */
export function useSetServiceLevels(publicId: string, clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: ServiceLevelUpsert[]) => unwrap(api.PUT('/api/v1/contracts/{publicId}/service-levels', { params: { path: { publicId } }, body })),
    onSuccess: (d) => settleContract(qc, clientPublicId, d),
  })
}

/** Cambio de estatus del contrato (StatusService valida y escribe el historial CONTRACT). */
export function useTransitionContract(publicId: string, clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['StatusChangeRequest']) => unwrap(api.POST('/api/v1/contracts/{publicId}/status', { params: { path: { publicId } }, body })),
    onSuccess: (d) => {
      settleContract(qc, clientPublicId, d)
      void qc.invalidateQueries({ queryKey: catalogKeys.history('CONTRACT', d.id ?? 0) })
    },
  })
}

// ---- Tarifas ----
export function useRateMutations(publicId: string) {
  const qc = useQueryClient()
  const onSuccess = () => qc.invalidateQueries({ queryKey: contractKeys.rates(publicId) })
  const path = { path: { publicId } }
  return {
    add: useMutation({ mutationFn: (body: Schemas['RateComponentCreateRequest']) => unwrap(api.POST('/api/v1/contracts/{publicId}/rate-components', { params: path, body })), onSuccess }),
    update: useMutation({
      mutationFn: (v: { id: number; body: Schemas['RateUpdateRequest'] }) => unwrap(api.PATCH('/api/v1/contracts/{publicId}/rate-components/{id}', { params: { path: { publicId, id: v.id } }, body: v.body })),
      onSuccess,
    }),
    close: useMutation({
      mutationFn: (v: { id: number; body?: Schemas['RateCloseRequest'] }) => unwrap(api.POST('/api/v1/contracts/{publicId}/rate-components/{id}/close', { params: { path: { publicId, id: v.id } }, body: v.body ?? {} })),
      onSuccess,
    }),
    addTier: useMutation({
      mutationFn: (v: { id: number; body: Schemas['TierUpsertRequest'] }) => unwrap(api.POST('/api/v1/contracts/{publicId}/rate-components/{id}/tiers', { params: { path: { publicId, id: v.id } }, body: v.body })),
      onSuccess,
    }),
    updateTier: useMutation({
      mutationFn: (v: { id: number; tierId: number; body: Schemas['TierPatchRequest'] }) =>
        unwrap(api.PATCH('/api/v1/contracts/{publicId}/rate-components/{id}/tiers/{tierId}', { params: { path: { publicId, id: v.id, tierId: v.tierId } }, body: v.body })),
      onSuccess,
    }),
    closeTier: useMutation({
      mutationFn: (v: { id: number; tierId: number }) =>
        unwrap(api.POST('/api/v1/contracts/{publicId}/rate-components/{id}/tiers/{tierId}/close', { params: { path: { publicId, id: v.id, tierId: v.tierId } }, body: {} })),
      onSuccess,
    }),
  }
}

// ---- Servicios especiales ----
export function useSpecialMutations(clientPublicId: string) {
  const qc = useQueryClient()
  const onSuccess = () => qc.invalidateQueries({ queryKey: contractKeys.specials(clientPublicId) })
  const path = { path: { publicId: clientPublicId } }
  return {
    add: useMutation({
      mutationFn: (body: Schemas['SpecialServiceCreateRequest']) => unwrap(api.POST('/api/v1/clients/{publicId}/special-services', { params: path, body })),
      // un tipo nuevo queda disponible para todos los clientes de la compañía
      onSuccess: () => Promise.all([onSuccess(), qc.invalidateQueries({ queryKey: contractKeys.specialTypes })]),
    }),
    update: useMutation({
      mutationFn: (v: { id: number; body: Schemas['SpecialServiceRateUpdateRequest'] }) =>
        unwrap(api.PATCH('/api/v1/clients/{publicId}/special-services/{id}', { params: { path: { publicId: clientPublicId, id: v.id } }, body: v.body })),
      onSuccess,
    }),
    close: useMutation({
      mutationFn: (v: { id: number }) => unwrap(api.POST('/api/v1/clients/{publicId}/special-services/{id}/close', { params: { path: { publicId: clientPublicId, id: v.id } }, body: {} })),
      onSuccess,
    }),
  }
}
