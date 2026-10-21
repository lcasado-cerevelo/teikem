import { parseReceiptRequirement, receiveOptions } from './receiptRequirement'

describe('receiptRequirement', () => {
  it('lo desconocido o vacío es libre', () => {
    expect(parseReceiptRequirement(null)).toBe('FREE')
    expect(parseReceiptRequirement('xx')).toBe('FREE')
    expect(parseReceiptRequirement(' asn ')).toBe('ASN')
  })

  it('libre ofrece todo; con documento obligatorio desaparece el ciego', () => {
    expect(receiveOptions('FREE')).toEqual({ blind: true, asn: true, purchaseOrder: true })
    expect(receiveOptions('ANY_DOCUMENT')).toEqual({ blind: false, asn: true, purchaseOrder: true })
  })

  it('aviso u orden específicos ofrecen solo ese', () => {
    expect(receiveOptions('ASN')).toEqual({ blind: false, asn: true, purchaseOrder: false })
    expect(receiveOptions('PURCHASE_ORDER')).toEqual({ blind: false, asn: false, purchaseOrder: true })
  })
})
