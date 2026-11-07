import { describe, expect, it } from 'vitest'
import { authorizerMissing, draftError, draftSubtotal, effectivePrice, EXTRA_DEFAULTS, isCreditTerm, lineAmount, parseNumber, paymentChanged, paymentError, prefillPayment, toRequest, withMethod, type LineDraft, type OrderDraft } from './salesRules'

const line = (patch: Partial<LineDraft> = {}): LineDraft => ({ key: 1, productPublicId: 'p1', sku: 'MED', name: 'Medidor', listPrice: 12.5, quantity: '4', price: '', isKit: false, components: null, ...patch })
const draft = (patch: Partial<OrderDraft> = {}): OrderDraft => ({ clientPublicId: 'c1', warehousePublicId: null, requiredDate: '', notes: '', lines: [line()], ...EXTRA_DEFAULTS, ...patch })

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

describe('kits (Lote 69)', () => {
  it('un kit se pide en piezas enteras y las piezas ajustadas deben ser mayores que cero', () => {
    expect(draftError(draft({ lines: [line({ isKit: true, quantity: '2' })] }))).toBeNull()
    expect(draftError(draft({ lines: [line({ isKit: true, quantity: '1,5' })] }))).toBe('kitWhole')
    expect(draftError(draft({ lines: [line({ isKit: true, components: [{ productPublicId: 'a', sku: 'A', name: 'A', quantity: '0' }] })] }))).toBe('kitPiece')
    expect(draftError(draft({ lines: [line({ quantity: '1,5' })] }))).toBeNull()   // un producto normal sí admite decimales
  })

  it('las piezas solo viajan si es un kit y se ajustaron', () => {
    const pieces = [{ productPublicId: 'a', sku: 'A', name: 'A', quantity: '1,5' }]
    expect(toRequest(draft({ lines: [line({ isKit: true, components: pieces })] })).lines[0].components).toEqual([{ productPublicId: 'a', quantity: 1.5 }])
    expect(toRequest(draft({ lines: [line({ isKit: true })] })).lines[0].components).toBeUndefined()
    expect(toRequest(draft({ lines: [line({ components: pieces })] })).lines[0].components).toBeUndefined()
  })
})

describe('salesRules — Lote 222 (pago, consignación, autoriza, urgente)', () => {
  it('reconoce los términos de crédito', () => {
    expect(isCreditTerm('NET30')).toBe(true)
    expect(isCreditTerm('pk_by_rep')).toBe(true)
    expect(isCreditTerm('COD')).toBe(false)
    expect(isCreditTerm('NET')).toBe(false)
    expect(isCreditTerm(null)).toBe(false)
  })

  it('prellena la forma de pago con el término del cliente', () => {
    expect(prefillPayment('CHEQUE')).toEqual({ method: 'COD_CHECK', term: '' })
    expect(prefillPayment('ach')).toEqual({ method: 'COD_ACH', term: '' })
    expect(prefillPayment('NET45')).toEqual({ method: 'CREDIT', term: 'NET45' })
    expect(prefillPayment('CONSIGNMENT')).toEqual({ method: 'CONSIGNMENT', term: '' })
    expect(prefillPayment('COD')).toEqual({ method: '', term: '' })
    expect(prefillPayment(null)).toEqual({ method: '', term: '' })
  })

  it('marca «cambió» solo cuando difiere de la del cliente', () => {
    expect(paymentChanged('CHEQUE', '', '')).toBe(false)
    expect(paymentChanged('CHEQUE', 'COD_CHECK', '')).toBe(false)
    expect(paymentChanged('CHEQUE', 'COD_CASH', '')).toBe(true)
    expect(paymentChanged('NET30', 'CREDIT', 'NET30')).toBe(false)
    expect(paymentChanged('NET30', 'CREDIT', 'NET15')).toBe(true)
    expect(paymentChanged('COD', 'COD_CASH', '')).toBe(false)
    expect(paymentChanged('COD', 'CREDIT', 'NET15')).toBe(true)
  })

  it('valida forma, término y consignación', () => {
    expect(paymentError('CREDIT', '', false)).toBe('creditNeedsTerm')
    expect(paymentError('COD_CASH', 'NET30', false)).toBe('termOnlyWithCredit')
    expect(paymentError('COD_CASH', '', true)).toBe('consignmentNeedsMethod')
    expect(paymentError('CONSIGNMENT', '', true)).toBeNull()
    expect(draftError(draft({ paymentMethod: 'CREDIT' }))).toBe('creditNeedsTerm')
    expect(draftError(draft({ authorizedByName: 'x'.repeat(151) }))).toBe('authorizedBy')
  })

  it('cambiar la forma quita el término y la consignación que ya no aplican', () => {
    const d = draft({ paymentMethod: 'CREDIT', paymentTerm: 'NET30', isConsignment: true })
    expect(withMethod(d, 'COD_CASH')).toMatchObject({ paymentMethod: 'COD_CASH', paymentTerm: '', isConsignment: false })
    expect(withMethod(d, 'CREDIT')).toMatchObject({ paymentTerm: 'NET30', isConsignment: false })
    expect(withMethod(draft({ paymentMethod: 'CONSIGNMENT', isConsignment: true }), 'CONSIGNMENT').isConsignment).toBe(true)
  })

  it('exige el nombre de quien autoriza solo si el cliente lo pide', () => {
    expect(authorizerMissing(true, '  ')).toBe(true)
    expect(authorizerMissing(true, 'Ana')).toBe(false)
    expect(authorizerMissing(false, '')).toBe(false)
  })

  it('el cuerpo lleva los datos nuevos solo si tienen valor', () => {
    expect(toRequest(draft())).toMatchObject({ isUrgent: false, deliveryMode: undefined, paymentMethod: undefined, paymentTerm: undefined, isConsignment: false, authorizedByName: undefined })
    const body = toRequest(draft({ isUrgent: true, deliveryMode: 'PICKUP', paymentMethod: 'CREDIT', paymentTerm: 'NET30', authorizedByName: ' Ana ' }))
    expect(body).toMatchObject({ isUrgent: true, deliveryMode: 'PICKUP', paymentMethod: 'CREDIT', paymentTerm: 'NET30', authorizedByName: 'Ana' })
  })
})
