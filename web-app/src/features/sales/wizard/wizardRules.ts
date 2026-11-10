// Lote 223 (P3 del pedido de vendedores) — reglas puras del PEDIDO GUIADO: los cuatro pasos (cliente, productos, entrega, autorización), el carrito, el resumen
// de totales que devuelve el servidor (subtotal, IVU, envío con su porqué y total), la validación de cada paso antes de guardar el borrador, el cuerpo que se
// manda en cada paso (POST/PATCH con `step`) y el alta mínima del cliente nuevo. Sin React ni navegador: la app móvil (P4) puede copiarlas tal cual.
// El servidor repite todas estas reglas (SalesDraftStepRules, SalesPaymentRules, ClientProspectRules): aquí solo se evita mandar lo que sabemos que va a fallar.
import { piecesToRequest } from '../kitSubstitution'
import { decimalsOf, paymentChanged, paymentError, parseNumber, prefillPayment, type KitPiece, type PaymentErrorKey } from '../salesRules'

export type WizardStep = 'CLIENT' | 'PRODUCTS' | 'DELIVERY' | 'AUTHORIZATION'
export const WIZARD_STEPS: readonly WizardStep[] = ['CLIENT', 'PRODUCTS', 'DELIVERY', 'AUTHORIZATION']
export const NOTES_MAX = 500
export const AUTHORIZED_BY_MAX = 150

/** Formas de pago del cliente nuevo (códigos del catálogo PaymentTerm, en el orden del formulario de los vendedores). */
export const CLIENT_PAYMENT_TERMS = ['CHEQUE', 'CASH', 'ACH', 'NET15', 'NET20', 'NET30', 'NET45', 'PK_BY_REP', 'CONSIGNMENT'] as const

/** Lo que el catálogo del pedido dice de un producto (subconjunto de SalesCatalogItemDto). */
export interface CatalogItem {
  productPublicId?: string
  sku?: string | null
  name?: string | null
  isKit?: boolean
  kitAdjustable?: boolean
  isPromotion?: boolean
  price?: number | null
  priceSource?: string | null
  imagePublicId?: string | null
}

export interface CartLine {
  productPublicId: string
  sku: string
  name: string
  /** Precio resuelto por el servidor para el cliente (null = sin precio: no se puede pedir). */
  price: number | null
  /** Precio escrito a mano en un producto sin precio (solo con `sales.price`); viaja como unitPrice. */
  typedPrice?: string
  priceSource: string
  isKit: boolean
  kitAdjustable: boolean
  isPromotion: boolean
  imagePublicId: string | null
  /** Texto de la casilla de cantidad, tal cual se escribió. */
  quantity: string
  /** Piezas ajustadas (kit ajustable) o sustituidas (Lote 230) al pedir; null = las del catálogo o las ya guardadas. */
  components: KitPiece[] | null
}

export type DeliveryChoice = 'DELIVERY' | 'PICKUP'

export interface WizardState {
  /** Borrador ya guardado (null = todavía no se ha guardado el primer paso). */
  orderPublicId: string | null
  number: string | null
  clientPublicId: string | null
  /** «Código · Nombre» del cliente escogido (para mostrarlo sin esperar la ficha). */
  clientLabel: string
  /** Almacén escogido a mano (Lote 235): null = el principal de la compañía (la pantalla lo muestra ya escogido cuando hay varios; el servidor también lo toma). */
  warehousePublicId: string | null
  isUrgent: boolean
  notes: string
  paymentMethod: string
  paymentTerm: string
  isConsignment: boolean
  cart: CartLine[]
  deliveryMode: DeliveryChoice
  consigneePublicId: string | null
  requiredDate: string
  authorizedByName: string
}

export function emptyWizard(): WizardState {
  return {
    orderPublicId: null,
    number: null,
    clientPublicId: null,
    clientLabel: '',
    warehousePublicId: null,
    isUrgent: false,
    notes: '',
    paymentMethod: '',
    paymentTerm: '',
    isConsignment: false,
    cart: [],
    deliveryMode: 'DELIVERY',
    consigneePublicId: null,
    requiredDate: '',
    authorizedByName: '',
  }
}

// ---------------------------------------------------------------- cliente y forma de pago

/** Escoger (o cambiar) el cliente: el consignatario del cliente anterior ya no vale; la forma de pago se prellena después, con su ficha. */
export function withClient(s: WizardState, publicId: string | null, label: string): WizardState {
  if (publicId === s.clientPublicId) return { ...s, clientLabel: label || s.clientLabel }
  return { ...s, clientPublicId: publicId, clientLabel: label, consigneePublicId: null }
}

