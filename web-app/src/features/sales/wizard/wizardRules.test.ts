// Lote 223 (P3) — reglas puras del pedido guiado: carrito, totales, envío con su porqué, validaciones por paso, cuerpo de cada paso, retomar un borrador,
// qué pasó al enviar y el alta mínima del cliente nuevo.
import { describe, expect, it } from 'vitest'
import {
  addressLine,
  cartLineAmount,
  cartQuantity,
  cartSummary,
  cartToLines,
  emptyNewClient,
  emptyWizard,
  firstInvalidStep,
  isPaymentChanged,
  newClientIssues,
  orderSummary,
  removeFromCart,
  resumeStep,
  setCartPieces,
  setCartPrice,
  setCartQuantity,
  stateFromDraft,
  stepCartQuantity,
  stepError,
  stepRequest,
  submitOutcome,
  toBasicClientRequest,
  withClient,
  withClientPayment,
  withPaymentMethod,
  withServerPrices,
  type CatalogItem,
  type WizardState,
} from './wizardRules'

const GLOVES: CatalogItem = { productPublicId: 'p-1', sku: '52800', name: 'Guantes', price: 6.25, priceSource: 'CLIENT_LIST' }
const BOX: CatalogItem = { productPublicId: 'p-2', sku: '070120', name: 'Caja', price: 3.5, priceSource: 'GENERAL_LIST' }
const KIT: CatalogItem = { productPublicId: 'k-1', sku: 'OFERTA1', name: 'Oferta 1', price: 41.94, isKit: true, kitAdjustable: true, isPromotion: true }
const NOPRICE: CatalogItem = { productPublicId: 'p-3', sku: 'X', name: 'Sin precio', price: null }

const NO_SIG = { requiresSignature: false, hasSignature: false }

function ready(): WizardState {
  return { ...emptyWizard(), clientPublicId: 'c-1', clientLabel: 'C1 · Farmacia', cart: setCartQuantity([], GLOVES, '2'), consigneePublicId: 'l-1' }
}

