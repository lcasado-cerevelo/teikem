// Reglas de la pantalla «Nuevo envío» del portal: qué se escribe, cómo se valida antes de mandar y cómo se arma la solicitud. 2026-10-23 (dictado de Luis): los MISMOS campos que
// «Nuevo envío» de la web del personal: tipo de servicio, fecha lista y fecha límite, N.º de orden (si el cliente los asigna), N.º de factura (si los asigna), consignatario (uno
// de los suyos o uno nuevo), recogida, paquetes con tipo de paquete, descripción, piezas y peso, COD y notas. Lo que el servidor vuelve a validar se repite aquí para avisar sin
// viaje; el servidor manda. Sin React: se prueba en NewOrderScreen.test.tsx.
import type { PortalOrderCreateRequest, PortalOrderFormDto } from '../portalApi'

export const MAX_PACKAGES = 50
export const MAX_NOTES = 500
export const NUMBER_MAX = 40

export interface PackageDraft {
  packageType: string
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
  /** Opcional; solo se usa al crear el consignatario en el servidor (no viaja en la orden). */
  phone: string
}

export interface OrderDraft {
  serviceType: string
  /** Fecha lista (la pedida), AAAA-MM-DD. */
  readyDate: string
  /** Fecha límite (la prometida), AAAA-MM-DD. */
  dueDate: string
  orderNumber: string
  invoiceNumber: string
  consigneeId: string | null          // un consignatario existente…
  newConsignee: NewConsigneeDraft | null   // …o uno nuevo (nunca los dos)
  pickupId: string | null
  packages: PackageDraft[]
  codAmount: string
  notes: string
}

/** Lo que el formulario del servidor dice del cliente: qué numera él y los predeterminados de servicio y paquete. */
export interface FormRules {
  typesInvoice: boolean
  assignsOrderNumber: boolean
  defaultServiceType: string
  defaultPackageType: string
  /** Si la compañía no ofrece catálogo (lista vacía), el servidor usa su predeterminado y aquí no se exige. */
  hasServiceTypes: boolean
  hasPackageTypes: boolean
}

export function rulesOf(form: PortalOrderFormDto | null | undefined): FormRules {
  return {
    typesInvoice: !!form?.clientTypesInvoiceNumber,
    assignsOrderNumber: !!form?.clientAssignsOrderNumber,
    defaultServiceType: form?.defaultServiceType ?? '',
    defaultPackageType: form?.defaultPackageType ?? '',
    hasServiceTypes: (form?.serviceTypes ?? []).length > 0,
    hasPackageTypes: (form?.packageTypes ?? []).length > 0,
  }
}

