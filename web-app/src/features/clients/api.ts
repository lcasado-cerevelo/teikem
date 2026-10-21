// Datos de Clientes (F-A1, manual 02). Lecturas con `useQuery` (clave `[ruta, params]`) y escrituras con `useMutation`
// que dejan en caché la ficha que devuelve el API e invalidan la lista. Permisos: lectura `clients.read`; alta
// `clients.create`; perfil, numeración, personas de contacto, estatus y baja `clients.update`; teléfonos y correos del
// cliente `contacts.manage` (más `clients.update`, permiso de la entidad dueña). Módulo CATALOG.
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { catalogKeys } from '../../kernel/catalogs'
import type { ClientNumberSettingsRequest, ClientProfileUpdateRequest, ClientCreateRequest } from './clientRules'

type Schemas = components['schemas']

export const clientKeys = {
  /** Prefijo de todas las listas (se invalida entero). */
  list: ['/api/v1/clients'] as const,
  listOf: (search: string, includeInactive: boolean, custom: object = {}) => ['/api/v1/clients', { search, includeInactive, ...custom }] as const,
  detail: (publicId: string) => ['/api/v1/clients/{publicId}', publicId] as const,
  preview: (pattern: string, seq: number) => ['/api/v1/clients/number-format/preview', { pattern, seq }] as const,
}

// ---- Lecturas ----
export function useClients(search: string, includeInactive: boolean, custom: object = {}) {
  return useQuery({
    queryKey: clientKeys.listOf(search, includeInactive, custom),
    queryFn: () => unwrap(api.GET('/api/v1/clients', { params: { query: { search: search || undefined, includeInactive, ...custom } as { search?: string; includeInactive?: boolean } } })),
    placeholderData: keepPreviousData,
  })
}

/** `quiet` = consulta secundaria (p. ej. la numeración del cliente en una orden): un 403 no saca de la pantalla. */
export function useClient(publicId: string | null, quiet = false) {
  return useQuery({
    queryKey: clientKeys.detail(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
    meta: quiet ? { handleAccessDenied: false } : undefined,
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

/**
 * ¿El cliente imprime solo sus labels de piezas («Imprimir los labels automáticamente», Lote 48) y su label de factura («Label de factura»)? Lee la ficha (en caché un minuto); si el usuario no
 * puede leer clientes o falla, es «no»: imprimir sin la bandera nunca debe romper el guardado de una orden.
 */
export async function clientAutoPrint(qc: QueryClient, clientPublicId: string | null | undefined): Promise<{ labels: boolean; invoice: boolean }> {
  if (!clientPublicId) return { labels: false, invoice: false }
  try {
    const detail = await qc.fetchQuery({
      queryKey: clientKeys.detail(clientPublicId),
      queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}', { params: { path: { publicId: clientPublicId } } })),
      staleTime: 60_000,
    })
    return { labels: detail.autoPrintLabels === true, invoice: detail.autoPrintInvoice === true }
  } catch {
    return { labels: false, invoice: false }
  }
}

/** Alta o cambio de una dirección del cliente (la física, la postal o un punto de recogida: una localización) y relectura de la ficha. */
export function useSaveClientAddress(clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (arg: { create: Schemas['LocationUpsertRequest'] } | { publicId: string; patch: Schemas['LocationPatchRequest'] }) =>
      'create' in arg
        ? unwrap(api.POST('/api/v1/locations', { body: arg.create }))
        : unwrap(api.PATCH('/api/v1/locations/{publicId}', { params: { path: { publicId: arg.publicId } }, body: arg.patch })),
    onSuccess: () => qc.invalidateQueries({ queryKey: clientKeys.detail(clientPublicId) }),
  })
}

/** Baja lógica de una dirección (un punto de recogida, o la postal propia para volver a «igual a la física»). Nunca DELETE. */
export function useDeactivateClientAddress(clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (publicId: string) => {
      await unwrap(api.POST('/api/v1/locations/{publicId}/deactivate', { params: { path: { publicId } } }))
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: clientKeys.detail(clientPublicId) }),
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