describe('carrito', () => {
  it('teclear una cantidad agrega la línea; cambiarla la actualiza; vacía o cero la quita', () => {
    let cart = setCartQuantity([], GLOVES, '3')
    expect(cart).toHaveLength(1)
    expect(cart[0]).toMatchObject({ productPublicId: 'p-1', sku: '52800', price: 6.25, quantity: '3', priceSource: 'CLIENT_LIST' })
    cart = setCartQuantity(cart, BOX, '1')
    expect(cart.map((l) => l.sku)).toEqual(['52800', '070120'])
    cart = setCartQuantity(cart, GLOVES, '5')
    expect(cartQuantity(cart, 'p-1')).toBe('5')
    expect(setCartQuantity(cart, GLOVES, '')).toHaveLength(1)
    expect(setCartQuantity(cart, GLOVES, '0')).toHaveLength(1)
    expect(removeFromCart(cart, 'p-2').map((l) => l.sku)).toEqual(['52800'])
  })

  it('un texto inválido se queda en la línea para corregirlo (lo señala la validación)', () => {
    const cart = setCartQuantity([], GLOVES, 'abc')
    expect(cart[0].quantity).toBe('abc')
    expect(stepError('PRODUCTS', { ...ready(), cart }, NO_SIG)).toBe('quantity')
  })

  it('+ / − de una oferta suma y resta enteros; a cero sale del carrito', () => {
    let cart = stepCartQuantity([], KIT, 1)
    cart = stepCartQuantity(cart, KIT, 1)
    expect(cartQuantity(cart, 'k-1')).toBe('2')
    cart = stepCartQuantity(cart, KIT, -1)
    expect(cartQuantity(cart, 'k-1')).toBe('1')
    expect(stepCartQuantity(cart, KIT, -1)).toEqual([])
    expect(stepCartQuantity([], KIT, -1)).toEqual([])
  })

  it('resumen: líneas, unidades y subtotal redondeado a centavos', () => {
    const cart = setCartQuantity(setCartQuantity([], GLOVES, '3'), BOX, '2.5')
    expect(cartSummary(cart)).toEqual({ lines: 2, units: 5.5, subtotal: 27.5 })
    expect(cartSummary([])).toEqual({ lines: 0, units: 0, subtotal: 0 })
  })

  it('las líneas para el servidor no llevan precio; un kit ajustado lleva sus piezas', () => {
    let cart = setCartQuantity(setCartQuantity([], GLOVES, '3'), KIT, '1')
    cart = setCartPieces(cart, 'k-1', [{ productPublicId: 'p-1', sku: '52800', name: 'Guantes', quantity: '6' }])
    const lines = cartToLines(cart)
    expect(lines[0]).toEqual({ productPublicId: 'p-1', quantity: 3, components: undefined })
    expect(lines[1]).toEqual({ productPublicId: 'k-1', quantity: 1, components: [{ productPublicId: 'p-1', quantity: 6 }] })
    expect(lines.every((l) => !('unitPrice' in l))).toBe(true)
  })

  it('un producto sin precio lleva el que escribió quien tiene sales.price, y sin él no pasa el paso', () => {
    const noPrice = { ...GLOVES, productPublicId: 'p-9', price: null, priceSource: 'NONE' }
    let cart = setCartQuantity([], noPrice, '2')
    expect(stepError('PRODUCTS', { ...emptyWizard(), cart })).toBe('noPrice')
    cart = setCartPrice(cart, 'p-9', '4.5')
    expect(cartToLines(cart)[0]).toMatchObject({ productPublicId: 'p-9', quantity: 2, unitPrice: 4.5 })
    expect(cartLineAmount(cart[0])).toBe(9)
    expect(stepError('PRODUCTS', { ...emptyWizard(), cart })).toBeNull()
  })

  it('tras guardar, el precio de cada línea es el del servidor', () => {
    const cart = withServerPrices(setCartQuantity([], GLOVES, '1'), [{ productPublicId: 'p-1', unitPrice: 5.9 }])
    expect(cart[0].price).toBe(5.9)
    expect(withServerPrices(cart, null)[0].price).toBe(5.9)
  })
})

describe('cliente y forma de pago', () => {
  it('cambiar de cliente olvida el consignatario del anterior', () => {
    const s = withClient({ ...ready(), consigneePublicId: 'l-1' }, 'c-2', 'C2 · Otro')
    expect(s).toMatchObject({ clientPublicId: 'c-2', clientLabel: 'C2 · Otro', consigneePublicId: null })
    expect(withClient(ready(), 'c-1', '').consigneePublicId).toBe('l-1')
  })

  it('prellenada con el término del cliente; el de consignación nace con la casilla marcada', () => {
    expect(withClientPayment(emptyWizard(), 'NET30')).toMatchObject({ paymentMethod: 'CREDIT', paymentTerm: 'NET30', isConsignment: false })
    expect(withClientPayment(emptyWizard(), 'CONSIGNMENT')).toMatchObject({ paymentMethod: 'CONSIGNMENT', isConsignment: true })
  })

  it('cambiar la forma de pago quita el término y la consignación que ya no aplican, y deja la marca de cambio', () => {
    const credit = withClientPayment(emptyWizard(), 'NET30')
    const cash = withPaymentMethod(credit, 'COD_CASH')
    expect(cash).toMatchObject({ paymentMethod: 'COD_CASH', paymentTerm: '', isConsignment: false })
    expect(isPaymentChanged(credit, 'NET30')).toBe(false)
    expect(isPaymentChanged(cash, 'NET30')).toBe(true)
    expect(withPaymentMethod(withClientPayment(emptyWizard(), 'CONSIGNMENT'), 'CREDIT').isConsignment).toBe(false)
  })
})

