// Conversión entre el valor del API y el del formulario, y validación en cliente de un campo personalizado
// (misma regla que CustomFieldService.SetValuesAsync: obligatorio, tipo y ValidationJson con el DSL).
import type { components } from '../api/schema'
import { toBool, toDate, toNumber, toText, validateValue, type ValidationIssue } from '../dsl'

export type CustomFieldDefinitionDto = components['schemas']['CustomFieldDefinitionDto']
export type CustomFieldValueDto = components['schemas']['CustomFieldValueDto']
export type CustomFieldOptionDto = components['schemas']['CustomFieldOptionDto']

/** Tipos de dato (CustomFieldDataTypes del dominio). Uno desconocido se trata como texto. */
export const CustomFieldDataTypes = {
  Text: 'TEXT',
  Number: 'NUMBER',
  Date: 'DATE',
  DateTime: 'DATETIME',
  Bool: 'BOOL',
  Select: 'SELECT',
  MultiSelect: 'MULTISELECT',
  LookupRef: 'LOOKUP_REF',
} as const

/**
 * Valor en el formulario: texto (TEXT, NUMBER, SELECT, LOOKUP_REF, DATE `yyyy-MM-dd`, DATETIME `yyyy-MM-ddTHH:mm` local),
 * booleano o null (BOOL) y arreglo de códigos (MULTISELECT).
 */
export type CustomFieldFormValue = string | boolean | null | string[]

/** Motivo de rechazo en cliente: los del DSL más obligatorio y tipo. */
export type CustomFieldIssue = ValidationIssue | { code: 'required' | 'number' | 'date' }

export function dataTypeOf(def: Pick<CustomFieldDefinitionDto, 'dataType'>): string {
  const type = (def.dataType ?? '').toUpperCase()
  return (Object.values(CustomFieldDataTypes) as string[]).includes(type) ? type : CustomFieldDataTypes.Text
}

const pad = (n: number) => String(n).padStart(2, '0')

function toLocalInput(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function toUtcDateInput(d: Date): string {
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`
}

/** Valor del API (o el DefaultValue de la definición) → valor del control. */
export function toFormValue(dataType: string, raw: unknown): CustomFieldFormValue {
  switch (dataType) {
    case CustomFieldDataTypes.Bool:
      return toBool(raw)
    case CustomFieldDataTypes.MultiSelect:
      if (Array.isArray(raw)) return raw.map((x) => toText(x).trim()).filter((x) => x.length > 0)
      if (typeof raw === 'string') return raw.split(',').map((x) => x.trim()).filter((x) => x.length > 0)
      return []
    case CustomFieldDataTypes.Date: {
      const d = toDate(raw)
      return d ? toUtcDateInput(d) : ''
    }
    case CustomFieldDataTypes.DateTime: {
      // El servidor guarda UTC (sin zona = UTC); el control datetime-local trabaja en hora local.
      const d = toDate(raw)
      return d ? toLocalInput(d) : ''
    }
    default:
      return raw === null || raw === undefined ? '' : toText(raw)
  }
}

export function isEmptyFormValue(v: CustomFieldFormValue | undefined): boolean {
  return v === null || v === undefined || v === '' || (Array.isArray(v) && v.length === 0)
}

function localInputToDate(v: string): Date | null {
  const d = new Date(v)
  return Number.isNaN(d.getTime()) ? null : d
}

/** Valor del control → valor que se envía en `PUT /custom-fields/values` (vacío = null; MULTISELECT vacío = []). */
export function fromFormValue(dataType: string, v: CustomFieldFormValue | undefined): unknown {
  if (dataType === CustomFieldDataTypes.MultiSelect) return Array.isArray(v) ? v : isEmptyFormValue(v) ? [] : [String(v)]
  if (isEmptyFormValue(v)) return null
  switch (dataType) {
    case CustomFieldDataTypes.Bool:
      return typeof v === 'boolean' ? v : toBool(v)
    case CustomFieldDataTypes.Number:
      return toNumber(v) ?? v
    case CustomFieldDataTypes.DateTime: {
      const d = typeof v === 'string' ? localInputToDate(v) : null
      return d ? d.toISOString() : v
    }
    default:
      return v
  }
}

/** Valor tipado para el DSL (igual que el servidor tras normalizar): número, Date, booleano o texto. */
function typedForRules(dataType: string, v: CustomFieldFormValue): unknown {
  switch (dataType) {
    case CustomFieldDataTypes.Number:
      return toNumber(v)
    case CustomFieldDataTypes.Date:
      return typeof v === 'string' ? toDate(v) : null
    case CustomFieldDataTypes.DateTime:
      return typeof v === 'string' ? localInputToDate(v) : null
    case CustomFieldDataTypes.Bool:
      return typeof v === 'boolean' ? v : toBool(v)
    case CustomFieldDataTypes.MultiSelect:
      return Array.isArray(v) ? v.join(',') : v
    default:
      return v
  }
}

/** Valida en cliente: obligatorio, tipo (número/fecha) y ValidationJson (DSL). Lista vacía = válido. */
export function validateCustomField(
  def: Pick<CustomFieldDefinitionDto, 'dataType' | 'isRequired' | 'validationJson'>,
  v: CustomFieldFormValue | undefined,
): CustomFieldIssue[] {
  const type = dataTypeOf(def)
  // Un booleano sin marcar (null) cuenta como vacío; false es un valor.
  if (isEmptyFormValue(v)) return def.isRequired ? [{ code: 'required' }] : []
  const typed = typedForRules(type, v as CustomFieldFormValue)
  if (type === CustomFieldDataTypes.Number && typed === null) return [{ code: 'number' }]
  if ((type === CustomFieldDataTypes.Date || type === CustomFieldDataTypes.DateTime) && typed === null) return [{ code: 'date' }]
  try {
    return validateValue(typed, def.validationJson)
  } catch {
    // ValidationJson inválido: el servidor lo rechaza al definir el campo; aquí no bloquea la captura.
    return []
  }
}

/** Clave i18n del motivo (`customFields.errors.<code>`) con su parámetro `limit`. */
export function issueMessageKey(issue: CustomFieldIssue): { key: string; params?: { limit: number } } {
  return 'limit' in issue ? { key: `customFields.errors.${issue.code}`, params: { limit: issue.limit } } : { key: `customFields.errors.${issue.code}` }
}

/** Opciones activas de la definición (SELECT/MULTISELECT sin lista de catálogo), por SortOrder. */
export function activeOptions(def: Pick<CustomFieldDefinitionDto, 'options'>): { code: string; label: string }[] {
  return [...(def.options ?? [])]
    .filter((o) => o.isActive !== false && !!o.value)
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
    .map((o) => ({ code: o.value ?? '', label: o.label || (o.value ?? '') }))
}
