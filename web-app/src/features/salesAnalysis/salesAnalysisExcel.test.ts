// Lote 241 — exportar a Excel: los dos libros llevan las hojas del dueño con sus nombres, encabezados, formatos y fórmulas de
// totales (se escribe el libro y se vuelve a leer con SheetJS, como lo abriría Excel).
import * as XLSX from 'xlsx'
import { describe, expect, it } from 'vitest'
import { translate } from '../../kernel/i18n/i18n'
import { buildByRepWorkbook, buildMovementWorkbook, col, excelDay, FMT, type TFn } from './salesAnalysisExcel'
import type { ProductMovement, SalesByRep } from './salesAnalysisRules'

const t: TFn = (key, params) => translate('es', key, params)

const BY_REP: SalesByRep = {
  month: '2026-10',
  compareMonth: '2026-09',
  cutoffDate: '2026-11-06',
  warnings: [],
  reps: [
    { routeId: 1, routeName: 'Cadenas', repId: 1, repName: 'ADVANCE SOLUTIONS GROUP LLC', compareSales: 60773, sales: 58866.41, change: -1906.59, changePct: -0.0314, quota: 57700, compliance: 1.0202 },
    { routeId: 2, routeName: 'Norte', repId: 2, repName: 'CINDY OLIVAS DAVILA', compareSales: 11348.22, sales: 16744.75, change: 5396.53, changePct: 0.4755, quota: 16500, compliance: 1.0148 },
  ],
  totals: { compareSales: 72121.22, sales: 75611.16, change: 3489.94, changePct: 0.0484, quota: 74200, compliance: 1.019 },
  reconciliation: { routesTotal: 75611.16, unassigned: -23.16, shippingAndOther: 12, monthTotal: 75600, difference: 0, productSales: 80000, discounts: -4412, shipping: 12, other: 0, unassignedClients: 0, unassignedCompare: 0 },
  lines: [
    { line: 'Prodigy', isDiscounts: false, sales: 70000, shareOfNet: 0.926, shareOfGross: 0.875, leaderRepId: 1, leaderName: 'ADVANCE SOLUTIONS GROUP LLC', leaderRoute: 'Cadenas', leaderSales: 55000 },
    { line: 'Descuentos sin línea', isDiscounts: true, sales: -4412, shareOfNet: -0.058, shareOfGross: -0.055 },
  ],
  repLines: [
    { routeName: 'Cadenas', repId: 1, repName: 'ADVANCE SOLUTIONS GROUP LLC', line: 'Prodigy', sales: 58866.41, shareOfRep: 1 },
    { routeName: 'Norte', repId: 2, repName: 'CINDY OLIVAS DAVILA', line: 'Prodigy', sales: 16744.75, shareOfRep: 1 },
  ],
  repProducts: [{ routeName: 'Norte', repId: 2, repName: 'CINDY OLIVAS DAVILA', line: 'Global Imports', productId: 3, sku: '00614-1', product: 'GLOBAL COTTON ROUND 80CT.', lineClass: 'PRODUCT', units: 60, sales: 65.4 }],
  routeProducts: [
    { routeId: 2, routeName: 'Norte', repNames: 'CINDY OLIVAS DAVILA', line: 'Global Imports', productId: 21, sku: 'ARC609517', product: 'AMERICAN RED CROSS', units: 12, sales: 16.92 },
    { routeId: 1, routeName: 'Cadenas', repNames: 'ADVANCE SOLUTIONS GROUP LLC', line: 'Global Imports', productId: 21, sku: 'ARC609517', product: 'AMERICAN RED CROSS', units: 0, sales: 0 },
  ],
}

