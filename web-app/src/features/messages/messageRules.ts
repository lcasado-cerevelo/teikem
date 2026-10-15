// Reglas puras de la pantalla de mensajes (Lote 53): iniciales y color del avatar, «en línea / visto hace…», separadores de día,
// agrupar mensajes seguidos de la misma persona y el texto del globo de Kemi. Sin React: se prueban solas.
import type { MessageDto } from './messagesApi'

type Translate = (key: string, params?: Record<string, string | number>) => string

export const ONLINE_WINDOW_MS = 90_000
export const MAX_BODY = 2000

/** Dos letras: «Ana Rivera» → «AR»; un solo nombre → sus dos primeras; sin nombre → «·». */
export function initialsOf(name: string | null | undefined): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '·'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[1][0]).toUpperCase()
}

/** Un tono (0–5) estable por nombre para colorear el avatar. */
export function avatarTone(name: string | null | undefined): number {
  let h = 0
  for (const ch of name ?? '') h = (h * 31 + ch.charCodeAt(0)) >>> 0
  return h % 6
}

export function isOnline(lastSeenUtc: string | null | undefined, nowMs: number): boolean {
  if (!lastSeenUtc) return false
  const seen = Date.parse(lastSeenUtc)
  return Number.isFinite(seen) && nowMs - seen < ONLINE_WINDOW_MS
}

/** «En línea», «visto hace 5 min», «visto hace 3 h», «visto el 10/12/2026»; sin dato, «sin conexión todavía». */
export function presenceText(online: boolean, lastSeenUtc: string | null | undefined, nowMs: number, t: Translate, date: (iso: string) => string): string {
  if (online) return t('messages.presence.online')
  if (!lastSeenUtc) return t('messages.presence.never')
  const mins = Math.max(0, Math.round((nowMs - Date.parse(lastSeenUtc)) / 60_000))
  if (mins < 1) return t('messages.presence.justNow')
  if (mins < 60) return t('messages.presence.minutes', { n: mins })
  if (mins < 24 * 60) return t('messages.presence.hours', { n: Math.round(mins / 60) })
  return t('messages.presence.date', { date: date(lastSeenUtc) })
}

/** Clave del día local (YYYY-MM-DD) de un instante ISO, en la zona del navegador. */
export function dayKey(iso: string): string {
  const d = new Date(iso)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export type DayLabel = 'today' | 'yesterday' | 'date'

export function dayLabel(iso: string, nowMs: number): DayLabel {
  const k = dayKey(iso)
  if (k === dayKey(new Date(nowMs).toISOString())) return 'today'
  if (k === dayKey(new Date(nowMs - 86_400_000).toISOString())) return 'yesterday'
  return 'date'
}

export interface MessageGroup {
  /** Clave del día de este grupo (el separador se muestra cuando cambia). */
  day: string
  /** El separador de día va antes de este grupo. */
  showDay: boolean
  senderId: number
  senderName: string
  mine: boolean
  messages: MessageDto[]
}

/** Agrupa mensajes seguidos de la misma persona dentro de 5 minutos y marca dónde empieza cada día. */
export function groupMessages(messages: readonly MessageDto[], gapMs = 5 * 60_000): MessageGroup[] {
  const groups: MessageGroup[] = []
  for (const m of messages) {
    const day = dayKey(m.createdAtUtc!)
    const last = groups[groups.length - 1]
    const lastMsg = last?.messages[last.messages.length - 1]
    if (last && last.senderId === m.senderId && last.day === day && lastMsg && Date.parse(m.createdAtUtc!) - Date.parse(lastMsg.createdAtUtc!) <= gapMs) {
      last.messages.push(m)
    } else {
      groups.push({ day, showDay: !last || last.day !== day, senderId: m.senderId!, senderName: m.senderName ?? '', mine: !!m.mine, messages: [m] })
    }
  }
  return groups
}

/** Texto del globo de Kemi: «Te llegó un mensaje de Ana» (una persona) — con la cantidad si son varios. */
export function messageBubbleText(fromName: string, count: number, t: Translate): string {
  return count <= 1 ? t('messages.kemi.bubbleOne', { name: fromName }) : t('messages.kemi.bubbleMany', { name: fromName, n: count })
}

/** Qué hacer cuando llega un mensaje en vivo. */
export function liveDecision(args: { muted: boolean; threadOpen: boolean; windowVisible: boolean; asleep: boolean }): 'markRead' | 'bubble' | 'quiet' {
  if (args.threadOpen && args.windowVisible) return 'markRead'   // lo está viendo: se marca leído y no se avisa
  if (args.muted || args.asleep) return 'quiet'                  // silenciado o Kemi dormida: solo el contador
  return 'bubble'
}

/** Enter envía; Mayús+Enter es salto de línea; nunca envía vacío ni mientras se compone con un teclado de IME. */
export function shouldSend(e: { key: string; shiftKey: boolean; isComposing?: boolean }, text: string): boolean {
  return e.key === 'Enter' && !e.shiftKey && !e.isComposing && text.trim().length > 0
}
