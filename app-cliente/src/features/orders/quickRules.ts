// Reglas puras de la captura RÁPIDA de envíos del portal (2026-10-23, dictado de Luis: igual que «Entrada rápida» de la web del personal: una fila = un envío de 1 paquete y 1 pieza;
// las columnas son Fecha, N.º de orden (si el cliente los asigna), N.º de factura (si los asigna), Consignatario, Dirección · Pueblo, Tipo de paquete, COD, Servicio y Comentario;
// Tab recorre, Enter guarda y sale la fila siguiente con la misma fecha, servicio y tipo de paquete). Se arma sobre las reglas de «Nuevo envío» (validateDraft / buildRequest).
import type { PortalPlaceDto } from '../portalApi'
import { buildRequest, emptyConsignee, type ErrorKey, type FormRules, type NewConsigneeDraft, type OrderDraft, todayText, validateDraft } from './newOrderRules'

export interface QuickRow {
  /** Fecha del envío AAAA-MM-DD (la primera de la fila; es la fecha lista y la límite). */
  date: string
  /** Su número de orden (solo si el cliente asigna sus números). */
  orderNumber: string
  /** Su número de factura (solo si el cliente numera sus facturas). */
  invoice: string
  /** Lo que se teclea para buscar al consignatario. */
  consigneeQuery: string
  /** publicId del consignatario elegido ('' = ninguno). */
  consigneeId: string
  /** Consignatario nuevo (si no existe en la lista); null = se usa el elegido. */
  newConsignee: NewConsigneeDraft | null
  packageType: string
  cod: string
  serviceType: string
  notes: string
}

export type QuickField = 'date' | 'orderNumber' | 'invoice' | 'consignee' | 'packageType' | 'cod' | 'serviceType' | 'notes'

/** Orden en que se recorren los campos con Tab (y el orden del primer error). */
export const QUICK_FIELDS: readonly QuickField[] = ['date', 'orderNumber', 'invoice', 'consignee', 'packageType', 'cod', 'serviceType', 'notes']

export { todayText }

export interface QuickDefaults {
  serviceType?: string
  packageType?: string
}

export const emptyQuickRow = (date = todayText(), defaults: QuickDefaults = {}): QuickRow => ({
  date, orderNumber: '', invoice: '', consigneeQuery: '', consigneeId: '', newConsignee: null, packageType: defaults.packageType ?? '', cod: '', serviceType: defaults.serviceType ?? '', notes: '',
})

/** La fila siguiente: conserva la fecha, el servicio y el tipo de paquete de la anterior (se captura por lotes); lo demás vuelve a su valor inicial. */
export const nextQuickRow = (previous: QuickRow): QuickRow => emptyQuickRow(previous.date, { serviceType: previous.serviceType, packageType: previous.packageType })

/** Texto en el que se busca un consignatario: nombre, número, dirección y pueblo. */
const haystack = (c: PortalPlaceDto) => `${c.name ?? ''} ${c.code ?? ''} ${c.line1 ?? ''} ${c.city ?? ''}`.toLowerCase()

/** Consignatarios que coinciden con lo tecleado (todas las palabras, sin importar mayúsculas), los primeros `max`. */
export function matchConsignees(list: readonly PortalPlaceDto[], query: string, max = 6): PortalPlaceDto[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean)
  if (words.length === 0) return []
  return list.filter((c) => words.every((w) => haystack(c).includes(w))).slice(0, max)
}

/** La fila como el borrador de «Nuevo envío» (un paquete de una pieza; la fecha es la lista y la límite, como en la web del personal). */
export function toDraft(row: QuickRow): OrderDraft {
  return {
    serviceType: row.serviceType,
    readyDate: row.date,
    dueDate: row.date,
    orderNumber: row.orderNumber,
    invoiceNumber: row.invoice,
    consigneeId: row.newConsignee ? null : row.consigneeId || null,
    newConsignee: row.newConsignee,
    pickupId: null,
    packages: [{ packageType: row.packageType, description: '', pieces: '1', weightKg: '' }],
    codAmount: row.cod,
    notes: row.notes,
  }
}

/** Errores de la fila (claves de «Nuevo envío»); vacío = se puede guardar. La fecha es obligatoria en la rápida. */
export function validateQuickRow(row: QuickRow, rules: Partial<FormRules> = {}, today = new Date()): ErrorKey[] {
  const errors = validateDraft(toDraft(row), rules, today)
  if (row.date.trim() === '' && !errors.includes('date')) errors.push('date')
  return errors
}

export const quickRequest = (row: QuickRow, rules: Partial<FormRules> = {}) => buildRequest(toDraft(row), rules)

/** Campo de la fila al que lleva cada error (para llevar el foco ahí). */
export const FIELD_OF_ERROR: Record<ErrorKey, QuickField> = {
  service: 'serviceType', consignee: 'consignee', packages: 'packageType', packageType: 'packageType', pieces: 'packageType', weight: 'packageType', cod: 'cod', date: 'date', dueDate: 'date', orderNumber: 'orderNumber', invoice: 'invoice', notes: 'notes',
}

