// Lote 223 (P3) — el pedido guiado completo sobre un API simulado: borrador guardado en cada paso (POST con CLIENT, PATCH con PRODUCTS, DELIVERY y
// AUTHORIZATION), la dirección con ?order=, la firma exigida (PNG multipart), «Enviar pedido» con la contraseña (mala = 400 en el campo), el faltante (409) con
// el diálogo de siempre que repite con backorder y la misma contraseña, la pantalla final OV-#####, retomar un borrador, y la prueba de 360 px.
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { useLocation } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import NewOrderScreen from './NewOrderScreen'
import { problem, recordingFormData, type FakeApi, type FakeRequest } from './wizardTestApi'
import { WizardProviders } from './wizardTestKit'

const mock = vi.hoisted(() => ({ requests: [], route: null, forms: [] }) as FakeApi)

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fakeFetch } = await import('./wizardTestApi')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fakeFetch(mock) }) }
})

const PASSWORD = 'Secreta1!'
const CLIENT = {
  id: 1,
  publicId: 'c-1',
  code: 'C0001',
  name: 'Farmacia Central',
  status: 'ACTIVE',
  isActive: true,
  paymentTerm: 'NET30',
  requireOrderSignature: false,
  physicalAddress: { publicId: 'l-phys', line1: 'Calle Sol 12', city: 'Bayamón', postalCode: '00961', isActive: true },
}
const CATALOG = {
  clientPublicId: 'c-1',
  brands: [{ brand: 'Ammex', offers: [], products: [{ productPublicId: 'p-1', sku: '52800', name: 'Guantes de nitrilo', price: 6.25, priceSource: 'CLIENT_LIST', isKit: false }] }],
}
const TERMS = { isUrgent: false, deliveryMode: null, deliveryModeSet: false, paymentMethod: null, paymentTerm: null, shippingCharge: 0, shippingTaxAmount: 0, shippingReason: null, shippingReasonLabel: null, isConsignment: false, authorizedByName: null, signatureFilePublicId: null, awaitingClientApproval: false, clientRequiresSignature: false }

/** El servidor simulado: guarda el borrador como lo dejaría el API y responde el envío según la contraseña y el inventario. */
const server = vi.hoisted(() => ({ order: null as Record<string, unknown> | null, client: null as Record<string, unknown> | null, shortage: false }))

function applyBody(body: Record<string, unknown>) {
  const o = (server.order ??= { publicId: 'o-1', number: 'OV-00042', status: 'DRAFT', statusLabel: 'Borrador', clientName: 'Farmacia Central', lines: [], subtotal: 0, taxAmount: 0, total: 0, hasBackorder: false, terms: { ...TERMS } })
  const terms = o.terms as Record<string, unknown>
  if (body.clientPublicId) o.clientPublicId = body.clientPublicId
  if (Array.isArray(body.lines)) {
    o.lines = (body.lines as { productPublicId: string; quantity: number }[]).map((l, i) => ({ id: i + 1, lineNo: i + 1, productPublicId: l.productPublicId, sku: '52800', productName: 'Guantes de nitrilo', qtyOrdered: l.quantity, unitPrice: 6.25 }))
    const subtotal = (o.lines as { qtyOrdered: number }[]).reduce((a, l) => a + l.qtyOrdered * 6.25, 0)
    const shipping = body.deliveryMode === 'PICKUP' ? 0 : 12
    o.subtotal = subtotal
    o.taxAmount = Math.round(subtotal * 0.115 * 100) / 100
    o.total = Math.round((subtotal + (o.taxAmount as number) + shipping + (shipping ? 1.38 : 0)) * 100) / 100
    Object.assign(terms, { shippingCharge: shipping, shippingTaxAmount: shipping ? 1.38 : 0, shippingReason: shipping ? 'COMPANY_RULE' : 'PICKUP', shippingReasonLabel: shipping ? 'regla de envío de la compañía' : 'el cliente lo recoge' })
  }
  if (body.deliveryMode) Object.assign(terms, { deliveryMode: body.deliveryMode, deliveryModeSet: true })
  if (body.consigneePublicId) o.consigneePublicId = body.consigneePublicId
  for (const k of ['isUrgent', 'paymentMethod', 'paymentTerm', 'isConsignment']) if (body[k] !== undefined) terms[k] = body[k]
  if (typeof body.authorizedByName === 'string') terms.authorizedByName = body.authorizedByName
  return o
}

