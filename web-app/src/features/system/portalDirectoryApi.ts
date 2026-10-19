// Lista de TODAS las personas de portal de la compañía (Lote 121): `GET /api/v1/portal-users?kind=CLIENT|CONSIGNEE` (permiso `portalusers.manage`, módulo CLIENT_PORTAL). Las acciones usan los endpoints por cliente de siempre
// (suspender, reactivar, dar de baja, reenviar la invitación): se llaman con el cliente de la fila y refrescan esta lista y la del expediente de ese cliente.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { portalKeys } from '../clients/portalApi'

export type PortalUserAdminDto = components['schemas']['PortalUserAdminDto']
export type PortalDirectoryKind = 'CLIENT' | 'CONSIGNEE'
export type PortalDirectoryAction = 'suspend' | 'reactivate' | 'remove' | 'resend'

export const portalDirectoryKeys = {
  list: (kind: PortalDirectoryKind, includeRemoved: boolean) => ['/api/v1/portal-users', kind, includeRemoved] as const,
  all: ['/api/v1/portal-users'] as const,
}

export function usePortalDirectory(kind: PortalDirectoryKind, includeRemoved: boolean, enabled = true) {
  return useQuery({
    queryKey: portalDirectoryKeys.list(kind, includeRemoved),
    enabled,
    queryFn: () => unwrap(api.GET('/api/v1/portal-users', { params: { query: { kind, includeRemoved } } })),
    meta: { handleAccessDenied: false },
  })
}

const idempotency = () => ({ 'Idempotency-Key': crypto.randomUUID() })

export function usePortalDirectoryAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ clientPublicId, id, action }: { clientPublicId: string; id: number; action: PortalDirectoryAction }) => {
      const p = { params: { path: { publicId: clientPublicId, id } } }
      switch (action) {
        case 'suspend':
          return unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/{id}/suspend', { ...p, headers: idempotency() }))
        case 'reactivate':
          return unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/{id}/reactivate', { ...p, headers: idempotency() }))
        case 'remove':
          return unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/{id}/remove', { ...p, headers: idempotency() }))
        default:
          return unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/{id}/resend-invite', p))
      }
    },
    onSuccess: (_r, v) => {
      void qc.invalidateQueries({ queryKey: portalDirectoryKeys.all })
      void qc.invalidateQueries({ queryKey: portalKeys.list(v.clientPublicId) })
    },
  })
}
