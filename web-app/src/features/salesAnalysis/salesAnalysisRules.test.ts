// Lote 241 — reglas puras del Análisis de ventas: meses, tonos (cobertura, ABC, estado, cumplimiento), filtros, columnas del
// Resumen, series de gráficos, avisos de estado, rejilla de cuotas y el reporte de importación.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import {
  abcTone,
  addMonths,
  changeTone,
  classRows,
  collapseShares,
  columnsOfGroup,
  complianceLevel,
  complianceWidth,
  copyPreviousMonth,
  coverageLevel,
  coveragePoints,
  filterMovement,
  filterRepProducts,
  filterRouteProducts,
  heatLevel,
  heatMix,
  lastCompleteMonth,
  lineShareFromLines,
  matrixColumnTotals,
  monthLong,
  monthOptions,
  monthRange,
  monthRows,
  monthShort,
  parseQuota,
  prevMonth,
  quotaCell,
  quotaChanges,
  quotaDraftFrom,
  quotaMonthTotal,
  statusIssues,
  statusKey,
  statusTone,
  topProductsBySales,
  topUnitsSeries,
  totalsMatch,
  EMPTY_MOVEMENT_FILTER,
  EMPTY_PRODUCT_FILTER,
  HEAT_MAX_MIX,
  MOVEMENT_COLUMNS,
  type ProductMovementRow,
  type SalesQuotaMonth,
} from './salesAnalysisRules'
import { formatCompactMoney, formatPct, formatSignedPct } from './saFormat'

beforeAll(() => setLang('es'))

describe('meses', () => {
  it('suma y resta meses cruzando el año', () => {
    expect(addMonths('2026-01', -1)).toBe('2025-12')
    expect(addMonths('2025-12', 1)).toBe('2026-01')
    expect(addMonths('2026-10', -13)).toBe('2025-09')
    expect(prevMonth('2026-10')).toBe('2026-09')
    expect(addMonths('raro', 1)).toBe('raro')
  })
  it('último mes completo, rango y opciones', () => {
    expect(lastCompleteMonth('2026-11-12')).toBe('2026-10')
    expect(monthRange('2025-12', '2026-03')).toEqual(['2025-12', '2026-01', '2026-02', '2026-03'])
    expect(monthRange('2026-03', '2026-01')).toEqual([])
    expect(monthOptions('2026-11-12', 3)).toEqual(['2026-11', '2026-10', '2026-09'])
  })
  it('nombres como los encabezados del Excel del dueño', () => {
    expect(monthShort('2026-10', 'es')).toBe('oct-26')
    expect(monthShort('2025-12', 'en')).toBe('dec-25')
    expect(monthLong('2026-10', 'es')).toMatch(/octubre.*2026/)
    expect(monthLong('2026-10', 'en')).toBe('October 2026')
  })
})

describe('tonos', () => {
  it('cobertura: rojo poco, ámbar, verde mucho; sin promedio = sin base', () => {
    expect(coverageLevel(null)).toBe('none')
    expect(coverageLevel(undefined)).toBe('none')
    expect(coverageLevel(0)).toBe('low')
    expect(coverageLevel(1.99)).toBe('low')
    expect(coverageLevel(2)).toBe('mid')
    expect(coverageLevel(3.9)).toBe('mid')
    expect(coverageLevel(4)).toBe('high')
    expect(coverageLevel(52)).toBe('high')
  })
  it('ABC y estado con su chip', () => {
    expect([abcTone('A'), abcTone('B'), abcTone('C'), abcTone('-'), abcTone(null)]).toEqual(['deliv', 'disp', 'warn', 'cap', 'cap'])
    expect(statusTone('STALE')).toBe('fail')
    expect(statusTone('RISING')).toBe('deliv')
    expect(statusTone('FALLING')).toBe('warn')
    expect(statusTone('NEW')).toBe('wh')
    expect(statusTone('OTRO')).toBe('cap')
    expect(statusKey('NO_SALES')).toBe('salesAnalysis.status.NO_SALES')
    expect(statusKey('OTRO')).toBeNull()
  })
  it('cumplimiento de cuota (semáforo) y ancho de la barra', () => {
    expect(complianceLevel(null)).toBe('none')
    expect(complianceLevel(1.0148)).toBe('met')
    expect(complianceLevel(1)).toBe('met')
    expect(complianceLevel(0.9)).toBe('near')
    expect(complianceLevel(0.574)).toBe('below')
    expect(complianceWidth(0.574)).toBe(57.4)
    expect(complianceWidth(1.5)).toBe(100)
    expect(complianceWidth(-1)).toBe(0)
    expect(changeTone(5396.53)).toBe('up')
    expect(changeTone(-0.5)).toBe('down')
    expect(changeTone(0.001)).toBe('flat')
  })
  it('escala de color de la matriz con tope para el contraste', () => {
    expect(heatLevel(0, 100)).toBe(0)
    expect(heatLevel(50, 100)).toBe(0.5)
    expect(heatLevel(200, 100)).toBe(1)
    expect(heatMix(1)).toBe(HEAT_MAX_MIX)
    expect(heatMix(0.5)).toBe(Math.round(HEAT_MAX_MIX / 2))
  })
})

