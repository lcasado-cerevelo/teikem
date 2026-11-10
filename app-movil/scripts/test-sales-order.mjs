// Prueba de las reglas puras del PEDIDO en la app del personal (Lote 226). Uso: npm run test:sales (Node 22+).
// La primera parte repite, caso por caso, web-app/src/features/sales/wizard/wizardRules.test.ts (las reglas son copia: deben seguir dando lo mismo);
// la segunda prueba lo propio del teléfono (Mis pedidos, la huella al enviar, la firma, los errores del API).
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  addressLine, afterBiometric, cartQuantity, cartSummary, cartToLines, emptyNewClient, emptyWizard, errorMessage, firstAuthStep, firstInvalidStep,
  isMine, isPaymentChanged, money, myOrders, newClientIssues, opensWizard, orderBadges, orderSummary, removeFromCart, requiredDateValid, resumeStep,
  setCartPieces, setCartQuantity, signatureAction, signatureBase64, stateFromDraft, stepCartQuantity, stepError, stepRequest, submitBody, submitOutcome,
  toApiError, toBasicClientRequest, withClient, withClientPayment, withPaymentMethod, withServerPrices, MY_ORDER_STATUSES, WIZARD_STEPS, CLIENT_DOC_TYPES,
} from '../src/utils/salesOrderRules.ts';

const GLOVES = { productPublicId: 'p-1', sku: '52800', name: 'Guantes', price: 6.25, priceSource: 'CLIENT_LIST' };
const BOX = { productPublicId: 'p-2', sku: '070120', name: 'Caja', price: 3.5, priceSource: 'GENERAL_LIST' };
const KIT = { productPublicId: 'k-1', sku: 'OFERTA1', name: 'Oferta 1', price: 41.94, isKit: true, kitAdjustable: true, isPromotion: true };
const NOPRICE = { productPublicId: 'p-3', sku: 'X', name: 'Sin precio', price: null };
const NO_SIG = { requiresSignature: false, hasSignature: false };
const ready = () => ({ ...emptyWizard(), clientPublicId: 'c-1', clientLabel: 'C1 · Farmacia', cart: setCartQuantity([], GLOVES, '2'), consigneePublicId: 'l-1' });
const like = (actual, expected, msg) => { for (const [k, v] of Object.entries(expected)) assert.deepEqual(actual[k], v, `${msg ?? ''} (${k})`); };

// ---------------- carrito
{
  let cart = setCartQuantity([], GLOVES, '3');
  assert.equal(cart.length, 1);
  like(cart[0], { productPublicId: 'p-1', sku: '52800', price: 6.25, quantity: '3', priceSource: 'CLIENT_LIST' }, 'línea nueva');
  cart = setCartQuantity(cart, BOX, '1');
  assert.deepEqual(cart.map((l) => l.sku), ['52800', '070120']);
  cart = setCartQuantity(cart, GLOVES, '5');
  assert.equal(cartQuantity(cart, 'p-1'), '5');
  assert.equal(setCartQuantity(cart, GLOVES, '').length, 1);
  assert.equal(setCartQuantity(cart, GLOVES, '0').length, 1);
  assert.deepEqual(removeFromCart(cart, 'p-2').map((l) => l.sku), ['52800']);

  const bad = setCartQuantity([], GLOVES, 'abc');
  assert.equal(bad[0].quantity, 'abc', 'el texto inválido se queda');
  assert.equal(stepError('PRODUCTS', { ...ready(), cart: bad }, NO_SIG), 'quantity');

  let offer = stepCartQuantity([], KIT, 1);
  offer = stepCartQuantity(offer, KIT, 1);
  assert.equal(cartQuantity(offer, 'k-1'), '2');
  offer = stepCartQuantity(offer, KIT, -1);
  assert.equal(cartQuantity(offer, 'k-1'), '1');
  assert.deepEqual(stepCartQuantity(offer, KIT, -1), []);
  assert.deepEqual(stepCartQuantity([], KIT, -1), []);

  assert.deepEqual(cartSummary(setCartQuantity(setCartQuantity([], GLOVES, '3'), BOX, '2.5')), { lines: 2, units: 5.5, subtotal: 27.5 });
  assert.deepEqual(cartSummary([]), { lines: 0, units: 0, subtotal: 0 });

  let withKit = setCartQuantity(setCartQuantity([], GLOVES, '3'), KIT, '1');
  withKit = setCartPieces(withKit, 'k-1', [{ productPublicId: 'p-1', sku: '52800', name: 'Guantes', quantity: '6' }]);
  const lines = cartToLines(withKit);
  assert.deepEqual(lines[0], { productPublicId: 'p-1', quantity: 3, components: undefined });
  assert.deepEqual(lines[1], { productPublicId: 'k-1', quantity: 1, components: [{ productPublicId: 'p-1', quantity: 6 }] });
  assert.ok(lines.every((l) => !('unitPrice' in l)), 'el precio no viaja');

  const priced = withServerPrices(setCartQuantity([], GLOVES, '1'), [{ productPublicId: 'p-1', unitPrice: 5.9 }]);
  assert.equal(priced[0].price, 5.9);
  assert.equal(withServerPrices(priced, null)[0].price, 5.9);
}

