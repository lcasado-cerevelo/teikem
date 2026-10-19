// Lote F-B — Consignatarios (Catálogo → Consignatarios). Lógica pura: tipos del API, esquemas zod espejo del servidor
// (LocationService, manual 02 §2), armado de los requests (alta y PATCH solo de lo cambiado), búsqueda local de la lista y
// reglas de la importación por CSV (plantillas por posición de columna, espejo de ConsigneeImportMapping). Sin React.
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'
import { matchesQ } from '../../kernel/ui/matchesQ'

type Schemas = components['schemas']

export type Consignee = Schemas['LocationDto']
export type ConsigneeCreateRequest = Schemas['LocationUpsertRequest']
export type ConsigneePatchRequest = Schemas['LocationPatchRequest']
export type ContactPoint = Schemas['ContactPointDto']
export type ImportTemplate = Schemas['ImportTemplateDto']
export type ImportTemplateCreateRequest = Schemas['ImportTemplateCreateRequest']
export type ImportTemplatePatchRequest = Schemas['ImportTemplatePatchRequest']
export type ImportField = Schemas['ImportFieldDto']
export type ImportPreview = Schemas['ConsigneeImportPreviewDto']
export type ImportRow = Schemas['ConsigneeImportRowDto']
export type ImportResult = Schemas['ConsigneeImportResultDto']
export type ImportFileInspection = Schemas['ImportFileInspectionDto']
export type ImportTemplateColumn = Schemas['ImportTemplateColumnDto']
/** Lote F-B3: variante de mapeo de una plantilla de consignatarios (condición por fila y columnas propias). */
export type ImportTemplateVariant = Schemas['ImportTemplateVariantDto']
export type ImportVariantCondition = Schemas['ImportVariantConditionDto']

/** Tipo de localización de un consignatario (la pantalla lo fija; no se muestra). */
export const DELIVERY_TYPE = 'DELIVERY'
/** País por defecto (el servidor exige uno; sin él usa PR). */
export const DEFAULT_COUNTRY = 'PR'
/** Tipo de dueño de los medios de contacto y de los campos personalizados. */
export const LOCATION_ENTITY_TYPE = 'LOCATION'
export const PHONE_TYPE = 'PHONE'

// ---- topes (= columnas de dbo.Location; los mismos de LocationService) ----
export const NAME_MAX = 200
export const CODE_MAX = 40
export const LINE_MAX = 200
export const CITY_MAX = 100
export const STATE_MAX = 100
export const POSTAL_MAX = 20
export const NOTES_MAX = 500
/** Lote F-B2: persona de contacto (Location.ContactName). */
export const CONTACT_NAME_MAX = 150

/** Valor del filtro de cliente / dueño que significa «sin dueño» (compartidos). */
export const SHARED = ''
/** Valor del filtro de la pantalla: todos los clientes. */
export const FILTER_ALL = 'all'
/** Valor del filtro de la pantalla: solo los compartidos (se resuelve en el cliente por `isShared`). */
export const FILTER_SHARED = 'shared'

// ---------------------------------------------------------------- formulario (alta y edición)

export interface ConsigneeMessages {
  nameRequired: string
  line1Required: string
  cityRequired: string
  countryRequired: string
  max: (max: number) => string
  minutesInteger: string
  minutesNegative: string
  windowPair: string
  windowOrder: string
  phoneInvalid: string
}

export interface ConsigneeValues {
  code: string
  name: string
  line1: string
  line2: string
  city: string
  state: string
  postalCode: string
  country: string
  /** publicId del cliente dueño; '' = compartido (sin dueño). */
  clientPublicId: string
  /** Teléfono con la máscara de la compañía ('' = sin teléfono). */
  phone: string
  /** Persona de contacto (Lote F-B2; '' = sin persona). */
  contactName: string
  defaultServiceMinutes: number | null
  /** 'HH:mm' o ''. */
  defaultWindowStart: string
  defaultWindowEnd: string
  accessNotes: string
  deliveryNotes: string
  allowDupInvoice: boolean
}

export function consigneeSchema(m: ConsigneeMessages, isValidPhone: (v: string) => boolean) {
  const optional = (max: number) => z.string().trim().max(max, m.max(max))
  return z
    .object({
      code: optional(CODE_MAX),
      name: z.string().trim().min(1, m.nameRequired).max(NAME_MAX, m.max(NAME_MAX)),
      line1: z.string().trim().min(1, m.line1Required).max(LINE_MAX, m.max(LINE_MAX)),
      line2: optional(LINE_MAX),
      city: z.string().trim().min(1, m.cityRequired).max(CITY_MAX, m.max(CITY_MAX)),
      state: optional(STATE_MAX),
      postalCode: optional(POSTAL_MAX),
      country: z.string().trim().min(1, m.countryRequired),
      clientPublicId: z.string(),
      phone: z.string().refine((v) => v.trim() === '' || isValidPhone(v), m.phoneInvalid),
      contactName: optional(CONTACT_NAME_MAX),
      defaultServiceMinutes: z.number(m.minutesInteger).int(m.minutesInteger).min(0, m.minutesNegative).nullable(),
      defaultWindowStart: z.string(),
      defaultWindowEnd: z.string(),
      accessNotes: optional(NOTES_MAX),
      deliveryNotes: optional(NOTES_MAX),
      allowDupInvoice: z.boolean(),
    })
    .superRefine((v, ctx) => {
      const start = v.defaultWindowStart.trim()
      const end = v.defaultWindowEnd.trim()
      if (!start && !end) return
      if (!start) ctx.addIssue({ code: 'custom', path: ['defaultWindowStart'], message: m.windowPair })
      else if (!end) ctx.addIssue({ code: 'custom', path: ['defaultWindowEnd'], message: m.windowPair })
      else if (start >= end) ctx.addIssue({ code: 'custom', path: ['defaultWindowEnd'], message: m.windowOrder })
    })
}

/** 'HH:mm:ss' del API → 'HH:mm' del campo de hora ('' sin valor). */
export function timeInput(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : ''
}

/** 'HH:mm' del campo → 'HH:mm:00' que espera el API. */
export function timeForApi(value: string): string {
  return value.length === 5 ? `${value}:00` : value
}

