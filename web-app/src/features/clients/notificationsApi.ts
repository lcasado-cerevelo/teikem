// Datos de «Avisos al cliente» (Lote 98, P10): la configuración por cliente (la decide la compañía; clients.read para ver, clients.update para cambiar) y el registro de resúmenes enviados.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type ClientNotificationSettingsDto = Schemas['ClientNotificationSettingsDto']
export type ClientNotificationSettingDto = Schemas['ClientNotificationSettingDto']
export type ClientNotificationDto = Schemas['ClientNotificationDto']

export const notificationKeys = {
  all: (clientPublicId: string) => ['client-notifications', clientPublicId] as const,
  settings: (clientPublicId: string) => ['client-notifications', clientPublicId, 'settings'] as const,
  log: (clientPublicId: string, skip: number, take: number) => ['client-notifications', clientPublicId, 'log', skip, take] as const,
}

export function useClientNotificationSettings(clientPublicId: string, enabled = true) {
  return useQuery({
    queryKey: notificationKeys.settings(clientPublicId),
    enabled: enabled && !!clientPublicId,
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}/notifications/settings', { params: { path: { publicId: clientPublicId } } })),
    meta: { handleAccessDenied: false },
  })
}

export function useClientNotificationLog(clientPublicId: string, skip: number, take: number, enabled = true) {
  return useQuery({
    queryKey: notificationKeys.log(clientPublicId, skip, take),
    enabled: enabled && !!clientPublicId,
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}/notifications/log', { params: { path: { publicId: clientPublicId }, query: { skip, take } } })),
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

export function useSaveClientNotificationSettings(clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (settings: ClientNotificationSettingDto[]) => unwrap(api.PUT('/api/v1/clients/{publicId}/notifications/settings', { params: { path: { publicId: clientPublicId } }, body: { settings } })),
    onSuccess: (data) => qc.setQueryData(notificationKeys.settings(clientPublicId), data),
  })
}