// ---------------- cliente y forma de pago
{
  like(withClient({ ...ready(), consigneePublicId: 'l-1' }, 'c-2', 'C2 · Otro'), { clientPublicId: 'c-2', clientLabel: 'C2 · Otro', consigneePublicId: null });
  assert.equal(withClient(ready(), 'c-1', '').consigneePublicId, 'l-1');
  like(withClientPayment(emptyWizard(), 'NET30'), { paymentMethod: 'CREDIT', paymentTerm: 'NET30', isConsignment: false });
  like(withClientPayment(emptyWizard(), 'CONSIGNMENT'), { paymentMethod: 'CONSIGNMENT', isConsignment: true });
  const credit = withClientPayment(emptyWizard(), 'NET30');
  const cash = withPaymentMethod(credit, 'COD_CASH');
  like(cash, { paymentMethod: 'COD_CASH', paymentTerm: '', isConsignment: false });
  assert.equal(isPaymentChanged(credit, 'NET30'), false);
  assert.equal(isPaymentChanged(cash, 'NET30'), true);
  assert.equal(withPaymentMethod(withClientPayment(emptyWizard(), 'CONSIGNMENT'), 'CREDIT').isConsignment, false);
  assert.equal(isPaymentChanged(withPaymentMethod(emptyWizard(), 'COD_ACH'), 'COD'), false, 'COD con ACH no es cambio');
}

// ---------------- validación por paso
{
  assert.equal(stepError('CLIENT', emptyWizard(), NO_SIG), 'client');
  assert.equal(stepError('CLIENT', ready(), { ...NO_SIG, needsWarehouse: true }), 'warehouse');
  assert.equal(stepError('CLIENT', { ...ready(), warehousePublicId: 'w-1' }, { ...NO_SIG, needsWarehouse: true }), null);
  assert.equal(stepError('CLIENT', { ...ready(), notes: 'x'.repeat(501) }, NO_SIG), 'notes');
  assert.equal(stepError('CLIENT', { ...ready(), notes: 'x'.repeat(500) }, NO_SIG), null);
  assert.equal(stepError('CLIENT', { ...ready(), paymentMethod: 'CREDIT' }, NO_SIG), 'creditNeedsTerm');

  assert.equal(stepError('PRODUCTS', { ...ready(), cart: [] }, NO_SIG), 'cartEmpty');
  assert.equal(stepError('PRODUCTS', { ...ready(), cart: setCartQuantity([], GLOVES, '1.2345') }, NO_SIG), 'quantityDecimals');
  assert.equal(stepError('PRODUCTS', { ...ready(), cart: setCartQuantity([], KIT, '1.5') }, NO_SIG), 'kitWhole');
  assert.equal(stepError('PRODUCTS', { ...ready(), cart: setCartPieces(setCartQuantity([], KIT, '1'), 'k-1', [{ productPublicId: 'p-1', sku: '', name: '', quantity: '0' }]) }, NO_SIG), 'kitPiece');
  assert.equal(stepError('PRODUCTS', { ...ready(), cart: setCartQuantity([], NOPRICE, '1') }, NO_SIG), 'noPrice');
  assert.equal(stepError('PRODUCTS', ready(), NO_SIG), null);

  assert.equal(stepError('DELIVERY', { ...ready(), consigneePublicId: null }, NO_SIG), 'consignee');
  assert.equal(stepError('DELIVERY', { ...ready(), consigneePublicId: null, deliveryMode: 'PICKUP' }, NO_SIG), null);
  assert.equal(stepError('DELIVERY', { ...ready(), isConsignment: true, paymentMethod: 'CREDIT' }, NO_SIG), 'consignmentNeedsMethod');

  assert.equal(stepError('AUTHORIZATION', ready(), NO_SIG), null);
  assert.equal(stepError('AUTHORIZATION', { ...ready(), authorizedByName: 'x'.repeat(151) }, NO_SIG), 'authorizedByMax');
  assert.equal(stepError('AUTHORIZATION', ready(), { requiresSignature: true, hasSignature: true }), 'authorizedByRequired');
  assert.equal(stepError('AUTHORIZATION', { ...ready(), authorizedByName: 'Ana' }, { requiresSignature: true, hasSignature: false }), 'signatureRequired');
  assert.equal(stepError('AUTHORIZATION', { ...ready(), authorizedByName: 'Ana' }, { requiresSignature: true, hasSignature: true }), null);

  assert.deepEqual(firstInvalidStep({ ...ready(), cart: [] }, NO_SIG), { step: 'PRODUCTS', error: 'cartEmpty' });
  assert.equal(firstInvalidStep(ready(), NO_SIG), null);
}

