// Lote 235 — reglas puras del disponible, el almacén por omisión, el backorder marcado (no bloquea) y la nota de otros almacenes.
import { describe, expect, it } from 'vitest'
import { anyBackorder, availableElsewhere, availableIn, backorderQty, lineStock, mainWarehouseId, orderWarehouseId, shortHeadKey, showWarehouseField, warehouseLabel, type StockInput } from './stockRules'

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
