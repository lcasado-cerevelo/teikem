// Filtros de 'Productos e inventario' (Lote 12, lógica pura): KPI de la URL, filtros → consulta de productos, traslado al
// Kárdex para el Reporte de ajustes y el texto legible de "Filtros aplicados" del PDF.
import { describe, expect, it } from 'vitest'
import {
  adjustmentsKardexQuery,
  describeProductFilters,
  EMPTY_PRODUCT_FILTERS,
  hasProductFilters,
  kpiQuery,
  parseKpiParam,
  productListQuery,
  toggleKpi,
  type ProductFilterState,
} from './productFilters'
import { OWN_OWNER } from './kardexView'

const WH = '11111111-1111-1111-1111-111111111111'
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'

const FULL: ProductFilterState = {
  warehouses: [WH],
  products: [{ publicId: P1, sku: 'GLU-100', label: 'GLU-100 · Medidor' }],
  name: '  medidor ',
  bins: [],
  categoryIds: ['3'],
  brands: ['Abbott'],
  owners: [],
  kpi: 'low',
  hasAvailable: false,
}

/** Traductor de prueba: la clave y sus parámetros, para ver qué se pidió. */
const t = (key: string, params?: Record<string, string | number>) => (params ? `${key}${JSON.stringify(params)}` : key)

describe('productFilters · posición', () => {
  it('las posiciones elegidas van al API como binIds, cuentan como filtro y salen en «Filtros aplicados»', () => {
    const bins = [{ id: 7, label: 'A-01 · PCK · ALM-01' }, { id: 9, label: 'A-02 · PCK · ALM-01' }]
    const f = { ...FULL, kpi: null, name: '', bins }
    expect(productListQuery(f).binIds).toEqual([7, 9])
    expect(productListQuery({ ...f, bins: [] }).binIds).toBeUndefined()
    expect(hasProductFilters({ ...EMPTY_PRODUCT_FILTERS, bins })).toBe(true)
    expect(hasProductFilters({ ...EMPTY_PRODUCT_FILTERS, bins: [] })).toBe(false)
    const applied = describeProductFilters({ ...EMPTY_PRODUCT_FILTERS, bins }, { warehouses: new Map(), categories: new Map() }, t, 'inventory')
    expect(applied.map((a) => a.label)).toEqual(['warehouse.products.filters.bin'])
    expect(applied[0].value).toContain('A-01 · PCK · ALM-01')
    expect(applied[0].value).toContain('A-02 · PCK · ALM-01')
  })
})

