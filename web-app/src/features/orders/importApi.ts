// Datos de la importación CSV de órdenes (Lote F-C3, manual 03 §7). Plantillas (`/import-templates`, tipo ORDER) y catálogo de campos
// (`/import-templates/fields`): `orders.edit`. Validar, confirmar y descartar un lote (`/orders/import/*`): `orders.create`. Módulo
// LTL_GROUND. Lecturas con `useQuery`; escrituras con `useMutation`.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { ImportTemplateCreateRequest, ImportTemplatePatchRequest } from '../consignees/consigneeRules'
import { orderKeys } from './api'
import { ORDER_KIND, templatesForClient, type OrderImportConfirmRequest } from './orderImportRules'

export const orderImportKeys = {
  fields: ['/api/v1/import-templates/fields'] as const,
  templates: ['/api/v1/import-templates'] as const,
  templatesOf: (clientId: string, includeInactive: boolean) => ['/api/v1/import-templates', { kind: ORDER_KIND, clientId, includeInactive }] as const,
}

/** Campos que una plantilla de órdenes puede mapear (`field`, `required`, `defaultAllowed`). */
export function useOrderImportFields(enabled = true) {
  return useQuery({
    queryKey: orderImportKeys.fields,
    queryFn: () => unwrap(api.GET('/api/v1/import-templates/fields')),
    enabled,
    staleTime: 10 * 60_000,
    meta: { handleAccessDenied: false },
  })
}

/** Plantillas de órdenes: con cliente, las generales y las suyas; sin cliente, solo las generales. */
export function useOrderImportTemplates(clientId: string, includeInactive: boolean, enabled = true) {
  return useQuery({
    queryKey: orderImportKeys.templatesOf(clientId, includeInactive),
    queryFn: async () => {
      const all = await unwrap(api.GET('/api/v1/import-templates', { params: { query: { kind: ORDER_KIND, clientId: clientId || undefined, includeInactive } } }))
      return templatesForClient(all, clientId)
    },
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export type OrderTemplateAction = { action: 'create'; body: ImportTemplateCreateRequest } | { action: 'update'; publicId: string; body: ImportTemplatePatchRequest }

export function useSaveOrderImportTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: OrderTemplateAction) =>
      req.action === 'create'
        ? unwrap(api.POST('/api/v1/import-templates', { body: { ...req.body, kind: ORDER_KIND } }))
        : unwrap(api.PATCH('/api/v1/import-templates/{publicId}', { params: { path: { publicId: req.publicId } }, body: req.body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: orderImportKeys.templates }),
  })
}

/** Desactivar o reactivar una plantilla (el servidor no tiene DELETE). */
export function useSetOrderTemplateActive() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, active }: { publicId: string; active: boolean }) =>
      active
        ? unwrap(api.POST('/api/v1/import-templates/{publicId}/reactivate', { params: { path: { publicId } } }))
        : unwrap(api.POST('/api/v1/import-templates/{publicId}/deactivate', { params: { path: { publicId } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: orderImportKeys.templates }),
  })
}

/** Valida el archivo (multipart: templatePublicId, clientPublicId, file; hasta 5.000 filas / 2 MB) y deja un lote VALIDATED. No crea órdenes. */
export function useValidateOrderImport() {
  return useMutation({
    mutationFn: ({ templatePublicId, clientPublicId, file }: { templatePublicId: string; clientPublicId: string; file: File }) => {
      const form = new FormData()
      form.append('templatePublicId', templatePublicId)
      form.append('clientPublicId', clientPublicId)
      form.append('file', file)
      return unwrap(api.POST('/api/v1/orders/import/validate-file', { body: {}, bodySerializer: () => form }))
    },
  })
}

/** Crea las órdenes de las filas válidas (una transacción por fila). Lleva `Idempotency-Key`: la misma clave en un reintento no duplica. */
export function useConfirmOrderImport() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ batchPublicId, body, idempotencyKey }: { batchPublicId: string; body: OrderImportConfirmRequest; idempotencyKey: string }) =>
      unwrap(
        api.POST('/api/v1/orders/import/{batchPublicId}/confirm', {
          params: { path: { batchPublicId } },
          body,
          headers: { 'Idempotency-Key': idempotencyKey },
        }),
      ),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: orderKeys.list })
      void qc.invalidateQueries({ queryKey: orderKeys.lookup })
    },
  })
}

export function useDiscardOrderImport() {
  return useMutation({
    mutationFn: (batchPublicId: string) => unwrap(api.POST('/api/v1/orders/import/{batchPublicId}/discard', { params: { path: { batchPublicId } } })),
  })
}
