// IP preferida / red de confianza (2026-10-12; docs/ip-preferida-mfa-diseno.md): lecturas y escrituras de la lista de la compañía sobre el cliente generado.
// Todo es de `admin.tenant`; las escrituras exigen además MFA reciente: el cliente (`kernel/api/client.ts`) abre solo el modal de reautenticación ante el
// 403 `aal2_required` y reintenta una vez.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

export type TrustedNetworkDto = components['schemas']['TrustedNetworkDto']
export type TrustedNetworksDto = components['schemas']['TrustedNetworksDto']
export type TrustedNetworkRequest = components['schemas']['TrustedNetworkRequest']

export const trustedNetworkKeys = { list: ['/api/v1/tenant/trusted-networks'] as const }

/** `GET /api/v1/tenant/trusted-networks`: la lista, el máximo, el interruptor del servidor, la IP actual y la entrada que la cubre. */
export function useTrustedNetworks(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: trustedNetworkKeys.list,
    queryFn: () => unwrap(api.GET('/api/v1/tenant/trusted-networks')),
    enabled: options?.enabled ?? true,
    meta: { handleAccessDenied: false },
  })
}

/** `POST /api/v1/tenant/trusted-networks`. */
export function useCreateTrustedNetwork() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: TrustedNetworkRequest) => unwrap(api.POST('/api/v1/tenant/trusted-networks', { body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: trustedNetworkKeys.list }),
  })
}

/** `PUT /api/v1/tenant/trusted-networks/{publicId}`: lo que va null no cambia. */
export function useUpdateTrustedNetwork() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: TrustedNetworkRequest }) =>
      unwrap(api.PUT('/api/v1/tenant/trusted-networks/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: trustedNetworkKeys.list }),
  })
}

/** `DELETE /api/v1/tenant/trusted-networks/{publicId}`. */
export function useDeleteTrustedNetwork() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (publicId: string): Promise<void> => {
      await unwrap(api.DELETE('/api/v1/tenant/trusted-networks/{publicId}', { params: { path: { publicId } } }))
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: trustedNetworkKeys.list }),
  })
}

/** `PUT /api/v1/tenant/trusted-networks/limit`: máximo de entradas de la compañía (null = sin tope). */
export function useSetTrustedNetworkLimit() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (maxEntries: number | null) => unwrap(api.PUT('/api/v1/tenant/trusted-networks/limit', { body: { maxEntries } })),
    onSuccess: (dto) => qc.setQueryData(trustedNetworkKeys.list, dto),
  })
}
