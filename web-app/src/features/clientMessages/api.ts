// Datos de «Mensajes de clientes» (Lote 104). Permisos clientmessages.reply / clientmessages.manage, módulo CLIENT_PORTAL.
// Sondeo: la lista cada ~20 s, la conversación abierta cada ~10 s y el contador del menú cada ~30 s.
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { mergeMessages, type Conversation, type ClientMessage } from './rules'

export const LIST_POLL_MS = 20_000
export const CONVERSATION_POLL_MS = 10_000
export const UNREAD_POLL_MS = 30_000

export const messageKeys = {
  list: ['/api/v1/client-messages/conversations'] as const,
  conversation: (id: string, take: number) => ['/api/v1/client-messages/conversations/{publicId}', id, take] as const,
  conversationAny: (id: string) => ['/api/v1/client-messages/conversations/{publicId}', id] as const,
  unread: ['/api/v1/client-messages/unread'] as const,
  people: (clientId: string) => ['/api/v1/client-messages/clients/{clientId}/people', clientId] as const,
  team: (clientId: string) => ['/api/v1/client-messages/clients/{clientId}/team', clientId] as const,
  candidates: ['/api/v1/client-messages/team-candidates'] as const,
}

export interface ConversationFilter {
  q: string
  unread: boolean
  clientId: string | null
  take: number
}

export function useConversations(filter: ConversationFilter) {
  const query = { q: filter.q || undefined, unread: filter.unread || undefined, clientId: filter.clientId ?? undefined, take: filter.take }
  return useQuery({
    queryKey: [...messageKeys.list, query],
    queryFn: () => unwrap(api.GET('/api/v1/client-messages/conversations', { params: { query } })),
    refetchInterval: LIST_POLL_MS,
    placeholderData: keepPreviousData,
  })
}

export function useStaffUnread(enabled = true) {
  return useQuery({
    queryKey: messageKeys.unread,
    enabled,
    queryFn: () => unwrap(api.GET('/api/v1/client-messages/unread')),
    refetchInterval: UNREAD_POLL_MS,
    meta: { handleAccessDenied: false },
  })
}

export function useConversation(publicId: string | null, take: number) {
  return useQuery({
    queryKey: messageKeys.conversation(publicId ?? '', take),
    enabled: !!publicId,
    queryFn: () => unwrap(api.GET('/api/v1/client-messages/conversations/{publicId}', { params: { path: { publicId: publicId ?? '' }, query: { take } } })),
    refetchInterval: CONVERSATION_POLL_MS,
  })
}

export function useMessageMutations() {
  const qc = useQueryClient()
  const refreshLists = () => Promise.all([qc.invalidateQueries({ queryKey: messageKeys.list }), qc.invalidateQueries({ queryKey: messageKeys.unread })])
  return {
    send: useMutation({
      mutationFn: ({ id, body }: { id: string; body: string }) =>
        unwrap(api.POST('/api/v1/client-messages/conversations/{publicId}/messages', { params: { path: { publicId: id } }, body: { body } })),
      onSuccess: (message: ClientMessage, { id }) => {
        // la burbuja nueva aparece sin esperar al siguiente sondeo
        qc.setQueriesData<Conversation>({ queryKey: messageKeys.conversationAny(id) }, (old) => (old ? { ...old, messages: mergeMessages(old.messages ?? [], [message]) } : old))
        void refreshLists()
      },
    }),
    markRead: useMutation({
      mutationFn: (id: string) => unwrap(api.POST('/api/v1/client-messages/conversations/{publicId}/read', { params: { path: { publicId: id } } })),
      onSuccess: () => refreshLists(),
    }),
    start: useMutation({
      mutationFn: (body: { clientPublicId: string; personId: number; body: string }) => unwrap(api.POST('/api/v1/client-messages/conversations', { body })),
      onSuccess: () => refreshLists(),
    }),
  }
}

export function useClientPeople(clientId: string, enabled: boolean) {
  return useQuery({
    queryKey: messageKeys.people(clientId),
    enabled: enabled && !!clientId,
    queryFn: () => unwrap(api.GET('/api/v1/client-messages/clients/{clientId}/people', { params: { path: { clientId } } })),
    meta: { handleAccessDenied: false },
  })
}

export function useServiceTeam(clientId: string, enabled: boolean) {
  return useQuery({
    queryKey: messageKeys.team(clientId),
    enabled: enabled && !!clientId,
    queryFn: () => unwrap(api.GET('/api/v1/client-messages/clients/{clientId}/team', { params: { path: { clientId } } })),
    meta: { handleAccessDenied: false },
  })
}

export function useTeamCandidates(enabled: boolean) {
  return useQuery({
    queryKey: messageKeys.candidates,
    enabled,
    queryFn: () => unwrap(api.GET('/api/v1/client-messages/team-candidates')),
    meta: { handleAccessDenied: false },
  })
}

export function useSetServiceTeam(clientId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (userIds: number[]) => unwrap(api.PUT('/api/v1/client-messages/clients/{clientId}/team', { params: { path: { clientId } }, body: { userIds } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: messageKeys.team(clientId) }),
  })
}
