// Reglas puras de la ADMINISTRACIÓN de definiciones de campos personalizados (lote F-F1): clave, validación amigable
// (validationJson), valor por defecto por tipo, opciones, listas del catálogo y el cuerpo de `CustomFieldDefinitionUpsert`.
// Espejo de `CustomFieldService` (C#): la clave, los tipos y los mensajes son los del servidor.
import { z } from 'zod'
import type { components } from '../api/schema'
import { utcFromZonedInput, zonedInputFromUtc } from '../api/tenantZone'
import { toBool, toNumber } from '../dsl'
import { CustomFieldDataTypes, type CustomFieldDefinitionDto } from './values'

export type CustomFieldDefinitionUpsert = components['schemas']['CustomFieldDefinitionUpsert']
export type CatalogListCreateRequest = components['schemas']['CatalogListCreateRequest']

/** `CustomFieldService.KeyRegex`: minúscula inicial, luego minúsculas, dígitos o `_`; 2 a 60 caracteres. */
export const FIELD_KEY_PATTERN = /^[a-z][a-z0-9_]{1,59}$/
/** `SortOrder` por omisión del servidor. */
export const DEFAULT_SORT_ORDER = 100
/** Máximo de caracteres del código de un valor de lista (`LookupService.NormalizeCode`). */
export const LIST_CODE_MAX = 40
/** Alcance de un dominio de catálogo que es lista de valores (`CatalogScope.Lookup`); 2 = estatus (no sirve para campos). */
export const LOOKUP_SCOPE = 1

/**
 * Entidades que admiten valores Y tienen dónde llenarse hoy en la web (ficha con `CustomFieldsForm`). El servidor también tiene
 * resolver para WAREHOUSE y CONTRACT (sin pantalla que los llene todavía) e INVOICE no tiene (ver `docs/frontend/loteFF1-decisiones.md`).
 */
export const CUSTOM_FIELD_ENTITY_TYPES = ['CLIENT', 'TRANSPORT_ORDER', 'DRIVER', 'VEHICLE', 'PRODUCT', 'LOCATION'] as const
export type CustomFieldEntityType = (typeof CUSTOM_FIELD_ENTITY_TYPES)[number]

/** Los 8 tipos del API, en el orden del selector. */
export const CUSTOM_FIELD_TYPES = [
  CustomFieldDataTypes.Text,
  CustomFieldDataTypes.Number,
  CustomFieldDataTypes.Date,
  CustomFieldDataTypes.DateTime,
  CustomFieldDataTypes.Bool,
  CustomFieldDataTypes.Select,
  CustomFieldDataTypes.MultiSelect,
  CustomFieldDataTypes.LookupRef,
] as const

/** Selección simple, múltiple o referencia a catálogo: sus opciones salen de una lista del catálogo. */
export function isListType(type: string): boolean {
  return type === CustomFieldDataTypes.Select || type === CustomFieldDataTypes.MultiSelect || type === CustomFieldDataTypes.LookupRef
}

/** El servidor recorta y pasa a minúsculas la clave antes de validarla. */
export function normalizeFieldKey(raw: string): string {
  return raw.trim().toLowerCase()
}

export function isValidFieldKey(raw: string): boolean {
  return FIELD_KEY_PATTERN.test(normalizeFieldKey(raw))
}

// ---------------------------------------------------------------------------------------------------------------- validación

export interface ValidationFields {
  regex: string
  minLength: number | null
  maxLength: number | null
  min: number | null
  max: number | null
}

export const EMPTY_VALIDATION: ValidationFields = { regex: '', minLength: null, maxLength: null, min: null, max: null }

export type ValidationFieldName = keyof ValidationFields

/**
 * Qué controles de validación aplican al tipo (lo que `RuleEvaluator.Validate` evalúa): texto → formato y longitudes; número →
 * mín. y máx.; fecha y fecha-hora → mín. y máx. en DÍAS respecto a hoy (negativo = pasado). El resto no valida nada.
 */
export function validationFieldsFor(type: string): ValidationFieldName[] {
  switch (type) {
    case CustomFieldDataTypes.Text:
      return ['regex', 'minLength', 'maxLength']
    case CustomFieldDataTypes.Number:
    case CustomFieldDataTypes.Date:
    case CustomFieldDataTypes.DateTime:
      return ['min', 'max']
    default:
      return []
  }
}

const KNOWN_VALIDATION_KEYS = new Set(['regex', 'pattern', 'min', 'max', 'minLength', 'maxLength'])

function finiteOrNull(v: unknown): number | null {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null
  if (typeof v === 'string' && v.trim() !== '') {
    const n = Number(v)
    return Number.isFinite(n) ? n : null
  }
  return null
}

