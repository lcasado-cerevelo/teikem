// Datos de la mensajería interna (Lote 53; permiso messages.use): hilos, mensajes, personas, roles y no leídos. Lo no leído se vuelve a
// pedir cada 30 s como RESPALDO de la conexión en vivo; los eventos en vivo invalidan todo lo que empieza con `messageKeys.all`.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type MessagePersonDto = Schemas['MessagePersonDto']
export type MessageRoleDto = Schemas['MessageRoleDto']
export type MessageThreadSummaryDto = Schemas['MessageThreadSummaryDto']
export type MessageThreadDto = Schemas['MessageThreadDto']
export type MessageDto = Schemas['MessageDto']
export type MessageMemberDto = Schemas['MessageMemberDto']
export type MessageUnreadDto = Schemas['MessageUnreadDto']
export type MessageThreadRequest = Schemas['MessageThreadRequest']

export const messageKeys = {
  all: ['messages'] as const,
  unread: ['messages', '/api/v1/messages/unread'] as const,
  threads: ['messages', '/api/v1/messages/threads'] as const,
  thread: (id: string) => ['messages', '/api/v1/messages/threads/{publicId}', id] as const,
  people: (q: string) => ['messages', '/api/v1/messages/people', q] as const,
  roles: ['messages', '/api/v1/messages/roles'] as const,
}

export const UNREAD_FALLBACK_MS = 30_000
export const PRESENCE_PING_MS = 30_000

/** Mensajes sin leer (contador de la cabecera y globo de Kemi). El sondeo lento es solo el respaldo del tiempo real. */
export function useMessageUnread(enabled: boolean) {
  return useQuery({
    queryKey: messageKeys.unread,
    queryFn: () => unwrap(api.GET('/api/v1/messages/unread')),
    enabled,
    refetchInterval: UNREAD_FALLBACK_MS,
    refetchIntervalInBackground: false,
    meta: { handleAccessDenied: false },
  })
}

export function useThreads(enabled = true) {
  return useQuery({ queryKey: messageKeys.threads, queryFn: () => unwrap(api.GET('/api/v1/messages/threads')), enabled, refetchInterval: UNREAD_FALLBACK_MS })
}

/** Un hilo con sus últimos mensajes; `before` pide los anteriores (la pantalla los junta). */
export function useThread(publicId: string | null) {
  return useQuery({
    queryKey: messageKeys.thread(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/messages/threads/{publicId}', { params: { path: { publicId: publicId! }, query: { take: 50 } } })),
    enabled: publicId !== null,
    placeholderData: keepPreviousData,
    refetchInterval: UNREAD_FALLBACK_MS,
  })
}

export async function fetchOlder(publicId: string, before: number): Promise<MessageThreadDto> {
  return unwrap(api.GET('/api/v1/messages/threads/{publicId}', { params: { path: { publicId }, query: { before, take: 50 } } }))
}

/** Personas activas de la compañía (con su presencia) que coinciden con lo escrito: nombre o correo. */
export function usePeople(q: string, enabled = true) {
  return useQuery({
    queryKey: messageKeys.people(q),
    queryFn: () => unwrap(api.GET('/api/v1/messages/people', { params: { query: { q: q || undefined, take: 20 } } })),
    enabled,
    placeholderData: keepPreviousData,
    refetchInterval: UNREAD_FALLBACK_MS,
  })
}

export function useMessageRoles(enabled = true) {
  return useQuery({ queryKey: messageKeys.roles, queryFn: () => unwrap(api.GET('/api/v1/messages/roles')), enabled, staleTime: 60_000 })
}

export function useMessageMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: messageKeys.all })
  const path = (publicId: string) => ({ params: { path: { publicId } } })
  return {
    create: useMutation({ mutationFn: (body: MessageThreadRequest) => unwrap(api.POST('/api/v1/messages/threads', { body })), onSettled: invalidate }),
    send: useMutation({
      mutationFn: (v: { publicId: string; text: string }) => unwrap(api.POST('/api/v1/messages/threads/{publicId}/messages', { ...path(v.publicId), body: { text: v.text } })),
      onSettled: invalidate,
    }),
    read: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/messages/threads/{publicId}/read', path(publicId))), onSettled: invalidate }),
    addMembers: useMutation({
      mutationFn: (v: { publicId: string; userIds: number[] }) => unwrap(api.POST('/api/v1/messages/threads/{publicId}/members', { ...path(v.publicId), body: { userIds: v.userIds } })),
      onSettled: invalidate,
    }),
    leave: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/messages/threads/{publicId}/leave', path(publicId))), onSettled: invalidate }),
    mute: useMutation({
      mutationFn: (v: { publicId: string; muted: boolean }) => unwrap(api.PUT('/api/v1/messages/threads/{publicId}/mute', { ...path(v.publicId), body: { muted: v.muted } })),
      onSettled: invalidate,
    }),
  }
}

/** Respaldo de la presencia (la conexión en vivo también la renueva al conectar). */
export function pingPresence() {
  return unwrap(api.POST('/api/v1/presence/ping'))
}
