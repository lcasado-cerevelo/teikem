// Reglas puras de la entrada RÁPIDA de órdenes (Lote F-C3; maqueta `quickEditor`/`initQuick`): una fila = una orden de 1 paquete y 1 pieza.
// Validación de la fila con los mensajes del servidor (manual 03 §1.1), request de `POST /orders` (la orden nace Sometida),
// traducción de los errores del servidor a campos de la fila y preferencia Rápida/Detallada. Sin React: se prueba en `orderQuickRules.test.ts`.
import { ApiError } from '../../kernel/api/problem'
import { parseNumber } from '../../kernel/format'
import type { OrderCreateRequest } from './api'
import { dayStart, NUMBER_MAX } from './orderRules'

/** Campos de una fila (las claves de los errores). */
export type QuickField = 'clientPublicId' | 'date' | 'orderNumber' | 'invoiceNumber' | 'consigneeId' | 'packageType' | 'cod' | 'serviceType' | 'notes'

export interface QuickRowValues {
  /** Fecha del envío ('YYYY-MM-DD'): el primer campo de la fila. Por omisión hoy; cada fila nueva repite la de la última orden guardada. */
  date: string
  /** Solo cuando el cliente asigna sus números de factura; vacío = lo asigna Teikem. */
  invoiceNumber: string
  /** Solo cuando el cliente asigna sus números de orden; vacío = lo asigna Teikem. */
  orderNumber: string
  /** publicId del consignatario ('' = ninguno). */
  consigneeId: string
  packageType: string
  /** Texto tal cual lo teclea el usuario (con los separadores de la compañía); vacío = sin COD. */
  cod: string
  serviceType: string
  notes: string
}

export const NOTES_MAX = 500

export function emptyQuickRow(defaults: { serviceType?: string; packageType?: string; date?: string } = {}): QuickRowValues {
  return { date: defaults.date ?? '', invoiceNumber: '', orderNumber: '', consigneeId: '', packageType: defaults.packageType ?? '', cod: '', serviceType: defaults.serviceType ?? '', notes: '' }
}

/**
 * La fila siguiente (dictado de Luis, 2026-10-21): **la fecha es la de la última orden guardada** (si alguien puso la de mañana, la fila siguiente sale con la de
 * mañana); el servicio y el paquete salen con los del cliente y, si no tiene, se conservan los de la fila anterior (si no, habría que elegirlos en cada fila);
 * lo demás vuelve a su valor inicial.
 */
export function nextQuickRow(previous: QuickRowValues, defaults: { serviceType?: string; packageType?: string }): QuickRowValues {
  return emptyQuickRow({
    date: previous.date,
    serviceType: defaults.serviceType || previous.serviceType,
    packageType: defaults.packageType || previous.packageType,
  })
}

/** ¿La fecha de la fila es distinta de hoy? (esas filas se sombrean: el usuario no debe pasarlo por alto.) */
export function isOffDate(date: string, today: string): boolean {
  return !!date && date !== today
}

/** Monto COD de la fila: null = vacío; NaN = no es un número. */
export function quickCod(text: string): number | null {
  const trimmed = text.trim()
  if (!trimmed) return null
  const n = parseNumber(trimmed)
  return n == null || Number.isNaN(n) ? Number.NaN : n
}

/** Mensajes de validación (ya traducidos; los del servidor, tal cual el manual 03 §1.1). */
export interface QuickMessages {
  dateRequired: string
  /** La fecha del envío no puede ser anterior a hoy (se avisa y se bloquea). */
  pastDate: string
  clientRequired: string
  consigneeRequired: string
  packageTypeRequired: string
  serviceRequired: string
  codInvalid: string
  codNegative: string
  numberMax: string
}

/** Errores de la fila por campo (vacío = se puede guardar). El orden de las claves es el orden en que se recorren los campos. */
export interface QuickValidationContext {
  orderNumberEditable: boolean
  /** El cliente asigna sus números de factura (si no, el campo está apagado y Teikem la genera). */
  invoiceNumberEditable?: boolean
  /** Hoy de la compañía ('YYYY-MM-DD'). */
  today: string
}

export function validateQuickRow(clientPublicId: string | null, v: QuickRowValues, ctx: QuickValidationContext, m: QuickMessages): Partial<Record<QuickField, string>> {
  const errors: Partial<Record<QuickField, string>> = {}
  if (!clientPublicId) errors.clientPublicId = m.clientRequired
  if (!v.date) errors.date = m.dateRequired
  else if (v.date < ctx.today) errors.date = m.pastDate
  if (ctx.orderNumberEditable && v.orderNumber.trim().length > NUMBER_MAX) errors.orderNumber = m.numberMax
  if (ctx.invoiceNumberEditable && v.invoiceNumber.trim().length > NUMBER_MAX) errors.invoiceNumber = m.numberMax
  if (!v.consigneeId) errors.consigneeId = m.consigneeRequired
  if (!v.packageType) errors.packageType = m.packageTypeRequired
  const cod = quickCod(v.cod)
  if (cod != null && Number.isNaN(cod)) errors.cod = m.codInvalid
  else if (cod != null && cod < 0) errors.cod = m.codNegative
  if (!v.serviceType) errors.serviceType = m.serviceRequired
  return errors
}