const P = (o: Partial<ProductMovementRow>): ProductMovementRow => ({ productId: 1, sku: 'X', name: 'X', units: 0, sales: 0, available: 0, ...o })

describe('filtros y columnas', () => {
  const repProducts = [
    { routeName: 'Norte', repId: 2, repName: 'CINDY', line: 'Global Imports', productId: 3, sku: '00614-1', product: 'GLOBAL COTTON ROUND 80CT.', units: 60, sales: 65.4 },
    { routeName: 'Cadenas', repId: 1, repName: 'ADVANCE', line: 'Prodigy', productId: 5, sku: '52800', product: 'PRODIGY STRIPS', units: 100, sales: 400 },
    { routeName: undefined, repId: undefined, repName: 'Sin representante', line: 'Prodigy', productId: 5, sku: '52800', product: 'PRODIGY STRIPS', units: 5, sales: 0 },
  ]
  it('producto por representante: ruta, representante (también «sin representante»), línea y buscador', () => {
    expect(filterRepProducts(repProducts, { ...EMPTY_PRODUCT_FILTER, route: 'Norte' }).map((r) => r.sku)).toEqual(['00614-1'])
    expect(filterRepProducts(repProducts, { ...EMPTY_PRODUCT_FILTER, rep: 'none' }).map((r) => r.repName)).toEqual(['Sin representante'])
    expect(filterRepProducts(repProducts, { ...EMPTY_PRODUCT_FILTER, line: 'Prodigy', q: 'advance' })).toHaveLength(1)
    expect(filterRepProducts(repProducts, { ...EMPTY_PRODUCT_FILTER, q: 'cotton round' })).toHaveLength(1)
  })
  it('producto por ruta: el representante se busca en los nombres agrupados', () => {
    const rows = [
      { routeId: 2, routeName: 'Norte', repNames: 'CINDY, PEDRO', line: 'Prodigy', sku: '1', product: 'A', units: 0, sales: 0 },
      { routeId: 3, routeName: 'Sur', repNames: 'RADAMES', line: 'Prodigy', sku: '1', product: 'A', units: 4, sales: 10 },
    ]
    expect(filterRouteProducts(rows, { ...EMPTY_PRODUCT_FILTER, rep: '9' }, 'PEDRO').map((r) => r.routeName)).toEqual(['Norte'])
    expect(filterRouteProducts(rows, EMPTY_PRODUCT_FILTER)).toHaveLength(2)
  })
  it('grupos de columnas del Resumen: SKU y nombre siempre, en el orden del Excel', () => {
    expect(columnsOfGroup('sales').slice(0, 2)).toEqual(['sku', 'name'])
    expect(columnsOfGroup('reorder')).toContain('suggestedReorderPoint')
    expect(columnsOfGroup('all')).toEqual([...MOVEMENT_COLUMNS])
    expect(columnsOfGroup('custom', ['trend', 'abc'])).toEqual(['sku', 'name', 'abc', 'trend'])
  })
  it('filtro del Resumen y los principales por venta $', () => {
    const rows = [P({ productId: 1, sku: 'A', abc: 'A', status: 'STABLE', line: 'Prodigy', sales: 100 }), P({ productId: 2, sku: 'B', abc: 'C', status: 'STALE', line: 'Padimont', sales: 5 }), P({ productId: 3, sku: 'C', abc: '-', status: 'NO_SALES', sales: 0 })]
    expect(filterMovement(rows, { ...EMPTY_MOVEMENT_FILTER, status: 'STALE' }).map((r) => r.sku)).toEqual(['B'])
    expect(filterMovement(rows, { ...EMPTY_MOVEMENT_FILTER, abc: '-' }).map((r) => r.sku)).toEqual(['C'])
    expect(topProductsBySales(rows, 5).map((r) => r.sku)).toEqual(['A', 'B'])
  })
})

