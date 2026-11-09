// Lote 221 (P1) — reglas puras de «Precios y ofertas»: estado de la lista, validación del borrador, cuerpo que se envía y precio de un renglón.
// Espejo de `PriceListRules` del servidor (que repite todo); aquí solo para avisar antes de enviar. Las ofertas (lote 229) están en `offerRules.ts`.
import type { components } from '../../kernel/api/schema'
import type { ChipTone } from '../../kernel/ui'

type S = components['schemas']
export type PriceListRequest = S['PriceListRequest']

export type PriceListState = 'DRAFT' | 'SCHEDULED' | 'ACTIVE' | 'EXPIRED'

/** Color del chip del estado calculado de la lista. */
export function stateTone(state: string | null | undefined): ChipTone {
  switch (state) {
    case 'ACTIVE':
      return 'deliv'
    case 'SCHEDULED':
      return 'warn'
    case 'EXPIRED':
      return 'fail'
    default:
      return 'cap'
  }
}

/** Clave de texto del estado (desconocido = borrador). */
export function stateKey(state: string | null | undefined): PriceListState {
  return state === 'ACTIVE' || state === 'SCHEDULED' || state === 'EXPIRED' ? state : 'DRAFT'
}

export interface PriceListDraft {
  name: string
  validFrom: string
  /** '' = sin fecha final. */
  validTo: string
  appliesToAll: boolean
  notes: string
}

export const EMPTY_LIST_DRAFT = (today: string): PriceListDraft => ({ name: '', validFrom: today, validTo: '', appliesToAll: true, notes: '' })

export type ListDraftProblem = 'nameRequired' | 'fromRequired' | 'range'

export function listDraftError(d: PriceListDraft): ListDraftProblem | null {
  if (!d.name.trim()) return 'nameRequired'
  if (!d.validFrom) return 'fromRequired'
  if (d.validTo && d.validTo < d.validFrom) return 'range'
  return null
}

/** Cuerpo de crear/editar; sin fecha final se manda `clearValidTo` para borrar la que hubiera. */
export function toListRequest(d: PriceListDraft): PriceListRequest {
  return {
    name: d.name.trim(),
    validFrom: d.validFrom,
    validTo: d.validTo || null,
    clearValidTo: d.validTo ? false : true,
    appliesToAll: d.appliesToAll,
    notes: d.notes.trim() || null,
  }
}

/** Precio escrito → número ≥ 0 con hasta 4 decimales (acepta coma decimal); vacío o inválido → null. */
export function parsePrice(text: string): number | null {
  const s = text.trim().replace(',', '.')
  if (!/^\d+(\.\d{1,4})?$/.test(s)) return null
  return Number(s)
}

/** Una lista publicada se puede cambiar, pero para publicar hace falta al menos un renglón y (clientes o «todos»). */
export function publishBlocker(itemCount: number, appliesToAll: boolean, clientCount: number): 'noItems' | 'noClients' | null {
  if (itemCount < 1) return 'noItems'
  if (!appliesToAll && clientCount < 1) return 'noClients'
  return null
}
