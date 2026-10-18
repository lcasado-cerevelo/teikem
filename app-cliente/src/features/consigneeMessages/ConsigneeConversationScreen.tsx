// Una conversación de un consignatario con este cliente (Lote 117): el cliente la lee y la contesta (administrador u operador; quien solo consulta la lee). Al abrirla se marcan como leídos los mensajes del consignatario
// para todos los de su cliente. La pantalla es la misma de los demás mensajes del portal.
import { useLocalSearchParams } from 'expo-router'
import { useMemo, useRef } from 'react'

import { useT } from '../../kernel/i18n/useT'
import { Spinner } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { type ChatApi, ChatScreen } from '../messages/ChatScreen'
import { refreshUnread } from '../messages/unread'
import { getConsigneeConversation, getConsigneeNotices, markConsigneeConversationRead, replyConsigneeConversation } from '../portalApi'

export function ConsigneeConversationScreen() {
  const { t } = useT()
  const { id, name } = useLocalSearchParams<{ id: string; name?: string }>()
  const settings = useAsync(getConsigneeNotices, [])   // de ahí sale si mi rol puede contestar
  const seen = useRef({ lastOther: 0, marked: 0 })   // el último mensaje del consignatario que vi / hasta cuál ya lo marqué leído
  const api = useMemo<ChatApi>(() => {
    const s = seen.current
    return {
      load: async (before, take) => {
        const c = await getConsigneeConversation(id, before, take)
        const msgs = c.messages ?? []
        for (const m of msgs) if (!m.mine && (m.id ?? 0) > s.lastOther) s.lastOther = m.id ?? 0
        return { conversationPublicId: c.publicId, messages: msgs, hasMore: c.hasMore, unread: s.lastOther > s.marked ? 1 : 0 }
      },
      send: (body) => replyConsigneeConversation(id, body),
      markRead: async () => {
        await markConsigneeConversationRead(id)
        s.marked = s.lastOther
      },
    }
  }, [id])

  if (settings.loading && !settings.data) return <Spinner />
  const canEdit = !!settings.data?.canEdit
  return (
    <ChatScreen
      api={api}
      help={t('consigneeMessages.chatHelp', { name: name ?? '' })}
      empty={t('consigneeMessages.empty')}
      otherLabel={t('consigneeMessages.other')}
      onUnread={() => void refreshUnread()}
      readOnlyNote={canEdit ? undefined : t('consigneeMessages.readOnly')}
    />
  )
}