/** `validationJson` del API → controles amigables. Las claves que la pantalla no edita se conservan en `extra`. */
export function readValidation(json: string | null | undefined): { fields: ValidationFields; extra: Record<string, unknown> } {
  const empty = { fields: { ...EMPTY_VALIDATION }, extra: {} }
  if (!json || json.trim() === '') return empty
  let parsed: unknown
  try {
    parsed = JSON.parse(json)
  } catch {
    return empty
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return empty
  const obj = parsed as Record<string, unknown>
  const regex = typeof obj.regex === 'string' ? obj.regex : typeof obj.pattern === 'string' ? obj.pattern : ''
  const extra: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(obj)) if (!KNOWN_VALIDATION_KEYS.has(k)) extra[k] = v
  return {
    fields: { regex, minLength: finiteOrNull(obj.minLength), maxLength: finiteOrNull(obj.maxLength), min: finiteOrNull(obj.min), max: finiteOrNull(obj.max) },
    extra,
  }
}

/** Controles amigables → `validationJson` (solo lo que aplica al tipo; vacío = null). Nunca se pide escribir JSON. */
export function buildValidationJson(type: string, fields: ValidationFields, extra: Record<string, unknown> = {}): string | null {
  const out: Record<string, unknown> = {}
  for (const name of validationFieldsFor(type)) {
    if (name === 'regex') {
      if (fields.regex.trim() !== '') out.regex = fields.regex
    } else if (fields[name] !== null && Number.isFinite(fields[name])) {
      out[name] = fields[name]
    }
  }
  const merged = { ...extra, ...out }
  return Object.keys(merged).length === 0 ? null : JSON.stringify(merged)
}

// ---------------------------------------------------------------------------------------------------------------- valor por defecto

export interface DefaultFields {
  text: string
  bool: boolean
  multi: string[]
}

export const EMPTY_DEFAULT: DefaultFields = { text: '', bool: false, multi: [] }

/** `defaultValue` (texto) del API → controles según el tipo. */
export function readDefault(type: string, raw: string | null | undefined): DefaultFields {
  const value = raw ?? ''
  switch (type) {
    case CustomFieldDataTypes.Bool:
      return { ...EMPTY_DEFAULT, bool: value !== '' && toBool(value) === true }
    case CustomFieldDataTypes.MultiSelect:
      return { ...EMPTY_DEFAULT, multi: value.split(',').map((x) => x.trim()).filter((x) => x.length > 0) }
    case CustomFieldDataTypes.DateTime:
      return { ...EMPTY_DEFAULT, text: zonedInputFromUtc(value) }
    case CustomFieldDataTypes.Date:
      return { ...EMPTY_DEFAULT, text: /^\d{4}-\d{2}-\d{2}/.test(value) ? value.slice(0, 10) : '' }
    default:
      return { ...EMPTY_DEFAULT, text: value }
  }
}

/** Controles → `defaultValue` del API (null = sin valor por defecto). BOOL sin marcar = sin valor por defecto. */
export function buildDefault(type: string, d: DefaultFields): string | null {
  switch (type) {
    case CustomFieldDataTypes.Bool:
      return d.bool ? 'true' : null
    case CustomFieldDataTypes.MultiSelect:
      return d.multi.length > 0 ? d.multi.join(',') : null
    case CustomFieldDataTypes.DateTime:
      return d.text.trim() === '' ? null : utcFromZonedInput(d.text)
    case CustomFieldDataTypes.Number:
    case CustomFieldDataTypes.Date:
      return d.text.trim() === '' ? null : d.text.trim()
    default:
      return d.text.trim() === '' ? null : d.text.trim()
  }
}

/** ¿El valor por defecto escrito es legible para el tipo? (NUMBER numérico, DATE/DATETIME con fecha válida). */
export function defaultIssue(type: string, text: string): 'number' | 'date' | null {
  const v = text.trim()
  if (v === '') return null
  if (type === CustomFieldDataTypes.Number) return toNumber(v) === null ? 'number' : null
  if (type === CustomFieldDataTypes.Date) return /^\d{4}-\d{2}-\d{2}$/.test(v) && !Number.isNaN(Date.parse(v)) ? null : 'date'
  if (type === CustomFieldDataTypes.DateTime) return utcFromZonedInput(v) === null ? 'date' : null
  return null
}

// ---------------------------------------------------------------------------------------------------------------- opciones y listas

export interface ListChoice {
  code: string
  label: string
}

