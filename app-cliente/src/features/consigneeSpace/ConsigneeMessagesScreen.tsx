// Mensajes del consignatario con su cliente (Lote 117): su conversación (la abre él con su primer mensaje); la pantalla de la conversación es la misma de los demás mensajes del portal.
import { useT } from '../../kernel/i18n/useT'
import { type ChatApi, ChatScreen } from '../messages/ChatScreen'
import { setConsigneesUnread } from '../messages/unread'
import { getConsigneeOwnMessages, markConsigneeOwnMessagesRead, sendConsigneeOwnMessage } from '../portalApi'

const ownApi: ChatApi = { load: getConsigneeOwnMessages, send: sendConsigneeOwnMessage, markRead: markConsigneeOwnMessagesRead }

export function ConsigneeMessagesScreen() {
  const { t } = useT()
  return <ChatScreen api={ownApi} help={t('consigneeSpace.chat.help')} empty={t('consigneeSpace.chat.empty')} otherLabel={t('consigneeSpace.chat.client')} onUnread={setConsigneesUnread} />
}
