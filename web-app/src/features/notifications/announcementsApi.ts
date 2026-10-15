// Datos de «Enviar avisos» (Lote 54; permiso notifications.send, módulo SYSTEM): personas y roles a quienes se puede avisar, enviar y el historial.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type AnnouncementDto = Schemas['AnnouncementDto']
export type AnnouncementRequest = Schemas['AnnouncementRequest']
export type AnnouncementPersonDto = Schemas['AnnouncementPersonDto']
export type MessageRoleDto = Schemas['MessageRoleDto']

export const announcementKeys = {
  all: ['announcements'] as const,
  list: (skip: number, take: number) => ['announcements', '/api/v1/announcements', skip, take] as const,
  people: (q: string) => ['announcements', '/api/v1/announcements/people', q] as const,
  roles: ['announcements', '/api/v1/announcements/roles'] as const,
}

/** Tope de personas que trae la búsqueda (el API pone 500): «Seleccionar todo» escoge las que llegaron. */
export const PEOPLE_TAKE = 500

export function useAnnouncementPeople(q: string, enabled = true) {
  return useQuery({
    queryKey: announcementKeys.people(q),
    queryFn: () => unwrap(api.GET('/api/v1/announcements/people', { params: { query: { q: q || undefined, take: PEOPLE_TAKE } } })),
    enabled,
    placeholderData: keepPreviousData,
  })
}

export function useAnnouncementRoles(enabled = true) {
  return useQuery({ queryKey: announcementKeys.roles, queryFn: () => unwrap(api.GET('/api/v1/announcements/roles')), enabled, staleTime: 60_000 })
}

export function useAnnouncements(skip: number, take: number) {
  return useQuery({
    queryKey: announcementKeys.list(skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/announcements', { params: { query: { skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

export function announcementPageReader() {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/announcements', { params: { query: { skip, take } } }))
}

export function useSendAnnouncement() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: AnnouncementRequest) => unwrap(api.POST('/api/v1/announcements', { body })),
    onSettled: () => qc.invalidateQueries({ queryKey: announcementKeys.all }),
  })
}
