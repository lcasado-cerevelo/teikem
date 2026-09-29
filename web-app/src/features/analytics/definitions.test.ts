import { describe, expect, it } from 'vitest'
import {
  buildChartRequest,
  buildIndicatorRequest,
  definitionToFormValues,
  filterIsUnrepresentable,
  groupDefinitions,
  parseFilterRows,
  serializeFilterRows,
  visibilityBadgeKey,
  withShowInPulse,
  type AnalyticsDefinition,
  type DefinitionFormValues,
  type FilterRow,
} from './definitions'

function def(over: Partial<AnalyticsDefinition> = {}): AnalyticsDefinition {
  return { id: 1, name: 'Órdenes entregadas hoy', businessModule: 'OPERATIONS', ...over }
}

describe('groupDefinitions', () => {
  it('agrupa por módulo de negocio en el orden Operación → Almacén → Contabilidad', () => {
    const list = [
      def({ id: 1, name: 'Z', businessModule: 'ACCOUNTING' }),
      def({ id: 2, name: 'A', businessModule: 'WAREHOUSE' }),
      def({ id: 3, name: 'B', businessModule: 'OPERATIONS' }),
    ]
    const groups = groupDefinitions(list)
    expect(groups.map((g) => g.module)).toEqual(['OPERATIONS', 'WAREHOUSE', 'ACCOUNTING'])
    expect(groups[0].group).toBe('ops')
    expect(groups[1].group).toBe('warehouse')
    expect(groups[2].group).toBe('money')
  })

  it('ordena las tarjetas de cada grupo por nombre y omite grupos vacíos', () => {
    const list = [def({ id: 1, name: 'Zeta', businessModule: 'OPERATIONS' }), def({ id: 2, name: 'Alfa', businessModule: 'OPERATIONS' })]
    const groups = groupDefinitions(list)
    expect(groups).toHaveLength(1)
    expect(groups[0].items.map((i) => i.name)).toEqual(['Alfa', 'Zeta'])
  })

  it('sin definiciones, no hay grupos', () => {
    expect(groupDefinitions(null)).toEqual([])
    expect(groupDefinitions([])).toEqual([])
  })
})

describe('serializeFilterRows / parseFilterRows', () => {
  it('sin filas, no hay filtro', () => {
    expect(serializeFilterRows([])).toBeNull()
  })

  it('serializa condiciones simples con valores numéricos y de texto', () => {
    const rows: FilterRow[] = [
      { field: 'status', op: 'eq', value: 'DELIVERED', value2: '' },
      { field: 'cod', op: 'gt', value: '0', value2: '' },
    ]
    const json = serializeFilterRows(rows)
    expect(JSON.parse(json ?? '')).toEqual({ and: [{ field: 'status', op: 'eq', value: 'DELIVERED' }, { field: 'cod', op: 'gt', value: 0 }] })
  })

  it('serializa in/notIn como lista y between como arreglo de dos', () => {
    const rows: FilterRow[] = [
      { field: 'town', op: 'in', value: 'SJ, PONCE, ARECIBO', value2: '' },
      { field: 'qty', op: 'between', value: '1', value2: '10' },
    ]
    const json = JSON.parse(serializeFilterRows(rows) ?? '')
    expect(json.and[0]).toEqual({ field: 'town', op: 'in', value: ['SJ', 'PONCE', 'ARECIBO'] })
    expect(json.and[1]).toEqual({ field: 'qty', op: 'between', value: [1, 10] })
  })

  it('los operadores sin valor no llevan "value"', () => {
    const json = JSON.parse(serializeFilterRows([{ field: 'cancelledAt', op: 'isNull', value: '', value2: '' }]) ?? '')
    expect(json.and[0]).toEqual({ field: 'cancelledAt', op: 'isNull' })
  })

  it('las filas sin campo se descartan', () => {
    expect(serializeFilterRows([{ field: '', op: 'eq', value: 'x', value2: '' }])).toBeNull()
  })

  it('parseFilterRows es el inverso de serializeFilterRows para el caso plano', () => {
    const rows: FilterRow[] = [
      { field: 'status', op: 'eq', value: 'DELIVERED', value2: '' },
      { field: 'qty', op: 'between', value: '1', value2: '10' },
      { field: 'town', op: 'in', value: 'SJ, PONCE', value2: '' },
      { field: 'cancelledAt', op: 'isNull', value: '', value2: '' },
    ]
    const json = serializeFilterRows(rows)
    expect(parseFilterRows(json)).toEqual(rows)
  })

  it('un filtro con or/not anidado no se puede armar en filas: se descarta', () => {
    expect(parseFilterRows(JSON.stringify({ or: [{ field: 'a', op: 'eq', value: 1 }] }))).toEqual([])
  })

  it('sin filtro o JSON inválido, sin filas', () => {
    expect(parseFilterRows(null)).toEqual([])
    expect(parseFilterRows('{not json')).toEqual([])
  })
})

