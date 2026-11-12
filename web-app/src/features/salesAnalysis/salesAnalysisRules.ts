// Lote 241 — Análisis de ventas: reglas puras de la pantalla (meses, tonos de color, filtros, columnas del Resumen, series de los
// gráficos, avisos de estado y la rejilla de cuotas). Sin React: se prueban en salesAnalysisRules.test.ts.
import type { components } from '../../kernel/api/schema'
import { formatDateLong } from '../../kernel/format'
import { matchesQ } from '../../kernel/ui/matchesQ'

type S = components['schemas']
export type SalesByRep = S['SalesByRepDto']
export type SalesRepRow = S['SalesRepRowDto']
export type SalesLineShare = S['SalesLineShareDto']
export type SalesRepLine = S['SalesRepLineDto']
export type SalesRepProduct = S['SalesRepProductDto']
export type SalesRouteProduct = S['SalesRouteProductDto']
export type SalesTransaction = S['SalesTransactionDto']
export type ProductMovement = S['ProductMovementDto']
export type ProductMovementRow = S['ProductMovementRowDto']
export type ProductMonthSeries = S['ProductMonthSeriesDto']
export type ProductClient = S['ProductClientDto']
export type SalesCharts = S['SalesChartsDto']
export type SalesChartSeries = S['SalesChartSeriesDto']
export type SalesStatus = S['SalesAnalysisStatusDto']
export type SalesRoute = S['SalesRouteDto']
export type SalesRep = S['SalesRepDto']
export type SalesQuotaMonth = S['SalesQuotaMonthDto']
export type SalesQuotaRow = S['SalesQuotaRowDto']
export type ProductLine = S['ProductLineDto']
export type SalesBrand = S['SalesBrandDto']
export type SalesClientRep = S['SalesClientRepDto']
export type SalesSettings = S['SalesAnalysisSettingsDto']
export type SalesImportReport = S['SalesHistoryImportReportDto']
export type SalesImportFile = S['SalesHistoryFileReportDto']
export type SalesUnmatched = S['SalesUnmatchedDto']

/** Permisos de la pantalla (los del API). */
export const SALES_ANALYSIS_VIEW = 'sales.analysis.view|sales.analysis.manage'
export const SALES_ANALYSIS_MANAGE = 'sales.analysis.manage'

// ---------------------------------------------------------------- meses ----------------------------------------------------------------

const YM = /^(\d{4})-(0[1-9]|1[0-2])$/

export function isMonth(v: string | null | undefined): v is string {
  return !!v && YM.test(v)
}

/** Suma `n` meses a 'AAAA-MM' (n negativo = hacia atrás). */
export function addMonths(ym: string, n: number): string {
  const m = YM.exec(ym)
  if (!m) return ym
  const total = Number(m[1]) * 12 + (Number(m[2]) - 1) + n
  const y = Math.floor(total / 12)
  const mo = total - y * 12 + 1
  return `${y}-${String(mo).padStart(2, '0')}`
}

export const prevMonth = (ym: string) => addMonths(ym, -1)

/** 'AAAA-MM' del día 'AAAA-MM-DD'. */
export const monthOf = (day: string) => day.slice(0, 7)

/** El último mes completo (el anterior al de `today`). */
export const lastCompleteMonth = (today: string) => prevMonth(monthOf(today))

/** Los meses de `from` a `to` (incluidos), en orden. */
export function monthRange(from: string, to: string): string[] {
  if (!isMonth(from) || !isMonth(to) || to < from) return []
  const out: string[] = []
  for (let m = from; m <= to && out.length < 120; m = addMonths(m, 1)) out.push(m)
  return out
}

/** Opciones de mes para un selector: los `count` meses que terminan en el de `today`, del más reciente al más viejo. */
export function monthOptions(today: string, count = 36): string[] {
  const last = monthOf(today)
  return Array.from({ length: count }, (_, i) => addMonths(last, -i))
}

