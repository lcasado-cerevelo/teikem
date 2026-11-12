// Lote 241 — Exportar el Análisis de ventas a Excel con las MISMAS hojas que los libros del dueño:
//  · «Por representante» (Analisis_Ventas_<Mes>.xlsx): Comparativo, Marcas, Representante producto, Ventas marca por representante,
//    Variedades por ruta, Inventario y Transacciones.
//  · «Movimiento de productos» (Movimiento_de_productos.xlsx): Resumen, Unidades por mes, Venta $ por mes, Compras por mes,
//    Clientes por producto, Gráfico y Notas.
// Encabezados, anchos, formato de número/moneda/porcentaje y FÓRMULAS de totales donde el original las tiene (con el valor ya
// calculado, así se ve bien aunque el visor no recalcule). Límite de SheetJS (edición comunitaria): no escribe estilos (negritas,
// rellenos, formato condicional), ni paneles fijos, ni gráficos ni imágenes: la hoja «Gráfico» lleva los DATOS del gráfico (como el
// original) y el gráfico dibujado sale de «Imprimir presentación» en la pestaña Gráficos. Funciones puras (reciben el módulo XLSX y
// `t`): se prueban en salesAnalysisExcel.test.ts sin descargar nada.
import type { WorkBook, WorkSheet } from 'xlsx'
import { exportFileName, sheetName } from '../../kernel/ui/exportTable'
import {
  MOVEMENT_COLUMNS,
  isMonth,
  monthName,
  monthShort,
  statusKey,
  topProductsBySales,
  productKey,
  type MovementColumn,
  type ProductMonthSeries,
  type ProductMovement,
  type ProductMovementRow,
  type SalesByRep,
  type SalesTransaction,
} from './salesAnalysisRules'

type XlsxModule = typeof import('xlsx')
export type TFn = (key: string, params?: Record<string, string | number>) => string

// formatos de número del libro del dueño
export const FMT = {
  money: '\\$#,##0.00;[Red]\\(\\$#,##0.00\\);"—"',
  money0: '\\$#,##0',
  moneyPlain: '\\$#,##0.00',
  units: '#,##0',
  unitsSigned: '#,##0;[Red]\\(#,##0\\);"—"',
  pct2: '0.00%',
  pct1: '0.0%',
  trend: '\\+0%;\\-0%;0%',
  dec1: '0.0',
  dec2: '0.00',
  date: 'dd\\-mmm\\-yy',
  dateIso: 'yyyy-mm-dd',
  text: '@',
} as const

export interface XCell {
  t: 's' | 'n'
  v: string | number
  f?: string
  z?: string
}
export type XValue = XCell | string | number | null | undefined
export type XRow = XValue[]

export const num = (v: number | null | undefined, z?: string): XCell | null => (v === null || v === undefined || !Number.isFinite(v) ? null : { t: 'n', v, z })
/** Fórmula con su valor ya calculado (`v`); un valor de texto deja la celda como texto calculado. */
export const fx = (formula: string, v: number | string | null | undefined, z?: string): XCell => ({
  t: typeof v === 'string' ? 's' : 'n',
  v: v ?? 0,
  f: formula,
  z,
})
const txt = (v: string | null | undefined, z?: string): XCell | null => (v === null || v === undefined || v === '' ? null : { t: 's', v, z })

/** Número de serie de Excel de un día 'AAAA-MM-DD'. */
export function excelDay(day: string | null | undefined): number | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(day ?? '')
  if (!m) return null
  return (Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])) - Date.UTC(1899, 11, 30)) / 86_400_000
}

/** Letra de la columna (0 = A). */
export function col(i: number): string {
  let s = ''
  let n = i + 1
  while (n > 0) {
    const r = (n - 1) % 26
    s = String.fromCharCode(65 + r) + s
    n = Math.floor((n - 1) / 26)
  }
  return s
}

export interface SheetSpec {
  name: string
  rows: XRow[]
  /** Ancho de cada columna en caracteres (los del libro del dueño). */
  widths: number[]
  /** Fila (base 1) de los encabezados de la tabla: lleva el autofiltro hasta `filterLastRow`. */
  headerRow?: number
  filterLastRow?: number
}

