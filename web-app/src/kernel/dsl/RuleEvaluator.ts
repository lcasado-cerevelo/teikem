// Evaluador del DSL de reglas en el cliente: misma semántica que src/Teikem.Infrastructure/Dsl/RuleEvaluator.cs
// (filtros de vistas/indicadores/gráficos y validación de campos personalizados). El servidor sigue siendo el árbitro;
// aquí se usa para filtrar lo que ya está en pantalla y para validar antes de enviar.
//
// Filtro (JSON o el objeto ya parseado):
//   { "and": [ {...}, {...} ] } | { "or": [...] } | { "not": {...} }
//   { "field": "Status", "op": "eq", "value": "DELIVERED" }
//   [ {...}, {...} ]  → equivale a "and"
// Operadores: eq, ne, gt, gte, lt, lte, contains, startsWith, endsWith, in, notIn, between, isNull, notNull, isTrue, isFalse
// (y los alias del servidor: neq, ge, le, empty, notEmpty).
// Validación (JSON): { "regex": "...", "min": 0, "max": 100, "minLength": 1, "maxLength": 50 }

/** Fila evaluada: nombre de campo → valor. El campo se busca exacto y, si no está, sin distinguir mayúsculas. */
export type RuleRow = Readonly<Record<string, unknown>>

/** Nodo del filtro tal como llega en el JSON. */
export type RuleNode =
  | readonly RuleNode[]
  | { readonly and: readonly RuleNode[] }
  | { readonly or: readonly RuleNode[] }
  | { readonly not: RuleNode }
  | { readonly field: string; readonly op?: string; readonly value?: unknown }

/** Filtro aceptado: JSON en texto, el objeto ya parseado o nada (= todo coincide). */
export type RuleInput = string | RuleNode | null | undefined

// El servidor distingue "value": null (JsonElement de tipo Null, no es null de .NET) de la ausencia de "value".
// Se replica con un centinela: un null del JSON no es igual a un campo nulo y se lee como texto vacío.
class JsonNull {
  toString(): string {
    return ''
  }
}
const JSON_NULL = new JsonNull()

type Value = unknown

// ------------------------------------------------------------------ Conversión tipada

function looksLikeDate(s: string | null | undefined): boolean {
  if (!s || s.length < 8) return false
  let dashes = 0
  for (const c of s) if (c === '-') dashes++
  return dashes >= 2
}

const NUMBER_RE = /^[+-]?(?:\d[\d,]*)?(?:\.\d*)?(?:[eE][+-]?\d+)?$/

function parseNumber(s: string): number | null {
  const text = s.trim()
  if (!/\d/.test(text) || !NUMBER_RE.test(text) || looksLikeDate(s)) return null
  const n = Number(text.replace(/,/g, ''))
  return Number.isFinite(n) ? n : null
}

/** Número o null (texto numérico invariante sí; fechas, booleanos y texto no numérico no). */
export function toNumber(v: Value): number | null {
  if (v === null || v === undefined || v instanceof JsonNull) return null
  if (typeof v === 'number') return Number.isFinite(v) ? v : null
  if (typeof v === 'bigint') return Number(v)
  if (typeof v === 'string') return parseNumber(v)
  return null
}