/** 'oct-26' (como los encabezados del Excel del dueño). */
export function monthShort(ym: string, lang: string): string {
  if (!isMonth(ym)) return ym
  // tres letras como el Excel del dueño ('sept.' de algunos idiomas → 'sep')
  const name = formatDateLong(`${ym}-01`, lang, { month: 'short' }).replace(/\.$/, '').toLowerCase().slice(0, 3)
  return `${name}-${ym.slice(2, 4)}`
}

/** 'octubre de 2026' / 'October 2026'. */
export function monthLong(ym: string, lang: string): string {
  if (!isMonth(ym)) return ym
  return formatDateLong(`${ym}-01`, lang, { month: 'long', year: 'numeric' })
}

/** Nombre del mes solo ('octubre' / 'October'). */
export function monthName(ym: string, lang: string): string {
  if (!isMonth(ym)) return ym
  return formatDateLong(`${ym}-01`, lang, { month: 'long' })
}

// ---------------------------------------------------------------- tonos ----------------------------------------------------------------

/** Tonos de `Chip` (colores del tema, contraste AA vigilado por las pruebas del kit). */
export type Tone = 'neutral' | 'cap' | 'wh' | 'disp' | 'route' | 'deliv' | 'cod' | 'fail' | 'warn'

/** Semanas de cobertura: menos de esto = rojo (poco). */
export const COVERAGE_LOW_WEEKS = 2
/** Semanas de cobertura: desde esto = verde (mucho). Entre las dos = ámbar. */
export const COVERAGE_HIGH_WEEKS = 4

export type CoverageLevel = 'none' | 'low' | 'mid' | 'high'

/** Nivel de cobertura (sin promedio semanal = 'none': no hay base para calcularla). */
export function coverageLevel(weeks: number | null | undefined): CoverageLevel {
  if (weeks === null || weeks === undefined || !Number.isFinite(weeks)) return 'none'
  if (weeks < COVERAGE_LOW_WEEKS) return 'low'
  if (weeks < COVERAGE_HIGH_WEEKS) return 'mid'
  return 'high'
}

export const COVERAGE_TONE: Record<CoverageLevel, Tone> = { none: 'cap', low: 'fail', mid: 'warn', high: 'deliv' }
/** Color del tema de cada nivel (barras del gráfico de cobertura). */
export const COVERAGE_COLOR: Record<CoverageLevel, string> = { none: 'var(--neutral-fg)', low: 'var(--danger)', mid: 'var(--warn)', high: 'var(--ok)' }

export function abcTone(abc: string | null | undefined): Tone {
  switch (abc) {
    case 'A':
      return 'deliv'
    case 'B':
      return 'disp'
    case 'C':
      return 'warn'
    default:
      return 'cap'
  }
}

export const MOVEMENT_STATUSES = ['NO_SALES', 'STALE', 'NEW', 'RISING', 'FALLING', 'STABLE'] as const
export type MovementStatus = (typeof MOVEMENT_STATUSES)[number]

const STATUS_TONE: Record<MovementStatus, Tone> = { NO_SALES: 'cap', STALE: 'fail', NEW: 'wh', RISING: 'deliv', FALLING: 'warn', STABLE: 'disp' }

export function statusTone(status: string | null | undefined): Tone {
  return STATUS_TONE[status as MovementStatus] ?? 'cap'
}

/** Clave i18n del estado del producto (`salesAnalysis.status.<CODE>`); desconocido = el código tal cual. */
export function statusKey(status: string | null | undefined): string | null {
  return MOVEMENT_STATUSES.includes(status as MovementStatus) ? `salesAnalysis.status.${status}` : null
}

export type ComplianceLevel = 'none' | 'met' | 'near' | 'below'
/** Cumplimiento de cuota desde el que se considera «cerca» (ámbar). */
export const COMPLIANCE_NEAR = 0.9

export function complianceLevel(c: number | null | undefined): ComplianceLevel {
  if (c === null || c === undefined || !Number.isFinite(c)) return 'none'
  if (c >= 1) return 'met'
  if (c >= COMPLIANCE_NEAR) return 'near'
  return 'below'
}

export const COMPLIANCE_TONE: Record<ComplianceLevel, Tone> = { none: 'cap', met: 'deliv', near: 'warn', below: 'fail' }
export const COMPLIANCE_COLOR: Record<ComplianceLevel, string> = { none: 'var(--neutral-fg)', met: 'var(--ok)', near: 'var(--warn)', below: 'var(--danger)' }

