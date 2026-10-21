import { describe, expect, it } from 'vitest'
import type { AsnDto } from './api'
import { asnOwnerKey, groupRequest, isReceivable, MAX_GROUP_DOCUMENTS, selectionBlock } from './receiptGroupView'

const asn = (id: number, over: Partial<AsnDto> = {}): AsnDto => ({ id, number: `LLE-${id}`, warehousePublicId: 'w1', clientPublicId: 'c1', statusCode: 'EXPECTED', isActive: true, ...over }) as AsnDto

describe('Recibir juntos (Lote 183)', () => {
  it('solo se recibe un aviso esperando, vigente y sin recibo', () => {
    expect(isReceivable(asn(1))).toBe(true)
    expect(isReceivable(asn(1, { statusCode: 'RECEIVED' }))).toBe(false)
    expect(isReceivable(asn(1, { receiptPublicId: 'r1' }))).toBe(false)
    expect(isReceivable(asn(1, { isActive: false }))).toBe(false)
  })

  it('el primero elegido fija el dueño y el almacén; lo de otro dueño o almacén se bloquea', () => {
    const first = asn(1)
    expect(selectionBlock(asn(2), [])).toBeNull()
    expect(selectionBlock(asn(2), [first])).toBeNull()
    expect(selectionBlock(asn(3, { clientPublicId: 'c2' }), [first])).toBe('otherOwner')
    expect(selectionBlock(asn(4, { clientPublicId: null, purchaseOrderNumber: 'PO-1' }), [first])).toBe('otherOwner')
    expect(selectionBlock(asn(5, { warehousePublicId: 'w2' }), [first])).toBe('otherWarehouse')
    expect(selectionBlock(first, [first])).toBeNull()   // el ya elegido se puede quitar
    expect(asnOwnerKey(asn(6, { clientPublicId: null }))).toBe('OWN')
  })

  it('tope de documentos', () => {
    const many = Array.from({ length: MAX_GROUP_DOCUMENTS }, (_, i) => asn(i + 1))
    expect(selectionBlock(asn(99), many)).toBe('full')
  })

  it('el cuerpo lleva los avisos y los textos compartidos sin vacíos', () => {
    expect(groupRequest([asn(1), asn(2)], { carrier: ' Transportes Uno ', reference: '', note: ' ' })).toEqual({ documents: [{ asnId: 1 }, { asnId: 2 }], carrier: 'Transportes Uno', reference: undefined, note: undefined })
  })
})
