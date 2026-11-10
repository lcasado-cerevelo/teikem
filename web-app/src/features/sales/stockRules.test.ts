// Lote 235 — reglas puras del disponible, el almacén por omisión, el backorder marcado (no bloquea) y la nota de otros almacenes.
import { describe, expect, it } from 'vitest'
import { anyBackorder, availableElsewhere, availableIn, backorderQty, isBuiltOnOrderKit, kitStock, lineStock, mainWarehouseId, orderKitPieces, orderWarehouseId, shortHeadKey, showWarehouseField, warehouseLabel, type StockInput } from './stockRules'

const ONE: StockInput = { warehouses: [{ publicId: 'w-1', code: 'ALM-01', name: 'Bayamón', isMain: true }], products: [{ productPublicId: 'p-1', stock: [{ warehousePublicId: 'w-1', available: 5 }] }] }
const THREE: StockInput = {
  warehouses: [
    { publicId: 'w-1', code: 'ALM-01', name: 'Bayamón', isMain: false },
    { publicId: 'w-2', code: 'ALM-02', name: 'Ponce', isMain: true },
    { publicId: 'w-3', code: 'ALM-03', name: 'Mayagüez', isMain: false },
  ],
  products: [
    { productPublicId: 'p-1', stock: [{ warehousePublicId: 'w-1', available: 12 }, { warehousePublicId: 'w-2', available: 5 }, { warehousePublicId: 'w-3', available: 3 }] },
    { productPublicId: 'p-2', stock: [{ warehousePublicId: 'w-3', available: 8 }] },
  ],
}

describe('almacén de la orden', () => {
  it('con un solo almacén no se pregunta y se usa ese', () => {
    expect(showWarehouseField(ONE)).toBe(false)
    expect(orderWarehouseId(ONE, null)).toBe('w-1')
  })
  it('con varios se muestra, ya escogido con el principal; lo que la persona escoge gana', () => {
    expect(showWarehouseField(THREE)).toBe(true)
    expect(mainWarehouseId(THREE)).toBe('w-2')
    expect(orderWarehouseId(THREE, null)).toBe('w-2')
    expect(orderWarehouseId(THREE, 'w-3')).toBe('w-3')
    expect(warehouseLabel(THREE, 'w-2')).toBe('Ponce')
  })
  it('sin datos no hay almacén ni campo', () => {
    expect(showWarehouseField(undefined)).toBe(false)
    expect(mainWarehouseId(null)).toBeNull()
    expect(orderWarehouseId(undefined, null)).toBeNull()
    // sin ninguno marcado (no debería pasar): el primero
    expect(mainWarehouseId({ warehouses: [{ publicId: 'a' }, { publicId: 'b' }] })).toBe('a')
  })
})

describe('disponible y backorder', () => {
  it('lo disponible es lo del almacén de la orden; lo que no viene es 0', () => {
    expect(availableIn(THREE, 'p-1', 'w-2')).toBe(5)
    expect(availableIn(THREE, 'p-2', 'w-2')).toBe(0)
    expect(availableIn(THREE, 'p-9', 'w-2')).toBe(0)
    expect(availableIn(THREE, 'p-1', null)).toBe(0)
  })
  it('pedir de más NO se bloquea: se cuenta lo que queda en backorder', () => {
    expect(backorderQty('5', 5)).toBe(0)
    expect(backorderQty('8', 5)).toBe(3)
    expect(backorderQty(2.5, 2)).toBe(0.5)
    expect(backorderQty('4', 0)).toBe(4)
    expect(backorderQty('', 5)).toBe(0)
    expect(backorderQty('abc', 0)).toBe(0)
    expect(shortHeadKey(0)).toBe('shortNone')
    expect(shortHeadKey(1)).toBe('shortOne')
    expect(shortHeadKey(5)).toBe('shortMany')
  })
  it('el renglón: disponible, backorder y otros almacenes; sin datos no dice nada', () => {
    expect(lineStock(THREE, 'p-1', 'w-2', '8')).toEqual({
      available: 5,
      backorder: 3,
      elsewhere: [{ warehousePublicId: 'w-1', name: 'Bayamón', available: 12 }, { warehousePublicId: 'w-3', name: 'Mayagüez', available: 3 }],
    })
    expect(lineStock(THREE, 'p-2', 'w-2', '')).toEqual({ available: 0, backorder: 0, elsewhere: [{ warehousePublicId: 'w-3', name: 'Mayagüez', available: 8 }] })
    expect(lineStock(undefined, 'p-1', 'w-2', '8')).toBeNull()
    expect(lineStock(THREE, null, 'w-2', '8')).toBeNull()
    expect(lineStock(THREE, 'p-1', null, '8')).toBeNull()
  })
  it('la nota de otros almacenes solo existe si hay otros con disponible', () => {
    expect(availableElsewhere(ONE, 'p-1', 'w-1')).toEqual([])
    expect(availableElsewhere(THREE, 'p-2', 'w-3')).toEqual([])
    expect(availableElsewhere(THREE, 'p-1', 'w-1').map((e) => e.name)).toEqual(['Ponce', 'Mayagüez'])
  })
  it('alguna línea en backorder', () => {
    expect(anyBackorder(THREE, 'w-2', [{ productPublicId: 'p-1', quantity: '5' }])).toBe(false)
    expect(anyBackorder(THREE, 'w-2', [{ productPublicId: 'p-1', quantity: '5' }, { productPublicId: 'p-2', quantity: '1' }])).toBe(true)
    expect(anyBackorder(undefined, 'w-2', [{ productPublicId: 'p-1', quantity: '99' }])).toBe(false)
  })
})

