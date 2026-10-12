// Lote F-F2 — reglas puras de «Vistas e informes» (`/analytics/reports`): catálogo de campos por fuente (propios, personalizados
// `cf.<clave>` y de fuentes combinadas `REL.Campo`), borrador del constructor ⇄ cuerpo del API (`ReportUpsertRequest`: columns,
// filterJson, groupJson, sortJson), validaciones, galería por fuente y rotulado del resultado. Sin React: se prueba sola.
// Formatos del servidor (`AnalyticsEngine`): columns "Campo" | "REL.Campo" | "cf.<clave>"; groupJson {by:[campos], aggregates:
// [{fn, field?}], totals}; sortJson [{field, dir}]; la columna de un agregado se llama `<fn en minúsculas>_<campo|rows>`.
import type { components } from '../../kernel/api/schema'
import {
  filterIsUnrepresentable,
  parseFilterRows,
  serializeFilterRows,
  type DataSource,
  type FilterRow,
  type ShareDto,
} from './definitions'
import type { ReportResultColumn, ReportRunResult } from './reportResult'

export type ReportDto = components['schemas']['ReportDto']
export type ReportUpsertRequest = components['schemas']['ReportUpsertRequest']
export type ReportRunRequest = components['schemas']['ReportRunRequest']

export type FieldType = 'Text' | 'Number' | 'Date' | 'Bool'

/** Funciones de agregado del motor (`AggregateFns`). */
export const AGGREGATE_FNS = ['COUNT', 'SUM', 'AVG', 'MIN', 'MAX'] as const
export type AggregateFn = (typeof AGGREGATE_FNS)[number]

/** Filas de la vista previa del constructor (la maqueta muestra 6). */
export const PREVIEW_ROWS = 10
/** Filas que se piden al correr una vista (máximo del servidor); la exportación sale de lo cargado. */
export const RUN_TAKE = 5000

const collator = new Intl.Collator('es', { sensitivity: 'base', numeric: true })

// --------------------------------------------------------------------------------------------------------------
// Campos disponibles
// --------------------------------------------------------------------------------------------------------------

export interface ViewField {
  /** Clave que viaja al API: "Campo", "cf.clave" o "REL.Campo". */
  key: string
  label: string
  type: FieldType
  isMoney: boolean
  /** Fuente a la que pertenece el campo (la principal o la combinada). */
  sourceKey: string
  /** Clave de la relación ("Client") si viene de una fuente combinada; null en la principal. */
  relationKey: string | null
  custom: boolean
}

export interface ViewFieldGroup {
  /** '' = principal, 'cf' = campos personalizados, o la clave de la relación. */
  id: string
  label: string
  fields: ViewField[]
}

function fieldType(raw: string | null | undefined): FieldType {
  return raw === 'Number' || raw === 'Date' || raw === 'Bool' ? raw : 'Text'
}

function toViewField(f: components['schemas']['DataFieldDto'], source: string, rel: { key: string; label: string } | null, custom: boolean): ViewField {
  const key = f.key ?? ''
  const label = f.label ?? key
  return {
    key: rel ? `${rel.key}.${key}` : key,
    label: rel ? `${rel.label} · ${label}` : label,
    type: fieldType(f.type),
    isMoney: Boolean(f.isMoney),
    sourceKey: source,
    relationKey: rel?.key ?? null,
    custom,
  }
}

/** Relaciones de la fuente cuyo destino el usuario puede leer (el API solo devuelve las fuentes legibles). */
export function combinableRelations(source: DataSource | undefined, sources: readonly DataSource[]) {
  return (source?.relations ?? []).filter((r) => r.key && sources.some((s) => s.key === r.targetSource))
}