const ISO_RE = /^(\d{4})-(\d{1,2})-(\d{1,2})(?:[T ](\d{1,2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?)?\s*(Z|[+-]\d{2}:?\d{2})?$/i
const MDY_RE = /^(\d{1,2})-(\d{1,2})-(\d{4})$/

function validDay(y: number, mo: number, d: number): boolean {
  if (mo < 1 || mo > 12 || d < 1) return false
  const date = new Date(Date.UTC(y, mo - 1, d))
  return date.getUTCMonth() === mo - 1 && date.getUTCDate() === d
}

function parseDate(s: string | null | undefined): Date | null {
  if (!s || !s.trim() || !looksLikeDate(s)) return null
  const text = s.trim()
  const iso = ISO_RE.exec(text)
  if (iso) {
    const [, y, mo, d, h = '0', mi = '0', sec = '0', frac = '', zone] = iso
    // Fechas u horas imposibles (2026-02-31, 25:00) no se aceptan
    if (!validDay(Number(y), Number(mo), Number(d)) || Number(h) > 23 || Number(mi) > 59 || Number(sec) > 59) return null
    const ms = frac ? Math.round(Number(`0.${frac}`) * 1000) : 0
    let time = Date.UTC(Number(y), Number(mo) - 1, Number(d), Number(h), Number(mi), Number(sec), ms)
    // Sin zona se asume UTC (AssumeUniversal del servidor); con desfase se lleva a UTC.
    if (zone && zone.toUpperCase() !== 'Z') {
      const sign = zone.startsWith('-') ? -1 : 1
      const digits = zone.slice(1).replace(':', '')
      time -= sign * (Number(digits.slice(0, 2)) * 60 + Number(digits.slice(2, 4))) * 60_000
    }
    return new Date(time)
  }
  const mdy = MDY_RE.exec(text)
  if (mdy) {
    const [, mo, d, y] = mdy
    return validDay(Number(y), Number(mo), Number(d)) ? new Date(Date.UTC(Number(y), Number(mo) - 1, Number(d))) : null
  }
  return null
}

/** Fecha o null: objetos Date o texto con forma de fecha (yyyy-MM-dd, ISO con o sin zona; sin zona = UTC). */
export function toDate(v: Value): Date | null {
  if (v instanceof Date) return Number.isNaN(v.getTime()) ? null : v
  if (typeof v === 'string') return parseDate(v)
  return null
}

/** Booleano o null: true/false, "true"/"false" (sin distinguir mayúsculas), "1"/"0" y enteros. */
export function toBool(v: Value): boolean | null {
  if (typeof v === 'boolean') return v
  if (typeof v === 'string') {
    const s = v.trim().toLowerCase()
    if (s === 'true') return true
    if (s === 'false') return false
    if (v === '1' || v === '0') return v === '1'
    return null
  }
  if (typeof v === 'number' && Number.isInteger(v)) return v !== 0
  return null
}

/** Texto para comparar (fechas en ISO, números invariantes). */
export function toText(v: Value): string {
  if (v === null || v === undefined || v instanceof JsonNull) return ''
  if (typeof v === 'string') return v
  if (v instanceof Date) return Number.isNaN(v.getTime()) ? '' : v.toISOString()
  if (typeof v === 'number' || typeof v === 'boolean' || typeof v === 'bigint') return String(v)
  try {
    return JSON.stringify(v, (_k, x: unknown) => (x instanceof JsonNull ? null : x)) ?? ''
  } catch {
    return String(v)
  }
}

function isNullish(v: Value): v is null | undefined {
  return v === null || v === undefined
}

function upper(s: string): string {
  return s.toUpperCase()
}

/** Comparación tipada: <0, 0, >0, o null si algún lado es nulo. Números, luego fechas, luego booleanos, luego texto. */
export function compareValues(left: Value, right: Value): number | null {
  if (isNullish(left) || isNullish(right)) return null
  const ln = toNumber(left)
  const rn = toNumber(right)
  if (ln !== null && rn !== null) return Math.sign(ln - rn)
  const ld = toDate(left)
  const rd = toDate(right)
  if (ld && rd) return Math.sign(ld.getTime() - rd.getTime())
  const rb = toBool(right)
  if (typeof left === 'boolean' && rb !== null) return left === rb ? 0 : left ? 1 : -1
  const a = upper(toText(left))
  const b = upper(toText(right))
  return a === b ? 0 : a < b ? -1 : 1
}

/** Igualdad tipada (texto sin distinguir mayúsculas). */
export function valuesEqual(left: Value, right: Value): boolean {
  if (isNullish(left) && isNullish(right)) return true
  if (isNullish(left) || isNullish(right)) return false
  const ln = toNumber(left)
  const rn = toNumber(right)
  if (ln !== null && rn !== null) return ln === rn
  const ld = toDate(left)
  const rd = toDate(right)
  if (ld && rd) return ld.getTime() === rd.getTime()
  const lb = toBool(left)
  const rb = toBool(right)
  if (lb !== null && rb !== null) return lb === rb
  return upper(toText(left)) === upper(toText(right))
}

// ------------------------------------------------------------------ Árbol de filtro

type Compiled = (row: RuleRow) => boolean
type FilterNode = { evaluate: Compiled; collect: (fields: Map<string, string>) => void }

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v) && !(v instanceof Date)
}

