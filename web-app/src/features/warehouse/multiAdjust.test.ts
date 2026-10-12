import { describe, expect, it } from 'vitest'
import {
  addPosition,
  applyBlocker,
  blockFromStock,
  buildRequest,
  errorKey,
  generalErrors,
  isDirty,
  parseCounted,
  refreshBlocks,
  rowDifference,
  rowErrors,
  summarize,
  type ProductBlock,
} from './multiAdjust'

const stock = (sku: string, positions: [number, string, number][], tracking = 'NONE') => ({
  productPublicId: `p-${sku}`,
  sku,
  name: `Producto ${sku}`,
  trackingTypeCode: tracking,
  isActive: true,
  positions: positions.map(([binId, binCode, qty]) => ({ binId, binCode, zoneCode: 'PCK', lotId: null, lotNumber: null, qtyOnHand: qty, qtyReserved: 0 })),
})

const blocks = (): ProductBlock[] => [
  blockFromStock(stock('P1', [[1, 'A-01', 10], [2, 'A-02', 4]])),
  blockFromStock(stock('P2', [[1, 'A-01', 7]])),
]

describe('parseCounted', () => {
  it('vacío no se toca; acepta coma; rechaza negativos, letras y más de 3 decimales', () => {
    expect(parseCounted('')).toBeNull()
    expect(parseCounted('  ')).toBeNull()
    expect(parseCounted('12')).toBe(12)
    expect(parseCounted('2,5')).toBe(2.5)
    expect(parseCounted('0')).toBe(0)
    for (const bad of ['-1', 'abc', '1.2345', '1..2', '1e3']) expect(parseCounted(bad)).toBe('invalid')
  })
})

describe('diferencias y resumen', () => {
  it('la diferencia es contado − sistema; vacío o mal escrito no tiene', () => {
    expect(rowDifference({ systemQty: 10, counted: '7' })).toBe(-3)
    expect(rowDifference({ systemQty: 4, counted: '9' })).toBe(5)
    expect(rowDifference({ systemQty: 10, counted: '10' })).toBe(0)
    expect(rowDifference({ systemQty: 10, counted: '' })).toBeNull()
    expect(rowDifference({ systemQty: 10, counted: 'x' })).toBeNull()
    expect(rowDifference({ systemQty: 0.1, counted: '0.3' })).toBe(0.2)
  })

  it('solo cuentan las líneas con diferencia; productos distintos; suben y bajan', () => {
    const b = blocks()
    b[0].rows[0].counted = '7'   // −3
    b[0].rows[1].counted = '9'   // +5
    b[1].rows[0].counted = '7'   // igual
    expect(summarize(b)).toEqual({ lines: 2, products: 1, up: 5, down: 3, invalid: 0 })
    b[1].rows[0].counted = '2'   // −5
    expect(summarize(b)).toEqual({ lines: 3, products: 2, up: 5, down: 8, invalid: 0 })
    b[1].rows[0].counted = 'x'
    expect(summarize(b).invalid).toBe(1)
  })
})

describe('aplicar', () => {
  it('no se puede sin almacén, con errores, sin diferencias o sin nota', () => {
    const b = blocks()
    expect(applyBlocker(b, 'nota', null)).toBe('warehouse')
    expect(applyBlocker(b, 'nota', 'w')).toBe('nothing')
    b[0].rows[0].counted = '7'
    expect(applyBlocker(b, '  ', 'w')).toBe('notes')
    expect(applyBlocker(b, 'nota', 'w')).toBeNull()
    b[0].rows[1].counted = '-2'
    expect(applyBlocker(b, 'nota', 'w')).toBe('invalid')
  })

  it('la petición lleva todas las posiciones con «Hay ahora» (también las iguales) y omite las vacías y las de serie', () => {
    const b = [...blocks(), blockFromStock(stock('PS', [[3, 'S-01', 2]], 'SERIAL'))]
    b[0].rows[0].counted = '7'
    b[1].rows[0].counted = '7'
    b[2].rows[0].counted = '1'
    const { body, index } = buildRequest('w1', '  Semanal ', b)
    expect(body.warehousePublicId).toBe('w1')
    expect(body.notes).toBe('Semanal')
    expect(body.lines).toEqual([
      { productPublicId: 'p-P1', binId: 1, lotId: null, systemQty: 10, countedQty: 7 },
      { productPublicId: 'p-P2', binId: 1, lotId: null, systemQty: 7, countedQty: 7 },
    ])
    expect(index).toEqual([
      { productPublicId: 'p-P1', key: '1:0' },
      { productPublicId: 'p-P2', key: '1:0' },
    ])
  })

  it('los errores lines[i] del API se pintan en su posición; los demás van arriba', () => {
    const b = blocks()
    b[0].rows[0].counted = '7'
    b[1].rows[0].counted = '1'
    const { index } = buildRequest('w1', 'n', b)
    const errors = { 'lines[1]': ['cambió'], 'lines[0].countedQty': ['mal'], notes: ['Escriba una nota'] }
    expect(rowErrors(errors, index)).toEqual({ [errorKey('p-P2', '1:0')]: 'cambió', [errorKey('p-P1', '1:0')]: 'mal' })
    expect(generalErrors(errors)).toEqual(['Escriba una nota'])
    expect(rowErrors(undefined, index)).toEqual({})
  })
})

describe('actualizar y agregar posiciones', () => {
  it('refreshBlocks pone al día «En sistema», conserva lo escrito y las agregadas, y suma las nuevas', () => {
    let b = blocks()
    b[0].rows[0].counted = '7'
    b[0] = addPosition(b[0], { binId: 9, binCode: 'Z-09', zoneCode: 'PCK' })
    b[0].rows[2].counted = '3'
    const fresh = [stock('P1', [[1, 'A-01', 8], [5, 'A-05', 2]])]   // A-01 bajó a 8, A-02 se vació, apareció A-05
    b = refreshBlocks(b, fresh)
    const rows = b[0].rows
    expect(rows.find((r) => r.binCode === 'A-01')).toMatchObject({ systemQty: 8, counted: '7' })
    expect(rows.find((r) => r.binCode === 'A-02')).toMatchObject({ systemQty: 0 })
    expect(rows.find((r) => r.binCode === 'Z-09')).toMatchObject({ systemQty: 0, counted: '3', added: true })
    expect(rows.find((r) => r.binCode === 'A-05')).toMatchObject({ systemQty: 2, counted: '' })
    expect(b[1].rows[0].systemQty).toBe(7)   // el producto que no vino queda igual
  })

  it('addPosition no duplica una posición', () => {
    const b = blocks()[0]
    expect(addPosition(b, { binId: 1, binCode: 'A-01', zoneCode: 'PCK' })).toBe(b)
    const c = addPosition(b, { binId: 7, binCode: 'A-07', zoneCode: 'PCK' })
    expect(c.rows).toHaveLength(3)
    expect(c.rows[2]).toMatchObject({ binId: 7, systemQty: 0, added: true, counted: '' })
  })

  it('isDirty avisa si hay algo escrito', () => {
    const b = blocks()
    expect(isDirty(b, '')).toBe(false)
    expect(isDirty(b, 'nota')).toBe(true)
    b[0].rows[0].counted = '1'
    expect(isDirty(b, '')).toBe(true)
  })
})