/** Todos los campos de la fuente principal, sus personalizados y los de CADA relación (para rotular vistas ya guardadas). */
export function fieldIndex(source: DataSource | undefined, sources: readonly DataSource[]): Map<string, ViewField> {
  const index = new Map<string, ViewField>()
  if (!source) return index
  for (const f of source.fields ?? []) index.set(f.key ?? '', toViewField(f, source.key ?? '', null, false))
  for (const f of source.customFields ?? []) index.set(f.key ?? '', toViewField(f, source.key ?? '', null, true))
  for (const rel of combinableRelations(source, sources)) {
    const target = sources.find((s) => s.key === rel.targetSource)
    for (const f of target?.fields ?? []) {
      const vf = toViewField(f, target?.key ?? '', { key: rel.key ?? '', label: rel.label ?? rel.key ?? '' }, false)
      index.set(vf.key, vf)
    }
  }
  return index
}

/** Bloques de casillas del constructor: la fuente principal, sus campos personalizados y cada fuente combinada elegida. */
export function fieldGroups(source: DataSource | undefined, sources: readonly DataSource[], secondary: readonly string[]): ViewFieldGroup[] {
  if (!source) return []
  const groups: ViewFieldGroup[] = [
    { id: '', label: source.label ?? source.key ?? '', fields: (source.fields ?? []).map((f) => toViewField(f, source.key ?? '', null, false)) },
  ]
  const custom = (source.customFields ?? []).map((f) => toViewField(f, source.key ?? '', null, true))
  if (custom.length > 0) groups.push({ id: 'cf', label: '', fields: custom })
  for (const relKey of secondary) {
    const rel = combinableRelations(source, sources).find((r) => r.key === relKey)
    const target = sources.find((s) => s.key === rel?.targetSource)
    if (!rel || !target) continue
    groups.push({
      id: relKey,
      label: rel.label ?? relKey,
      fields: (target.fields ?? []).map((f) => toViewField(f, target.key ?? '', { key: relKey, label: rel.label ?? relKey }, false)),
    })
  }
  return groups
}

/** Los campos de los bloques, en un solo arreglo (el orden de las casillas). */
export function flatFields(groups: readonly ViewFieldGroup[]): ViewField[] {
  return groups.flatMap((g) => g.fields)
}

// --------------------------------------------------------------------------------------------------------------
// groupJson y sortJson
// --------------------------------------------------------------------------------------------------------------

export interface AggregateRow {
  fn: AggregateFn
  /** '' = todas las filas (solo COUNT). */
  field: string
}

export interface SortRow {
  field: string
  dir: 'asc' | 'desc'
}

export interface GroupSpec {
  by: string[]
  aggregates: AggregateRow[]
  totals: boolean
}

function parseJson(json: string | null | undefined): unknown {
  if (!json) return null
  try {
    return JSON.parse(json)
  } catch {
    return null
  }
}

function asFn(v: unknown): AggregateFn {
  const up = typeof v === 'string' ? v.toUpperCase() : 'COUNT'
  return (AGGREGATE_FNS as readonly string[]).includes(up) ? (up as AggregateFn) : 'COUNT'
}

export function parseGroupJson(json: string | null | undefined): GroupSpec {
  const root = parseJson(json)
  const spec: GroupSpec = { by: [], aggregates: [], totals: false }
  if (!root || typeof root !== 'object') return spec
  const r = root as Record<string, unknown>
  if (Array.isArray(r.by)) spec.by = r.by.filter((x): x is string => typeof x === 'string')
  if (Array.isArray(r.aggregates)) {
    spec.aggregates = r.aggregates
      .filter((x): x is Record<string, unknown> => !!x && typeof x === 'object')
      .map((a) => ({ fn: asFn(a.fn), field: typeof a.field === 'string' ? a.field : '' }))
  }
  spec.totals = r.totals === true
  return spec
}

export function serializeGroupJson(spec: GroupSpec): string | null {
  if (spec.by.length === 0) return null
  return JSON.stringify({
    by: spec.by,
    aggregates: spec.aggregates.map((a) => (a.field ? { fn: a.fn, field: a.field } : { fn: a.fn })),
    totals: spec.totals,
  })
}