describe('validación por paso', () => {
  it('Cliente: escogido, almacén si hay varios, nota hasta 500, crédito con término', () => {
    expect(stepError('CLIENT', emptyWizard(), NO_SIG)).toBe('client')
    expect(stepError('CLIENT', ready(), { ...NO_SIG, needsWarehouse: true })).toBe('warehouse')
    expect(stepError('CLIENT', { ...ready(), warehousePublicId: 'w-1' }, { ...NO_SIG, needsWarehouse: true })).toBeNull()
    expect(stepError('CLIENT', { ...ready(), notes: 'x'.repeat(501) }, NO_SIG)).toBe('notes')
    expect(stepError('CLIENT', { ...ready(), notes: 'x'.repeat(500) }, NO_SIG)).toBeNull()
    expect(stepError('CLIENT', { ...ready(), paymentMethod: 'CREDIT' }, NO_SIG)).toBe('creditNeedsTerm')
  })

  it('Productos: al menos uno, cantidades válidas, kit entero, todo con precio', () => {
    expect(stepError('PRODUCTS', { ...ready(), cart: [] }, NO_SIG)).toBe('cartEmpty')
    expect(stepError('PRODUCTS', { ...ready(), cart: setCartQuantity([], GLOVES, '1.2345') }, NO_SIG)).toBe('quantityDecimals')
    expect(stepError('PRODUCTS', { ...ready(), cart: setCartQuantity([], KIT, '1.5') }, NO_SIG)).toBe('kitWhole')
    expect(stepError('PRODUCTS', { ...ready(), cart: setCartPieces(setCartQuantity([], KIT, '1'), 'k-1', [{ productPublicId: 'p-1', sku: '', name: '', quantity: '0' }]) }, NO_SIG)).toBe('kitPiece')
    expect(stepError('PRODUCTS', { ...ready(), cart: setCartQuantity([], NOPRICE, '1') }, NO_SIG)).toBe('noPrice')
    expect(stepError('PRODUCTS', ready(), NO_SIG)).toBeNull()
  })

  it('Entrega: con entrega hace falta a dónde; recogido no; consignación solo con su forma', () => {
    expect(stepError('DELIVERY', { ...ready(), consigneePublicId: null }, NO_SIG)).toBe('consignee')
    expect(stepError('DELIVERY', { ...ready(), consigneePublicId: null, deliveryMode: 'PICKUP' }, NO_SIG)).toBeNull()
    expect(stepError('DELIVERY', { ...ready(), isConsignment: true, paymentMethod: 'CREDIT' }, NO_SIG)).toBe('consignmentNeedsMethod')
  })

  it('Autorización: nombre y firma solo si el cliente los exige; el nombre hasta 150', () => {
    expect(stepError('AUTHORIZATION', ready(), NO_SIG)).toBeNull()
    expect(stepError('AUTHORIZATION', { ...ready(), authorizedByName: 'x'.repeat(151) }, NO_SIG)).toBe('authorizedByMax')
    expect(stepError('AUTHORIZATION', ready(), { requiresSignature: true, hasSignature: true })).toBe('authorizedByRequired')
    expect(stepError('AUTHORIZATION', { ...ready(), authorizedByName: 'Ana' }, { requiresSignature: true, hasSignature: false })).toBe('signatureRequired')
    expect(stepError('AUTHORIZATION', { ...ready(), authorizedByName: 'Ana' }, { requiresSignature: true, hasSignature: true })).toBeNull()
  })

  it('antes de enviar, el primer paso con problema', () => {
    expect(firstInvalidStep({ ...ready(), cart: [] }, NO_SIG)).toEqual({ step: 'PRODUCTS', error: 'cartEmpty' })
    expect(firstInvalidStep(ready(), NO_SIG)).toBeNull()
  })
})

