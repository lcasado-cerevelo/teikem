import type { ExpectedLine, LocalDoc } from './localLookup'
import type { DraftLine } from './receiveLogic'
import { allocateLines, mergeExpected } from './receiveQueue'

const D1: LocalDoc = { kind: 'asn', purchaseOrderPublicId: null, asnId: 1, label: 'LLE-1' }
const D2: LocalDoc = { kind: 'asn', purchaseOrderPublicId: null, asnId: 2, label: 'LLE-2' }
const exp = (p: string, expectedQty: number, postedQty = 0): ExpectedLine => ({ productPublicId: p, sku: p, productName: p, expectedQty, postedQty, lotNumber: null })
const line = (p: string, receivedQty: number, extra: Partial<DraftLine> = {}): DraftLine => ({
  productPublicId: p, sku: p, productName: p, trackingTypeCode: 'NONE', receivedQty, lotNumber: null, expiryDate: null, serialNumbers: null, targetBinCode: null,
  damagedQty: 0, damageCause: null, damageNote: null, damageBinCode: null, damageDiscard: false, damageDestination: null, ...extra,
})

describe('recibir varios documentos juntos (Lote 199)', () => {
  it('mergeExpected suma lo esperado y lo ya recibido por producto', () => {
    expect(mergeExpected([[exp('a', 4, 1)], [exp('a', 3), exp('b', 2)]]).map((l) => [l.productPublicId, l.expectedQty, l.postedQty])).toEqual([['a', 7, 1], ['b', 2, 0]])
  })

  it('reparte en el orden marcado, cada documento hasta lo que le falta; lo que sobra va al último que trae el producto', () => {
    const out = allocateLines([line('a', 9)], [D1, D2], [[exp('a', 4)], [exp('a', 3)]])
    expect(out.map((s) => [s.doc.asnId, s.lines.map((l) => l.receivedQty)])).toEqual([[1, [4]], [2, [5]]])
  })

  it('lo ya recibido por partes no se vuelve a asignar', () => {
    const out = allocateLines([line('a', 5)], [D1, D2], [[exp('a', 4, 3)], [exp('a', 6)]])
    expect(out.map((s) => [s.doc.asnId, s.lines.map((l) => l.receivedQty)])).toEqual([[1, [1]], [2, [4]]])
  })

  it('un producto que solo trae el segundo documento va al segundo, y un documento sin nada no sale', () => {
    const out = allocateLines([line('b', 2)], [D1, D2], [[exp('a', 4)], [exp('b', 2)]])
    expect(out.map((s) => s.doc.asnId)).toEqual([2])
  })

  it('las series se reparten en orden y el daño cae en las primeras piezas', () => {
    const out = allocateLines([line('s', 5, { trackingTypeCode: 'SERIAL', serialNumbers: ['1', '2', '3', '4', '5'], damagedQty: 3, damageCause: 'X' })], [D1, D2], [[exp('s', 2)], [exp('s', 3)]])
    expect(out[0].lines[0]).toMatchObject({ receivedQty: 2, serialNumbers: ['1', '2'], damagedQty: 2 })
    expect(out[1].lines[0]).toMatchObject({ receivedQty: 3, serialNumbers: ['3', '4', '5'], damagedQty: 1 })
  })
})
