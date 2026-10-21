// Importación de PRODUCTOS con plantilla (2026-10-21): campos y plantillas (`/products/import-templates`, `inventory.view` / `inventory.manage`) y la importación (`POST /products/import`,
// multipart, `commit` false = vista previa / true = crea). Módulo WMS_LOT_SERIAL. Lecturas con `useQuery`; escrituras con `useMutation`.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { ImportTemplate, ImportTemplateCreateRequest, ImportTemplatePatchRequest } from '../consignees/consigneeRules'
import type { components } from '../../kernel/api/schema'

export type ProductImportResult = components['schemas']['ProductImportResultDto']
export type ProductImportRow = components['schemas']['ProductImportRowDto']

export const productImportKeys = {
  fields: ['/api/v1/products/import-templates/fields'] as const,
  templates: ['/api/v1/products/import-templates'] as const,
  templatesOf: (includeInactive: boolean) => ['/api/v1/products/import-templates', { includeInactive }] as const,
}

export function useProductImportFields(enabled = true) {
  return useQuery({
    queryKey: productImportKeys.fields,
    queryFn: () => unwrap(api.GET('/api/v1/products/import-templates/fields')),
    enabled,
    staleTime: 10 * 60_000,
    meta: { handleAccessDenied: false },
  })
}

export function useProductImportTemplates(includeInactive: boolean, enabled = true) {
  return useQuery({
    queryKey: productImportKeys.templatesOf(includeInactive),
    queryFn: (): Promise<ImportTemplate[]> => unwrap(api.GET('/api/v1/products/import-templates', { params: { query: { includeInactive } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export type ProductTemplateAction = { action: 'create'; body: ImportTemplateCreateRequest } | { action: 'update'; publicId: string; body: ImportTemplatePatchRequest }

export function useSaveProductImportTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: ProductTemplateAction) =>
      req.action === 'create'
        ? unwrap(api.POST('/api/v1/products/import-templates', { body: req.body }))
        : unwrap(api.PATCH('/api/v1/products/import-templates/{publicId}', { params: { path: { publicId: req.publicId } }, body: req.body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: productImportKeys.templates }),
  })
}

export function useSetProductTemplateActive() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, active }: { publicId: string; active: boolean }) =>
      active
        ? unwrap(api.POST('/api/v1/products/import-templates/{publicId}/reactivate', { params: { path: { publicId } } }))
        : unwrap(api.POST('/api/v1/products/import-templates/{publicId}/deactivate', { params: { path: { publicId } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: productImportKeys.templates }),
  })
}

/** Revisa (commit = false, no guarda nada) o importa (commit = true, crea las filas válidas) el archivo con la plantilla. */
export function useImportProducts() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (v: { templatePublicId: string; file: File; commit: boolean }): Promise<ProductImportResult> => {
      const form = new FormData()
      form.append('templatePublicId', v.templatePublicId)
      form.append('commit', v.commit ? 'true' : 'false')
      form.append('file', v.file)
      return unwrap(api.POST('/api/v1/products/import', { body: {}, bodySerializer: () => form }))
    },
    onSuccess: (r) => {
      if (r.committed) void qc.invalidateQueries({ queryKey: ['/api/v1/products'] })
    },
  })
}
