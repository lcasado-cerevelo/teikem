// Lote 249 (Lote E) — reglas puras de la pantalla «Modificar la orden»: el borrador de cambios, las operaciones que viajan al servidor, la validación de la
// autorización del cliente y la traducción de los textos que el servidor genera en español (bloqueos, motivos de las recolecciones).
import { parseNumber } from '../salesRules'
import type { AdditionRequest, ChangeLineOp, ChangeLinePositionDto, ChangeRequest, ChangeTermsRequest } from './changesApi'

/** Los canales con que el cliente autorizó (catálogo SalesChangeChannel). */
export const CHANNELS = ['PHONE', 'EMAIL', 'IN_PERSON', 'MESSAGE'] as const
export type Channel = (typeof CHANNELS)[number]

export const AUTH_NAME_MAX = 150
export const AUTH_TITLE_MAX = 100
export const REASON_MAX = 500
export const TAX_NOTE_MAX = 250

// ---------------------------------------------------------------- borrador de cambios

/** Lo que se escribió sobre una línea existente (texto vacío o `undefined` = sin cambio). */
export interface LineEdit {
  quantity?: string
  price?: string
  /** IVU manual de la línea en % ('' = sin cambio); con `taxNote` obligatorio. */
  taxPct?: string
  taxNote?: string
  /** Quitar el IVU manual (la línea vuelve a su cálculo normal con la exención del cliente). */
  clearTax?: boolean
  removed?: boolean
}

/** Un producto que se agrega a la orden. */
export interface AddedLine {
  key: number
  productPublicId: string
  sku: string
  name: string
  listPrice: number | null
  quantity: string
  /** Vacío = el precio de hoy; distinto del de lista exige sales.price. */
  price: string
}

export interface TermsDraft {
  paymentMethod?: string
  paymentTerm?: string
  deliveryMode?: string
  consigneePublicId?: string
  notes?: string
}

export interface ChangeDraft {
  edits: Record<number, LineEdit>
  added: AddedLine[]
  terms: TermsDraft
}

export const EMPTY_DRAFT: ChangeDraft = { edits: {}, added: [], terms: {} }

/** La posición de una línea con lo que hoy dice la orden (para saber si algo cambió de verdad). */
export interface BaseTerms {
  paymentMethod?: string | null
  paymentTerm?: string | null
  deliveryMode?: string | null
  deliveryModeSet?: boolean
  consigneePublicId?: string | null
  notes?: string | null
}

const num = (text: string | undefined) => (text === undefined || text.trim() === '' ? null : parseNumber(text))

/** Lo que cambió en una línea existente, como operaciones del servidor. */
export function opsOfLine(pos: ChangeLinePositionDto, edit: LineEdit | undefined): ChangeLineOp[] {
  if (!edit) return []
  const id = pos.lineId
  if (edit.removed) return [{ kind: 'REMOVE', lineId: id }]
  const ops: ChangeLineOp[] = []
  const q = num(edit.quantity)
  if (q !== null && q !== pos.qtyOrdered) ops.push({ kind: 'QTY', lineId: id, quantity: q })
  const p = num(edit.price)
  if (p !== null && p !== pos.unitPrice) ops.push({ kind: 'PRICE', lineId: id, unitPrice: p })
  if (edit.clearTax) ops.push({ kind: 'CLEAR_TAX', lineId: id })
  else if ((edit.taxPct ?? '').trim() !== '') {
    const pct = num(edit.taxPct)
    ops.push({ kind: 'TAX', lineId: id, taxPct: pct ?? undefined, note: (edit.taxNote ?? '').trim() })
  }
  return ops
}

/** Las operaciones de todo el borrador: cambios por línea y productos agregados. */
export function buildOps(draft: ChangeDraft, positions: readonly ChangeLinePositionDto[]): ChangeLineOp[] {
  const ops: ChangeLineOp[] = []
  for (const pos of positions) ops.push(...opsOfLine(pos, draft.edits[pos.lineId]))
  for (const a of draft.added) {
    const q = num(a.quantity)
    const p = num(a.price)
    ops.push({ kind: 'ADD', productPublicId: a.productPublicId, quantity: q ?? undefined, ...(p !== null ? { unitPrice: p } : {}) })
  }
  return ops
}

