// Lote 247 (Lote C) — reglas puras de «Precios especiales»: validación del borrador, cuerpo que se envía, cuerpo de cambio y el texto del origen del precio
// que se cobra hoy. Espejo corto de lo que repite el servidor (400 al crear con «hasta» pasado, precio > 0); aquí solo para avisar antes de enviar.
import { parsePrice } from '../warehouse/priceListRules'
import type { SpecialPricePatchRequest, SpecialPriceRequest, SpecialPriceRowDto } from './specialPricesApi'

export interface SpecialPriceDraft {
  productPublicId: string | null
  price: string
  validFrom: string
  /** '' = sin fecha final. */
  validTo: string
}

export const emptySpecialDraft = (today: string): SpecialPriceDraft => ({ productPublicId: null, price: '', validFrom: today, validTo: '' })

export type SpecialDraftProblem = 'productRequired' | 'priceInvalid' | 'fromRequired' | 'range' | 'toPast'

/** `creating` = alta: un «hasta» ya pasado no se permite (al editar sí, para cerrar un precio que ya corre). */
export function specialDraftError(d: SpecialPriceDraft, today: string, creating: boolean): SpecialDraftProblem | null {
  if (creating && !d.productPublicId) return 'productRequired'
  const price = parsePrice(d.price)
  if (price == null || price <= 0) return 'priceInvalid'
  if (!d.validFrom) return 'fromRequired'
  if (d.validTo && d.validTo < d.validFrom) return 'range'
  if (creating && d.validTo && d.validTo < today) return 'toPast'
  return null
}

export function toSpecialRequest(d: SpecialPriceDraft): SpecialPriceRequest {
  return { productPublicId: d.productPublicId, price: parsePrice(d.price), validFrom: d.validFrom, validTo: d.validTo || null }
}

/** Cambio de un renglón: sin «hasta» se manda `clearValidTo` para quitar el que hubiera. */
export function toSpecialPatch(d: SpecialPriceDraft): SpecialPricePatchRequest {
  return { price: parsePrice(d.price), validFrom: d.validFrom, validTo: d.validTo || null, clearValidTo: d.validTo ? false : true }
}

export function draftOfRow(r: SpecialPriceRowDto): SpecialPriceDraft {
  return { productPublicId: r.productPublicId ?? null, price: String(r.price ?? ''), validFrom: r.validFrom ?? '', validTo: r.validTo ?? '' }
}

export type ChargedSource = 'CLIENT_LIST' | 'CHAIN_LIST' | 'OFFER' | 'GENERAL_LIST' | 'PRODUCT' | 'NONE'

/** Clave de texto del origen que se cobra hoy (desconocido o vacío = NONE). */
export function chargedSourceKey(source: string | null | undefined): ChargedSource {
  switch (source) {
    case 'CLIENT_LIST':
    case 'CHAIN_LIST':
    case 'OFFER':
    case 'GENERAL_LIST':
    case 'PRODUCT':
      return source
    default:
      return 'NONE'
  }
}

/** Texto de «Hasta» según los días que faltan: null = sin fecha final. */
export function expiresSoon(r: Pick<SpecialPriceRowDto, 'daysToExpire' | 'state'>, within = 14): boolean {
  return r.state === 'ACTIVE' && r.daysToExpire != null && r.daysToExpire <= within
}
