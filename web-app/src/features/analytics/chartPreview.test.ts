// Fase 10b — vista previa del editor de gráficos: petición al endpoint de vista previa de vistas y réplica de
// `AnalyticsEngine.EvaluateChartAsync` (top 8 con "Otras" / últimos 30, agrupación por día local, rango personalizado como filtro).
import { describe, expect, it } from 'vitest'
import { aggregateValues, chartPreviewPoints, foldOthers, nextDay, OTHERS_KEY, planChartPreview, withDateBounds, type ChartPreviewInput } from './chartPreview'
import type { DataSource } from './definitions'

const SOURCE: DataSource = {
  key: 'TRANSPORT_ORDER',
  dateField: 'CreatedAt',
  fields: [
    { key: 'Status', type: 'Text' },
    { key: 'CodAmount', type: 'Number', isMoney: true },
    { key: 'CreatedAt', type: 'Date' },
  ],
  customFields: [],
}

function input(over: Partial<ChartPreviewInput> = {}): ChartPreviewInput {
  return {
    source: SOURCE,
    groupByField: 'Status',
    aggregateFn: 'COUNT',
    field: '',
    chartType: 'BAR',
    dateRangeMode: 'LAST30',
    dateFrom: '',
    dateTo: '',
    filterJson: null,
    ...over,
  }
}

describe('planChartPreview', () => {
  it('incompleto: sin fuente, sin agrupar, SUM sin campo o CUSTOM sin fechas válidas', () => {
    expect(planChartPreview(input({ source: undefined }))).toBeNull()
    expect(planChartPreview(input({ groupByField: '' }))).toBeNull()
    expect(planChartPreview(input({ aggregateFn: 'SUM', field: '' }))).toBeNull()
    expect(planChartPreview(input({ dateRangeMode: 'CUSTOM', dateFrom: '2026-09-01' }))).toBeNull()
    expect(planChartPreview(input({ dateRangeMode: 'CUSTOM', dateFrom: '2026-09-10', dateTo: '2026-09-01' }))).toBeNull()
  })

  it('barra sobre un campo de texto con SUM: agrupa en el servidor, ordena por el agregado y trae TODOS los grupos (para "Otras")', () => {
    const req = planChartPreview(input({ aggregateFn: 'SUM', field: 'CodAmount' }))
    expect(req).not.toBeNull()
    expect(req?.baseEntityType).toBe('TRANSPORT_ORDER')
    expect(req?.byDay).toBe(false)
    expect(req?.foldOthers).toBe(true)
    expect(req?.query).toEqual({ dateRangeMode: 'LAST30', take: 5000 })
    expect(JSON.parse(req?.body.groupJson ?? '')).toEqual({ by: ['Status'], aggregates: [{ fn: 'SUM', field: 'CodAmount' }] })
    expect(JSON.parse(req?.body.sortJson ?? '')).toEqual([{ field: 'sum_CodAmount', dir: 'desc' }])
    expect(req?.body.columns).toEqual(['Status'])
  })

  it('con AVG/MIN/MAX no hay "Otras": los 8 mayores (take 8); en línea tampoco', () => {
    const avg = planChartPreview(input({ aggregateFn: 'AVG', field: 'CodAmount', chartType: 'DONUT' }))
    expect(avg?.foldOthers).toBe(false)
    expect(avg?.query.take).toBe(8)
    expect(planChartPreview(input({ chartType: 'LINE' }))?.foldOthers).toBe(false)
    expect(planChartPreview(input({ chartType: 'DONUT' }))?.foldOthers).toBe(true)
  })

  it('COUNT no manda campo (clave count_rows); sin modo elegido usa LAST7', () => {
    const req = planChartPreview(input({ field: 'CodAmount', dateRangeMode: '' }))
    expect(req?.field).toBeNull()
    expect(req?.aggKey).toBe('count_rows')
    expect(req?.query.dateRangeMode).toBe('LAST7')
  })

  it('línea sobre un campo de texto: los 30 últimos por el valor del campo', () => {
    const req = planChartPreview(input({ chartType: 'LINE' }))
    expect(req?.query.take).toBe(30)
    expect(JSON.parse(req?.body.sortJson ?? '')).toEqual([{ field: 'Status', dir: 'desc' }])
  })

  it('agrupar por el campo de fecha: filas sin agrupar (5 000) para agrupar por día aquí', () => {
    const req = planChartPreview(input({ groupByField: 'CreatedAt', aggregateFn: 'AVG', field: 'CodAmount' }))
    expect(req?.byDay).toBe(true)
    expect(req?.query.take).toBe(5000)
    expect(req?.body.groupJson).toBeUndefined()
    expect(req?.body.columns).toEqual(['CreatedAt', 'CodAmount'])
  })

  it('rango personalizado: ALL + límites en el filtro (Hasta exclusivo al día siguiente)', () => {
    const req = planChartPreview(
      input({ dateRangeMode: 'CUSTOM', dateFrom: '2026-09-01', dateTo: '2026-09-30', filterJson: '{"and":[{"field":"Status","op":"eq","value":"OPEN"}]}' }),
    )
    expect(req?.query.dateRangeMode).toBe('ALL')
    expect(JSON.parse(req?.body.filterJson ?? '')).toEqual({
      and: [
        { and: [{ field: 'Status', op: 'eq', value: 'OPEN' }] },
        { field: 'CreatedAt', op: 'gte', value: '2026-09-01' },
        { field: 'CreatedAt', op: 'lt', value: '2026-10-01' },
      ],
    })
  })

  it('fuente sin campo de fecha: rango ALL', () => {
    const req = planChartPreview(input({ source: { ...SOURCE, dateField: null }, dateRangeMode: 'CUSTOM' }))
    expect(req?.query.dateRangeMode).toBe('ALL')
  })
})