/** Los términos que cambiaron respecto de la orden (undefined = no cambia ninguno). Las notas van aparte. */
export function buildTerms(terms: TermsDraft, base: BaseTerms): ChangeTermsRequest | undefined {
  const out: ChangeTermsRequest = {}
  let any = false
  if (terms.paymentMethod !== undefined && terms.paymentMethod !== (base.paymentMethod ?? '')) {
    out.paymentMethod = terms.paymentMethod
    // sin crédito no hay término: se limpia
    out.paymentTerm = terms.paymentMethod === 'CREDIT' ? (terms.paymentTerm ?? '') : ''
    if (terms.paymentMethod === 'CONSIGNMENT') out.isConsignment = true
    else if (base.paymentMethod === 'CONSIGNMENT') out.isConsignment = false
    any = true
  } else if (terms.paymentTerm !== undefined && terms.paymentTerm !== (base.paymentTerm ?? '')) {
    out.paymentTerm = terms.paymentTerm
    any = true
  }
  if (terms.deliveryMode !== undefined && terms.deliveryMode !== (base.deliveryModeSet ? base.deliveryMode ?? '' : '')) {
    out.deliveryMode = terms.deliveryMode
    any = true
  }
  if (terms.consigneePublicId !== undefined && terms.consigneePublicId !== (base.consigneePublicId ?? '')) {
    if (terms.consigneePublicId === '') out.clearConsignee = true
    else out.consigneePublicId = terms.consigneePublicId
    any = true
  }
  return any ? out : undefined
}

export interface AuthDraft {
  name: string
  title: string
  contactId: string
  channel: string
  /** «YYYY-MM-DDTHH:mm» en la hora de la compañía. */
  at: string
  reason: string
  attachmentId: string | null
}

export const EMPTY_AUTH: AuthDraft = { name: '', title: '', contactId: '', channel: '', at: '', reason: '', attachmentId: null }

export interface ChangeFlags {
  reopenPack: boolean
  allowBackorder: boolean
  overrideCredit: boolean
  rowVersion?: string | null
}

/** La solicitud completa de una modificación (vista previa y registro). `atUtc` ya viene convertida a UTC (null = ahora). */
export function buildChangeRequest(
  draft: ChangeDraft,
  positions: readonly ChangeLinePositionDto[],
  base: BaseTerms,
  auth: AuthDraft,
  atUtc: string | null,
  flags: ChangeFlags,
): ChangeRequest {
  const terms = buildTerms(draft.terms, base)
  const notes = draft.terms.notes !== undefined && draft.terms.notes !== (base.notes ?? '') ? draft.terms.notes : undefined
  return {
    ops: buildOps(draft, positions),
    terms,
    notes,
    authorizedByName: auth.name.trim() || undefined,
    authorizedByTitle: auth.title.trim() || undefined,
    authorizedByContactId: auth.contactId ? Number(auth.contactId) : undefined,
    authorizationChannel: auth.channel || undefined,
    authorizedAtUtc: atUtc ?? undefined,
    reason: auth.reason.trim() || undefined,
    attachmentFilePublicId: auth.attachmentId ?? undefined,
    reopenPack: flags.reopenPack || undefined,
    allowBackorder: flags.allowBackorder || undefined,
    overrideCredit: flags.overrideCredit || undefined,
    rowVersion: flags.rowVersion ?? undefined,
  }
}

/** La orden interna de adición: las líneas nuevas y la misma autorización del cliente. */
export function buildAdditionRequest(added: readonly AddedLine[], notes: string, auth: AuthDraft, atUtc: string | null, flags: { allowBackorder: boolean; overrideCredit: boolean }): AdditionRequest {
  return {
    lines: added.map((a) => {
      const p = num(a.price)
      return { productPublicId: a.productPublicId, quantity: num(a.quantity) ?? undefined, ...(p !== null ? { unitPrice: p } : {}) }
    }),
    notes: notes.trim() || undefined,
    authorizedByName: auth.name.trim() || undefined,
    authorizedByTitle: auth.title.trim() || undefined,
    authorizedByContactId: auth.contactId ? Number(auth.contactId) : undefined,
    authorizationChannel: auth.channel || undefined,
    authorizedAtUtc: atUtc ?? undefined,
    reason: auth.reason.trim() || undefined,
    attachmentFilePublicId: auth.attachmentId ?? undefined,
    allowBackorder: flags.allowBackorder || undefined,
    overrideCredit: flags.overrideCredit || undefined,
  }
}

/** ¿El borrador cambia algo? (operaciones, términos o notas) */
export function hasChanges(draft: ChangeDraft, positions: readonly ChangeLinePositionDto[], base: BaseTerms): boolean {
  return buildOps(draft, positions).length > 0 || buildTerms(draft.terms, base) !== undefined || (draft.terms.notes !== undefined && draft.terms.notes !== (base.notes ?? ''))
}

