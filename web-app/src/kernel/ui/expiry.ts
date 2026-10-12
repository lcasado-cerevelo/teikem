// Lógica pura del chip de vencimiento (sin React): qué tono y qué texto lleva un documento según lo que calcula el servidor.
import type { ChipTone } from './Chip'

/** Estados de vencimiento que manda el API (`FleetRules.ExpiryState`). */
export const ExpiryStates = { Ok: 'OK', Expiring: 'EXPIRING', Expired: 'EXPIRED', NoExpiry: 'NO_EXPIRY' } as const

export interface ExpiryChipInfo {
  tone: ChipTone
  /** Clave de texto (`ui.expiry.*`) y sus parámetros. */
  key: string
  params?: { days: number }
  /** Clave del texto largo (title) del chip. */
  titleKey: string
}

/**
 * Qué se pinta para un documento (lógica pura, sin React):
 * reemplazado → neutro «Reemplazada»; sin fecha → neutro «Sin vencimiento»; vencido → rojo «Vencida hace N días»;
 * por vencer → ámbar «Vence hoy» / «Vence en N días»; vigente → verde «Vigente».
 */
export function expiryChipInfo(state: string | null | undefined, days: number | null | undefined, superseded = false): ExpiryChipInfo {
  if (superseded) return { tone: 'cap', key: 'ui.expiry.superseded', titleKey: 'ui.expiry.supersededTitle' }
  const s = (state ?? '').toUpperCase()
  const n = typeof days === 'number' ? days : null
  if (s === ExpiryStates.Expired) {
    const ago = Math.abs(n ?? 0)
    return { tone: 'fail', key: ago === 1 ? 'ui.expiry.expiredOne' : 'ui.expiry.expired', params: { days: ago }, titleKey: 'ui.expiry.expiredTitle' }
  }
  if (s === ExpiryStates.Expiring) {
    if (n === 0) return { tone: 'warn', key: 'ui.expiry.today', titleKey: 'ui.expiry.expiringTitle' }
    return { tone: 'warn', key: n === 1 ? 'ui.expiry.inOne' : 'ui.expiry.in', params: { days: n ?? 0 }, titleKey: 'ui.expiry.expiringTitle' }
  }
  if (s === ExpiryStates.Ok) return { tone: 'deliv', key: 'ui.expiry.ok', titleKey: 'ui.expiry.okTitle' }
  return { tone: 'cap', key: 'ui.expiry.none', titleKey: 'ui.expiry.noneTitle' }
}