/** Forma de pago prellenada con el término del cliente (espejo de `SalesPaymentRules.Prefill`); el cliente de consignación nace con la casilla marcada. */
export function withClientPayment(s: WizardState, clientTerm: string | null | undefined): WizardState {
  const p = prefillPayment(clientTerm)
  return { ...s, paymentMethod: p.method, paymentTerm: p.term, isConsignment: (clientTerm ?? '').trim().toUpperCase() === 'CONSIGNMENT' }
}

/** Cambiar la forma de pago: el término solo sobrevive con crédito y la consignación solo con su forma (decisión 3 del lote 222). */
export function withPaymentMethod(s: WizardState, method: string): WizardState {
  return { ...s, paymentMethod: method, paymentTerm: method === 'CREDIT' ? s.paymentTerm : '', isConsignment: method === 'CONSIGNMENT' ? s.isConsignment : false }
}

/** ¿La forma de pago del pedido cambió respecto a la del cliente? (la marca que ve la oficina). */
export function isPaymentChanged(s: WizardState, clientTerm: string | null | undefined): boolean {
  return paymentChanged(clientTerm, s.paymentMethod, s.paymentTerm)
}

// ---------------------------------------------------------------- carrito

function lineOf(item: CatalogItem, quantity: string): CartLine {
  return {
    productPublicId: item.productPublicId ?? '',
    sku: item.sku ?? '',
    name: item.name ?? '',
    price: item.price ?? null,
    priceSource: item.priceSource ?? 'NONE',
    isKit: item.isKit === true,
    kitAdjustable: item.kitAdjustable === true,
    isPromotion: item.isPromotion === true,
    imagePublicId: item.imagePublicId ?? null,
    quantity,
    components: null,
  }
}

/** Texto de la cantidad de un producto en el carrito ('' = no está). */
export function cartQuantity(cart: readonly CartLine[], productPublicId: string | undefined): string {
  return cart.find((l) => l.productPublicId === productPublicId)?.quantity ?? ''
}

/**
 * Teclear una cantidad: vacía o cero quita la línea; cualquier otro texto la agrega o la actualiza tal cual (un texto inválido se queda para que la persona lo
 * corrija: la validación del paso lo señala). Una línea nueva va al final.
 */
export function setCartQuantity(cart: readonly CartLine[], item: CatalogItem, text: string): CartLine[] {
  const id = item.productPublicId ?? ''
  const trimmed = text.trim()
  const q = parseNumber(trimmed)
  if (trimmed === '' || q === 0) return cart.filter((l) => l.productPublicId !== id)
  if (cart.some((l) => l.productPublicId === id)) return cart.map((l) => (l.productPublicId === id ? { ...l, quantity: text } : l))
  return [...cart, lineOf(item, text)]
}

/** Botones + / − de una oferta: suma o resta enteros; a cero sale del carrito. */
export function stepCartQuantity(cart: readonly CartLine[], item: CatalogItem, delta: number): CartLine[] {
  const current = parseNumber(cartQuantity(cart, item.productPublicId)) ?? 0
  const next = Math.max(0, Math.floor(current) + delta)
  return setCartQuantity(cart, item, next === 0 ? '' : String(next))
}

export function removeFromCart(cart: readonly CartLine[], productPublicId: string): CartLine[] {
  return cart.filter((l) => l.productPublicId !== productPublicId)
}

export function setCartPieces(cart: readonly CartLine[], productPublicId: string, components: KitPiece[] | null): CartLine[] {
  return cart.map((l) => (l.productPublicId === productPublicId ? { ...l, components } : l))
}

/** Importe de una línea (cantidad × precio, a 4 decimales como el servidor); sin precio o sin cantidad válida = 0. */
export function cartLineAmount(l: CartLine): number {
  const q = parseNumber(l.quantity)
  const price = effectivePrice(l)
  return q === null || price === null ? 0 : Math.round(q * price * 10_000) / 10_000
}

/** El precio de la línea: el del servidor o, si el producto no tiene, el que escribió quien tiene `sales.price`. */
export function effectivePrice(l: CartLine): number | null {
  if (l.price !== null) return l.price
  const typed = parseNumber(l.typedPrice ?? '')
  return typed !== null && typed >= 0 ? typed : null
}