function route(r: FakeRequest): Response | unknown | undefined {
  const { method: m, path: p } = r
  if (m === 'GET' && p === '/api/v1/clients') return [CLIENT]
  if (m === 'GET' && p === '/api/v1/clients/c-1') return server.client ?? CLIENT
  if (m === 'GET' && p === '/api/v1/sales-orders/catalog') return CATALOG
  if (m === 'POST' && p === '/api/v1/sales-orders') return applyBody(r.body as Record<string, unknown>)
  if (m === 'PATCH' && p === '/api/v1/sales-orders/o-1') return applyBody(r.body as Record<string, unknown>)
  if (m === 'GET' && p === '/api/v1/sales-orders/o-1') return server.order ?? problem(404, 'La orden no existe.')
  if (m === 'POST' && p === '/api/v1/sales-orders/o-1/signature') {
    Object.assign(server.order!.terms as object, { signatureFilePublicId: 'f-sig' })
    return server.order
  }
  if (m === 'GET' && p === '/api/v1/sales-orders/o-1/availability') return { lines: [{ lineNo: 1, sku: '52800', productName: 'Guantes de nitrilo', ordered: 4, available: 1, shortage: 3 }], hasShortage: true, credit: null }
  if (m === 'POST' && p === '/api/v1/sales-orders/o-1/submit') {
    const b = r.body as { authMethod: string; password: string; allowBackorder?: boolean }
    if (b.password !== PASSWORD) return problem(400, 'Contraseña incorrecta.', 'validation', { password: ['Contraseña incorrecta.'] })
    if (server.shortage && !b.allowBackorder) return problem(409, 'No alcanza el inventario.', 'insufficient_stock')
    server.order = { ...server.order!, status: 'REQUESTED', hasBackorder: !!b.allowBackorder, terms: { ...(server.order!.terms as object), submittedWithAuth: 'PASSWORD', awaitingClientApproval: false } }
    return server.order
  }
  return undefined
}

function Where() {
  const loc = useLocation()
  return <output data-testid="where">{loc.pathname + loc.search}</output>
}