export function parseSortJson(json: string | null | undefined): SortRow[] {
  const root = parseJson(json)
  if (!Array.isArray(root)) return []
  return root
    .filter((x): x is Record<string, unknown> => !!x && typeof x === 'object' && typeof (x as Record<string, unknown>).field === 'string')
    .map((x) => ({ field: String(x.field), dir: String(x.dir ?? 'asc').toLowerCase() === 'desc' ? ('desc' as const) : ('asc' as const) }))
}

export function serializeSortJson(rows: readonly SortRow[]): string | null {
  const valid = rows.filter((r) => r.field)
  return valid.length === 0 ? null : JSON.stringify(valid.map((r) => ({ field: r.field, dir: r.dir })))
}

/** Nombre de la columna que el servidor devuelve para un agregado (`AnalyticsEngine.AggKey`). */
export function aggregateKey(a: AggregateRow): string {
  return `${a.fn.toLowerCase()}_${a.field || 'rows'}`
}

/** Cuántas condiciones tiene un filtro guardado (hojas del árbol and/or/not); 0 sin filtro o ilegible. */
export function countFilterConditions(json: string | null | undefined): number {
  const walk = (node: unknown): number => {
    if (Array.isArray(node)) return node.reduce<number>((n, x) => n + walk(x), 0)
    if (!node || typeof node !== 'object') return 0
    const o = node as Record<string, unknown>
    if (typeof o.field === 'string') return 1
    return ['and', 'or', 'not'].reduce((n, k) => n + (k in o ? walk(o[k]) : 0), 0)
  }
  return walk(parseJson(json))
}

// --------------------------------------------------------------------------------------------------------------
// Borrador del constructor
// --------------------------------------------------------------------------------------------------------------

export interface ViewDraft {
  /** null = alta. */
  id: number | null
  name: string
  source: string
  secondary: string[]
  /** Columnas elegidas (se ignoran si hay agrupación). */
  columns: string[]
  filters: FilterRow[]
  /** Filtro guardado con una forma que las filas no representan (or/not): se conserva hasta que se agregue una fila. */
  keepFilter: string | null
  groupBy: string[]
  aggregates: AggregateRow[]
  showTotals: boolean
  sort: SortRow[]
  visibility: string
  shares: ShareDto[]
  /** Se reenvían tal cual para no borrar lo que otro cliente del API haya guardado (el PUT reemplaza todo). */
  scheduleCron: string | null
  deliveryEmails: string | null
  chartType: string | null
}

export function blankDraft(source: string): ViewDraft {
  return {
    id: null,
    name: '',
    source,
    secondary: [],
    columns: [],
    filters: [],
    keepFilter: null,
    groupBy: [],
    aggregates: [],
    showTotals: false,
    sort: [],
    visibility: 'PRIVATE',
    shares: [],
    scheduleCron: null,
    deliveryEmails: null,
    chartType: null,
  }
}

/** Borrador para editar una vista guardada. */
export function draftFromReport(r: ReportDto): ViewDraft {
  const group = parseGroupJson(r.groupJson)
  return {
    id: r.id ?? null,
    name: r.name ?? '',
    source: r.baseEntityType ?? '',
    secondary: [...(r.secondary ?? [])],
    columns: [...(r.columns ?? [])],
    filters: parseFilterRows(r.filterJson),
    keepFilter: filterIsUnrepresentable(r.filterJson) ? (r.filterJson ?? null) : null,
    groupBy: group.by,
    aggregates: group.aggregates,
    showTotals: group.totals,
    sort: parseSortJson(r.sortJson),
    visibility: r.visibility || 'PRIVATE',
    shares: (r.shares ?? []).map((s) => ({ userId: s.userId ?? null, roleId: s.roleId ?? null, canEdit: Boolean(s.canEdit) })),
    scheduleCron: r.scheduleCron ?? null,
    deliveryEmails: r.deliveryEmails ?? null,
    chartType: r.chartType ?? null,
  }
}