/** Opciones propias (sin lista del catálogo) que traen las definiciones hechas por API: se conservan al editar. */
export function legacyOptions(def: Pick<CustomFieldDefinitionDto, 'options'> | null | undefined): NonNullable<CustomFieldDefinitionUpsert['options']> {
  return [...(def?.options ?? [])]
    .filter((o) => o.isActive !== false && !!o.value)
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
    .map((o) => ({ value: o.value ?? '', labels: o.labels && Object.keys(o.labels).length > 0 ? o.labels : { es: o.label ?? o.value ?? '' }, sortOrder: o.sortOrder }))
}

/** ¿La definición usa opciones propias en vez de una lista del catálogo? */
export function hasLegacyOptions(def: Pick<CustomFieldDefinitionDto, 'options' | 'refEntity'> | null | undefined): boolean {
  return !!def && !def.refEntity && legacyOptions(def).length > 0
}

/** Código de un valor de lista como lo normaliza el servidor (`LookupService.NormalizeCode`), sin acentos: "Más alto" → MAS_ALTO. */
export function listCodeOf(label: string): string {
  const plain = label.normalize('NFD').replace(/[̀-ͯ]/g, '')
  return plain.trim().toUpperCase().replace(/[^A-Z0-9_]+/g, '_').replace(/^_+|_+$/g, '')
}

export interface ParsedListOptions {
  options: ListChoice[]
  /** Textos que no dan un código válido (vacío tras limpiar o de más de 40 caracteres). */
  invalid: string[]
}

/** «Alto, Medio, Bajo» → opciones únicas (por código) con su etiqueta; lo repetido se ignora. */
export function parseListOptions(text: string): ParsedListOptions {
  const options: ListChoice[] = []
  const invalid: string[] = []
  const seen = new Set<string>()
  for (const part of text.split(/[,\n;]/)) {
    const label = part.trim()
    if (label === '') continue
    const code = listCodeOf(label)
    if (code === '' || code.length > LIST_CODE_MAX) {
      invalid.push(label)
      continue
    }
    if (seen.has(code)) continue
    seen.add(code)
    options.push({ code, label })
  }
  return { options, invalid }
}

/** Cuerpo de `POST /api/v1/catalogs/lists` para «Nueva lista…». */
export function buildListRequest(name: string, options: ListChoice[]): CatalogListCreateRequest {
  const label = name.trim()
  return {
    name: label,
    nameEn: label,
    description: null,
    values: options.map((o, i) => ({ code: o.code, labels: { es: o.label, en: o.label }, descriptions: null, extraJson: null, sortOrder: (i + 1) * 10 })),
  }
}

// ---------------------------------------------------------------------------------------------------------------- formulario

export interface DefinitionFormValues {
  fieldKey: string
  dataType: string
  labelEs: string
  labelEn: string
  description: string
  isRequired: boolean
  isUnique: boolean
  showInList: boolean
  sortOrder: number | null
  refEntity: string
  defaultText: string
  defaultBool: boolean
  defaultMulti: string[]
  regex: string
  minLength: number | null
  maxLength: number | null
  min: number | null
  max: number | null
}

export function emptyDefinitionValues(): DefinitionFormValues {
  return {
    fieldKey: '',
    dataType: CustomFieldDataTypes.Text,
    labelEs: '',
    labelEn: '',
    description: '',
    isRequired: false,
    isUnique: false,
    showInList: false,
    sortOrder: DEFAULT_SORT_ORDER,
    refEntity: '',
    defaultText: '',
    defaultBool: false,
    defaultMulti: [],
    ...EMPTY_VALIDATION,
  }
}

/** Definición del API → valores del formulario de edición. */
export function definitionToValues(def: CustomFieldDefinitionDto): DefinitionFormValues {
  const type = (def.dataType ?? CustomFieldDataTypes.Text).toUpperCase()
  const d = readDefault(type, def.defaultValue)
  const v = readValidation(def.validationJson).fields
  const labels = def.labels ?? {}
  return {
    fieldKey: def.fieldKey ?? '',
    dataType: type,
    labelEs: labels.es ?? def.label ?? '',
    labelEn: labels.en ?? '',
    description: def.description ?? '',
    isRequired: def.isRequired === true,
    isUnique: def.isUnique === true,
    showInList: def.showInList === true,
    sortOrder: def.sortOrder ?? DEFAULT_SORT_ORDER,
    refEntity: def.refEntity ?? '',
    defaultText: d.text,
    defaultBool: d.bool,
    defaultMulti: d.multi,
    ...v,
  }
}

/**
 * Esquema del formulario (espejo del servidor). `creating`: la clave se valida solo en el alta. `ownOptions`: la definición trae
 * opciones propias, así que una selección puede quedarse sin lista del catálogo (LOOKUP_REF siempre la exige).
 */