/** Hoja de SheetJS a partir de las filas (celdas con tipo, fórmula y formato). */
export function buildSheet(XLSX: XlsxModule, spec: SheetSpec): WorkSheet {
  const ws: WorkSheet = {}
  let maxC = 0
  spec.rows.forEach((row, r) => {
    row.forEach((value, c) => {
      if (value === null || value === undefined || value === '') return
      const cell: XCell = typeof value === 'object' ? value : typeof value === 'number' ? { t: 'n', v: value } : { t: 's', v: value }
      const out: Record<string, unknown> = { t: cell.t, v: cell.v }
      if (cell.f) out.f = cell.f
      if (cell.z) out.z = cell.z
      ws[XLSX.utils.encode_cell({ r, c })] = out
      if (c > maxC) maxC = c
    })
  })
  const lastRow = Math.max(0, spec.rows.length - 1)
  const lastCol = Math.max(maxC, spec.widths.length - 1)
  ws['!ref'] = XLSX.utils.encode_range({ s: { r: 0, c: 0 }, e: { r: lastRow, c: lastCol } })
  ws['!cols'] = spec.widths.map((wch) => ({ wch }))
  if (spec.headerRow && spec.filterLastRow && spec.filterLastRow > spec.headerRow) {
    ws['!autofilter'] = { ref: XLSX.utils.encode_range({ s: { r: spec.headerRow - 1, c: 0 }, e: { r: spec.filterLastRow - 1, c: lastCol } }) }
  }
  return ws
}

export function buildWorkbook(XLSX: XlsxModule, sheets: readonly SheetSpec[]): WorkBook {
  const wb = XLSX.utils.book_new()
  for (const s of sheets) XLSX.utils.book_append_sheet(wb, buildSheet(XLSX, s), sheetName(s.name))
  return wb
}

const cap = (s: string) => (s ? s.charAt(0).toUpperCase() + s.slice(1) : s)

// =============================================================== libro «Por representante» ===============================================================

export interface ByRepWorkbookInput {
  byRep: SalesByRep
  transactions: readonly SalesTransaction[]
  /** Movimiento del MISMO mes (existencia de hoy y unidades del mes) para la hoja Inventario; null = sin la hoja. */
  inventory: ProductMovement | null
  t: TFn
  lang: string
}

