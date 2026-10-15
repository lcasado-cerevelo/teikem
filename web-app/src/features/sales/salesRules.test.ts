import { describe, expect, it } from 'vitest'
import { draftError, draftSubtotal, effectivePrice, lineAmount, parseNumber, toRequest, type LineDraft, type OrderDraft } from './salesRules'

const line = (patch: Partial<LineDraft> = {}): LineDraft => ({ key: 1, productPublicId: 'p1', sku: 'MED', name: 'Medidor', listPrice: 12.5, quantity: '4', price: '', ...patch })
const draft = (patch: Partial<OrderDraft> = {}): OrderDraft => ({ clientPublicId: 'c1', warehousePublicId: null, requiredDate: '', notes: '', lines: [line()], ...patch })

describe('salesRules', () => {
  it('lee cantidades con coma o punto', () => {
    expect(parseNumber('2,5')).toBe(2.5)
    expect(parseNumber(' 10 ')).toBe(10)
    expect(parseNumber('')).toBeNull()
    expect(parseNumber('x')).toBeNull()
    expect(parseNumber('-1')).toBeNull()
  })

  it('el precio efectivo es el escrito o el de lista; el importe y el subtotal salen de ahí', () => {
    expect(effectivePrice(line())).toBe(12.5)
    expect(effectivePrice(line({ price: '10' }))).toBe(10)
    expect(effectivePrice(line({ listPrice: null }))).toBeNull()
    expect(lineAmount(line())).toBe(50)
    expect(lineAmount(line({ quantity: '2,5', price: '3' }))).toBe(7.5)
    expect(lineAmount(line({ quantity: '' }))).toBe(0)
    expect(draftSubtotal(draft({ lines: [line(), line({ key: 2, productPublicId: 'p2', quantity: '10', listPrice: 3 })] }))).toBe(80)
  })

  it('valida el borrador en el mismo orden que el servidor', () => {
    expect(draftError(draft())).toBeNull()
    expect(draftError(draft({ clientPublicId: null }))).toBe('client')
    expect(draftError(draft({ lines: [] }))).toBe('lines')
    expect(draftError(draft({ lines: [line({ productPublicId: null })] }))).toBe('product')
    expect(draftError(draft({ lines: [line(), line({ key: 2 })] }))).toBe('repeated')
    expect(draftError(draft({ lines: [line({ quantity: '0' })] }))).toBe('quantity')
    expect(draftError(draft({ lines: [line({ quantity: '' })] }))).toBe('quantity')
    expect(draftError(draft({ lines: [line({ quantity: '1,0001' })] }))).toBe('quantityDecimals')
    expect(draftError(draft({ lines: [line({ price: 'abc' })] }))).toBe('price')
    expect(draftError(draft({ lines: [line({ price: '1,00001' })] }))).toBe('priceDecimals')
    expect(draftError(draft({ lines: [line({ listPrice: null })] }))).toBe('noPrice')
    expect(draftError(draft({ lines: [line({ listPrice: null, price: '5' })] }))).toBeNull()
    expect(draftError(draft({ notes: 'x'.repeat(501) }))).toBe('notes')
  })

  it('el precio solo viaja si es distinto del de lista y la fecha vacía la quita', () => {
    const body = toRequest(draft({ requiredDate: '', lines: [line(), line({ key: 2, productPublicId: 'p2', quantity: '2', price: '9' }), line({ key: 3, productPublicId: 'p3', price: '12,5' })] }))
    expect(body.clearRequiredDate).toBe(true)
    expect(body.requiredDate).toBeUndefined()
    expect(body.lines.map((l) => l.unitPrice)).toEqual([undefined, 9, undefined])
    expect(body.lines.map((l) => l.quantity)).toEqual([4, 2, 4])
    expect(toRequest(draft({ requiredDate: '2026-11-01' })).requiredDate).toBe('2026-11-01')
  })
})