export function definitionSchema(t: (key: string, params?: Record<string, string | number>) => string, opts: { creating: boolean; ownOptions: boolean }) {
  const intOrNull = z.number().nullable()
  return z
    .object({
      fieldKey: z.string(),
      dataType: z.string(),
      labelEs: z.string().trim().min(1, t('customFieldsAdmin.errors.labelRequired')),
      labelEn: z.string().trim(),
      description: z.string().trim(),
      isRequired: z.boolean(),
      isUnique: z.boolean(),
      showInList: z.boolean(),
      sortOrder: z.number().int(t('customFieldsAdmin.errors.sortOrder')).min(0, t('customFieldsAdmin.errors.sortOrder')).max(100000, t('customFieldsAdmin.errors.sortOrder')).nullable(),
      refEntity: z.string(),
      defaultText: z.string(),
      defaultBool: z.boolean(),
      defaultMulti: z.array(z.string()),
      regex: z.string(),
      minLength: intOrNull,
      maxLength: intOrNull,
      min: intOrNull,
      max: intOrNull,
    })
    .superRefine((v, ctx) => {
      const add = (path: keyof DefinitionFormValues, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      if (opts.creating && !isValidFieldKey(v.fieldKey)) add('fieldKey', t('customFieldsAdmin.errors.keyInvalid'))
      if (isListType(v.dataType) && v.refEntity.trim() === '' && (v.dataType === CustomFieldDataTypes.LookupRef || !opts.ownOptions)) {
        add('refEntity', t('customFieldsAdmin.errors.listRequired'))
      }
      const issue = defaultIssue(v.dataType, v.defaultText)
      if (issue) add('defaultText', t(issue === 'number' ? 'customFieldsAdmin.errors.defaultNumber' : 'customFieldsAdmin.errors.defaultDate'))
      const fields = validationFieldsFor(v.dataType)
      if (fields.includes('regex') && v.regex.trim() !== '') {
        try {
          new RegExp(v.regex)
        } catch {
          add('regex', t('customFieldsAdmin.errors.regexInvalid'))
        }
      }
      for (const name of ['minLength', 'maxLength'] as const) {
        const n = v[name]
        if (fields.includes(name) && n !== null && (!Number.isInteger(n) || n < 0)) add(name, t('customFieldsAdmin.errors.lengthInvalid'))
      }
      if (fields.includes('minLength') && v.minLength !== null && v.maxLength !== null && v.minLength > v.maxLength) {
        add('maxLength', t('customFieldsAdmin.errors.lengthOrder'))
      }
      if (fields.includes('min') && v.min !== null && v.max !== null && v.min > v.max) add('max', t('customFieldsAdmin.errors.rangeOrder'))
    })
}

/**
 * Valores del formulario → cuerpo del alta/edición. La etiqueta en inglés vacía copia la española; la descripción se guarda en
 * los dos idiomas; las opciones propias de una definición sin lista se conservan, y con lista se vacían.
 */
export function buildUpsert(v: DefinitionFormValues, def?: CustomFieldDefinitionDto | null): CustomFieldDefinitionUpsert {
  const type = v.dataType
  const labelEs = v.labelEs.trim()
  const list = isListType(type)
  const refEntity = list && v.refEntity.trim() !== '' ? v.refEntity.trim() : null
  const description = v.description.trim()
  return {
    fieldKey: def ? def.fieldKey : normalizeFieldKey(v.fieldKey),
    labels: { es: labelEs, en: v.labelEn.trim() || labelEs },
    descriptions: description === '' ? {} : { es: description, en: description },
    dataType: type,
    isRequired: v.isRequired,
    isUnique: v.isUnique,
    defaultValue: buildDefault(type, { text: v.defaultText, bool: v.defaultBool, multi: v.defaultMulti }),
    validationJson: buildValidationJson(type, v, readValidation(def?.validationJson).extra),
    refEntity,
    showInList: v.showInList,
    sortOrder: v.sortOrder,
    options: list && refEntity === null && def ? legacyOptions(def) : [],
  }
}

/**
 * Errores del servidor → nombre de campo del formulario. 409: en el alta es la clave repetida; en la edición, el cambio de tipo con
 * valores guardados. `labels`/`options`/`validationJson` no son campos del formulario y se llevan al control que los causa.
 */
export function mapServerErrors(status: number, title: string, errors: Record<string, string[]>, creating: boolean, type: string): Record<string, string[]> {
  const out: Record<string, string[]> = {}
  for (const [name, messages] of Object.entries(errors)) {
    const target =
      name === 'labels' ? 'labelEs' : name === 'options' ? 'refEntity' : name === 'validationJson' ? (validationFieldsFor(type)[0] ?? 'dataType') : name
    out[target] = [...(out[target] ?? []), ...messages]
  }
  if (status === 409 && Object.keys(out).length === 0) out[creating ? 'fieldKey' : 'dataType'] = [title]
  return out
}