/** Las hojas del libro «Análisis de ventas» del mes (Comparativo con su conciliación, Marcas, …, Transacciones). */
export function byRepSheets({ byRep, transactions, inventory, t, lang }: ByRepWorkbookInput): SheetSpec[] {
  const k = (key: string, p?: Record<string, string | number>) => t(`salesAnalysis.xlsx.${key}`, p)
  const month = byRep.month ?? ''
  const compare = byRep.compareMonth ?? ''
  const mName = cap(monthName(month, lang))
  const cName = cap(monthName(compare, lang))
  const reps = byRep.reps ?? []
  const totals = byRep.totals ?? {}
  const rec = byRep.reconciliation ?? {}
  const noRep = t('salesAnalysis.noRep')

  // ---- Comparativo ----
  const comp: XRow[] = [[k('compTitle', { month: monthName(month, lang), year: month.slice(0, 4), compare: monthName(compare, lang) })], []]
  comp.push([k('route'), k('rep'), `${cName} $`, `${mName} $`, k('change'), k('changePct'), k('quota'), k('compliance')])
  const first = comp.length + 1
  for (const r of reps) {
    const R = comp.length + 1
    comp.push([
      r.routeName ?? '',
      r.repName ?? noRep,
      num(r.compareSales ?? 0, FMT.money),
      num(r.sales ?? 0, FMT.money),
      fx(`D${R}-C${R}`, r.change ?? 0, FMT.money),
      fx(`IF(C${R}<>0,E${R}/C${R},"")`, r.changePct ?? '', FMT.pct2),
      num(r.quota, FMT.money),
      fx(`IF(N(G${R})>0,D${R}/G${R},"")`, r.compliance ?? '', FMT.pct2),
    ])
  }
  const last = comp.length
  const T = comp.length + 1
  const sum = (c: string, v: number | undefined) => (reps.length > 0 ? fx(`SUM(${c}${first}:${c}${last})`, v ?? 0, FMT.money) : num(v ?? 0, FMT.money))
  comp.push([
    k('totalRoutes'),
    '',
    sum('C', totals.compareSales),
    sum('D', totals.sales),
    sum('E', totals.change),
    fx(`IF(C${T}<>0,E${T}/C${T},"")`, totals.changePct ?? '', FMT.pct2),
    sum('G', totals.quota),
    fx(`IF(G${T}>0,D${T}/G${T},"")`, totals.compliance ?? '', FMT.pct2),
  ])
  comp.push([])
  comp.push([k('reconciliation')])
  const rRoutes = comp.length + 1
  comp.push([k('recRoutes'), '', '', fx(`D${T}`, rec.routesTotal ?? 0, FMT.money)])
  const rUn = comp.length + 1
  comp.push([k('recUnassigned'), '', '', num(rec.unassigned ?? 0, FMT.money)])
  const rNet = comp.length + 1
  comp.push([k('recNet'), '', '', fx(`D${rRoutes}+D${rUn}`, (rec.routesTotal ?? 0) + (rec.unassigned ?? 0), FMT.money)])
  const rShip = comp.length + 1
  comp.push([k('recShipping'), '', '', num(rec.shippingAndOther ?? 0, FMT.money)])
  const rMonth = comp.length + 1
  comp.push([k('recMonthTotal', { month: monthName(month, lang) }), '', '', fx(`D${rNet}+D${rShip}`, rec.monthTotal ?? 0, FMT.money)])
  if (Math.abs(rec.difference ?? 0) >= 0.005) comp.push([k('recDifference'), '', '', num(rec.difference ?? 0, FMT.money)])
  comp.push([])
  const rProd = comp.length + 1
  comp.push([k('recProductSales'), '', '', num(rec.productSales ?? 0, FMT.money)])
  comp.push([k('recDiscounts'), '', '', num(rec.discounts ?? 0, FMT.money)])
  comp.push([k('recShippingOnly'), '', '', num(rec.shipping ?? 0, FMT.money)])
  comp.push([k('recOther'), '', '', num(rec.other ?? 0, FMT.money)])
  comp.push([])
  if (byRep.cutoffDate) comp.push([k('cutoffNote', { date: byRep.cutoffDate })])
  for (const w of byRep.warnings ?? []) comp.push([w])
  void rMonth

  // ---- Marcas (líneas) ----
  const lines = byRep.lines ?? []
  const marcas: XRow[] = [[k('linesTitle', { month: monthName(month, lang), year: month.slice(0, 4) })], []]
  marcas.push([k('line'), k('sales'), k('shareNet'), k('shareGross'), k('leader'), k('territory'), k('leaderSales')])
  const lFirst = marcas.length + 1
  const netRef = `Comparativo!$D$${rNet}`
  const prodRef = `Comparativo!$D$${rProd}`
  for (const l of lines) {
    const R = marcas.length + 1
    marcas.push([
      l.line ?? '',
      num(l.sales ?? 0, FMT.money),
      fx(`IF(${netRef}<>0,B${R}/${netRef},0)`, l.shareOfNet ?? 0, FMT.pct2),
      fx(`IF(${prodRef}<>0,B${R}/${prodRef},0)`, l.shareOfGross ?? 0, FMT.pct2),
      l.isDiscounts ? k('notApplicable') : (l.leaderName ?? ''),
      l.isDiscounts ? k('notApplicable') : (l.leaderRoute ?? ''),
      l.isDiscounts ? null : num(l.leaderSales, FMT.money),
    ])
  }
  if (lines.length > 0) {
    const R = marcas.length + 1
    const total = lines.reduce((s, l) => s + (l.sales ?? 0), 0)
    marcas.push([k('total'), fx(`SUM(B${lFirst}:B${R - 1})`, Math.round(total * 100) / 100, FMT.money), fx(`IF(${netRef}<>0,B${R}/${netRef},0)`, (rec.routesTotal ?? 0) + (rec.unassigned ?? 0) !== 0 ? 1 : 0, FMT.pct2)])
  }

  // ---- Representante producto ----
  const rp: XRow[] = [[k('repProductsTitle', { month: monthName(month, lang), year: month.slice(0, 4) })], []]
  rp.push([k('rep'), k('route'), k('line'), k('sku'), k('product'), k('netUnits'), k('netSales')])
  const rpHeader = rp.length
  for (const r of byRep.repProducts ?? []) rp.push([r.repName ?? noRep, r.routeName ?? '', r.line ?? '', txt(r.sku, FMT.text), r.product ?? '', num(r.units ?? 0, FMT.units), num(r.sales ?? 0, FMT.money)])

  // ---- Ventas marca por representante ----
  const rl: XRow[] = [[k('repLinesTitle', { month: monthName(month, lang), year: month.slice(0, 4) })], []]
  rl.push([k('route'), k('rep'), k('line'), k('netSales'), k('shareOfRep')])
  const groups: { key: string; rows: typeof byRep.repLines }[] = []
  for (const r of byRep.repLines ?? []) {
    const key = r.repId === null || r.repId === undefined ? 'none' : String(r.repId)
    const g = groups.find((x) => x.key === key)
    if (g) g.rows!.push(r)
    else groups.push({ key, rows: [r] })
  }
  const totalCells: string[] = []
  let grand = 0
  for (const g of groups) {
    const rows = g.rows ?? []
    const start = rl.length + 1
    const totalRow = start + rows.length
    const repTotal = rows.reduce((s, r) => s + (r.sales ?? 0), 0)
    grand += repTotal
    for (const r of rows) {
      const R = rl.length + 1
      rl.push([r.routeName ?? '', r.repName ?? noRep, r.line ?? '', num(r.sales ?? 0, FMT.money), fx(`IF(D${totalRow}<>0,D${R}/D${totalRow},0)`, r.shareOfRep ?? 0, FMT.pct2)])
    }
    const head = rows[0]
    rl.push([head?.routeName ?? '', head?.repName ?? noRep, k('repTotal'), fx(`SUM(D${start}:D${totalRow - 1})`, Math.round(repTotal * 100) / 100, FMT.money), fx(`IF(D${totalRow}<>0,D${totalRow}/D${totalRow},0)`, repTotal !== 0 ? 1 : 0, FMT.pct2)])
    totalCells.push(`D${totalRow}`)
  }
  if (totalCells.length > 0) rl.push(['', '', k('monthTotal', { month: monthName(month, lang).toUpperCase() }), fx(totalCells.join('+'), Math.round(grand * 100) / 100, FMT.money)])

  // ---- Variedades por ruta ----
  const vr: XRow[] = [[k('routeProductsTitle', { month: monthName(month, lang), year: month.slice(0, 4) })], [k('routeProductsNote')]]
  vr.push([k('route'), k('reps'), k('line'), k('sku'), k('variety'), k('netUnits'), k('netSales')])
  const vrHeader = vr.length
  const vrFirst = vr.length + 1
  for (const r of byRep.routeProducts ?? []) vr.push([r.routeName ?? noRep, r.repNames ?? '', r.line ?? '', txt(r.sku, FMT.text), r.product ?? '', num(r.units ?? 0, FMT.unitsSigned), num(r.sales ?? 0, FMT.money)])
  const vrLast = vr.length
  if (vrLast >= vrFirst) {
    const units = (byRep.routeProducts ?? []).reduce((s, r) => s + (r.units ?? 0), 0)
    const sales = (byRep.routeProducts ?? []).reduce((s, r) => s + (r.sales ?? 0), 0)
    vr.push([])
    vr.push(['', '', '', '', k('productsTotal'), fx(`SUM(F${vrFirst}:F${vrLast})`, units, FMT.unitsSigned), fx(`SUM(G${vrFirst}:G${vrLast})`, Math.round(sales * 100) / 100, FMT.money)])
  }

  const sheets: SheetSpec[] = [
    { name: k('sheet.comparativo'), rows: comp, widths: [26, 42, 19, 19, 17, 12, 19, 20] },
    { name: k('sheet.marcas'), rows: marcas, widths: [34, 19, 18, 22, 42, 18, 20] },
    { name: k('sheet.repProducts'), rows: rp, widths: [42, 16, 29, 24, 60, 18, 20], headerRow: rpHeader, filterLastRow: rp.length },
    { name: k('sheet.repLines'), rows: rl, widths: [18, 44, 39, 22, 25] },
    { name: k('sheet.routeProducts'), rows: vr, widths: [18, 44, 28, 26, 60, 20, 22], headerRow: vrHeader, filterLastRow: vrLast },
  ]

  // ---- Inventario ----
  if (inventory) {
    const inv: XRow[] = [[k('inventoryTitle', { date: inventory.asOf ?? '' })], [k('inventoryNote', { month: monthName(month, lang) })]]
    inv.push([k('sku'), k('product'), k('brandOrCategory'), k('onHand'), k('reserved'), k('available'), k('monthUnits', { month: monthName(month, lang) }), k('coverageMonths'), k('after1'), k('after2'), k('after3'), k('pendingPurchase')])
    const invHeader = inv.length
    for (const p of (inventory.products ?? []).filter((x) => x.matched !== false)) {
      const R = inv.length + 1
      const units = p.units ?? 0
      const avail = p.available ?? 0
      inv.push([
        txt(p.sku, FMT.text),
        p.name ?? '',
        p.brand || p.category || '',
        num(p.onHand ?? 0, FMT.units),
        num(p.reserved ?? 0, FMT.units),
        num(avail, FMT.units),
        num(units, FMT.units),
        fx(`IF(G${R}>0,F${R}/G${R},"${k('noSales')}")`, units > 0 ? Math.round((avail / units) * 100) / 100 : k('noSales'), FMT.dec2),
        fx(`MAX(0,F${R}-G${R}*1)`, Math.max(0, avail - units), FMT.units),
        fx(`MAX(0,F${R}-G${R}*2)`, Math.max(0, avail - units * 2), FMT.units),
        fx(`MAX(0,F${R}-G${R}*3)`, Math.max(0, avail - units * 3), FMT.units),
        num(p.pendingPurchase ?? 0, FMT.units),
      ])
    }
    sheets.push({ name: k('sheet.inventory'), rows: inv, widths: [24, 60, 30, 15, 12, 16, 21, 20, 21, 21, 21, 16], headerRow: invHeader, filterLastRow: inv.length })
  }

  // ---- Transacciones ----
  const tx: XRow[] = [[k('transactionsTitle', { month: monthName(month, lang), year: month.slice(0, 4) })], [k('transactionsNote', { count: transactions.length })]]
  tx.push([k('date'), k('rep'), k('route'), k('type'), k('number'), k('client'), k('sku'), k('product'), k('line'), k('lineClass'), k('units'), k('unitPrice'), k('amount'), k('source')])
  const txHeader = tx.length
  const txFirst = tx.length + 1
  for (const r of transactions) {
    tx.push([
      num(excelDay(r.date), FMT.dateIso),
      r.repName ?? noRep,
      r.routeName ?? '',
      r.type ?? '',
      txt(r.number, FMT.text),
      r.clientName ?? '',
      txt(r.sku, FMT.text),
      r.product ?? '',
      r.line ?? '',
      r.lineClass ? t(`salesAnalysis.lineClass.${r.lineClass}`) : '',
      num(r.quantity ?? 0, FMT.units),
      num(r.unitPrice, FMT.moneyPlain),
      num(r.amount ?? 0, FMT.money),
      r.source ? t(`salesAnalysis.source.${r.source}`) : '',
    ])
  }
  const txLast = tx.length
  if (txLast >= txFirst) {
    const total = transactions.reduce((s, r) => s + (r.amount ?? 0), 0)
    tx.push(['', '', '', '', '', '', '', '', '', '', '', k('total'), fx(`SUM(M${txFirst}:M${txLast})`, Math.round(total * 100) / 100, FMT.money)])
  }
  sheets.push({ name: k('sheet.transactions'), rows: tx, widths: [12, 36, 15, 14, 16, 40, 18, 52, 22, 14, 12, 12, 16, 14], headerRow: txHeader, filterLastRow: txLast })
  return sheets
}

