// «Hablar sobre esta orden / ruta» (Lote 212): el botón que abre «Nuevo mensaje» con la tarjeta ya puesta y los sugeridos arriba, y el panel de la ficha con
// los mensajes de MIS conversaciones que hablan de esa orden o ruta. Sin el permiso messages.use (o sin conexión en vivo posible) no se pinta nada.
import { Link } from 'react-router-dom'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Panel, Spinner } from '../../kernel/ui'
import { useMessagesAbout } from './messagesApi'
import { useMessages, type AboutRef } from './MessagesProvider'
import './messages.css'

export function TalkAboutButton({ about, compact }: { about: AboutRef; compact?: boolean }) {
  const t = useT()
  const { enabled, openNewMessage } = useMessages()
  if (!enabled) return null
  const label = t(about.entity === 'ORDER' ? 'messages.about.talkOrder' : 'messages.about.talkTrip')
  return (
    <button type="button" className={compact ? 'rowbtn' : 'btn'} aria-label={label} title={label} onClick={() => openNewMessage(about)}>
      <span aria-hidden="true">💬</span>
      {!compact && ` ${label}`}
    </button>
  )
}

/** El panel «Mensajes» de la ficha de una orden o ruta. */
export function MessagesAboutPanel({ about }: { about: AboutRef }) {
  const t = useT()
  const f = useFormat()
  const { enabled } = useMessages()
  const q = useMessagesAbout(about.entity, about.publicId, enabled)
  if (!enabled) return null
  const rows = q.data ?? []
  return (
    <Panel icon={<span aria-hidden="true">💬</span>} title={t('messages.about.panel')} actions={<TalkAboutButton about={about} compact />}>
      {q.isPending ? (
        <Spinner block />
      ) : rows.length === 0 ? (
        <p className="note">{t('messages.about.empty')}</p>
      ) : (
        <ul className="msg-about-list">
          {rows.map((r) => (
            <li key={r.messageId}>
              <Link to={`/messages?thread=${r.threadPublicId}`}>
                <b>{r.senderName}</b> <span>{r.preview}</span>
                <time dateTime={r.createdAtUtc}>{f.dateTime(r.createdAtUtc!)}</time>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}