describe('cuerpo de cada paso', () => {
  it('CLIENT al crear: sin productos y forma de pago vacía (la prellena el servidor)', () => {
    const body = stepRequest('CLIENT', { ...emptyWizard(), clientPublicId: 'c-1', isUrgent: true, notes: 'Urge' })
    expect(body).toMatchObject({ step: 'CLIENT', clientPublicId: 'c-1', isUrgent: true, notes: 'Urge', lines: [], paymentMethod: undefined, deliveryMode: undefined })
  })

  it('CLIENT al actualizar no manda productos; PRODUCTS los manda con la entrega', () => {
    const s = { ...ready(), orderPublicId: 'o-1', paymentMethod: 'COD_CASH' }
    expect(stepRequest('CLIENT', s).lines).toBeUndefined()
    expect(stepRequest('CLIENT', s).paymentMethod).toBe('COD_CASH')
    expect(stepRequest('PRODUCTS', s)).toMatchObject({ step: 'PRODUCTS', lines: [{ productPublicId: 'p-1', quantity: 2 }], deliveryMode: 'DELIVERY', consigneePublicId: 'l-1' })
  })

  it('recogido quita el consignatario; la fecha requerida y quien autoriza van en su paso', () => {
    const s = { ...ready(), orderPublicId: 'o-1', deliveryMode: 'PICKUP' as const, requiredDate: '', authorizedByName: '  Ana  ' }
    expect(stepRequest('PRODUCTS', s)).toMatchObject({ deliveryMode: 'PICKUP', consigneePublicId: undefined, clearConsignee: true, requiredDate: undefined, clearRequiredDate: undefined })
    expect(stepRequest('DELIVERY', s)).toMatchObject({ clearRequiredDate: true, authorizedByName: undefined })
    expect(stepRequest('DELIVERY', { ...s, requiredDate: '2026-11-20' })).toMatchObject({ requiredDate: '2026-11-20', clearRequiredDate: undefined })
    expect(stepRequest('AUTHORIZATION', s).authorizedByName).toBe('Ana')
  })
})

describe('resumen del servidor (envío con su porqué)', () => {
  it('subtotal + IVU (con el del envío) + envío = total del servidor', () => {
    const sum = orderSummary({ subtotal: 100, taxAmount: 11.5, total: 125.88, terms: { shippingCharge: 12, shippingTaxAmount: 1.38, shippingReason: 'COMPANY_RULE', shippingReasonLabel: 'Regla de la compañía' } })
    expect(sum).toEqual({ subtotal: 100, tax: 12.88, shipping: 12, shippingTax: 1.38, total: 125.88, reason: 'COMPANY_RULE', reasonLabel: 'Regla de la compañía' })
  })

  it('sin orden guardada todo en cero; recogido sin cargo con su porqué', () => {
    expect(orderSummary(null)).toMatchObject({ subtotal: 0, tax: 0, shipping: 0, total: 0, reason: null })
    expect(orderSummary({ subtotal: 10, total: 10, terms: { shippingCharge: 0, shippingReason: 'PICKUP', shippingReasonLabel: 'Recogido' } })).toMatchObject({ shipping: 0, reason: 'PICKUP' })
  })
})

describe('retomar un borrador', () => {
  const draft = {
    publicId: 'o-1',
    number: 'OV-00042',
    status: 'DRAFT',
    clientPublicId: 'c-1',
    clientName: 'Farmacia',
    notes: 'nota',
    lines: [{ productPublicId: 'p-1', sku: '52800', productName: 'Guantes', qtyOrdered: 4, unitPrice: 6.25 }],
    terms: { isUrgent: true, deliveryMode: 'PICKUP', deliveryModeSet: true, paymentMethod: 'CREDIT', paymentTerm: 'NET30', authorizedByName: 'Ana' },
  }

  it('el estado sale del borrador con sus precios', () => {
    expect(stateFromDraft(draft)).toMatchObject({ orderPublicId: 'o-1', number: 'OV-00042', isUrgent: true, deliveryMode: 'PICKUP', paymentTerm: 'NET30', authorizedByName: 'Ana', cart: [{ sku: '52800', quantity: '4', price: 6.25 }] })
  })

  it('se retoma en el primer paso pendiente', () => {
    expect(resumeStep({ ...draft, clientPublicId: undefined })).toBe('CLIENT')
    expect(resumeStep({ ...draft, lines: [] })).toBe('PRODUCTS')
    expect(resumeStep({ ...draft, terms: { ...draft.terms, deliveryModeSet: false } })).toBe('DELIVERY')
    expect(resumeStep(draft)).toBe('AUTHORIZATION')
  })
})