export function buildByRepWorkbook(XLSX: XlsxModule, input: ByRepWorkbookInput): WorkBook {
  return buildWorkbook(XLSX, byRepSheets(input))
}

// =============================================================== libro «Movimiento de productos» ===============================================================

export interface MovementWorkbookInput {
  movement: ProductMovement
  company: string
  t: TFn
  lang: string
  /** Cuántos productos lleva la hoja Gráfico (los principales por venta $; 6 como el original). */
  chartProducts?: number
}

/** Encabezados de mes: 'oct-26' y, si el último es el del día de corte, 'nov-26 (al 5)'. */
export function monthHeaders(movement: ProductMovement, lang: string, t: TFn): string[] {
  const months = movement.months ?? []
  const asOf = movement.asOf ?? ''
  return months.map((m) => {
    const label = monthShort(m, lang)
    return isMonth(m) && asOf.startsWith(m) && m !== movement.lastCompleteMonth ? `${label} ${t('salesAnalysis.xlsx.partialMonth', { day: Number(asOf.slice(8, 10)) })}` : label
  })
}

/** Formato y ancho de cada columna del Resumen. */
const RESUMEN_FMT: Partial<Record<MovementColumn, string>> = {
  price: FMT.moneyPlain,
  cost: FMT.moneyPlain,
  marginPct: FMT.pct1,
  onHand: FMT.units,
  reserved: FMT.units,
  available: FMT.units,
  units: FMT.units,
  sales: FMT.money0,
  shareOfSales: FMT.pct1,
  monthlyAverage: FMT.units,
  monthlyMin: FMT.units,
  monthlyMax: FMT.units,
  weeklyAverage: FMT.units,
  coverageWeeks: FMT.dec1,
  projected1: FMT.units,
  projected2: FMT.units,
  projected3: FMT.units,
  reorderPoint: FMT.units,
  suggestedReorderPoint: FMT.units,
  trend: FMT.trend,
  clients: FMT.units,
  invoices: FMT.units,
  topClientUnits: FMT.units,
  pendingPurchase: FMT.units,
}
const RESUMEN_WIDTH: Partial<Record<MovementColumn, number>> = { sku: 20, name: 42, category: 16, brand: 14, line: 16, abc: 9, status: 22, topClient: 28, lastSale: 13, bestMonth: 11 }

