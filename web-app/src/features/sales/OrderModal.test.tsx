// Lote 222 — la ventana de la orden con los datos nuevos del pedido de vendedores, sobre un fetch simulado: urgente, entrega/recogido, forma de pago (prellenada con la del cliente y marca
// «cambió»), consignación, quién autoriza, y en la ficha de una orden solicitada los distintivos, el cargo de envío con su porqué y la firma pendiente.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { OrderModal } from './OrderModal'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; body: unknown; search?: URLSearchParams }[],
  client: null as unknown,
  catalog: null as unknown,
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body, search: url.searchParams })
    const p = url.pathname
    const m = req.method
    if (m === 'GET' && p === `/api/v1/sales-orders/${DRAFT}`) return json(DRAFT_ORDER)
    if (m === 'GET' && p === `/api/v1/sales-orders/${REQ}`) return json(REQUESTED_ORDER)
    if (m === 'PATCH' && p === `/api/v1/sales-orders/${DRAFT}`) return json(DRAFT_ORDER)
    if (m === 'POST' && p === '/api/v1/sales-orders') return json({ ...DRAFT_ORDER, publicId: DRAFT })
    if (m === 'GET' && p === `/api/v1/clients/${CLIENT}`) return mock.client ? json(mock.client) : new Response('{}', { status: 404 })
    if (m === 'GET' && p === '/api/v1/clients') return json([{ id: 1, publicId: CLIENT, code: 'FARM', name: 'Farmacia Central', isActive: true }])
    if (m === 'GET' && p === '/api/v1/products') return json({ items: [{ id: 1, publicId: PROD, sku: 'MED', name: 'Medidor', isActive: true, isOwn: true, salePrice: 12.5 }], total: 1, skip: 0, take: 50 })
    if (m === 'GET' && p === '/api/v1/sales-orders/catalog') return json(mock.catalog ?? { brands: [] })
    if (m === 'GET' && p === '/api/v1/warehouses') return json([{ id: 1, publicId: WH, code: 'ALM-01', name: 'Principal', isActive: true }])
    if (p === '/api/v1/catalogs/SalesPaymentMethod') {
      return json(
        [
          ['COD_CHECK', 'Contra entrega con cheque'],
          ['COD_CASH', 'Contra entrega en efectivo'],
          ['CREDIT', 'Crédito'],
          ['CONSIGNMENT', 'Consignación'],
        ].map(([code, label], i) => ({ id: i + 1, entity: 'SalesPaymentMethod', code, label, labels: { es: label }, sortOrder: i, isSystem: true, isEnabled: true, isOverridden: false, isActive: true })),
      )
    }
    if (p === '/api/v1/catalogs/PaymentTerm') {
      return json(
        [
          ['CHEQUE', 'Cheque'],
          ['NET15', 'Neto 15'],
          ['NET30', 'Neto 30'],
        ].map(([code, label], i) => ({ id: 10 + i, entity: 'PaymentTerm', code, label, labels: { es: label }, sortOrder: i, isSystem: true, isEnabled: true, isOverridden: false, isActive: true })),
      )
    }
    if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return json([])
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const DRAFT = '11111111-0000-0000-0000-000000000001'
const REQ = '22222222-0000-0000-0000-000000000002'
const CLIENT = '33333333-0000-0000-0000-000000000003'
const PROD = '44444444-0000-0000-0000-000000000004'
const WH = '55555555-0000-0000-0000-000000000005'