function renderScreen(route = '/sales-orders/new-order', mfa = false) {
  return render(
    <WizardProviders route={route} mfa={mfa}>
      <NewOrderScreen />
      <Where />
    </WizardProviders>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

async function pickClient(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole('searchbox', { name: 'Buscar cliente' }), 'farm')
  await user.click(await screen.findByRole('button', { name: /Farmacia Central/ }))
  // la forma de pago se prellena con la del cliente (NET30 → Crédito, Neto 30)
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Forma de pago' })).toHaveValue('CREDIT'))
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Término del crédito' })).toHaveValue('NET30'))
}

async function next(user: ReturnType<typeof userEvent.setup>, title: string) {
  await user.click(screen.getByRole('button', { name: 'Siguiente' }))
  await screen.findByRole('heading', { level: 2, name: title })
}

describe('NewOrderScreen — pedido guiado web', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    vi.stubGlobal('FormData', recordingFormData(mock))
    mock.requests.length = 0
    mock.forms = []
    mock.route = route
    server.order = null
    server.client = null
    server.shortage = false
  })
  afterEach(() => vi.unstubAllGlobals())

  it('cuatro pasos con borrador en cada uno; envía con la contraseña y, con faltante, el diálogo de siempre repite con backorder', async () => {
    server.shortage = true
    const user = userEvent.setup()
    renderScreen()
    expect(screen.getByText('Paso 1 de 4: Cliente')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Atrás' })).toBeDisabled()

    // paso 1 — cliente, urgente y nota
    await pickClient(user)
    await user.click(screen.getByRole('switch', { name: 'Pedido urgente' }))
    await user.type(screen.getByRole('textbox', { name: 'Nota del pedido' }), 'Antes de las 10')
    await next(user, 'Productos')
    expect(sent('POST', '/api/v1/sales-orders')[0].body).toMatchObject({ step: 'CLIENT', clientPublicId: 'c-1', isUrgent: true, notes: 'Antes de las 10', paymentMethod: 'CREDIT', paymentTerm: 'NET30', lines: [] })
    expect(screen.getByTestId('where')).toHaveTextContent('/sales-orders/new-order?order=o-1')
    expect(screen.getByText('Borrador OV-00042: se guarda en cada paso.')).toBeInTheDocument()

    // paso 2 — sin productos no avanza; teclear la cantidad agrega la línea
    await user.click(screen.getByRole('button', { name: 'Siguiente' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Agregue al menos un producto al pedido.')
    expect(sent('PATCH', '/api/v1/sales-orders/o-1')).toHaveLength(0)
    await user.type(await screen.findByRole('textbox', { name: 'Cantidad de 52800' }), '4')
    expect(screen.getByRole('button', { name: /Carrito: 1 producto/ })).toHaveTextContent('$25.00')
    await next(user, 'Entrega')
    expect(sent('PATCH', '/api/v1/sales-orders/o-1')[0].body).toMatchObject({ step: 'PRODUCTS', lines: [{ productPublicId: 'p-1', quantity: 4 }], deliveryMode: 'DELIVERY', consigneePublicId: 'l-phys' })

    // paso 3 — la dirección física ya escogida y el envío calculado con su porqué
    expect(screen.getByRole('radio', { name: /Dirección física del cliente/ })).toBeChecked()
    const summary = screen.getByRole('region', { name: 'Resumen' })
    expect(within(summary).getByText('Cargo de envío').nextSibling).toHaveTextContent('$12.00')
    expect(within(summary).getByText('Por qué: regla de envío de la compañía')).toBeInTheDocument()
    expect(within(summary).getByText('Total').nextSibling).toHaveTextContent('$41.26')
    await next(user, 'Autorización y envío')
    expect(sent('PATCH', '/api/v1/sales-orders/o-1')[1].body).toMatchObject({ step: 'DELIVERY' })

    // paso 4 — volver atrás con la barra de pasos y regresar
    await user.click(screen.getByRole('button', { name: /Productos/ }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Productos' })).toBeInTheDocument()
    await next(user, 'Entrega')
    await next(user, 'Autorización y envío')
    await user.type(screen.getByRole('textbox', { name: 'Nombre de quien autoriza' }), 'Ana Ruiz')
    await user.click(screen.getByRole('button', { name: 'Enviar pedido' }))
    const dialog = await screen.findByRole('dialog', { name: 'Confirme con su contraseña' })
    const patches = sent('PATCH', '/api/v1/sales-orders/o-1')
    expect(patches[patches.length - 1].body).toMatchObject({ step: 'AUTHORIZATION', authorizedByName: 'Ana Ruiz' })
    expect(sent('POST', '/api/v1/sales-orders/o-1/signature')).toHaveLength(0)

    // contraseña mala: 400 en el campo, la ventana sigue abierta
    await user.type(within(dialog).getByLabelText('Contraseña'), 'mala{Enter}')
    expect(await within(dialog).findByText('Contraseña incorrecta.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Contraseña')).toHaveAttribute('aria-invalid', 'true')

    // contraseña buena: falta inventario (409) → el diálogo de faltante; reservar lo disponible repite con backorder y la misma contraseña
    await user.clear(within(dialog).getByLabelText('Contraseña'))
    await user.type(within(dialog).getByLabelText('Contraseña'), `${PASSWORD}{Enter}`)
    const check = await screen.findByRole('dialog', { name: 'Solicitar la orden OV-00042' })
    expect(within(check).getByText('Guantes de nitrilo')).toBeInTheDocument()
    await user.click(within(check).getByRole('button', { name: 'Reservar lo disponible y dejar el resto en backorder' }))

    expect(await screen.findByRole('heading', { level: 2, name: 'Pedido enviado' })).toBeInTheDocument()
    const submits = sent('POST', '/api/v1/sales-orders/o-1/submit')
    expect(submits.map((s) => s.body)).toEqual([
      { authMethod: 'PASSWORD', password: 'mala' },
      { authMethod: 'PASSWORD', password: PASSWORD },
      { authMethod: 'PASSWORD', password: PASSWORD, allowBackorder: true },
    ])
    expect(screen.getByText('OV-00042')).toBeInTheDocument()
    expect(screen.getByText('Parte del pedido quedó en backorder: se surte cuando llegue el inventario.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver en Órdenes de venta' })).toHaveAttribute('href', '/sales-orders?order=o-1')

    // «Nuevo pedido» empieza otro desde cero
    await user.click(screen.getByRole('button', { name: 'Nuevo pedido' }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Cliente' })).toBeInTheDocument()
    expect(screen.getByTestId('where')).toHaveTextContent(/^\/sales-orders\/new-order$/)
  })

  it('sin cliente no guarda; un cliente que exige firma no deja enviar sin ella y la firma sube como PNG con el nombre', async () => {
    server.client = { ...CLIENT, requireOrderSignature: true }
    const getContext = vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
      () => ({ fillRect() {}, beginPath() {}, moveTo() {}, lineTo() {}, stroke() {} }) as never,
    )
    const toBlob = vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (cb: BlobCallback) {
      cb(new Blob(['png'], { type: 'image/png' }))
    })
    const user = userEvent.setup()
    renderScreen()
    await user.click(screen.getByRole('button', { name: 'Siguiente' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Escoja el cliente que compra.')
    expect(sent('POST', '/api/v1/sales-orders')).toHaveLength(0)

    await pickClient(user)
    await next(user, 'Productos')
    await user.type(await screen.findByRole('textbox', { name: 'Cantidad de 52800' }), '2')
    await next(user, 'Entrega')
    await user.click(screen.getByRole('radio', { name: /^Recogido/ }))
    await waitFor(() => expect(sent('PATCH', '/api/v1/sales-orders/o-1').some((r) => (r.body as { deliveryMode?: string }).deliveryMode === 'PICKUP')).toBe(true))
    expect(await screen.findByText('Por qué: el cliente lo recoge')).toBeInTheDocument()
    await next(user, 'Autorización y envío')
    expect(screen.getByText('Este cliente exige el nombre y la firma de quien autoriza el pedido.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Enviar pedido' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Este cliente exige el nombre de quien autoriza el pedido.')
    await user.type(screen.getByRole('textbox', { name: /Nombre de quien autoriza/ }), 'Ana Ruiz')
    await user.click(screen.getByRole('button', { name: 'Enviar pedido' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Dibuje la firma de quien autoriza el pedido.')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    const box = screen.getByRole('img', { name: /Firma de quien autoriza/ })
    fireEvent.pointerDown(box, { clientX: 10, clientY: 10, pointerId: 1, pointerType: 'touch' })
    fireEvent.pointerMove(box, { clientX: 80, clientY: 50, pointerId: 1, pointerType: 'touch' })
    fireEvent.pointerUp(box, { clientX: 80, clientY: 50, pointerId: 1, pointerType: 'touch' })
    await user.click(screen.getByRole('button', { name: 'Enviar pedido' }))
    await screen.findByRole('dialog', { name: 'Confirme con su contraseña' })
    const sig = sent('POST', '/api/v1/sales-orders/o-1/signature')
    expect(sig).toHaveLength(1)
    expect(sig[0].form).toMatchObject({ authorizedByName: 'Ana Ruiz' })
    expect((sig[0].form!.file as Blob).type).toBe('image/png')
    getContext.mockRestore()
    toBlob.mockRestore()
  })

  it('retoma un borrador de la dirección (?order=) en el primer paso pendiente; uno ya solicitado no se edita aquí', async () => {
    server.order = { publicId: 'o-1', number: 'OV-00042', status: 'DRAFT', clientPublicId: 'c-1', clientName: 'Farmacia Central', lines: [{ productPublicId: 'p-1', sku: '52800', productName: 'Guantes de nitrilo', qtyOrdered: 3, unitPrice: 6.25 }], subtotal: 18.75, taxAmount: 2.16, total: 20.91, terms: { ...TERMS, deliveryMode: 'PICKUP', deliveryModeSet: true, paymentMethod: 'COD_CASH', authorizedByName: 'Ana' } }
    const { unmount } = renderScreen('/sales-orders/new-order?order=o-1')
    expect(await screen.findByText('Paso 4 de 4: Autorización')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Nombre de quien autoriza' })).toHaveValue('Ana')
    expect(screen.getByText('Borrador OV-00042: se guarda en cada paso.')).toBeInTheDocument()
    unmount()

    server.order = { ...server.order, status: 'REQUESTED' }
    renderScreen('/sales-orders/new-order?order=o-1')
    expect(await screen.findByText('La orden OV-00042 ya no es un borrador: ábrala en Órdenes de venta.')).toBeInTheDocument()
  })

  it('con MFA en la cuenta, la contraseña va con su código', async () => {
    server.order = { publicId: 'o-1', number: 'OV-00042', status: 'DRAFT', clientPublicId: 'c-1', clientName: 'Farmacia Central', lines: [{ productPublicId: 'p-1', sku: '52800', productName: 'Guantes', qtyOrdered: 1, unitPrice: 6.25 }], subtotal: 6.25, taxAmount: 0.72, total: 6.97, terms: { ...TERMS, deliveryMode: 'PICKUP', deliveryModeSet: true } }
    const user = userEvent.setup()
    renderScreen('/sales-orders/new-order?order=o-1', true)
    await user.click(await screen.findByRole('button', { name: 'Enviar pedido' }))
    const dialog = await screen.findByRole('dialog', { name: 'Confirme con su contraseña' })
    await user.type(within(dialog).getByLabelText('Contraseña'), PASSWORD)
    await user.type(within(dialog).getByLabelText('Código de verificación (MFA)'), '123456')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar pedido' }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Pedido enviado' })).toBeInTheDocument()
    expect(sent('POST', '/api/v1/sales-orders/o-1/submit')[0].body).toEqual({ authMethod: 'PASSWORD', password: PASSWORD, mfaCode: '123456' })
  })
})

describe('NewOrderScreen — 360 px', () => {
  const css = ['wizard.css', '../../../kernel/ui/signaturePad.css'].map((f) => readFileSync(resolve(__dirname, f), 'utf8')).join('\n')

  it('ningún ancho fijo pasa de 340 px y toda rejilla deja encoger sus columnas', () => {
    const widths = [...css.matchAll(/(?:^|[\s;{])(?:min-)?width:\s*(\d+)px/g)].map((m) => Number(m[1]))
    expect(widths.every((w) => w <= 340)).toBe(true)
    const grids = [...css.matchAll(/grid-template-columns:\s*([^;}]+)/g)].map((m) => m[1].trim())
    expect(grids.length).toBeGreaterThan(0)
    for (const g of grids) expect(g).toMatch(/minmax\(0|minmax\(min\(100%/)
  })

  it('bajo 480 px los pasos muestran solo su número (y el nombre del actual) y la lista y el carrito pasan a dos renglones', () => {
    const narrow = css.slice(css.indexOf('@media (max-width: 480px)'))
    expect(narrow).toMatch(/\.wz-lab\s*\{\s*display:\s*none/)
    expect(narrow).toMatch(/\.wz-progress \.on \.wz-lab\s*\{\s*display:\s*inline/)
    expect(narrow).toMatch(/\.wz-prod\s*\{\s*grid-template-columns:\s*44px minmax\(0, 1fr\)/)
    expect(narrow).toMatch(/\.wz-cart-line\s*\{\s*grid-template-columns:\s*minmax\(0, 1fr\) auto/)
  })

  it('a 360 px la pantalla no usa tablas ni anchos en línea: listas que envuelven y el carrito fijo abajo', async () => {
    mock.route = route
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 })
    window.dispatchEvent(new Event('resize'))
    const { container } = renderScreen()
    await screen.findByRole('searchbox', { name: 'Buscar cliente' })
    expect(container.querySelector('table')).toBeNull()
    expect(container.querySelector('.wz-screen [style*="width:"][style*="px"]')).toBeNull()
    expect(container.querySelector('.wz-foot .wz-nav')).not.toBeNull()
    // las etiquetas de los pasos llevan su texto (lector de pantalla) aunque a 360 px solo se vea el número
    expect(within(screen.getByRole('navigation', { name: 'Pasos del pedido' })).getAllByRole('button').map((b) => b.textContent)).toEqual(['1Cliente', '2Productos', '3Entrega', '4Autorización'])
  })
})

