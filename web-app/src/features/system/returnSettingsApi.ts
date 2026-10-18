// «Devolver tras N intentos» (Lote 113): con cuántos intentos fallidos se SUGIERE devolver una orden (0 = sin sugerencia). Ver: cualquier usuario; cambiar: admin.tenant. Solo una sugerencia: nunca bloquea.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

export type ReturnSettingsDto = components['schemas']['ReturnSettingsDto']
export const returnSettingsKey = ['/api/v1/tenant/return-settings'] as const

export function useReturnSettings() {
  return useQuery({ queryKey: returnSettingsKey, queryFn: () => unwrap(api.GET('/api/v1/tenant/return-settings')), meta: { handleAccessDenied: false } })
}

/** `PUT /api/v1/tenant/return-settings`: de 0 a 20. */
export function useSaveReturnSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (suggestAfterAttempts: number) => unwrap(api.PUT('/api/v1/tenant/return-settings', { body: { suggestAfterAttempts } })),
    onSuccess: (data) => qc.setQueryData(returnSettingsKey, data),
  })
}