/** Valores iniciales del alta. `ownerClientId` preselecciona el dueño con el filtro de la pantalla. */
export function emptyConsigneeValues(ownerClientId = SHARED): ConsigneeValues {
  return {
    code: '',
    name: '',
    line1: '',
    line2: '',
    city: '',
    state: '',
    postalCode: '',
    country: DEFAULT_COUNTRY,
    clientPublicId: ownerClientId,
    phone: '',
    contactName: '',
    defaultServiceMinutes: 0,
    defaultWindowStart: '',
    defaultWindowEnd: '',
    accessNotes: '',
    deliveryNotes: '',
    allowDupInvoice: false,
  }
}

/** Valores del formulario de edición a partir del consignatario (y su teléfono ya con máscara). */
export function consigneeValuesOf(c: Consignee, phone: string): ConsigneeValues {
  return {
    code: c.code ?? '',
    name: c.name ?? '',
    line1: c.line1 ?? '',
    line2: c.line2 ?? '',
    city: c.city ?? '',
    state: c.state ?? '',
    postalCode: c.postalCode ?? '',
    country: c.country ?? DEFAULT_COUNTRY,
    clientPublicId: c.isShared ? SHARED : (c.clientPublicId ?? SHARED),
    phone,
    contactName: c.contactName ?? '',
    defaultServiceMinutes: c.defaultServiceMinutes ?? 0,
    defaultWindowStart: timeInput(c.defaultWindowStart),
    defaultWindowEnd: timeInput(c.defaultWindowEnd),
    accessNotes: c.accessNotes ?? '',
    deliveryNotes: c.deliveryNotes ?? '',
    allowDupInvoice: c.allowDupInvoice ?? false,
  }
}

/** El esquema infiere `defaultServiceMinutes` como `number | null`; aquí también se acepta ausente. */
export type ConsigneeInput = Omit<ConsigneeValues, 'defaultServiceMinutes'> & { defaultServiceMinutes?: number | null }

/** POST /locations: tipo fijo DELIVERY; vacío → null; ventana solo si trae inicio y fin. */
export function buildCreateRequest(v: ConsigneeInput): ConsigneeCreateRequest {
  const start = v.defaultWindowStart.trim()
  const end = v.defaultWindowEnd.trim()
  const hasWindow = start !== '' && end !== ''
  return {
    clientPublicId: v.clientPublicId || null,
    code: v.code.trim() || null,
    name: v.name.trim(),
    locationType: DELIVERY_TYPE,
    line1: v.line1.trim(),
    line2: v.line2.trim() || null,
    city: v.city.trim(),
    state: v.state.trim() || null,
    postalCode: v.postalCode.trim() || null,
    country: v.country.trim() || DEFAULT_COUNTRY,
    defaultServiceMinutes: v.defaultServiceMinutes ?? 0,
    defaultWindowStart: hasWindow ? timeForApi(start) : null,
    defaultWindowEnd: hasWindow ? timeForApi(end) : null,
    accessNotes: v.accessNotes.trim() || null,
    deliveryNotes: v.deliveryNotes.trim() || null,
    allowDupInvoice: v.allowDupInvoice,
    contactName: v.contactName.trim() || null,
  }
}

/**
 * PATCH /locations/{id}: solo lo que cambió respecto de `original` (null = no tocar; '' borra un texto opcional).
 * Dueño: '' → `makeShared`, otro → `clientPublicId`. Ventana: ambas vacías (y había) → `clearWindow`. Siempre lleva `rowVersion`.
 * `changed` = hay algo que mandar (el teléfono se guarda aparte, por ContactPoint).
 */
export function buildPatchRequest(v: ConsigneeInput, original: ConsigneeValues, rowVersion: string | null | undefined): { request: ConsigneePatchRequest; changed: boolean } {
  const req: ConsigneePatchRequest = { rowVersion: rowVersion ?? null }
  let changed = false
  const text = (key: 'code' | 'name' | 'line1' | 'line2' | 'city' | 'state' | 'postalCode' | 'accessNotes' | 'deliveryNotes' | 'contactName') => {
    const next = v[key].trim()
    if (next !== original[key].trim()) {
      req[key] = next
      changed = true
    }
  }
  text('code')
  text('name')
  text('line1')
  text('line2')
  text('city')
  text('state')
  text('postalCode')
  text('accessNotes')
  text('deliveryNotes')
  text('contactName')
  if (v.country.trim() !== original.country.trim()) {
    req.country = v.country.trim()
    changed = true
  }
  if (v.clientPublicId !== original.clientPublicId) {
    if (v.clientPublicId === SHARED) req.makeShared = true
    else req.clientPublicId = v.clientPublicId
    changed = true
  }
  if ((v.defaultServiceMinutes ?? 0) !== (original.defaultServiceMinutes ?? 0)) {
    req.defaultServiceMinutes = v.defaultServiceMinutes ?? 0
    changed = true
  }
  if (v.allowDupInvoice !== original.allowDupInvoice) {
    req.allowDupInvoice = v.allowDupInvoice
    changed = true
  }
  const start = v.defaultWindowStart.trim()
  const end = v.defaultWindowEnd.trim()
  if (start !== original.defaultWindowStart || end !== original.defaultWindowEnd) {
    if (start === '' && end === '') req.clearWindow = true
    else {
      req.defaultWindowStart = timeForApi(start)
      req.defaultWindowEnd = timeForApi(end)
    }
    changed = true
  }
  return { request: req, changed }
}

/** El teléfono de un consignatario: el principal activo de tipo teléfono (no correo) o el primero que haya. */
export function pickPhone(points: readonly ContactPoint[] | undefined): ContactPoint | undefined {
  const phones = (points ?? []).filter((p) => p.isActive !== false && (p.contactType ?? '').toUpperCase() !== 'EMAIL')
  return phones.find((p) => p.isPrimary) ?? phones[0]
}

// ---------------------------------------------------------------- lista

/** Dueño visible de la fila: el cliente o «Compartido (todos los clientes)». */
export function ownerLabel(c: Consignee, shared: string): string {
  return c.isShared ? shared : (c.clientName ?? '')
}