const usesRelation = (key: string, rel: string) => key.startsWith(`${rel}.`)

/** Quita del borrador todo lo que dependía de una fuente combinada que ya no está (columnas, filtros, grupos, agregados, orden). */
export function withoutRelation(d: ViewDraft, rel: string): ViewDraft {
  const groupBy = d.groupBy.filter((k) => !usesRelation(k, rel))
  return {
    ...d,
    secondary: d.secondary.filter((s) => s !== rel),
    columns: d.columns.filter((k) => !usesRelation(k, rel)),
    filters: d.filters.filter((f) => !usesRelation(f.field, rel)),
    groupBy,
    aggregates: groupBy.length === 0 ? [] : d.aggregates.filter((a) => !usesRelation(a.field, rel)),
    showTotals: groupBy.length === 0 ? false : d.showTotals,
    sort: d.sort.filter((s) => !usesRelation(s.field, rel)),
  }
}

export function withRelation(d: ViewDraft, rel: string, on: boolean): ViewDraft {
  if (!on) return withoutRelation(d, rel)
  return d.secondary.includes(rel) ? d : { ...d, secondary: [...d.secondary, rel] }
}

export function withColumn(d: ViewDraft, key: string, on: boolean): ViewDraft {
  if (on) return d.columns.includes(key) ? d : { ...d, columns: [...d.columns, key] }
  return { ...d, columns: d.columns.filter((k) => k !== key) }
}

/** Quitar el último campo de agrupación apaga también los agregados y la fila de totales (como la maqueta). */
export function withGroupBy(d: ViewDraft, groupBy: string[]): ViewDraft {
  return groupBy.length === 0 ? { ...d, groupBy, aggregates: [], showTotals: false, sort: d.sort.filter((s) => !isAggregateSortKey(s.field)) } : { ...d, groupBy }
}

function isAggregateSortKey(field: string): boolean {
  return /^(count|sum|avg|min|max)_/.test(field)
}

/** Agregado nuevo: conteo de filas (no necesita campo). */
export function newAggregate(): AggregateRow {
  return { fn: 'COUNT', field: '' }
}

/** Al cambiar la función: COUNT no necesita campo; las demás sí, y solo con un campo numérico. */
export function withAggregateFn(row: AggregateRow, fn: AggregateFn, numericKeys: readonly string[]): AggregateRow {
  if (fn === 'COUNT') return { fn, field: row.field }
  return { fn, field: numericKeys.includes(row.field) ? row.field : (numericKeys[0] ?? '') }
}

/** Campos por los que se puede ordenar: con agrupación, los de grupo y los agregados; sin ella, las columnas elegidas. */
export function sortChoices(d: ViewDraft, index: ReadonlyMap<string, ViewField>, aggLabel: (a: AggregateRow) => string): { key: string; label: string }[] {
  if (d.groupBy.length > 0) {
    return [
      ...d.groupBy.map((k) => ({ key: k, label: index.get(k)?.label ?? k })),
      ...d.aggregates.map((a) => ({ key: aggregateKey(a), label: aggLabel(a) })),
    ]
  }
  return d.columns.map((k) => ({ key: k, label: index.get(k)?.label ?? k }))
}

/** Orden que ya no tiene de dónde salir (columna desmarcada, agregado quitado): se descarta al guardar y al ver la vista previa. */
export function validSort(d: ViewDraft, choices: readonly { key: string }[]): SortRow[] {
  const keys = new Set(choices.map((c) => c.key))
  return d.sort.filter((s) => s.field && keys.has(s.field))
}

export interface DraftIssues {
  name?: 'nameRequired'
  columns?: 'columnsRequired'
  /** Posiciones de agregados que necesitan campo. */
  aggregateFields: number[]
}

