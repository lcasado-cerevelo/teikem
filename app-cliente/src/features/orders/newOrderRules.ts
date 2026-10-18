// Reglas de la pantalla «Nueva orden» (Lote 105): qué se escribe, cómo se valida antes de mandar y cómo se arma la solicitud. Lo que el servidor vuelve a validar (consignatario, paquetes, notas) se repite aquí
// para avisar sin viaje; el servidor manda.
import type { PortalOrderCreateRequest } from '../portalApi'

export const MAX_PACKAGES = 50
export const MAX_NOTES = 500

export interface PackageDraft {
  description: string
  pieces: string
  weightKg: string
}

export interface NewConsigneeDraft {
  name: string
  line1: string
  line2: string
  city: string
  state: string
  postalCode: string
}

export interface OrderDraft {
  consigneeId: string | null          // un consignatario existente…
  newConsignee: NewConsigneeDraft | null   // …o uno nuevo (nunca los dos)
  pickupId: string | null
  packages: PackageDraft[]
  codAmount: string
  invoiceNumber: string
  requestedDate: string               // AAAA-MM-DD
  notes: string
}

export const emptyPackage = (): PackageDraft => ({ description: '', pieces: '1', weightKg: '' })
export const emptyConsignee = (): NewConsigneeDraft => ({ name: '', line1: '', line2: '', city: '', state: '', postalCode: '' })
export const emptyDraft = (): OrderDraft => ({ consigneeId: null, newConsignee: null, pickupId: null, packages: [emptyPackage()], codAmount: '', invoiceNumber: '', requestedDate: '', notes: '' })

/** Número de un campo escrito a mano (acepta coma decimal); null si está vacío; NaN si no es número. */
export function parseNumber(v: string): number | null {
  const s = v.trim().replace(',', '.')
  if (s === '') return null
  return /^\d+(\.\d+)?$/.test(s) ? Number(s) : Number.NaN
}

export type ErrorKey = 'consignee' | 'packages' | 'pieces' | 'weight' | 'cod' | 'date' | 'notes'

/** Claves de error (se traducen en la pantalla); vacío = se puede mandar. */
export function validateDraft(d: OrderDraft, today = new Date()): ErrorKey[] {
  const errors: ErrorKey[] = []
  const n = d.newConsignee
  if (d.consigneeId === null && (n === null || !n.name.trim() || !n.line1.trim() || !n.city.trim())) errors.push('consignee')
  if (d.packages.length === 0 || d.packages.length > MAX_PACKAGES) errors.push('packages')
  if (d.packages.some((p) => !Number.isInteger(parseNumber(p.pieces)) || (parseNumber(p.pieces) ?? 0) < 1)) errors.push('pieces')
  if (d.packages.some((p) => Number.isNaN(parseNumber(p.weightKg)))) errors.push('weight')
  const cod = parseNumber(d.codAmount)
  if (Number.isNaN(cod)) errors.push('cod')
  if (d.requestedDate.trim() !== '') {
    const ok = /^\d{4}-\d{2}-\d{2}$/.test(d.requestedDate.trim()) && !Number.isNaN(Date.parse(d.requestedDate.trim()))
    const day = new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime()
    if (!ok || Date.parse(d.requestedDate.trim()) < day - 24 * 3600 * 1000) errors.push('date')   // no se aceptan fechas ya pasadas (con un día de margen por la zona horaria)
  }
  if (d.notes.trim().length > MAX_NOTES) errors.push('notes')
  return errors
}

/** La solicitud al servidor (ya validada). El cliente NO se manda: sale de la sesión. */
export function buildRequest(d: OrderDraft, typesInvoice: boolean): PortalOrderCreateRequest {
  const n = d.newConsignee
  const cod = parseNumber(d.codAmount)
  return {
    consigneePublicId: d.consigneeId,
    newConsignee: d.consigneeId === null && n ? { name: n.name.trim(), line1: n.line1.trim(), line2: n.line2.trim() || null, city: n.city.trim(), state: n.state.trim() || null, postalCode: n.postalCode.trim() || null } : undefined,
    pickupPublicId: d.pickupId,
    packages: d.packages.map((p) => ({ description: p.description.trim() || null, pieces: parseNumber(p.pieces) ?? 1, weightKg: parseNumber(p.weightKg) })),
    codAmount: cod && cod > 0 ? cod : null,
    clientInvoiceNumber: typesInvoice && d.invoiceNumber.trim() ? d.invoiceNumber.trim() : null,
    requestedDate: d.requestedDate.trim() ? `${d.requestedDate.trim()}T00:00:00Z` : null,
    notes: d.notes.trim() || null,
  }
}