/** El primer campo con error, en el orden de la fila. */
export function firstErrorField(errors: readonly ErrorKey[]): QuickField | null {
  const fields = new Set(errors.map((e) => FIELD_OF_ERROR[e]))
  return QUICK_FIELDS.find((f) => fields.has(f)) ?? null
}

export { emptyConsignee }

// ---------------------------------------------------------------------------------------------------------------------
// Lo capturado en esta sesión: filtro de UN día + búsqueda libre y el resumen del pie (pedido de Luis, 2026-10-23; igual que la «Entrada rápida» de la web del personal)
// ---------------------------------------------------------------------------------------------------------------------

/** Minúsculas y sin acentos, para comparar. */
const fold = (v: string) => v.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()

/** ¿El texto de la entrada tiene todas las palabras de la búsqueda? (sin importar mayúsculas ni acentos) */
function matchesWords(q: string, texts: (string | number | null | undefined)[]): boolean {
  const words = fold(q).split(/\s+/).filter(Boolean)
  if (words.length === 0) return true
  const hay = fold(texts.filter((x) => x !== null && x !== undefined && x !== '').join(' '))
  return words.every((w) => hay.includes(w))
}

/** Lo que de una entrada guardada se filtra y se resume. */
export interface SavedEntry {
  /** Fecha del envío 'YYYY-MM-DD'. */
  date: string
  /** N.º de factura tal como se ve: puede traer varios separados por coma. */
  invoice: string
  packageLabel: string
  serviceLabel: string
  cod: number
}

/** El rango de fechas de lo capturado (ambos extremos incluidos, 'YYYY-MM-DD'). */
export interface DayRange {
  from: string
  to: string
}

/**
 * El rango después de guardar una entrada con la fecha `date`: lo que se está capturando siempre se ve. Se ensancha hacia la fecha más vieja (desde) o la más reciente (hasta) y, mientras no se
 * capture nada fuera de hoy, desde y hasta siguen iguales (pedido de Luis, 2026-10-24).
 */
export function widenRange(range: DayRange, date: string): DayRange {
  if (!date) return range
  return { from: date < range.from ? date : range.from, to: date > range.to ? date : range.to }
}

/** Cambia un extremo del rango sin dejarlo al revés: si «desde» pasa de «hasta» (o al contrario) el otro lo sigue. */
export function setRangeEdge(range: DayRange, edge: 'from' | 'to', value: string): DayRange {
  if (!value) return range
  if (edge === 'from') return { from: value, to: value > range.to ? value : range.to }
  return { from: value < range.from ? value : range.from, to: value }
}

/**
 * Las entradas guardadas que se ven: las del rango `range` (desde y hasta incluidos) y, si hay búsqueda, las que tienen en cualquier texto mostrado todas las palabras (sin importar mayúsculas ni
 * acentos). `texts` da los textos de la entrada (número, factura, consignatario, dirección, paquete, servicio, COD, comentario…).
 */
export function filterSavedEntries<T extends SavedEntry>(rows: readonly T[], range: DayRange, q: string, texts: (row: T) => (string | number | null | undefined)[]): T[] {
  return rows.filter((r) => r.date >= range.from && r.date <= range.to && matchesWords(q, [r.date, ...texts(r)]))
}

/** Los números de factura de un texto: se separan por coma, cada coma es otro número; sin espacios sobrantes ni vacíos. */
export function splitInvoices(value: string | null | undefined): string[] {
  return (value ?? '').split(',').map((x) => x.trim()).filter(Boolean)
}

export interface SavedSummaryLine {
  packageLabel: string
  serviceLabel: string
  count: number
}

export interface SavedSummary {
  /** Total de paquetes (cada entrada es un paquete de una pieza; es la suma de la columna de paquete). */
  total: number
  /** Números de factura DISTINTOS (una factura con varios números separados por coma cuenta cada uno; sin importar mayúsculas). */
  invoices: number
  /** Total de COD. */
  cod: number
  /** «8 cajas de servicio estándar, 3 sobres de servicio expreso…»: por tipo de paquete y tipo de servicio, las más numerosas primero. */
  lines: SavedSummaryLine[]
}

export function summarizeSaved(rows: readonly SavedEntry[]): SavedSummary {
  const byKey = new Map<string, SavedSummaryLine>()
  const invoices = new Set<string>()
  let cod = 0
  for (const r of rows) {
    cod += r.cod
    for (const n of splitInvoices(r.invoice)) invoices.add(n.toUpperCase())
    const key = `${r.packageLabel}\u0000${r.serviceLabel}`
    const line = byKey.get(key)
    if (line) line.count += 1
    else byKey.set(key, { packageLabel: r.packageLabel, serviceLabel: r.serviceLabel, count: 1 })
  }
  const lines = [...byKey.values()].sort((a, b) => b.count - a.count || a.packageLabel.localeCompare(b.packageLabel) || a.serviceLabel.localeCompare(b.serviceLabel))
  return { total: rows.length, invoices: invoices.size, cod: Math.round(cod * 100) / 100, lines }
}