/** Validaciones de la maqueta («Ponle un nombre», «Escoge al menos una columna», no se exige con agrupación) más el campo de cada agregado. */
export function validateDraft(d: ViewDraft): DraftIssues {
  const issues: DraftIssues = { aggregateFields: [] }
  if (!d.name.trim()) issues.name = 'nameRequired'
  if (d.groupBy.length === 0 && d.columns.length === 0) issues.columns = 'columnsRequired'
  d.aggregates.forEach((a, i) => {
    if (a.fn !== 'COUNT' && !a.field) issues.aggregateFields.push(i)
  })
  return issues
}

export function hasIssues(i: DraftIssues): boolean {
  return Boolean(i.name || i.columns || i.aggregateFields.length > 0)
}

/** Cuerpo de `POST/PUT /reports` y de la vista previa. `sortChoices` descarta el orden sin origen. */
export function buildReportBody(d: ViewDraft, choices: readonly { key: string }[]): ReportUpsertRequest {
  const grouped = d.groupBy.length > 0
  const filterJson = d.keepFilter ?? serializeFilterRows(d.filters)
  return {
    name: d.name.trim(),
    // null = no tocar las descripciones que ya tenga (el PUT las reemplaza solo si vienen)
    descriptions: null,
    visibility: d.visibility || 'PRIVATE',
    secondary: d.secondary,
    columns: grouped ? [] : d.columns,
    filterJson,
    sortJson: serializeSortJson(validSort(d, choices)),
    groupJson: serializeGroupJson({ by: d.groupBy, aggregates: d.aggregates, totals: d.showTotals }),
    chartType: d.chartType,
    scheduleCron: d.scheduleCron,
    deliveryEmails: d.deliveryEmails,
    shares: d.visibility === 'SHARED' ? d.shares : [],
  }
}

/** Petición de vista previa o `null` si la configuración está incompleta (sin columnas ni grupos, o un agregado sin campo). */
export function previewBody(d: ViewDraft, choices: readonly { key: string }[]): ReportUpsertRequest | null {
  if (d.groupBy.length === 0 && d.columns.length === 0) return null
  if (d.aggregates.some((a) => a.fn !== 'COUNT' && !a.field)) return null
  return buildReportBody({ ...d, name: d.name.trim() || '_' }, choices)
}

/** El filtro se puede agregar fila a fila: desde ese momento ya no se conserva el original. */
export function withFilterRows(d: ViewDraft, filters: FilterRow[]): ViewDraft {
  return { ...d, filters, keepFilter: null }
}

/** ¿Ya hay otra vista activa con ese nombre en la fuente? (el servidor repite la regla con 409; esta es la del cliente al EDITAR). */
export function nameTaken(name: string, source: string, reports: readonly ReportDto[], exceptId: number | null): boolean {
  const n = name.trim().toLowerCase()
  return reports.some((r) => r.isActive !== false && r.baseEntityType === source && r.id !== exceptId && (r.name ?? '').trim().toLowerCase() === n)
}

// --------------------------------------------------------------------------------------------------------------
// Compartir
// --------------------------------------------------------------------------------------------------------------

/** ¿Todas las personas elegidas pueden editar la vista? (sin personas: false). */
export function usersCanEdit(shares: readonly ShareDto[]): boolean {
  const users = shares.filter((s) => s.userId != null)
  return users.length > 0 && users.every((s) => s.canEdit)
}

export function setUsersCanEdit(shares: readonly ShareDto[], canEdit: boolean): ShareDto[] {
  return shares.map((s) => (s.userId != null ? { ...s, canEdit } : s))
}

// --------------------------------------------------------------------------------------------------------------
// Galería
// --------------------------------------------------------------------------------------------------------------

export interface SourceGroup {
  source: DataSource
  views: ReportDto[]
}

/** ¿Puede este usuario editar o eliminar la vista? El API ya lo dice en `canEdit`; las de sistema nunca. */
export function canManageView(r: Pick<ReportDto, 'isSystem' | 'canEdit'>): boolean {
  return !r.isSystem && Boolean(r.canEdit)
}

