// Reporte de inventario y Reporte de ajustes (Lote 12, armado puro): grupos por categoría con subtotales, valor = total ×
// costo (sin costo = '—' y aviso), total general, avisos de lista truncada y de KPI que no aplica, ubicación del ajuste,
// entradas/salidas/neto.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t as translate } from '../../kernel/i18n/i18n'
import type { KardexRowDto, ProductListItemDto } from './api'
import {
  adjustmentLocation,
  adjustmentTotals,
  buildAdjustmentsReport,
  buildInventoryReport,
  groupInventoryByCategory,
  inventoryValue,
  type ProductReportContext,
} from './inventoryReports'
import { EMPTY_PRODUCT_FILTERS } from './productFilters'

beforeAll(() => setLang('es'))

const t = (key: string, params?: Record<string, string | number>) => translate(key, params)

function ctx(over: Partial<ProductReportContext> = {}): ProductReportContext {
  return {
    t,
    lang: 'en',
    company: 'Advance Depot',
    user: 'Ana Pérez',
    generatedAt: new Date(2026, 8, 30, 14, 5),
    filters: EMPTY_PRODUCT_FILTERS,
    names: { warehouses: new Map(), categories: new Map() },
    ...over,
  }
}

const P = (over: Partial<ProductListItemDto>): ProductListItemDto => ({
  sku: 'X',
  name: 'X',
  qtyOnHand: 0,
  qtyAvailable: 0,
  qtyReserved: 0,
  ...over,
})

const ITEMS: ProductListItemDto[] = [
  P({ sku: 'B-2', name: 'Tiras', categoryName: 'Médico', qtyOnHand: 10, qtyAvailable: 8, qtyReserved: 2, purchaseCost: 1.5 }),
  P({ sku: 'B-10', name: 'Medidor', categoryName: 'Médico', brand: 'Abbott', qtyOnHand: 4, qtyAvailable: 4, purchaseCost: 25 }),
  P({ sku: 'A-1', name: 'Caja', categoryName: 'Empaque', qtyOnHand: 100, qtyAvailable: 100 }),
  P({ sku: 'Z-1', name: 'Suelto', qtyOnHand: 3, qtyAvailable: 3, purchaseCost: 2 }),
]

describe('Reporte de inventario', () => {
  it('inventoryValue: total × costo; sin costo = null', () => {
    expect(inventoryValue({ qtyOnHand: 4, purchaseCost: 25 })).toBe(100)
    expect(inventoryValue({ qtyOnHand: 4, purchaseCost: null })).toBeNull()
  })

  it('agrupa por categoría (orden alfabético, "Sin categoría" al final), cada grupo por SKU natural, con sus sumas', () => {
    const groups = groupInventoryByCategory(ITEMS, 'Sin categoría', 'es')
    expect(groups.map((g) => g.category)).toEqual(['Empaque', 'Médico', 'Sin categoría'])
    expect(groups[1].items.map((p) => p.sku)).toEqual(['B-2', 'B-10'])
    expect(groups[1]).toMatchObject({ available: 12, reserved: 2, onHand: 14, value: 115 })
    // ningún producto con costo en el grupo: valor null (no 0)
    expect(groups[0].value).toBeNull()
  })

  it('buildInventoryReport: columnas, filas, subtotales, total general, resumen y avisos', () => {
    const spec = buildInventoryReport(ITEMS, false, ctx())
    expect(spec.title).toBe('Reporte de inventario')
    expect(spec.company).toBe('Advance Depot')
    expect(spec.columns.map((c) => c.header)).toEqual([
      'SKU',
      'Producto',
      'Categoría',
      'Marca',
      'Disponible',
      'Reservado',
      'Total',
      'Costo unitario',
      'Valor',
    ])
    expect(spec.sections.map((s) => s.title)).toEqual(['Empaque (1)', 'Médico (2)', 'Sin categoría (1)'])
    expect(spec.sections[0].rows[0]).toEqual(['A-1', 'Caja', 'Empaque', '', 100, 0, 100, '—', '—'])
    expect(spec.sections[1].rows[1]).toEqual(['B-10', 'Medidor', 'Médico', 'Abbott', 4, 0, 4, 25, 100])
    expect(spec.sections[1].subtotal).toEqual(['Subtotal Médico', null, null, null, 12, 2, 14, '', 115])
    expect(spec.sections[0].subtotal?.[8]).toBe('—')
    expect(spec.totals).toEqual([['Total general', null, null, null, 115, 2, 117, '', 121]])
    expect(spec.summary?.map((s) => s.value)).toEqual(['4', '117', '115', '121.00'])
    // sin filtros: el bloque del PDF dice "Sin filtros"
    expect(spec.filters).toEqual([])
    // un producto con existencia sin costo + la nota de valoración
    expect(spec.notices).toHaveLength(2)
    expect(spec.notices?.[0]).toContain('Productos con existencia sin costo de compra: 1.')
  })

  it('lista truncada: lo dice en el reporte; sin productos: sin totales y con su texto vacío', () => {
    expect(buildInventoryReport(ITEMS, true, ctx()).notices?.[0]).toContain('primeros 4 productos')
    const empty = buildInventoryReport([], false, ctx())
    expect(empty.totals).toEqual([])
    expect(empty.emptyText).toBe('Ningún producto cumple los filtros.')
  })

  it('los productos sin existencia no se listan: solo se cuentan en un aviso', () => {
    const spec = buildInventoryReport([...ITEMS, P({ sku: 'C-0', name: 'Agotado', categoryName: 'Médico' })], false, ctx())
    expect(spec.sections.map((s) => s.title)).toEqual(['Empaque (1)', 'Médico (2)', 'Sin categoría (1)'])
    expect(spec.summary?.[0].value).toBe('4')
    expect(spec.notices).toContain('Productos sin existencia (en mano 0) no incluidos: 1.')
  })
})