// ---------------- cuerpo de cada paso
{
  like(stepRequest('CLIENT', { ...emptyWizard(), clientPublicId: 'c-1', isUrgent: true, notes: 'Urge' }),
    { step: 'CLIENT', clientPublicId: 'c-1', isUrgent: true, notes: 'Urge', lines: [], paymentMethod: undefined, deliveryMode: undefined }, 'CLIENT al crear');
  const s = { ...ready(), orderPublicId: 'o-1', paymentMethod: 'COD_CASH' };
  assert.equal(stepRequest('CLIENT', s).lines, undefined);
  assert.equal(stepRequest('CLIENT', s).paymentMethod, 'COD_CASH');
  like(stepRequest('PRODUCTS', s), { step: 'PRODUCTS', lines: [{ productPublicId: 'p-1', quantity: 2, components: undefined }], deliveryMode: 'DELIVERY', consigneePublicId: 'l-1' });
  const p = { ...ready(), orderPublicId: 'o-1', deliveryMode: 'PICKUP', requiredDate: '', authorizedByName: '  Ana  ' };
  like(stepRequest('PRODUCTS', p), { deliveryMode: 'PICKUP', consigneePublicId: undefined, clearConsignee: true, requiredDate: undefined, clearRequiredDate: undefined });
  like(stepRequest('DELIVERY', p), { clearRequiredDate: true, authorizedByName: undefined });
  like(stepRequest('DELIVERY', { ...p, requiredDate: '2026-11-20' }), { requiredDate: '2026-11-20', clearRequiredDate: undefined });
  assert.equal(stepRequest('AUTHORIZATION', p).authorizedByName, 'Ana');
}

// ---------------- resumen del servidor
{
  assert.deepEqual(orderSummary({ subtotal: 100, taxAmount: 11.5, total: 125.88, terms: { shippingCharge: 12, shippingTaxAmount: 1.38, shippingReason: 'COMPANY_RULE', shippingReasonLabel: 'Regla de la compañía' } }),
    { subtotal: 100, tax: 12.88, shipping: 12, shippingTax: 1.38, total: 125.88, reason: 'COMPANY_RULE', reasonLabel: 'Regla de la compañía' });
  like(orderSummary(null), { subtotal: 0, tax: 0, shipping: 0, total: 0, reason: null });
  like(orderSummary({ subtotal: 10, total: 10, terms: { shippingCharge: 0, shippingReason: 'PICKUP', shippingReasonLabel: 'Recogido' } }), { shipping: 0, reason: 'PICKUP' });
}

