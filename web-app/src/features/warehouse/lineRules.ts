// Reglas puras de captura de líneas de Recepción, Recolección y Conteo cíclico (Lote F6). Réplica en cliente de
// ReceiptRules / PickBatchRules / CycleCountRules del dominio: devuelven el campo y un código que la pantalla traduce con
// `t('warehouse.lineRules.<code>', params)` (el texto en español es el mensaje exacto del manual 06). El servidor vuelve a
// validar todo; esto solo pone el error bajo el campo antes de enviar.
import { useEffect, useState } from 'react'
import { parseApiDate } from '../../kernel/api/dates'
import { ApiError } from '../../kernel/api/problem'
import { formatMoney, formatQuantity } from '../../kernel/i18n'

export type TrackingCode = 'NONE' | 'LOT' | 'SERIAL' | string

export interface LineIssue {
  /** Campo del formulario (camelCase, como el DTO). */
  field: string
  /** Clave bajo `warehouse.lineRules`. */
  code: string
  params?: Record<string, string | number>
}

/** Tope exclusivo de DECIMAL(16,3) (máximo 9 999 999 999 999.999; el literal exacto no cabe en un double). */
export const QTY_LIMIT = 1e13
export const MAX_SERIAL_LENGTH = 80
export const MAX_SERIALS_PER_LINE = 500
export const LOT_NUMBER_MAX = 60