/** Búsqueda libre de la lista (maqueta: número, nombre, dirección, pueblo, dueño y estado). */
export function matchesConsignee(c: Consignee, q: string, labels: { shared: string; active: string; inactive: string }): boolean {
  return matchesQ(q, c.code, c.name, c.line1, c.line2, c.city, c.postalCode, c.contactName, ownerLabel(c, labels.shared), c.isActive ? labels.active : labels.inactive)
}

/** El filtro «Compartidos» se resuelve en el cliente; «Todos» y un cliente ya vienen filtrados por el servidor. */
export function applyOwnerFilter(list: readonly Consignee[], filter: string): Consignee[] {
  return filter === FILTER_SHARED ? list.filter((c) => c.isShared) : [...list]
}

/** Dirección en una línea (línea 1, línea 2, pueblo, estado y ZIP). */
export function addressLine(c: Consignee): string {
  return [c.line1, c.line2, [c.city, [c.state, c.postalCode].filter(Boolean).join(' ')].filter(Boolean).join(', ')].filter(Boolean).join(', ')
}

// ---------------------------------------------------------------- importación: plantillas

export const TEMPLATE_NAME_MAX = 120
export const MAX_IMPORT_ROWS = 5000
export const MAX_IMPORT_BYTES = 2 * 1024 * 1024

/** Campos que identifican el dueño: no pueden estar los dos en la plantilla. */
export const CLIENT_CODE_FIELD = 'clientCode'
export const CLIENT_NAME_FIELD = 'clientName'
export const REQUIRED_COLUMN_FIELDS = ['name', 'line1'] as const
export const CITY_FIELD = 'city'
/** Lote F-B3: con el ZIP basta (el pueblo sale del catálogo de códigos postales en Puerto Rico y EE. UU.). */
export const POSTAL_FIELD = 'postalCode'
export const STATE_FIELD = 'state'

/** Delimitadores que ofrece el editor (el servidor acepta cualquier carácter único menos comilla y salto de línea). */
export const DELIMITERS = [',', ';', '\t', '|'] as const

/** Columnas de la plantilla sembrada «Formato estándar» (la maqueta trae name, number, address, town, phone). */
export const STANDARD_COLUMNS = ['name', 'code', 'line1', 'city', 'contactPhone'] as const

export function standardTemplateRequest(name: string): ImportTemplateCreateRequest {
  return {
    name,
    delimiter: ',',
    hasHeader: true,
    columns: STANDARD_COLUMNS.map((field, i) => ({ position: i + 1, field })),
  }
}

export interface TemplateMessages {
  nameRequired: string
  nameMax: string
  columnsRequired: string
  nameLine1: string
  city: string
  clientBoth: string
  repeated: (field: string) => string
  noDefault: (field: string) => string
  defaultValueRequired: (field: string) => string
}

export interface TemplateValues {
  name: string
  delimiter: string
  hasHeader: boolean
  /** Una fila por columna del archivo, en orden; '' = «— no importar —». */
  columns: { field: string }[]
  defaults: { field: string; value: string }[]
  /** Lote 131 (solo plantillas de órdenes): el archivo trae varias líneas por orden y las reglas que dicen cuáles NO son pieza. */
  multiLine?: boolean
  lineRules?: LineRuleValues[]
}

/** Regla de línea en el formulario: la columna se escribe como texto (1…500). */
export interface LineRuleValues {
  kind: string
  position: string
  operator: string
  value: string
}

/** Errores de la definición (espejo de ConsigneeImportMapping.ValidateColumns): ruta → mensaje. */
export function templateIssues(
  v: Pick<TemplateValues, 'columns' | 'defaults'>,
  fields: readonly ImportField[],
  m: Pick<TemplateMessages, 'columnsRequired' | 'nameLine1' | 'city' | 'clientBoth' | 'repeated' | 'noDefault' | 'defaultValueRequired'>,
): { path: string; message: string }[] {
  const issues: { path: string; message: string }[] = []
  const chosen = v.columns.map((c) => c.field).filter(Boolean)
  const seen = new Set<string>()
  v.columns.forEach((c, i) => {
    if (!c.field) return
    if (seen.has(c.field)) issues.push({ path: `columns.${i}.field`, message: m.repeated(c.field) })
    seen.add(c.field)
  })
  const allowDefault = new Set(fields.filter((f) => f.defaultAllowed).map((f) => f.field ?? ''))
  const defaultFields = new Set<string>()
  v.defaults.forEach((d, i) => {
    if (!d.field) return
    if (fields.length > 0 && !allowDefault.has(d.field)) issues.push({ path: `defaults.${i}.field`, message: m.noDefault(d.field) })
    else if (!d.value.trim()) issues.push({ path: `defaults.${i}.value`, message: m.defaultValueRequired(d.field) })
    else defaultFields.add(d.field)
  })
  if (chosen.length === 0) issues.push({ path: 'columns', message: m.columnsRequired })
  else if (!REQUIRED_COLUMN_FIELDS.every((f) => seen.has(f))) issues.push({ path: 'columns', message: m.nameLine1 })
  else if (!seen.has(CITY_FIELD) && !defaultFields.has(CITY_FIELD) && !seen.has(POSTAL_FIELD) && !defaultFields.has(POSTAL_FIELD)) issues.push({ path: 'columns', message: m.city })
  else if (seen.has(CLIENT_CODE_FIELD) && seen.has(CLIENT_NAME_FIELD)) issues.push({ path: 'columns', message: m.clientBoth })
  return issues
}

/** Un problema de la definición de una plantilla: ruta del campo (`columns`, `columns.2.field`, `defaults.0.value`) y mensaje. */
export interface TemplateIssue {
  path: string
  message: string
}

/**
 * Esquema del editor de plantillas. `issues` son las reglas de la definición (por defecto las de consignatarios; el editor de
 * ÓRDENES de F-C3 pasa las suyas, `orderTemplateIssues`): el nombre se valida aquí y lo demás lo decide quien llama.
 */