/** Valor de una celda del Resumen. */
export function resumenCell(r: ProductMovementRow, c: MovementColumn, t: TFn, lang: string): XValue {
  switch (c) {
    case 'sku':
      return txt(r.sku, FMT.text)
    case 'status': {
      const key = statusKey(r.status)
      return key ? t(key) : (r.status ?? '')
    }
    case 'bestMonth':
      return r.bestMonth ? monthShort(r.bestMonth, lang) : null
    case 'lastSale':
      return num(excelDay(r.lastSale), FMT.date)
    case 'name':
    case 'category':
    case 'brand':
    case 'line':
    case 'abc':
    case 'topClient':
      return r[c] ?? ''
    default:
      return num(r[c] as number | null | undefined, RESUMEN_FMT[c])
  }
}

function matrixSheet(name: string, series: readonly ProductMonthSeries[], headers: string[], totalLabel: string, z: string, withStats: boolean, t: TFn): SheetSpec {
  const k = (key: string) => t(`salesAnalysis.xlsx.${key}`)
  const head: XRow = [k('item'), k('description'), ...headers, totalLabel]
  if (withStats) head.push(k('minActive'), k('max'), k('average'))
  const rows: XRow[] = [head]
  const n = headers.length
  for (const s of series) {
    const R = rows.length + 1
    const values = s.values ?? []
    const row: XRow = [txt(s.sku, FMT.text), s.name ?? '', ...headers.map((_, i) => (values[i] ? num(values[i], z) : null))]
    row.push(n > 0 ? fx(`SUM(${col(2)}${R}:${col(1 + n)}${R})`, s.total ?? 0, z) : num(s.total ?? 0, z))
    if (withStats) row.push(num(s.min, z), num(s.max, z), num(s.average, FMT.dec1))
    rows.push(row)
  }
  return { name, rows, widths: [20, 42, ...headers.map(() => 12), 14, ...(withStats ? [16, 11, 11] : [])], headerRow: 1, filterLastRow: rows.length }
}

