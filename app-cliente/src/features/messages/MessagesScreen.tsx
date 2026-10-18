// Mensajes: mi conversación con el equipo de servicio al cliente de la compañía (la mía: nadie más de mi empresa la ve). La pantalla de la conversación es ChatScreen (burbujas, anteriores, escribir); aquí solo se le ponen
// las llamadas del equipo y los textos.
import { useT } from '../../kernel/i18n/useT'
import { getMessages, markMessagesRead, sendMessage } from '../portalApi'
import { type ChatApi, ChatScreen } from './ChatScreen'
import { setUnread } from './unread'

export { MAX_LENGTH, mergeMessages, REFRESH_MS } from './ChatScreen'

const teamApi: ChatApi = { load: getMessages, send: sendMessage, markRead: markMessagesRead }

export function MessagesScreen() {
  const { t } = useT()
  return <ChatScreen api={teamApi} help={t('messages.help')} empty={t('messages.empty')} otherLabel={t('messages.team')} onUnread={setUnread} />
}