export function templateSchema(
  m: Pick<TemplateMessages, 'nameRequired' | 'nameMax'> & Partial<TemplateMessages>,
  fields: readonly ImportField[],
  issues?: (v: TemplateValues) => TemplateIssue[],
) {
  const rules = issues ?? ((v: TemplateValues) => templateIssues(v, fields, m as TemplateMessages))
  return z
    .object({
      name: z.string().trim().min(1, m.nameRequired).max(TEMPLATE_NAME_MAX, m.nameMax),
      delimiter: z.string().min(1),
      hasHeader: z.boolean(),
      columns: z.array(z.object({ field: z.string() })),
      defaults: z.array(z.object({ field: z.string(), value: z.string() })),
      multiLine: z.boolean().optional(),
      lineRules: z.array(z.object({ kind: z.string(), position: z.string(), operator: z.string(), value: z.string() })).optional(),
    })
    .superRefine((v, ctx) => {
      for (const issue of rules(v)) ctx.addIssue({ code: 'custom', path: issue.path.split('.').map((p) => (/^\d+$/.test(p) ? Number(p) : p)), message: issue.message })
    })
}

export function templateValuesOf(t: ImportTemplate | null): TemplateValues {
  if (!t) return { name: '', delimiter: ',', hasHeader: true, columns: [{ field: '' }, { field: '' }, { field: '' }, { field: '' }, { field: '' }], defaults: [] }
  const columns = [...(t.columns ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0))
  const last = columns.reduce((max, c) => Math.max(max, c.position ?? 0), 0)
  const byPosition = new Map(columns.map((c) => [c.position ?? 0, c.field ?? '']))
  return {
    name: t.name ?? '',
    delimiter: t.delimiter || ',',
    hasHeader: t.hasHeader ?? true,
    // las posiciones sin campo se muestran como «— no importar —»
    columns: Array.from({ length: Math.max(last, 1) }, (_, i) => ({ field: byPosition.get(i + 1) ?? '' })),
    defaults: Object.entries(t.defaults ?? {}).map(([field, value]) => ({ field, value })),
    ...(t.multiLine === true
      ? { multiLine: true, lineRules: (t.lineRules ?? []).map((r) => ({ kind: r.kind ?? 'DESCRIPTION', position: String(r.position ?? ''), operator: r.operator ?? 'EQUALS', value: r.value ?? '' })) }
      : {}),
  }
}

/** Columnas del request: la posición es el orden en el editor (1…n); las ignoradas no se mandan. */
export function templateColumns(v: Pick<TemplateValues, 'columns'>) {
  return v.columns.flatMap((c, i) => (c.field ? [{ position: i + 1, field: c.field }] : []))
}

export function templateDefaults(v: Pick<TemplateValues, 'defaults'>): Record<string, string> {
  return Object.fromEntries(v.defaults.filter((d) => d.field && d.value.trim()).map((d) => [d.field, d.value.trim()]))
}

/**
 * Lote 131: varias líneas por orden y sus reglas. Solo se manda si está encendido o si la plantilla ya lo tenía (`was`): apagarlo manda `multiLine: false` y las
 * reglas vacías; una plantilla que nunca lo usó no lleva nada nuevo.
 */
export function templateMultiLine(v: Pick<TemplateValues, 'multiLine' | 'lineRules'>, was = false): { multiLine?: boolean; lineRules?: { kind: string; position: number; operator: string; value?: string }[] } {
  if (!v.multiLine) return was ? { multiLine: false, lineRules: [] } : {}
  return {
    multiLine: true,
    lineRules: (v.lineRules ?? []).map((r) => ({
      kind: r.kind,
      position: Number(r.position),
      operator: r.operator,
      ...(r.operator === 'EMPTY' || r.operator === 'NOT_EMPTY' || !r.value.trim() ? {} : { value: r.value.trim() }),
    })),
  }
}

export function buildTemplateCreate(v: TemplateValues): ImportTemplateCreateRequest {
  const defaults = templateDefaults(v)
  return {
    name: v.name.trim(),
    delimiter: v.delimiter,
    hasHeader: v.hasHeader,
    columns: templateColumns(v),
    defaults: Object.keys(defaults).length > 0 ? defaults : null,
    ...templateMultiLine(v),
  }
}

/** PATCH: columnas y defaults reemplazan la lista completa; sin defaults se manda `clearDefaults`. */
export function buildTemplatePatch(v: TemplateValues, wasMultiLine = false): ImportTemplatePatchRequest {
  const defaults = templateDefaults(v)
  const hasDefaults = Object.keys(defaults).length > 0
  return {
    name: v.name.trim(),
    delimiter: v.delimiter,
    hasHeader: v.hasHeader,
    columns: templateColumns(v),
    defaults: hasDefaults ? defaults : null,
    clearDefaults: hasDefaults ? null : true,
    ...templateMultiLine(v, wasMultiLine),
  }
}

/** Columnas de la plantilla en orden de posición y, dentro de un campo que une varias, de parte. */
function sortedColumns(t: ImportTemplate | undefined): ImportTemplateColumn[] {
  return [...(t?.columns ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0) || (a.part ?? 0) - (b.part ?? 0))
}

/**
 * Resumen «1. Nombre · 2. Número…» de la plantilla elegida. Lote F-B2: un campo que une columnas sale una vez con sus columnas en
 * orden («1+2. Nombre») y las «(No usar)» van juntas al final («3, 5. (No usar)»). Las plantillas de órdenes no traen ninguna de las dos.
 */
export function templateSummary(t: ImportTemplate | undefined, label: (field: string) => string): string {
  const byField = new Map<string, { position: number; part: number }[]>()
  const ignored: number[] = []
  for (const c of sortedColumns(t)) {
    const field = c.field ?? ''
    if (field === IGNORE_FIELD) {
      ignored.push(c.position ?? 0)
      continue
    }
    byField.set(field, [...(byField.get(field) ?? []), { position: c.position ?? 0, part: c.part ?? 0 }])
  }
  const parts = [...byField].map(([field, list]) => `${[...list].sort((a, b) => a.part - b.part || a.position - b.position).map((x) => x.position).join('+')}. ${label(field)}`)
  if (ignored.length > 0) parts.push(`${ignored.join(', ')}. ${label(IGNORE_FIELD)}`)
  return parts.join(' · ')
}