// ---------------- retomar un borrador
{
  const draft = {
    publicId: 'o-1', number: 'OV-00042', status: 'DRAFT', clientPublicId: 'c-1', clientName: 'Farmacia', notes: 'nota',
    lines: [{ productPublicId: 'p-1', sku: '52800', productName: 'Guantes', qtyOrdered: 4, unitPrice: 6.25 }],
    terms: { isUrgent: true, deliveryMode: 'PICKUP', deliveryModeSet: true, paymentMethod: 'CREDIT', paymentTerm: 'NET30', authorizedByName: 'Ana' },
  };
  const st = stateFromDraft(draft);
  like(st, { orderPublicId: 'o-1', number: 'OV-00042', isUrgent: true, deliveryMode: 'PICKUP', paymentTerm: 'NET30', authorizedByName: 'Ana' });
  like(st.cart[0], { sku: '52800', quantity: '4', price: 6.25 });
  assert.equal(resumeStep({ ...draft, clientPublicId: undefined }), 'CLIENT');
  assert.equal(resumeStep({ ...draft, lines: [] }), 'PRODUCTS');
  assert.equal(resumeStep({ ...draft, terms: { ...draft.terms, deliveryModeSet: false } }), 'DELIVERY');
  assert.equal(resumeStep(draft), 'AUTHORIZATION');
}

// ---------------- qué pasó al enviar
{
  assert.equal(submitOutcome({ status: 409, code: 'insufficient_stock' }), 'shortage');
  assert.equal(submitOutcome({ status: 409, code: 'conflict' }), 'other');
  assert.equal(submitOutcome({ status: 422, code: 'credit_exceeded' }), 'credit');
  assert.equal(submitOutcome({ status: 400, code: 'validation', errors: { password: ['Contraseña incorrecta.'] } }), 'password');
  assert.equal(submitOutcome({ status: 400, code: 'validation', errors: { MfaCode: ['Código MFA inválido.'] } }), 'mfa');
  assert.equal(submitOutcome({ status: 403, code: 'aal2_required' }), 'reauth');
  assert.equal(submitOutcome({ status: 400, code: 'validation', errors: { signature: ['x'] } }), 'other');
  assert.equal(submitOutcome(null), 'other');
}

// ---------------- cliente nuevo
{
  const ok = { ...emptyNewClient(), name: 'Colmado Luis', contactName: 'Luis', phone: '(787) 555-1234', physicalLine1: 'Calle 1', physicalCity: 'Bayamón' };
  like(newClientIssues(emptyNewClient()), { name: 'nameRequired', contactName: 'contactRequired', phone: 'phoneRequired', physicalLine1: 'addressRequired', physicalCity: 'cityRequired' });
  assert.deepEqual(newClientIssues(ok), {});
  assert.equal(newClientIssues({ ...ok, phone: '555-1234' }).phone, 'phoneTenDigits');
  assert.equal(newClientIssues({ ...ok, phone2: '123' }).phone2, 'phoneTenDigits');
  assert.equal(newClientIssues({ ...ok, phoneExtension: '12a' }).phoneExtension, 'extensionInvalid');
  assert.equal(newClientIssues({ ...ok, email: 'no-es-correo' }).email, 'emailInvalid');
  assert.equal(newClientIssues({ ...ok, merchantRegistration: 'x'.repeat(51) }).merchantRegistration, 'suriMax');
  like(newClientIssues({ ...ok, hasPostal: true }), { postalLine1: 'postalLineRequired', postalCity: 'cityRequired' });
  const body = toBasicClientRequest({ ...ok, physicalPostalCode: '00961', paymentTerm: 'NET30' });
  like(body, { name: 'Colmado Luis', phone: '7875551234', phone2: undefined, email: undefined, paymentTerm: 'NET30', postalAddress: undefined });
  like(body.physicalAddress, { line1: 'Calle 1', city: 'Bayamón', postalCode: '00961', line2: undefined });
  like(toBasicClientRequest({ ...ok, hasPostal: true, postalLine1: 'PO Box 1', postalCity: 'Bayamón' }).postalAddress, { line1: 'PO Box 1' });
  assert.equal(addressLine({ line1: 'Calle 1', line2: '', city: 'Bayamón', postalCode: '00961' }), 'Calle 1, Bayamón 00961');
  assert.equal(addressLine(null), '');
}

// ================ propio del teléfono