/** Ancho de la barra de cumplimiento (0–100 %, tope 100). */
export function complianceWidth(c: number | null | undefined): number {
  if (c === null || c === undefined || !Number.isFinite(c) || c <= 0) return 0
  return Math.min(100, Math.round(c * 1000) / 10)
}

/** Tono del cambio contra el mes anterior: sube = verde, baja = rojo, igual = neutro. */
export function changeTone(change: number | null | undefined): 'up' | 'down' | 'flat' {
  if (!change || Math.abs(change) < 0.005) return 'flat'
  return change > 0 ? 'up' : 'down'
}

/** Intensidad (0–1) de una celda de matriz contra el máximo de la matriz (escala de color de «Unidades por mes»). */
export function heatLevel(value: number | null | undefined, max: number): number {
  if (!value || value <= 0 || !(max > 0)) return 0
  return Math.min(1, value / max)
}

/** Porcentaje de mezcla del color de la celda (tope 38 % para que el texto conserve el contraste AA en los dos temas). */
export const HEAT_MAX_MIX = 38
export const heatMix = (level: number) => Math.round(level * HEAT_MAX_MIX)

// ---------------------------------------------------------------- filtros ----------------------------------------------------------------

export interface ProductFilter {
  route: string
  rep: string
  line: string
  q: string
}

export const EMPTY_PRODUCT_FILTER: ProductFilter = { route: '', rep: '', line: '', q: '' }

/** Clave de representante para los filtros (`repId` o «sin representante»). */
export const repKey = (repId: number | null | undefined) => (repId === null || repId === undefined ? 'none' : String(repId))

/** Producto por representante con los filtros de ruta, representante, línea y el buscador libre. */
export function filterRepProducts(rows: readonly SalesRepProduct[], f: ProductFilter): SalesRepProduct[] {
  return rows.filter(
    (r) =>
      (!f.route || (r.routeName ?? '') === f.route) &&
      (!f.rep || repKey(r.repId) === f.rep) &&
      (!f.line || (r.line ?? '') === f.line) &&
      matchesQ(f.q, r.sku, r.product, r.repName, r.routeName, r.line),
  )
}

/** Producto por ruta (el filtro de representante busca su nombre en `repNames`). */
export function filterRouteProducts(rows: readonly SalesRouteProduct[], f: ProductFilter, repName?: string | null): SalesRouteProduct[] {
  return rows.filter(
    (r) =>
      (!f.route || (r.routeName ?? '') === f.route) &&
      (!f.rep || (repName ? (r.repNames ?? '').split(/\s*,\s*/).includes(repName) : true)) &&
      (!f.line || (r.line ?? '') === f.line) &&
      matchesQ(f.q, r.sku, r.product, r.repNames, r.routeName, r.line),
  )
}

/** Valores distintos (sin vacíos), ordenados como llegan. */
export function distinct(values: readonly (string | null | undefined)[]): string[] {
  const seen = new Set<string>()
  const out: string[] = []
  for (const v of values) {
    if (v && !seen.has(v)) {
      seen.add(v)
      out.push(v)
    }
  }
  return out
}

/** Representantes (clave + nombre) de las filas por representante, en el orden de la vista. */
export function repOptions(reps: readonly SalesRepRow[]): { value: string; label: string }[] {
  return reps.map((r) => ({ value: repKey(r.repId), label: r.repName ?? '' }))
}

// ---------------------------------------------------------------- Resumen de movimiento ----------------------------------------------------------------

/** Columnas del Resumen (en el orden del Excel «Movimiento de productos» más las del contrato). */
export const MOVEMENT_COLUMNS = [
  'sku',
  'name',
  'category',
  'brand',
  'line',
  'abc',
  'status',
  'price',
  'cost',
  'marginPct',
  'onHand',
  'reserved',
  'available',
  'units',
  'sales',
  'shareOfSales',
  'monthlyAverage',
  'monthlyMin',
  'monthlyMax',
  'bestMonth',
  'weeklyAverage',
  'coverageWeeks',
  'projected1',
  'projected2',
  'projected3',
  'reorderPoint',
  'suggestedReorderPoint',
  'trend',
  'clients',
  'invoices',
  'topClient',
  'topClientUnits',
  'lastSale',
  'pendingPurchase',
] as const
export type MovementColumn = (typeof MOVEMENT_COLUMNS)[number]