/**
 * Campos que la plantilla llena (sin «No usar»), en el orden de su primera columna: columnas de la vista previa. Lote F-B3: con
 * variantes, los de todas (en el orden de la 1 y después los que agregan las demás).
 */
export function templateFields(t: ImportTemplate | undefined): string[] {
  const out: string[] = []
  const sources: (ImportTemplate | undefined)[] = (t?.variants?.length ?? 0) > 0 ? (t?.variants ?? []).map((v) => ({ columns: v.columns }) as ImportTemplate) : [t]
  for (const source of sources)
    for (const c of sortedColumns(source)) {
      const field = c.field ?? ''
      if (field && field !== IGNORE_FIELD && !out.includes(field)) out.push(field)
    }
  return out
}

/**
 * Lote F-B3 — columnas de la vista previa: las de la plantilla y, si alguna fila los dedujo del catálogo de códigos postales, el pueblo,
 * el estado y el ZIP (aunque la plantilla no los traiga).
 */
export function previewFields(t: ImportTemplate | undefined, rows: readonly Pick<ImportRow, 'inferred'>[]): string[] {
  const out = templateFields(t)
  for (const field of [CITY_FIELD, STATE_FIELD, POSTAL_FIELD]) if (!out.includes(field) && rows.some((r) => r.inferred?.includes(field))) out.push(field)
  return out
}

/** Lote F-B3 — ¿la plantilla (o alguna de sus variantes) usa alguno de esos campos? */
export function templateUsesField(t: ImportTemplate | undefined, ...fields: string[]): boolean {
  const columns = [...(t?.columns ?? []), ...(t?.variants ?? []).flatMap((v) => v.columns ?? [])]
  return columns.some((c) => fields.includes(c.field ?? ''))
}

// ---------------------------------------------------------------- importación: mapeo de consignatarios (Lote F-B2)

/** Marca de columna «(No usar)» (espejo de ConsigneeImportFields.Ignore): se guarda en la plantilla y la columna se ignora; la fila sigue. */
export const IGNORE_FIELD = 'ignore'
/** Campos con formato propio: toman UNA sola columna (espejo de ConsigneeImportFields.SingleColumn). */
export const SINGLE_COLUMN_FIELDS: readonly string[] = ['country', 'clientCode', 'clientName', 'defaultServiceMinutes', 'defaultWindowStart', 'defaultWindowEnd', 'allowDupInvoice']
export const MAX_JOIN_PARTS = 5
export const SEPARATOR_MAX = 10
/** Separador entre dos columnas unidas cuando no se escribe otro. */
export const DEFAULT_SEPARATOR = ' '
/** Archivos que acepta la importación: CSV en UTF-8 o Excel .xlsx (lo lee el servidor). */
export const IMPORT_FILE_ACCEPT = '.csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'

/** Una columna del archivo en el editor: los campos que alimenta; `fields[0].field === ''` = «(No usar)». */
export interface MappingColumn {
  fields: { field: string }[]
}

/**
 * Un campo que une varias columnas: `positions` en el orden en que se unen y `separators[k]` el texto ENTRE `positions[k]` y
 * `positions[k + 1]` (al reordenar, los separadores se quedan en su hueco).
 */
export interface JoinDef {
  field: string
  positions: number[]
  separators: string[]
}

export interface ConsigneeTemplateValues {
  name: string
  delimiter: string
  hasHeader: boolean
  /** Cliente por defecto de la importación ('' = sin cliente: compartidos). */
  defaultClientPublicId: string
  /** Una fila por columna del archivo, en orden (posición = índice + 1). Lote F-B3: el mapeo de la variante 1. */
  columns: MappingColumn[]
  defaults: { field: string; value: string }[]
  /** Lote F-B3: nombre y condiciones de la variante 1 (solo cuentan si hay más variantes). */
  variantName: string
  conditions: ConditionValues[]
  /** Lote F-B3: variantes 2…n (vacío = plantilla de una sola variante, lo de siempre). */
  moreVariants: VariantValues[]
}

/** Condición de una variante en el editor: columna (texto del select, '' = sin escoger), operador y valor. */
export interface ConditionValues {
  position: string
  operator: string
  value: string
}

/** Una variante de mapeo en el editor (la 1 vive en los campos de arriba de ConsigneeTemplateValues). */
export interface VariantValues {
  name: string
  conditions: ConditionValues[]
  columns: MappingColumn[]
}

export interface ConsigneeTemplateMessages {
  nameRequired: string
  nameMax: string
  columnsRequired: string
  nameLine1: string
  city: string
  clientBoth: string
  repeated: (field: string) => string
  tooManyParts: (field: string) => string
  noDefault: (field: string) => string
  defaultValueRequired: (field: string) => string
  /** Lote F-B3 (variantes): columna de la condición sin escoger. */
  conditionColumn?: string
  /** Lote F-B3: «igual a» / «contiene» sin valor. */
  conditionValue?: string
  /** Lote F-B3: variante sin condición que no es la última (n = 1…). */
  unreachable?: (n: number) => string
}

/** Columnas (1..n) de cada campo, en orden de posición y en el orden en que aparece el campo. */
export function fieldPositions(columns: readonly MappingColumn[]): Map<string, number[]> {
  const map = new Map<string, number[]>()
  columns.forEach((c, i) => {
    for (const f of c.fields) {
      if (!f.field || f.field === IGNORE_FIELD) continue
      const list = map.get(f.field) ?? []
      if (!list.includes(i + 1)) list.push(i + 1)
      map.set(f.field, list)
    }
  })
  return map
}

/**
 * Uniones al día con las columnas: un campo con dos o más columnas es una unión. Conserva el orden y los separadores de lo que
 * sigue; una columna nueva se agrega al final con un espacio; una unión que queda con una sola columna desaparece.
 */
export function syncJoins(columns: readonly MappingColumn[], joins: readonly JoinDef[]): JoinDef[] {
  const out: JoinDef[] = []
  for (const [field, list] of fieldPositions(columns)) {
    if (list.length < 2) continue
    const prev = joins.find((j) => j.field === field)
    const kept = (prev?.positions ?? []).filter((p) => list.includes(p))
    const positions = [...kept, ...list.filter((p) => !kept.includes(p))]
    const separators = positions.slice(1).map((_, k) => prev?.separators[k] ?? DEFAULT_SEPARATOR)
    out.push({ field, positions, separators })
  }
  return out
}

