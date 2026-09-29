// Lote F8a (P3) — lógica pura de Indicadores y Gráficos: agrupación por módulo de negocio, filas de filtro (campo ·
// operador · valor) serializadas al `filterJson` que valida el API (mismos operadores que `kernel/dsl`), y el cuerpo
// de `IndicatorUpsertRequest`/`ChartUpsertRequest` a partir del formulario o de la definición ya guardada (para
// reenviar el PUT completo al cambiar "Mostrar en Pulso de la compañía": el endpoint es un upsert, no un PATCH).
// Sin React: se prueba sola.
import type { components } from '../../kernel/api/schema'
import { moduleGroup, type ModuleGroup } from './pulseLayout'

export type AnalyticsDefinition = components['schemas']['AnalyticsDefinitionDto']
export type ShareDto = components['schemas']['ShareDto']
export type IndicatorUpsertRequest = components['schemas']['IndicatorUpsertRequest']
export type ChartUpsertRequest = components['schemas']['ChartUpsertRequest']
export type DataSource = components['schemas']['DataSourceDto']
export type DataField = components['schemas']['DataFieldDto']

/** Código del modo de rango personalizado (DateRangeModes.Custom del dominio); igual que `format.ts`. */
export const CUSTOM_RANGE = 'CUSTOM'

// --------------------------------------------------------------------------------------------------------------
// Agrupación por módulo de negocio (businessModule): orden de la maqueta (Operación → Almacén → Contabilidad),
// cualquier código que este frontend no reconozca va al final, en orden alfabético (no debería pasar: BusinessModule
// del catálogo solo trae esos tres).
// --------------------------------------------------------------------------------------------------------------

const MODULE_ORDER = ['OPERATIONS', 'WAREHOUSE', 'ACCOUNTING'] as const

const byName = new Intl.Collator('es', { sensitivity: 'base', numeric: true })

export interface DefinitionGroup<T> {
  /** Código de BusinessModule (o '' si la definición no trae ninguno). */
  module: string
  group: ModuleGroup
  items: T[]
}

/** Agrupa por `businessModule`, cada grupo ordenado por nombre; grupos vacíos no se incluyen. */
export function groupDefinitions<T extends { businessModule?: string | null; name?: string | null }>(
  items: readonly T[] | null | undefined,
): DefinitionGroup<T>[] {
  const byModule = new Map<string, T[]>()
  for (const item of items ?? []) {
    const mod = item.businessModule ?? ''
    const arr = byModule.get(mod)
    if (arr) arr.push(item)
    else byModule.set(mod, [item])
  }
  const known = MODULE_ORDER.filter((m) => byModule.has(m))
  const rest = [...byModule.keys()].filter((m) => !(MODULE_ORDER as readonly string[]).includes(m)).sort()
  return [...known, ...rest].map((module) => ({
    module,
    group: moduleGroup(module),
    items: [...(byModule.get(module) ?? [])].sort((a, b) => byName.compare(a.name ?? '', b.name ?? '')),
  }))
}

// --------------------------------------------------------------------------------------------------------------
// Filtro del editor: filas campo · operador · valor sobre los operadores del DSL (`kernel/dsl`). Serializa a
// `{"and":[{field,op,value}, ...]}` (o `null` sin filas), que el DSL del cliente y el del servidor leen igual.
// --------------------------------------------------------------------------------------------------------------

export const FILTER_OPS = [
  'eq',
  'ne',
  'gt',
  'gte',
  'lt',
  'lte',
  'contains',
  'startsWith',
  'endsWith',
  'in',
  'notIn',
  'between',
  'isNull',
  'notNull',
  'isTrue',
  'isFalse',
] as const
export type FilterOp = (typeof FILTER_OPS)[number]

/** Operadores sin valor (isNull, notNull, isTrue, isFalse). */
export const NO_VALUE_OPS: ReadonlySet<string> = new Set(['isNull', 'notNull', 'isTrue', 'isFalse'])
/** Operadores de lista (in, notIn): el valor es una lista separada por comas. */
export const LIST_OPS: ReadonlySet<string> = new Set(['in', 'notIn'])
/** Operador de rango (between): dos valores. */
export const RANGE_OPS: ReadonlySet<string> = new Set(['between'])

export interface FilterRow {
  field: string
  op: FilterOp
  /** Único valor, o el primero de un rango, o la lista separada por comas. */
  value: string
  /** Segundo valor de un rango (`between`). */
  value2: string
}

export function blankFilterRow(field: string): FilterRow {
  return { field, op: 'eq', value: '', value2: '' }
}

/** Número si el texto se ve como uno (para no perder el tipo al comparar en el servidor); si no, el texto tal cual. */
function coerceValue(raw: string): string | number {
  const trimmed = raw.trim()
  return trimmed !== '' && /^-?\d+(\.\d+)?$/.test(trimmed) ? Number(trimmed) : raw
}

