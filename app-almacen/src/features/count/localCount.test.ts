import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests } from '../../kernel/db/database'
import type { ExpectedLine } from './countLogic'
import {
  addExtraLine,
  captureExpectedLine,
  discardLocalCount,
  getCapturedLines,
  getOpenCount,
  removeLocalCountLine,
  startLocalCount,
  toCapturedEntries,
} from './localCount'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

const BIN = { id: 5, code: 'B-5' }
const STARTED = { countId: 100, isBlind: false }

const LINE: ExpectedLine = { lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 3 }

describe('conteo local', () => {
  it('empieza vacío', () => {
    expect(getOpenCount()).toBeNull()
  })

  it('arranca un conteo y se puede leer de vuelta', () => {
    const id = startLocalCount('wh-1', BIN, STARTED)
    const open = getOpenCount()
    expect(open).toMatchObject({ id, countId: 100, warehousePublicId: 'wh-1', binId: 5, binCode: 'B-5', isBlind: false })
  })

  it('un conteo a la vez: empezar otro mientras uno sigue abierto lanza', () => {
    startLocalCount('wh-1', BIN, STARTED)
    expect(() => startLocalCount('wh-1', BIN, STARTED)).toThrow(/ya hay un conteo en curso/i)
  })

  it('discardLocalCount libera el aparato', () => {
    const id = startLocalCount('wh-1', BIN, STARTED)
    captureExpectedLine(id, LINE, 3)
    discardLocalCount()
    expect(getOpenCount()).toBeNull()
    expect(() => startLocalCount('wh-1', BIN, STARTED)).not.toThrow()
  })

  it('captureExpectedLine inserta y luego reemplaza (upsert) la misma línea', () => {
    const id = startLocalCount('wh-1', BIN, STARTED)
    captureExpectedLine(id, LINE, 2)
    captureExpectedLine(id, LINE, 5)
    const rows = getCapturedLines(id)
    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject({ lineId: 7, countedQty: 5, isExtra: false, systemQty: 3 })
  })

  it('addExtraLine agrega un producto encontrado fuera de la lista esperada', () => {
    const id = startLocalCount('wh-1', BIN, STARTED)
    addExtraLine(id, { publicId: 'p9', sku: 'Z', name: 'Extra' }, 1)
    const rows = getCapturedLines(id)
    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject({ lineId: null, productPublicId: 'p9', sku: 'Z', countedQty: 1, isExtra: true, systemQty: null })
  })

  it('removeLocalCountLine quita una línea capturada', () => {
    const id = startLocalCount('wh-1', BIN, STARTED)
    captureExpectedLine(id, LINE, 3)
    const rows = getCapturedLines(id)
    removeLocalCountLine(rows[0].id)
    expect(getCapturedLines(id)).toHaveLength(0)
  })

  it('toCapturedEntries mapea las filas al formato de CapturedEntry', () => {
    const id = startLocalCount('wh-1', BIN, STARTED)
    captureExpectedLine(id, LINE, 3)
    addExtraLine(id, { publicId: 'p9', sku: 'Z', name: 'Extra' }, 1)
    const entries = toCapturedEntries(getCapturedLines(id))
    expect(entries).toEqual([
      { lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', countedQty: 3, isExtra: false },
      { lineId: null, productPublicId: 'p9', sku: 'Z', productName: 'Extra', countedQty: 1, isExtra: true },
    ])
  })
})
