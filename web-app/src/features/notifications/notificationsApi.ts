// Datos de los avisos (Lote 52): la bandeja del usuario (campanita y Kemi; sin permiso especial) y las reglas de aviso de la compañía
// (Sistema → Reglas de aviso; permiso notifications.manage). La cantidad de no leídos se vuelve a pedir cada 30 s mientras la pestaña
// está visible; cada escritura invalida todo lo que empieza con `notificationKeys.all`.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type UserNotificationDto = Schemas['UserNotificationDto']
export type NotificationPrefDto = Schemas['NotificationPrefDto']
export type NotificationEventDto = Schemas['NotificationEventDto']
export type NotificationChannelStatusDto = Schemas['NotificationChannelStatusDto']
export type NotificationRuleDto = Schemas['NotificationRuleDto']
export type NotificationRuleRequest = Schemas['NotificationRuleRequest']
export type NotificationRuleChannelDto = Schemas['NotificationRuleChannelDto']
export type NotificationRecipientDto = Schemas['NotificationRecipientDto']
export type NotificationLogDto = Schemas['NotificationLogDto']
export type NotificationTestResultDto = Schemas['NotificationTestResultDto']

export const notificationKeys = {
  all: ['notifications'] as const,
  unread: ['notifications', '/api/v1/inbox/unread-count'] as const,
  inbox: ['notifications', '/api/v1/inbox'] as const,
  prefs: ['notifications', '/api/v1/inbox/prefs'] as const,
  events: ['notifications', '/api/v1/notifications/events'] as const,
  channels: ['notifications', '/api/v1/notifications/channels'] as const,
  rules: ['notifications', '/api/v1/notifications/rules'] as const,
  log: (q: LogQuery) => ['notifications', '/api/v1/notifications/log', q] as const,
}

export const UNREAD_POLL_MS = 30_000

/** Cantidad de avisos sin leer de quien inició sesión. Un error (p. ej. sin red) no saca de la pantalla ni molesta: el contador queda como estaba. */
export function useUnreadCount(enabled = true) {
  return useQuery({
    queryKey: notificationKeys.unread,
    queryFn: () => unwrap(api.GET('/api/v1/inbox/unread-count')),
    refetchInterval: UNREAD_POLL_MS,
    refetchIntervalInBackground: false,
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** Los últimos avisos de la bandeja (los más nuevos primero). Solo se pide con la campanita abierta. */
export function useInbox(enabled: boolean) {
  return useQuery({
    queryKey: notificationKeys.inbox,
    queryFn: () => unwrap(api.GET('/api/v1/inbox', { params: { query: { take: 20 } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export function useInboxPrefs(enabled: boolean) {
  return useQuery({ queryKey: notificationKeys.prefs, queryFn: () => unwrap(api.GET('/api/v1/inbox/prefs')), enabled, meta: { handleAccessDenied: false } })
}

export function useInboxMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: notificationKeys.all })
  return {
    markRead: useMutation({ mutationFn: (id: number) => unwrap(api.POST('/api/v1/inbox/{id}/read', { params: { path: { id } } })), onSettled: invalidate }),
    markAllRead: useMutation({ mutationFn: () => unwrap(api.POST('/api/v1/inbox/read-all')), onSettled: invalidate }),
    setMuted: useMutation({
      mutationFn: (v: { eventCode: string; muted: boolean }) =>
        unwrap(api.PUT('/api/v1/inbox/prefs/{eventCode}', { params: { path: { eventCode: v.eventCode } }, body: { muted: v.muted } })),
      onSettled: invalidate,
    }),
  }
}

// ---- reglas (notifications.manage) ----

export function useNotificationEvents() {
  return useQuery({ queryKey: notificationKeys.events, queryFn: () => unwrap(api.GET('/api/v1/notifications/events')), staleTime: 10 * 60 * 1000 })
}

export function useNotificationChannels() {
  return useQuery({ queryKey: notificationKeys.channels, queryFn: () => unwrap(api.GET('/api/v1/notifications/channels')), staleTime: 60 * 1000 })
}

export function useNotificationRules() {
  return useQuery({ queryKey: notificationKeys.rules, queryFn: () => unwrap(api.GET('/api/v1/notifications/rules')) })
}

export function useRuleMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: notificationKeys.all })
  return {
    create: useMutation({ mutationFn: (body: NotificationRuleRequest) => unwrap(api.POST('/api/v1/notifications/rules', { body })), onSettled: invalidate }),
    update: useMutation({
      mutationFn: (v: { publicId: string; body: NotificationRuleRequest }) =>
        unwrap(api.PATCH('/api/v1/notifications/rules/{publicId}', { params: { path: { publicId: v.publicId } }, body: v.body })),
      onSettled: invalidate,
    }),
    test: useMutation({
      mutationFn: (v: { publicId: string; channel: string }) =>
        unwrap(api.POST('/api/v1/notifications/rules/{publicId}/test', { params: { path: { publicId: v.publicId } }, body: { channel: v.channel } })),
      onSettled: invalidate,
    }),
    testEvent: useMutation({ mutationFn: (message: string) => unwrap(api.POST('/api/v1/notifications/test-event', { body: { message } })), onSettled: invalidate }),
  }
}

// ---- registro de envíos ----

export interface LogQuery {
  eventCode?: string
  channel?: string
  status?: string
  skip: number
  take: number
}

const clean = (q: LogQuery) => ({ ...q, eventCode: q.eventCode || undefined, channel: q.channel || undefined, status: q.status || undefined })

export function useNotificationLog(query: LogQuery) {
  return useQuery({
    queryKey: notificationKeys.log(query),
    queryFn: () => unwrap(api.GET('/api/v1/notifications/log', { params: { query: clean(query) } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las líneas del registro con el filtro actual (para exportar). */
export function logPageReader(query: Omit<LogQuery, 'skip' | 'take'>) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/notifications/log', { params: { query: clean({ ...query, skip, take }) } }))
}