/** Mueve la columna `index` de la unión una posición arriba (-1) o abajo (+1); los separadores se quedan en su hueco. */
export function moveJoinPart(join: JoinDef, index: number, delta: -1 | 1): JoinDef {
  const target = index + delta
  if (target < 0 || target >= join.positions.length) return join
  const positions = [...join.positions]
  ;[positions[index], positions[target]] = [positions[target], positions[index]]
  return { ...join, positions }
}

/**
 * Resultado de unir (mismo criterio que el servidor): las celdas vacías no dejan separador. `cellOf(posición)` da el valor de una
 * fila de muestra (undefined = no hay archivo: se muestra «[Columna N]»).
 */
export function joinPreview(join: JoinDef, cellOf: (position: number) => string | undefined, columnLabel: (position: number) => string): string {
  let out = ''
  join.positions.forEach((p, k) => {
    const raw = cellOf(p)
    const value = raw === undefined ? `[${columnLabel(p)}]` : raw.trim()
    if (!value) return
    if (out) out += join.separators[k - 1] ?? DEFAULT_SEPARATOR
    out += value
  })
  return out
}

/** Filas del editor (una por columna del archivo) y uniones a partir de las columnas guardadas de una variante. */
function mappingOf(cols: readonly ImportTemplateColumn[], fileColumns: number): { columns: MappingColumn[]; joins: JoinDef[] } {
  const last = cols.reduce((max, c) => Math.max(max, c.position ?? 0), 0)
  const width = Math.max(last, fileColumns, 1)
  const columns = Array.from({ length: width }, (_, i) => {
    const fields = cols.filter((c) => c.position === i + 1 && c.field && c.field !== IGNORE_FIELD).map((c) => ({ field: c.field ?? '' }))
    return { fields: fields.length > 0 ? fields : [{ field: '' }] }
  })
  const joins: JoinDef[] = []
  for (const [field] of fieldPositions(columns)) {
    const parts = cols.filter((c) => c.field === field).sort((a, b) => (a.part ?? 1e9) - (b.part ?? 1e9) || (a.position ?? 0) - (b.position ?? 0))
    if (parts.length < 2) continue
    joins.push({ field, positions: parts.map((c) => c.position ?? 0), separators: parts.slice(1).map((c) => c.separator ?? DEFAULT_SEPARATOR) })
  }
  return { columns, joins }
}

function conditionValuesOf(conditions: readonly ImportVariantCondition[] | null | undefined): ConditionValues[] {
  return (conditions ?? []).map((c) => ({ position: c.position ? String(c.position) : '', operator: c.operator ?? 'EQUALS', value: c.value ?? '' }))
}

/**
 * Valores del editor y uniones a partir de la plantilla; `fileColumns` = columnas del archivo revisado (se muestran todas).
 * Lote F-B3: con variantes, la 1 va en los campos de arriba (`columns`, `variantName`, `conditions`; uniones en `joins`) y las demás en
 * `moreVariants` (uniones en `moreJoins`, una lista por variante).
 */
export function consigneeTemplateValuesOf(t: ImportTemplate | null, fileColumns = 0): { values: ConsigneeTemplateValues; joins: JoinDef[]; moreJoins: JoinDef[][] } {
  if (!t) {
    const width = Math.max(5, fileColumns)
    return {
      values: {
        name: '',
        delimiter: ',',
        hasHeader: true,
        defaultClientPublicId: '',
        columns: Array.from({ length: width }, () => ({ fields: [{ field: '' }] })),
        defaults: [],
        variantName: '',
        conditions: [],
        moreVariants: [],
      },
      joins: [],
      moreJoins: [],
    }
  }
  const variants = t.variants ?? []
  const first = mappingOf(variants.length > 0 ? (variants[0].columns ?? []) : (t.columns ?? []), fileColumns)
  const rest = variants.slice(1).map((v) => ({ v, m: mappingOf(v.columns ?? [], fileColumns) }))
  return {
    values: {
      name: t.name ?? '',
      delimiter: t.delimiter || ',',
      hasHeader: t.hasHeader ?? true,
      defaultClientPublicId: t.defaultClientPublicId ?? '',
      columns: first.columns,
      defaults: Object.entries(t.defaults ?? {}).map(([field, value]) => ({ field, value })),
      variantName: variants[0]?.name ?? '',
      conditions: variants.length > 1 ? conditionValuesOf(variants[0].conditions) : [],
      moreVariants: rest.map(({ v, m }) => ({ name: v.name ?? '', conditions: conditionValuesOf(v.conditions), columns: m.columns })),
    },
    joins: first.joins,
    moreJoins: rest.map(({ m }) => m.joins),
  }
}

/** Errores de la definición (espejo de ConsigneeImportMapping.ValidateColumns, Lote F-B2): ruta → mensaje. */
export function consigneeTemplateIssues(
  v: Pick<ConsigneeTemplateValues, 'columns' | 'defaults'>,
  fields: readonly ImportField[],
  m: Omit<ConsigneeTemplateMessages, 'nameRequired' | 'nameMax'>,
): TemplateIssue[] {
  const issues: TemplateIssue[] = []
  const count = new Map<string, number>()
  v.columns.forEach((c, i) => {
    const inColumn = new Set<string>()
    c.fields.forEach((f, j) => {
      if (!f.field || f.field === IGNORE_FIELD) return
      const path = `columns.${i}.fields.${j}.field`
      if (inColumn.has(f.field)) {
        issues.push({ path, message: m.repeated(f.field) })
        return
      }
      inColumn.add(f.field)
      const n = (count.get(f.field) ?? 0) + 1
      count.set(f.field, n)
      if (n > 1 && SINGLE_COLUMN_FIELDS.includes(f.field)) issues.push({ path, message: m.repeated(f.field) })
      else if (n > MAX_JOIN_PARTS) issues.push({ path, message: m.tooManyParts(f.field) })
    })
  })
  const allowDefault = new Set(fields.filter((f) => f.defaultAllowed).map((f) => f.field ?? ''))
  const defaultFields = new Set<string>()
  v.defaults.forEach((d, i) => {
    if (!d.field) return
    if (fields.length > 0 && !allowDefault.has(d.field)) issues.push({ path: `defaults.${i}.field`, message: m.noDefault(d.field) })
    else if (!d.value.trim()) issues.push({ path: `defaults.${i}.value`, message: m.defaultValueRequired(d.field) })
    else defaultFields.add(d.field)
  })
  if (count.size === 0) issues.push({ path: 'columns', message: m.columnsRequired })
  else if (!REQUIRED_COLUMN_FIELDS.every((f) => count.has(f))) issues.push({ path: 'columns', message: m.nameLine1 })
  else if (!count.has(CITY_FIELD) && !defaultFields.has(CITY_FIELD) && !count.has(POSTAL_FIELD) && !defaultFields.has(POSTAL_FIELD)) issues.push({ path: 'columns', message: m.city })
  else if (count.has(CLIENT_CODE_FIELD) && count.has(CLIENT_NAME_FIELD)) issues.push({ path: 'columns', message: m.clientBoth })
  return issues
}