/** Las hojas del libro «Movimiento de productos» (Resumen, matrices por mes, Clientes por producto, Gráfico y Notas). */
export function movementSheets({ movement, company, t, lang, chartProducts = 6 }: MovementWorkbookInput): SheetSpec[] {
  const k = (key: string, p?: Record<string, string | number>) => t(`salesAnalysis.xlsx.${key}`, p)
  const headers = monthHeaders(movement, lang, t)
  const months = movement.months ?? []

  // ---- Resumen ----
  const res: XRow[] = [
    [k('movementTitle', { company })],
    [k('movementSubtitle', { from: months[0] ? monthShort(months[0], lang) : '', asOf: movement.asOf ?? '', weeksFrom: movement.weeksFrom ?? '', weeksTo: movement.weeksTo ?? '', lead: movement.leadTimeDays ?? 0, z: movement.serviceLevelZ ?? 0 })],
    [],
    MOVEMENT_COLUMNS.map((c) => t(`salesAnalysis.col.${c}`)),
  ]
  for (const r of movement.products ?? []) res.push(MOVEMENT_COLUMNS.map((c) => resumenCell(r, c, t, lang)))
  const resumen: SheetSpec = { name: k('sheet.resumen'), rows: res, widths: MOVEMENT_COLUMNS.map((c) => RESUMEN_WIDTH[c] ?? 12), headerRow: 4, filterLastRow: res.length }

  // ---- Clientes por producto ----
  const cp: XRow[] = [[k('item'), k('description'), k('client'), k('units'), k('shareOfProduct')]]
  for (const c of movement.topClients ?? []) cp.push([txt(c.sku, FMT.text), c.name ?? '', c.client ?? '', num(c.units ?? 0, FMT.units), num(c.shareOfProduct, FMT.pct1)])

  // ---- Gráfico (los datos del gráfico «Unidades por mes» de los principales) ----
  const top = topProductsBySales(movement.products ?? [], chartProducts)
  const unitsByKey = new Map((movement.unitsByMonth ?? []).map((s) => [productKey(s), s]))
  const g: XRow[] = [[k('chartTitle', { n: top.length })], [], [k('month'), ...top.map((p) => p.sku ?? p.name ?? '')]]
  headers.forEach((h, i) => g.push([h, ...top.map((p) => num(unitsByKey.get(productKey(p))?.values?.[i] ?? 0, FMT.units))]))
  g.push([])
  g.push([k('chartNote')])

  // ---- Notas ----
  const notes: XRow[] = [[k('notesTitle')], []]
  const np = { lead: movement.leadTimeDays ?? 0, z: movement.serviceLevelZ ?? 0, weeksFrom: movement.weeksFrom ?? '', weeksTo: movement.weeksTo ?? '' }
  for (const key of NOTE_KEYS) notes.push([`${t(`salesAnalysis.notesTerm.${key}`)}: ${t(`salesAnalysis.notes.${key}`, np)}`])
  notes.push([])
  notes.push([k('sourcesTitle')])
  notes.push([k('sourcesSales')])
  notes.push([k('sourcesStock', { asOf: movement.asOf ?? '' })])
  if (movement.cutoffDate) notes.push([k('cutoffNote', { date: movement.cutoffDate })])
  for (const w of movement.warnings ?? []) notes.push([w])

  return [
    resumen,
    matrixSheet(k('sheet.unitsByMonth'), movement.unitsByMonth ?? [], headers, k('total'), FMT.units, true, t),
    matrixSheet(k('sheet.salesByMonth'), movement.salesByMonth ?? [], headers, k('total'), FMT.money0, false, t),
    matrixSheet(k('sheet.purchasesByMonth'), movement.purchasesByMonth ?? [], headers, k('totalPurchased'), FMT.units, false, t),
    { name: k('sheet.clientsByProduct'), rows: cp, widths: [20, 42, 36, 11, 13], headerRow: 1, filterLastRow: cp.length },
    { name: k('sheet.chart'), rows: g, widths: [14, ...top.map(() => 12)] },
    { name: k('sheet.notes'), rows: notes, widths: [130] },
  ]
}

/** Definiciones de las columnas (hoja Notas y la sección plegable «Qué significa cada columna»). */
export const NOTE_KEYS = ['abc', 'status', 'monthly', 'weekly', 'coverage', 'projected', 'reorder', 'suggested', 'trend', 'topClient', 'pending'] as const

export function buildMovementWorkbook(XLSX: XlsxModule, input: MovementWorkbookInput): WorkBook {
  return buildWorkbook(XLSX, movementSheets(input))
}

/** Nombre del archivo con el día de la compañía. */
export const workbookFileName = (base: string) => exportFileName(base, 'xlsx')

/** Descarga el libro (carga SheetJS bajo demanda). */
export async function downloadWorkbook(make: (XLSX: XlsxModule) => WorkBook, fileName: string): Promise<void> {
  const XLSX = await import('xlsx')
  XLSX.writeFile(make(XLSX), fileName, { compression: true })
}
