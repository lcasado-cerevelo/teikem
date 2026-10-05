import { checkLotBin, inExitOrder, nextStockOption, pickedQty, binScanOutcome, buildCollectAndPackBody, buildPickLine, newPickLineDraft, pickQtyState, sameOwner, uniqueBinCodes } from './dispatchLogic'
import type { StockOption } from './dispatchLogic'

const PRODUCT = { publicId: 'p1', sku: 'SKU-1', name: 'Producto 1' }

describe('newPickLineDraft', () => {
  it('arranca SIN cantidad (no hay "1" por omisión) y sin posición', () => {
    const draft = newPickLineDraft(PRODUCT)
    expect(draft).toMatchObject({ qtyText: '', fromBinCode: '' })
  })
})

describe('pickQtyState', () => {
  it('en blanco = missing; 0, negativa o texto = invalid; mayor que 0 = ok (coma o punto)', () => {
    expect(pickQtyState('')).toBe('missing')
    expect(pickQtyState('   ')).toBe('missing')
    expect(pickQtyState('0')).toBe('invalid')
    expect(pickQtyState('-2')).toBe('invalid')
    expect(pickQtyState('abc')).toBe('invalid')
    expect(pickQtyState('3')).toBe('ok')
    expect(pickQtyState('2,5')).toBe('ok')
  })
})

// decisión del dueño 5 (docs/decisiones-del-dueno-2026-10-03.md): la cantidad va primero
describe('binScanOutcome', () => {
  it('con cantidad escrita: la lectura agrega la línea con esa cantidad y la posición recortada', () => {
    const draft = { ...newPickLineDraft(PRODUCT), qtyText: '12' }
    expect(binScanOutcome(draft, ' B-1 ')).toEqual({
      kind: 'add',
      line: { productPublicId: 'p1', sku: 'SKU-1', productName: 'Producto 1', quantity: 12, fromBinCode: 'B-1' },
    })
  })

  it('sin cantidad: no agrega (needQty), nunca una línea con 1', () => {
    expect(binScanOutcome(newPickLineDraft(PRODUCT), 'B-1')).toEqual({ kind: 'needQty' })
    expect(binScanOutcome({ ...newPickLineDraft(PRODUCT), qtyText: '  ' }, 'B-1')).toEqual({ kind: 'needQty' })
  })

  it('cantidad 0 o inválida: no agrega (invalidQty)', () => {
    expect(binScanOutcome({ ...newPickLineDraft(PRODUCT), qtyText: '0' }, 'B-1')).toEqual({ kind: 'invalidQty' })
    expect(binScanOutcome({ ...newPickLineDraft(PRODUCT), qtyText: 'x' }, 'B-1')).toEqual({ kind: 'invalidQty' })
    expect(binScanOutcome({ ...newPickLineDraft(PRODUCT), qtyText: '-1' }, 'B-1')).toEqual({ kind: 'invalidQty' })
  })

  it('una lectura vacía no hace nada', () => {
    expect(binScanOutcome({ ...newPickLineDraft(PRODUCT), qtyText: '3' }, '   ')).toEqual({ kind: 'noBin' })
  })
})

describe('buildPickLine', () => {
  it('arma la línea con la cantidad numérica y el código de posición recortado', () => {
    const line = buildPickLine({ ...newPickLineDraft(PRODUCT), qtyText: '4', fromBinCode: ' B-9 ' })
    expect(line).toEqual({ productPublicId: 'p1', sku: 'SKU-1', productName: 'Producto 1', quantity: 4, fromBinCode: 'B-9' })
  })
})

describe('uniqueBinCodes', () => {
  it('devuelve los códigos sin repetir, en el orden en que aparecen', () => {
    const lines = [
      buildPickLine({ ...newPickLineDraft(PRODUCT), fromBinCode: 'B-1' }),
      buildPickLine({ ...newPickLineDraft(PRODUCT), fromBinCode: 'B-2' }),
      buildPickLine({ ...newPickLineDraft(PRODUCT), fromBinCode: 'B-1' }),
    ]
    expect(uniqueBinCodes(lines)).toEqual(['B-1', 'B-2'])
  })
})

