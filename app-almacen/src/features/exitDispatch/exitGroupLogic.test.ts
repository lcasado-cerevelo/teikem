import { buildGroupBody, groupBlock, groupNumber, groupReady, MAX_GROUP } from './exitGroupLogic'
import type { SalesTask } from '../salesDispatch/salesDispatchLogic'

const n = (id: number, client = 'ASICS') => ({ id, publicId: `sn-${id}`, number: `SAL-0000${id}`, clientName: client })
const task = (lineId: number, binId: number, qty: number): SalesTask => ({
  key: `${lineId}|${binId}|`, lineId, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', binId, binCode: `P-0${binId}`, lotId: null, lotNumber: null, qty, kitSku: null, perKit: null,
})

describe('recolectar juntos (aparato)', () => {
  it('una maestra es de un solo cliente y tiene tope', () => {
    expect(groupBlock(n(2), [n(1)])).toBeNull()
    expect(groupBlock(n(3, 'NIKE'), [n(1)])).toBe('otherClient')
    expect(groupBlock(n(1), [n(1)])).toBeNull()
    expect(groupBlock(n(99), Array.from({ length: MAX_GROUP }, (_, i) => n(i)))).toBe('max')
  })

  it('el cuerpo lleva las líneas de cada aviso con su posición, y deja fuera al que no se recogió nada', () => {
    const tasks = [task(10, 1, 4), task(20, 2, 3), task(30, 3, 5)]
    const picks = [
      { taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 },
      { taskKey: tasks[1].key, binId: 9, binCode: 'P-09', qty: 2 },
    ]
    const lineNotice = new Map([[10, 1], [20, 2], [30, 3]])
    const body = buildGroupBody([n(1), n(2), n(3)], tasks, picks, lineNotice, 'wh')
    expect(body.documents).toEqual([
      { kind: 'EXIT_NOTICE', publicId: 'sn-1', lines: [{ lineId: 10, quantity: 4, binId: 1, lotId: undefined }] },
      { kind: 'EXIT_NOTICE', publicId: 'sn-2', lines: [{ lineId: 20, quantity: 2, binId: 9, lotId: undefined }] },
    ])
    expect(groupReady(body)).toBe(true)
    expect(groupReady(buildGroupBody([n(1), n(2)], tasks, [picks[0]], lineNotice, 'wh'))).toBe(false)
  })

  it('lee el número de la maestra de la respuesta', () => {
    expect(groupNumber({ number: 'EMP-00009' })).toBe('EMP-00009')
    expect(groupNumber({})).toBeNull()
    expect(groupNumber(null)).toBeNull()
  })
})
