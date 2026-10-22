// Reglas puras de la captura RÁPIDA de envíos del portal (2026-10-22, pedido de Luis: «igual que en la aplicación del personal»: una fila = un envío, Enter guarda y sale la fila siguiente).
// Se arma sobre las mismas reglas de «Nuevo envío» (validateDraft / buildRequest), así que el servidor y los mensajes son los mismos.
import type { PortalPlaceDto } from '../portalApi'
import { buildRequest, emptyConsignee, type ErrorKey, type NewConsigneeDraft, type OrderDraft, validateDraft } from './newOrderRules'

export interface QuickRow {
  /** Fecha pedida AAAA-MM-DD (la primera de la fila). */
  date: string
  /** Su número de factura (solo si el cliente numera sus facturas). */
  invoice: string
  /** Lo que se teclea para buscar al consignatario. */
  consigneeQuery: string
  /** publicId del consignatario elegido ('' = ninguno). */
  consigneeId: string
  /** Consignatario nuevo (si no existe en la lista); null = se usa el elegido. */
  newConsignee: NewConsigneeDraft | null
  description: string
  pieces: string
  weightKg: string
  cod: string
  notes: string
}

export type QuickField = 'date' | 'invoice' | 'consignee' | 'description' | 'pieces' | 'weight' | 'cod' | 'notes'

/** Orden en que se recorren los campos con Enter / Tab. */
export const QUICK_FIELDS: readonly QuickField[] = ['date', 'invoice', 'consignee', 'description', 'pieces', 'weight', 'cod', 'notes']

/** Fecha de hoy (AAAA-MM-DD) en la zona de la persona. */
export function todayText(now = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`
}

export const emptyQuickRow = (date = todayText()): QuickRow => ({ date, invoice: '', consigneeQuery: '', consigneeId: '', newConsignee: null, description: '', pieces: '1', weightKg: '', cod: '', notes: '' })

/** La fila siguiente: conserva la fecha de la anterior (se captura por lotes del mismo día); lo demás vuelve a su valor inicial. */
export const nextQuickRow = (previous: QuickRow): QuickRow => emptyQuickRow(previous.date)

/** Texto en el que se busca un consignatario: nombre, dirección y pueblo. */
const haystack = (c: PortalPlaceDto) => `${c.name ?? ''} ${c.line1 ?? ''} ${c.city ?? ''}`.toLowerCase()

/** Consignatarios que coinciden con lo tecleado (todas las palabras, sin importar mayúsculas), los primeros `max`. */
export function matchConsignees(list: readonly PortalPlaceDto[], query: string, max = 6): PortalPlaceDto[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean)
  if (words.length === 0) return []
  return list.filter((c) => words.every((w) => haystack(c).includes(w))).slice(0, max)
}

/** La fila como el borrador de «Nuevo envío» (un paquete). */
export function toDraft(row: QuickRow): OrderDraft {
  return {
    consigneeId: row.newConsignee ? null : row.consigneeId || null,
    newConsignee: row.newConsignee,
    pickupId: null,
    packages: [{ description: row.description, pieces: row.pieces, weightKg: row.weightKg }],
    codAmount: row.cod,
    invoiceNumber: row.invoice,
    requestedDate: row.date,
    notes: row.notes,
  }
}

/** Errores de la fila (claves de «Nuevo envío»); vacío = se puede guardar. */
export function validateQuickRow(row: QuickRow, today = new Date()): ErrorKey[] {
  // sin consignatario elegido ni nuevo: el borrador diría «escoja o escriba uno nuevo»
  return validateDraft(toDraft(row), today)
}

export const quickRequest = (row: QuickRow, typesInvoice: boolean) => buildRequest(toDraft(row), typesInvoice)

/** Campo de la fila al que lleva cada error (para llevar el foco ahí). */
export const FIELD_OF_ERROR: Record<ErrorKey, QuickField> = { consignee: 'consignee', packages: 'pieces', pieces: 'pieces', weight: 'weight', cod: 'cod', date: 'date', notes: 'notes' }

/** El primer campo con error, en el orden de la fila. */
export function firstErrorField(errors: readonly ErrorKey[]): QuickField | null {
  const fields = new Set(errors.map((e) => FIELD_OF_ERROR[e]))
  return QUICK_FIELDS.find((f) => fields.has(f)) ?? null
}

export { emptyConsignee }
