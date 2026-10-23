// Administración de la mensajería (Lote 212): quién participa (FULL | OFFICE | NONE, por usuario y compañía; permiso admin.users), cuánto viven las fotos del chat
// (admin.tenant) y los responsables de una ruta o zona (messages.use para verlos; trips.plan / fleet.manage para cambiarlos).
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type MessagingModeDto = Schemas['MessagingModeDto']
export type MessagingSettingsDto = Schemas['MessagingSettingsDto']
export type ResponsibleDto = Schemas['ResponsibleDto']

export type MessagingMode = 'FULL' | 'OFFICE' | 'NONE'
export const MESSAGING_MODES: readonly MessagingMode[] = ['FULL', 'OFFICE', 'NONE']

export const messagingAdminKeys = {
  modes: ['messaging', '/api/v1/messaging-modes'] as const,
  settings: ['messaging', '/api/v1/messaging-settings'] as const,
  responsibles: (entity: string, key: string) => ['messaging', '/api/v1/messages/responsibles', entity, key] as const,
}

/** La mensajería de cada persona de la compañía. Solo con admin.users (si no, no se pide). */
export function useMessagingModes(enabled = true) {
  return useQuery({
    queryKey: messagingAdminKeys.modes,
    queryFn: () => unwrap(api.GET('/api/v1/messaging-modes')),
    enabled,
    staleTime: 30_000,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

export function useSetMessagingMode() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { userId: number; mode: MessagingMode }) => unwrap(api.PUT('/api/v1/messaging-modes/{userId}', { params: { path: { userId: v.userId } }, body: { mode: v.mode } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: messagingAdminKeys.modes }),
  })
}

export function useMessagingSettings(enabled = true) {
  return useQuery({
    queryKey: messagingAdminKeys.settings,
    queryFn: () => unwrap(api.GET('/api/v1/messaging-settings')),
    enabled,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

export function useUpdateMessagingSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { photoDays?: number; photoAction?: string }) => unwrap(api.PUT('/api/v1/messaging-settings', { body })),
    onSuccess: (data) => qc.setQueryData(messagingAdminKeys.settings, data),
  })
}

/** Los responsables de una ruta (`TRIP`, clave = publicId) o de una zona de despacho (`DISPATCH_ZONE`, clave = id). */
export function useResponsibles(entity: 'TRIP' | 'DISPATCH_ZONE', key: string, enabled = true) {
  return useQuery({
    queryKey: messagingAdminKeys.responsibles(entity, key),
    queryFn: () => unwrap(api.GET('/api/v1/messages/responsibles', { params: { query: { entity, key } } })),
    enabled: enabled && key !== '',
    staleTime: 15_000,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

export function useSetResponsibles() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { entity: 'TRIP' | 'DISPATCH_ZONE'; key: string; userIds: number[] }) => unwrap(api.PUT('/api/v1/messages/responsibles', { body: v })),
    onSuccess: (data, v) => qc.setQueryData(messagingAdminKeys.responsibles(v.entity, v.key), data),
  })
}