const K = (over: Partial<KardexRowDto>): KardexRowDto => ({ sku: 'GLU-100', productName: 'Medidor', typeCode: 'ADJUSTMENT', ...over })

describe('Reporte de ajustes', () => {
  it('ubicación: destino si entra, origen si sale; si no hay códigos, la posición del API', () => {
    expect(adjustmentLocation(K({ signedQuantity: 5, toWarehouseCode: 'ALM-01', toBinCode: 'A-01' }))).toBe('ALM-01 / A-01')
    expect(adjustmentLocation(K({ signedQuantity: -5, fromWarehouseCode: 'ALM-02', fromBinCode: 'B-02' }))).toBe('ALM-02 / B-02')
    expect(adjustmentLocation(K({ signedQuantity: -1, position: 'ALM-03' }))).toBe('ALM-03')
  })

  it('entradas, salidas y neto', () => {
    expect(adjustmentTotals([K({ signedQuantity: 5 }), K({ signedQuantity: -2 }), K({ signedQuantity: 1.5 })])).toEqual({ inQty: 6.5, outQty: 2, net: 4.5 })
  })

  it('buildAdjustmentsReport: columnas, lote/serie bajo el producto, totales, resumen y aviso del KPI', () => {
    const rows = [
      K({ createdAtUtc: '2026-09-28T14:00:00', signedQuantity: 3, toWarehouseCode: 'ALM-01', toBinCode: 'A-01', reason: 'Encontrado', notes: 'En muelle', userName: 'Ana', lotNumber: 'L-7' }),
      K({ createdAtUtc: '2026-09-27T10:00:00', signedQuantity: -1, fromWarehouseCode: 'ALM-01', fromBinCode: 'A-02', reasonCode: 'DAMAGED', serialNumber: 'S-1' }),
    ]
    const spec = buildAdjustmentsReport(rows, false, ctx({ filters: { ...EMPTY_PRODUCT_FILTERS, kpi: 'serial' } }))
    expect(spec.title).toBe('Reporte de ajustes')
    expect(spec.columns.map((c) => c.header)).toEqual(['Fecha y hora', 'SKU', 'Producto', 'Almacén / Posición', 'Cantidad (±)', 'Motivo', 'Nota', 'Usuario'])
    expect(spec.columns[4].format).toBe('signed')
    const [first, second] = spec.sections[0].rows
    expect(first[0]).toMatch(/2026|26/)
    expect(first.slice(1)).toEqual(['GLU-100', 'Medidor\nLote L-7', 'ALM-01 / A-01', 3, 'Encontrado', 'En muelle', 'Ana'])
    // sin etiqueta del motivo, su código; sin nota ni usuario, vacío
    expect(second.slice(2)).toEqual(['Medidor\nSerie S-1', 'ALM-01 / A-02', -1, 'DAMAGED', '', ''])
    expect(spec.totals?.map((r) => [r[0], r[4]])).toEqual([
      ['Entradas', 3],
      ['Salidas', -1],
      ['Neto', 2],
    ])
    expect(spec.summary?.map((s) => s.value)).toEqual(['2', '+3', '-1', '+2'])
    expect(spec.filters[0]).toEqual({ label: 'Tipo de movimiento', value: 'Ajuste' })
    expect(spec.notices?.[0]).toContain('«Con número de serie»')
  })

  it('truncado: lo dice; sin ajustes: sin totales', () => {
    expect(buildAdjustmentsReport([K({ signedQuantity: 1 })], true, ctx()).notices?.[0]).toContain('1 ajustes más recientes')
    expect(buildAdjustmentsReport([], false, ctx()).totals).toEqual([])
  })

  it('los saldos iniciales de la migración no son ajustes de la operación: se excluyen con un aviso', () => {
    const spec = buildAdjustmentsReport([K({ signedQuantity: 100, reasonCode: 'OPENING_BALANCE' }), K({ signedQuantity: -2, reasonCode: 'DAMAGED' })], false, ctx())
    expect(spec.sections[0].rows).toHaveLength(1)
    expect(spec.summary?.[0].value).toBe('1')
    expect(spec.notices).toContain('Saldos iniciales de la migración excluidos: 1 (no son ajustes de la operación).')
  })
})
