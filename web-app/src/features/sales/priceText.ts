// Textos de precios que el servidor genera en español (avisos de precio, nombre de la lista dedicada, vigencia de una oferta), armados aquí en el idioma activo con
// los datos estructurados que el servidor manda junto al texto (códigos, nombres, precios y fechas ISO). Si falta algún dato se devuelve el texto del servidor
// (`message`, `name`, `validityText`), que sigue en español como respaldo. Sin React: reciben `t` y el formateador de `useFormat()`.
import { useCallback } from 'react'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import type { components } from '../../kernel/api/schema'

type TFn = ReturnType<typeof useT>
export interface PriceFormat {
  money: (n: number) => string
  date: (v: string) => string
}

export type SalesPriceNotice = components['schemas']['SalesPriceNoticeDto']
export type PriceListWarning = components['schemas']['PriceListWarningDto']

/** La vigencia en palabras: «del 01/11/2026 al 30/11/2026» / «desde el 01/11/2026, sin fecha de fin» (o su versión en inglés). */
export function validityRange(t: TFn, f: PriceFormat, from: string | null | undefined, to: string | null | undefined): string | null {
  if (!from) return null
  return to ? t('pricing.range.between', { from: f.date(from), to: f.date(to) }) : t('pricing.range.open', { from: f.date(from) })
}

/** La vigencia de una oferta: con las fechas del servidor; sin ellas, `validityText` tal cual. */
export function offerValidityText(t: TFn, f: PriceFormat, o: { validFrom?: string | null; validTo?: string | null; hasEndDate?: boolean; validityText?: string | null }): string | null {
  const range = validityRange(t, f, o.validFrom, o.hasEndDate === false ? null : o.validTo)
  return range ?? o.validityText ?? null
}

/** El nombre de una lista dedicada («Precio especial — dueño (vigencia)») en el idioma activo; sin dueño o fechas, el nombre del servidor. */
export function dedicatedListName(
  t: TFn,
  f: PriceFormat,
  list: { name?: string | null; specialOwnerKind?: string | null; specialOwnerName?: string | null; validFrom?: string | null; validTo?: string | null },
): string {
  if (list.specialOwnerKind !== 'CLIENT' && list.specialOwnerKind !== 'CHAIN') return list.name ?? ''
  const range = validityRange(t, f, list.validFrom, list.validTo)
  if (!list.specialOwnerName || !range) return list.name ?? ''
  return t('pricing.dedicatedName', { owner: list.specialOwnerName, range })
}

/** El nombre de la lista de un aviso de precio (dedicada con dueño y fechas → armado aquí; si no, el del servidor). */
function noticeListName(t: TFn, f: PriceFormat, n: SalesPriceNotice): string | null {
  if (n.priceListIsSpecial && n.priceListOwnerName) {
    const range = validityRange(t, f, n.priceListValidFrom, n.priceListValidTo)
    if (range) return t('pricing.dedicatedName', { owner: n.priceListOwnerName, range })
  }
  return n.priceListName ?? null
}

/** El texto de un aviso de precio (dos ofertas, oferta más cara que la general, precio especial más caro que la general) en el idioma activo. */
export function priceNoticeText(t: TFn, f: PriceFormat, n: SalesPriceNotice): string {
  const fallback = n.message ?? ''
  const sku = n.sku
  const price = n.price
  const other = n.otherPrice
  if (!sku || price == null) return fallback
  switch (n.code) {
    case 'TWO_OFFERS':
      if (!n.offerName || !n.otherOfferName || other == null) return fallback
      return t('pricing.notice.TWO_OFFERS', { sku, offer: n.offerName, price: f.money(price), other: n.otherOfferName, otherPrice: f.money(other) })
    case 'OFFER_ABOVE_GENERAL':
      if (!n.offerName || other == null) return fallback
      return t('pricing.notice.OFFER_ABOVE_GENERAL', { sku, offer: n.offerName, price: f.money(price), otherPrice: f.money(other) })
    case 'SPECIAL_ABOVE_GENERAL': {
      const list = noticeListName(t, f, n)
      if (!list || other == null) return fallback
      return t('pricing.notice.SPECIAL_ABOVE_GENERAL', { sku, list, price: f.money(price), otherPrice: f.money(other) })
    }
    default:
      return fallback
  }
}

/** El aviso de una lista de precios (`PriceListWarningDto`: precio especial más caro que la lista general) en el idioma activo. */
export function priceListWarningText(t: TFn, f: PriceFormat, w: Partial<PriceListWarning>): string {
  const fallback = w.message ?? ''
  if (w.code !== 'SPECIAL_ABOVE_GENERAL' || !w.sku || w.price == null || w.generalListPrice == null) return fallback
  return t('pricing.warning.SPECIAL_ABOVE_GENERAL', { sku: w.sku, price: f.money(w.price), general: f.money(w.generalListPrice) })
}

/** Hook: `text(notice)` arma el aviso en el idioma activo (se vuelve a pintar al cambiar el idioma o el formato). */
export function usePriceNoticeText(): (n: SalesPriceNotice) => string {
  const t = useT()
  const f = useFormat()
  return useCallback((n: SalesPriceNotice) => priceNoticeText(t, f, n), [t, f])
}
