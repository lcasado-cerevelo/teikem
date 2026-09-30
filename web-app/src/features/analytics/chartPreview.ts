// Fase 10b — "Vista previa" del editor de gráficos (maqueta `renderChartEditorHtml`: el gráfico real con la
// configuración que se está editando, sin guardar). No hay un endpoint de vista previa de gráficos: se usa el de la
// vista previa del constructor de vistas (`POST /api/v1/analytics/reports/{fuente}/preview`, `analytics.view`), que
// corre el mismo motor (`AnalyticsEngine`) con filtro, agrupación y agregado ad hoc, y aquí se replica lo que
// `AnalyticsEngine.EvaluateChartAsync` hace encima (orden, top 8 con "Otras" / últimos 30, agrupación por día local):
// - Agrupar por un campo que NO es fecha: el servidor agrupa y agrega (`groupJson`) y ordena con `sortJson` (barra/dona:
//   mayor valor primero; línea: los 30 últimos por el valor del campo). Barra/dona con SUM o COUNT piden TODOS los grupos
//   (hasta 5 000) para juntar el resto en "Otras" aquí; con AVG/MIN/MAX, los 8 mayores (`take`). Exacto.
// - Agrupar por el campo de fecha (tipo Date de la fuente): el motor de gráficos agrupa por DÍA LOCAL de la compañía
//   (Lote 15: hora de Puerto Rico, `localDayOf`) y el de vistas por el valor exacto (marca de tiempo), así que se piden
//   las filas sin agrupar (hasta 5 000) y se agrupan y agregan aquí. Si la fuente trae más filas, la vista previa se
//   calcula con las primeras 5 000 (`truncated`).
// - "Otras" (Lote 15, D11, `AnalyticsEngine.FoldOthers`): en barra, dona o pastel con SUM o COUNT, con más de 8 grupos se
//   muestran los 7 mayores y "Otras" (clave `$others`) con la suma exacta del resto.
// - Rango de fecha: los modos relativos van en `?dateRangeMode=`; el endpoint no recibe Desde/Hasta, así que un rango
//   CUSTOM se manda como `ALL` más dos condiciones en el filtro sobre el campo de fecha (`gte` Desde, `lt` Hasta + 1
//   día: el mismo límite exclusivo que `DateRangeResolver`). Sin modo elegido se usa LAST7 (el que aplica el servidor).
// Sin React: se prueba sola.
import type { components } from '../../kernel/api/schema'
import { localDayOf } from '../../kernel/api/tenantZone'
import type { ChartVisualPoint } from './ChartVisual'
import type { DataSource } from './definitions'

type ReportUpsertRequest = components['schemas']['ReportUpsertRequest']
type ReportRunResultDto = components['schemas']['ReportRunResultDto']

/** Máximo de filas que acepta el endpoint (`Math.Clamp(take, 1, 5000)`). */
export const PREVIEW_RAW_TAKE = 5000
/** Barra/dona: los N grupos de mayor valor (`topN` de `EvaluateChartAsync`). */
export const PREVIEW_TOP_N = 8
/** Línea: los últimos N puntos en orden. */
export const PREVIEW_LINE_POINTS = 30
/** Clave del punto "Otras" (`AnalyticsEngine.OthersKey`). */
export const OTHERS_KEY = '$others'

export interface ChartPreviewInput {
  source: DataSource | null | undefined
  groupByField: string
  aggregateFn: string
  field: string
  chartType: string
  dateRangeMode: string
  dateFrom: string
  dateTo: string
  /** Filtro ya serializado (filas del editor o el original conservado). */
  filterJson: string | null
}

export interface ChartPreviewRequest {
  baseEntityType: string
  query: { dateRangeMode: string; take: number }
  body: ReportUpsertRequest
  /** true = filas sin agrupar, agrupadas por día en el cliente. */
  byDay: boolean
  line: boolean
  groupByField: string
  aggregateFn: string
  field: string | null
  /** Clave de la columna agregada en las filas agrupadas (`AnalyticsEngine.AggKey`). */
  aggKey: string
  /** true = barra/dona con SUM o COUNT: se junta el resto en "Otras" (se piden todos los grupos). */
  foldOthers: boolean
}