describe('buildIndicatorRequest / buildChartRequest', () => {
  const values: DefinitionFormValues = {
    name: 'Órdenes entregadas hoy',
    descriptionEs: 'es',
    descriptionEn: 'en',
    dataSource: 'ORDERS',
    aggregateFn: 'COUNT',
    field: '',
    businessModule: 'OPERATIONS',
    isMoney: false,
    visibility: 'PRIVATE',
    dateRangeMode: 'LAST7',
    dateFrom: '',
    dateTo: '',
    sortOrder: 10,
    showInPulse: true,
    groupByField: '',
    chartType: '',
  }

  it('arma el cuerpo del indicador con nombre, descripciones y filtro', () => {
    const filterJson = serializeFilterRows([{ field: 'status', op: 'eq', value: 'DELIVERED', value2: '' }])
    const body = buildIndicatorRequest(values, { es: 'es', en: 'en' }, filterJson, [])
    expect(body.name).toBe('Órdenes entregadas hoy')
    expect(body.descriptions).toEqual({ es: 'es', en: 'en' })
    expect(body.dataSource).toBe('ORDERS')
    expect(body.field).toBeNull() // COUNT no lleva campo
    expect(JSON.parse(body.filterJson ?? '')).toEqual({ and: [{ field: 'status', op: 'eq', value: 'DELIVERED' }] })
    expect(body.dateFrom).toBeNull() // no es CUSTOM
  })

  it('con rango CUSTOM manda dateFrom/dateTo; si no, van null', () => {
    const custom = buildIndicatorRequest({ ...values, dateRangeMode: 'CUSTOM', dateFrom: '2026-01-01', dateTo: '2026-01-31' }, null, null, [])
    expect(custom.dateFrom).toBe('2026-01-01')
    expect(custom.dateTo).toBe('2026-01-31')
  })

  it('el gráfico agrega groupByField y chartType', () => {
    const body = buildChartRequest({ ...values, groupByField: 'status', chartType: 'DONUT' }, null, null, [])
    expect(body.groupByField).toBe('status')
    expect(body.chartType).toBe('DONUT')
  })

  it('con un cálculo distinto de COUNT, envía el campo elegido', () => {
    const body = buildIndicatorRequest({ ...values, aggregateFn: 'SUM', field: 'cod' }, null, null, [])
    expect(body.field).toBe('cod')
  })

  it('descriptions null no cambia la descripción; filterJson null la borra (así lo trata el API)', () => {
    const body = buildIndicatorRequest(values, null, null, [])
    expect(body.descriptions).toBeNull()
    expect(body.filterJson).toBeNull()
  })
})