/** Grupos de columnas del Resumen (SKU y nombre van siempre) para que la tabla quepa. */
export const MOVEMENT_COLUMN_GROUPS = {
  sales: ['abc', 'status', 'units', 'sales', 'shareOfSales', 'monthlyAverage', 'bestMonth', 'trend'],
  stock: ['onHand', 'reserved', 'available', 'weeklyAverage', 'coverageWeeks', 'projected1', 'projected2', 'projected3', 'pendingPurchase'],
  reorder: ['abc', 'status', 'available', 'weeklyAverage', 'coverageWeeks', 'reorderPoint', 'suggestedReorderPoint', 'pendingPurchase'],
  prices: ['category', 'brand', 'line', 'price', 'cost', 'marginPct'],
  monthly: ['monthlyAverage', 'monthlyMin', 'monthlyMax', 'bestMonth', 'weeklyAverage', 'trend'],
  clients: ['clients', 'invoices', 'topClient', 'topClientUnits', 'lastSale'],
} as const satisfies Record<string, readonly MovementColumn[]>
export type MovementColumnGroup = keyof typeof MOVEMENT_COLUMN_GROUPS | 'all' | 'custom'

export const ALWAYS_VISIBLE: readonly MovementColumn[] = ['sku', 'name']

/** Columnas visibles de un grupo (con SKU y nombre al principio, en el orden del Resumen). */
export function columnsOfGroup(group: MovementColumnGroup, custom: readonly string[] = []): MovementColumn[] {
  const pick: readonly string[] = group === 'all' ? MOVEMENT_COLUMNS : group === 'custom' ? custom : MOVEMENT_COLUMN_GROUPS[group]
  const set = new Set<string>([...ALWAYS_VISIBLE, ...pick])
  return MOVEMENT_COLUMNS.filter((c) => set.has(c))
}

export interface MovementFilter {
  line: string
  abc: string
  status: string
  q: string
}
export const EMPTY_MOVEMENT_FILTER: MovementFilter = { line: '', abc: '', status: '', q: '' }

export function filterMovement(rows: readonly ProductMovementRow[], f: MovementFilter): ProductMovementRow[] {
  return rows.filter(
    (r) =>
      (!f.line || (r.line ?? '') === f.line) &&
      (!f.abc || (r.abc ?? '-') === f.abc) &&
      (!f.status || r.status === f.status) &&
      matchesQ(f.q, r.sku, r.name, r.category, r.brand, r.line, r.topClient),
  )
}

/** Los `n` productos con más venta $ (empate: SKU), solo los que vendieron. */
export function topProductsBySales(rows: readonly ProductMovementRow[], n: number): ProductMovementRow[] {
  return [...rows]
    .filter((r) => (r.sales ?? 0) > 0)
    .sort((a, b) => (b.sales ?? 0) - (a.sales ?? 0) || (a.sku ?? '').localeCompare(b.sku ?? ''))
    .slice(0, n)
}

/** Clave de una fila de producto (con id o, si no emparejó, su SKU/nombre crudo). */
export const productKey = (r: { productId?: number | null; sku?: string | null; name?: string | null }) =>
  r.productId !== null && r.productId !== undefined ? `p${r.productId}` : `raw:${r.sku ?? ''}|${r.name ?? ''}`

/** Máximo de los valores de una matriz (para la escala de color). */
export function matrixMax(series: readonly ProductMonthSeries[]): number {
  let max = 0
  for (const s of series) for (const v of s.values ?? []) if (v > max) max = v
  return max
}

/** Totales por columna (mes) de una matriz. */
export function matrixColumnTotals(series: readonly ProductMonthSeries[], months: number): number[] {
  const out = Array.from({ length: months }, () => 0)
  for (const s of series) (s.values ?? []).forEach((v, i) => (out[i] = (out[i] ?? 0) + (v ?? 0)))
  return out.map((v) => Math.round(v * 100) / 100)
}

