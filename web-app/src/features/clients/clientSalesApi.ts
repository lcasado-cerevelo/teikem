// Lote 222 (pedido de vendedores): aprobar o rechazar un cliente prospecto y sus documentos. Permisos: aprobar/rechazar `sales.approve_client`;
// documentos: ver `clients.read`, subir o quitar `clients.update` (o `clients.create_basic` mientras el cliente es prospecto).
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { catalogKeys } from '../../kernel/catalogs'
import { clientKeys } from './api'

type Schemas = components['schemas']
export type ClientDocumentDto = Schemas['ClientDocumentDto']
export type ClientApproveRequest = Schemas['ClientApproveRequest']
export type ClientRejectResultDto = Schemas['ClientRejectResultDto']

export const clientDocKeys = { list: (publicId: string) => ['/api/v1/clients/{publicId}/documents', publicId] as const }

/** Cuando cambia el estatus se guarda la ficha que devuelve el servidor y se invalidan la lista, los avisos y las órdenes (que esperaban la aprobación). */
function useSettle() {
  const qc = useQueryClient()
  return (detail: Schemas['ClientDetailDto'] | undefined) => {
    if (detail?.publicId) qc.setQueryData(clientKeys.detail(detail.publicId), detail)
    void qc.invalidateQueries({ queryKey: clientKeys.list })
    void qc.invalidateQueries({ queryKey: ['sales'] })
    void qc.invalidateQueries({ queryKey: ['/api/v1/analytics/attention'] })
    if (detail?.id != null) void qc.invalidateQueries({ queryKey: catalogKeys.history('CLIENT', detail.id) })
  }
}

export function useApproveClient(publicId: string) {
  const settle = useSettle()
  return useMutation({
    mutationFn: (body: ClientApproveRequest) => unwrap(api.POST('/api/v1/clients/{publicId}/approve', { params: { path: { publicId } }, body })),
    onSuccess: settle,
  })
}

/** Rechazar exige comentario; el servidor cancela las órdenes que esperaban (`cancelledOrders`: sus números). */
export function useRejectClient(publicId: string) {
  const settle = useSettle()
  return useMutation({
    mutationFn: (comment: string) => unwrap(api.POST('/api/v1/clients/{publicId}/reject', { params: { path: { publicId } }, body: { comment } })),
    onSuccess: (r) => settle(r.client),
  })
}

export function useClientDocuments(publicId: string, enabled = true) {
  return useQuery({
    queryKey: clientDocKeys.list(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}/documents', { params: { path: { publicId } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export function useUploadClientDocument(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ file, documentType, notes }: { file: File; documentType: string; notes: string }): Promise<ClientDocumentDto> => {
      const form = new FormData()
      form.append('file', file)
      form.append('documentType', documentType)
      if (notes.trim()) form.append('notes', notes.trim())
      return unwrap(api.POST('/api/v1/clients/{publicId}/documents', { params: { path: { publicId } }, body: {}, bodySerializer: () => form }))
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: clientDocKeys.list(publicId) }),
  })
}

export function useDeleteClientDocument(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (documentPublicId: string): Promise<void> => {
      await unwrap(api.DELETE('/api/v1/clients/{publicId}/documents/{documentPublicId}', { params: { path: { publicId, documentPublicId } } }))
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: clientDocKeys.list(publicId) }),
  })
}

/** Abre el archivo de un documento en otra pestaña (se pide con la sesión: nunca un enlace directo al disco). */
export async function openClientFile(filePublicId: string): Promise<void> {
  const res = await api.GET('/api/v1/files/{publicId}', { params: { path: { publicId: filePublicId } }, parseAs: 'blob' })
  if (res.error || !res.data) throw res.error ?? new Error('file')
  const url = URL.createObjectURL(res.data as Blob)
  window.open(url, '_blank', 'noopener')
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}