describe('definitionToFormValues / withShowInPulse', () => {
  it('precarga cada idioma por separado desde `descriptions` (ya no del `description` resuelto)', () => {
    const values = definitionToFormValues(def({ description: 'Hola', descriptions: { es: 'Hola', en: 'Hi' } }), 'indicator')
    expect(values.descriptionEs).toBe('Hola')
    expect(values.descriptionEn).toBe('Hi')
  })

  it('sin `descriptions` (o con solo un idioma), el que falta queda vacío: no se rellena con el del otro', () => {
    const values = definitionToFormValues(def({ description: 'Hola', descriptions: { es: 'Hola' } }), 'indicator')
    expect(values.descriptionEs).toBe('Hola')
    expect(values.descriptionEn).toBe('')
  })

  it('reenvía la definición completa cambiando solo showInPulse, sin tocar descripción ni filtro (el PUT es un upsert, no un PATCH)', () => {
    const dto = def({
      id: 7,
      name: 'Productos bajo mínimo',
      dataSource: 'PRODUCT',
      aggregateFn: 'COUNT',
      businessModule: 'WAREHOUSE',
      visibility: 'TENANT',
      showInPulse: false,
      descriptions: { es: 'Descripción es', en: 'Description en' },
      filterJson: JSON.stringify({ and: [{ field: 'belowMin', op: 'isTrue' }] }),
    })
    const body = withShowInPulse(dto, 'indicator', true)
    expect(body.name).toBe('Productos bajo mínimo')
    expect(body.dataSource).toBe('PRODUCT')
    expect(body.showInPulse).toBe(true)
    expect(body.descriptions).toBeNull() // no se toca: el API conserva la descripción actual en ambos idiomas
    expect(JSON.parse(body.filterJson ?? '')).toEqual({ and: [{ field: 'belowMin', op: 'isTrue' }] })
  })

  it('un filtro con `or`/`not` (que este editor no arma con filas) se reenvía intacto, no se borra', () => {
    const dto = def({ id: 8, dataSource: 'ORDERS', aggregateFn: 'COUNT', filterJson: JSON.stringify({ or: [{ field: 'a', op: 'eq', value: 1 }, { field: 'b', op: 'eq', value: 2 }] }) })
    const body = withShowInPulse(dto, 'indicator', true)
    expect(JSON.parse(body.filterJson ?? '')).toEqual({ or: [{ field: 'a', op: 'eq', value: 1 }, { field: 'b', op: 'eq', value: 2 }] })
  })

  it('para un gráfico conserva groupByField y chartType', () => {
    const dto = def({ dataSource: 'ORDERS', groupByField: 'status', chartType: 'BAR', aggregateFn: 'COUNT' })
    const body = withShowInPulse(dto, 'chart', true)
    expect(body.groupByField).toBe('status')
    expect(body.chartType).toBe('BAR')
  })
})

describe('filterIsUnrepresentable', () => {
  it('false sin filtro o con una lista plana de condiciones (arreglo o {and:[...]})', () => {
    expect(filterIsUnrepresentable(null)).toBe(false)
    expect(filterIsUnrepresentable(JSON.stringify({ and: [{ field: 'a', op: 'eq', value: 1 }] }))).toBe(false)
    expect(filterIsUnrepresentable(JSON.stringify([{ field: 'a', op: 'eq', value: 1 }]))).toBe(false)
  })

  it('true con `or`/`not`, o con JSON inválido', () => {
    expect(filterIsUnrepresentable(JSON.stringify({ or: [{ field: 'a', op: 'eq', value: 1 }] }))).toBe(true)
    expect(filterIsUnrepresentable(JSON.stringify({ not: { field: 'a', op: 'eq', value: 1 } }))).toBe(true)
    expect(filterIsUnrepresentable('{not json')).toBe(true)
  })
})

describe('visibilityBadgeKey', () => {
  it('TENANT → all, SHARED → shared, cualquier otro (o nada) → private', () => {
    expect(visibilityBadgeKey('TENANT')).toBe('all')
    expect(visibilityBadgeKey('SHARED')).toBe('shared')
    expect(visibilityBadgeKey('PRIVATE')).toBe('private')
    expect(visibilityBadgeKey(null)).toBe('private')
  })
})
