import { describe, expect, it } from 'vitest'
import type { AsnDto } from './api'
import { asnCandidate, groupRequest, isPoReceivable, isReceivable, MAX_GROUP_DOCUMENTS, poCandidate, selectionBlock } from './receiptGroupView'

const asn = (id: number, over: Partial<AsnDto> = {}) => asnCandidate({ id, number: `LLE-${id}`, warehousePublicId: 'w1', clientPublicId: 'c1', statusCode: 'EXPECTED', isActive: true, ...over } as AsnDto)
const po = (id: string, warehouse = 'w1') => poCandidate({ publicId: id, number: `PO-${id}`, warehousePublicId: warehouse, warehouseCode: 'ALM' })

describe('Recibir juntos (Lotes 183 y siguientes)', () => {
  it('solo se recibe un aviso esperando, vigente y sin recibo; una orden enviada o parcial', () => {
    const base = { id: 1, statusCode: 'EXPECTED', isActive: true } as AsnDto
    expect(isReceivable(base)).toBe(true)
    expect(isReceivable({ ...base, statusCode: 'RECEIVED' })).toBe(false)
    expect(isReceivable({ ...base, receiptPublicId: 'r1' })).toBe(false)
    expect(isReceivable({ ...base, isActive: false })).toBe(false)
    expect([isPoReceivable('SENT'), isPoReceivable('PARTIAL'), isPoReceivable('DRAFT'), isPoReceivable('RECEIVED')]).toEqual([true, true, false, false])
  })

  it('el primero elegido fija el dueño y el almacén; una orden de compra es de lo propio', () => {
    const first = asn(1)
    expect(selectionBlock(asn(2), [first])).toBeNull()
    expect(selectionBlock(asn(3, { clientPublicId: 'c2' }), [first])).toBe('otherOwner')
    expect(selectionBlock(po('a'), [first])).toBe('otherOwner')   // lo de un cliente con lo propio
    expect(selectionBlock(po('b'), [po('a')])).toBeNull()
    expect(selectionBlock(asn(4, { clientPublicId: null }), [po('a')])).toBeNull()   // un aviso nacido de una orden es propio
    expect(selectionBlock(asn(5, { warehousePublicId: 'w2' }), [first])).toBe('otherWarehouse')
    expect(selectionBlock(po('c', 'w2'), [po('a')])).toBe('otherWarehouse')
    expect(selectionBlock(first, [first])).toBeNull()
  })

  it('tope de documentos', () => {
    expect(selectionBlock(asn(99), Array.from({ length: MAX_GROUP_DOCUMENTS }, (_, i) => asn(i + 1)))).toBe('full')
  })

  it('el cuerpo manda aviso u orden según el documento y los textos sin vacíos', () => {
    expect(groupRequest([asn(1), po('abc')], { carrier: ' Transportes Uno ', reference: '', note: ' ' })).toEqual({
      documents: [{ asnId: 1 }, { purchaseOrderPublicId: 'abc' }], carrier: 'Transportes Uno', reference: undefined, note: undefined,
    })
  })
})