describe('series de los gráficos', () => {
  it('filas por mes con una clave por serie', () => {
    expect(monthRows(['2026-09', '2026-10'], [{ values: [1, 2] }, { values: [3] }], 'es')).toEqual([
      { month: '2026-09', label: 'sep-26', s0: 1, s1: 3 },
      { month: '2026-10', label: 'oct-26', s0: 2, s1: 0 },
    ])
  })
  it('participación sin descuentos y «Otras» cuando son muchas', () => {
    const pts = lineShareFromLines([
      { line: 'Prodigy', sales: 134341.2, shareOfGross: 0.9117 },
      { line: 'Descuentos sin línea', isDiscounts: true, sales: -5457.17 },
      { line: 'Padimont', sales: 7950.54, shareOfGross: 0.054 },
      { line: 'Vacía', sales: 0 },
    ])
    expect(pts.map((p) => p.name)).toEqual(['Prodigy', 'Padimont'])
    const many = [5, 4, 3, 2, 1].map((v, i) => ({ name: `L${i}`, value: v, share: v / 15 }))
    const c = collapseShares(many, 3, 'Otras')
    expect(c.map((p) => p.name)).toEqual(['L0', 'L1', 'Otras'])
    expect(c[2].value).toBe(6)
  })
  it('cobertura: solo con promedio, de menor a mayor, con tope', () => {
    const pts = coveragePoints(
      [P({ productId: 1, sku: 'A', coverageWeeks: 7.9 }), P({ productId: 2, sku: 'B', coverageWeeks: 1.2 }), P({ productId: 3, sku: 'C', coverageWeeks: null }), P({ productId: 4, sku: 'D', coverageWeeks: 4.6 })],
      2,
    )
    expect(pts.map((p) => [p.label, p.level])).toEqual([
      ['B', 'low'],
      ['D', 'high'],
    ])
  })
  it('unidades por mes de los principales desde las matrices', () => {
    const series = topUnitsSeries(
      {
        products: [P({ productId: 1, sku: '52800', sales: 1000 }), P({ productId: 2, sku: '070120', sales: 500 }), P({ productId: 3, sku: 'Z', sales: 0 })],
        unitsByMonth: [
          { productId: 2, sku: '070120', values: [188, 2346] },
          { productId: 1, sku: '52800', values: [1431, 21630] },
        ],
      },
      6,
    )
    expect(series).toEqual([
      { key: '52800', label: '52800', values: [1431, 21630] },
      { key: '070120', label: '070120', values: [188, 2346] },
    ])
    expect(matrixColumnTotals([{ values: [1, 2] }, { values: [3, 4.005] }], 2)).toEqual([4, 6.01])
  })
})

describe('aviso de estado', () => {
  it('lista lo que falta con la sección donde se arregla', () => {
    const issues = statusIssues({
      clientsWithoutRep: 3,
      brandsWithoutLine: ['YARDLEY'],
      unmatchedClients: [{ raw: 'X' }],
      unmatchedItems: [],
      unmatchedPurchaseItems: [{ raw: '53310/53350' }],
      batches: [{ isActive: true }],
      cutoffDate: null,
      overlap: false,
    })
    expect(issues).toEqual([
      { key: 'clientsWithoutRep', count: 3, section: 'clients' },
      { key: 'brandsWithoutLine', count: 1, section: 'lines' },
      { key: 'unmatched', count: 2, section: 'link' },
      { key: 'noCutoff', count: 0, section: 'settings' },
    ])
  })
  it('sin historial pide importarlo; el traslape gana a «sin fecha de corte»; todo bien = nada', () => {
    expect(statusIssues({ batches: [], overlap: false }).map((i) => i.key)).toEqual(['noHistory'])
    expect(statusIssues({ batches: [{ isActive: true }], overlap: true }).map((i) => i.key)).toEqual(['overlap'])
    expect(statusIssues({ batches: [{ isActive: true }], cutoffDate: '2026-11-16', overlap: false })).toEqual([])
    expect(statusIssues(undefined)).toEqual([])
  })
})

