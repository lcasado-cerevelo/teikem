// Lógica pura de Ubicaciones (Lote 1): recuadros de zona con capacidad real, estatus y ocupación de posición, columna
// Producto, selección de zona por clic y consulta del listado paginado.
import { describe, expect, it } from 'vitest'
import type { WarehouseZoneDto } from './api'
import {
  EMPTY_LOCATION_FILTERS,
  binFillPct,
  binOccupancy,
  binProductCell,
  buildBinListQuery,
  expandBinsForExport,
  parseZoneParam,
  toggleZoneSelection,
  zoneCapacities,
} from './locations'

const ZONES: WarehouseZoneDto[] = [
  // con cupo en todas sus posiciones
  { id: 1, code: 'A', name: 'Almacenaje', zoneTypeCode: 'STORAGE', binCount: 3, occupiedBinCount: 2, capacityQty: 300, qtyOnHandInCapacityBins: 120, qtyOnHand: 120, binsWithoutCapacity: 0 },
  // cupo solo en parte: 2 posiciones sin cupo (su existencia no entra al porcentaje)
  { id: 2, code: 'P', name: 'Picking', zoneTypeCode: 'PICKING', binCount: 4, occupiedBinCount: 3, capacityQty: 50, qtyOnHandInCapacityBins: 50, qtyOnHand: 80, binsWithoutCapacity: 2 },
  // ninguna posición con cupo (hoy, Advance Depot)
  { id: 3, code: 'S', name: 'Staging', zoneTypeCode: 'STAGING', binCount: 5, occupiedBinCount: 1, capacityQty: 0, qtyOnHandInCapacityBins: 0, qtyOnHand: 1234.5, binsWithoutCapacity: 5 },
  // sin posiciones
  { id: 4, code: 'V', name: 'Vacía', zoneTypeCode: 'STORAGE', binCount: 0, occupiedBinCount: 0, capacityQty: 0, qtyOnHandInCapacityBins: 0, qtyOnHand: 0, binsWithoutCapacity: 0 },
]

describe('zoneCapacities (recuadros del río)', () => {
  it('ocupado / capacidad de la zona con % real; sin cupo, la existencia total sin porcentaje', () => {
    expect(zoneCapacities(ZONES).map((z) => [z.zone.code, z.mode, z.occupied, z.capacity, z.pct, z.binsWithoutCapacity, z.qtyOnHand])).toEqual([
      ['A', 'capacity', 120, 300, 40, 0, 120],
      ['P', 'capacity', 50, 50, 100, 2, 80],
      ['S', 'noCapacity', 0, 0, null, 5, 1234.5],
      ['V', 'noBins', 0, 0, null, 0, 0],
    ])
  })

  it('nunca inventa un porcentaje: sin cupo pct es null aunque haya existencia', () => {
    const [z] = zoneCapacities([{ id: 9, code: 'X', binCount: 2, qtyOnHand: 10 }])
    expect(z.mode).toBe('noCapacity')
    expect(z.pct).toBeNull()
  })
})

describe('binOccupancy / binFillPct', () => {
  it('usa el estatus del API cuando llega', () => {
    expect(binOccupancy({ occupancy: 'FULL', qtyOnHand: 0, maxCapacityQty: null })).toBe('FULL')
  })

  it('sin estatus del API aplica la regla del servidor: Vacía / Sin cupo / Llena / Parcial', () => {
    expect(binOccupancy({ qtyOnHand: 0, maxCapacityQty: 10 })).toBe('EMPTY')
    expect(binOccupancy({ qtyOnHand: 3, maxCapacityQty: null })).toBe('NO_CAPACITY')
    expect(binOccupancy({ qtyOnHand: 10, maxCapacityQty: 10 })).toBe('FULL')
    expect(binOccupancy({ qtyOnHand: 12, maxCapacityQty: 10 })).toBe('FULL')
    expect(binOccupancy({ qtyOnHand: 4, maxCapacityQty: 10 })).toBe('PARTIAL')
    expect(binOccupancy({ occupancy: 'RARO', qtyOnHand: 4, maxCapacityQty: 10 })).toBe('PARTIAL')
  })

  it('porcentaje real respecto al cupo (puede pasar de 100); sin cupo, null', () => {
    expect(binFillPct(25, 100)).toBe(25)
    expect(binFillPct(0, 100)).toBe(0)
    expect(binFillPct(150, 100)).toBe(150)
    expect(binFillPct(5, null)).toBeNull()
    expect(binFillPct(5, 0)).toBeNull()
  })
})