describe('utilidades', () => {
  it('nextDay cruza mes y año', () => {
    expect(nextDay('2026-12-31')).toBe('2027-01-01')
    expect(nextDay('2028-02-28')).toBe('2028-02-29')
  })

  it('withDateBounds sin filtro previo', () => {
    expect(JSON.parse(withDateBounds(null, 'D', '2026-01-01', '2026-01-02'))).toEqual({
      and: [
        { field: 'D', op: 'gte', value: '2026-01-01' },
        { field: 'D', op: 'lt', value: '2026-01-03' },
      ],
    })
  })

  it('aggregateValues replica el motor', () => {
    const rows = [{ v: 2 }, { v: '4' }, { v: null }, {}]
    expect(aggregateValues(rows, 'COUNT', null)).toBe(4)
    expect(aggregateValues(rows, 'COUNT', 'v')).toBe(2)
    expect(aggregateValues(rows, 'SUM', 'v')).toBe(6)
    expect(aggregateValues(rows, 'AVG', 'v')).toBe(3)
    expect(aggregateValues(rows, 'MIN', 'v')).toBe(2)
    expect(aggregateValues(rows, 'MAX', 'v')).toBe(4)
    expect(aggregateValues([{ v: null }], 'SUM', 'v')).toBe(0)
    expect(aggregateValues([{ v: null }], 'AVG', 'v')).toBeNull()
  })
})