/** Cuerpo del filtro (`{"and":[...]}`) a partir de las filas del editor; sin filas, `null`. */
export function serializeFilterRows(rows: readonly FilterRow[]): string | null {
  const conditions = rows
    .filter((r) => r.field)
    .map((r) => {
      if (NO_VALUE_OPS.has(r.op)) return { field: r.field, op: r.op }
      if (LIST_OPS.has(r.op)) {
        const value = r.value
          .split(',')
          .map((v) => v.trim())
          .filter((v) => v !== '')
          .map(coerceValue)
        return { field: r.field, op: r.op, value }
      }
      if (RANGE_OPS.has(r.op)) return { field: r.field, op: r.op, value: [coerceValue(r.value), coerceValue(r.value2)] }
      return { field: r.field, op: r.op, value: coerceValue(r.value) }
    })
  return conditions.length === 0 ? null : JSON.stringify({ and: conditions })
}

function isPlainAnd(v: unknown): v is { and: unknown[] } {
  return !!v && typeof v === 'object' && Array.isArray((v as Record<string, unknown>).and)
}

/**
 * Filas del editor a partir de un `filterJson` guardado, o `null` si no se puede representar como filas (usa
 * `or`/`not`, o cualquier forma que este editor no arma). Distinto de "sin filtro" (`[]` genuino): quien llama debe
 * distinguirlos con `filterIsUnrepresentable` para no reemplazar en silencio un filtro que no sabe editar.
 */
function tryParseFilterRows(json: string | null | undefined): FilterRow[] | null {
  if (!json) return []
  let root: unknown
  try {
    root = JSON.parse(json)
  } catch {
    return null
  }
  const list = Array.isArray(root) ? root : isPlainAnd(root) ? root.and : [root]
  if (!Array.isArray(list)) return null
  const rows: FilterRow[] = []
  for (const node of list) {
    if (!node || typeof node !== 'object') return null
    const n = node as Record<string, unknown>
    if (typeof n.field !== 'string') return null
    const op = (typeof n.op === 'string' ? n.op : 'eq') as FilterOp
    if (NO_VALUE_OPS.has(op)) {
      rows.push({ field: n.field, op, value: '', value2: '' })
    } else if (RANGE_OPS.has(op) && Array.isArray(n.value) && n.value.length === 2) {
      rows.push({ field: n.field, op, value: String(n.value[0] ?? ''), value2: String(n.value[1] ?? '') })
    } else if (LIST_OPS.has(op) && Array.isArray(n.value)) {
      rows.push({ field: n.field, op, value: n.value.map((v) => String(v)).join(', '), value2: '' })
    } else {
      rows.push({ field: n.field, op, value: n.value == null ? '' : String(n.value), value2: '' })
    }
  }
  return rows
}

/** Filas del editor a partir de un `filterJson` guardado; `[]` tanto si no hay filtro como si no se puede representar. */
export function parseFilterRows(json: string | null | undefined): FilterRow[] {
  return tryParseFilterRows(json) ?? []
}

/** true si el filtro guardado usa una forma (`or`/`not`, u otra) que este editor de filas no puede representar. */
export function filterIsUnrepresentable(json: string | null | undefined): boolean {
  return tryParseFilterRows(json) === null
}

// --------------------------------------------------------------------------------------------------------------
// Cuerpo de la petición (IndicatorUpsertRequest / ChartUpsertRequest) a partir del formulario del editor.
// --------------------------------------------------------------------------------------------------------------

export interface DefinitionFormValues {
  name: string
  descriptionEs: string
  descriptionEn: string
  dataSource: string
  aggregateFn: string
  field: string
  businessModule: string
  isMoney: boolean
  visibility: string
  dateRangeMode: string
  dateFrom: string
  dateTo: string
  sortOrder?: number | null
  showInPulse: boolean
  /** Solo gráficos. */
  groupByField: string
  /** Solo gráficos. */
  chartType: string
}

/**
 * `descriptions: null` deja la descripción actual sin tocar (el API así lo trata); pasarlo solo cuando el editor
 * realmente tiene los dos idiomas en pantalla. `filterJson` se recibe ya resuelto por quien llama: desde el editor,
 * serializado de las filas; desde una acción que no toca el filtro (p. ej. "Mostrar en el Pulso de la compañía"), el
 * `filterJson` original tal cual — nunca reconstruido de filas, que perdería un filtro con `or`/`not`.
 */
function commonUpsertFields(
  values: DefinitionFormValues,
  descriptions: Record<string, string> | null,
  filterJson: string | null,
  shares: readonly ShareDto[],
) {
  const custom = values.dateRangeMode === CUSTOM_RANGE
  return {
    name: values.name,
    descriptions,
    dataSource: values.dataSource,
    field: values.aggregateFn === 'COUNT' ? null : values.field || null,
    aggregateFn: values.aggregateFn,
    filterJson,
    businessModule: values.businessModule || null,
    isMoney: values.isMoney,
    visibility: values.visibility || null,
    dateRangeMode: values.dateRangeMode || null,
    dateFrom: custom ? values.dateFrom || null : null,
    dateTo: custom ? values.dateTo || null : null,
    showInPulse: values.showInPulse,
    shares: [...shares],
    sortOrder: values.sortOrder ?? null,
  }
}