// Mis pedidos: las que capturé yo (por nombre o, sin nombre, por correo), los distintivos y qué abre cada una
{
  const me = { fullName: 'Ana Vendedora', email: 'ana@solutions.pr' };
  const rows = [
    { publicId: 'a', status: 'DRAFT', requestedByName: 'Ana Vendedora' },
    { publicId: 'b', status: 'REQUESTED', requestedByName: ' ana vendedora ' },
    { publicId: 'c', status: 'REQUESTED', requestedByName: 'Pedro' },
    { publicId: 'd', status: 'SHIPPED', requestedByName: 'ana@solutions.pr' },
    { publicId: 'e', status: 'SHIPPED', requestedByName: null },
  ];
  assert.deepEqual(myOrders(rows, me).map((r) => r.publicId), ['a', 'b', 'd']);
  assert.equal(isMine(rows[0], null), false, 'sin sesión nada es mío');
  assert.equal(isMine({ requestedByName: '' }, { fullName: '', email: '' }), false, 'vacío con vacío no cuenta');
  assert.deepEqual(orderBadges({ isUrgent: true, awaitingClientApproval: true, isConsignment: true, deliveryMode: 'PICKUP', hasBackorder: true }), ['URGENT', 'AWAITING', 'CONSIGNMENT', 'PICKUP', 'BACKORDER']);
  assert.deepEqual(orderBadges({ deliveryMode: 'DELIVERY' }), []);
  assert.equal(opensWizard({ status: 'DRAFT' }), true);
  assert.equal(opensWizard({ status: 'requested' }), false);
}

// La huella al enviar: primero la huella si el teléfono la tiene; si no o si falla, la contraseña
{
  assert.equal(firstAuthStep(true), 'BIOMETRIC');
  assert.equal(firstAuthStep(false), 'PASSWORD');
  assert.equal(afterBiometric(true), 'send');
  assert.equal(afterBiometric(false), 'password');
  assert.deepEqual(submitBody({ method: 'BIOMETRIC' }, { mfa: true }), { authMethod: 'BIOMETRIC', password: undefined, mfaCode: undefined, allowBackorder: undefined, overrideCredit: undefined });
  assert.deepEqual(submitBody({ method: 'PASSWORD', password: 'x1', mfaCode: ' 123456 ' }, { mfa: true, allowBackorder: true }),
    { authMethod: 'PASSWORD', password: 'x1', mfaCode: '123456', allowBackorder: true, overrideCredit: undefined });
  assert.equal(submitBody({ method: 'PASSWORD', password: 'x1', mfaCode: '123456' }, { mfa: false }).mfaCode, undefined, 'sin MFA no se manda código');
  assert.equal(submitBody({ method: 'BIOMETRIC' }, { mfa: false, overrideCredit: true }).overrideCredit, true);
}

// La firma del lienzo: solo el base64; subirla, quitarla o dejarla
{
  assert.equal(signatureBase64('data:image/png;base64,iVBORw0KGgo='), 'iVBORw0KGgo=');
  assert.equal(signatureBase64(null), '');
  assert.equal(signatureAction(false, 'data:image/png;base64,AAA', null), 'none');
  assert.equal(signatureAction(true, 'data:image/png;base64,AAA', null), 'upload');
  assert.equal(signatureAction(true, null, 'f-1'), 'remove');
  assert.equal(signatureAction(true, null, null), 'none');
}

// Errores del API (axios) y otros detalles
{
  const e409 = toApiError({ response: { status: 409, data: { title: 'Falta inventario.', code: 'insufficient_stock' } } });
  like(e409, { network: false, status: 409, code: 'insufficient_stock', title: 'Falta inventario.' });
  assert.equal(submitOutcome(e409), 'shortage');
  const e400 = toApiError({ response: { status: 400, data: { title: 'One or more validation errors occurred.', errors: { password: ['Contraseña incorrecta.'] } } } });
  assert.equal(submitOutcome(e400), 'password');
  assert.equal(errorMessage(e400), 'Contraseña incorrecta.', 'el mensaje del campo manda');
  assert.equal(toApiError(new Error('Network Error')).network, true);
  assert.equal(errorMessage({ title: 'X' }), 'X');
  assert.equal(money(1234.5), '$1,234.50');
  assert.equal(money(0), '$0.00');
  assert.equal(money(41.94), '$41.94');
  assert.equal(money(1000000), '$1,000,000.00');
  assert.equal(requiredDateValid(''), true);
  assert.equal(requiredDateValid('2026-11-20'), true);
  assert.equal(requiredDateValid('2026-02-30'), false);
  assert.equal(requiredDateValid('20/11/2026'), false);
}