describe('buildCollectAndPackBody', () => {
  it('arma el cuerpo de collect-and-pack con las líneas ya resueltas y el consignatario elegido', () => {
    const line = { ...buildPickLine({ ...newPickLineDraft(PRODUCT), qtyText: '2', fromBinCode: 'B-3' }), fromBinId: 3 }
    const body = buildCollectAndPackBody('wh-1', 'client-1', 'loc-1', 2, [line])
    expect(body).toEqual({
      warehousePublicId: 'wh-1',
      lines: [{ productPublicId: 'p1', quantity: 2, binId: 3 }],
      pack: {
        order: {
          clientPublicId: 'client-1',
          consigneeLocationPublicId: 'loc-1',
          serviceType: 'STANDARD',
          packages: [{ packageType: 'BOX', pieces: 2 }],
          confirmNow: true,
        },
      },
    })
  })
})

describe('sameOwner', () => {
  it('inventario propio (null) solo con propio; un cliente 3PL solo con sus productos', () => {
    expect(sameOwner(null, null)).toBe(true)
    expect(sameOwner(undefined, null)).toBe(true)
    expect(sameOwner('c1', 'c1')).toBe(true)
    expect(sameOwner(null, 'c1')).toBe(false)
    expect(sameOwner('c1', null)).toBe(false)
    expect(sameOwner('c1', 'c2')).toBe(false)
  })
})

describe('posición sugerida (orden de salida del servidor)', () => {
  const opt = (binCode: string, available: number, rank: number, over: Partial<StockOption> = {}): StockOption => ({
    binCode,
    zoneTypeCode: 'RESERVE',
    lotNumber: null,
    expiryDate: null,
    available,
    rank,
    ...over,
  })

  it('la app no reordena: sigue el rank del servidor y solo descarta lo que no tiene disponible', () => {
    const rows = [opt('Z-9', 5, 3), opt('B-1', 5, 1, { expiryDate: '2027-03-01' }), opt('E-1', 0, 2), opt('A-1', 5, 2)]
    expect(inExitOrder(rows).map((o) => o.binCode)).toEqual(['B-1', 'A-1', 'Z-9'])
  })

  it('lo que sigue descuenta lo ya sacado de este despacho y devuelve lo que queda de esa existencia', () => {
    const rows = [opt('C-1', 10, 1, { expiryDate: '2026-12-31', lotNumber: 'L1' }), opt('B-1', 20, 2, { expiryDate: '2027-03-01', lotNumber: 'L2' })]
    expect(nextStockOption(rows, 0)).toMatchObject({ binCode: 'C-1', available: 10 })
    expect(nextStockOption(rows, 4)).toMatchObject({ binCode: 'C-1', available: 6 })
    expect(nextStockOption(rows, 10)).toMatchObject({ binCode: 'B-1', available: 20 })
    expect(nextStockOption(rows, 31)).toBeNull()
    expect(nextStockOption([], 0)).toBeNull()
  })

  it('con lote la posición es la de la sugerencia: otra posición o más de lo que hay se rechazan; sin sugerencia no se exige nada', () => {
    const expected = opt('C-1', 10, 1, { lotNumber: 'L1', expiryDate: '2026-12-31' })
    expect(checkLotBin(expected, 'c-1', 10)).toEqual({ kind: 'ok' })
    expect(checkLotBin(expected, 'B-1', 1)).toEqual({ kind: 'otherBin', expected })
    expect(checkLotBin(expected, 'C-1', 11)).toEqual({ kind: 'tooMuch', expected })
    expect(checkLotBin(null, 'B-1', 99)).toEqual({ kind: 'ok' })
  })

  it('suma lo sacado de un producto en las líneas del despacho', () => {
    const lines = [
      { productPublicId: 'p1', sku: 'A', productName: 'A', quantity: 2, fromBinCode: 'X' },
      { productPublicId: 'p2', sku: 'B', productName: 'B', quantity: 5, fromBinCode: 'X' },
      { productPublicId: 'p1', sku: 'A', productName: 'A', quantity: 3, fromBinCode: 'Y' },
    ]
    expect(pickedQty(lines, 'p1')).toBe(5)
    expect(pickedQty(lines, 'p9')).toBe(0)
  })
})