// ---------------------------------------------------------------- validación en pantalla (el servidor repite estas reglas)

export type LineErrorKey = 'quantity' | 'price' | 'taxPct' | 'taxNote' | 'belowMin'

/** El primer problema de lo escrito en una línea existente o null. `reopen` = se va a reabrir el empaque (el mínimo es solo lo salido). */
export function lineError(pos: ChangeLinePositionDto, edit: LineEdit | undefined, reopen: boolean): LineErrorKey | null {
  if (!edit || edit.removed) return null
  if (edit.quantity !== undefined && edit.quantity.trim() !== '') {
    const q = parseNumber(edit.quantity)
    if (q === null || q <= 0) return 'quantity'
    const min = reopen ? pos.minQuantityWithReopen : pos.minQuantity
    if (q < (min ?? 0)) return 'belowMin'
  }
  if (edit.price !== undefined && edit.price.trim() !== '' && parseNumber(edit.price) === null) return 'price'
  if (!edit.clearTax && edit.taxPct !== undefined && edit.taxPct.trim() !== '') {
    const pct = parseNumber(edit.taxPct)
    if (pct === null || pct > 100) return 'taxPct'
    if ((edit.taxNote ?? '').trim() === '') return 'taxNote'
  }
  return null
}

export type AuthErrorKey = 'name' | 'channel' | 'at' | 'reason' | 'attachment'

/** Lo que falta de la autorización del cliente (en el orden de la pantalla) o null. */
export function authError(auth: AuthDraft, attachmentRequired: boolean): AuthErrorKey | null {
  if (auth.name.trim() === '') return 'name'
  if (auth.channel === '') return 'channel'
  if (auth.at.trim() === '') return 'at'
  if (auth.reason.trim() === '') return 'reason'
  if (attachmentRequired && !auth.attachmentId) return 'attachment'
  return null
}

/** Lo mínimo que puede quedar en una línea: sin reabrir el empaque o reabriéndolo. */
export function minQuantity(pos: ChangeLinePositionDto, reopen: boolean): number {
  return (reopen ? pos.minQuantityWithReopen : pos.minQuantity) ?? 0
}

/** La línea no admite ningún cambio. */
export const isLineLocked = (pos: ChangeLinePositionDto) => Boolean(pos.lockedReason) || pos.isRemoved === true

// ---------------------------------------------------------------- textos del servidor en español → idioma de la interfaz

type Translate = (key: string, params?: Record<string, string | number>) => string

interface Pattern {
  re: RegExp
  key: string
  /** Nombre de cada grupo capturado, en orden. */
  params?: readonly string[]
}

