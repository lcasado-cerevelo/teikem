import { describe, expect, it } from 'vitest'
import { allowedReceiptSources, initialReceiptSource } from './receiptSources'

describe('receiptSources', () => {
  it('libre (o sin dato) ofrece todo', () => {
    expect(allowedReceiptSources('FREE', true)).toEqual(['BLIND', 'RETURN', 'ASN', 'PO'])
    expect(allowedReceiptSources(undefined, false)).toEqual(['BLIND', 'RETURN', 'ASN'])
  })
  it('si se exige documento ya no se ofrece a ciegas ni devolución', () => {
    expect(allowedReceiptSources('ANY_DOCUMENT', true)).toEqual(['ASN', 'PO'])
    expect(allowedReceiptSources('ANY_DOCUMENT', false)).toEqual(['ASN'])
    expect(allowedReceiptSources('ASN', true)).toEqual(['ASN'])
    expect(allowedReceiptSources('PURCHASE_ORDER', true)).toEqual(['PO'])
  })
  it('abre con el aviso preajustado o con el primer origen permitido', () => {
    expect(initialReceiptSource(['BLIND', 'ASN'], true)).toBe('ASN')
    expect(initialReceiptSource(['BLIND', 'ASN'], false)).toBe('BLIND')
    expect(initialReceiptSource(['PO'], false)).toBe('PO')
  })
})