export function consigneeTemplateSchema(m: ConsigneeTemplateMessages, fields: readonly ImportField[]) {
  return z
    .object({
      name: z.string().trim().min(1, m.nameRequired).max(TEMPLATE_NAME_MAX, m.nameMax),
      delimiter: z.string().min(1),
      hasHeader: z.boolean(),
      defaultClientPublicId: z.string(),
      columns: z.array(z.object({ fields: z.array(z.object({ field: z.string() })) })),
      defaults: z.array(z.object({ field: z.string(), value: z.string() })),
      variantName: z.string(),
      conditions: z.array(conditionSchema),
      moreVariants: z.array(z.object({ name: z.string(), conditions: z.array(conditionSchema), columns: z.array(z.object({ fields: z.array(z.object({ field: z.string() })) })) })),
    })
    .superRefine((v, ctx) => {
      for (const issue of [...consigneeTemplateIssues(v, fields, m), ...variantIssues(v, fields, m)])
        ctx.addIssue({ code: 'custom', path: issue.path.split('.').map((p) => (/^\d+$/.test(p) ? Number(p) : p)), message: issue.message })
    })
}

const conditionSchema = z.object({ position: z.string(), operator: z.string(), value: z.string() })

// ---------------------------------------------------------------- variantes de mapeo (Lote F-B3)

/** Operadores de la condición (espejo de ImportConditionOperators), en el orden del selector. */
export const CONDITION_OPERATORS = ['EQUALS', 'CONTAINS', 'NOT_EMPTY', 'EMPTY'] as const
export const MAX_VARIANTS = 10
export const MAX_CONDITIONS = 5
export const VARIANT_NAME_MAX = 60
export const CONDITION_VALUE_MAX = 100

/** ¿El operador compara con un valor? («igual a», «contiene»). */
export function conditionNeedsValue(operator: string): boolean {
  return operator === 'EQUALS' || operator === 'CONTAINS'
}

/** Las variantes del editor en orden (la 1 sale de los campos de arriba). */
export function variantsOf(v: Pick<ConsigneeTemplateValues, 'variantName' | 'conditions' | 'columns' | 'moreVariants'>): VariantValues[] {
  return [{ name: v.variantName, conditions: v.conditions, columns: v.columns }, ...v.moreVariants]
}

/** Valores del editor con otra lista de variantes (la primera vuelve a los campos de arriba). */
export function withVariants<V extends ConsigneeTemplateValues>(v: V, list: readonly VariantValues[]): V {
  const [first, ...rest] = list
  return { ...v, variantName: first?.name ?? '', conditions: first?.conditions ?? [], columns: first?.columns ?? v.columns, moreVariants: [...rest] }
}

/** Ruta de los campos de la variante k (0 = la 1, en los campos de arriba). */
export function variantPath(k: number): string {
  return k === 0 ? '' : `moreVariants.${k - 1}.`
}

/**
 * Lote F-B3 — errores de las variantes (espejo de ConsigneeImportVariants.Validate): solo si hay dos o más. Condición sin columna,
 * «igual a»/«contiene» sin valor, variante sin condición que no es la última, y el mapeo de cada variante (las mismas reglas que la 1).
 */
export function variantIssues(
  v: Pick<ConsigneeTemplateValues, 'variantName' | 'conditions' | 'columns' | 'moreVariants' | 'defaults'>,
  fields: readonly ImportField[],
  m: Omit<ConsigneeTemplateMessages, 'nameRequired' | 'nameMax'>,
): TemplateIssue[] {
  if (v.moreVariants.length === 0) return []
  const issues: TemplateIssue[] = []
  const list = variantsOf(v)
  list.forEach((variant, k) => {
    const base = variantPath(k)
    variant.conditions.forEach((c, j) => {
      if (!c.position) issues.push({ path: `${base}conditions.${j}.position`, message: m.conditionColumn ?? 'La posición debe ser 1 o mayor.' })
      if (conditionNeedsValue(c.operator) && !c.value.trim())
        issues.push({ path: `${base}conditions.${j}.value`, message: m.conditionValue ?? 'La condición «igual a» o «contiene» necesita un valor.' })
    })
    if (variant.conditions.length === 0 && k < list.length - 1)
      issues.push({
        path: `${base}conditions`,
        message: m.unreachable?.(k + 1) ?? `La variante ${k + 1} no tiene condición y deja sin uso a las que le siguen; póngala al final o agréguele una condición.`,
      })
    if (k > 0)
      for (const issue of consigneeTemplateIssues({ columns: variant.columns, defaults: v.defaults }, fields, m))
        if (!issue.path.startsWith('defaults')) issues.push({ path: `${base}${issue.path}`, message: issue.message })
  })
  return issues
}

/** Variantes con algún error (para marcar su pestaña): índices 0…n-1. */
export function variantsWithIssues(paths: readonly string[], count: number): number[] {
  const out: number[] = []
  for (let k = 0; k < count; k++) {
    const hit = k === 0 ? paths.some((p) => p.startsWith('columns') || p.startsWith('conditions')) : paths.some((p) => p.startsWith(`moreVariants.${k - 1}.`))
    if (hit) out.push(k)
  }
  return out
}