// Lote 236 — el disponible de un kit que se arma al pedir, desglosado por pieza
describe('desglose del disponible de un kit', () => {
  const KITS: StockInput = {
    warehouses: [
      { publicId: 'w-1', code: 'ALM-01', name: 'Bayamón', isMain: true },
      { publicId: 'w-2', code: 'ALM-02', name: 'Ponce', isMain: false },
    ],
    products: [
      { productPublicId: 'strips', stock: [{ warehousePublicId: 'w-1', available: 14022 }, { warehousePublicId: 'w-2', available: 13 }] },
      { productPublicId: 'monitor', stock: [{ warehousePublicId: 'w-1', available: 5000 }, { warehousePublicId: 'w-2', available: 1 }] },
      { productPublicId: 'other', stock: [{ warehousePublicId: 'w-1', available: 40 }] },
      // el servidor también manda el número del kit; la pantalla lo cuenta con las piezas (da lo mismo)
      { productPublicId: 'kit', stock: [{ warehousePublicId: 'w-1', available: 2337 }, { warehousePublicId: 'w-2', available: 1 }] },
      { productPublicId: 'assembled', stock: [{ warehousePublicId: 'w-1', available: 7 }] },
    ],
    kits: [
      { kitPublicId: 'kit', components: [{ productPublicId: 'strips', sku: '52800', name: 'Tirillas', qtyPerKit: 6 }, { productPublicId: 'monitor', sku: '070120', name: 'Monitor', qtyPerKit: 1 }] },
      { kitPublicId: 'empty', components: [] },
    ],
  }

  it('por pieza: cuánto lleva el kit, cuánto hay, cuántos kits da y cuál limita', () => {
    expect(kitStock(KITS, 'kit', 'w-1')).toEqual({
      available: 2337,
      pieces: [
        { productPublicId: 'strips', sku: '52800', name: 'Tirillas', qtyPerKit: 6, available: 14022, kits: 2337, limiting: true },
        { productPublicId: 'monitor', sku: '070120', name: 'Monitor', qtyPerKit: 1, available: 5000, kits: 5000, limiting: false },
      ],
    })
    // en el otro almacén limita el monitor (13 ÷ 6 = 2, pero solo hay 1)
    const ponce = kitStock(KITS, 'kit', 'w-2')!
    expect(ponce.available).toBe(1)
    expect(ponce.pieces.map((p) => p.limiting)).toEqual([false, true])
  })

  it('una pieza sin disponible deja el kit en 0 y es la que limita; un kit sin piezas da 0', () => {
    const none = kitStock({ ...KITS, products: [{ productPublicId: 'strips', stock: [{ warehousePublicId: 'w-1', available: 60 }] }] }, 'kit', 'w-1')!
    expect(none.available).toBe(0)
    expect(none.pieces.map((p) => [p.sku, p.kits, p.limiting])).toEqual([['52800', 10, false], ['070120', 0, true]])
    expect(kitStock(KITS, 'empty', 'w-1')).toEqual({ available: 0, pieces: [] })
  })

  it('si empatan, limitan todas las empatadas; las cantidades con decimales no pierden un kit por redondeo', () => {
    const tie: StockInput = { ...KITS, products: [{ productPublicId: 'strips', stock: [{ warehousePublicId: 'w-1', available: 12 }] }, { productPublicId: 'monitor', stock: [{ warehousePublicId: 'w-1', available: 2 }] }] }
    expect(kitStock(tie, 'kit', 'w-1')!.pieces.map((p) => p.limiting)).toEqual([true, true])
    const dec: StockInput = { ...KITS, products: [{ productPublicId: 'strips', stock: [{ warehousePublicId: 'w-1', available: 0.3 }] }], kits: [{ kitPublicId: 'kit', components: [{ productPublicId: 'strips', sku: '52800', name: 'Tirillas', qtyPerKit: 0.1 }] }] }
    expect(kitStock(dec, 'kit', 'w-1')!.available).toBe(3)
  })

  it('un kit armado o un producto normal no se desglosan: su existencia, como siempre', () => {
    expect(kitStock(KITS, 'assembled', 'w-1')).toBeNull()
    expect(isBuiltOnOrderKit(KITS, 'assembled')).toBe(false)
    expect(isBuiltOnOrderKit(KITS, 'kit')).toBe(true)
    expect(lineStock(KITS, 'assembled', 'w-1', '9')).toEqual({ available: 7, backorder: 2, elsewhere: [] })
    expect(kitStock(null, 'kit', 'w-1')).toBeNull()
    expect(kitStock(KITS, 'kit', null)).toBeNull()
  })

  it('el renglón del kit: disponible, backorder y otros almacenes salen de las piezas', () => {
    const line = lineStock(KITS, 'kit', 'w-1', '2400')!
    expect(line.available).toBe(2337)
    expect(line.backorder).toBe(63)
    expect(line.elsewhere).toEqual([{ warehousePublicId: 'w-2', name: 'Ponce', available: 1 }])
    expect(line.kit?.pieces).toHaveLength(2)
  })

  it('con una pieza SUSTITUIDA en la orden, el desglose y la cuenta usan la pieza real', () => {
    const pieces = [
      { productPublicId: 'other', sku: '52900', name: 'Tirillas alternas', quantity: '6', substitutesProductPublicId: 'strips' },
      { productPublicId: 'monitor', sku: '070120', name: 'Monitor', quantity: '1' },
    ]
    const k = kitStock(KITS, 'kit', 'w-1', pieces)!
    expect(k.available).toBe(6)   // 40 ÷ 6
    expect(k.pieces[0]).toMatchObject({ sku: '52900', available: 40, kits: 6, limiting: true })
    expect(lineStock(KITS, 'kit', 'w-1', '10', pieces)).toMatchObject({ available: 6, backorder: 4, elsewhere: [] })
    expect(anyBackorder(KITS, 'w-1', [{ productPublicId: 'kit', quantity: '10', components: pieces }])).toBe(true)
    expect(anyBackorder(KITS, 'w-1', [{ productPublicId: 'kit', quantity: '10' }])).toBe(false)
    // una cantidad por kit que no es un número deja el kit en 0
    expect(kitStock(KITS, 'kit', 'w-1', [{ productPublicId: 'strips', sku: '52800', name: 'Tirillas', quantity: '' }])!.available).toBe(0)
  })

  it('las piezas de la orden: las cambiadas en pantalla, o las guardadas, o ninguna (las del catálogo)', () => {
    const edited = [{ productPublicId: 'a', sku: 'A', name: 'A', quantity: '2' }]
    const saved = { components: [{ productPublicId: 'b', sku: 'B', name: 'B', qtyPerKit: 3 }] }
    expect(orderKitPieces(edited, saved)).toBe(edited)
    expect(orderKitPieces(null, saved)).toEqual([{ productPublicId: 'b', sku: 'B', name: 'B', quantity: 3 }])
    expect(orderKitPieces(null, undefined)).toBeNull()
    expect(orderKitPieces([], { components: [] })).toBeNull()
  })
})
