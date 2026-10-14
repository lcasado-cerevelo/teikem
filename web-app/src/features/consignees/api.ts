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
  forClient: (clientId: string) => ['/api/v1/locations', { forOrder: clientId, includeShared: true, locationType: DELIVERY_TYPE }] as const,
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

/**
 * Consignatarios para elegir en una orden (Lote F-C1): activos DELIVERY del cliente MÁS los compartidos
 * (`includeShared=true`). Es una consulta secundaria de la pantalla de órdenes: un 403 (sin `locations.read`) no la saca de ahí.
 */
export function useClientConsignees(clientId: string | null) {
  return useQuery({
    queryKey: consigneeKeys.forClient(clientId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/locations', { params: { query: { locationType: DELIVERY_TYPE, includeInactive: false, clientId: clientId ?? undefined, includeShared: true } } })),
    enabled: !!clientId,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

/**
 * El consignatario elegido en una orden: sale de la lista del cliente (propios y compartidos) y, si no está ahí (recién creado, de otro
 * cliente o una lista que no se pudo leer), de su ficha. null = ninguno.
 */
export function useSelectedConsignee(clientPublicId: string | null, value: string | null | undefined): Consignee | null {
  const list = useClientConsignees(clientPublicId)
  const fromList = value ? list.data?.find((l) => l.publicId === value) : undefined
  const detail = useConsignee(fromList || !value ? null : value, true)
  return fromList ?? detail.data ?? null
}

/** `quiet` = consulta secundaria (p. ej. el consignatario elegido en una orden): un 403 no saca de la pantalla. */
export function useConsignee(publicId: string | null, quiet = false) {
  return useQuery({
    queryKey: consigneeKeys.detail(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/locations/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
    meta: quiet ? { handleAccessDenied: false } : undefined,
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
    // al cambiar de cliente (p. ej. el de la plantilla, Lote F-B2) la lista anterior se mantiene mientras llega la nueva: sin parpadeo
    placeholderData: keepPreviousData,
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
/**
 * Lote F-B2: revisa el archivo (CSV o .xlsx) SIN guardar nada: formato, hojas del libro, hoja leída, filas y, por columna, el encabezado
 * y muestras (multipart: file, delimiter, hasHeader, sheet opcional). Lo usan el selector de hoja y el editor de plantillas.
 */
export function useInspectImportFile() {
  return useMutation({
    mutationFn: ({ file, delimiter, hasHeader, sheet }: { file: File; delimiter: string; hasHeader: boolean; sheet?: string | null }) => {
      const form = new FormData()
      form.append('file', file)
      form.append('delimiter', delimiter)
      form.append('hasHeader', String(hasHeader))
      if (sheet) form.append('sheet', sheet)
      return unwrap(api.POST('/api/v1/locations/import/inspect-file', { body: {}, bodySerializer: () => form }))
    },
  })
}

/**
 * Valida el archivo (multipart: templatePublicId, clientPublicId opcional = cliente por defecto, file —CSV o .xlsx— y la hoja del libro)
 * y deja un lote VALIDATED. No crea nada.
 */
export function useValidateImportFile() {
  return useMutation({
    mutationFn: ({ templatePublicId, clientPublicId, file, sheet }: { templatePublicId: string; clientPublicId: string | null; file: File; sheet?: string | null }) => {
      const form = new FormData()
      form.append('templatePublicId', templatePublicId)
      if (clientPublicId) form.append('clientPublicId', clientPublicId)
      form.append('file', file)
      if (sheet) form.append('sheet', sheet)
      return unwrap(api.POST('/api/v1/locations/import/validate-file', { body: {}, bodySerializer: () => form }))
    },
  })
}

/**
 * Crea las filas válidas del lote (`rows` vacío = todas). Lleva `Idempotency-Key`: la misma clave en un reintento. `rememberClient`
 * (Lote F-B2) guarda en la plantilla el cliente del lote como su cliente por defecto.
 */
export function useConfirmImport() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ batchPublicId, rows, idempotencyKey, rememberClient }: { batchPublicId: string; rows?: number[]; idempotencyKey: string; rememberClient?: boolean }) =>
      unwrap(
        api.POST('/api/v1/locations/import/{batchPublicId}/confirm', {
          params: { path: { batchPublicId } },
          body: rememberClient ? { rows: rows ?? null, rememberClient: true } : { rows: rows ?? null },
          headers: { 'Idempotency-Key': idempotencyKey },
        }),
      ),
    onSuccess: (_dto, req) => {
      void qc.invalidateQueries({ queryKey: consigneeKeys.list })
      if (req.rememberClient) void qc.invalidateQueries({ queryKey: consigneeKeys.templates })
    },
  })
}

export function useDiscardImport() {
  return useMutation({
    mutationFn: (batchPublicId: string) => unwrap(api.POST('/api/v1/locations/import/{batchPublicId}/discard', { params: { path: { batchPublicId } } })),
  })
}
