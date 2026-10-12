// Datos de los usuarios de portal del cliente (F-A3, manual 02 cap. 7). Permiso `portalusers.manage` y módulo CLIENT_PORTAL.
// Invitar y reenviar son [SkipIdempotency] en el servidor (devuelven el enlace): sin Idempotency-Key. Suspender, reactivar y dar de baja
// llevan una clave nueva por intento. Cada escritura refresca la lista del cliente.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { PortalInviteRequest } from './portalRules'

export const portalKeys = {
  list: (clientPublicId: string) => ['/api/v1/clients/{publicId}/portal-users', clientPublicId] as const,
}

const idempotency = () => ({ 'Idempotency-Key': crypto.randomUUID() })

export function usePortalUsers(clientPublicId: string | null, enabled = true) {
  return useQuery({
    queryKey: portalKeys.list(clientPublicId ?? ''),
    enabled: enabled && !!clientPublicId,
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}/portal-users', { params: { path: { publicId: clientPublicId ?? '' } } })),
    // lectura secundaria de la ficha: un 403 (módulo apagado, permiso) no saca al usuario de la pantalla
    meta: { handleAccessDenied: false },
  })
}

export function usePortalMutations(clientPublicId: string) {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: portalKeys.list(clientPublicId) })
  const path = (id: number) => ({ params: { path: { publicId: clientPublicId, id } }, headers: idempotency() })
  return {
    invite: useMutation({
      mutationFn: (body: PortalInviteRequest) => unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/invite', { params: { path: { publicId: clientPublicId } }, body })),
      onSuccess: refresh,
    }),
    resend: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/{id}/resend-invite', { params: { path: { publicId: clientPublicId, id } } })),
      onSuccess: refresh,
    }),
    suspend: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/{id}/suspend', path(id))),
      onSuccess: refresh,
    }),
    reactivate: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/{id}/reactivate', path(id))),
      onSuccess: refresh,
    }),
    remove: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/clients/{publicId}/portal-users/{id}/remove', path(id))),
      onSuccess: refresh,
    }),
  }
}