const TERMS = { isUrgent: false, deliveryMode: 'DELIVERY', deliveryModeSet: false, paymentMethod: null, paymentMethodLabel: null, paymentTerm: null, paymentTermLabel: null, paymentChanged: false, shippingCharge: 0, shippingTaxAmount: 0, shippingReason: null, shippingReasonLabel: null, isConsignment: false, authorizedByName: null, signatureFilePublicId: null, submittedWithAuth: null, awaitingClientApproval: false, clientIsProspect: false, clientRequiresSignature: false }
const LINE = { id: 10, lineNo: 1, productPublicId: PROD, sku: 'MED', productName: 'Medidor', lotId: null, qtyOrdered: 4, qtyReserved: 0, qtyBackordered: 0, qtyShipped: 0, isClosed: false, unitPrice: 12.5, lineSubtotal: 50, taxRatePct: 11.5, taxAmount: 5.75, isTaxExempt: false }
const BASE = { publicId: DRAFT, number: 'OV-00001', status: 'DRAFT', statusLabel: 'Borrador', statusColor: '#9CA3AF', clientPublicId: CLIENT, clientName: 'Farmacia Central', warehousePublicId: WH, warehouseName: 'Principal', requestedDate: '2026-10-15', requiredDate: null, subtotal: 50, taxAmount: 5.75, total: 55.75, hasBackorder: false, requestedByName: 'Ana', createdAtUtc: '2026-10-15T12:00:00Z', consigneePublicId: null, consigneeName: null, notes: null, requestedAtUtc: null, lines: [LINE], terms: TERMS }
let DRAFT_ORDER: Record<string, unknown> = BASE
let REQUESTED_ORDER: Record<string, unknown> = BASE

