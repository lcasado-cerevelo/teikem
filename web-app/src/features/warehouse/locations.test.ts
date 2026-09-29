// Lógica pura de Ubicaciones: ocupación por zona, productos por posición, filas y filtros.
import { describe, expect, it } from 'vitest'
import type { BalanceDto, WarehouseBinDto, WarehouseZoneDto } from './api'
import {
  EMPTY_LOCATION_FILTERS,
  binState,
  buildLocationRows,
  filterLocationRows,
  productOptions,
  productsByBin,
  zoneOccupancy,
} from './locations'

const ZONES: WarehouseZoneDto[] = [
  { id: 1, code: 'A', name: 'Almacenaje', zoneTypeCode: 'STORAGE', zoneType: 'Almacenaje', isActive: true, binCount: 3 },
  { id: 2, code: 'S', name: 'Staging', zoneTypeCode: 'STAGING', zoneType: 'Staging', isActive: true, binCount: 1 },
  { id: 3, code: 'V', name: 'Vacía', zoneTypeCode: 'STORAGE', zoneType: 'Almacenaje', isActive: true, binCount: 0 },
]

const BINS: WarehouseBinDto[] = [
  { id: 10, zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', code: 'A-10', isActive: true, qtyOnHand: 40, productCount: 2 },
  { id: 11, zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', code: 'A-2', isActive: true, qtyOnHand: 0, productCount: 0 },
  { id: 12, zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', code: 'A-3', isActive: true, qtyOnHand: 10, productCount: 1 },
  { id: 20, zoneId: 2, zoneCode: 'S', zoneTypeCode: 'STAGING', code: 'S-1', isActive: true, qtyOnHand: 0, productCount: 0 },
]

const LINES: BalanceDto[] = [
  { binId: 10, productPublicId: 'p-2', sku: 'SKU-10', productName: 'Tornillo', qtyOnHand: 25 },
  { binId: 10, productPublicId: 'p-1', sku: 'SKU-2', productName: 'Tuerca', qtyOnHand: 5 },
  // otro lote del mismo producto: no se repite
  { binId: 10, productPublicId: 'p-1', sku: 'SKU-2', productName: 'Tuerca', qtyOnHand: 10 },
  { binId: 12, productPublicId: 'p-1', sku: 'SKU-2', productName: 'Tuerca', qtyOnHand: 10 },
  // sin posición o sin unidades: no cuentan
  { binId: null, productPublicId: 'p-3', sku: 'SKU-3', productName: 'Arandela', qtyOnHand: 3 },
  { binId: 11, productPublicId: 'p-3', sku: 'SKU-3', productName: 'Arandela', qtyOnHand: 0 },
]

describe('binState', () => {
  it('vacía sin unidades, ocupada con unidades', () => {
    expect(binState(0)).toBe('EMPTY')
    expect(binState(null)).toBe('EMPTY')
    expect(binState(0.5)).toBe('OCCUPIED')
  })
})

describe('productsByBin / productOptions', () => {
  it('un producto por posición aunque tenga varios lotes, en orden natural de SKU; ignora líneas sin posición o en cero', () => {
    const map = productsByBin(LINES)
    expect(map.get(10)?.map((p) => p.sku)).toEqual(['SKU-2', 'SKU-10'])
    expect(map.get(12)?.map((p) => p.publicId)).toEqual(['p-1'])
    expect(map.has(11)).toBe(false)
    expect(productOptions(map)).toEqual([
      { value: 'p-1', label: 'SKU-2 · Tuerca' },
      { value: 'p-2', label: 'SKU-10 · Tornillo' },
    ])
  })
})

describe('zoneOccupancy', () => {
  it('ocupadas / activas por zona, con porcentaje entero y 0 % en una zona sin posiciones', () => {
    const occ = zoneOccupancy(ZONES, [...BINS, { id: 13, zoneId: 1, code: 'A-4', isActive: false, qtyOnHand: 5 }])
    expect(occ.map((o) => [o.zone.code, o.used, o.total, o.pct])).toEqual([
      ['A', 2, 3, 67],
      ['S', 0, 1, 0],
      ['V', 0, 0, 0],
    ])
  })
})

describe('buildLocationRows / filterLocationRows', () => {
  const rows = buildLocationRows(BINS, ZONES, productsByBin(LINES))

  it('zona por nombre, estado y barra relativa a la posición con más unidades', () => {
    expect(rows.map((r) => [r.bin.code, r.zoneName, r.state, r.fill])).toEqual([
      ['A-10', 'Almacenaje', 'OCCUPIED', 100],
      ['A-2', 'Almacenaje', 'EMPTY', 0],
      ['A-3', 'Almacenaje', 'OCCUPIED', 25],
      ['S-1', 'Staging', 'EMPTY', 0],
    ])
  })

  it('sin filtros devuelve todo; cada filtro vacío no filtra', () => {
    expect(filterLocationRows(rows, EMPTY_LOCATION_FILTERS)).toHaveLength(4)
  })

  it('Zona, Tipo, Producto y Estado se combinan', () => {
    const codes = (f: Partial<typeof EMPTY_LOCATION_FILTERS>) =>
      filterLocationRows(rows, { ...EMPTY_LOCATION_FILTERS, ...f }).map((r) => r.bin.code)
    expect(codes({ zoneIds: ['2'] })).toEqual(['S-1'])
    expect(codes({ zoneTypes: ['STORAGE'] })).toEqual(['A-10', 'A-2', 'A-3'])
    expect(codes({ products: ['p-2'] })).toEqual(['A-10'])
    expect(codes({ products: ['p-1', 'p-2'] })).toEqual(['A-10', 'A-3'])
    expect(codes({ states: ['EMPTY'] })).toEqual(['A-2', 'S-1'])
    expect(codes({ zoneIds: ['1'], states: ['EMPTY'] })).toEqual(['A-2'])
  })
})