/** Escribe (o borra) el precio de un producto sin precio en el carrito. */
export function setCartPrice(cart: readonly CartLine[], productPublicId: string, text: string): CartLine[] {
  return cart.map((l) => (l.productPublicId === productPublicId ? { ...l, typedPrice: text } : l))
}

/** Lo que dice la barra del carrito: líneas, unidades y subtotal (sin IVU ni envío: esos los calcula el servidor). */
export function cartSummary(cart: readonly CartLine[]): { lines: number; units: number; subtotal: number } {
  const units = cart.reduce((acc, l) => acc + (parseNumber(l.quantity) ?? 0), 0)
  const subtotal = Math.round(cart.reduce((acc, l) => acc + cartLineAmount(l), 0) * 100) / 100
  return { lines: cart.length, units: Math.round(units * 1000) / 1000, subtotal }
}

/** Las líneas para el servidor: producto, cantidad y (solo kits ajustados) sus piezas. El precio NO viaja: lo resuelve el servidor (el vendedor no teclea precios). */
export function cartToLines(cart: readonly CartLine[]) {
  return cart.map((l) => {
    // unitPrice solo si el precio NO sale de una lista: producto sin precio (escrito por quien tiene sales.price) o línea ya guardada en la orden
    // (tras guardar, el precio queda en la línea y hay que seguir mandándolo, si no el servidor lo vuelve a resolver y no lo encuentra); si no, el servidor lo resuelve
    const typed = l.priceSource === 'NONE' || l.priceSource === 'ORDER' ? effectivePrice(l) : null
    return {
      productPublicId: l.productPublicId,
      quantity: parseNumber(l.quantity) ?? undefined,
      ...(typed !== null ? { unitPrice: typed } : {}),
      // Lote 230: piezas ajustadas o sustituidas: todas, la sustituta con la original en substitutesProductPublicId
      components: l.isKit && l.components ? piecesToRequest(l.components) : undefined,
    }
  })
}

/** Tras guardar, el precio de cada línea es el que dejó el servidor (lista del cliente, general o producto). */
export function withServerPrices(cart: readonly CartLine[], lines: readonly { productPublicId?: string; unitPrice?: number }[] | null | undefined): CartLine[] {
  if (!lines) return [...cart]
  return cart.map((l) => {
    const saved = lines.find((x) => x.productPublicId === l.productPublicId)
    return saved && typeof saved.unitPrice === 'number' ? { ...l, price: saved.unitPrice } : l
  })
}

// ---------------------------------------------------------------- validación por paso

export type WizardErrorKey =
  | PaymentErrorKey
  | 'client'
  | 'warehouse'
  | 'notes'
  | 'cartEmpty'
  | 'quantity'
  | 'quantityDecimals'
  | 'kitWhole'
  | 'kitPiece'
  | 'noPrice'
  | 'consignee'
  | 'authorizedByMax'
  | 'authorizedByRequired'
  | 'signatureRequired'

export interface StepContext {
  /** El cliente exige la firma de quien autoriza el pedido. */
  requiresSignature: boolean
  /** Hay una firma dibujada (o ya subida) para el pedido. */
  hasSignature: boolean
  /** La compañía tiene más de un almacén: hay que escoger de cuál sale el pedido. */
  needsWarehouse?: boolean
}

/** Primer problema del paso (la clave se traduce en `salesWizard.errors.*`) o null. */
export function stepError(step: WizardStep, s: WizardState, ctx: StepContext = { requiresSignature: false, hasSignature: false }): WizardErrorKey | null {
  switch (step) {
    case 'CLIENT':
      if (!s.clientPublicId) return 'client'
      if (ctx.needsWarehouse && !s.warehousePublicId) return 'warehouse'
      if (s.notes.length > NOTES_MAX) return 'notes'
      return paymentError(s.paymentMethod, s.paymentTerm, s.isConsignment)
    case 'PRODUCTS':
      if (s.cart.length === 0) return 'cartEmpty'
      for (const l of s.cart) {
        const q = parseNumber(l.quantity)
        if (q === null || q <= 0) return 'quantity'
        if (decimalsOf(q) > 3) return 'quantityDecimals'
        if (l.isKit && !Number.isInteger(q)) return 'kitWhole'
        if (l.isKit && l.components?.some((c) => { const n = parseNumber(c.quantity); return n === null || n <= 0 })) return 'kitPiece'
        if (effectivePrice(l) === null) return 'noPrice'
      }
      return null
    case 'DELIVERY':
      if (s.deliveryMode === 'DELIVERY' && !s.consigneePublicId) return 'consignee'
      if (s.isConsignment && s.paymentMethod !== 'CONSIGNMENT') return 'consignmentNeedsMethod'
      return null
    case 'AUTHORIZATION':
      if (s.authorizedByName.length > AUTHORIZED_BY_MAX) return 'authorizedByMax'
      if (ctx.requiresSignature && s.authorizedByName.trim() === '') return 'authorizedByRequired'
      if (ctx.requiresSignature && !ctx.hasSignature) return 'signatureRequired'
      return null
  }
}

