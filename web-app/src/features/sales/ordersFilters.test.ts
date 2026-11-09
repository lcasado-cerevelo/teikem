// Filtros de la pestaña Órdenes (2026-11-09): del estado de la pantalla a la consulta de GET /api/v1/sales-orders (puro).
import { describe, expect, it } from 'vitest'
import { EMPTY_ORDERS_FILTERS, hasOrdersFilters, ordersApiFilters } from './ordersFilters'
import { salesOrderListQuery } from './salesApi'

describe('filtros de Órdenes de venta', () => {
  it('sin filtros la consulta va vacía (nada de parámetros sueltos)', () => {
    expect(hasOrdersFilters(EMPTY_ORDERS_FILTERS)).toBe(false)
    const q = salesOrderListQuery(ordersApiFilters(EMPTY_ORDERS_FILTERS, ''))
    expect(Object.values(q).every((v) => v === undefined)).toBe(true)
  })

  it('varios clientes, productos y estatus viajan como listas; el texto libre es el ya pausado', () => {
    const f = {
      ...EMPTY_ORDERS_FILTERS,
      q: 'med  ',
      clients: [{ publicId: 'c1', label: 'A' }, { publicId: 'c2', label: 'B' }],
      products: [{ publicId: 'p1', sku: 'MED', label: 'MED · Medidor' }],
      statuses: ['REQUESTED', 'PARTIAL'],
      mine: true,
    }
    expect(hasOrdersFilters(f)).toBe(true)
    expect(salesOrderListQuery(ordersApiFilters(f, 'med'))).toEqual({
      q: 'med',
      status: ['REQUESTED', 'PARTIAL'],
      clientPublicId: ['c1', 'c2'],
      productPublicId: ['p1'],
      from: undefined,
      to: undefined,
      dateField: undefined,
      assignedToMe: true,
    })
  })

  it('la fecha requerida solo viaja si hay rango; la solicitada es la de omisión y no se manda', () => {
    const requiredNoRange = { ...EMPTY_ORDERS_FILTERS, dateField: 'REQUIRED' as const }
    expect(salesOrderListQuery(ordersApiFilters(requiredNoRange, '')).dateField).toBeUndefined()
    expect(hasOrdersFilters(requiredNoRange)).toBe(false)
    const required = { ...requiredNoRange, range: { from: '2026-11-01', to: '' } }
    expect(salesOrderListQuery(ordersApiFilters(required, ''))).toMatchObject({ from: '2026-11-01', to: undefined, dateField: 'REQUIRED' })
    const requested = { ...EMPTY_ORDERS_FILTERS, range: { from: '2026-11-01', to: '2026-11-30' } }
    expect(salesOrderListQuery(ordersApiFilters(requested, ''))).toMatchObject({ from: '2026-11-01', to: '2026-11-30', dateField: undefined })
  })
})