// Textos: español e inglés con las mismas claves, y las claves que las pantallas arman con un código (errores, estatus, distintivos, pasos…) existen
{
  const read = (lang) => JSON.parse(readFileSync(new URL(`../src/lang/${lang}.json`, import.meta.url), 'utf8')).translation.SALES_ORDER;
  const flat = (o, pre = '') => Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' ? flat(v, `${pre}${k}.`) : [`${pre}${k}`]));
  const es = read('es');
  const en = read('en');
  assert.deepEqual(flat(es).sort(), flat(en).sort(), 'es y en con las mismas claves');
  const has = (path) => path.split('.').reduce((o, k) => (o == null ? undefined : o[k]), es) !== undefined;
  const wizardErrors = ['client', 'warehouse', 'notes', 'creditNeedsTerm', 'termOnlyWithCredit', 'consignmentNeedsMethod', 'cartEmpty', 'quantity', 'quantityDecimals',
    'kitWhole', 'kitPiece', 'noPrice', 'consignee', 'authorizedByMax', 'authorizedByRequired', 'signatureRequired', 'requiredDate'];
  const newClientErrors = ['nameRequired', 'nameMax', 'legalNameMax', 'contactRequired', 'contactMax', 'phoneRequired', 'phoneTenDigits', 'extensionInvalid', 'emailInvalid',
    'suriMax', 'addressRequired', 'cityRequired', 'postalLineRequired'];
  const dynamic = [
    ...wizardErrors.map((k) => `ERRORS.${k}`),
    ...newClientErrors.map((k) => `NEW_CLIENT.ERRORS.${k}`),
    ...MY_ORDER_STATUSES.map((s) => `STATUS.${s || 'ALL'}`),
    ...['URGENT', 'AWAITING', 'CONSIGNMENT', 'PICKUP', 'BACKORDER'].map((b) => `BADGE.${b}`),
    ...WIZARD_STEPS.map((s) => `STEPS.${s}`),
    ...CLIENT_DOC_TYPES.map((d) => `NEW_CLIENT.DOC_${d}`),
    ...['NO_BIOMETRIC', 'BIOMETRIC_FAILED', 'EXPIRED'].map((r) => `SEND.WHY_${r}`),
    'DELIVERY.DELIVERY', 'DELIVERY.DELIVERY_HELP', 'DELIVERY.PICKUP', 'DELIVERY.PICKUP_HELP',
  ];
  for (const k of dynamic) assert.ok(has(k), `falta SALES_ORDER.${k}`);
  // las claves fijas que usan las pantallas
  const sources = ['src/app/(auth)/SalesOrders/index.tsx', 'src/app/(auth)/NewSalesOrder/index.tsx', 'src/components/sales/ClientStep.tsx', 'src/components/sales/NewClientModal.tsx',
    'src/components/sales/ProductsStep.tsx', 'src/components/sales/DeliveryStep.tsx', 'src/components/sales/AuthorizationStep.tsx', 'src/components/sales/SubmitSheets.tsx'];
  for (const f of sources) {
    const text = readFileSync(new URL(`../${f}`, import.meta.url), 'utf8');
    for (const m of text.matchAll(/t\('SALES_ORDER\.([A-Za-z0-9_.]+)'/g)) assert.ok(has(m[1]), `falta SALES_ORDER.${m[1]} (usada en ${f})`);
  }
  // los mensajes que repiten los del servidor (Lote 223) son exactos
  assert.equal(es.ERRORS.cartEmpty, 'Agregue al menos un producto a la orden.');
  assert.equal(es.ERRORS.consignee, 'Escoja a dónde se entrega el pedido o márquelo como recogido.');
  assert.equal(es.ERRORS.signatureRequired, 'Dibuje la firma de quien autoriza el pedido.');
  assert.equal(es.SEND.PASSWORD_MISSING, 'Escriba su contraseña para enviar el pedido.');
}

// ---------------- Lote 235: almacén de la orden, disponible, backorder marcado (no bloquea) y nota de otros almacenes (copia de web-app/.../stockRules.test.ts)
{
  const {
    anyBackorder, availableElsewhere, availableIn, backorderQty, lineStock, mainWarehouseId, orderWarehouseId, shortHeadKey, showWarehouseField, warehouseLabel,
  } = await import('../src/utils/salesStockRules.ts');
  const ONE = { warehouses: [{ publicId: 'w-1', code: 'ALM-01', name: 'Bayamón', isMain: true }], products: [{ productPublicId: 'p-1', stock: [{ warehousePublicId: 'w-1', available: 5 }] }] };
  const THREE = {
    warehouses: [
      { publicId: 'w-1', code: 'ALM-01', name: 'Bayamón', isMain: false },
      { publicId: 'w-2', code: 'ALM-02', name: 'Ponce', isMain: true },
      { publicId: 'w-3', code: 'ALM-03', name: 'Mayagüez', isMain: false },
    ],
    products: [
      { productPublicId: 'p-1', stock: [{ warehousePublicId: 'w-1', available: 12 }, { warehousePublicId: 'w-2', available: 5 }, { warehousePublicId: 'w-3', available: 3 }] },
      { productPublicId: 'p-2', stock: [{ warehousePublicId: 'w-3', available: 8 }] },
    ],
  };
  // con un solo almacén no se pregunta; con varios, el principal por omisión
  assert.equal(showWarehouseField(ONE), false);
  assert.equal(orderWarehouseId(ONE, null), 'w-1');
  assert.equal(showWarehouseField(THREE), true);
  assert.equal(mainWarehouseId(THREE), 'w-2');
  assert.equal(orderWarehouseId(THREE, null), 'w-2');
  assert.equal(orderWarehouseId(THREE, 'w-3'), 'w-3');
  assert.equal(warehouseLabel(THREE, 'w-2'), 'Ponce');
  assert.equal(showWarehouseField(null), false);
  assert.equal(orderWarehouseId(undefined, null), null);
  // disponible
  assert.equal(availableIn(THREE, 'p-1', 'w-2'), 5);
  assert.equal(availableIn(THREE, 'p-2', 'w-2'), 0);
  // pedir de más NO se bloquea: se cuenta el backorder
  assert.equal(backorderQty('5', 5), 0);
  assert.equal(backorderQty('8', 5), 3);
  assert.equal(backorderQty('4', 0), 4);
  assert.equal(backorderQty('', 5), 0);
  assert.equal(shortHeadKey(0), 'shortNone');
  assert.equal(shortHeadKey(1), 'shortOne');
  assert.equal(shortHeadKey(5), 'shortMany');
  assert.deepEqual(lineStock(THREE, 'p-1', 'w-2', '8'), {
    available: 5, backorder: 3, elsewhere: [{ warehousePublicId: 'w-1', name: 'Bayamón', available: 12 }, { warehousePublicId: 'w-3', name: 'Mayagüez', available: 3 }],
  });
  assert.equal(lineStock(null, 'p-1', 'w-2', '8'), null);
  // la nota de otros almacenes
  assert.deepEqual(availableElsewhere(ONE, 'p-1', 'w-1'), []);
  assert.deepEqual(availableElsewhere(THREE, 'p-1', 'w-1').map((e) => e.name), ['Ponce', 'Mayagüez']);
  assert.equal(anyBackorder(THREE, 'w-2', [{ productPublicId: 'p-1', quantity: '5' }]), false);
  assert.equal(anyBackorder(THREE, 'w-2', [{ productPublicId: 'p-2', quantity: '1' }]), true);
  // el paso Productos ya no se frena por la cantidad disponible (solo por las reglas de siempre)
  assert.equal(stepError('PRODUCTS', { ...ready(), cart: setCartQuantity([], GLOVES, '999') }, NO_SIG), null);
  // los textos del aviso
  for (const lang of ['es', 'en']) {
    const tr = JSON.parse(readFileSync(new URL(`../src/lang/${lang}.json`, import.meta.url), 'utf8')).translation.SALES_ORDER.STOCK;
    for (const k of ['AVAILABLE', 'BACKORDER_CHIP', 'shortMany', 'shortOne', 'shortNone', 'backMany', 'backOne', 'ELSEWHERE_ONE', 'ELSEWHERE_MANY', 'BACKORDER_HELP']) assert.ok(tr[k], `${lang}: STOCK.${k}`);
  }
}

console.log('test-sales-order: ok');
