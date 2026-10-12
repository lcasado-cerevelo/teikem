// Datos de Consignatarios (Lote F-B, manual 02 §2). Localizaciones de tipo DELIVERY: lectura `locations.read`, alta
// `locations.create`, edición y baja `locations.update`; teléfono = ContactPoint de la localización (`contacts.manage` más
// `locations.update`). Importación por CSV: ver plantillas y campos `locations.read`; validar, confirmar, descartar y editar
// plantillas `locations.create`. Módulo CATALOG. Lecturas con `useQuery` (clave `[ruta, params]`); escrituras con `useMutation`.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import {
  DELIVERY_TYPE,
  type Consignee,
  type ConsigneeCreateRequest,
  type ConsigneePatchRequest,
  type ImportTemplateCreateRequest,
  type ImportTemplatePatchRequest,
} from './consigneeRules'

type Schemas = components['schemas']

export const consigneeKeys = {
  /** Prefijo de todas las listas (se invalida entero). */
  list: ['/api/v1/locations'] as const,
  listOf: (clientId: string | null, includeInactive: boolean) => ['/api/v1/locations', { clientId, includeInactive, locationType: DELIVERY_TYPE }] as const,
  detail: (publicId: string) => ['/api/v1/locations/{publicId}', publicId] as const,
  phones: (locationId: number) => ['/api/v1/contacts/{ownerEntity}/{ownerId}', 'LOCATION', locationId] as const,
  clients: (includeInactive: boolean) => ['/api/v1/clients', 'consignees', { includeInactive }] as const,
  fields: ['/api/v1/locations/import/fields'] as const,
  templates: ['/api/v1/locations/import-templates'] as const,
  templatesOf: (clientId: string | null, includeInactive: boolean) => ['/api/v1/locations/import-templates', { clientId, includeInactive }] as const,
}

// ---- Lecturas ----
/** Consignatarios (DELIVERY). Con cliente: solo los suyos (sin compartidos, como el filtro de la maqueta). Sin él: todos. */
export function useConsignees(clientId: string | null, includeInactive: boolean) {
  return useQuery({
    queryKey: consigneeKeys.listOf(clientId, includeInactive),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/locations', {
          params: { query: { locationType: DELIVERY_TYPE, includeInactive, clientId: clientId ?? undefined, includeShared: clientId ? false : undefined } },
        }),
      ),
    placeholderData: keepPreviousData,
  })
}

export function useConsignee(publicId: string | null) {
  return useQuery({
    queryKey: consigneeKeys.detail(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/locations/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
  })
}

/** Medios de contacto activos de la localización (el teléfono sale de aquí). Una consulta secundaria: un 403 no saca de la pantalla. */
export function useConsigneeContacts(locationId: number | null) {
  return useQuery({
    queryKey: consigneeKeys.phones(locationId ?? 0),
    queryFn: () => unwrap(api.GET('/api/v1/contacts/{ownerEntity}/{ownerId}', { params: { path: { ownerEntity: 'LOCATION', ownerId: locationId ?? 0 } } })),
    enabled: locationId != null,
    meta: { handleAccessDenied: false },
  })
}

/** Clientes para el filtro y el dueño (permiso clients.read; si falta, la lista queda vacía y solo hay «compartidos»). */
export function useClientOptions(includeInactive: boolean) {
  return useQuery({
    queryKey: consigneeKeys.clients(includeInactive),
    queryFn: () => unwrap(api.GET('/api/v1/clients', { params: { query: { includeInactive } } })),
    staleTime: 60_000,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

// ---- Escrituras ----
/** Deja el consignatario devuelto en caché y refresca las listas. */
function useSettle() {
  const qc = useQueryClient()
  return (dto: Consignee) => {
    if (dto.publicId) qc.setQueryData(consigneeKeys.detail(dto.publicId), dto)
    void qc.invalidateQueries({ queryKey: consigneeKeys.list })
  }
}

export function useCreateConsignee() {
  const settle = useSettle()
  return useMutation({
    mutationFn: (body: ConsigneeCreateRequest) => unwrap(api.POST('/api/v1/locations', { body })),
    onSuccess: settle,
  })
}

export function useUpdateConsignee(publicId: string) {
  const settle = useSettle()
  return useMutation({
    mutationFn: (body: ConsigneePatchRequest) => unwrap(api.PATCH('/api/v1/locations/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: settle,
  })
}

/** Baja lógica o reactivación (nunca DELETE; el servidor responde 204). */
export function useSetConsigneeActive(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (active: boolean) => {
      await unwrap(
        active
          ? api.POST('/api/v1/locations/{publicId}/reactivate', { params: { path: { publicId } } })
          : api.POST('/api/v1/locations/{publicId}/deactivate', { params: { path: { publicId } } }),
      )
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: consigneeKeys.detail(publicId) })
      void qc.invalidateQueries({ queryKey: consigneeKeys.list })
    },
  })
}

/** Teléfono de la localización: alta, cambio (PUT) o quitar (DELETE = baja del medio de contacto). */
export type PhoneAction =
  | { action: 'create'; locationId: number; body: Schemas['ContactPointUpsertRequest'] }
  | { action: 'update'; id: number; locationId: number; body: Schemas['ContactPointUpsertRequest'] }
  | { action: 'remove'; id: number; locationId: number }

export function useSaveConsigneePhone() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (req: PhoneAction) => {
      if (req.action === 'create')
        return unwrap(api.POST('/api/v1/contacts/{ownerEntity}/{ownerId}', { params: { path: { ownerEntity: 'LOCATION', ownerId: req.locationId } }, body: req.body }))
      if (req.action === 'update') return unwrap(api.PUT('/api/v1/contacts/{id}', { params: { path: { id: req.id } }, body: req.body }))
      await unwrap(api.DELETE('/api/v1/contacts/{id}', { params: { path: { id: req.id } } }))
      return null
    },
    onSuccess: (_data, req) => qc.invalidateQueries({ queryKey: consigneeKeys.phones(req.locationId) }),
  })
}