describe('binProductCell', () => {
  it('nada, el nombre del único producto o "N productos"', () => {
    expect(binProductCell({ productCount: 0 })).toEqual({ kind: 'none' })
    expect(binProductCell({ productCount: 1, singleProductSku: 'TORN-01', singleProductName: 'Tornillo' })).toEqual({ kind: 'one', name: 'Tornillo', sku: 'TORN-01' })
    expect(binProductCell({ productCount: 1, singleProductSku: 'TORN-01', singleProductName: null })).toEqual({ kind: 'one', name: 'TORN-01', sku: 'TORN-01' })
    expect(binProductCell({ productCount: 3 })).toEqual({ kind: 'many', count: 3 })
  })
})

describe('selección de zona (recuadro + ?zone=)', () => {
  it('clic en un recuadro deja solo esa zona; otro clic en la misma quita el filtro', () => {
    expect(toggleZoneSelection([], '2')).toEqual(['2'])
    expect(toggleZoneSelection(['2'], '2')).toEqual([])
    expect(toggleZoneSelection(['1', '2'], '2')).toEqual(['2'])
    expect(toggleZoneSelection(['1'], '2')).toEqual(['2'])
  })

  it('?zone= acepta repetido o separado por comas; descarta lo que no es un id', () => {
    expect(parseZoneParam(['3', '5,3', 'x', '0', ' 7 '])).toEqual(['3', '5', '7'])
    expect(parseZoneParam([])).toEqual([])
  })
})

describe('buildBinListQuery', () => {
  it('sin filtros: solo activas', () => {
    expect(buildBinListQuery(EMPTY_LOCATION_FILTERS, ZONES)).toEqual({
      query: { includeInactive: false, zoneIds: undefined, productPublicIds: undefined, occupancy: undefined },
      impossible: false,
    })
  })

  it('Zona, Producto y Estatus van al servidor', () => {
    const { query } = buildBinListQuery({ zoneIds: ['2'], zoneTypes: [], productPublicIds: ['p-1'], occupancy: ['FULL', 'NO_CAPACITY'] }, ZONES)
    expect(query).toEqual({ includeInactive: false, zoneIds: [2], productPublicIds: ['p-1'], occupancy: ['FULL', 'NO_CAPACITY'] })
  })

  it('Tipo se traduce a las zonas de ese tipo y se cruza con Zona', () => {
    expect(buildBinListQuery({ ...EMPTY_LOCATION_FILTERS, zoneTypes: ['STORAGE'] }, ZONES).query.zoneIds).toEqual([1, 4])
    expect(buildBinListQuery({ ...EMPTY_LOCATION_FILTERS, zoneIds: ['1', '3'], zoneTypes: ['STORAGE'] }, ZONES).query.zoneIds).toEqual([1])
  })

  it('una combinación sin zonas posibles no consulta (zoneIds vacío sería "todas")', () => {
    const r = buildBinListQuery({ ...EMPTY_LOCATION_FILTERS, zoneIds: ['3'], zoneTypes: ['STORAGE'] }, ZONES)
    expect(r.impossible).toBe(true)
    expect(buildBinListQuery({ ...EMPTY_LOCATION_FILTERS, zoneTypes: ['CROSSDOCK'] }, ZONES).impossible).toBe(true)
  })
})


describe('expandBinsForExport (una línea por producto)', () => {
  const bin = (id: number, code: string, productCount: number, extra: Record<string, unknown> = {}) => ({ id, code, productCount, qtyOnHand: 9, ...extra })
  const bal = (binId: number, sku: string, qtyOnHand: number, productPublicId = sku) => ({ binId, productPublicId, sku, productName: `Nombre ${sku}`, qtyOnHand })

  it('una posición con varios productos sale repetida, una vez por producto (por SKU, lotes sumados); las de 0 o 1 salen igual', () => {
    const bins = [bin(1, 'A-01', 3), bin(2, 'A-02', 1, { singleProductSku: 'X', singleProductName: 'X' }), bin(3, 'A-03', 0)]
    const out = expandBinsForExport(bins, [bal(1, 'TUER', 4), bal(1, 'ARAN', 3), bal(1, 'TORN', 2), bal(1, 'TORN', 1), bal(2, 'X', 9)])
    expect(out.map((r) => [r.code, r.exportProduct?.sku, r.exportProduct?.qty])).toEqual([
      ['A-01', 'ARAN', 3],
      ['A-01', 'TORN', 3],
      ['A-01', 'TUER', 4],
      ['A-02', undefined, undefined],
      ['A-03', undefined, undefined],
    ])
    expect(out[0]).toMatchObject({ id: 1, productCount: 3, exportProduct: { name: 'Nombre ARAN' } })
  })

  it('si de una posición con varios no llegó ningún saldo, queda en una sola línea', () => {
    const out = expandBinsForExport([bin(1, 'A-01', 2)], [])
    expect(out).toHaveLength(1)
    expect(out[0].exportProduct).toBeUndefined()
  })
})