/** Valor del JSON de la regla: null → centinela, arreglos recursivos. */
function fromJson(v: unknown): Value {
  if (v === null) return JSON_NULL
  if (Array.isArray(v)) return v.map(fromJson)
  return v
}

function items(v: Value): Value[] {
  if (Array.isArray(v)) return v
  if (isNullish(v)) return []
  return [v]
}

function readField(row: RuleRow, field: string): Value {
  if (Object.prototype.hasOwnProperty.call(row, field)) return row[field]
  const wanted = field.toLowerCase()
  for (const key of Object.keys(row)) if (key.toLowerCase() === wanted) return row[key]
  return undefined
}

function isEmptyValue(v: Value): boolean {
  return isNullish(v) || v === ''
}

function condition(field: string, op: string, value: Value): FilterNode {
  const test = (actual: Value): boolean => {
    switch (op.toLowerCase()) {
      case 'eq':
        return valuesEqual(actual, value)
      case 'ne':
      case 'neq':
        return !valuesEqual(actual, value)
      case 'gt': {
        const c = compareValues(actual, value)
        return c !== null && c > 0
      }
      case 'gte':
      case 'ge': {
        const c = compareValues(actual, value)
        return c !== null && c >= 0
      }
      case 'lt': {
        const c = compareValues(actual, value)
        return c !== null && c < 0
      }
      case 'lte':
      case 'le': {
        const c = compareValues(actual, value)
        return c !== null && c <= 0
      }
      case 'contains':
        return !isNullish(actual) && upper(toText(actual)).includes(upper(toText(value)))
      case 'startswith':
        return !isNullish(actual) && upper(toText(actual)).startsWith(upper(toText(value)))
      case 'endswith':
        return !isNullish(actual) && upper(toText(actual)).endsWith(upper(toText(value)))
      case 'isnull':
      case 'empty':
        return isEmptyValue(actual)
      case 'notnull':
      case 'notempty':
        return !isEmptyValue(actual)
      case 'istrue':
        return toBool(actual) === true
      case 'isfalse':
        return toBool(actual) === false
      case 'in':
        return items(value).some((item) => valuesEqual(actual, item))
      case 'notin':
        return !items(value).some((item) => valuesEqual(actual, item))
      case 'between': {
        const range = items(value)
        if (range.length !== 2) return false
        const lo = compareValues(actual, range[0])
        const hi = compareValues(actual, range[1])
        return lo !== null && lo >= 0 && hi !== null && hi <= 0
      }
      default:
        throw new Error(`Operador de filtro desconocido: '${op}'.`)
    }
  }
  return {
    evaluate: (row) => test(readField(row, field)),
    collect: (fields) => {
      const k = field.toLowerCase()
      if (!fields.has(k)) fields.set(k, field)
    },
  }
}

function logical(op: 'and' | 'or', children: FilterNode[]): FilterNode {
  return {
    evaluate: op === 'or' ? (row) => children.some((c) => c.evaluate(row)) : (row) => children.every((c) => c.evaluate(row)),
    collect: (fields) => children.forEach((c) => c.collect(fields)),
  }
}

function parseNode(e: unknown): FilterNode | null {
  if (Array.isArray(e)) return logical('and', e.map(parseNode).filter((n): n is FilterNode => n !== null))
  if (!isRecord(e)) return null
  for (const op of ['and', 'or'] as const) {
    const arr = e[op]
    if (Array.isArray(arr)) return logical(op, arr.map(parseNode).filter((n): n is FilterNode => n !== null))
  }
  if ('not' in e && e.not !== undefined) {
    const inner = parseNode(e.not)
    return { evaluate: (row) => inner === null || !inner.evaluate(row), collect: (fields) => inner?.collect(fields) }
  }
  if (typeof e.field === 'string') {
    const op = typeof e.op === 'string' ? e.op : 'eq'
    const value = 'value' in e && e.value !== undefined ? fromJson(e.value) : null
    return condition(e.field, op, value)
  }
  return null
}