describe('qué pasó al enviar', () => {
  it('faltante, crédito, contraseña, MFA, reautenticación y lo demás', () => {
    expect(submitOutcome({ status: 409, code: 'insufficient_stock' })).toBe('shortage')
    expect(submitOutcome({ status: 409, code: 'conflict' })).toBe('other')
    expect(submitOutcome({ status: 422, code: 'credit_exceeded' })).toBe('credit')
    expect(submitOutcome({ status: 400, code: 'validation', errors: { password: ['Contraseña incorrecta.'] } })).toBe('password')
    expect(submitOutcome({ status: 400, code: 'validation', errors: { MfaCode: ['Código MFA inválido.'] } })).toBe('mfa')
    expect(submitOutcome({ status: 403, code: 'aal2_required' })).toBe('reauth')
    expect(submitOutcome({ status: 400, code: 'validation', errors: { signature: ['x'] } })).toBe('other')
    expect(submitOutcome(null)).toBe('other')
  })
})

describe('cliente nuevo (alta mínima)', () => {
  const ok = { ...emptyNewClient(), name: 'Colmado Luis', contactName: 'Luis', phone: '(787) 555-1234', physicalLine1: 'Calle 1', physicalCity: 'Bayamón' }

  it('lo obligatorio y el teléfono de 10 dígitos', () => {
    expect(newClientIssues(emptyNewClient())).toMatchObject({ name: 'nameRequired', contactName: 'contactRequired', phone: 'phoneRequired', physicalLine1: 'addressRequired', physicalCity: 'cityRequired' })
    expect(newClientIssues(ok)).toEqual({})
    expect(newClientIssues({ ...ok, phone: '555-1234' }).phone).toBe('phoneTenDigits')
    expect(newClientIssues({ ...ok, phone2: '123' }).phone2).toBe('phoneTenDigits')
    expect(newClientIssues({ ...ok, phoneExtension: '12a' }).phoneExtension).toBe('extensionInvalid')
    expect(newClientIssues({ ...ok, email: 'no-es-correo' }).email).toBe('emailInvalid')
    expect(newClientIssues({ ...ok, merchantRegistration: 'x'.repeat(51) }).merchantRegistration).toBe('suriMax')
    expect(newClientIssues({ ...ok, hasPostal: true })).toMatchObject({ postalLine1: 'postalLineRequired', postalCity: 'cityRequired' })
  })

  it('el cuerpo: teléfonos con solo dígitos, vacíos fuera, la postal solo si se marcó', () => {
    const body = toBasicClientRequest({ ...ok, physicalPostalCode: '00961', paymentTerm: 'NET30' })
    expect(body).toMatchObject({ name: 'Colmado Luis', phone: '7875551234', phone2: undefined, email: undefined, paymentTerm: 'NET30', postalAddress: undefined })
    expect(body.physicalAddress).toMatchObject({ line1: 'Calle 1', city: 'Bayamón', postalCode: '00961', line2: undefined })
    expect(toBasicClientRequest({ ...ok, hasPostal: true, postalLine1: 'PO Box 1', postalCity: 'Bayamón' }).postalAddress).toMatchObject({ line1: 'PO Box 1' })
  })

  it('dirección en una línea', () => {
    expect(addressLine({ line1: 'Calle 1', line2: '', city: 'Bayamón', postalCode: '00961' })).toBe('Calle 1, Bayamón 00961')
    expect(addressLine(null)).toBe('')
  })
})
