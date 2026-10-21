import { describe, expect, it } from 'vitest'
import { groupRequest, isGroupable, movementsByDocument, selectionBlock, MAX_GROUP_DOCUMENTS } from './pickGroupView'

const doc = (n: number, client = 'ASICS', kind = 'EXIT_NOTICE') => ({ kind, publicId: `id-${n}`, clientName: client, reserved: 3 })

describe('pickGroupView', () => {
  it('solo avisos de salida y órdenes de venta con algo reservado se pueden juntar', () => {
    expect(isGroupable(doc(1))).toBe(true)
    expect(isGroupable({ ...doc(1), reserved: 0 })).toBe(false)
    expect(isGroupable({ ...doc(1, 'X', 'ORDER') })).toBe(false)
  })
  it('una maestra es de un solo cliente: tras elegir uno, los de otro cliente se bloquean', () => {
    const chosen = [doc(1)]
    expect(selectionBlock(doc(2), chosen)).toBeNull()
    expect(selectionBlock(doc(3, 'NIKE'), chosen)).toBe('otherClient')
    expect(selectionBlock(doc(1), chosen)).toBeNull()   // el ya elegido se puede quitar
    expect(selectionBlock(doc(9, 'NIKE'), [])).toBeNull()
  })
  it('tope de documentos', () => {
    const many = Array.from({ length: MAX_GROUP_DOCUMENTS }, (_, i) => doc(i))
    expect(selectionBlock(doc(99), many)).toBe('max')
  })
  it('arma el cuerpo con el orden elegido y sin líneas (todo lo reservado)', () => {
    expect(groupRequest([doc(2), doc(1, 'ASICS', 'SALES_ORDER')], '  Camión 5 ')).toEqual({
      documents: [{ kind: 'EXIT_NOTICE', publicId: 'id-2' }, { kind: 'SALES_ORDER', publicId: 'id-1' }],
      note: 'Camión 5',
    })
    expect(groupRequest([doc(1)]).note).toBeNull()
  })
  it('agrupa los movimientos por documento con su total', () => {
    const m = (n: string, q: number, bin: string) => ({ documentKind: 'EXIT_NOTICE', documentNumber: n, quantity: q, binCode: bin })
    const g = movementsByDocument([m('SAL-1', 2, 'A'), m('SAL-2', 1, 'A'), m('SAL-1', 3, 'B')])
    expect(g.map((x) => [x.document, x.total, x.rows.length])).toEqual([['SAL-1', 5, 2], ['SAL-2', 1, 1]])
  })
})
