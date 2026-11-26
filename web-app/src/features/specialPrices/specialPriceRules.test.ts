// Lote 247 — reglas puras de «Precios especiales»: validación del borrador (alta y cambio), cuerpos que se envían y origen del precio que se cobra hoy.
import { describe, expect, it } from 'vitest'
import { chargedSourceKey, draftOfRow, emptySpecialDraft, expiresSoon, specialDraftError, toSpecialPatch, toSpecialRequest } from './specialPriceRules'

const TODAY = '2026-11-26'
const draft = (extra: object = {}) => ({ ...emptySpecialDraft(TODAY), productPublicId: 'p1', price: '8.5', ...extra })

describe('specialPriceRules', () => {
  it('valida el alta: producto, precio mayor que cero, desde, rango y «hasta» pasado', () => {
    expect(specialDraftError(draft({ productPublicId: null }), TODAY, true)).toBe('productRequired')
    expect(specialDraftError(draft({ price: '' }), TODAY, true)).toBe('priceInvalid')
    expect(specialDraftError(draft({ price: '0' }), TODAY, true)).toBe('priceInvalid')
    expect(specialDraftError(draft({ price: '1.23456' }), TODAY, true)).toBe('priceInvalid')
    expect(specialDraftError(draft({ validFrom: '' }), TODAY, true)).toBe('fromRequired')
    expect(specialDraftError(draft({ validTo: '2026-11-01' }), TODAY, true)).toBe('range')
    expect(specialDraftError(draft({ validFrom: '2026-10-01', validTo: '2026-11-01' }), TODAY, true)).toBe('toPast')
    expect(specialDraftError(draft({ validTo: '2026-12-31' }), TODAY, true)).toBeNull()
    expect(specialDraftError(draft({ price: '8,5' }), TODAY, true)).toBeNull() // coma decimal
  })

  it('al cambiar se permite un «hasta» ya pasado (para cerrar un precio que corre) y no pide producto', () => {
    expect(specialDraftError(draft({ productPublicId: null, validFrom: '2026-10-01', validTo: '2026-11-01' }), TODAY, false)).toBeNull()
    expect(specialDraftError(draft({ validTo: '2026-11-01' }), TODAY, false)).toBe('range')
  })

  it('cuerpo del alta y del cambio: sin «hasta» se manda clearValidTo', () => {
    expect(toSpecialRequest(draft({ price: '8,5', validTo: '2026-12-31' }))).toEqual({ productPublicId: 'p1', price: 8.5, validFrom: TODAY, validTo: '2026-12-31' })
    expect(toSpecialPatch(draft())).toEqual({ price: 8.5, validFrom: TODAY, validTo: null, clearValidTo: true })
    expect(toSpecialPatch(draft({ validTo: '2026-12-31' }))).toEqual({ price: 8.5, validFrom: TODAY, validTo: '2026-12-31', clearValidTo: false })
  })

  it('el borrador de un renglón copia precio y fechas', () => {
    expect(draftOfRow({ productPublicId: 'p1', price: 9, validFrom: '2026-11-01', validTo: null })).toEqual({ productPublicId: 'p1', price: '9', validFrom: '2026-11-01', validTo: '' })
  })

  it('origen del precio que se cobra hoy: conocido o NONE; «por vencer» solo si está vigente', () => {
    expect(chargedSourceKey('CHAIN_LIST')).toBe('CHAIN_LIST')
    expect(chargedSourceKey('OFFER')).toBe('OFFER')
    expect(chargedSourceKey('???')).toBe('NONE')
    expect(chargedSourceKey(null)).toBe('NONE')
    expect(expiresSoon({ state: 'ACTIVE', daysToExpire: 3 })).toBe(true)
    expect(expiresSoon({ state: 'ACTIVE', daysToExpire: 30 })).toBe(false)
    expect(expiresSoon({ state: 'ACTIVE', daysToExpire: null })).toBe(false)
    expect(expiresSoon({ state: 'EXPIRED', daysToExpire: 0 })).toBe(false)
  })
})
