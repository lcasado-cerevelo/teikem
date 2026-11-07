// Lote 225 (P5) — reglas puras de la pestaña QuickBooks y de Ajustes → Operación → QuickBooks (sin React). Espejo de los límites de `QuickBooksRules` (C#):
// cuentas hasta 159 caracteres, ítems y términos hasta 31; marcar/confirmar hasta 1000 facturas por llamada.
import type { components } from '../../kernel/api/schema'
import { matchesQ } from '../../kernel/ui/matchesQ'

type PendingInvoice = components['schemas']['QuickBooksPendingInvoiceDto']
type PendingDto = components['schemas']['QuickBooksPendingDto']
type NewClient = components['schemas']['QuickBooksNewClientDto']
type NewProduct = components['schemas']['QuickBooksNewProductDto']
type SettingsDto = components['schemas']['QuickBooksSettingsDto']
type SettingsRequest = components['schemas']['QuickBooksSettingsRequest']

/** Tope del servidor por llamada a mark/confirm. */
export const MARK_BATCH = 1000
export const QB_ACCOUNT_MAX = 159
export const QB_ITEM_MAX = 31
export const QB_TERM_MAX = 31

/** Parte una lista de facturas en tandas de `MARK_BATCH` (sin repetidas, en el orden recibido). */
export function markBatches(ids: readonly string[], size = MARK_BATCH): string[][] {
  const unique = [...new Set(ids.filter(Boolean))]
  const out: string[][] = []
  for (let i = 0; i < unique.length; i += size) out.push(unique.slice(i, i + size))
  return out
}

/** Base64 del servidor → bytes (el archivo IIF va en Windows-1252: no se pasa por texto para no cambiar los acentos). */
export function base64ToBytes(b64: string): Uint8Array {
  const bin = atob(b64.replace(/\s+/g, ''))
  const bytes = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
  return bytes
}

/** Baja el archivo del servidor tal cual (bytes exactos) con su nombre. */
export function downloadBase64File(b64: string, fileName: string, contentType?: string | null): void {
  const bytes = base64ToBytes(b64)
  const blob = new Blob([bytes as BlobPart], { type: contentType || 'application/octet-stream' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

/** Buscador libre de la lista: número, cliente (nombre, número o nombre en QuickBooks) y órdenes. */
export function filterPending(rows: readonly PendingInvoice[], q: string): PendingInvoice[] {
  if (!q.trim()) return [...rows]
  return rows.filter((r) => matchesQ(q, r.number, r.clientName, r.clientCode, r.quickBooksCustomer, ...(r.orderNumbers ?? [])))
}

/** Lo nuevo de una factura para «ver datos»: su cliente (si es nuevo) y sus productos nuevos (por SKU). */
export function newDataOf(invoice: PendingInvoice, pending: PendingDto | undefined): { client: NewClient | null; products: NewProduct[] } {
  const client = invoice.newClient ? (pending?.newClients ?? []).find((c) => c.publicId === invoice.clientPublicId) ?? null : null
  const skus = new Set((invoice.newProductSkus ?? []).map((s) => s.toUpperCase()))
  const products = (pending?.newProducts ?? []).filter((p) => skus.has((p.sku ?? '').toUpperCase()))
  return { client, products }
}

/** ¿Trae algo nuevo para QuickBooks? */
export function hasNewData(invoice: PendingInvoice): boolean {
  return !!invoice.newClient || (invoice.newProductSkus ?? []).length > 0
}

// ---------- Ajustes → Operación → QuickBooks ----------

export const QB_ACCOUNT_FIELDS = ['receivableAccount', 'salesAccount', 'costAccount', 'inventoryAccount', 'taxAccount'] as const
export const QB_ITEM_FIELDS = ['taxItem', 'shippingItem'] as const
export type QbTextField = (typeof QB_ACCOUNT_FIELDS)[number] | (typeof QB_ITEM_FIELDS)[number]

export interface QuickBooksSettingsDraft {
  fields: Record<QbTextField, string>
  /** código del término de pago → nombre en QuickBooks ('' = sin nombre). */
  terms: Record<string, string>
}

const ALL_FIELDS: readonly QbTextField[] = [...QB_ACCOUNT_FIELDS, ...QB_ITEM_FIELDS]

export function quickBooksDraftOf(dto: SettingsDto | undefined): QuickBooksSettingsDraft {
  const fields = Object.fromEntries(ALL_FIELDS.map((k) => [k, dto?.[k] ?? ''])) as Record<QbTextField, string>
  const terms = Object.fromEntries((dto?.terms ?? []).filter((x) => x.code).map((x) => [x.code!, x.quickBooksName ?? '']))
  return { fields, terms }
}

export function maxLengthOf(field: QbTextField): number {
  return (QB_ACCOUNT_FIELDS as readonly string[]).includes(field) ? QB_ACCOUNT_MAX : QB_ITEM_MAX
}

/** Primer problema del borrador (la clave de su mensaje) o null. Los mismos límites que el servidor. */
export function quickBooksDraftError(d: QuickBooksSettingsDraft): 'account' | 'item' | 'term' | null {
  if (QB_ACCOUNT_FIELDS.some((k) => d.fields[k].trim().length > QB_ACCOUNT_MAX)) return 'account'
  if (QB_ITEM_FIELDS.some((k) => d.fields[k].trim().length > QB_ITEM_MAX)) return 'item'
  if (Object.values(d.terms).some((v) => v.trim().length > QB_TERM_MAX)) return 'term'
  return null
}

/**
 * Cuerpo del PATCH con SOLO lo que cambió (null = sin cambio para el servidor). Un campo vaciado va como '' (= el valor por omisión de QuickBooks);
 * un término vaciado va como '' (= se quita del mapa: esas facturas salen sin término). Sin cambios → null.
 */
export function quickBooksSettingsRequest(draft: QuickBooksSettingsDraft, original: QuickBooksSettingsDraft): SettingsRequest | null {
  const body: SettingsRequest = {}
  let changed = false
  for (const k of ALL_FIELDS) {
    const v = draft.fields[k].trim()
    if (v !== original.fields[k].trim()) {
      body[k] = v
      changed = true
    }
  }
  const terms: Record<string, string> = {}
  for (const [code, name] of Object.entries(draft.terms)) {
    const v = name.trim()
    if (v !== (original.terms[code] ?? '').trim()) terms[code] = v
  }
  if (Object.keys(terms).length > 0) {
    body.terms = terms
    changed = true
  }
  return changed ? body : null
}