function parseInput(filter: RuleInput): FilterNode | null {
  if (filter === null || filter === undefined) return null
  if (typeof filter === 'string') {
    if (!filter.trim()) return null
    return parseNode(JSON.parse(filter) as unknown)
  }
  return parseNode(filter)
}

/** Compila el filtro una vez y devuelve el predicado. Filtro vacío = todo coincide. JSON inválido lanza. */
export function compileFilter(filter: RuleInput): Compiled {
  const node = parseInput(filter)
  return node === null ? () => true : node.evaluate
}

/** ¿La fila cumple el filtro? */
export function matches(row: RuleRow, filter: RuleInput): boolean {
  return compileFilter(filter)(row)
}

/** Alias con el orden (regla, fila): `evaluateRule(rule, row)`. */
export function evaluateRule(rule: RuleInput, row: RuleRow): boolean {
  return matches(row, rule)
}

/** Campos referenciados por el filtro (sin repetir, sin distinguir mayúsculas). */
export function referencedFields(filter: RuleInput): string[] {
  const node = parseInput(filter)
  const fields = new Map<string, string>()
  node?.collect(fields)
  return [...fields.values()]
}

// ------------------------------------------------------------------ Validación

export interface ValidationSpec {
  regex: string | null
  min: number | null
  max: number | null
  minLength: number | null
  maxLength: number | null
}

/** Motivo de rechazo de un valor. `limit` es el límite violado (longitud, número o días relativos a hoy). */
export type ValidationIssue =
  | { code: 'minLength' | 'maxLength' | 'min' | 'max' | 'minDate' | 'maxDate'; limit: number }
  | { code: 'pattern' | 'patternInvalid' }

/** Lee la especificación de validación ("regex" o "pattern", min, max, minLength, maxLength). JSON inválido lanza. */
export function parseValidation(validation: string | Record<string, unknown> | null | undefined): ValidationSpec | null {
  if (validation === null || validation === undefined) return null
  let root: unknown = validation
  if (typeof validation === 'string') {
    if (!validation.trim()) return null
    root = JSON.parse(validation) as unknown
  }
  if (!isRecord(root)) return null
  const str = (name: string) => (typeof root[name] === 'string' ? (root[name] as string) : null)
  const num = (name: string) => (name in root ? toNumber(root[name]) : null)
  const int = (name: string) => {
    const n = num(name)
    return n === null ? null : Math.trunc(n)
  }
  return { regex: str('regex') ?? str('pattern'), min: num('min'), max: num('max'), minLength: int('minLength'), maxLength: int('maxLength') }
}

function todayUtcPlusDays(days: number): number {
  const now = new Date()
  return Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()) + days * 86_400_000
}

/**
 * Valida un valor contra la especificación; lista vacía = válido. Texto: longitud y regex; número (o texto numérico):
 * min/max; Date: min/max en días relativos a hoy (negativo = pasado). Valor nulo = válido (lo obligatorio va aparte).
 */
export function validateValue(value: unknown, validation: string | Record<string, unknown> | null | undefined): ValidationIssue[] {
  const spec = parseValidation(validation)
  const issues: ValidationIssue[] = []
  if (spec === null || isNullish(value)) return issues

  if (typeof value === 'string') {
    if (spec.minLength !== null && value.length < spec.minLength) issues.push({ code: 'minLength', limit: spec.minLength })
    if (spec.maxLength !== null && value.length > spec.maxLength) issues.push({ code: 'maxLength', limit: spec.maxLength })
    if (spec.regex) {
      try {
        if (!new RegExp(spec.regex).test(value)) issues.push({ code: 'pattern' })
      } catch {
        issues.push({ code: 'patternInvalid' })
      }
    }
  }
  const n = toNumber(value)
  if (n !== null) {
    if (spec.min !== null && n < spec.min) issues.push({ code: 'min', limit: spec.min })
    if (spec.max !== null && n > spec.max) issues.push({ code: 'max', limit: spec.max })
  }
  if (value instanceof Date) {
    if (spec.min !== null && value.getTime() < todayUtcPlusDays(spec.min)) issues.push({ code: 'minDate', limit: spec.min })
    if (spec.max !== null && value.getTime() > todayUtcPlusDays(spec.max)) issues.push({ code: 'maxDate', limit: spec.max })
  }
  return issues
}
