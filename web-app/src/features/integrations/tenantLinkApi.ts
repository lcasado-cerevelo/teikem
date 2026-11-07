// Datos de «Compañías enlazadas» (Lote 227, P7; módulo INTERCOMPANY): enlaces entre compañías (proponer, aceptar, rechazar, terminar) y las entregas que una compañía cliente
// manda a la que le da el transporte (reintentar, revisar ahora, comprobante). Permisos intercompany.view / intercompany.manage. Proponer y aceptar piden AAL2 (el cliente lo resuelve solo).
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type TenantLinkDto = Schemas['TenantLinkDto']
export type TenantLinkCompanyDto = Schemas['TenantLinkCompanyDto']
export type TenantLinkDeliveryDto = Schemas['TenantLinkDeliveryDto']
export type TenantLinkDeliveryPageDto = Schemas['TenantLinkDeliveryPageDto']
export type TenantLinkProposeRequest = Schemas['TenantLinkProposeRequest']
export type DeliveryProofDto = Schemas['DeliveryProofDto']

/** Estados de un enlace y de una entrega (los del API). */
export const LINK_STATUSES = ['PROPOSED', 'ACTIVE', 'REJECTED', 'ENDED'] as const
export const DELIVERY_STATES = ['PENDING', 'CREATED', 'FAILED'] as const

export const tenantLinkKeys = {
  all: ['integrations', 'tenant-links'] as const,
  list: ['integrations', 'tenant-links', 'list'] as const,
  options: ['integrations', 'tenant-links', 'options'] as const,
  deliveries: (state: string, q: string, skip: number, take: number) => ['integrations', 'tenant-links', 'deliveries', { state, q, skip, take }] as const,
  proof: (id: string) => ['integrations', 'tenant-links', 'proof', id] as const,
}

export function useTenantLinks(enabled = true) {
  return useQuery({
    queryKey: tenantLinkKeys.list,
    queryFn: () => unwrap(api.GET('/api/v1/integrations/tenant-links')),
    enabled,
  })
}

export function useTenantLinkOptions(enabled: boolean) {
  return useQuery({
    queryKey: tenantLinkKeys.options,
    queryFn: () => unwrap(api.GET('/api/v1/integrations/tenant-links/options')),
    enabled,
    staleTime: 60_000,
    meta: { handleAccessDenied: false },
  })
}

export function useTenantLinkDeliveries(state: string, q: string, skip: number, take: number, enabled = true) {
  return useQuery({
    queryKey: tenantLinkKeys.deliveries(state, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/tenant-links/deliveries', { params: { query: { state: state || undefined, q: q || undefined, skip, take } } })),
    enabled,
  })
}

/** Lector de páginas para `fetchAllPages` (exportación completa con los filtros de la pantalla). */
export function tenantLinkDeliveryPageReader(state: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/integrations/tenant-links/deliveries', { params: { query: { state: state || undefined, q: q || undefined, skip, take } } }))
}

export function useTenantLinkMutations() {
  const qc = useQueryClient()
  // también refresca la orden de venta que muestra la entrega enlazada
  const invalidate = () => Promise.all([qc.invalidateQueries({ queryKey: tenantLinkKeys.all }), qc.invalidateQueries({ queryKey: ['sales'] })])
  return {
    propose: useMutation({
      mutationFn: (body: TenantLinkProposeRequest) => unwrap(api.POST('/api/v1/integrations/tenant-links', { body })),
      onSettled: invalidate,
    }),
    accept: useMutation({
      mutationFn: (v: { publicId: string; clientPublicId: string | null; note: string | null }) =>
        unwrap(api.POST('/api/v1/integrations/tenant-links/{publicId}/accept', { params: { path: { publicId: v.publicId } }, body: { clientPublicId: v.clientPublicId, note: v.note } })),
      onSettled: invalidate,
    }),
    reject: useMutation({
      mutationFn: (v: { publicId: string; reason: string | null }) =>
        unwrap(api.POST('/api/v1/integrations/tenant-links/{publicId}/reject', { params: { path: { publicId: v.publicId } }, body: { reason: v.reason } })),
      onSettled: invalidate,
    }),
    end: useMutation({
      mutationFn: (v: { publicId: string; reason: string }) =>
        unwrap(api.POST('/api/v1/integrations/tenant-links/{publicId}/end', { params: { path: { publicId: v.publicId } }, body: { reason: v.reason } })),
      onSettled: invalidate,
    }),
    retry: useMutation({
      mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/integrations/tenant-links/deliveries/{publicId}/retry', { params: { path: { publicId } } })),
      onSettled: invalidate,
    }),
    sync: useMutation({
      mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/integrations/tenant-links/deliveries/{publicId}/sync', { params: { path: { publicId } } })),
      onSettled: invalidate,
    }),
  }
}

/** Comprobante de entrega leído en vivo de la otra compañía; `null` mientras no haya (204). */
export function useLinkedDeliveryProof(publicId: string | null) {
  return useQuery({
    queryKey: tenantLinkKeys.proof(publicId ?? ''),
    queryFn: async () => (await unwrap(api.GET('/api/v1/integrations/tenant-links/deliveries/{publicId}/proof', { params: { path: { publicId: publicId! } } }))) ?? null,
    enabled: publicId !== null,
    meta: { handleAccessDenied: false },
  })
}

/** Abre un archivo del comprobante (firma, foto): se pide con la sesión y se muestra con un enlace temporal. */
export async function openProofFile(deliveryId: string, filePublicId: string): Promise<void> {
  const res = await api.GET('/api/v1/integrations/tenant-links/deliveries/{publicId}/proof/files/{filePublicId}', { params: { path: { publicId: deliveryId, filePublicId } }, parseAs: 'blob' })
  if (res.error !== undefined || !res.response.ok) throw new Error('proof-file')
  const url = URL.createObjectURL(res.data as Blob)
  window.open(url, '_blank', 'noopener')
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
}
