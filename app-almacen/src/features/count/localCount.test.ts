import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests } from '../../kernel/db/database'
import type { ExpectedLine, ProductCountLine } from './countLogic'
import {
  addExtraLine,
  addOpenCountLine,
  addProductExtraRow,
  captureExpectedLine,
  discardLocalCount,
  getCapturedLines,
  getOpenCount,
  getProductCountRows,
  removeLocalCountLine,
  setProductRowQty,
  startLocalOpenCount,
  startLocalCount,
  startLocalProductCount,
  toCapturedEntries,
  toProductEntries,
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
    expect(open).toMatchObject({ id, countId: 100, warehousePublicId: 'wh-1', mode: 'BIN', binId: 5, binCode: 'B-5', product: null, isBlind: false })
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
      { lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', countedQty: 3, isExtra: false, binId: 5 },
      { lineId: null, productPublicId: 'p9', sku: 'Z', productName: 'Extra', countedQty: 1, isExtra: true, binId: 5 },
    ])
  })
})

const PRODUCT = { publicId: 'p1', sku: 'A', name: 'Uno', trackingTypeCode: 'LOT' as const }
const PLINES: ProductCountLine[] = [
  { lineId: 1, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 4, binId: 10, binCode: 'A-01', lotId: 3, lotNumber: 'L-3', binIsProvisional: false },
  { lineId: 2, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 1, binId: 11, binCode: 'B-02', lotId: 4, lotNumber: 'L-4', binIsProvisional: false },
  { lineId: 3, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: null, binId: 12, binCode: 'C-03', lotId: 3, lotNumber: 'L-3', binIsProvisional: true },
]

describe('conteo local por producto', () => {
  it('guarda el conteo sin posición y todas sus filas en blanco; se lee de vuelta tal cual (retomar tras cerrar la app)', () => {
    const id = startLocalProductCount('wh-1', PRODUCT, { countId: 300, isBlind: true }, PLINES)
    expect(getOpenCount()).toEqual({
      id,
      countId: 300,
      warehousePublicId: 'wh-1',
      mode: 'PRODUCT',
      binId: null,
      binCode: null,
      product: PRODUCT,
      isBlind: true,
    })
    const rows = getProductCountRows(id)
    expect(rows.map((r) => [r.lineId, r.binId, r.binCode, r.lotNumber, r.countedQty, r.isProvisionalBin])).toEqual([
      [1, 10, 'A-01', 'L-3', null, false],
      [2, 11, 'B-02', 'L-4', null, false],
      [3, 12, 'C-03', 'L-3', null, true],
    ])
    // las filas en blanco no son "capturadas" del conteo por posición
    expect(getCapturedLines(id)).toEqual([])
  })

  it('un conteo a la vez: no se abre uno por producto con otro en curso', () => {
    startLocalCount('wh-1', BIN, STARTED)
    expect(() => startLocalProductCount('wh-1', PRODUCT, { countId: 300, isBlind: true }, PLINES)).toThrow(/ya hay un conteo en curso/i)
  })

  it('lo escrito se guarda a cada cambio; volver a dejarlo en blanco lo borra', () => {
    const id = startLocalProductCount('wh-1', PRODUCT, { countId: 300, isBlind: true }, PLINES)
    const [first, second] = getProductCountRows(id)
    setProductRowQty(first.id, 4)
    setProductRowQty(second.id, 2)
    setProductRowQty(second.id, null)
    expect(getProductCountRows(id).map((r) => r.countedQty)).toEqual([4, null, null])
  })

  it('"Otra posición" agrega una fila nueva al final con su lote, y lo que viaja trae los blancos como 0', () => {
    const id = startLocalProductCount('wh-1', PRODUCT, { countId: 300, isBlind: false }, PLINES)
    const rowId = addProductExtraRow(id, { publicId: 'p1', sku: 'A', name: 'Uno' }, { id: 99, code: 'Z-09', isProvisional: true }, { number: 'L-9', expiryDate: '2027-01-31' })
    setProductRowQty(rowId, 6)
    setProductRowQty(getProductCountRows(id)[0].id, 3)
    const rows = getProductCountRows(id)
    expect(rows[3]).toMatchObject({ lineId: null, binId: 99, binCode: 'Z-09', lotNumber: 'L-9', lotExpiryDate: '2027-01-31', isExtra: true, isProvisionalBin: true, countedQty: 6 })
    expect(toProductEntries(rows).map((e) => [e.lineId, e.binId, e.countedQty, e.isExtra])).toEqual([
      [1, 10, 3, false],
      [2, 11, 0, false],
      [3, 12, 0, false],
      [null, 99, 6, true],
    ])
    removeLocalCountLine(rowId)
    expect(getProductCountRows(id)).toHaveLength(3)
  })

  it('discardLocalCount libera el aparato también en el conteo por producto', () => {
    startLocalProductCount('wh-1', PRODUCT, { countId: 300, isBlind: true }, PLINES)
    discardLocalCount()
    expect(getOpenCount()).toBeNull()
  })
})

describe('conteo local abierto (Lote 24)', () => {
  it('se abre sin producto ni posición y cada línea guarda la suya, con su lote y su cantidad', () => {
    const id = startLocalOpenCount('wh-1', { countId: 31, isBlind: true })
    expect(getOpenCount()).toMatchObject({ id, countId: 31, mode: 'OPEN', binId: null, binCode: null, product: null, isBlind: true })
    addOpenCountLine(id, { publicId: 'p1', sku: 'S1', name: 'Uno' }, { id: 10, code: 'A-01', isProvisional: false }, null, 4)
    addOpenCountLine(id, { publicId: 'p2', sku: 'S2', name: 'Dos' }, { id: 11, code: 'B-02', isProvisional: true }, { id: 5, number: 'L-5', expiryDate: '2027-01-31' }, 2)
    const rows = getProductCountRows(id)
    expect(rows.map((r) => [r.sku, r.binId, r.binCode, r.lotNumber, r.countedQty, r.isExtra, r.isProvisionalBin])).toEqual([
      ['S1', 10, 'A-01', null, 4, true, false],
      ['S2', 11, 'B-02', 'L-5', 2, true, true],
    ])
    expect(toProductEntries(rows)).toEqual([
      expect.objectContaining({ lineId: null, productPublicId: 'p1', binId: 10, countedQty: 4, isExtra: true }),
      expect.objectContaining({ lineId: null, productPublicId: 'p2', binId: 11, countedQty: 2, lotNumber: 'L-5', lotExpiryDate: '2027-01-31' }),
    ])
  })

  it('un solo conteo abierto por aparato', () => {
    startLocalOpenCount('wh-1', { countId: 32, isBlind: false })
    expect(() => startLocalOpenCount('wh-1', { countId: 33, isBlind: false })).toThrow('Ya hay un conteo en curso')
  })
})