/** Antes de enviar: el primer paso que tenga un problema (para volver ahí) o null. */
export function firstInvalidStep(s: WizardState, ctx: StepContext): { step: WizardStep; error: WizardErrorKey } | null {
  for (const step of WIZARD_STEPS) {
    const error = stepError(step, s, ctx)
    if (error) return { step, error }
  }
  return null
}

export function stepIndex(step: WizardStep): number {
  return WIZARD_STEPS.indexOf(step)
}

// ---------------------------------------------------------------- cuerpo de cada paso

/**
 * El cuerpo del POST (borrador nuevo) o del PATCH (borrador que ya existe) de un paso. En un PATCH, lo ausente no cambia y '' borra; al crear, la forma de pago vacía
 * deja que el servidor la prellene con la del cliente. CLIENT no manda productos al actualizar (los del carrito ya están guardados); desde PRODUCTS van siempre.
 * La entrega se manda desde PRODUCTS (para que el resumen ya traiga el envío); recogido quita el consignatario.
 */
export function stepRequest(step: WizardStep, s: WizardState) {
  const update = s.orderPublicId !== null
  const fromProducts = step !== 'CLIENT'
  const fromDelivery = step === 'DELIVERY' || step === 'AUTHORIZATION'
  return {
    step,
    clientPublicId: s.clientPublicId ?? undefined,
    warehousePublicId: s.warehousePublicId ?? undefined,
    isUrgent: s.isUrgent,
    notes: s.notes,
    paymentMethod: update ? s.paymentMethod : s.paymentMethod || undefined,
    paymentTerm: update ? s.paymentTerm : s.paymentTerm || undefined,
    isConsignment: s.isConsignment,
    lines: fromProducts ? cartToLines(s.cart) : update ? undefined : [],
    deliveryMode: fromProducts ? s.deliveryMode : undefined,
    consigneePublicId: fromProducts && s.deliveryMode === 'DELIVERY' ? s.consigneePublicId ?? undefined : undefined,
    clearConsignee: fromProducts && s.deliveryMode === 'PICKUP' && update ? true : undefined,
    requiredDate: fromDelivery && s.requiredDate ? s.requiredDate : undefined,
    clearRequiredDate: fromDelivery && !s.requiredDate && update ? true : undefined,
    authorizedByName: step === 'AUTHORIZATION' ? s.authorizedByName.trim() : undefined,
  }
}

// ---------------------------------------------------------------- resumen (lo calcula el servidor)

export interface OrderSummaryInput {
  subtotal?: number
  taxAmount?: number
  total?: number
  terms?: { shippingCharge?: number; shippingTaxAmount?: number; shippingReason?: string | null; shippingReasonLabel?: string | null } | null
}

export interface OrderSummary {
  subtotal: number
  /** IVU de los productos más el del envío (si el envío lleva IVU). */
  tax: number
  shipping: number
  shippingTax: number
  total: number
  /** Código del porqué del envío (COMPANY_RULE, CLIENT_FREE, CLIENT_RULE, AMOUNT, PICKUP) o null si no hay regla de envío. */
  reason: string | null
  reasonLabel: string | null
}

/** Subtotal + IVU + envío = total (el total del servidor ya incluye el envío y su IVU, Lote 222). */
export function orderSummary(dto: OrderSummaryInput | null | undefined): OrderSummary {
  const shipping = dto?.terms?.shippingCharge ?? 0
  const shippingTax = dto?.terms?.shippingTaxAmount ?? 0
  return {
    subtotal: dto?.subtotal ?? 0,
    tax: Math.round(((dto?.taxAmount ?? 0) + shippingTax) * 100) / 100,
    shipping,
    shippingTax,
    total: dto?.total ?? 0,
    reason: dto?.terms?.shippingReason ?? null,
    reasonLabel: dto?.terms?.shippingReasonLabel ?? null,
  }
}

// ---------------------------------------------------------------- retomar un borrador