// ---------------------------------------------------------------- series de los gráficos ----------------------------------------------------------------

/** Paleta de series (colores del tema: cambian con claro/oscuro). */
export const SERIES_COLORS = [
  'var(--info)',
  'var(--flow)',
  'var(--ok)',
  'var(--violet)',
  'var(--warn)',
  'var(--money-2)',
  'var(--danger)',
  'var(--neutral-fg)',
] as const
export const seriesColor = (i: number) => SERIES_COLORS[i % SERIES_COLORS.length]

export interface MonthPoint {
  month: string
  label: string
  [series: string]: number | string
}

/** Filas por mes para recharts: `{ month, label, s0: v, s1: v… }` con una clave por serie (`s<i>`). */
export function monthRows(months: readonly string[], series: readonly { values?: readonly number[] | null }[], lang: string): MonthPoint[] {
  return months.map((m, i) => {
    const row: MonthPoint = { month: m, label: monthShort(m, lang) }
    series.forEach((s, k) => (row[`s${k}`] = s.values?.[i] ?? 0))
    return row
  })
}

export interface RepQuotaPoint {
  key: string
  rep: string
  route: string
  sales: number
  quota: number | null
  compliance: number | null
}

/** Venta contra cuota por representante, desde las filas del Comparativo. */
export function repQuotaFromRows(rows: readonly SalesRepRow[]): RepQuotaPoint[] {
  return rows.map((r) => ({
    key: repKey(r.repId),
    rep: r.repName ?? '',
    route: r.routeName ?? '',
    sales: r.sales ?? 0,
    quota: r.quota ?? null,
    compliance: r.compliance ?? null,
  }))
}

export function repQuotaFromCharts(points: readonly S['SalesRepQuotaPointDto'][]): RepQuotaPoint[] {
  return points.map((p) => ({ key: repKey(p.repId), rep: p.rep ?? '', route: p.route ?? '', sales: p.sales ?? 0, quota: p.quota ?? null, compliance: p.compliance ?? null }))
}

export interface SharePoint {
  name: string
  value: number
  share: number | null
}

/** Participación por línea (sin los descuentos ni las líneas en cero o negativas: un pastel no pinta negativos). */
export function lineShareFromLines(lines: readonly SalesLineShare[]): SharePoint[] {
  return lines.filter((l) => !l.isDiscounts && (l.sales ?? 0) > 0).map((l) => ({ name: l.line ?? '', value: l.sales ?? 0, share: l.shareOfGross ?? null }))
}

export function lineShareFromCharts(points: readonly S['SalesLineSharePointDto'][]): SharePoint[] {
  return points.filter((p) => (p.sales ?? 0) > 0).map((p) => ({ name: p.line ?? '', value: p.sales ?? 0, share: p.share ?? null }))
}

/** Más de `max` rebanadas: las pequeñas se juntan en «Otras» (`othersLabel`). */
export function collapseShares(points: readonly SharePoint[], max: number, othersLabel: string): SharePoint[] {
  const sorted = [...points].sort((a, b) => b.value - a.value)
  if (sorted.length <= max) return sorted
  const head = sorted.slice(0, max - 1)
  const rest = sorted.slice(max - 1)
  const value = rest.reduce((s, p) => s + p.value, 0)
  const share = rest.every((p) => p.share !== null) ? rest.reduce((s, p) => s + (p.share ?? 0), 0) : null
  return [...head, { name: othersLabel, value, share }]
}

export interface CoveragePoint {
  key: string
  label: string
  name: string
  weeks: number
  level: CoverageLevel
  available: number
  weeklyAverage: number | null
}

/**
 * Cobertura por producto para el gráfico de barras horizontales: solo los que tienen promedio semanal (con cobertura), de
 * menor a mayor, los `limit` primeros (los que hay que mirar).
 */