/** Día siguiente a 'YYYY-MM-DD' (límite exclusivo del rango CUSTOM). */
export function nextDay(ymd: string): string {
  const d = new Date(`${ymd}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + 1)
  return d.toISOString().slice(0, 10)
}

/** Filtro del usuario + límites del rango CUSTOM sobre el campo de fecha. */
export function withDateBounds(filterJson: string | null, dateField: string, from: string, to: string): string {
  const bounds = [
    { field: dateField, op: 'gte', value: from },
    { field: dateField, op: 'lt', value: nextDay(to) },
  ]
  let userFilter: unknown = null
  if (filterJson) {
    try {
      userFilter = JSON.parse(filterJson)
    } catch {
      userFilter = null
    }
  }
  return JSON.stringify({ and: userFilter == null ? bounds : [userFilter, ...bounds] })
}

/** `AnalyticsEngine.AggKey`: `{fn en minúsculas}_{campo o "rows"}`. */
export function previewAggKey(fn: string, field: string | null): string {
  return `${fn.toLowerCase()}_${field ?? 'rows'}`
}

/** true si el campo es de tipo fecha de la fuente (el motor solo mira los campos propios, no los personalizados). */
function isDateField(source: DataSource, key: string): boolean {
  return (source.fields ?? []).some((f) => (f.key ?? '').toLowerCase() === key.toLowerCase() && f.type === 'Date')
}

/**
 * Petición de la vista previa, o `null` mientras la configuración esté incompleta (sin fuente, sin "Agrupar por", sin
 * campo con un cálculo distinto de COUNT, o un rango personalizado sin Desde/Hasta válidos).
 */
export function planChartPreview(input: ChartPreviewInput): ChartPreviewRequest | null {
  const source = input.source
  if (!source?.key || !input.groupByField || !input.aggregateFn) return null
  const fn = input.aggregateFn.toUpperCase()
  const field = fn === 'COUNT' ? null : input.field || null
  if (fn !== 'COUNT' && !field) return null

  let dateRangeMode = input.dateRangeMode || 'LAST7'
  let filterJson = input.filterJson
  if (!source.dateField) {
    dateRangeMode = 'ALL'
  } else if (dateRangeMode === 'CUSTOM') {
    if (!input.dateFrom || !input.dateTo || input.dateFrom > input.dateTo) return null
    filterJson = withDateBounds(filterJson, source.dateField, input.dateFrom, input.dateTo)
    dateRangeMode = 'ALL'
  }

  const line = (input.chartType || '').toUpperCase() === 'LINE'
  const byDay = isDateField(source, input.groupByField)
  const aggKey = previewAggKey(fn, field)
  const foldOthers = !line && (fn === 'SUM' || fn === 'COUNT')
  const base = { baseEntityType: source.key, byDay, line, groupByField: input.groupByField, aggregateFn: fn, field, aggKey, foldOthers }

  if (byDay) {
    return {
      ...base,
      query: { dateRangeMode, take: PREVIEW_RAW_TAKE },
      body: { columns: field ? [input.groupByField, field] : [input.groupByField], filterJson },
    }
  }
  return {
    ...base,
    query: { dateRangeMode, take: line ? PREVIEW_LINE_POINTS : foldOthers ? PREVIEW_RAW_TAKE : PREVIEW_TOP_N },
    body: {
      columns: [input.groupByField],
      filterJson,
      groupJson: JSON.stringify({ by: [input.groupByField], aggregates: [{ fn, field }] }),
      sortJson: JSON.stringify([line ? { field: input.groupByField, dir: 'desc' } : { field: aggKey, dir: 'desc' }]),
    },
  }
}

/** Valor de una fila sin distinguir mayúsculas en la clave (las filas del motor no lo hacen). */
function cell(row: Record<string, unknown>, key: string): unknown {
  if (key in row) return row[key]
  const lower = key.toLowerCase()
  for (const k of Object.keys(row)) if (k.toLowerCase() === lower) return row[k]
  return undefined
}

/** `RuleEvaluator.ToDecimal`: números y textos numéricos (que no parezcan fecha). */
function toNumber(v: unknown): number | null {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null
  if (typeof v === 'string' && v.trim() !== '' && !/^\d{4}-\d{2}-\d{2}/.test(v)) {
    const n = Number(v)
    return Number.isFinite(n) ? n : null
  }
  return null
}

/** Etiqueta del grupo como `GroupKey` del motor: vacío = '—'; booleanos como .NET ("True"/"False"). */
function groupLabel(v: unknown): string {
  if (v == null || v === '') return '—'
  if (typeof v === 'boolean') return v ? 'True' : 'False'
  return String(v)
}

/** `AnalyticsEngine.Aggregate` (COUNT, SUM, AVG a 4 decimales, MIN, MAX). */
export function aggregateValues(rows: readonly Record<string, unknown>[], fn: string, field: string | null): number | null {
  if (fn === 'COUNT') return field == null ? rows.length : rows.filter((r) => cell(r, field) != null).length
  if (field == null) return null
  const values = rows.map((r) => toNumber(cell(r, field))).filter((v): v is number => v != null)
  if (values.length === 0) return fn === 'SUM' ? 0 : null
  switch (fn) {
    case 'SUM':
      return values.reduce((a, b) => a + b, 0)
    case 'AVG':
      return Math.round((values.reduce((a, b) => a + b, 0) / values.length) * 10000) / 10000
    case 'MIN':
      return Math.min(...values)
    case 'MAX':
      return Math.max(...values)
    default:
      return null
  }
}

/**
 * `AnalyticsEngine.FoldOthers`: de mayor a menor (orden estable); con más de `topN` puntos, los `topN − 1` mayores y al final
 * "Otras" (`OTHERS_KEY`) con la suma del resto; con `topN` o menos, sin cambios.
 */
export function foldOthers(points: readonly { label: string; value: number }[], topN: number, othersLabel: string): ChartVisualPoint[] {
  const ordered = [...points].sort((a, b) => b.value - a.value).map(({ label, value }) => ({ label, value }))
  if (topN < 2 || ordered.length <= topN) return ordered.slice(0, Math.max(topN, 0))
  const rest = ordered.slice(topN - 1).reduce((sum, p) => sum + p.value, 0)
  return [...ordered.slice(0, topN - 1), { label: othersLabel, key: OTHERS_KEY, value: rest }]
}

export interface ChartPreviewResult {
  points: ChartVisualPoint[]
  /** La fuente trajo más filas de las que se usaron (solo al agrupar por día). */
  truncated: boolean
}

/**
 * Puntos del gráfico a partir de la respuesta de la vista previa, con el mismo orden y recorte que el motor.
 * `othersLabel`: etiqueta del grupo "Otras" en el idioma de la interfaz (el motor usa la del idioma del usuario).
 */
export function chartPreviewPoints(result: ReportRunResultDto | null | undefined, req: ChartPreviewRequest, othersLabel = 'Otras'): ChartPreviewResult {
  const rows = (result?.rows ?? []) as Record<string, unknown>[]
  const total = result?.total ?? rows.length
  const truncated = total > rows.length
  if (!req.byDay) {
    const points = rows.map((r) => ({ label: groupLabel(cell(r, req.groupByField)), value: toNumber(cell(r, req.aggKey)) ?? 0 }))
    // Línea: el servidor devolvió los 30 últimos de mayor a menor; se ponen en orden cronológico/ascendente.
    if (req.line) return { points: points.reverse(), truncated: false }
    if (!req.foldOthers) return { points, truncated: false }
    return { points: foldOthers(points, PREVIEW_TOP_N, othersLabel), truncated }
  }

  // Agrupar por día LOCAL en el cliente (orden de aparición, como `GroupBy` de LINQ; `GroupKey` del motor).
  const groups = new Map<string, { day: string | null; rows: Record<string, unknown>[] }>()
  for (const r of rows) {
    const day = localDayOf(cell(r, req.groupByField))
    const key = day ?? '—'
    const g = groups.get(key)
    if (g) g.rows.push(r)
    else groups.set(key, { day, rows: [r] })
  }
  let points = [...groups.values()].map((g) => ({ day: g.day, label: g.day ?? '—', value: aggregateValues(g.rows, req.aggregateFn, req.field) ?? 0 }))
  if (req.line) {
    // Sin fecha primero (el comparador del motor pone null antes), luego por día; los últimos 30.
    points = points.sort((a, b) => (a.day == null ? (b.day == null ? 0 : -1) : b.day == null ? 1 : a.day < b.day ? -1 : a.day > b.day ? 1 : 0))
    points = points.slice(Math.max(0, points.length - PREVIEW_LINE_POINTS))
  } else if (req.foldOthers) {
    return { points: foldOthers(points, PREVIEW_TOP_N, othersLabel), truncated }
  } else {
    points = points.sort((a, b) => b.value - a.value).slice(0, PREVIEW_TOP_N)
  }
  return { points: points.map(({ label, value }) => ({ label, value })), truncated }
}
