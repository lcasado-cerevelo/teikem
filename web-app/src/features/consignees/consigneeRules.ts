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
  const text = (key: 'code' | 'name' | 'line1' | 'line2' | 'city' | 'state' | 'postalCode' | 'accessNotes' | 'deliveryNotes') => {
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
  return matchesQ(q, c.code, c.name, c.line1, c.line2, c.city, c.postalCode, ownerLabel(c, labels.shared), c.isActive ? labels.active : labels.inactive)
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
  else if (!seen.has(CITY_FIELD) && !defaultFields.has(CITY_FIELD)) issues.push({ path: 'columns', message: m.city })
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
  issues?: (v: Pick<TemplateValues, 'columns' | 'defaults'>) => TemplateIssue[],
) {
  const rules = issues ?? ((v: Pick<TemplateValues, 'columns' | 'defaults'>) => templateIssues(v, fields, m as TemplateMessages))
  return z
    .object({
      name: z.string().trim().min(1, m.nameRequired).max(TEMPLATE_NAME_MAX, m.nameMax),
      delimiter: z.string().min(1),
      hasHeader: z.boolean(),
      columns: z.array(z.object({ field: z.string() })),
      defaults: z.array(z.object({ field: z.string(), value: z.string() })),
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
  }
}

/** Columnas del request: la posición es el orden en el editor (1…n); las ignoradas no se mandan. */
export function templateColumns(v: Pick<TemplateValues, 'columns'>) {
  return v.columns.flatMap((c, i) => (c.field ? [{ position: i + 1, field: c.field }] : []))
}

export function templateDefaults(v: Pick<TemplateValues, 'defaults'>): Record<string, string> {
  return Object.fromEntries(v.defaults.filter((d) => d.field && d.value.trim()).map((d) => [d.field, d.value.trim()]))
}

export function buildTemplateCreate(v: TemplateValues): ImportTemplateCreateRequest {
  const defaults = templateDefaults(v)
  return {
    name: v.name.trim(),
    delimiter: v.delimiter,
    hasHeader: v.hasHeader,
    columns: templateColumns(v),
    defaults: Object.keys(defaults).length > 0 ? defaults : null,
  }
}

/** PATCH: columnas y defaults reemplazan la lista completa; sin defaults se manda `clearDefaults`. */
export function buildTemplatePatch(v: TemplateValues): ImportTemplatePatchRequest {
  const defaults = templateDefaults(v)
  const hasDefaults = Object.keys(defaults).length > 0
  return {
    name: v.name.trim(),
    delimiter: v.delimiter,
    hasHeader: v.hasHeader,
    columns: templateColumns(v),
    defaults: hasDefaults ? defaults : null,
    clearDefaults: hasDefaults ? null : true,
  }
}

/** Resumen «1. Nombre · 2. Número…» de la plantilla elegida. */
export function templateSummary(t: ImportTemplate | undefined, label: (field: string) => string): string {
  return [...(t?.columns ?? [])]
    .sort((a, b) => (a.position ?? 0) - (b.position ?? 0))
    .map((c) => `${c.position}. ${label(c.field ?? '')}`)
    .join(' · ')
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
