// Datos de Clientes (F-A1, manual 02). Lecturas con `useQuery` (clave `[ruta, params]`) y escrituras con `useMutation`
// que dejan en caché la ficha que devuelve el API e invalidan la lista. Permisos: lectura `clients.read`; alta
// `clients.create`; perfil, numeración, personas de contacto, estatus y baja `clients.update`; teléfonos y correos del
// cliente `contacts.manage` (más `clients.update`, permiso de la entidad dueña). Módulo CATALOG.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { catalogKeys } from '../../kernel/catalogs'
import type { ClientNumberSettingsRequest, ClientProfileUpdateRequest, ClientCreateRequest } from './clientRules'

type Schemas = components['schemas']

export const clientKeys = {
  /** Prefijo de todas las listas (se invalida entero). */
  list: ['/api/v1/clients'] as const,
  listOf: (search: string, includeInactive: boolean) => ['/api/v1/clients', { search, includeInactive }] as const,
  detail: (publicId: string) => ['/api/v1/clients/{publicId}', publicId] as const,
  preview: (pattern: string, seq: number) => ['/api/v1/clients/number-format/preview', { pattern, seq }] as const,
}

// ---- Lecturas ----
export function useClients(search: string, includeInactive: boolean) {
  return useQuery({
    queryKey: clientKeys.listOf(search, includeInactive),
    queryFn: () => unwrap(api.GET('/api/v1/clients', { params: { query: { search: search || undefined, includeInactive } } })),
    placeholderData: keepPreviousData,
  })
}

export function useClient(publicId: string | null) {
  return useQuery({
    queryKey: clientKeys.detail(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
  })
}

/** Ejemplo en vivo de un patrón (GET puro, sin BD). Un patrón inválido responde 400 y el mensaje llega como error. */
export function useNumberPreview(pattern: string, enabled: boolean, seq = 1) {
  return useQuery({
    queryKey: clientKeys.preview(pattern, seq),
    queryFn: () => unwrap(api.GET('/api/v1/clients/number-format/preview', { params: { query: { pattern, seq } } })),
    enabled: enabled && pattern !== '',
    retry: false,
    staleTime: 5 * 60_000,
    meta: { handleAccessDenied: false },
  })
}

// ---- Escrituras ----
/** Deja la ficha devuelta en caché y refresca la lista (nombre, estatus y resumen de facturación salen de ahí). */
function useSettleDetail() {
  const qc = useQueryClient()
  return (detail: Schemas['ClientDetailDto']) => {
    if (detail.publicId) qc.setQueryData(clientKeys.detail(detail.publicId), detail)
    void qc.invalidateQueries({ queryKey: clientKeys.list })
  }
}

export function useCreateClient() {
  const settle = useSettleDetail()
  return useMutation({
    mutationFn: (body: ClientCreateRequest) => unwrap(api.POST('/api/v1/clients', { body })),
    onSuccess: settle,
  })
}

export function useUpdateClientProfile(publicId: string) {
  const settle = useSettleDetail()
  return useMutation({
    mutationFn: (body: ClientProfileUpdateRequest) => unwrap(api.PATCH('/api/v1/clients/{publicId}/profile', { params: { path: { publicId } }, body })),
    onSuccess: settle,
  })
}

export function useUpdateNumberSettings(publicId: string) {
  const settle = useSettleDetail()
  return useMutation({
    mutationFn: (body: ClientNumberSettingsRequest) => unwrap(api.PATCH('/api/v1/clients/{publicId}/number-settings', { params: { path: { publicId } }, body })),
    onSuccess: settle,
  })
}

/** Cambio de estatus por el endpoint del módulo (StatusService valida y escribe el historial). */
export function useTransitionClient(publicId: string) {
  const qc = useQueryClient()
  const settle = useSettleDetail()
  return useMutation({
    mutationFn: (body: Schemas['StatusChangeRequest']) => unwrap(api.POST('/api/v1/clients/{publicId}/status', { params: { path: { publicId } }, body })),
    onSuccess: (detail) => {
      settle(detail)
      // el historial de estatus de la ficha
      void qc.invalidateQueries({ queryKey: catalogKeys.history('CLIENT', detail.id ?? 0) })
    },
  })
}

/** Baja lógica o reactivación (nunca DELETE). */
export function useSetClientActive(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (active: boolean) =>
      active
        ? unwrap(api.POST('/api/v1/clients/{publicId}/reactivate', { params: { path: { publicId } } }))
        : unwrap(api.POST('/api/v1/clients/{publicId}/deactivate', { params: { path: { publicId } } })),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: clientKeys.detail(publicId) })
      void qc.invalidateQueries({ queryKey: clientKeys.list })
    },
  })
}

/** Personas de contacto: alta y edición (PATCH; «Quitar» = isActive:false). Refresca la ficha (trae `contacts`). */
export type PersonAction =
  | { action: 'create'; body: Schemas['ClientContactCreateRequest'] }
  | { action: 'update'; id: number; body: Schemas['ClientContactUpdateRequest'] }

export function useSaveClientContact(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: PersonAction) =>
      req.action === 'create'
        ? unwrap(api.POST('/api/v1/clients/{publicId}/contacts', { params: { path: { publicId } }, body: req.body }))
        : unwrap(api.PATCH('/api/v1/clients/{publicId}/contacts/{id}', { params: { path: { publicId, id: req.id } }, body: req.body })),
    // el principal anterior cambia en el servidor: se vuelve a leer la ficha completa
    onSuccess: () => qc.invalidateQueries({ queryKey: clientKeys.detail(publicId) }),
  })
}

/** Medios de contacto (ContactPoint) del dueño: el propio cliente (`CLIENT`) o una persona (`CLIENT_CONTACT`). */
export type PointAction =
  | { action: 'create'; ownerEntity: 'CLIENT' | 'CLIENT_CONTACT'; ownerId: number; body: Schemas['ContactPointUpsertRequest'] }
  | { action: 'update'; id: number; body: Schemas['ContactPointUpsertRequest'] }
  | { action: 'remove'; id: number }

export function useSaveContactPoint(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (req: PointAction) => {
      if (req.action === 'create')
        return unwrap(api.POST('/api/v1/contacts/{ownerEntity}/{ownerId}', { params: { path: { ownerEntity: req.ownerEntity, ownerId: req.ownerId } }, body: req.body }))
      if (req.action === 'update') return unwrap(api.PUT('/api/v1/contacts/{id}', { params: { path: { id: req.id } }, body: req.body }))
      return unwrap(api.DELETE('/api/v1/contacts/{id}', { params: { path: { id: req.id } } }))
    },
    // un principal nuevo desmarca a otro del mismo tipo: se relee la ficha
    onSuccess: () => qc.invalidateQueries({ queryKey: clientKeys.detail(publicId) }),
  })
}