export interface DraftInput {
  publicId?: string
  number?: string
  status?: string
  clientPublicId?: string
  clientName?: string
  warehousePublicId?: string
  consigneePublicId?: string | null
  requiredDate?: string | null
  notes?: string | null
  lines?: readonly { productPublicId?: string; sku?: string; productName?: string; qtyOrdered?: number; unitPrice?: number; isKit?: boolean }[] | null
  terms?: {
    isUrgent?: boolean
    deliveryMode?: string
    deliveryModeSet?: boolean
    paymentMethod?: string | null
    paymentTerm?: string | null
    isConsignment?: boolean
    authorizedByName?: string | null
  } | null
}

/** Estado del asistente desde un borrador guardado (`?order=`). Los precios son los que la orden ya tiene. */
export function stateFromDraft(dto: DraftInput): WizardState {
  return {
    ...emptyWizard(),
    orderPublicId: dto.publicId ?? null,
    number: dto.number ?? null,
    clientPublicId: dto.clientPublicId ?? null,
    clientLabel: dto.clientName ?? '',
    warehousePublicId: dto.warehousePublicId ?? null,
    isUrgent: dto.terms?.isUrgent === true,
    notes: dto.notes ?? '',
    paymentMethod: dto.terms?.paymentMethod ?? '',
    paymentTerm: dto.terms?.paymentTerm ?? '',
    isConsignment: dto.terms?.isConsignment === true,
    cart: (dto.lines ?? []).map((l) => ({
      productPublicId: l.productPublicId ?? '',
      sku: l.sku ?? '',
      name: l.productName ?? '',
      price: l.unitPrice ?? null,
      priceSource: 'ORDER',
      isKit: l.isKit === true,
      kitAdjustable: false,
      isPromotion: false,
      imagePublicId: null,
      quantity: String(l.qtyOrdered ?? ''),
      components: null,
    })),
    deliveryMode: dto.terms?.deliveryMode === 'PICKUP' ? 'PICKUP' : 'DELIVERY',
    consigneePublicId: dto.consigneePublicId ?? null,
    requiredDate: dto.requiredDate ?? '',
    authorizedByName: dto.terms?.authorizedByName ?? '',
  }
}

/** Dónde se retoma un borrador: sin productos → Productos; sin entrega decidida → Entrega; si no, Autorización. El paso no se guarda (decisión 1 del lote 223). */
export function resumeStep(dto: DraftInput): WizardStep {
  if (!dto.clientPublicId) return 'CLIENT'
  if ((dto.lines ?? []).length === 0) return 'PRODUCTS'
  if (!dto.terms?.deliveryModeSet) return 'DELIVERY'
  return 'AUTHORIZATION'
}

// ---------------------------------------------------------------- enviar: qué pasó

export type SubmitOutcome = 'shortage' | 'credit' | 'password' | 'mfa' | 'reauth' | 'other'

/** Clasifica el error de «Enviar pedido» (status, code y campos del ProblemDetails). */
export function submitOutcome(err: { status?: number; code?: string; errors?: Record<string, string[]> } | null | undefined): SubmitOutcome {
  if (!err) return 'other'
  // 409 del inventario (insufficient_stock); otro 409 (orden cancelada) no es un faltante
  if (err.code === 'insufficient_stock' || (err.status === 409 && !err.code)) return 'shortage'
  if (err.code === 'credit_exceeded') return 'credit'
  if (err.code === 'aal2_required') return 'reauth'
  const fields = Object.keys(err.errors ?? {}).map((k) => k.replace(/^\$\./, '').toLowerCase())
  if (err.status === 400 && fields.includes('mfacode')) return 'mfa'
  if (err.status === 400 && fields.includes('password')) return 'password'
  return 'other'
}

// ---------------------------------------------------------------- cliente nuevo (alta mínima)

export interface NewClientValues {
  name: string
  legalName: string
  contactName: string
  phone: string
  phoneExtension: string
  phone2: string
  email: string
  merchantRegistration: string
  physicalLine1: string
  physicalLine2: string
  physicalCity: string
  physicalPostalCode: string
  physicalState: string
  physicalCountry: string
  hasPostal: boolean
  postalLine1: string
  postalLine2: string
  postalCity: string
  postalPostalCode: string
  postalState: string
  postalCountry: string
  paymentTerm: string
}

export function emptyNewClient(): NewClientValues {
  return {
    name: '', legalName: '', contactName: '', phone: '', phoneExtension: '', phone2: '', email: '', merchantRegistration: '',
    physicalLine1: '', physicalLine2: '', physicalCity: '', physicalPostalCode: '', physicalState: '', physicalCountry: '',
    hasPostal: false, postalLine1: '', postalLine2: '', postalCity: '', postalPostalCode: '', postalState: '', postalCountry: '',
    paymentTerm: '',
  }
}