/** Request de `POST /orders`: UNA línea de 1 pieza, borrador (sin cotizar) y la fecha elegida en la fila (la lista y el límite, como en el alta detallada). */
export function buildQuickRequest(clientPublicId: string, v: QuickRowValues, o: { orderNumberEditable: boolean; invoiceNumberEditable?: boolean }): OrderCreateRequest {
  const cod = quickCod(v.cod)
  const req: OrderCreateRequest = {
    clientPublicId,
    consigneeLocationPublicId: v.consigneeId,
    serviceType: v.serviceType,
    packages: [{ packageType: v.packageType, pieces: 1 }],
    codAmount: cod != null && !Number.isNaN(cod) && cod > 0 ? cod : null,
    requestedDate: dayStart(v.date),
    promisedDate: dayStart(v.date),
    notes: v.notes.trim() || null,
  }
  if (o.orderNumberEditable && v.orderNumber.trim()) req.orderNumber = v.orderNumber.trim()
  if (o.invoiceNumberEditable && v.invoiceNumber.trim()) req.clientInvoiceNumber = v.invoiceNumber.trim()
  return req
}

export interface QuickProblem {
  /** Texto principal (`title` del ProblemDetails). */
  title: string
  /** Errores del servidor que corresponden a un campo de la fila. */
  fields: Partial<Record<QuickField, string>>
  /** Errores que no son de un campo de la fila (factura, fechas…): se muestran bajo la fila. */
  others: string[]
  /** Falla de red o del servidor (5xx): la orden pudo haberse creado; un reintento con la misma clave no duplica. */
  retryable: boolean
}

/** Nombre de campo del servidor → campo de la fila (`packages[0].packageType` → `packageType`, `consignee` → `consigneeId`…). */
export function quickFieldOf(serverField: string): QuickField | null {
  const bare = serverField.startsWith('$.') ? serverField.slice(2) : serverField
  if (bare === 'consignee' || bare === 'consigneeLocationPublicId' || bare.startsWith('newConsignee')) return 'consigneeId'
  if (/^packages\[\d+\]\.packageType$/.test(bare)) return 'packageType'
  if (bare === 'packages') return 'packageType'
  if (bare === 'serviceType') return 'serviceType'
  if (bare === 'codAmount') return 'cod'
  if (bare === 'orderNumber') return 'orderNumber'
  if (bare === 'requestedDate' || bare === 'promisedDate') return 'date'
  if (bare === 'clientInvoiceNumber') return 'invoiceNumber'
  if (bare === 'clientPublicId') return 'clientPublicId'
  if (bare === 'notes') return 'notes'
  return null
}

/** Separa un error del servidor en el de cada campo de la fila y el resto; los datos de factura repetida no se pintan. */
export function quickProblemOf(err: unknown): QuickProblem {
  if (!(err instanceof ApiError)) return { title: err instanceof Error ? err.message : String(err), fields: {}, others: [], retryable: true }
  const fields: Partial<Record<QuickField, string>> = {}
  const others: string[] = []
  for (const [k, v] of Object.entries(err.errors)) {
    if (k === 'existingOrderNumber' || k === 'existingOrderPublicId') continue
    const field = quickFieldOf(k)
    const text = v.join(' ')
    if (field && !fields[field]) fields[field] = text
    else if (!field && !others.includes(text)) others.push(text)
  }
  const retryable = err.code === 'network' || err.status >= 500
  return { title: err.title, fields, others: others.filter((o) => o !== err.title), retryable }
}

// ---------------------------------------------------------------------------------------------------------------------
// Preferencia Rápida / Detallada
// ---------------------------------------------------------------------------------------------------------------------

export type EntryMode = 'quick' | 'detail'
export const ENTRY_MODE_KEY = 'teikem.orders.entryMode'

/** Última elección guardada; por omisión Rápida. Un localStorage que falla (modo privado, bloqueado) o un valor raro no rompe nada. */
export function readEntryMode(): EntryMode {
  try {
    const v = window.localStorage.getItem(ENTRY_MODE_KEY)
    return v === 'detail' ? 'detail' : 'quick'
  } catch {
    return 'quick'
  }
}

export function saveEntryMode(mode: EntryMode): void {
  try {
    window.localStorage.setItem(ENTRY_MODE_KEY, mode)
  } catch {
    // sin almacenamiento: la elección vale solo para esta pantalla
  }
}