/** Series capturadas por teclado: una por renglón (o separadas por coma/punto y coma), recortadas, sin vacías. */
export function parseSerials(text: string | null | undefined): string[] {
  if (!text) return []
  return text
    .split(/[\n,;]+/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
}

/** Primera serie repetida (sin distinguir mayúsculas), o null. */
export function firstDuplicate(serials: readonly string[]): string | null {
  const seen = new Set<string>()
  for (const s of serials) {
    const k = s.toLowerCase()
    if (seen.has(k)) return s
    seen.add(k)
  }
  return null
}

export function decimalsOf(n: number): number {
  if (!Number.isFinite(n)) return 0
  const s = String(n)
  if (s.includes('e-')) return Number(s.split('e-')[1])
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

/** 3 decimales como máximo y dentro de DECIMAL(16,3). */
function scaleIssue(field: string, qty: number): LineIssue | null {
  if (qty >= QTY_LIMIT) return { field, code: 'qtyTooLarge' }
  if (decimalsOf(qty) > 3) return { field, code: 'qtyDecimals' }
  return null
}

function formatQty(qty: number): string {
  return String(Math.round(qty * 1000) / 1000)
}

// ---------------------------------------------------------------------------------------------------------------------
// Recepción (manual 06 §4, ReceiptRules)
// ---------------------------------------------------------------------------------------------------------------------
export interface ReceiptLineInput {
  sku: string
  trackingTypeCode: TrackingCode | null | undefined
  receivedQty: number | null | undefined
  /** Número de lote capturado ('' = sin lote). */
  lot: string
  lotManufactureDate?: string
  lotExpiryDate?: string
  serials: readonly string[]
}

/**
 * Validaciones de una línea de recibo (captura y alta). Como al confirmar: en SERIAL la cantidad debe ser entera e igual al
 * número de series; en LOT se exige el lote si se recibió algo; NONE no admite lote ni series.
 */
export function receiptLineIssues(line: ReceiptLineInput): LineIssue[] {
  const issues: LineIssue[] = []
  const qty = line.receivedQty
  if (qty == null || Number.isNaN(qty)) issues.push({ field: 'receivedQty', code: 'receivedQtyRequired' })
  else if (qty < 0) issues.push({ field: 'receivedQty', code: 'receivedQtyNegative' })
  else {
    const s = scaleIssue('receivedQty', qty)
    if (s) issues.push(s)
  }

  const serialIssue = serialListIssue(line.serials, 'serialNumbers', 'serialDuplicated', 'serialTooLong', 'tooManySerials')
  if (serialIssue) issues.push(serialIssue)

  const lot = line.lot.trim()
  if (lot.length > LOT_NUMBER_MAX) issues.push({ field: 'lot', code: 'lotTooLong' })
  else if (lot && line.lotManufactureDate && line.lotExpiryDate && line.lotExpiryDate < line.lotManufactureDate)
    issues.push({ field: 'lot', code: 'lotDates' })

  const sku = line.sku
  const hasQty = qty != null && !Number.isNaN(qty) && qty >= 0
  switch (line.trackingTypeCode) {
    case 'LOT':
      if (line.serials.length > 0) issues.push({ field: 'serialNumbers', code: 'serialsNotAllowed', params: { sku } })
      if (!lot && hasQty && (qty ?? 0) > 0) issues.push({ field: 'lot', code: 'lotRequired', params: { sku } })
      break
    case 'SERIAL':
      if (hasQty && !Number.isInteger(qty)) issues.push({ field: 'receivedQty', code: 'serialInteger', params: { sku } })
      else if (hasQty && !serialIssue && line.serials.length !== qty)
        issues.push({ field: 'serialNumbers', code: 'serialCountMismatch', params: { sku, qty: formatQty(qty ?? 0), n: line.serials.length } })
      break
    case 'NONE':
      if (lot) issues.push({ field: 'lot', code: 'lotNotAllowed', params: { sku } })
      if (line.serials.length > 0) issues.push({ field: 'serialNumbers', code: 'serialsNotAllowed', params: { sku } })
      break
    default:
      break
  }
  return firstPerField(issues)
}

function serialListIssue(serials: readonly string[], field: string, dupCode: string, longCode: string, manyCode: string): LineIssue | null {
  const long = serials.find((s) => s.length > MAX_SERIAL_LENGTH)
  if (long) return { field, code: longCode, params: { serial: long } }
  const dup = firstDuplicate(serials)
  if (dup) return { field, code: dupCode, params: { serial: dup } }
  if (serials.length > MAX_SERIALS_PER_LINE) return { field, code: manyCode }
  return null
}

/** Un error por campo (el primero), como lo pintaría el servidor. */
function firstPerField(issues: LineIssue[]): LineIssue[] {
  const seen = new Set<string>()
  return issues.filter((i) => (seen.has(i.field) ? false : (seen.add(i.field), true)))
}

// ---------------------------------------------------------------------------------------------------------------------
// Conteo cíclico (manual 06 §6, CycleCountRules.Capture)
// ---------------------------------------------------------------------------------------------------------------------
export interface CountLineInput {
  sku: string
  trackingTypeCode: TrackingCode | null | undefined
  /** null = sin capturar (borra la captura). */
  countedQty: number | null | undefined
  serials: readonly string[]
}

/** SERIAL: se capturan las series (sin cantidad suelta); NONE/LOT: la cantidad (≥ 0, 3 decimales) y ninguna serie. */
export function countLineIssues(line: CountLineInput): LineIssue[] {
  const issues: LineIssue[] = []
  if (line.trackingTypeCode === 'SERIAL') {
    const s = serialListIssue(line.serials, 'serialNumbers', 'countSerialDuplicated', 'countSerialTooLong', 'tooManySerials')
    if (s) issues.push(s)
    return issues
  }
  if (line.serials.length > 0) issues.push({ field: 'serialNumbers', code: 'serialsNotAllowed', params: { sku: line.sku } })
  const qty = line.countedQty
  if (qty != null && !Number.isNaN(qty)) {
    if (qty < 0) issues.push({ field: 'countedQty', code: 'countedNegative' })
    else {
      const s = scaleIssue('countedQty', qty)
      if (s) issues.push(s)
    }
  }
  return issues
}

/** Lote de una línea agregada a mano al conteo: LOT lo exige, NONE lo prohíbe, SERIAL lo admite. */
export function countLotIssue(trackingTypeCode: TrackingCode | null | undefined, sku: string, hasLot: boolean): LineIssue | null {
  if (trackingTypeCode === 'LOT' && !hasLot) return { field: 'lot', code: 'countLotRequired', params: { sku } }
  if (trackingTypeCode === 'NONE' && hasLot) return { field: 'lot', code: 'lotNotAllowed', params: { sku } }
  return null
}

// ---------------------------------------------------------------------------------------------------------------------
// Recolección (manual 06 §7, PickBatchRules)
// ---------------------------------------------------------------------------------------------------------------------
export interface PickLineInput {
  sku: string
  trackingTypeCode: TrackingCode | null | undefined
  quantity: number | null | undefined
  hasLot: boolean
  serials: readonly string[]
}

export function pickLineIssues(line: PickLineInput): LineIssue[] {
  const issues: LineIssue[] = []
  const qty = line.quantity
  if (qty == null || Number.isNaN(qty) || qty <= 0) issues.push({ field: 'quantity', code: 'pickQtyRequired' })
  else {
    const s = scaleIssue('quantity', qty)
    if (s) issues.push(s)
  }
  const s = serialListIssue(line.serials, 'serialNumbers', 'pickSerialDuplicated', 'serialTooLong', 'pickTooManySerials')
  if (s) issues.push(s)
  const sku = line.sku
  if (line.trackingTypeCode === 'SERIAL') {
    if (line.serials.length === 0) issues.push({ field: 'serialNumbers', code: 'pickSerialRequired', params: { sku } })
    else if (qty != null && qty > 0 && line.serials.length !== qty) issues.push({ field: 'serialNumbers', code: 'pickSerialMismatch' })
  } else if (line.trackingTypeCode === 'NONE' || line.trackingTypeCode === 'LOT') {
    if (line.serials.length > 0) issues.push({ field: 'serialNumbers', code: 'pickSerialNotAllowed', params: { sku } })
    if (line.trackingTypeCode === 'NONE' && line.hasLot) issues.push({ field: 'lotId', code: 'pickLotNotTracked', params: { sku } })
  }
  return firstPerField(issues)
}

/** Serie repetida entre líneas de la misma recolección: devuelve [índice de línea, serie] de la primera repetición. */
export function pickDuplicateAcrossLines(lines: readonly (readonly string[])[]): [number, string] | null {
  const seen = new Set<string>()
  for (let i = 0; i < lines.length; i++) {
    const own = new Set<string>()
    for (const s of lines[i]) {
      const k = s.toLowerCase()
      if (own.has(k)) continue
      own.add(k)
      if (seen.has(k)) return [i, s]
    }
    for (const k of own) seen.add(k)
  }
  return null
}

/** Una recolección solo admite productos de un mismo dueño (null = propio). Índice de la primera línea que rompe la regla. */
export function firstOtherOwner(owners: readonly (string | null | undefined)[]): number | null {
  let first: string | null | undefined
  let set = false
  for (let i = 0; i < owners.length; i++) {
    if (owners[i] === undefined) continue // producto aún sin elegir
    const o = owners[i] ?? null
    if (!set) {
      first = o
      set = true
    } else if (o !== first) return i
  }
  return null
}

// ---------------------------------------------------------------------------------------------------------------------
// Errores del servidor y formato
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Renombra los campos de un error del API (p. ej. `lines[0].countedQty` → `countedQty`, `line` → `serialNumbers`) para que
 * `Form` los pinte bajo el campo correcto. Cualquier otro error se devuelve tal cual.
 */
export function remapProblemFields(err: unknown, rename: (field: string) => string | null | undefined): unknown {
  if (!(err instanceof ApiError)) return err
  const errors: Record<string, string[]> = {}
  for (const [k, v] of Object.entries(err.errors)) {
    const n = rename(k) || k
    errors[n] = [...(errors[n] ?? []), ...v]
  }
  return new ApiError(err.status, { title: err.title, code: err.code, errors, correlationId: err.correlationId })
}

/** Errores por línea del API (`lines[i]` o `lines[i].campo`) → { índice: mensajes }. */
export function lineErrorsByIndex(err: unknown): Record<number, string[]> {
  const out: Record<number, string[]> = {}
  if (!(err instanceof ApiError)) return out
  for (const [k, v] of Object.entries(err.errors)) {
    const m = /^(?:\$\.)?lines\[(\d+)\]/i.exec(k)
    if (m) out[Number(m[1])] = [...(out[Number(m[1])] ?? []), ...v]
  }
  return out
}

/** Cantidad con coma de miles (formato de Puerto Rico; ver kernel/i18n/numberFormat). */
export function formatNumber(n: number | null | undefined, lang: string): string {
  if (n == null) return ''
  return formatQuantity(n, lang)
}

/** Dinero con signo de dólar: "$1,234.50" (vacío si no hay valor). */
export function formatMoneyValue(n: number | null | undefined, lang: string, opts?: { unitPrice?: boolean; currency?: string | null }): string {
  if (n == null) return ''
  return formatMoney(n, lang, opts)
}

export function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const d = parseApiDate(iso)
  if (Number.isNaN(d.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(d)
}

export function formatDate(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  // fecha sin hora ('YYYY-MM-DD'): se muestra tal cual el día, sin corrimiento de zona
  const d = /^\d{4}-\d{2}-\d{2}$/.test(iso) ? new Date(`${iso}T12:00:00`) : parseApiDate(iso)
  if (Number.isNaN(d.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium' }).format(d)
}

/** Valor con retardo (búsquedas libres que van al API: no una consulta por tecla). */
export function useDebounced<T>(value: T, ms = 300): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return debounced
}