describe('rejilla de cuotas', () => {
  const months: SalesQuotaMonth[] = [
    { month: '2026-09', rows: [{ repId: 1, repName: 'A', amount: 57700, effective: 57700 }, { repId: 2, repName: 'B', amount: null, effective: 16000, effectiveFrom: '2026-08' }] },
    { month: '2026-10', rows: [{ repId: 1, repName: 'A', amount: null, effective: 57700, effectiveFrom: '2026-09' }, { repId: 2, repName: 'B', amount: 16500, effective: 16500 }] },
  ]
  it('lee montos: vacío = quitar, inválido = undefined', () => {
    expect(parseQuota('')).toBeNull()
    expect(parseQuota(' 1,250.50 ')).toBe(1250.5)
    expect(parseQuota('$300')).toBe(300)
    expect(parseQuota('-5')).toBeUndefined()
    expect(parseQuota('1.234')).toBeUndefined()
    expect(parseQuota('abc')).toBeUndefined()
  })
  it('borrador con la cuota propia y cambios por mes (solo lo que cambió; vacío = quitar)', () => {
    const draft = quotaDraftFrom(months)
    expect(draft[quotaCell(1, '2026-09')]).toBe('57700')
    expect(draft[quotaCell(1, '2026-10')]).toBe('')
    expect(quotaChanges(months, draft)).toEqual({ changes: [], invalid: [] })
    const edited = { ...draft, [quotaCell(1, '2026-10')]: '60000', [quotaCell(2, '2026-10')]: '', [quotaCell(2, '2026-09')]: 'x' }
    expect(quotaChanges(months, edited)).toEqual({
      changes: [{ month: '2026-10', items: [{ repId: 1, amount: 60000 }, { repId: 2, amount: null }] }],
      invalid: [quotaCell(2, '2026-09')],
    })
  })
  it('«copiar del mes anterior» pone lo que VALE el mes de antes (propia o heredada) y el total del mes', () => {
    const draft = { ...quotaDraftFrom(months), [quotaCell(1, '2026-09')]: '58000' }
    const copied = copyPreviousMonth(draft, months, '2026-10')
    expect(copied[quotaCell(1, '2026-10')]).toBe('58000')
    expect(copied[quotaCell(2, '2026-10')]).toBe('16000')
    expect(copyPreviousMonth(draft, months, '2026-09')).toBe(draft)
    expect(quotaMonthTotal(quotaDraftFrom(months), months[1])).toBe(57700 + 16500)
  })
})

describe('reporte de importación y formatos', () => {
  it('clases en orden y total contra el TOTAL del reporte', () => {
    const file = { linesByClass: { DISCOUNT: 1795, PRODUCT: 11542, RARO: 1 }, amountByClass: { PRODUCT: 1354427.03, DISCOUNT: -68634.37 }, totalAmount: 1285918.96, reportTotal: 1285918.96 }
    expect(classRows(file).map((c) => c.cls)).toEqual(['PRODUCT', 'DISCOUNT', 'RARO'])
    expect(totalsMatch(file)).toBe(true)
    expect(totalsMatch({ ...file, reportTotal: 1285918.9 })).toBe(false)
  })
  it('porcentajes y moneda compacta con los ajustes de la compañía', () => {
    expect(formatPct(0.4755)).toBe('47.6%')
    expect(formatPct(null)).toBe('—')
    expect(formatSignedPct(0.3012)).toBe('+30.1%')
    expect(formatSignedPct(-0.2348)).toBe('-23.5%')
    expect(formatSignedPct(0)).toBe('0.0%')
    expect(formatCompactMoney(58866.41)).toMatch(/^\$58\.9K$/)
  })
})