const MOVEMENT: ProductMovement = {
  fromMonth: '2026-09',
  toMonth: '2026-11',
  asOf: '2026-11-05',
  lastCompleteMonth: '2026-10',
  weeksFrom: '2026-09-07',
  weeksTo: '2026-11-01',
  leadTimeDays: 5,
  serviceLevelZ: 1.645,
  months: ['2026-09', '2026-10', '2026-11'],
  totalUnits: 3000,
  totalSales: 15000,
  products: [
    { productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large 1/100ct.', category: 'GLOVES', brand: 'PADIMONT', line: 'Padimont', matched: true, abc: 'B', status: 'STABLE', price: 5, cost: 3.5, marginPct: 0.3, onHand: 835, reserved: 0, available: 835, units: 1015, sales: 5013.95, shareOfSales: 0.0104, monthlyAverage: 450, monthlyMin: 440, monthlyMax: 460, bestMonth: '2026-09', weeklyAverage: 106.25, coverageWeeks: 7.9, coverageMonths: 1.8, projected1: 373, projected2: 0, projected3: 0, reorderPoint: 140, suggestedReorderPoint: 132, trend: -0.2348, clients: 152, invoices: 215, topClient: 'Farmacia Deborah #2', topClientUnits: 240, lastSale: '2026-11-05', pendingPurchase: 0 },
    { productId: 5, sku: '52800', name: 'PRODIGY STRIPS 1/50CT.', abc: 'A', status: 'RISING', units: 2000, sales: 9986, onHand: 24130, available: 24130, lastSale: '2026-11-04' },
  ],
  unitsByMonth: [
    { productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large 1/100ct.', values: [460, 440, 115], total: 1015, min: 440, max: 460, average: 450 },
    { productId: 5, sku: '52800', name: 'PRODIGY STRIPS 1/50CT.', values: [0, 1500, 500], total: 2000, min: 1500, max: 1500, average: 1500 },
  ],
  salesByMonth: [{ productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large 1/100ct.', values: [2353.85, 2219.1, 441], total: 5013.95 }],
  purchasesByMonth: [{ productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large 1/100ct.', values: [900, 0, 0], total: 900 }],
  topClients: [{ productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large 1/100ct.', rank: 1, client: 'Farmacia Deborah #2', units: 240, shareOfProduct: 0.0868 }],
}

/** Escribe el libro y lo vuelve a leer (fórmulas y formatos incluidos). */
function roundTrip(wb: XLSX.WorkBook): XLSX.WorkBook {
  const buf = XLSX.write(wb, { type: 'array', bookType: 'xlsx' })
  return XLSX.read(buf, { type: 'array', cellFormula: true, cellNF: true, cellStyles: true })
}
const cell = (ws: XLSX.WorkSheet, a: string) => ws[a] as XLSX.CellObject | undefined

describe('libro «Por representante»', () => {
  const inventory: ProductMovement = { ...MOVEMENT, fromMonth: '2026-10', toMonth: '2026-10', months: ['2026-10'] }
  const wb = roundTrip(
    buildByRepWorkbook(XLSX, {
      byRep: BY_REP,
      transactions: [{ date: '2026-10-01', source: 'HISTORY', type: 'Invoice', number: '2025-5114', clientName: 'FARMACIA CARIDAD #13', repName: 'ADVANCE SOLUTIONS GROUP LLC', routeName: 'Cadenas', sku: '52800', product: 'PRODIGY STRIPS', line: 'Prodigy', lineClass: 'PRODUCT', quantity: 160, unitPrice: 4.18, amount: 668.8 }],
      inventory,
      t,
      lang: 'es',
    }),
  )

  it('tiene las 7 hojas del Excel del dueño, en su orden', () => {
    expect(wb.SheetNames).toEqual(['Comparativo', 'Marcas', 'Representante producto', 'Ventas marca por representante', 'Variedades por ruta', 'Inventario', 'Transacciones'])
  })

  it('Comparativo: encabezados con los meses, fórmulas de cambio, cumplimiento, TOTAL RUTAS y la conciliación', () => {
    const ws = wb.Sheets.Comparativo
    expect(cell(ws, 'A1')?.v).toBe('Ventas octubre 2026 frente a septiembre')
    expect(['A3', 'B3', 'C3', 'D3', 'E3', 'F3', 'G3', 'H3'].map((a) => cell(ws, a)?.v)).toEqual(['Ruta', 'Representante', 'Septiembre $', 'Octubre $', 'Cambio $', 'Cambio %', 'Cuota $', 'Cumplimiento'])
    expect(cell(ws, 'B4')?.v).toBe('ADVANCE SOLUTIONS GROUP LLC')
    expect(cell(ws, 'D4')?.v).toBe(58866.41)
    expect(cell(ws, 'D4')?.z).toBe(FMT.money)
    expect(cell(ws, 'E5')?.f).toBe('D5-C5')
    expect(cell(ws, 'F5')?.f).toBe('IF(C5<>0,E5/C5,"")')
    expect(cell(ws, 'F5')?.z).toBe(FMT.pct2)
    expect(cell(ws, 'H5')?.f).toBe('IF(N(G5)>0,D5/G5,"")')
    expect(cell(ws, 'A6')?.v).toBe('TOTAL RUTAS')
    expect(cell(ws, 'D6')?.f).toBe('SUM(D4:D5)')
    expect(cell(ws, 'D6')?.v).toBe(75611.16)
    expect(cell(ws, 'G6')?.f).toBe('SUM(G4:G5)')
    expect(cell(ws, 'A8')?.v).toBe('Conciliación')
    expect(cell(ws, 'D9')?.f).toBe('D6')
    expect(cell(ws, 'A10')?.v).toBe('Sin representante')
    expect(cell(ws, 'D10')?.v).toBe(-23.16)
    expect(cell(ws, 'D11')?.f).toBe('D9+D10')
    expect(cell(ws, 'A13')?.v).toBe('Total contable octubre')
    expect(cell(ws, 'D13')?.f).toBe('D11+D12')
    expect(ws['!cols']?.[1]?.wch).toBe(42)
  })

  it('Marcas: participación con fórmula contra la venta neta del Comparativo y «No aplica» en los descuentos', () => {
    const ws = wb.Sheets.Marcas
    expect(cell(ws, 'A3')?.v).toBe('Distribuidor o línea')
    expect(cell(ws, 'C4')?.f).toBe('IF(Comparativo!$D$11<>0,B4/Comparativo!$D$11,0)')
    expect(cell(ws, 'E5')?.v).toBe('No aplica')
    expect(cell(ws, 'A6')?.v).toBe('Total')
    expect(cell(ws, 'B6')?.f).toBe('SUM(B4:B5)')
  })

  it('Ventas marca por representante: TOTAL REPRESENTANTE por bloque y el TOTAL del mes', () => {
    const ws = wb.Sheets['Ventas marca por representante']
    expect(cell(ws, 'C5')?.v).toBe('TOTAL REPRESENTANTE')
    expect(cell(ws, 'D5')?.f).toBe('SUM(D4:D4)')
    expect(cell(ws, 'E4')?.f).toBe('IF(D5<>0,D4/D5,0)')
    expect(cell(ws, 'C8')?.v).toBe('TOTAL OCTUBRE')
    expect(cell(ws, 'D8')?.f).toBe('D5+D7')
  })

  it('Variedades por ruta incluye los ceros y suma los productos; SKU como texto', () => {
    const ws = wb.Sheets['Variedades por ruta']
    expect(cell(ws, 'F5')?.v).toBe(0)
    expect(cell(ws, 'D4')?.t).toBe('s')
    expect(cell(ws, 'E7')?.v).toBe('Total de productos (antes de descuentos)')
    expect(cell(ws, 'G7')?.f).toBe('SUM(G4:G5)')
  })

  it('Inventario con meses de cobertura y saldos con fórmula; Transacciones con fecha de Excel y total', () => {
    const inv = wb.Sheets.Inventario
    expect(cell(inv, 'A3')?.v).toBe('SKU')
    expect(cell(inv, 'H4')?.f).toBe('IF(G4>0,F4/G4,"Sin ventas")')
    expect(cell(inv, 'I4')?.f).toBe('MAX(0,F4-G4*1)')
    const tx = wb.Sheets.Transacciones
    expect(cell(tx, 'A4')?.v).toBe(excelDay('2026-10-01'))
    expect(cell(tx, 'A4')?.z).toBe(FMT.dateIso)
    expect(cell(tx, 'N4')?.v).toBe('QuickBooks')
    expect(cell(tx, 'M5')?.f).toBe('SUM(M4:M4)')
  })
})

describe('libro «Movimiento de productos»', () => {
  const wb = roundTrip(buildMovementWorkbook(XLSX, { movement: MOVEMENT, company: 'Advance Solutions', t, lang: 'es' }))

  it('tiene las 7 hojas del Excel del dueño', () => {
    expect(wb.SheetNames).toEqual(['Resumen', 'Unidades por mes', 'Venta $ por mes', 'Compras por mes', 'Clientes por producto', 'Gráfico', 'Notas'])
  })

  it('Resumen: título, encabezados en la fila 4 y formatos (moneda, %, tendencia, fecha)', () => {
    const ws = wb.Sheets.Resumen
    expect(cell(ws, 'A1')?.v).toBe('Movimiento de productos - Advance Solutions')
    expect(cell(ws, 'A4')?.v).toBe('SKU')
    expect(cell(ws, 'F4')?.v).toBe('Clasif. ABC')
    expect(cell(ws, 'G5')?.v).toBe('Estable')
    expect(cell(ws, 'H5')?.z).toBe(FMT.moneyPlain)
    expect(cell(ws, 'J5')?.z).toBe(FMT.pct1)
    const trendCol = col(27) // 'trend' es la columna 28
    expect(cell(ws, `${trendCol}4`)?.v).toBe('Tendencia')
    expect(cell(ws, `${trendCol}5`)?.z).toBe(FMT.trend)
    const lastSale = col(32)
    expect(cell(ws, `${lastSale}5`)?.v).toBe(excelDay('2026-11-05'))
  })

  it('matrices: meses como «sep-26» y el último «(al 5)», total con SUM y estadísticas en unidades', () => {
    const ws = wb.Sheets['Unidades por mes']
    expect(['A1', 'B1', 'C1', 'D1', 'E1', 'F1', 'G1', 'H1', 'I1'].map((a) => cell(ws, a)?.v)).toEqual(['Item', 'Descripción', 'sep-26', 'oct-26', 'nov-26 (al 5)', 'Total', 'Mínimo (meses activos)', 'Máximo', 'Promedio'])
    expect(cell(ws, 'F2')?.f).toBe('SUM(C2:E2)')
    expect(cell(ws, 'F2')?.v).toBe(1015)
    expect(cell(ws, 'C3')).toBeUndefined() // mes sin venta = celda vacía, como el original
    expect(cell(wb.Sheets['Venta $ por mes'], 'F2')?.z).toBe(FMT.money0)
    expect(cell(wb.Sheets['Compras por mes'], 'F1')?.v).toBe('Total comprado')
  })

  it('Gráfico trae los datos (mes × producto principal) y Notas las definiciones', () => {
    const g = wb.Sheets['Gráfico']
    expect(cell(g, 'B3')?.v).toBe('52800')
    expect(cell(g, 'C3')?.v).toBe('NECH1003')
    expect(cell(g, 'A5')?.v).toBe('oct-26')
    expect(cell(g, 'B5')?.v).toBe(1500)
    const notes = wb.Sheets.Notas
    expect(cell(notes, 'A1')?.v).toBe('Qué es cada columna del Resumen')
    expect(String(cell(notes, 'A3')?.v)).toMatch(/^Clasif\. ABC: A = /)
    expect(notes['!cols']?.[0]?.wch).toBe(130)
  })
})