/**
 * Una entrada por fuente de datos (solo las que devuelve el API), ordenadas por nombre, con sus vistas activas: las de sistema
 * primero y luego por nombre. `matches(texto)` filtra por nombre de la vista o de la fuente; una fuente sin coincidencias se oculta.
 */
export function groupReportsBySource(reports: readonly ReportDto[], sources: readonly DataSource[], matches?: (...texts: string[]) => boolean): SourceGroup[] {
  const groups: SourceGroup[] = []
  for (const source of [...sources].sort((a, b) => collator.compare(a.label ?? a.key ?? '', b.label ?? b.key ?? ''))) {
    const sourceMatch = !matches || matches(source.label ?? '', source.key ?? '')
    const views = reports
      .filter((r) => r.isActive !== false && r.baseEntityType === source.key)
      .filter((r) => sourceMatch || !matches || matches(r.name ?? ''))
      .sort((a, b) => Number(b.isSystem ?? false) - Number(a.isSystem ?? false) || collator.compare(a.name ?? '', b.name ?? ''))
    if (matches && !sourceMatch && views.length === 0) continue
    groups.push({ source, views })
  }
  return groups
}

export interface ViewSubtitle {
  kind: 'grouped' | 'columns'
  /** Campos de agrupación rotulados ("Cliente, Estado") o número de columnas. */
  fields: string
  columns: number
}

export function viewSubtitle(r: ReportDto, index: ReadonlyMap<string, ViewField>): ViewSubtitle {
  const group = parseGroupJson(r.groupJson)
  if (group.by.length > 0) return { kind: 'grouped', fields: group.by.map((k) => index.get(k)?.label ?? k).join(', '), columns: 0 }
  return { kind: 'columns', fields: '', columns: (r.columns ?? []).length }
}

// --------------------------------------------------------------------------------------------------------------
// Resultado de una vista
// --------------------------------------------------------------------------------------------------------------

/**
 * Rotula el resultado con lo que el servidor no sabe: columnas de agregados («Suma · Total» y no «Suma (CodAmount)»; un conteo
 * nunca es dinero) y campos personalizados (etiqueta y tipo reales: el servidor los manda como texto con su clave).
 * No toca filas ni totales.
 */
export function decorateResult(
  result: ReportRunResult | null | undefined,
  report: Pick<ReportDto, 'groupJson'> | null,
  index: ReadonlyMap<string, ViewField>,
  fnLabel: (fn: AggregateFn) => string,
): ReportRunResult | null | undefined {
  if (!result) return result
  const aggs = new Map(parseGroupJson(report?.groupJson).aggregates.map((a) => [aggregateKey(a), a]))
  const columns = (result.columns ?? []).map<ReportResultColumn>((c) => {
    const key = c.key ?? ''
    const agg = aggs.get(key)
    if (agg) {
      const field = agg.field ? index.get(agg.field) : undefined
      const label = agg.field ? `${fnLabel(agg.fn)} · ${field?.label ?? agg.field}` : fnLabel(agg.fn)
      return { ...c, label, type: 'Number', isMoney: agg.fn !== 'COUNT' && Boolean(field?.isMoney) }
    }
    const known = index.get(key)
    if (known?.custom) return { ...c, label: known.label, type: known.type, isMoney: known.isMoney }
    return c
  })
  return { ...result, columns }
}

/** Rango de fechas de la pantalla de resultado: cuerpo de `POST /reports/{id}/run`. `null` = rango personalizado incompleto o invertido. */
export function runBody(mode: string, from: string, to: string, hasDateField: boolean): ReportRunRequest | null {
  if (!hasDateField || !mode || mode === 'ALL') return { dateRangeMode: 'ALL', take: RUN_TAKE }
  if (mode === 'CUSTOM') {
    if (!from || !to || from > to) return null
    return { dateRangeMode: 'CUSTOM', dateFrom: from, dateTo: to, take: RUN_TAKE }
  }
  return { dateRangeMode: mode, take: RUN_TAKE }
}
