import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests } from '../../kernel/db/database'
import { addLocalPickLine, discardLocalPick, getOpenPick, removeLocalPickLine, restoreLocalPick, startLocalPick } from './localPick'
import type { PickLine } from './dispatchLogic'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

const LINE: PickLine = { productPublicId: 'p1', sku: 'SKU-1', productName: 'Uno', quantity: 3, fromBinCode: 'B-5' }

describe('despacho local', () => {
  it('empieza vacío', () => {
    expect(getOpenPick()).toBeNull()
  })

  it('arranca un despacho, agrega y quita líneas', () => {
    const id = startLocalPick('wh-1', { publicId: 'c1', name: 'Cliente 1' })
    addLocalPickLine(id, LINE)
    let open = getOpenPick()
    expect(open?.clientName).toBe('Cliente 1')
    expect(open?.lineRows).toHaveLength(1)

    removeLocalPickLine(open!.lineRows[0].id)
    open = getOpenPick()
    expect(open?.lineRows).toHaveLength(0)
  })

  it('un despacho a la vez: empezar otro mientras uno sigue abierto lanza', () => {
    startLocalPick('wh-1', null)
    expect(() => startLocalPick('wh-1', null)).toThrow(/ya hay un despacho en curso/i)
  })

  it('discardLocalPick libera el aparato', () => {
    const id = startLocalPick('wh-1', null)
    addLocalPickLine(id, LINE)
    discardLocalPick()
    expect(getOpenPick()).toBeNull()
    expect(() => startLocalPick('wh-1', null)).not.toThrow()
  })

  it('restoreLocalPick vuelve a abrir el despacho tal como estaba (despacho manual rechazado, 2026-10-11); no pisa uno abierto', () => {
    const id = startLocalPick('wh-1', { publicId: 'c1', name: 'Cliente 1' })
    addLocalPickLine(id, LINE)
    addLocalPickLine(id, { ...LINE, quantity: 2, fromBinCode: 'B-6' })
    const snapshot = getOpenPick()!
    discardLocalPick()
    restoreLocalPick(snapshot)
    const back = getOpenPick()
    expect([back?.warehousePublicId, back?.clientPublicId, back?.clientName]).toEqual(['wh-1', 'c1', 'Cliente 1'])
    expect(back?.lineRows.map((l) => [l.sku, l.quantity, l.fromBinCode])).toEqual([['SKU-1', 3, 'B-5'], ['SKU-1', 2, 'B-6']])
    restoreLocalPick(snapshot)
    expect(getOpenPick()?.lineRows).toHaveLength(2)
  })
})