const PATTERNS: readonly Pattern[] = [
  { re: /^La orden ya está facturada: solo se le puede añadir\./, key: 'salesChanges.blockers.billed' },
  { re: /^La orden está en borrador/, key: 'salesChanges.blockers.draft' },
  { re: /^La orden está cancelada/, key: 'salesChanges.blockers.cancelled' },
  { re: /^La orden ya salió completa/, key: 'salesChanges.blockers.leftComplete' },
  { re: /^Indique al menos un cambio\./, key: 'salesChanges.blockers.nothing' },
  { re: /^No se pueden quitar todas las líneas/, key: 'salesChanges.blockers.removeAll' },
  { re: /^Ya salió parte de la orden: el pago/, key: 'salesChanges.blockers.termsLeft' },
  { re: /^La línea (\d+) \((.+)\) está quitada/, key: 'salesChanges.blockers.lineRemoved', params: ['n', 'sku'] },
  { re: /^La línea (\d+) \((.+)\) está cerrada/, key: 'salesChanges.blockers.lineClosed', params: ['n', 'sku'] },
  { re: /^El producto (.+) ya está en la orden/, key: 'salesChanges.blockers.productRepeated', params: ['sku'] },
  { re: /^No se puede bajar (.+) a ([\d.]+): ya salieron ([\d.]+)\./, key: 'salesChanges.blockers.belowLeft', params: ['sku', 'qty', 'left'] },
  { re: /^No se puede quitar (.+): ya salieron ([\d.]+)\./, key: 'salesChanges.blockers.removeLeft', params: ['sku', 'left'] },
  { re: /^El precio de (.+) ya no se cambia/, key: 'salesChanges.blockers.priceLeft', params: ['sku'] },
  { re: /^El IVU de (.+) ya no se cambia/, key: 'salesChanges.blockers.taxLeft', params: ['sku'] },
  { re: /^La línea (\d+) \((.+)\) no tiene IVU manual/, key: 'salesChanges.blockers.noTaxOverride', params: ['n', 'sku'] },
  { re: /^(.+) está en el empaque (.+): para cambiarla marque/, key: 'salesChanges.blockers.lineNeedsReopen', params: ['sku', 'pack'] },
  { re: /^La orden está en el empaque (.+): para cambiar el pago/, key: 'salesChanges.blockers.termsNeedReopen', params: ['pack'] },
  { re: /^La recolección (.+) ya está facturada \((.+)\): no se puede deshacer\./, key: 'salesChanges.blockers.batchInvoiced', params: ['batch', 'invoices'] },
  { re: /^Es un recogido/, key: 'salesChanges.reasons.pickup' },
  { re: /^La cuenta una compañía enlazada/, key: 'salesChanges.reasons.linked' },
  { re: /^Es un despacho manual/, key: 'salesChanges.reasons.manual' },
  { re: /^El empaque no tiene envío/, key: 'salesChanges.reasons.noShipment' },
  { re: /^Tiene productos ya facturados/, key: 'salesChanges.reasons.invoiced' },
  { re: /^El envío (.+) está anulado/, key: 'salesChanges.reasons.shipmentInactive', params: ['shipment'] },
  { re: /^El envío (.+) ya está «(.+)»\./, key: 'salesChanges.reasons.shipmentStatus', params: ['shipment', 'status'] },
  { re: /^El envío (.+) ya pasó de su etapa inicial/, key: 'salesChanges.reasons.shipmentAdvanced', params: ['shipment'] },
  { re: /^El flete del envío (.+) ya está facturado/, key: 'salesChanges.reasons.freightInvoiced', params: ['shipment'] },
  { re: /^La recolección está «(.+)»/, key: 'salesChanges.reasons.status', params: ['status'] },
  { re: /^Adjunte la evidencia de la autorización/, key: 'salesChanges.blockers.attachmentRequired' },
  { re: /^La orden cambió mientras se modificaba/, key: 'salesChanges.blockers.changed' },
  { re: /^Ya hay una modificación pendiente/, key: 'salesChanges.blockers.pendingExists' },
]

/**
 * El texto de un bloqueo o motivo que generó el servidor, en el idioma de la interfaz. En español se deja tal cual; en otro idioma se reconoce el mensaje conocido
 * y se arma con sus datos; un mensaje que no se reconoce queda como lo mandó el servidor (nunca se inventa).
 */
export function serverText(t: Translate, lang: string, text: string | null | undefined): string {
  if (!text) return ''
  if (lang === 'es') return text
  for (const p of PATTERNS) {
    const m = p.re.exec(text)
    if (!m) continue
    const params: Record<string, string> = {}
    ;(p.params ?? []).forEach((name, i) => {
      params[name] = m[i + 1] ?? ''
    })
    return t(p.key, params)
  }
  return text
}

/** Por qué pide segunda aprobación: sale de la regla (ALWAYS | ABOVE_AMOUNT) y el monto de los ajustes vigentes, no del texto del servidor. */
export function approvalRuleKey(rule: string | null | undefined): 'always' | 'aboveAmount' | null {
  if (rule === 'ALWAYS') return 'always'
  if (rule === 'ABOVE_AMOUNT') return 'aboveAmount'
  return null
}

/** El nombre del campo de una diferencia (qtyOrdered, unitPrice…); un campo desconocido se muestra tal cual. */
export const KNOWN_FIELDS = ['qtyOrdered', 'unitPrice', 'taxRatePct', 'taxAmount', 'taxReason', 'paymentMethod', 'paymentTerm', 'deliveryMode', 'consignee', 'isConsignment', 'notes'] as const

export type FieldKind = 'money' | 'number' | 'percent' | 'text'

export function fieldKind(field: string): FieldKind {
  if (field === 'unitPrice' || field === 'taxAmount') return 'money'
  if (field === 'qtyOrdered') return 'number'
  if (field === 'taxRatePct') return 'percent'
  return 'text'
}

/** Un valor numérico de la diferencia llega como texto invariante («12.5»); null si no lo es. */
export function numericValue(text: string | null | undefined): number | null {
  if (text === null || text === undefined || text.trim() === '') return null
  const n = Number(text)
  return Number.isFinite(n) ? n : null
}
