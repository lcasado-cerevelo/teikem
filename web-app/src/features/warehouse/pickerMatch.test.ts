import { describe, expect, it } from 'vitest'
import { exactCodeMatch, filterBinOptions, filterWarehouses, foldText, orderBins } from './pickerMatch'

const WAREHOUSES = [
  { code: 'ALM-010', name: 'Anexo' },
  { code: 'ALM-01', name: 'Almacén principal' },
  { code: 'NTE', name: 'Bodega Norte' },
]

describe('foldText', () => {
  it('quita mayúsculas y acentos', () => {
    expect(foldText('Almacén ÑANDÚ')).toBe('almacen nandu')
    expect(foldText(null)).toBe('')
  })
})

describe('exactCodeMatch', () => {
  it('compara el código completo sin mayúsculas ni espacios a los lados', () => {
    expect(exactCodeMatch(WAREHOUSES, '  alm-01 ')?.name).toBe('Almacén principal')
    expect(exactCodeMatch(WAREHOUSES, 'alm-0')).toBeUndefined()
    expect(exactCodeMatch(WAREHOUSES, '')).toBeUndefined()
  })
})

describe('filterWarehouses', () => {
  it('texto vacío: todos en el orden de la lista', () => {
    expect(filterWarehouses(WAREHOUSES, ' ').map((w) => w.code)).toEqual(['ALM-010', 'ALM-01', 'NTE'])
  })

  it('subcadena de código o nombre, sin acentos; la coincidencia exacta por código va primero', () => {
    expect(filterWarehouses(WAREHOUSES, 'ALM-01').map((w) => w.code)).toEqual(['ALM-01', 'ALM-010'])
    expect(filterWarehouses(WAREHOUSES, 'almacen').map((w) => w.code)).toEqual(['ALM-01'])
    expect(filterWarehouses(WAREHOUSES, 'norte').map((w) => w.code)).toEqual(['NTE'])
    expect(filterWarehouses(WAREHOUSES, 'zzz')).toEqual([])
  })
})

describe('orderBins', () => {
  const BINS = [
    { id: 1, code: 'A-01' },
    { id: 2, code: 'A-010' },
    { id: 3, code: 'B-01' },
    { id: 4, code: 'C-01' },
  ]

  it('sugeridas primero en su orden, luego el resto en el orden del servidor', () => {
    expect(orderBins(BINS, '', [4, 3]).map((b) => b.id)).toEqual([4, 3, 1, 2])
  })

  it('la coincidencia exacta por código gana a las sugeridas', () => {
    expect(orderBins(BINS, 'a-01', [4]).map((b) => b.id)).toEqual([1, 4, 2, 3])
  })
})

describe('filterBinOptions', () => {
  const items = [
    { id: 1, code: 'PISO', zoneCode: 'Piso' },
    { id: 2, code: 'B-02', zoneCode: 'Reserva' },
    { id: 3, code: 'A-01', zoneCode: null },
  ]
  it('subcadena en código o zona, sin mayúsculas ni acentos, en el orden de la lista; vacío = todas', () => {
    expect(filterBinOptions(items, '').map((b) => b.id)).toEqual([1, 2, 3])
    expect(filterBinOptions(items, ' RESERVA ').map((b) => b.id)).toEqual([2])
    expect(filterBinOptions(items, '0').map((b) => b.id)).toEqual([2, 3])
    expect(filterBinOptions(items, 'zz')).toEqual([])
  })
})