describe('productFilters', () => {
  it('KPI: ?kpi= válido o nada; clic elige o quita; cada KPI con sus parámetros del API', () => {
    expect(parseKpiParam('serial')).toBe('serial')
    expect(parseKpiParam('otro')).toBeNull()
    expect(parseKpiParam(null)).toBeNull()
    expect(toggleKpi(null, 'low')).toBe('low')
    expect(toggleKpi('low', 'low')).toBeNull()
    expect(toggleKpi('low', 'active')).toBe('active')
    expect(kpiQuery('active')).toEqual({ activeOnly: true })
    expect(kpiQuery('available')).toEqual({ activeOnly: true, onlyOnHand: true })
    expect(kpiQuery('low')).toEqual({ belowMin: true })
    // 2026-10-05: tableta "No disponibles" (activos con disponible = 0)
    expect(parseKpiParam('unavailable')).toBe('unavailable')
    expect(kpiQuery('unavailable')).toEqual({ activeOnly: true, unavailable: true })
    expect(kpiQuery('serial')).toEqual({ activeOnly: true, serialOnly: true })
    expect(kpiQuery(null)).toEqual({})
  })

  it('productListQuery: listas vacías y nombre vacío no viajan; nombre recortado; categorías como número', () => {
    expect(JSON.parse(JSON.stringify(productListQuery(EMPTY_PRODUCT_FILTERS)))).toEqual({})
    expect(productListQuery(FULL)).toEqual({
      warehousePublicIds: [WH],
      productPublicIds: [P1],
      name: 'medidor',
      categoryIds: [3],
      brands: ['Abbott'],
      belowMin: true,
    })
    expect(productListQuery({ ...FULL, kpi: 'available' })).toMatchObject({ activeOnly: true, onlyOnHand: true })
    // Unidades totales = con existencia en mano, no con disponible (decisión del 2026-09-30)
    expect(productListQuery({ ...FULL, kpi: 'available' })).not.toHaveProperty('onlyAvailable')
  })

  it('adjustmentsKardexQuery: tipo ADJUSTMENT y los mismos filtros, sin el KPI (no aplica a movimientos)', () => {
    expect(adjustmentsKardexQuery(FULL)).toEqual({
      types: ['ADJUSTMENT'],
      warehousePublicIds: [WH],
      productPublicIds: [P1],
      categoryIds: [3],
      brands: ['Abbott'],
      name: 'medidor',
    })
    expect(JSON.parse(JSON.stringify(adjustmentsKardexQuery(EMPTY_PRODUCT_FILTERS)))).toEqual({ types: ['ADJUSTMENT'] })
  })

  it('Dueño (varios): Propio va como includeOwn y los clientes como ownerClientPublicIds, a la tabla y al Kárdex de ajustes; vacío = todos', () => {
    const C = 'cccccccc-0000-0000-0000-000000000001'
    const f = { ...FULL, kpi: null, owners: [OWN_OWNER, C] }
    expect(productListQuery(f)).toMatchObject({ ownerClientPublicIds: [C], includeOwn: true })
    expect(adjustmentsKardexQuery(f)).toMatchObject({ ownerClientPublicIds: [C], includeOwn: true })
    expect(productListQuery({ ...f, owners: [C] })).toMatchObject({ ownerClientPublicIds: [C], includeOwn: undefined })
    expect(productListQuery({ ...f, owners: [OWN_OWNER] })).toMatchObject({ ownerClientPublicIds: undefined, includeOwn: true })
    expect(JSON.parse(JSON.stringify(productListQuery(EMPTY_PRODUCT_FILTERS)))).toEqual({})
    expect(hasProductFilters({ ...EMPTY_PRODUCT_FILTERS, owners: [C] })).toBe(true)
    const names = { warehouses: new Map(), categories: new Map(), owners: new Map([[OWN_OWNER, 'Propio'], [C, 'Cliente C']]) }
    expect(describeProductFilters({ ...EMPTY_PRODUCT_FILTERS, owners: [OWN_OWNER, C] }, names, t, 'inventory')).toEqual([
      { label: 'warehouse.products.filters.owner', value: 'Propio, Cliente C' },
    ])
  })

  it('hasProductFilters', () => {
    expect(hasProductFilters(EMPTY_PRODUCT_FILTERS)).toBe(false)
    expect(hasProductFilters({ ...EMPTY_PRODUCT_FILTERS, name: '   ' })).toBe(false)
    expect(hasProductFilters({ ...EMPTY_PRODUCT_FILTERS, kpi: 'serial' })).toBe(true)
  })

  it('describeProductFilters: nombres (no ids), en el orden de la pantalla; ajustes con el tipo y sin la vista', () => {
    const names = { warehouses: new Map([[WH, 'ALM-01 · Principal']]), categories: new Map([['3', 'Médico']]) }
    const inv = describeProductFilters(FULL, names, t, 'inventory')
    expect(inv.map((f) => f.label)).toEqual([
      'warehouse.products.filters.warehouse',
      'warehouse.products.filters.sku',
      'warehouse.products.filters.name',
      'warehouse.products.filters.category',
      'warehouse.products.filters.brand',
      'warehouse.products.reports.filters.view',
    ])
    expect(inv[0].value).toBe('ALM-01 · Principal warehouse.products.reports.filters.warehouseScope')
    expect(inv[1].value).toBe('GLU-100 · Medidor')
    expect(inv[2].value).toBe('warehouse.products.reports.filters.contains{"text":"medidor"}')
    expect(inv[3].value).toBe('Médico warehouse.products.reports.filters.withSubcategories')
    expect(inv[5].value).toBe('warehouse.products.kpis.view.low')

    const adj = describeProductFilters(FULL, names, t, 'adjustments')
    expect(adj[0]).toEqual({ label: 'warehouse.products.reports.filters.type', value: 'warehouse.products.reports.filters.typeAdjustment' })
    expect(adj.map((f) => f.label)).not.toContain('warehouse.products.reports.filters.view')
    expect(adj[1].value).toBe('ALM-01 · Principal')

    // id sin nombre conocido: se muestra tal cual
    const unknown = describeProductFilters({ ...EMPTY_PRODUCT_FILTERS, warehouses: ['x-1'] }, { warehouses: new Map(), categories: new Map() }, t, 'adjustments')
    expect(unknown[1].value).toBe('x-1')
    expect(describeProductFilters(EMPTY_PRODUCT_FILTERS, names, t, 'inventory')).toEqual([])
  })
})

describe('Excluir no disponibles (hasAvailable)', () => {
  it('manda hasAvailable solo encendido, cuenta como filtro y sale en «Filtros aplicados»', () => {
    expect(productListQuery(EMPTY_PRODUCT_FILTERS).hasAvailable).toBeUndefined()
    const on = { ...EMPTY_PRODUCT_FILTERS, hasAvailable: true }
    expect(productListQuery(on).hasAvailable).toBe(true)
    expect(hasProductFilters(on)).toBe(true)
  })
})
