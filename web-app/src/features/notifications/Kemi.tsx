// Kemi, la capa de presentación del asistente (Lote 52). NO reemplaza la campanita: la anuncia.
// - Despierta: la mascota flotante, abajo a la derecha o donde el usuario la arrastre (se acuerda por usuario). Tiene vida: parpadea, mira, guiña y hace muecas (useKemiLife.ts);
//   con el puntero encima se queda pendiente y las pupilas lo siguen; cuando llega un aviso brinca. Cuando llega un aviso nuevo parpadea y muestra el globo «Te llegó un aviso → Ver»
//   (Ver abre la campanita). Un clic en Kemi abre sus opciones ya listas (sin escribir): ver mis avisos y dormir a Kemi.
// - Dormida: se esconde en la esquina de la cabecera (junto a la campanita); un clic la despierta. Dormida no muestra globos.
// El «cerebro» de Kemi (hacer cosas, proponer rutas, mensajes) es de lotes posteriores; las opciones de aquí son botones.
import { useCallback, useEffect, useRef, useState, type PointerEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useT } from '../../kernel/i18n'
import { messageBubbleText } from '../messages/messageRules'
import { useMessages } from '../messages/MessagesProvider'
import { KemiMascot } from './KemiMascot'
import { DEFAULT_POS, DRAG_THRESHOLD, anchorStyle, clampPos, isMoved, quadrant, readPos, writePos, type KemiPos } from './kemiLife'
import { bubbleKey } from './kemiState'
import { useKemiLife } from './useKemiLife'
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
  const { asleep, setAsleep, unread, bubble, dismissBubble, setBellOpen, kemiOpen, setKemiOpen, userKey } = useNotifications()
  const msg = useMessages()
  const navigate = useNavigate()
  const root = useRef<HTMLDivElement>(null)
  const svg = useRef<SVGSVGElement>(null)
  const excited = bubble !== null || msg.bubble !== null
  const life = useKemiLife(svg, !asleep && !excited && !kemiOpen)

  // dónde está Kemi: se arrastra con el puntero (un movimiento de menos de 5 px sigue siendo un clic) y se guarda por usuario
  const [pos, setPos] = useState<KemiPos>(() => readPos(userKey))
  const [dragging, setDragging] = useState(false)
  const drag = useRef<{ x: number; y: number; from: KemiPos; moved: boolean; now: KemiPos } | null>(null)
  const justDragged = useRef(false)
  const [view, setView] = useState(() => ({ w: window.innerWidth, h: window.innerHeight }))
  useEffect(() => {
    const onResize = () => setView({ w: window.innerWidth, h: window.innerHeight })
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])
  const shown = clampPos(pos, view.w, view.h)
  const side = quadrant(shown, view.w, view.h)

  const onDown = (e: PointerEvent<HTMLButtonElement>) => {
    if (e.pointerType === 'mouse' && e.button !== 0) return
    drag.current = { x: e.clientX, y: e.clientY, from: shown, moved: false, now: shown }
    e.currentTarget.setPointerCapture?.(e.pointerId)
  }
  const onMove = (e: PointerEvent<HTMLButtonElement>) => {
    life.onPointerMove(e)
    const d = drag.current
    if (!d) return
    const dx = e.clientX - d.x
    const dy = e.clientY - d.y
    if (!d.moved) {
      if (Math.hypot(dx, dy) < DRAG_THRESHOLD) return
      d.moved = true
      setDragging(true)
    }
    d.now = clampPos({ right: d.from.right - dx, bottom: d.from.bottom - dy }, window.innerWidth, window.innerHeight)
    if (root.current) {
      root.current.style.right = `${d.now.right}px`
      root.current.style.bottom = `${d.now.bottom}px`
      root.current.style.top = 'auto'
      root.current.style.left = 'auto'
    }
  }
  const clearInline = () => {
    const st = root.current?.style
    if (st) [st.top, st.bottom, st.left, st.right] = ['', '', '', '']
  }
  const onUp = () => {
    const d = drag.current
    drag.current = null
    if (!d?.moved) return
    justDragged.current = true
    window.setTimeout(() => (justDragged.current = false), 0)
    setDragging(false)
    clearInline()
    setPos(d.now)
    writePos(userKey, d.now)
    setKemiOpen(false)
  }
  const goHome = useCallback(() => {
    setPos(DEFAULT_POS)
    writePos(userKey, DEFAULT_POS)
    clearInline()
    setKemiOpen(false)
  }, [userKey, setKemiOpen])

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
    <div
      className={`kemi${dragging ? ' dragging' : ''}`}
      ref={root}
      data-v={side.v}
      data-h={side.h}
      style={isMoved(shown) ? anchorStyle(shown, view.w, view.h) : undefined}
    >
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
            {isMoved(shown) && (
              <button type="button" className="kemi-opt" onClick={goHome}>
                {t('notifications.kemi.optHome')}
              </button>
            )}
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
        className={`kemi-fab${excited ? ' jump' : ''}`}
        aria-label={t('notifications.kemi.fab')}
        aria-haspopup="dialog"
        aria-expanded={kemiOpen}
        onClick={() => {
          if (!justDragged.current) setKemiOpen(!kemiOpen)
        }}
        onPointerDown={onDown}
        onPointerMove={onMove}
        onPointerUp={onUp}
        onPointerCancel={onUp}
        onPointerEnter={life.onPointerEnter}
        onPointerLeave={life.onPointerLeave}
      >
        <KemiMascot size={38} excited={excited} svgRef={svg} />
        {unread + msg.count > 0 && (
          <span className="kemi-tag" aria-hidden="true">
            {unread + msg.count > 99 ? '99+' : unread + msg.count}
          </span>
        )}
      </button>
    </div>
  )
}
