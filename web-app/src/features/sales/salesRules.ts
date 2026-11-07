// Reglas puras de la pantalla de órdenes de venta (Lote 60): cantidades y precios escritos a mano, totales del borrador y su validación.

/** «2,5» o «2.5» → 2.5. Vacío o no numérico → null. */
export function parseNumber(text: string): number | null {
  const clean = text.trim().replace(',', '.')
  if (clean === '' || !/^\d+(\.\d+)?$/.test(clean)) return null
  return Number(clean)
}

export function decimalsOf(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

export interface LineDraft {
  /** Clave estable de la fila en pantalla (no se envía). */
  key: number
  productPublicId: string | null
  sku: string
  name: string
  /** Precio de venta del producto (el que se usa si no se escribe otro). */
  listPrice: number | null
  quantity: string
  /** Vacío = el precio de lista. */
  price: string
  /** Lote 69: la línea es de un kit (se pide en piezas enteras; sus componentes se ven debajo). */
  isKit: boolean
  /** Lote 69: piezas ajustadas al pedir (solo kits ajustables); null = las del catálogo / las ya guardadas en la orden. */
  components: KitPiece[] | null
}

export interface KitPiece {
  productPublicId: string
  sku: string
  name: string
  /** Cantidad por kit escrita a mano. */
  quantity: string
}

export interface OrderDraft {
  clientPublicId: string | null
  warehousePublicId: string | null
  requiredDate: string
  notes: string
  lines: LineDraft[]
  /** Lote 222: pedido urgente (el almacén lo ve primero; la entrega nace con prioridad alta). */
  isUrgent: boolean
  /** Lote 222: '' = el servidor lo deduce del consignatario (con consignatario = entrega). */
  deliveryMode: DeliveryMode
  /** Lote 222: forma de pago de la orden (SalesPaymentMethod; '' = sin escoger) y, solo con crédito, su término. */
  paymentMethod: string
  paymentTerm: string
  /** Lote 222: venta en consignación (solo con la forma de pago Consignación). */
  isConsignment: boolean
  /** Lote 222: nombre de quien autoriza el pedido en el negocio del cliente. */
  authorizedByName: string
}

export type DeliveryMode = '' | 'DELIVERY' | 'PICKUP'
export const DELIVERY_MODES = ['DELIVERY', 'PICKUP'] as const
export const AUTHORIZED_BY_MAX = 150

export const PAYMENT_METHODS = ['COD_CHECK', 'COD_CASH', 'COD_ACH', 'CREDIT', 'CONSIGNMENT'] as const

/** Un borrador sin los datos de Lote 222 (valores neutros). */
export const EXTRA_DEFAULTS = { isUrgent: false, deliveryMode: '' as DeliveryMode, paymentMethod: '', paymentTerm: '', isConsignment: false, authorizedByName: '' }

/** ¿El término es de crédito? NET15, NET30… o PK_BY_REP («cobra el representante»). Espejo de `SalesPaymentRules.IsCreditTerm`. */
export function isCreditTerm(term: string | null | undefined): boolean {
  const t = (term ?? '').trim().toUpperCase()
  return t === 'PK_BY_REP' || /^NET\d+$/.test(t)
}

/** Forma y término que se prellenan con el término de pago del cliente (espejo de `SalesPaymentRules.Prefill`). */
export function prefillPayment(clientTerm: string | null | undefined): { method: string; term: string } {
  const t = (clientTerm ?? '').trim().toUpperCase()
  switch (t) {
    case 'CHEQUE': return { method: 'COD_CHECK', term: '' }
    case 'CASH': return { method: 'COD_CASH', term: '' }
    case 'ACH': return { method: 'COD_ACH', term: '' }
    case 'CONSIGNMENT': return { method: 'CONSIGNMENT', term: '' }
    default: return isCreditTerm(t) ? { method: 'CREDIT', term: t } : { method: '', term: '' }
  }
}

/** ¿La forma de pago cambió respecto a la del cliente? (espejo de `SalesPaymentRules.IsChange`: lo que paga el cliente «COD» con cheque, efectivo o ACH no cuenta). */
export function paymentChanged(clientTerm: string | null | undefined, method: string, term: string): boolean {
  if (method === '' && term === '') return false
  const p = prefillPayment(clientTerm)
  if (method.toUpperCase() === p.method && term.toUpperCase() === p.term) return false
  if ((clientTerm ?? '').trim().toUpperCase() === 'COD' && ['COD_CHECK', 'COD_CASH', 'COD_ACH'].includes(method) && term === '') return false
  return true
}

export type PaymentErrorKey = 'creditNeedsTerm' | 'termOnlyWithCredit' | 'consignmentNeedsMethod'

/** Forma, término y consignación juntos (espejo de las reglas del servidor). */
export function paymentError(method: string, term: string, isConsignment: boolean): PaymentErrorKey | null {
  if (method === 'CREDIT' && term === '') return 'creditNeedsTerm'
  if (term !== '' && method !== 'CREDIT') return 'termOnlyWithCredit'
  if (isConsignment && method !== 'CONSIGNMENT') return 'consignmentNeedsMethod'
  return null
}

/** Al cambiar la forma de pago: el término solo sobrevive con crédito y la consignación solo con su forma (decisión 3 del lote 222). */
export function withMethod(d: OrderDraft, method: string): OrderDraft {
  return { ...d, paymentMethod: method, paymentTerm: method === 'CREDIT' ? d.paymentTerm : '', isConsignment: method === 'CONSIGNMENT' ? d.isConsignment : false }
}

/** ¿Falta lo que exige el cliente («Exige la firma de quien autoriza el pedido»)? Aquí solo el nombre; la firma dibujada llega con la app. */
export function authorizerMissing(requires: boolean, name: string): boolean {
  return requires && name.trim() === ''
}

export type DraftErrorKey = PaymentErrorKey | 'authorizedBy' | 'client' | 'lines' | 'product' | 'quantity' | 'quantityDecimals' | 'kitWhole' | 'kitPiece' | 'price' | 'priceDecimals' | 'noPrice' | 'repeated' | 'notes'

/** Primer problema del borrador (la clave se traduce en la pantalla) o null. El servidor repite estas reglas. */
export function draftError(d: OrderDraft): DraftErrorKey | null {
  if (!d.clientPublicId) return 'client'
  if (d.lines.length === 0) return 'lines'
  const seen = new Set<string>()
  for (const l of d.lines) {
    if (!l.productPublicId) return 'product'
    if (seen.has(l.productPublicId)) return 'repeated'
    seen.add(l.productPublicId)
    const q = parseNumber(l.quantity)
    if (q === null || q <= 0) return 'quantity'
    if (decimalsOf(q) > 3) return 'quantityDecimals'
    if (l.isKit && !Number.isInteger(q)) return 'kitWhole'
    if (l.isKit && l.components && l.components.some((c) => { const n = parseNumber(c.quantity); return n === null || n <= 0 })) return 'kitPiece'
    if (l.price.trim() !== '') {
      const p = parseNumber(l.price)
      if (p === null) return 'price'
      if (decimalsOf(p) > 4) return 'priceDecimals'
    } else if (l.listPrice === null) return 'noPrice'
  }
  if (d.notes.length > 500) return 'notes'
  if (d.authorizedByName.length > AUTHORIZED_BY_MAX) return 'authorizedBy'
  const pay = paymentError(d.paymentMethod, d.paymentTerm, d.isConsignment)
  if (pay) return pay
  return null
}

/** Precio que se usará en la línea: el escrito o el de lista. */
export function effectivePrice(l: LineDraft): number | null {
  if (l.price.trim() !== '') return parseNumber(l.price)
  return l.listPrice
}

export function lineAmount(l: LineDraft): number {
  const q = parseNumber(l.quantity)
  const p = effectivePrice(l)
  return q === null || p === null ? 0 : Math.round(q * p * 10_000) / 10_000
}

/** Subtotal del borrador (sin impuesto: lo calcula el servidor al guardar). */
export function draftSubtotal(d: OrderDraft): number {
  return Math.round(d.lines.reduce((acc, l) => acc + lineAmount(l), 0) * 10_000) / 10_000
}

/** Cuerpo para crear o reemplazar: el precio solo viaja si se escribió uno distinto del de lista. */
export function toRequest(d: OrderDraft) {
  return {
    warehousePublicId: d.warehousePublicId ?? undefined,
    clientPublicId: d.clientPublicId ?? undefined,
    requiredDate: d.requiredDate || undefined,
    clearRequiredDate: d.requiredDate ? undefined : true,
    notes: d.notes,
    isUrgent: d.isUrgent,
    deliveryMode: d.deliveryMode || undefined,
    paymentMethod: d.paymentMethod || undefined,
    paymentTerm: d.paymentTerm || undefined,
    isConsignment: d.isConsignment,
    authorizedByName: d.authorizedByName.trim() || undefined,
    lines: d.lines.map((l) => {
      const price = l.price.trim() === '' ? null : parseNumber(l.price)
      return {
        productPublicId: l.productPublicId,
        quantity: parseNumber(l.quantity) ?? undefined,
        unitPrice: price === null || price === l.listPrice ? undefined : price,
        components: l.isKit && l.components ? l.components.map((c) => ({ productPublicId: c.productPublicId, quantity: parseNumber(c.quantity) ?? 0 })) : undefined,
      }
    }),
  }
}

export const ORDER_STATUSES = ['DRAFT', 'REQUESTED', 'PARTIAL', 'SHIPPED', 'CANCELLED'] as const

// ---------------------------------------------------------------- Ajustes → Operación → Ventas (Lote 222)

export interface ShippingSettingsDraft {
  /** Vacío = sin cargo de envío. */
  charge: string
  /** Vacío o 0 = sin mínimo para envío gratis. */
  freeFrom: string
  taxable: boolean
}

export type ShippingSettingsErrorKey = 'charge' | 'freeFrom'

/** Cargo mayor que cero y mínimo de cero o más, ambos con hasta 2 decimales (espejo de `ShippingRules.IsValidCharge/IsValidFreeFrom`). El servidor repite las reglas. */
export function shippingSettingsError(d: ShippingSettingsDraft): ShippingSettingsErrorKey | null {
  if (d.charge.trim() !== '') {
    const c = parseNumber(d.charge)
    if (c === null || c <= 0 || decimalsOf(c) > 2) return 'charge'
  }
  if (d.freeFrom.trim() !== '') {
    const m = parseNumber(d.freeFrom)
    if (m === null || decimalsOf(m) > 2) return 'freeFrom'
  }
  return null
}

/** Cuerpo del PATCH: vacío se manda como 0 («sin cargo», «sin mínimo»); null del servidor significa «sin cambio». */
export function toShippingSettingsRequest(d: ShippingSettingsDraft) {
  return { charge: parseNumber(d.charge) ?? 0, freeFrom: parseNumber(d.freeFrom) ?? 0, taxable: d.taxable }
}

export function shippingSettingsDraftOf(s: { charge?: number | null; freeFrom?: number | null; taxable?: boolean } | undefined): ShippingSettingsDraft {
  return { charge: s?.charge != null ? String(s.charge) : '', freeFrom: s?.freeFrom != null ? String(s.freeFrom) : '', taxable: s?.taxable === true }
}