/** Fecha de hoy (AAAA-MM-DD) en la zona de la persona. */
export function todayText(now = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`
}

export const emptyPackage = (packageType = ''): PackageDraft => ({ packageType, description: '', pieces: '1', weightKg: '' })
export const emptyConsignee = (): NewConsigneeDraft => ({ name: '', line1: '', line2: '', city: '', state: '', postalCode: '', phone: '' })
/** El borrador inicial: fechas de hoy y los predeterminados del cliente (como en la web del personal). */
export const emptyDraft = (defaults: { serviceType?: string; packageType?: string; today?: string } = {}): OrderDraft => ({
  serviceType: defaults.serviceType ?? '',
  readyDate: defaults.today ?? todayText(),
  dueDate: defaults.today ?? todayText(),
  orderNumber: '',
  invoiceNumber: '',
  consigneeId: null,
  newConsignee: null,
  pickupId: null,
  packages: [emptyPackage(defaults.packageType ?? '')],
  codAmount: '',
  notes: '',
})

/** Número de un campo escrito a mano (acepta coma decimal); null si está vacío; NaN si no es número. */
export function parseNumber(v: string): number | null {
  const s = v.trim().replace(',', '.')
  if (s === '') return null
  return /^\d+(\.\d+)?$/.test(s) ? Number(s) : Number.NaN
}

export type ErrorKey = 'service' | 'consignee' | 'packages' | 'packageType' | 'pieces' | 'weight' | 'cod' | 'date' | 'dueDate' | 'orderNumber' | 'invoice' | 'notes'

const isDay = (v: string) => /^\d{4}-\d{2}-\d{2}$/.test(v) && !Number.isNaN(Date.parse(`${v}T00:00:00Z`))

/** Claves de error (se traducen en la pantalla); vacío = se puede mandar. `rules` dice qué exige el cliente. */
export function validateDraft(d: OrderDraft, rules: Partial<FormRules> = {}, today = new Date()): ErrorKey[] {
  const errors: ErrorKey[] = []
  const n = d.newConsignee
  if (rules.hasServiceTypes && !d.serviceType.trim()) errors.push('service')
  if (d.consigneeId === null && (n === null || !n.name.trim() || !n.line1.trim() || !n.city.trim())) errors.push('consignee')
  if (d.packages.length === 0 || d.packages.length > MAX_PACKAGES) errors.push('packages')
  if (rules.hasPackageTypes && d.packages.some((p) => !p.packageType.trim())) errors.push('packageType')
  if (d.packages.some((p) => !Number.isInteger(parseNumber(p.pieces)) || (parseNumber(p.pieces) ?? 0) < 1)) errors.push('pieces')
  if (d.packages.some((p) => Number.isNaN(parseNumber(p.weightKg)))) errors.push('weight')
  const cod = parseNumber(d.codAmount)
  if (Number.isNaN(cod)) errors.push('cod')
  const todayDay = todayText(today)
  const ready = d.readyDate.trim()
  if (ready !== '' && (!isDay(ready) || ready < todayDay)) errors.push('date')   // no se aceptan fechas ya pasadas
  const due = d.dueDate.trim()
  if (due !== '' && (!isDay(due) || (isDay(ready) && due < ready))) errors.push('dueDate')
  if (rules.assignsOrderNumber && d.orderNumber.trim().length > NUMBER_MAX) errors.push('orderNumber')
  if (rules.typesInvoice && d.invoiceNumber.trim().length > NUMBER_MAX) errors.push('invoice')
  if (d.notes.trim().length > MAX_NOTES) errors.push('notes')
  return errors
}

/** La solicitud al servidor (ya validada). El cliente NO se manda: sale de la sesión. Lo que el cliente no numera no se manda. */
export function buildRequest(d: OrderDraft, rules: Partial<FormRules> = {}): PortalOrderCreateRequest {
  const n = d.newConsignee
  const cod = parseNumber(d.codAmount)
  const day = (v: string) => (isDay(v.trim()) ? `${v.trim()}T00:00:00Z` : null)
  return {
    consigneePublicId: d.consigneeId,
    newConsignee: d.consigneeId === null && n ? { name: n.name.trim(), line1: n.line1.trim(), line2: n.line2.trim() || null, city: n.city.trim(), state: n.state.trim() || null, postalCode: n.postalCode.trim() || null } : undefined,
    pickupPublicId: d.pickupId,
    serviceType: d.serviceType.trim() || null,
    packages: d.packages.map((p) => ({ packageType: p.packageType.trim() || null, description: p.description.trim() || null, pieces: parseNumber(p.pieces) ?? 1, weightKg: parseNumber(p.weightKg) })),
    codAmount: cod && cod > 0 ? cod : null,
    orderNumber: rules.assignsOrderNumber && d.orderNumber.trim() ? d.orderNumber.trim() : null,
    clientInvoiceNumber: rules.typesInvoice && d.invoiceNumber.trim() ? d.invoiceNumber.trim() : null,
    requestedDate: day(d.readyDate),
    promisedDate: day(d.dueDate),
    notes: d.notes.trim() || null,
  }
}

/** Resumen de los paquetes para el ticket: «2 × Caja · 1 × Sobre» (por tipo, con las piezas sumadas). */
export function packageSummary(packages: readonly PackageDraft[], labelOf: (code: string) => string): string {
  const byType = new Map<string, number>()
  for (const p of packages) {
    const pieces = parseNumber(p.pieces)
    const n = pieces !== null && !Number.isNaN(pieces) ? pieces : 0
    const key = p.packageType || p.description || ''
    byType.set(key, (byType.get(key) ?? 0) + n)
  }
  return [...byType.entries()].map(([k, n]) => `${n} × ${labelOf(k) || k || '—'}`).join(' · ')
}

/** Campo de la pantalla al que lleva un error del servidor (`errors` del ProblemDetails): consignee / newConsignee.* → consignatario, packages[i].packageType → tipo de paquete… */
export function serverFieldKey(field: string): ErrorKey | null {
  const bare = field.startsWith('$.') ? field.slice(2) : field
  if (bare === 'consignee' || bare.startsWith('newConsignee')) return 'consignee'
  if (bare === 'serviceType') return 'service'
  if (/^packages\[\d+\]\.packageType$/.test(bare)) return 'packageType'
  if (/^packages(\[\d+\]\.pieces)?$/.test(bare)) return bare === 'packages' ? 'packages' : 'pieces'
  if (bare === 'orderNumber') return 'orderNumber'
  if (bare === 'clientInvoiceNumber') return 'invoice'
  if (bare === 'requestedDate') return 'date'
  if (bare === 'promisedDate') return 'dueDate'
  if (bare === 'codAmount') return 'cod'
  if (bare === 'notes') return 'notes'
  return null
}
