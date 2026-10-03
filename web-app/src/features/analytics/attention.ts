// Lote 14 (D6) — lógica pura del panel "Necesita tu atención" del Pulso (GET /api/v1/analytics/attention). Sin React: arma
// el enlace de "Revisar"/"Ver todos" con la ruta y los parámetros que manda el servidor, el tono de la fila (clases de la
// maqueta .work.tone-*) y el texto de cada tipo de aviso con `t('analytics.attention.items.<code>.*', params)`.
import type { components } from '../../kernel/api/schema'
import type { TParams } from '../../kernel/i18n/i18n'
import { formatEventTime } from './activity'
import { formatNumber } from '../../kernel/format'

export type AttentionDto = components['schemas']['AttentionDto']
export type AttentionItemDto = components['schemas']['AttentionItemDto']
export type AttentionGroupDto = components['schemas']['AttentionGroupDto']

type Translate = (key: string, params?: TParams) => string

/** Tipos de aviso que esta web sabe describir (el resto se pinta con su código y su "Revisar"). */
export const ATTENTION_CODES = ['INVENTORY_DISCREPANCY'] as const
export type AttentionCode = (typeof ATTENTION_CODES)[number]

export function isKnownAttention(code: string | null | undefined): code is AttentionCode {
  return (ATTENTION_CODES as readonly string[]).includes(code ?? '')
}

/** Ruta + parámetros del servidor → `href` de la web (null sin ruta). Los parámetros van en el orden recibido. */
export function attentionHref(route: string | null | undefined, query: Record<string, string> | null | undefined): string | null {
  if (!route) return null
  const qs = new URLSearchParams(Object.entries(query ?? {}).filter(([, v]) => v != null && v !== '')).toString()
  return qs ? `${route}?${qs}` : route
}

/** Tono del servidor (danger, warn, info) → clase de la fila de la maqueta (`.tone-danger`, `.tone-warn`, `.tone-flow`). */
export function attentionToneClass(tone: string | null | undefined): 'tone-danger' | 'tone-warn' | 'tone-flow' {
  switch ((tone ?? '').toLowerCase()) {
    case 'danger':
      return 'tone-danger'
    case 'warn':
    case 'warning':
      return 'tone-warn'
    default:
      return 'tone-flow'
  }
}

/** Número del servidor (cadena con punto decimal) con los separadores de la compañía; '' si no es número. */
export function formatQty(value: string | null | undefined, _lang: string): string {
  if (value == null || value.trim() === '') return ''
  const n = Number(value)
  return Number.isFinite(n) ? formatNumber(n) : value
}

/** Diferencia con signo explícito (+1, −2,5; 0 sin signo): saldo − Kárdex. */
export function formatSigned(value: string | null | undefined, _lang: string): string {
  if (value == null || value.trim() === '') return ''
  const n = Number(value)
  if (!Number.isFinite(n)) return value
  const abs = formatNumber(Math.abs(n))
  return n > 0 ? `+${abs}` : n < 0 ? `−${abs}` : abs
}

/** Una cifra de la fila: etiqueta, valor ya formateado y si es la diferencia (se pinta en tono de peligro). */
export interface AttentionFigure {
  label: string
  value: string
  emphasis?: boolean
}

/** Texto de una fila: título (`<b>`), línea de dónde/qué, cifras y desde cuándo; `reviewLabel` = nombre accesible de "Revisar". */
export interface AttentionRowText {
  title: string
  detail: string
  figures: AttentionFigure[]
  since: string
  reviewLabel: string
}

/**
 * Texto de una fila según su tipo. Descuadre (INVENTORY_DISCREPANCY): "Descuadre de {sku}", producto y dónde (almacén ·
 * posición · lote, o "todas las posiciones" si es del total del producto), Kárdex vs. saldo y diferencia, y desde cuándo.
 * Un tipo desconocido muestra su código (compatibilidad hacia adelante con avisos de otros módulos).
 */
export function attentionRowText(item: AttentionItemDto, t: Translate, lang: string, now: Date = new Date()): AttentionRowText {
  const p = item.params ?? {}
  const sinceIso = item.sinceUtc ?? p.detectedAtUtc ?? null
  const when = formatEventTime(sinceIso, lang, now)
  const since = when ? t('analytics.attention.since', { when }) : ''
  const review = t('analytics.attention.review')

  if (item.code === 'INVENTORY_DISCREPANCY') {
    const base = 'analytics.attention.items.INVENTORY_DISCREPANCY'
    const sku = p.sku ?? ''
    const isTotal = (p.kind ?? '').toUpperCase() === 'PRODUCT_TOTAL' || (!p.bin && !p.warehouse)
    const where = isTotal
      ? [t(`${base}.allBins`)]
      : [p.warehouse, p.bin, p.lot ? t(`${base}.lot`, { lot: p.lot }) : null].filter((s): s is string => Boolean(s))
    return {
      title: t(isTotal ? `${base}.titleTotal` : `${base}.title`, { sku }),
      detail: [p.productName, ...where].filter(Boolean).join(' · '),
      figures: [
        { label: t(`${base}.ledger`), value: formatQty(p.ledgerQty, lang) },
        { label: t(`${base}.balance`), value: formatQty(p.balanceQty, lang) },
        { label: t(`${base}.difference`), value: formatSigned(p.difference, lang), emphasis: true },
      ].filter((f) => f.value !== ''),
      since,
      reviewLabel: t(`${base}.reviewLabel`, { sku, where: where.join(' · ') }),
    }
  }

  const title = item.code ?? ''
  return { title, detail: '', figures: [], since, reviewLabel: title ? `${review} ${title}` : review }
}

/** Nombre de un grupo para "Ver todos" cuando hay más de un tipo de aviso. */
export function attentionGroupLabel(group: AttentionGroupDto, t: Translate): string {
  return isKnownAttention(group.code) ? t(`analytics.attention.items.${group.code}.group`) : (group.code ?? '')
}
