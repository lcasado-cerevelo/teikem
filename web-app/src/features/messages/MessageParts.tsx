// Piezas de una burbuja del chat (Lote 212): la tarjeta de la orden o ruta de la que se habla, las fotos (con la sesión; nunca un enlace directo) y el visor.
import { Link } from 'react-router-dom'
import { useT } from '../../kernel/i18n'
import { Modal } from '../../kernel/ui'
import { useChatPhoto, type MessageAttachmentDto, type MessageRefDto } from './messagesApi'

/** A dónde lleva la tarjeta: la ficha de la orden o la ruta en Despacho. Sin permiso (`canOpen` falso) solo se ve el título. */
export function refLink(ref: MessageRefDto): string | null {
  if (!ref.canOpen || !ref.publicId) return null
  return ref.entity === 'ORDER' ? `/orders/${ref.publicId}` : `/dispatch?trip=${ref.publicId}`
}

export function RefCard({ refCard }: { refCard: MessageRefDto }) {
  const t = useT()
  const to = refLink(refCard)
  const body = (
    <>
      <b>
        <span aria-hidden="true">{refCard.entity === 'ORDER' ? '📦' : '🚚'}</span> {refCard.title}
      </b>
      {refCard.subtitle && <span>{refCard.subtitle}</span>}
      {refCard.status && <span>{refCard.status}</span>}
    </>
  )
  return to ? (
    <Link to={to} className="msg-card" aria-label={t(refCard.entity === 'ORDER' ? 'messages.card.openOrder' : 'messages.card.openTrip', { title: refCard.title ?? '' })}>
      {body}
    </Link>
  ) : (
    <div className="msg-card nolink">{body}</div>
  )
}

function Gone({ state }: { state: string | null | undefined }) {
  const t = useT()
  return <div className="msg-photo gone">{state === 'DELETED' ? t('messages.photo.deleted') : state === 'ARCHIVED' ? t('messages.photo.archived') : t('messages.photo.unavailable')}</div>
}

export function ChatPhotoThumb({ photo, onOpen }: { photo: MessageAttachmentDto; onOpen: (p: MessageAttachmentDto) => void }) {
  const t = useT()
  const q = useChatPhoto(photo, true)
  if (photo.state !== 'ACTIVE' || q.isError) return <Gone state={photo.state === 'ACTIVE' ? 'ERROR' : photo.state} />
  if (!q.data) return <div className="msg-photo loading" aria-busy="true" />
  return (
    <button type="button" className="msg-photo" onClick={() => onOpen(photo)} aria-label={t('messages.photo.open')}>
      <img src={q.data} alt={t('messages.photo.alt')} />
    </button>
  )
}

/** La foto completa a pantalla completa. */
export function PhotoViewer({ photo, onClose }: { photo: MessageAttachmentDto | null; onClose: () => void }) {
  const t = useT()
  return photo ? <ViewerBody photo={photo} onClose={onClose} title={t('messages.photo.alt')} /> : null
}

function ViewerBody({ photo, onClose, title }: { photo: MessageAttachmentDto; onClose: () => void; title: string }) {
  const q = useChatPhoto(photo, false)
  return (
    <Modal open size="lg" title={title} onClose={onClose}>
      <div className="msg-viewer">{q.data ? <img src={q.data} alt={title} /> : q.isError ? <Gone state="ERROR" /> : <div className="msg-photo loading" aria-busy="true" />}</div>
    </Modal>
  )
}
