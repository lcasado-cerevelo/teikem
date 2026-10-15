// El botón de mensajes de la cabecera (Lote 53): un globo de chat con el contador de mensajes sin leer; lleva a la pantalla de mensajes. Aparte de la
// campanita de avisos (los avisos del sistema y los mensajes de personas no se mezclan). Solo lo ve quien tiene messages.use.
import { Link } from 'react-router-dom'
import { IconMessage } from '../../app/icons'
import { useT } from '../../kernel/i18n'
import { badgeText } from '../notifications/NotificationBell'
import { useMessages } from './MessagesProvider'

export function MessagesButton() {
  const t = useT()
  const { enabled, count } = useMessages()
  if (!enabled) return null
  const label = count > 0 ? t('messages.header.openUnread', { n: count }) : t('messages.header.open')
  return (
    <Link to="/messages" className="iconbtn nbell-btn" aria-label={label} title={label}>
      <IconMessage />
      {count > 0 && (
        <span className="nbadge" aria-hidden="true">
          {badgeText(count)}
        </span>
      )}
    </Link>
  )
}
