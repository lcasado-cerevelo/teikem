// Formatos de la app: fecha y hora en el idioma de la interfaz, dinero en dólares y el color de cada estatus de la orden.
import type { Lang } from '../kernel/i18n/i18n'

export function formatDateTime(iso: string | null | undefined, lang: Lang): string {
  if (!iso) return '—'
  // el servidor manda UTC sin la «Z» en algunos campos: se lee como UTC
  const date = new Date(/[zZ]|[+-]\d{2}:?\d{2}$/.test(iso) ? iso : `${iso}Z`)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat(lang === 'en' ? 'en-US' : 'es-PR', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

export function formatDate(iso: string | null | undefined, lang: Lang): string {
  if (!iso) return '—'
  const date = new Date(iso.length <= 10 ? `${iso}T12:00:00` : iso)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat(lang === 'en' ? 'en-US' : 'es-PR', { dateStyle: 'medium' }).format(date)
}

export function formatMoney(amount: number | null | undefined, lang: Lang): string {
  if (amount === null || amount === undefined) return '—'
  return new Intl.NumberFormat(lang === 'en' ? 'en-US' : 'es-PR', { style: 'currency', currency: 'USD' }).format(amount)
}

export type Tone = 'ok' | 'warn' | 'danger' | 'info' | 'neutral'

/** Color del estatus de una orden por su código: entregada = verde; fallida o cancelada = rojo; pendiente = amarillo; lo demás (en camino, recogida…) = azul. */
export function statusTone(code: string | null | undefined): Tone {
  const c = (code ?? '').toUpperCase()
  if (c.includes('DELIVER') || c === 'COMPLETED') return 'ok'
  if (c.includes('FAIL') || c.includes('CANCEL') || c.includes('REJECT') || c.includes('RETURN')) return 'danger'
  if (c.includes('DRAFT') || c.includes('PENDING') || c === 'CREATED' || c === 'NEW') return 'warn'
  if (c === '') return 'neutral'
  return 'info'
}
