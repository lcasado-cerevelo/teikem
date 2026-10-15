// Campanita de avisos de la cabecera (Lote 52): contador de no leídos, la lista de los últimos avisos (clic = marcar leído y abrir el
// enlace si lo trae), «Marcar todo como leído» y la lista de avisos opcionales que la persona puede silenciar. No la reemplaza Kemi:
// Kemi solo avisa y la abre. Cada quien ve solo los suyos (la bandeja es personal).
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { IconBell } from '../../app/icons'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Spinner, toast } from '../../kernel/ui'
import { useInbox, useInboxMutations, useInboxPrefs, type UserNotificationDto } from './notificationsApi'
import { useNotifications } from './NotificationsProvider'
import './notifications.css'

export const MAX_BADGE = 99

/** «5» o «99+». */
export function badgeText(n: number): string {
  return n > MAX_BADGE ? `${MAX_BADGE}+` : String(n)
}

/** Un enlace de la web (empieza con «/», nunca con «//» ni con un protocolo); si no, no se navega. */
export function internalLink(link: string | null | undefined): string | null {
  const l = (link ?? '').trim()
  return l.startsWith('/') && !l.startsWith('//') ? l : null
}

function Prefs() {
  const t = useT()
  const lang = useLang()
  const prefs = useInboxPrefs(true)
  const { setMuted } = useInboxMutations()
  if (!prefs.data || prefs.data.length === 0) return null
  return (
    <details className="nprefs">
      <summary>{t('notifications.bell.prefs')}</summary>
      <p className="help">{t('notifications.bell.prefsHelp')}</p>
      {prefs.data.map((p) => (
        <label key={p.eventCode} className="sw">
          <input
            type="checkbox"
            role="switch"
            checked={!p.isMuted}
            aria-label={t('notifications.bell.receive', { name: (lang === 'en' ? p.nameEn : p.name) ?? p.eventCode ?? '' })}
            disabled={setMuted.isPending}
            onChange={(e) => setMuted.mutate({ eventCode: p.eventCode!, muted: !e.target.checked }, { onError: (err) => toast.error(applyProblemDetails(err).title) })}
          />
          <span className="tk" aria-hidden="true" />
          <span>{(lang === 'en' ? p.nameEn : p.name) ?? p.eventCode}</span>
        </label>
      ))}
    </details>
  )
}

export function NotificationBell() {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const { unread, bellOpen, setBellOpen } = useNotifications()
  const inbox = useInbox(bellOpen)
  const { markRead, markAllRead } = useInboxMutations()
  const root = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!bellOpen) return
    const onDown = (e: MouseEvent) => {
      if (root.current && !root.current.contains(e.target as Node)) setBellOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setBellOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [bellOpen, setBellOpen])

  const open = (n: UserNotificationDto) => {
    if (!n.isRead) markRead.mutate(n.id!, { onError: (err) => toast.error(applyProblemDetails(err).title) })
    const to = internalLink(n.linkUrl)
    if (to) {
      setBellOpen(false)
      navigate(to)
    }
  }

  const label = unread > 0 ? t('notifications.bell.openUnread', { n: unread }) : t('notifications.bell.open')
  return (
    <div className="nbell" ref={root}>
      <button type="button" className="iconbtn nbell-btn" aria-label={label} title={label} aria-haspopup="dialog" aria-expanded={bellOpen} onClick={() => setBellOpen(!bellOpen)}>
        <IconBell />
        {unread > 0 && (
          <span className="nbadge" aria-hidden="true">
            {badgeText(unread)}
          </span>
        )}
      </button>
      {bellOpen && (
        <div className="npop" role="dialog" aria-label={t('notifications.bell.title')}>
          <div className="nhead">
            <b>{t('notifications.bell.title')}</b>
            <button
              type="button"
              className="btn sm"
              disabled={unread === 0 || markAllRead.isPending}
              onClick={() => markAllRead.mutate(undefined, { onError: (err) => toast.error(applyProblemDetails(err).title) })}
            >
              {t('notifications.bell.markAll')}
            </button>
          </div>
          {inbox.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(inbox.error).title}
            </p>
          ) : inbox.isPending ? (
            <Spinner block />
          ) : (inbox.data?.items ?? []).length === 0 ? (
            <p className="nempty">{t('notifications.bell.empty')}</p>
          ) : (
            <ul className="nlist">
              {(inbox.data?.items ?? []).map((n) => (
                <li key={n.id}>
                  <button type="button" className={`nitem${n.isRead ? '' : ' unread'}`} onClick={() => open(n)}>
                    <span className="ndot" aria-hidden="true" />
                    <span className="ntxt">
                      <b>{n.title}</b>
                      {n.body && <span className="nbody">{n.body}</span>}
                      <time dateTime={n.createdAtUtc}>{f.dateTime(n.createdAtUtc)}</time>
                    </span>
                    {!n.isRead && <span className="sr-only">{t('notifications.bell.unreadMark')}</span>}
                  </button>
                </li>
              ))}
            </ul>
          )}
          <Prefs />
        </div>
      )}
    </div>
  )
}