const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(['sales.view', 'sales.create', 'sales.cancel', 'warehouse.pick', 'clients.read']),
  modules: new Set(['SALES_ORDERS', 'CATALOG']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(publicId: string | null) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={SESSION.permissions} modules={SESSION.modules}>
          <MemoryRouter>
            <OrderModal publicId={publicId} onClose={() => {}} />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

describe('OrderModal — Lote 222', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.client = null
    mock.catalog = null
    DRAFT_ORDER = BASE
    REQUESTED_ORDER = BASE
  })

  it('el borrador muestra y guarda urgente, recogido, forma de pago con término y quién autoriza', async () => {
    DRAFT_ORDER = { ...BASE, terms: { ...TERMS, isUrgent: true, deliveryModeSet: true, deliveryMode: 'PICKUP', paymentMethod: 'CREDIT', paymentTerm: 'NET30', authorizedByName: 'Ana Ruiz' } }
    const user = userEvent.setup()
    wrap(DRAFT)
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00001/ })
    expect(within(dialog).getByRole('switch', { name: 'Pedido urgente' })).toBeChecked()
    expect(within(dialog).getByLabelText('Entrega o recogido')).toHaveValue('PICKUP')
    await waitFor(() => expect(within(dialog).getByLabelText('Forma de pago')).toHaveValue('CREDIT'))
    await waitFor(() => expect(within(dialog).getByLabelText('Término del crédito')).toHaveValue('NET30'))
    expect(within(dialog).getByLabelText('Quién autoriza el pedido')).toHaveValue('Ana Ruiz')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(sent('PATCH', `/api/v1/sales-orders/${DRAFT}`)).toHaveLength(1))
    expect(sent('PATCH', `/api/v1/sales-orders/${DRAFT}`)[0].body).toMatchObject({ isUrgent: true, deliveryMode: 'PICKUP', paymentMethod: 'CREDIT', paymentTerm: 'NET30', isConsignment: false, authorizedByName: 'Ana Ruiz' })
  })

  it('cambiar la forma de pago quita el término; con otra forma que la del cliente sale la marca «cambió»', async () => {
    mock.client = { id: 1, publicId: CLIENT, name: 'Farmacia Central', paymentTerm: 'NET30', requireOrderSignature: false }
    DRAFT_ORDER = { ...BASE, terms: { ...TERMS, paymentMethod: 'CREDIT', paymentTerm: 'NET30' } }
    const user = userEvent.setup()
    wrap(DRAFT)
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00001/ })
    await waitFor(() => expect(within(dialog).getByLabelText('Término del crédito')).toHaveValue('NET30'))
    await waitFor(() => expect(mock.requests.some((r) => r.path === `/api/v1/clients/${CLIENT}`)).toBe(true))
    expect(within(dialog).queryByText('Cambió la forma de pago')).not.toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Forma de pago'), 'COD_CASH')
    expect(within(dialog).queryByLabelText('Término del crédito')).not.toBeInTheDocument()
    expect(await within(dialog).findByText('Cambió la forma de pago')).toBeInTheDocument()
  })

  it('un cliente de consignación deja marcar la consignación solo con su forma de pago, y el cliente que exige firma vuelve obligatorio el nombre', async () => {
    mock.client = { id: 1, publicId: CLIENT, name: 'Farmacia Central', paymentTerm: 'CONSIGNMENT', requireOrderSignature: true }
    DRAFT_ORDER = { ...BASE, terms: { ...TERMS, paymentMethod: 'CONSIGNMENT', isConsignment: true } }
    const user = userEvent.setup()
    wrap(DRAFT)
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00001/ })
    expect(within(dialog).getByRole('switch', { name: /En consignación/ })).toBeChecked()
    expect(await within(dialog).findByLabelText('Quién autoriza el pedido (obligatorio)')).toBeInTheDocument()
    expect(within(dialog).getByText('Este cliente exige el nombre y la firma de quien autoriza el pedido.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Forma de pago'), 'COD_CHECK')
    expect(within(dialog).queryByRole('switch', { name: /En consignación/ })).not.toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(sent('PATCH', `/api/v1/sales-orders/${DRAFT}`)).toHaveLength(1))
    expect(sent('PATCH', `/api/v1/sales-orders/${DRAFT}`)[0].body).toMatchObject({ paymentMethod: 'COD_CHECK', isConsignment: false })
  })

  it('el borrador guardado muestra el cargo de envío con su porqué', async () => {
    DRAFT_ORDER = { ...BASE, terms: { ...TERMS, shippingCharge: 5, shippingTaxAmount: 0.58, shippingReason: 'COMPANY_RULE', shippingReasonLabel: 'Regla de la compañía' } }
    wrap(DRAFT)
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00001/ })
    expect(within(dialog).getByText('Cargo de envío: $5.00')).toBeInTheDocument()
    expect(within(dialog).getByText('Porqué: Regla de la compañía')).toBeInTheDocument()
  })

  it('un envío sin cargo dice por qué (recogido, cliente sin cargo o monto)', async () => {
    DRAFT_ORDER = { ...BASE, terms: { ...TERMS, shippingCharge: 0, shippingReason: 'AMOUNT', shippingReasonLabel: 'Pasa del mínimo para envío gratis' } }
    wrap(DRAFT)
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00001/ })
    expect(within(dialog).getByText('Envío sin cargo')).toBeInTheDocument()
    expect(within(dialog).getByText('Porqué: Pasa del mínimo para envío gratis')).toBeInTheDocument()
  })

  it('la ficha de una orden solicitada pinta los distintivos, la forma de pago, el envío y la firma pendiente', async () => {
    REQUESTED_ORDER = {
      ...BASE,
      publicId: REQ,
      number: 'OV-00002',
      status: 'REQUESTED',
      statusLabel: 'Solicitada',
      terms: {
        ...TERMS,
        isUrgent: true,
        deliveryModeSet: true,
        deliveryMode: 'PICKUP',
        paymentMethod: 'CONSIGNMENT',
        paymentMethodLabel: 'Consignación',
        paymentChanged: true,
        isConsignment: true,
        awaitingClientApproval: true,
        clientIsProspect: true,
        clientRequiresSignature: true,
        authorizedByName: 'Ana Ruiz',
        shippingCharge: 0,
        shippingReason: 'PICKUP',
        shippingReasonLabel: 'Recogido en el almacén',
      },
    }
    wrap(REQ)
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00002/ })
    for (const text of ['Urgente', 'Espera aprobación', 'Recogido', 'En consignación', 'Cambió la forma de pago', 'Cliente nuevo', 'Firma pendiente']) {
      expect(within(dialog).getAllByText(text).length).toBeGreaterThan(0)
    }
    expect(within(dialog).getByText('Ana Ruiz', { exact: false })).toBeInTheDocument()
    expect(within(dialog).getByText('Porqué: Recogido en el almacén')).toBeInTheDocument()
    expect(within(dialog).getByText('Consignación')).toBeInTheDocument()
  })

  it('una orden nueva con cliente de crédito prellena la forma de pago con la del cliente', async () => {
    mock.client = { id: 1, publicId: CLIENT, name: 'Farmacia Central', paymentTerm: 'NET15', requireOrderSignature: false }
    const user = userEvent.setup()
    wrap(null)
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden' })
    await user.click(within(dialog).getByLabelText('Cliente que compra'))
    await user.click(await screen.findByText(/Farmacia Central/))
    await waitFor(() => expect(within(dialog).getByLabelText('Forma de pago')).toHaveValue('CREDIT'))
    await waitFor(() => expect(within(dialog).getByLabelText('Término del crédito')).toHaveValue('NET15'))
    expect(within(dialog).queryByText('Cambió la forma de pago')).not.toBeInTheDocument()
  })

  it('lote 234: «Nueva orden» ofrece los kits de oferta del catálogo del cliente (los mismos del pedido guiado), al precio de la oferta, y no pide «cualquier» kit de oferta', async () => {
    mock.client = { id: 1, publicId: CLIENT, name: 'Farmacia Central', paymentTerm: 'NET15', requireOrderSignature: false }
    const KIT = '66666666-0000-0000-0000-000000000006'
    mock.catalog = {
      brands: [
        {
          brand: 'PRODIGY',
          offers: [
            { productPublicId: KIT, sku: 'OFERTA1', name: 'Oferta 1', brand: 'PRODIGY', isKit: true, isOfferKit: true, isPromotion: true, price: 41.94, offerPrice: 41.94, priceSource: 'OFFER' },
            // un producto normal en oferta no se duplica: ya sale en la lista de productos
            { productPublicId: PROD, sku: 'MED', name: 'Medidor', isKit: false, isOfferKit: false, isPromotion: true, price: 9, priceSource: 'OFFER' },
          ],
          products: [],
        },
      ],
    }
    const user = userEvent.setup()
    wrap(null)
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden' })
    // sin cliente no se ofrece ningún kit de oferta (no se sabe cuáles le aplican)
    await user.click(within(dialog).getByRole('combobox', { name: 'Producto de la línea 1' }))
    expect(await within(dialog).findByRole('option', { name: /MED/ })).toBeInTheDocument()
    expect(within(dialog).queryByRole('option', { name: /OFERTA1/ })).toBeNull()
    expect(sent('GET', '/api/v1/sales-orders/catalog')).toHaveLength(0)

    await user.click(within(dialog).getByLabelText('Cliente que compra'))
    await user.click(await screen.findByText(/Farmacia Central/))
    await waitFor(() => expect(sent('GET', '/api/v1/sales-orders/catalog')).toHaveLength(1))
    expect(sent('GET', '/api/v1/sales-orders/catalog')[0].search?.get('clientPublicId')).toBe(CLIENT)
    await user.click(within(dialog).getByRole('combobox', { name: 'Producto de la línea 1' }))
    // solo las opciones del buscador de productos («SKU · nombre»); la ventana tiene otros desplegables con sus opciones
    const options = (await within(dialog).findAllByRole('option')).filter((o) => (o.textContent ?? '').includes(' · '))
    expect(options.map((o) => o.textContent)).toEqual(['OFERTA1 · Oferta 1', 'MED · Medidor'])
    await user.click(options[0])
    // el precio que se propone es el de la oferta
    expect(within(dialog).getByLabelText('Precio de la línea 1')).toHaveAttribute('placeholder', expect.stringContaining('41.94'))
    // el buscador de productos ya no pide los kits de oferta al servidor
    expect(sent('GET', '/api/v1/products').every((r) => !r.search?.has('includeOfferKits'))).toBe(true)
  })
})
