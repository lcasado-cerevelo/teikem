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

const WH = '11111111-1111-1111-1111-111111111111'
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'

const FULL: ProductFilterState = {
  warehouses: [WH],
  products: [{ publicId: P1, sku: 'GLU-100', label: 'GLU-100 · Medidor' }],
  name: '  medidor ',
  categoryIds: ['3'],
  brands: ['Abbott'],
  kpi: 'low',
}

/** Traductor de prueba: la clave y sus parámetros, para ver qué se pidió. */
const t = (key: string, params?: Record<string, string | number>) => (params ? `${key}${JSON.stringify(params)}` : key)

describe('productFilters', () => {
  it('KPI: ?kpi= válido o nada; clic elige o quita; cada KPI con sus parámetros del API', () => {
    expect(parseKpiParam('serial')).toBe('serial')
    expect(parseKpiParam('otro')).toBeNull()
    expect(parseKpiParam(null)).toBeNull()
    expect(toggleKpi(null, 'low')).toBe('low')
    expect(toggleKpi('low', 'low')).toBeNull()
    expect(toggleKpi('low', 'active')).toBe('active')
    expect(kpiQuery('active')).toEqual({ activeOnly: true })
    expect(kpiQuery('available')).toEqual({ activeOnly: true, onlyAvailable: true })
    expect(kpiQuery('low')).toEqual({ belowMin: true })
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
    expect(productListQuery({ ...FULL, kpi: 'available' })).toMatchObject({ activeOnly: true, onlyAvailable: true })
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