export function coveragePoints(
  rows: readonly { productId?: number | null; sku?: string | null; name?: string | null; available?: number; weeklyAverage?: number | null; coverageWeeks?: number | null }[],
  limit: number,
): CoveragePoint[] {
  return rows
    .filter((r) => r.coverageWeeks !== null && r.coverageWeeks !== undefined && Number.isFinite(r.coverageWeeks))
    .map((r) => ({
      key: productKey(r),
      label: r.sku ?? r.name ?? '',
      name: r.name ?? '',
      weeks: r.coverageWeeks as number,
      level: coverageLevel(r.coverageWeeks),
      available: r.available ?? 0,
      weeklyAverage: r.weeklyAverage ?? null,
    }))
    .sort((a, b) => a.weeks - b.weeks || a.label.localeCompare(b.label))
    .slice(0, limit)
}

/** Series de unidades por mes de los productos principales (desde las matrices del movimiento). */
export function topUnitsSeries(movement: ProductMovement, n: number): SalesChartSeries[] {
  const top = topProductsBySales(movement.products ?? [], n)
  const byKey = new Map((movement.unitsByMonth ?? []).map((s) => [productKey(s), s]))
  return top.map((p) => ({ key: p.sku ?? p.name ?? '', label: p.sku ?? p.name ?? '', values: byKey.get(productKey(p))?.values ?? [] }))
}

// ---------------------------------------------------------------- estado (aviso de arriba) ----------------------------------------------------------------

export type SetupSection = 'routes' | 'reps' | 'quotas' | 'lines' | 'clients' | 'settings' | 'import' | 'link'
export const SETUP_SECTIONS: readonly SetupSection[] = ['routes', 'reps', 'quotas', 'lines', 'clients', 'settings', 'import', 'link']

export interface StatusIssue {
  key: 'clientsWithoutRep' | 'brandsWithoutLine' | 'unmatched' | 'noCutoff' | 'overlap' | 'noHistory'
  count: number
  section: SetupSection
}

/** Lo que falta arreglar según `GET status`, con la sección de Configuración donde se arregla. */
export function statusIssues(s: SalesStatus | null | undefined): StatusIssue[] {
  if (!s) return []
  const out: StatusIssue[] = []
  const unmatched = (s.unmatchedClients?.length ?? 0) + (s.unmatchedItems?.length ?? 0) + (s.unmatchedPurchaseItems?.length ?? 0)
  if ((s.clientsWithoutRep ?? 0) > 0) out.push({ key: 'clientsWithoutRep', count: s.clientsWithoutRep ?? 0, section: 'clients' })
  if ((s.brandsWithoutLine?.length ?? 0) > 0) out.push({ key: 'brandsWithoutLine', count: s.brandsWithoutLine?.length ?? 0, section: 'lines' })
  if (unmatched > 0) out.push({ key: 'unmatched', count: unmatched, section: 'link' })
  const hasHistory = (s.batches ?? []).some((b) => b.isActive)
  if (!hasHistory) out.push({ key: 'noHistory', count: 0, section: 'import' })
  if (s.overlap) out.push({ key: 'overlap', count: 0, section: 'settings' })
  else if (!s.cutoffDate && hasHistory) out.push({ key: 'noCutoff', count: 0, section: 'settings' })
  return out
}

// ---------------------------------------------------------------- cuotas ----------------------------------------------------------------

/** Rejilla de cuotas: texto por celda `repId|mes` (vacío = sin cuota propia ese mes: vale la anterior). */
export type QuotaDraft = Record<string, string>
export const quotaCell = (repId: number, month: string) => `${repId}|${month}`

/** Texto editable de un monto (sin separadores de miles; vacío si no hay). */
export function quotaText(amount: number | null | undefined): string {
  return amount === null || amount === undefined ? '' : String(Math.round(amount * 100) / 100)
}

/** Lee el texto de una cuota: '' = quitar (null); número ≥ 0 con hasta 2 decimales; si no, `undefined` (inválido). */
export function parseQuota(text: string): number | null | undefined {
  const s = text.trim().replace(/[$,\s]/g, '')
  if (s === '') return null
  if (!/^\d+(\.\d{1,2})?$/.test(s)) return undefined
  return Number(s)
}

