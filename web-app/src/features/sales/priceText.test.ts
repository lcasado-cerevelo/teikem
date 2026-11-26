// Textos de precios armados en el idioma activo con los datos estructurados del servidor (aviso de precio, nombre de la lista dedicada, vigencia de oferta);
// sin datos, el texto del servidor (español) como respaldo.
import { beforeEach, describe, expect, it } from 'vitest'
import { formatDate, formatMoney } from '../../kernel/format'
import { setLang, t } from '../../kernel/i18n'
import { dedicatedListName, offerValidityText, priceListWarningText, priceNoticeText, type SalesPriceNotice } from './priceText'

const f = { money: (n: number) => formatMoney(n), date: (v: string) => formatDate(v) }
const SPECIAL: SalesPriceNotice = {
  code: 'SPECIAL_ABOVE_GENERAL', sku: 'MED', productPublicId: 'p', price: 15, otherPrice: 14, priceListIsSpecial: true, priceListOwnerName: 'Farmacia Central',
  priceListValidFrom: '2026-10-26', priceListValidTo: null, priceListName: 'Precio especial — Farmacia Central (desde el 26/10/2026, sin fecha de fin)',
  message: 'texto del servidor',
}

describe('priceText', () => {
  beforeEach(() => setLang('es'))

  it('aviso SPECIAL_ABOVE_GENERAL en español y en inglés, con el nombre de la lista dedicada armado aquí', () => {
    expect(priceNoticeText(t, f, SPECIAL)).toBe(
      'El precio especial «Precio especial — Farmacia Central (desde el 10/26/2026, sin fecha de fin)» pone MED a $15.00, más caro que la lista general ($14.00): se aplica el de la lista general. Corrija el precio especial.',
    )
    setLang('en')
    const en = priceNoticeText(t, f, SPECIAL)
    expect(en).toBe('Special price “Special price — Farmacia Central (from 10/26/2026, no end date)” puts MED at $15.00, higher than the general list ($14.00): the general list price applies. Please fix the special price.')
    expect(en).not.toMatch(/Precio|desde|sin fecha/)
  })

  it('con rango de fechas, y los otros avisos', () => {
    setLang('en')
    expect(priceNoticeText(t, f, { ...SPECIAL, priceListValidTo: '2026-12-31' })).toContain('(from 10/26/2026 to 12/31/2026)')
    expect(priceNoticeText(t, f, { code: 'OFFER_ABOVE_GENERAL', sku: 'X', productPublicId: 'p', offerName: 'Verano', price: 4, otherPrice: 3.5 })).toBe(
      'Offer “Verano” puts X at $4.00, higher than the general list ($3.50): the lower price applies.',
    )
    expect(priceNoticeText(t, f, { code: 'TWO_OFFERS', sku: 'X', productPublicId: 'p', offerName: 'Navidad', price: 5.5, otherOfferName: 'Verano', otherPrice: 6 })).toBe(
      'Product X is in two offers at once: “Navidad” at $5.50 and “Verano” at $6.00. The cheaper one (“Navidad”) applies.',
    )
  })

  it('sin datos estructurados cae al texto del servidor', () => {
    setLang('en')
    expect(priceNoticeText(t, f, { code: 'SPECIAL_ABOVE_GENERAL', message: 'texto del servidor', productPublicId: 'p' })).toBe('texto del servidor')
    expect(priceNoticeText(t, f, { code: 'OTRO', message: 'texto del servidor', sku: 'X', price: 1, productPublicId: 'p' })).toBe('texto del servidor')
    expect(priceNoticeText(t, f, { ...SPECIAL, priceListIsSpecial: false, priceListOwnerName: null, priceListName: 'Lista X' })).toContain('“Lista X”')
  })

  it('aviso de la lista de precios', () => {
    const w = { code: 'SPECIAL_ABOVE_GENERAL', message: 'texto del servidor', sku: 'MED-2', price: 11, generalListPrice: 10 }
    expect(priceListWarningText(t, f, w)).toBe('Precio especial más caro que la lista general: MED-2 a $11.00 y en la lista general a $10.00. Se cobra el más bajo; corrija el precio especial.')
    setLang('en')
    expect(priceListWarningText(t, f, w)).toBe('Special price higher than the general list: MED-2 at $11.00 and in the general list at $10.00. The lower price is charged; please fix the special price.')
    expect(priceListWarningText(t, f, { ...w, generalListPrice: null })).toBe('texto del servidor')
  })

  it('nombre de la lista dedicada y vigencia de la oferta', () => {
    const list = { name: 'Precio especial — Walgreens (del 01/12/2026 al 31/12/2026)', specialOwnerKind: 'CHAIN', specialOwnerName: 'Walgreens', validFrom: '2026-12-01', validTo: '2026-12-31' }
    expect(dedicatedListName(t, f, list)).toBe('Precio especial — Walgreens (del 12/01/2026 al 12/31/2026)')
    setLang('en')
    expect(dedicatedListName(t, f, list)).toBe('Special price — Walgreens (from 12/01/2026 to 12/31/2026)')
    expect(dedicatedListName(t, f, { ...list, specialOwnerKind: null, name: 'Base 2026' })).toBe('Base 2026')
    expect(dedicatedListName(t, f, { ...list, specialOwnerName: null })).toBe(list.name)
    expect(offerValidityText(t, f, { validFrom: '2026-11-01', validTo: null, hasEndDate: false, validityText: 'desde el 01/11/2026, sin fecha de fin' })).toBe('from 11/01/2026, no end date')
    expect(offerValidityText(t, f, { validFrom: '2026-11-01', validTo: '2026-12-31', hasEndDate: true })).toBe('from 11/01/2026 to 12/31/2026')
    expect(offerValidityText(t, f, { validityText: 'del servidor' })).toBe('del servidor')
  })
})
