// Lote 248 — datos de los documentos del cliente con vigencia, las exenciones de IVU de la ficha y la lista «Documentos por vencer» (cliente generado).
// Documentos: ver clients.read; subir, editar o quitar clients.update (o clients.create_basic con el cliente prospecto). Exenciones: tax.manage y módulo TAX.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { CATALOG_STALE_MS } from '../../kernel/catalogs'
import { taxKeys } from '../tax/taxApi'
import { clientDocKeys } from './clientSalesApi'
import { parseDocumentTypeMarks, CLIENT_DOCUMENT_TYPE_ENTITY, type DocumentTypeMarks } from './documentRules'

type Schemas = components['schemas']
export type ClientDocumentPatchRequest = Schemas['ClientDocumentPatchRequest']
export type ClientTaxExemptionCaptureDto = Schemas['ClientTaxExemptionCaptureDto']
export type ExpiringClientDocumentDto = Schemas['ExpiringClientDocumentDto']
export type ExpiringClientDocumentsDto = Schemas['ExpiringClientDocumentsDto']
export type TaxExemptionDto = Schemas['TaxExemptionDto']

export const documentKeys = {
  types: ['/api/v1/catalogs/{entity}', CLIENT_DOCUMENT_TYPE_ENTITY, 'marks'] as const,
  exemptions: (publicId: string) => ['/api/v1/clients/{publicId}/tax-exemptions', publicId] as const,
  expiring: (q: ExpiringQuery) => ['/api/v1/client-documents/expiring', q] as const,
}

export interface DocumentTypeOption {
  code: string
  label: string
  marks: DocumentTypeMarks
}

/** Tipos de documento activos de la compañía con sus marcas (el catálogo trae el ExtraJson ya combinado con el de la compañía). */
export function useClientDocumentTypes() {
  return useQuery({
    queryKey: documentKeys.types,
    queryFn: async (): Promise<DocumentTypeOption[]> => {
      const rows = await unwrap(api.GET('/api/v1/catalogs/{entity}', { params: { path: { entity: CLIENT_DOCUMENT_TYPE_ENTITY }, query: { includeDisabled: false } } }))
      return rows
        .filter((r) => r.code && r.isEnabled !== false)
        .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
        .map((r) => ({ code: r.code ?? '', label: r.label || r.code || '', marks: parseDocumentTypeMarks(r.extraJson) }))
    },
    staleTime: CATALOG_STALE_MS,
    meta: { handleAccessDenied: false },
  })
}

/** Editar número, vigencia y nota (el tipo no se edita). 409 si el documento cambió; 403 si respalda una exención y falta tax.manage. */
export function useUpdateClientDocument(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { documentPublicId: string; body: ClientDocumentPatchRequest }) =>
      unwrap(api.PATCH('/api/v1/clients/{publicId}/documents/{documentPublicId}', { params: { path: { publicId, documentPublicId: v.documentPublicId } }, body: v.body })),
    onSuccess: () => Promise.all([qc.invalidateQueries({ queryKey: clientDocKeys.list(publicId) }), qc.invalidateQueries({ queryKey: documentKeys.exemptions(publicId) })]),
  })
}

/** Exenciones de IVU del cliente (la más reciente primero), con el documento que las respalda o «falta el documento». */
export function useClientExemptions(publicId: string, enabled = true) {
  return useQuery({
    queryKey: documentKeys.exemptions(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}/tax-exemptions', { params: { path: { publicId } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export interface CaptureExemptionInput {
  /** Documento ya subido: no se manda archivo. */
  clientDocumentPublicId?: string
  /** Archivo nuevo: crea el documento y la exención en una sola transacción. */
  file?: File
  documentType?: string
  documentNumber?: string
  validFrom?: string
  validTo?: string
  scope: string
  componentCodes: string[]
  notes?: string
  exemptionType?: string
}

/** Captura única: certificado (archivo nuevo o documento ya subido) y exención en un paso. Lleva Idempotency-Key para que un doble clic no la repita. */
export function useCaptureExemption(clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: CaptureExemptionInput): Promise<ClientTaxExemptionCaptureDto> => {
      const form = new FormData()
      if (v.file) form.append('file', v.file)
      if (v.clientDocumentPublicId) form.append('clientDocumentPublicId', v.clientDocumentPublicId)
      const text = { documentType: v.documentType, documentNumber: v.documentNumber?.trim(), validFrom: v.validFrom, validTo: v.validTo, scope: v.scope, notes: v.notes?.trim(), exemptionType: v.exemptionType?.trim() }
      for (const [k, val] of Object.entries(text)) if (val) form.append(k, val)
      for (const c of v.componentCodes) form.append('componentCodes', c)
      return unwrap(
        api.POST('/api/v1/clients/{publicId}/tax-exemptions', {
          params: { path: { publicId: clientPublicId } },
          body: {},
          bodySerializer: () => form,
          headers: { 'Idempotency-Key': crypto.randomUUID() },
        }),
      )
    },
    onSuccess: () =>
      Promise.all([
        qc.invalidateQueries({ queryKey: clientDocKeys.list(clientPublicId) }),
        qc.invalidateQueries({ queryKey: documentKeys.exemptions(clientPublicId) }),
        qc.invalidateQueries({ queryKey: taxKeys.all }),
        qc.invalidateQueries({ queryKey: ['/api/v1/sales-orders/tax-notice'] }),
      ]),
  })
}

export interface ExpiringQuery {
  withinDays?: number
  includeExpired: boolean
  includeMissing: boolean
}

/** «Documentos por vencer»: sin renovados, por «hasta» ascendente, tope de 2000 (truncated). */
export function useExpiringDocuments(q: ExpiringQuery) {
  return useQuery({
    queryKey: documentKeys.expiring(q),
    queryFn: () => unwrap(api.GET('/api/v1/client-documents/expiring', { params: { query: { withinDays: q.withinDays, includeExpired: q.includeExpired, includeMissing: q.includeMissing } } })),
    meta: { handleAccessDenied: false },
  })
}