/** Borrador inicial: la cuota PROPIA de cada representante en cada mes. */
export function quotaDraftFrom(months: readonly SalesQuotaMonth[]): QuotaDraft {
  const draft: QuotaDraft = {}
  for (const m of months) for (const r of m.rows ?? []) if (r.repId !== undefined) draft[quotaCell(r.repId, m.month ?? '')] = quotaText(r.amount)
  return draft
}

export interface QuotaChange {
  month: string
  items: { repId: number; amount: number | null }[]
}

/** Lo que cambió del borrador contra lo guardado, por mes (para `PUT quotas/{month}`); `invalid` = celdas que no se pueden leer. */
export function quotaChanges(months: readonly SalesQuotaMonth[], draft: QuotaDraft): { changes: QuotaChange[]; invalid: string[] } {
  const changes: QuotaChange[] = []
  const invalid: string[] = []
  for (const m of months) {
    const month = m.month ?? ''
    const items: QuotaChange['items'] = []
    for (const r of m.rows ?? []) {
      if (r.repId === undefined) continue
      const cell = quotaCell(r.repId, month)
      const text = draft[cell]
      if (text === undefined) continue
      const value = parseQuota(text)
      if (value === undefined) {
        invalid.push(cell)
        continue
      }
      const saved = r.amount ?? null
      if (value !== saved) items.push({ repId: r.repId, amount: value })
    }
    if (items.length > 0) changes.push({ month, items })
  }
  return { changes, invalid }
}

/** «Copiar del mes anterior»: pone en `month` la cuota que VALE en el mes anterior (propia o heredada) de cada representante. */
export function copyPreviousMonth(draft: QuotaDraft, months: readonly SalesQuotaMonth[], month: string): QuotaDraft {
  const idx = months.findIndex((m) => m.month === month)
  if (idx <= 0) return draft
  const prev = months[idx - 1]
  const next = { ...draft }
  for (const r of prev.rows ?? []) {
    if (r.repId === undefined) continue
    const own = draft[quotaCell(r.repId, prev.month ?? '')]
    const parsed = own !== undefined ? parseQuota(own) : undefined
    const value = parsed !== undefined && parsed !== null ? parsed : (r.effective ?? null)
    next[quotaCell(r.repId, month)] = quotaText(value)
  }
  return next
}

/** Total de un mes en la rejilla (lo escrito o, si la celda está vacía, la cuota heredada). */
export function quotaMonthTotal(draft: QuotaDraft, m: SalesQuotaMonth): number {
  let total = 0
  for (const r of m.rows ?? []) {
    if (r.repId === undefined) continue
    const parsed = parseQuota(draft[quotaCell(r.repId, m.month ?? '')] ?? '')
    total += parsed !== undefined && parsed !== null ? parsed : (r.effective ?? 0)
  }
  return Math.round(total * 100) / 100
}

// ---------------------------------------------------------------- importación ----------------------------------------------------------------

/** Clases de línea del historial en el orden del reporte. */
export const LINE_CLASSES = ['PRODUCT', 'DISCOUNT', 'SHIPPING', 'OTHER'] as const

/** Filas «clase · líneas · monto» del reporte de un archivo (las clases conocidas primero, luego las demás). */
export function classRows(file: SalesImportFile): { cls: string; lines: number; amount: number }[] {
  const lines = file.linesByClass ?? {}
  const amounts = file.amountByClass ?? {}
  const keys = distinct([...LINE_CLASSES.filter((c) => c in lines || c in amounts), ...Object.keys(lines), ...Object.keys(amounts)])
  return keys.map((cls) => ({ cls, lines: lines[cls] ?? 0, amount: amounts[cls] ?? 0 }))
}

/** El total leído cuadra con el TOTAL del reporte (al centavo). */
export const totalsMatch = (file: SalesImportFile) => Math.abs((file.totalAmount ?? 0) - (file.reportTotal ?? 0)) < 0.005

/** Niveles de servicio conocidos → z (los del Excel «Reorder point»). */
export const SERVICE_LEVELS: readonly { pct: number; z: number }[] = [
  { pct: 0.9, z: 1.282 },
  { pct: 0.95, z: 1.645 },
  { pct: 0.975, z: 1.96 },
  { pct: 0.99, z: 2.326 },
]