describe('chartPreviewPoints', () => {
  it('agrupado en el servidor: etiqueta y valor; vacío = "—"; booleano como .NET', () => {
    const req = planChartPreview(input())!
    const res = chartPreviewPoints({ rows: [{ Status: 'OPEN', count_rows: 5 }, { Status: null, count_rows: 2 }, { Status: true, count_rows: 1 }], total: 3 }, req)
    expect(res.points).toEqual([
      { label: 'OPEN', value: 5 },
      { label: '—', value: 2 },
      { label: 'True', value: 1 },
    ])
    expect(res.truncated).toBe(false)
  })

  it('línea agrupada en el servidor: se invierte a orden ascendente', () => {
    const req = planChartPreview(input({ chartType: 'LINE' }))!
    const res = chartPreviewPoints({ rows: [{ Status: 'C', count_rows: 1 }, { Status: 'B', count_rows: 2 }, { Status: 'A', count_rows: 3 }] }, req)
    expect(res.points.map((p) => p.label)).toEqual(['A', 'B', 'C'])
  })

  it('por día LOCAL de Puerto Rico (Lote 15): agrupa, agrega, ordena de mayor a menor y avisa si se truncó', () => {
    const req = planChartPreview(input({ groupByField: 'CreatedAt', aggregateFn: 'AVG', field: 'CodAmount' }))!
    const rows = [
      // instantes UTC sin zona (como el API): 08:00Z y 23:30Z del 1 son el 1 en PR; 01:00Z del 2 es el 1 a las 21:00 en PR
      { CreatedAt: '2026-09-01T08:00:00', CodAmount: 10 },
      { CreatedAt: '2026-09-01T23:30:00', CodAmount: 5 },
      { CreatedAt: '2026-09-02T01:00:00', CodAmount: 45 },
      // 04:00Z del 2 ya es el 2 en PR
      { CreatedAt: '2026-09-02T04:00:00Z', CodAmount: 40 },
      { CreatedAt: null, CodAmount: 1 },
    ]
    const res = chartPreviewPoints({ rows, total: 9000 }, req)
    expect(res.points).toEqual([
      { label: '2026-09-02', value: 40 },
      { label: '2026-09-01', value: 20 },
      { label: '—', value: 1 },
    ])
    expect(res.truncated).toBe(true)
  })

  it('por día: un día de calendario (DateOnly del API, "2026-09-30") se agrupa tal cual, sin correrlo', () => {
    const req = planChartPreview(input({ groupByField: 'CreatedAt', chartType: 'LINE' }))!
    const res = chartPreviewPoints({ rows: [{ CreatedAt: '2026-09-30' }, { CreatedAt: '2026-09-30' }, { CreatedAt: '2026-09-29' }], total: 3 }, req)
    expect(res.points).toEqual([
      { label: '2026-09-29', value: 1 },
      { label: '2026-09-30', value: 2 },
    ])
  })

  it('por día en línea: sin fecha primero, luego cronológico (días locales)', () => {
    const req = planChartPreview(input({ groupByField: 'CreatedAt', chartType: 'LINE' }))!
    const rows = [{ CreatedAt: '2026-09-03T04:00:00' }, { CreatedAt: '2026-09-01T04:00:00' }, { CreatedAt: null }, { CreatedAt: '2026-09-01T10:00:00' }, { CreatedAt: '2026-09-01T03:59:00' }]
    const res = chartPreviewPoints({ rows, total: 5 }, req)
    expect(res.points).toEqual([
      { label: '—', value: 1 },
      { label: '2026-08-31', value: 1 },
      { label: '2026-09-01', value: 2 },
      { label: '2026-09-03', value: 1 },
    ])
  })

  it('"Otras" (D11): con más de 8 grupos, los 7 mayores y "Otras" con la suma exacta del resto (agrupado en el servidor)', () => {
    const req = planChartPreview(input({ aggregateFn: 'SUM', field: 'CodAmount', chartType: 'DONUT' }))!
    // el servidor devuelve TODOS los grupos, de mayor a menor
    const rows = [90, 80, 70, 60, 50, 40, 30, 20, 10, 5].map((v, i) => ({ Status: `S${i + 1}`, sum_CodAmount: v }))
    const res = chartPreviewPoints({ rows, total: 10 }, req, 'Others')
    expect(res.points).toEqual([
      { label: 'S1', value: 90 },
      { label: 'S2', value: 80 },
      { label: 'S3', value: 70 },
      { label: 'S4', value: 60 },
      { label: 'S5', value: 50 },
      { label: 'S6', value: 40 },
      { label: 'S7', value: 30 },
      { label: 'Others', key: OTHERS_KEY, value: 35 },
    ])
    // la suma de los puntos es el total
    expect(res.points.reduce((s, p) => s + (p.value ?? 0), 0)).toBe(455)
    expect(res.truncated).toBe(false)
  })

  it('"Otras": con 8 grupos o menos, sin cambios; por día también se junta; etiqueta por defecto "Otras"', () => {
    const req = planChartPreview(input({ aggregateFn: 'COUNT' }))!
    const eight = Array.from({ length: 8 }, (_, i) => ({ Status: `S${i}`, count_rows: 8 - i }))
    expect(chartPreviewPoints({ rows: eight, total: 8 }, req).points).toHaveLength(8)
    const byDay = planChartPreview(input({ groupByField: 'CreatedAt', aggregateFn: 'COUNT' }))!
    const rows = Array.from({ length: 9 }, (_, i) => ({ CreatedAt: `2026-09-0${i + 1}T12:00:00` }))
    const res = chartPreviewPoints({ rows, total: 9 }, byDay)
    expect(res.points).toHaveLength(8)
    expect(res.points[7]).toEqual({ label: 'Otras', key: OTHERS_KEY, value: 2 })
  })

  it('foldOthers replica FoldOthers del motor', () => {
    expect(foldOthers([{ label: 'a', value: 1 }, { label: 'b', value: 3 }], 8, 'Otras')).toEqual([
      { label: 'b', value: 3 },
      { label: 'a', value: 1 },
    ])
    const nine = Array.from({ length: 9 }, (_, i) => ({ label: `L${i}`, value: i + 1 }))
    const folded = foldOthers(nine, 8, 'Otras')
    expect(folded.map((p) => p.label)).toEqual(['L8', 'L7', 'L6', 'L5', 'L4', 'L3', 'L2', 'Otras'])
    expect(folded[7].value).toBe(3)
  })
})
