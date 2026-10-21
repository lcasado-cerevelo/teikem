// Importación de entidades con plantilla (motor genérico, 2026-10-21; productos y proveedores): campos y plantillas (`{base}/import-templates`) y la importación (`POST {base}/import`, multipart,
// `commit` false = vista previa / true = crea). Las rutas de cada entidad salen de `ENTITY_CONFIG`; los tipos son los generados (las dos entidades tienen las mismas formas).
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import type { ImportField, ImportTemplate, ImportTemplateCreateRequest, ImportTemplatePatchRequest } from '../consignees/consigneeRules'
import { ENTITY_CONFIG, type ImportEntity } from './entityImportRules'

export type EntityImportResult = components['schemas']['EntityImportResultDto']
export type EntityImportRow = components['schemas']['EntityImportRowDto']

type AnyApi = {
  GET: (path: string, init?: unknown) => Promise<{ data?: unknown; error?: unknown; response: Response }>
  POST: (path: string, init?: unknown) => Promise<{ data?: unknown; error?: unknown; response: Response }>
  PATCH: (path: string, init?: unknown) => Promise<{ data?: unknown; error?: unknown; response: Response }>
}
// las rutas de las dos entidades son idénticas salvo el prefijo: se llaman por texto
const raw = api as unknown as AnyApi
const call = <T>(p: Promise<{ data?: unknown; error?: unknown; response: Response }>) => unwrap(p as never) as Promise<T>

export const entityImportKeys = {
  fields: (e: ImportEntity) => [`${ENTITY_CONFIG[e].base}/import-templates/fields`] as const,
  templates: (e: ImportEntity) => [`${ENTITY_CONFIG[e].base}/import-templates`] as const,
  templatesOf: (e: ImportEntity, includeInactive: boolean) => [`${ENTITY_CONFIG[e].base}/import-templates`, { includeInactive }] as const,
}

export function useEntityImportFields(entity: ImportEntity, enabled = true) {
  return useQuery({
    queryKey: entityImportKeys.fields(entity),
    queryFn: () => call<ImportField[]>(raw.GET(`${ENTITY_CONFIG[entity].base}/import-templates/fields`)),
    enabled,
    staleTime: 10 * 60_000,
    meta: { handleAccessDenied: false },
  })
}

/** La lista de plantillas de una entidad (con o sin inactivas), para pantallas que no usan el hook. */
export const fetchEntityTemplates = (entity: ImportEntity, includeInactive: boolean) =>
  call<ImportTemplate[]>(raw.GET(`${ENTITY_CONFIG[entity].base}/import-templates`, { params: { query: { includeInactive } } }))

export function useEntityImportTemplates(entity: ImportEntity, includeInactive: boolean, enabled = true) {
  return useQuery({
    queryKey: entityImportKeys.templatesOf(entity, includeInactive),
    queryFn: () => call<ImportTemplate[]>(raw.GET(`${ENTITY_CONFIG[entity].base}/import-templates`, { params: { query: { includeInactive } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export type EntityTemplateAction = { action: 'create'; body: ImportTemplateCreateRequest } | { action: 'update'; publicId: string; body: ImportTemplatePatchRequest }

export function useSaveEntityImportTemplate(entity: ImportEntity) {
  const qc = useQueryClient()
  const base = ENTITY_CONFIG[entity].base
  return useMutation({
    mutationFn: (req: EntityTemplateAction) =>
      req.action === 'create'
        ? call<ImportTemplate>(raw.POST(`${base}/import-templates`, { body: req.body }))
        : call<ImportTemplate>(raw.PATCH(`${base}/import-templates/{publicId}`, { params: { path: { publicId: req.publicId } }, body: req.body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: entityImportKeys.templates(entity) }),
  })
}

export function useSetEntityTemplateActive(entity: ImportEntity) {
  const qc = useQueryClient()
  const base = ENTITY_CONFIG[entity].base
  return useMutation({
    mutationFn: ({ publicId, active }: { publicId: string; active: boolean }) =>
      call<ImportTemplate>(raw.POST(`${base}/import-templates/{publicId}/${active ? 'reactivate' : 'deactivate'}`, { params: { path: { publicId } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: entityImportKeys.templates(entity) }),
  })
}

/** Revisa (commit = false, no guarda nada) o importa (commit = true, crea las filas válidas) el archivo con la plantilla. */
export function useImportEntity(entity: ImportEntity) {
  const qc = useQueryClient()
  const base = ENTITY_CONFIG[entity].base
  return useMutation({
    mutationFn: (v: { templatePublicId: string; file: File; commit: boolean }) => {
      const form = new FormData()
      form.append('templatePublicId', v.templatePublicId)
      form.append('commit', v.commit ? 'true' : 'false')
      form.append('file', v.file)
      return call<EntityImportResult>(raw.POST(`${base}/import`, { body: {}, bodySerializer: () => form }))
    },
    onSuccess: (r) => {
      if (r.committed) void qc.invalidateQueries({ queryKey: [base] })
    },
  })
}
