// Seguimiento público «¿Cómo va mi orden?» (Lote 103): el identificador de la compañía (null = apagado). Ver: cualquier usuario; cambiar: admin.tenant.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

export type TrackingSettingsDto = components['schemas']['TrackingSettingsDto']
export const trackingKey = ['/api/v1/tenant/tracking'] as const

export function useTrackingSettings() {
  return useQuery({ queryKey: trackingKey, queryFn: () => unwrap(api.GET('/api/v1/tenant/tracking')), meta: { handleAccessDenied: false } })
}

/** `PUT /api/v1/tenant/tracking`: slug vacío = apagar. */
export function useSaveTracking() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (slug: string) => unwrap(api.PUT('/api/v1/tenant/tracking', { body: { slug } })),
    onSuccess: (data) => qc.setQueryData(trackingKey, data),
  })
}

/** Dirección de la app del cliente donde vive la página pública (VITE_CLIENT_APP_URL); sin ella se muestra solo la ruta. */
export function trackingLink(slug: string): string {
  const base = (import.meta.env.VITE_CLIENT_APP_URL as string | undefined)?.replace(/\/+$/, '') ?? ''
  return `${base}/track/${slug}`
}