export function buildIndicatorRequest(
  values: DefinitionFormValues,
  descriptions: Record<string, string> | null,
  filterJson: string | null,
  shares: readonly ShareDto[],
): IndicatorUpsertRequest {
  return commonUpsertFields(values, descriptions, filterJson, shares)
}

export function buildChartRequest(
  values: DefinitionFormValues,
  descriptions: Record<string, string> | null,
  filterJson: string | null,
  shares: readonly ShareDto[],
): ChartUpsertRequest {
  return { ...commonUpsertFields(values, descriptions, filterJson, shares), groupByField: values.groupByField, chartType: values.chartType || null }
}

/** Valores iniciales del editor a partir de una definición existente (`descriptions` trae cada idioma por separado;
 *  sin él, vacío — no se rellena con el resuelto del otro idioma, que ya no hace falta desde que el API lo expone). */
export function definitionToFormValues(dto: AnalyticsDefinition | null | undefined, kind: 'indicator' | 'chart'): DefinitionFormValues {
  return {
    name: dto?.name ?? '',
    descriptionEs: dto?.descriptions?.es ?? '',
    descriptionEn: dto?.descriptions?.en ?? '',
    dataSource: dto?.dataSource ?? '',
    aggregateFn: dto?.aggregateFn ?? 'COUNT',
    field: dto?.field ?? '',
    businessModule: dto?.businessModule ?? '',
    isMoney: dto?.isMoney ?? false,
    visibility: dto?.visibility ?? 'PRIVATE',
    dateRangeMode: dto?.dateRangeMode ?? '',
    dateFrom: dto?.dateFrom ?? '',
    dateTo: dto?.dateTo ?? '',
    sortOrder: dto?.sortOrder ?? null,
    showInPulse: dto?.showInPulse ?? false,
    groupByField: kind === 'chart' ? (dto?.groupByField ?? '') : '',
    chartType: kind === 'chart' ? (dto?.chartType ?? 'BAR') : '',
  }
}

/**
 * Cuerpo completo del PUT a partir de la definición ya guardada, con `showInPulse` cambiado: el endpoint es un
 * upsert completo (no un PATCH), así que cambiar "Mostrar en Pulso de la compañía" desde la tarjeta reenvía todo lo
 * demás tal cual venía. No toca ni la descripción (`descriptions: null` = no cambiarla) ni el filtro (se reenvía el
 * `filterJson` original, sin pasar por filas: reconstruirlo de filas perdería un filtro con `or`/`not`).
 */
export function withShowInPulse(dto: AnalyticsDefinition, kind: 'indicator', next: boolean): IndicatorUpsertRequest
export function withShowInPulse(dto: AnalyticsDefinition, kind: 'chart', next: boolean): ChartUpsertRequest
export function withShowInPulse(dto: AnalyticsDefinition, kind: 'indicator' | 'chart', next: boolean): IndicatorUpsertRequest | ChartUpsertRequest {
  const values = { ...definitionToFormValues(dto, kind), showInPulse: next }
  const shares = dto.shares ?? []
  return kind === 'indicator'
    ? buildIndicatorRequest(values, null, dto.filterJson ?? null, shares)
    : buildChartRequest(values, null, dto.filterJson ?? null, shares)
}

/** Campos de la fuente (propios y personalizados) que se ofrecen en un `Select` del editor. */
export function sourceFieldOptions(source: DataSource | null | undefined): DataField[] {
  return [...(source?.fields ?? []), ...(source?.customFields ?? [])]
}

/** Chip de visibilidad de la tarjeta: Todos / Privado / Compartido (n). */
export function visibilityBadgeKey(visibility: string | null | undefined): 'all' | 'private' | 'shared' {
  if (visibility === 'TENANT') return 'all'
  if (visibility === 'SHARED') return 'shared'
  return 'private'
}

/** Texto del rango efectivo (el que se usó para calcular el valor actual): null si no aplica o es "todo el tiempo". */
export function effectiveRangeCaption(
  dto: Pick<AnalyticsDefinition, 'effectiveDateRangeMode' | 'effectiveDateFrom' | 'effectiveDateTo'>,
  modeLabel: (code: string) => string,
  formatDate: (ymd: string) => string,
): string | null {
  const mode = dto.effectiveDateRangeMode
  if (!mode || mode === 'ALL') return null
  if (mode === CUSTOM_RANGE) {
    if (!dto.effectiveDateFrom && !dto.effectiveDateTo) return null
    return `${formatDate(dto.effectiveDateFrom ?? '')} → ${formatDate(dto.effectiveDateTo ?? '')}`
  }
  return modeLabel(mode)
}