export type NewClientErrorKey =
  | 'nameRequired' | 'nameMax' | 'legalNameMax' | 'contactRequired' | 'contactMax' | 'phoneRequired' | 'phoneTenDigits' | 'extensionInvalid'
  | 'emailInvalid' | 'suriMax' | 'addressRequired' | 'cityRequired' | 'postalLineRequired'

const digitsOnly = (v: string) => v.replace(/\D/g, '')
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Problemas del alta mínima por campo (espejo de `ClientProspectService.CreateBasicAsync` y `ClientProspectRules`; los mensajes exactos son los del servidor). */
export function newClientIssues(v: NewClientValues): Partial<Record<keyof NewClientValues, NewClientErrorKey>> {
  const out: Partial<Record<keyof NewClientValues, NewClientErrorKey>> = {}
  if (v.name.trim() === '') out.name = 'nameRequired'
  else if (v.name.trim().length > 200) out.name = 'nameMax'
  if (v.legalName.trim().length > 250) out.legalName = 'legalNameMax'
  if (v.contactName.trim() === '') out.contactName = 'contactRequired'
  else if (v.contactName.trim().length > 150) out.contactName = 'contactMax'
  const phone = digitsOnly(v.phone)
  if (phone === '') out.phone = 'phoneRequired'
  else if (phone.length !== 10) out.phone = 'phoneTenDigits'
  const ext = v.phoneExtension.trim()
  if (ext !== '' && !/^\d{1,6}$/.test(ext)) out.phoneExtension = 'extensionInvalid'
  const phone2 = digitsOnly(v.phone2)
  if (phone2 !== '' && phone2.length !== 10) out.phone2 = 'phoneTenDigits'
  if (v.email.trim() !== '' && !EMAIL_RE.test(v.email.trim())) out.email = 'emailInvalid'
  if (v.merchantRegistration.trim().length > 50) out.merchantRegistration = 'suriMax'
  if (v.physicalLine1.trim() === '') out.physicalLine1 = 'addressRequired'
  if (v.physicalCity.trim() === '') out.physicalCity = 'cityRequired'
  if (v.hasPostal) {
    if (v.postalLine1.trim() === '') out.postalLine1 = 'postalLineRequired'
    if (v.postalCity.trim() === '') out.postalCity = 'cityRequired'
  }
  return out
}

const opt = (v: string) => (v.trim() === '' ? undefined : v.trim())

/** Cuerpo de POST /clients/basic: teléfonos solo con dígitos, vacíos fuera; la dirección postal solo si se marcó. */
export function toBasicClientRequest(v: NewClientValues) {
  return {
    name: v.name.trim(),
    legalName: opt(v.legalName),
    contactName: v.contactName.trim(),
    phone: digitsOnly(v.phone),
    phoneExtension: opt(v.phoneExtension),
    phone2: digitsOnly(v.phone2) || undefined,
    email: opt(v.email),
    merchantRegistration: opt(v.merchantRegistration),
    physicalAddress: { line1: v.physicalLine1.trim(), line2: opt(v.physicalLine2), city: v.physicalCity.trim(), postalCode: opt(v.physicalPostalCode), state: opt(v.physicalState), country: opt(v.physicalCountry) },
    postalAddress: v.hasPostal
      ? { line1: v.postalLine1.trim(), line2: opt(v.postalLine2), city: v.postalCity.trim(), postalCode: opt(v.postalPostalCode), state: opt(v.postalState), country: opt(v.postalCountry) }
      : undefined,
    paymentTerm: opt(v.paymentTerm),
  }
}

/** Fotos del alta: el registro de comerciante y el certificado de revendedor (tipos del catálogo ClientDocumentType). */
export const CLIENT_DOC_TYPES = ['MERCHANT_REGISTRATION', 'RESELLER_CERTIFICATE'] as const
export type ClientDocType = (typeof CLIENT_DOC_TYPES)[number]

/** Una dirección en una línea: «Calle 1, Apto 2, Bayamón 00961» (lo vacío no se pinta). */
export function addressLine(a: { line1?: string | null; line2?: string | null; city?: string | null; postalCode?: string | null } | null | undefined): string {
  if (!a) return ''
  return [a.line1, a.line2, [a.city, a.postalCode].filter(Boolean).join(' ')].filter((x) => x && String(x).trim() !== '').join(', ')
}