/** Variante del request: nombre (vacío = null), condiciones (el valor solo en «igual a»/«contiene») y sus columnas. */
function variantRequest(variant: VariantValues, joins: readonly JoinDef[]): ImportTemplateVariant {
  return {
    name: variant.name.trim() || null,
    conditions: variant.conditions.map((c) => ({ position: Number(c.position), operator: c.operator, value: conditionNeedsValue(c.operator) ? c.value.trim() : null })),
    columns: consigneeTemplateColumns({ columns: variant.columns }, joins),
  }
}

/** Variantes del request (solo con dos o más; si no, la plantilla viaja con `columns`, como siempre). */
export function consigneeTemplateVariants(v: ConsigneeTemplateValues, joins: readonly JoinDef[], moreJoins: readonly JoinDef[][]): ImportTemplateVariant[] | null {
  if (v.moreVariants.length === 0) return null
  return variantsOf(v).map((variant, k) => variantRequest(variant, k === 0 ? joins : (moreJoins[k - 1] ?? [])))
}

/**
 * Columnas del request: TODAS las filas del editor (posición = orden); sin campo → «ignore»; una columna puede ir a varios campos;
 * un campo de una unión lleva su parte (1..n) y, desde la segunda, el separador que va antes de ella.
 */
export function consigneeTemplateColumns(v: Pick<ConsigneeTemplateValues, 'columns'>, joins: readonly JoinDef[]): ImportTemplateColumn[] {
  const out: ImportTemplateColumn[] = []
  v.columns.forEach((c, i) => {
    const position = i + 1
    const fields = [...new Set(c.fields.map((f) => f.field).filter((f) => f && f !== IGNORE_FIELD))]
    if (fields.length === 0) {
      out.push({ position, field: IGNORE_FIELD })
      return
    }
    for (const field of fields) {
      const join = joins.find((j) => j.field === field && j.positions.length > 1)
      const k = join ? join.positions.indexOf(position) : -1
      if (!join || k < 0) out.push({ position, field })
      else if (k === 0) out.push({ position, field, part: 1 })
      else out.push({ position, field, part: k + 1, separator: join.separators[k - 1] ?? DEFAULT_SEPARATOR })
    }
  })
  return out
}

export function buildConsigneeTemplateCreate(v: ConsigneeTemplateValues, joins: readonly JoinDef[], moreJoins: readonly JoinDef[][] = []): ImportTemplateCreateRequest {
  const defaults = templateDefaults(v)
  const variants = consigneeTemplateVariants(v, joins, moreJoins)
  return {
    name: v.name.trim(),
    delimiter: v.delimiter,
    hasHeader: v.hasHeader,
    // Lote F-B3: con variantes, las columnas van dentro de cada una
    ...(variants ? { variants } : { columns: consigneeTemplateColumns(v, joins) }),
    defaults: Object.keys(defaults).length > 0 ? defaults : null,
    ...(v.defaultClientPublicId ? { defaultClientPublicId: v.defaultClientPublicId } : {}),
  }
}

/**
 * PATCH: columnas y defaults reemplazan la lista completa; sin defaults → `clearDefaults`; sin cliente por defecto → `clearDefaultClient`.
 * Lote F-B3: con variantes viajan `variants` (sin `columns`); una plantilla que tenía variantes y queda con una sola manda `variants: []`.
 */
export function buildConsigneeTemplatePatch(v: ConsigneeTemplateValues, joins: readonly JoinDef[], moreJoins: readonly JoinDef[][] = [], hadVariants = false): ImportTemplatePatchRequest {
  const defaults = templateDefaults(v)
  const hasDefaults = Object.keys(defaults).length > 0
  const variants = consigneeTemplateVariants(v, joins, moreJoins)
  return {
    name: v.name.trim(),
    delimiter: v.delimiter,
    hasHeader: v.hasHeader,
    ...(variants ? { variants } : { columns: consigneeTemplateColumns(v, joins), ...(hadVariants ? { variants: [] } : {}) }),
    defaults: hasDefaults ? defaults : null,
    clearDefaults: hasDefaults ? null : true,
    ...(v.defaultClientPublicId ? { defaultClientPublicId: v.defaultClientPublicId } : { clearDefaultClient: true }),
  }
}

/** ¿Es un libro de Excel? (por la extensión o el tipo; el servidor lo vuelve a decidir por el contenido). */
export function isExcelFile(file: Pick<File, 'name' | 'type'>): boolean {
  return /\.xlsx$/i.test(file.name) || file.type === 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
}

// ---------------------------------------------------------------- importación: vista previa y resultado

/** Problemas de una fila: errores por campo («Campo: mensaje»), el error de la confirmación y los avisos. */
export function rowProblems<R extends Pick<ImportRow, 'errors' | 'warnings' | 'error'>>(row: R, label: (field: string) => string): { errors: string[]; warnings: string[] } {
  const errors = Object.entries(row.errors ?? {}).map(([field, message]) => `${label(field)}: ${message}`)
  if (row.error) errors.push(row.error)
  return { errors, warnings: [...(row.warnings ?? [])] }
}

/** ¿La fila tiene algo que mostrar en «solo con problemas»? */
export function rowHasProblems<R extends Pick<ImportRow, 'isValid' | 'warnings' | 'error'>>(row: R): boolean {
  return row.isValid === false || (row.warnings?.length ?? 0) > 0 || Boolean(row.error)
}

/** Peso del archivo por encima del límite del servidor (aviso inmediato; el servidor lo vuelve a validar). */
export function exceedsImportSize(size: number): boolean {
  return size > MAX_IMPORT_BYTES
}

// ---------------------------------------------------------------- selector de consignatario (órdenes, Lote F-C1)

/** Etiqueta «Número · Nombre» (o lo que haya). */
export function consigneeOptionLabel(c: Pick<Consignee, 'code' | 'name'>): string {
  return [c.code, c.name].filter(Boolean).join(' · ')
}

/** «Calle 1 · San Juan», sin partes vacías. */
export function consigneeAddress(c: Pick<Consignee, 'line1' | 'city'>): string {
  return [c.line1, c.city].filter(Boolean).join(' · ')
}