// ---- Importación: campos y plantillas ----
export function useImportFields(enabled = true) {
  return useQuery({
    queryKey: consigneeKeys.fields,
    queryFn: () => unwrap(api.GET('/api/v1/locations/import/fields')),
    enabled,
    staleTime: 10 * 60_000,
  })
}

/** Plantillas: con cliente, las generales y las suyas; sin cliente solo sirven las generales (las de un cliente se filtran). */
export function useImportTemplates(clientId: string | null, includeInactive: boolean, enabled = true) {
  return useQuery({
    queryKey: consigneeKeys.templatesOf(clientId, includeInactive),
    queryFn: async () => {
      const all = await unwrap(api.GET('/api/v1/locations/import-templates', { params: { query: { clientId: clientId ?? undefined, includeInactive } } }))
      return clientId ? all : all.filter((x) => !x.clientPublicId)
    },
    enabled,
  })
}

export type TemplateAction =
  | { action: 'create'; body: ImportTemplateCreateRequest }
  | { action: 'update'; publicId: string; body: ImportTemplatePatchRequest }

export function useSaveImportTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: TemplateAction) =>
      req.action === 'create'
        ? unwrap(api.POST('/api/v1/locations/import-templates', { body: req.body }))
        : unwrap(api.PATCH('/api/v1/locations/import-templates/{publicId}', { params: { path: { publicId: req.publicId } }, body: req.body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: consigneeKeys.templates }),
  })
}

/** Desactivar o reactivar una plantilla (el servidor no tiene DELETE). */
export function useSetTemplateActive() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, active }: { publicId: string; active: boolean }) =>
      active
        ? unwrap(api.POST('/api/v1/locations/import-templates/{publicId}/reactivate', { params: { path: { publicId } } }))
        : unwrap(api.POST('/api/v1/locations/import-templates/{publicId}/deactivate', { params: { path: { publicId } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: consigneeKeys.templates }),
  })
}

// ---- Importación: lote ----
/** Valida el archivo (multipart: templatePublicId, clientPublicId opcional, file) y deja un lote VALIDATED. No crea nada. */
export function useValidateImportFile() {
  return useMutation({
    mutationFn: ({ templatePublicId, clientPublicId, file }: { templatePublicId: string; clientPublicId: string | null; file: File }) => {
      const form = new FormData()
      form.append('templatePublicId', templatePublicId)
      if (clientPublicId) form.append('clientPublicId', clientPublicId)
      form.append('file', file)
      return unwrap(api.POST('/api/v1/locations/import/validate-file', { body: {}, bodySerializer: () => form }))
    },
  })
}

/** Crea las filas válidas del lote (`rows` vacío = todas). Lleva `Idempotency-Key`: la misma clave en un reintento. */
export function useConfirmImport() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ batchPublicId, rows, idempotencyKey }: { batchPublicId: string; rows?: number[]; idempotencyKey: string }) =>
      unwrap(
        api.POST('/api/v1/locations/import/{batchPublicId}/confirm', {
          params: { path: { batchPublicId } },
          body: { rows: rows ?? null },
          headers: { 'Idempotency-Key': idempotencyKey },
        }),
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: consigneeKeys.list }),
  })
}

export function useDiscardImport() {
  return useMutation({
    mutationFn: (batchPublicId: string) => unwrap(api.POST('/api/v1/locations/import/{batchPublicId}/discard', { params: { path: { batchPublicId } } })),
  })
}
