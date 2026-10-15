// Kemi, la capa de presentación del asistente (Lote 52). NO reemplaza la campanita: la anuncia.
// - Despierta: la mascota flotante abajo a la derecha. Cuando llega un aviso nuevo parpadea y muestra el globo «Te llegó un aviso → Ver»
//   (Ver abre la campanita). Un clic en Kemi abre sus opciones ya listas (sin escribir): ver mis avisos y dormir a Kemi.
// - Dormida: se esconde en la esquina de la cabecera (junto a la campanita); un clic la despierta. Dormida no muestra globos.
// El «cerebro» de Kemi (hacer cosas, proponer rutas, mensajes) es de lotes posteriores; las opciones de aquí son botones.
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useT } from '../../kernel/i18n'
import { messageBubbleText } from '../messages/messageRules'
import { useMessages } from '../messages/MessagesProvider'
import { KemiMascot } from './KemiMascot'
import { bubbleKey } from './kemiState'
import { useNotifications } from './NotificationsProvider'
import './notifications.css'

/** El botón de la cabecera con Kemi dormida; despertarla la devuelve a su lugar flotante. */
export function KemiSleepButton() {
  const t = useT()
  const { asleep, setAsleep } = useNotifications()
  if (!asleep) return null
  return (
    <button type="button" className="iconbtn kemi-zz" aria-label={t('notifications.kemi.wake')} title={t('notifications.kemi.wake')} onClick={() => setAsleep(false)}>
      <KemiMascot size={26} sleeping />
    </button>
  )
}

/** Kemi despierta: la mascota, el globo del aviso nuevo y su panel de opciones. */
export function KemiFloating() {
  const t = useT()
  const { asleep, setAsleep, unread, bubble, dismissBubble, setBellOpen, kemiOpen, setKemiOpen } = useNotifications()
  const msg = useMessages()
  const navigate = useNavigate()
  const root = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!kemiOpen) return
    const onDown = (e: MouseEvent) => {
      if (root.current && !root.current.contains(e.target as Node)) setKemiOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setKemiOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [kemiOpen, setKemiOpen])

  if (asleep) return null
  return (
    <div className="kemi" ref={root}>
      {kemiOpen && (
        <div className="kemi-panel" role="dialog" aria-label={t('notifications.kemi.name')}>
          <div className="kemi-head">
            <KemiMascot size={30} />
            <div>
              <b>{t('notifications.kemi.name')}</b>
              <small>{t('notifications.kemi.subtitle')}</small>
            </div>
            <button type="button" className="iconbtn kemi-x" aria-label={t('notifications.kemi.close')} onClick={() => setKemiOpen(false)}>
              ✕
            </button>
          </div>
          <p className="kemi-msg">{unread > 0 ? t('notifications.kemi.hiUnread', { n: unread }) : t('notifications.kemi.hi')}</p>
          <div className="kemi-options">
            {msg.enabled && (
              <button
                type="button"
                className="kemi-opt"
                onClick={() => {
                  setKemiOpen(false)
                  msg.openNewMessage()
                }}
              >
                {t('messages.kemi.optSend')}
              </button>
            )}
            {msg.enabled && (
              <button
                type="button"
                className="kemi-opt"
                onClick={() => {
                  setKemiOpen(false)
                  navigate('/messages')
                }}
              >
                {t('messages.kemi.optMine')}
                {msg.count > 0 && <span className="kemi-count">{msg.count}</span>}
              </button>
            )}
            <button type="button" className="kemi-opt" onClick={() => setBellOpen(true)}>
              {t('notifications.kemi.optAvisos')}
              {unread > 0 && <span className="kemi-count">{unread}</span>}
            </button>
            <button type="button" className="kemi-opt" onClick={() => setAsleep(true)}>
              {t('notifications.kemi.optSleep')}
            </button>
          </div>
        </div>
      )}
      {msg.bubble !== null && !kemiOpen && (
        <div className="kemi-bubble" role="status">
          <span>{messageBubbleText(msg.bubble.fromName, msg.bubble.count, t)}</span>
          <button
            type="button"
            className="kemi-see"
            onClick={() => {
              const id = msg.bubble?.threadId
              msg.dismissBubble()
              navigate(id ? `/messages?thread=${id}` : '/messages')
            }}
          >
            {t('notifications.kemi.see')} →
          </button>
          <button type="button" className="kemi-dismiss" aria-label={t('notifications.kemi.dismiss')} onClick={msg.dismissBubble}>
            ✕
          </button>
        </div>
      )}
      {bubble !== null && msg.bubble === null && !kemiOpen && (
        <div className="kemi-bubble" role="status">
          <span>{t(`notifications.kemi.bubble.${bubbleKey(bubble)}`, { n: bubble })}</span>
          <button type="button" className="kemi-see" onClick={() => setBellOpen(true)}>
            {t('notifications.kemi.see')} →
          </button>
          <button type="button" className="kemi-dismiss" aria-label={t('notifications.kemi.dismiss')} onClick={dismissBubble}>
            ✕
          </button>
        </div>
      )}
      <button
        type="button"
        className="kemi-fab"
        aria-label={t('notifications.kemi.fab')}
        aria-haspopup="dialog"
        aria-expanded={kemiOpen}
        onClick={() => setKemiOpen(!kemiOpen)}
      >
        <KemiMascot size={38} blinking={bubble !== null || msg.bubble !== null} />
        {unread + msg.count > 0 && (
          <span className="kemi-tag" aria-hidden="true">
            {unread + msg.count > 99 ? '99+' : unread + msg.count}
          </span>
        )}
      </button>
    </div>
  )
}
