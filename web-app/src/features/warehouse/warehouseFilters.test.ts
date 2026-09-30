import { describe, expect, it } from 'vitest'
import {
  binsQuery,
  distinctOptions,
  EMPTY_BIN_TEXT,
  EMPTY_WAREHOUSE_FILTERS,
  filterWarehouseRows,
  localityCity,
  localityOptionLabel,
  warehouseAddress,
  zoneOptions,
} from './warehouseFilters'

const WAREHOUSES = [
  { publicId: '1', code: 'ALM-10', name: 'Central', line1: 'Calle Luna 5', city: 'San Juan', state: 'PR', postalCode: '00901', statusCode: 'ACTIVE', zoneTypeCodes: ['STORAGE', 'STAGING'] },
  { publicId: '2', code: 'ALM-2', name: 'Norte', line1: null, city: 'Mayagüez', state: 'PR', postalCode: '00680', statusCode: 'INACTIVE', zoneTypeCodes: ['CROSSDOCK'] },
  { publicId: '3', code: 'ALM-3', name: 'Sur', line1: 'Carr. 2', city: 'Ponce', state: null, postalCode: null, statusCode: 'ACTIVE', zoneTypeCodes: null },
]

describe('filterWarehouseRows (lista de almacenes, en el cliente)', () => {
  it('sin filtros devuelve todo', () => {
    expect(filterWarehouseRows(WAREHOUSES, EMPTY_WAREHOUSE_FILTERS)).toHaveLength(3)
  })

  it('Código, Nombre y Estatus: multiselección exacta', () => {
    expect(filterWarehouseRows(WAREHOUSES, { ...EMPTY_WAREHOUSE_FILTERS, codes: ['ALM-2', 'ALM-3'] }).map((w) => w.publicId)).toEqual(['2', '3'])
    expect(filterWarehouseRows(WAREHOUSES, { ...EMPTY_WAREHOUSE_FILTERS, names: ['Central'] }).map((w) => w.publicId)).toEqual(['1'])
    expect(filterWarehouseRows(WAREHOUSES, { ...EMPTY_WAREHOUSE_FILTERS, statuses: ['INACTIVE'] }).map((w) => w.publicId)).toEqual(['2'])
  })

  it('Tipo: el almacén pasa si tiene alguna zona de esos tipos (zoneTypeCodes null = ninguna)', () => {
    expect(filterWarehouseRows(WAREHOUSES, { ...EMPTY_WAREHOUSE_FILTERS, zoneTypes: ['STAGING', 'CROSSDOCK'] }).map((w) => w.publicId)).toEqual(['1', '2'])
  })

  it('Dirección: texto sin acentos sobre dirección, ciudad, estado y código postal; filtros combinados', () => {
    expect(filterWarehouseRows(WAREHOUSES, { ...EMPTY_WAREHOUSE_FILTERS, address: 'mayaguez' }).map((w) => w.publicId)).toEqual(['2'])
    expect(filterWarehouseRows(WAREHOUSES, { ...EMPTY_WAREHOUSE_FILTERS, address: '0090' }).map((w) => w.publicId)).toEqual(['1'])
    expect(filterWarehouseRows(WAREHOUSES, { ...EMPTY_WAREHOUSE_FILTERS, address: 'luna' }).map((w) => w.publicId)).toEqual(['1'])
    expect(filterWarehouseRows(WAREHOUSES, { ...EMPTY_WAREHOUSE_FILTERS, address: 'pr', statuses: ['ACTIVE'] }).map((w) => w.publicId)).toEqual(['1'])
  })
})

describe('warehouseAddress / distinctOptions / zoneOptions', () => {
  it('une lo que hay: "Dirección, Ciudad, Estado ZIP"', () => {
    expect(warehouseAddress(WAREHOUSES[0])).toBe('Calle Luna 5, San Juan, PR 00901')
    expect(warehouseAddress(WAREHOUSES[2])).toBe('Carr. 2, Ponce')
    expect(warehouseAddress({})).toBe('')
  })

  it('distinctOptions: sin vacíos ni repetidos, orden natural', () => {
    expect(distinctOptions(['ALM-10', 'ALM-2', null, '', 'ALM-2']).map((o) => o.value)).toEqual(['ALM-2', 'ALM-10'])
  })

  it('zoneOptions: solo zonas activas, "Código · Nombre" y el tipo como hint', () => {
    const opts = zoneOptions([
      { id: 1, code: 'A', name: 'Zona A', zoneType: 'Almacenaje', isActive: true },
      { id: 2, code: 'B', name: 'Zona B', isActive: false },
    ])
    expect(opts).toEqual([{ value: '1', label: 'A · Zona A', hint: 'Almacenaje' }])
  })
})

describe('binsQuery (pestaña Posiciones → GET .../bins)', () => {
  it('sin filtros solo manda los interruptores', () => {
    expect(binsQuery(EMPTY_BIN_TEXT, [], false, false)).toEqual({ includeInactive: false, onlyWithStock: false })
  })

  it('Código va como search; las partes y las zonas, cada una en su parámetro (recortadas)', () => {
    expect(binsQuery({ code: ' a-01 ', aisle: 'A01', rack: ' ', level: 'N3', position: 'P04' }, ['1', '2'], true, true)).toEqual({
      includeInactive: true,
      onlyWithStock: true,
      search: 'a-01',
      aisle: 'A01',
      level: 'N3',
      position: 'P04',
      zoneIds: [1, 2],
    })
  })
})

describe('localityOptionLabel / localityCity (catálogo USPS de localidades)', () => {
  const SABANA = { city: 'SABANA SECA', postalCode: '00952', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Toa Baja' }
  const MAYAGUEZ = { city: 'MAYAGUEZ', postalCode: '00680', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Mayagüez' }
  const NY = { city: 'NEW YORK', postalCode: '10001', state: 'NY', countryCode: 'US', country: 'Estados Unidos', municipality: null }

  it('municipio entre paréntesis solo si difiere de la ciudad postal (sin acentos ni mayúsculas); país solo fuera de PR', () => {
    expect(localityOptionLabel(SABANA)).toBe('00952 · SABANA SECA (Toa Baja), PR')
    expect(localityOptionLabel(MAYAGUEZ)).toBe('00680 · MAYAGUEZ, PR')
    expect(localityOptionLabel(NY)).toBe('10001 · NEW YORK, NY · Estados Unidos')
  })

  it('la ciudad del almacén es el municipio (con acentos) o, sin él, la ciudad postal', () => {
    expect(localityCity(SABANA)).toBe('Toa Baja')
    expect(localityCity(MAYAGUEZ)).toBe('Mayagüez')
    expect(localityCity(NY)).toBe('NEW YORK')
  })
})
