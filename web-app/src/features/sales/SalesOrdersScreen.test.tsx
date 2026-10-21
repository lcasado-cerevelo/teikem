// Pruebas de «Órdenes de venta» (Lote 60) sobre un fetch simulado: la lista con su filtro, armar un borrador y solicitarlo (diálogo de faltante con
// «reservar lo disponible»), la orden solicitada (cambiar cantidad, cancelar) y los backorders (surtir todo).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import SalesOrdersScreen from './SalesOrdersScreen'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  availability: null as unknown,
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    const m = req.method
    if (m === 'GET' && p === '/api/v1/sales-orders') return json({ items: [DRAFT_ITEM, REQUESTED_ITEM], total: 2, skip: 0, take: 25 })
    if (m === 'GET' && p === '/api/v1/sales-orders/backorders') return json([BACKORDER])
    if (m === 'POST' && p === '/api/v1/sales-orders/backorders/fulfill') return json({ lines: [{ orderPublicId: REQ, orderNumber: 'OV-00002', lineId: 20, sku: 'MED', reserved: 3, stillPending: 0 }], totalReserved: 3, linesCompleted: 1 })
    if (m === 'GET' && p === `/api/v1/sales-orders/${DRAFT}`) return json(DRAFT_ORDER)
    if (m === 'GET' && p === `/api/v1/sales-orders/${REQ}`) return json(REQUESTED_ORDER)
    if (m === 'POST' && p === '/api/v1/sales-orders') return json({ ...DRAFT_ORDER, number: 'OV-00003' })
    if (m === 'PATCH' && p === `/api/v1/sales-orders/${DRAFT}`) return json(DRAFT_ORDER)
    if (m === 'GET' && p === `/api/v1/sales-orders/${DRAFT}/availability`) return json(mock.availability)
    if (m === 'POST' && p === `/api/v1/sales-orders/${DRAFT}/request`) return json({ ...DRAFT_ORDER, status: 'REQUESTED' })
    if (m === 'PATCH' && p === `/api/v1/sales-orders/${REQ}/lines/20`) return json(REQUESTED_ORDER)
    if (m === 'POST' && p === `/api/v1/sales-orders/${REQ}/dispatch`) return json({ pickBatchPublicId: BATCH, pickBatchNumber: 'EMP-00009', order: REQUESTED_ORDER })
    if (m === 'POST' && p === `/api/v1/sales-orders/${REQ}/cancel`) return json({ ...REQUESTED_ORDER, status: 'CANCELLED' })
    if (m === 'GET' && p === '/api/v1/clients') return json([{ id: 1, publicId: CLIENT, code: 'FARM', name: 'Farmacia Central', isActive: true }])
    if (m === 'GET' && p === '/api/v1/products') return json({ items: [{ id: 1, publicId: PROD, sku: 'MED', name: 'Medidor', isActive: true, isOwn: true, salePrice: 12.5 }], total: 1, skip: 0, take: 50 })
    if (m === 'GET' && p === '/api/v1/warehouses') return json([{ id: 1, publicId: WH, code: 'ALM-01', name: 'Principal', isActive: true }])
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
const BATCH = '66666666-0000-0000-0000-000000000006'

const DRAFT_ITEM = { publicId: DRAFT, number: 'OV-00001', status: 'DRAFT', statusLabel: 'Borrador', statusColor: '#9CA3AF', clientPublicId: CLIENT, clientName: 'Farmacia Central', warehousePublicId: WH, warehouseName: 'Principal', requestedDate: '2026-10-15', requiredDate: null, subtotal: 50, taxAmount: 5.75, total: 55.75, lineCount: 1, hasBackorder: false, requestedByName: 'Ana', createdAtUtc: '2026-10-15T12:00:00Z' }
const REQUESTED_ITEM = { ...DRAFT_ITEM, publicId: REQ, number: 'OV-00002', status: 'REQUESTED', statusLabel: 'Solicitada', statusColor: '#3B82F6', hasBackorder: true }
const LINE = { id: 10, lineNo: 1, productPublicId: PROD, sku: 'MED', productName: 'Medidor', lotId: null, qtyOrdered: 4, qtyReserved: 0, qtyBackordered: 0, qtyShipped: 0, isClosed: false, unitPrice: 12.5, lineSubtotal: 50, taxRatePct: 11.5, taxAmount: 5.75, isTaxExempt: false }
const DRAFT_ORDER = { ...DRAFT_ITEM, consigneePublicId: null, consigneeName: null, notes: null, requestedAtUtc: null, lines: [LINE] }
const REQUESTED_ORDER = { ...REQUESTED_ITEM, consigneePublicId: null, consigneeName: null, notes: 'Entregar el viernes', requestedAtUtc: '2026-10-15T13:00:00Z', lines: [{ ...LINE, id: 20, qtyOrdered: 10, qtyReserved: 7, qtyBackordered: 3 }], dispatches: [{ pickBatchPublicId: BATCH, number: 'EMP-00001', status: 'COLLECTED', collectedAtUtc: '2026-10-15T14:00:00Z', quantity: 2, transportOrderPublicId: null, transportOrderNumber: null }] }
const BACKORDER = { orderPublicId: REQ, orderNumber: 'OV-00002', orderStatus: 'REQUESTED', clientName: 'Farmacia Central', warehouseName: 'Principal', lineId: 20, lineNo: 1, productPublicId: PROD, sku: 'MED', productName: 'Medidor', ordered: 10, reserved: 7, pending: 3, available: 5, fulfillable: 3, requestedAtUtc: '2026-10-10T12:00:00Z', daysWaiting: 5 }

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
  permissions: new Set(['sales.view', 'sales.create', 'sales.cancel', 'sales.backorder', 'warehouse.pick', 'orders.create', 'orders.credit_override', 'clients.read', 'inventory.view', 'warehouse.manage']),
  modules: new Set(['SALES_ORDERS', 'CATALOG', 'WMS_LOTSERIAL']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(initial = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={SESSION.permissions} modules={SESSION.modules}>
          <MemoryRouter initialEntries={[initial]}>
            <SalesOrdersScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

describe('SalesOrdersScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.availability = { lines: [{ lineNo: 1, productPublicId: PROD, sku: 'MED', productName: 'Medidor', ordered: 4, available: 6, shortage: 0 }], hasShortage: false, credit: { limit: null, exposure: 0, newAmount: 55.75, available: null, exceeded: false, message: null } }
  })

  it('lista las órdenes con su estatus y la marca de backorder, y filtra por estatus en el servidor', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByRole('heading', { level: 1, name: 'Órdenes de venta' })).toBeInTheDocument()
    expect(await screen.findByText('OV-00001')).toBeInTheDocument()
    expect(screen.getByText('OV-00002')).toBeInTheDocument()
    expect(screen.getByText('Con backorder')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Filtrar por estatus'), 'REQUESTED')
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/sales-orders' && r.search.includes('status=REQUESTED'))).toBe(true))
  })

  it('Lote 195: las acciones de asignar están en la tabla como iconos (solo en la orden solicitada) y el interruptor filtra «asignadas a mí»', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('OV-00002')
    expect(screen.getAllByRole('button', { name: 'Tomar' })).toHaveLength(1)   // el borrador no se asigna
    expect(screen.getAllByRole('button', { name: 'Asignar' })).toHaveLength(1)
    await user.click(screen.getByRole('switch', { name: 'Solo los asignados a mí' }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/sales-orders' && r.search.includes('assignedToMe=true'))).toBe(true))
  })

  it('el aviso del Pulso abre la orden directo con ?order= (Lote 67)', async () => {
    wrap(`/sales-orders?order=${DRAFT}`)
    expect(await screen.findByRole('dialog', { name: /Orden OV-00001/ })).toBeInTheDocument()
  })

  it('un borrador se abre para editar y «Solicitar…» comprueba el inventario: con faltante ofrece reservar lo disponible y dejar el resto en backorder', async () => {
    mock.availability = {
      lines: [{ lineNo: 1, productPublicId: PROD, sku: 'MED', productName: 'Medidor', ordered: 10, available: 6, shortage: 4 }],
      hasShortage: true,
      credit: { limit: null, exposure: 0, newAmount: 55.75, available: null, exceeded: false, message: null },
    }
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('OV-00001'))
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00001/ })
    expect(within(dialog).getByLabelText('Cantidad de la línea 1')).toHaveValue('4')
    await user.click(within(dialog).getByRole('button', { name: 'Solicitar…' }))
    await waitFor(() => expect(sent('PATCH', `/api/v1/sales-orders/${DRAFT}`)).toHaveLength(1))
    const check = await screen.findByRole('dialog', { name: /Solicitar la orden/ })
    expect(within(check).getByText('MED')).toBeInTheDocument()
    expect(within(check).getByText('No alcanza el inventario para todo lo pedido. Nada se ha reservado todavía.')).toBeInTheDocument()
    await user.click(within(check).getByRole('button', { name: 'Reservar lo disponible y dejar el resto en backorder' }))
    await waitFor(() => expect(sent('POST', `/api/v1/sales-orders/${DRAFT}/request`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/sales-orders/${DRAFT}/request`)[0].body).toEqual({ allowBackorder: true })
  })

  it('sin faltante el diálogo confirma y solicita sin backorder; con el crédito excedido pide autorizarlo', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('OV-00001'))
    let dialog = await screen.findByRole('dialog', { name: /Orden OV-00001/ })
    await user.click(within(dialog).getByRole('button', { name: 'Solicitar…' }))
    let check = await screen.findByRole('dialog', { name: /Solicitar la orden/ })
    expect(within(check).getByText(/Hay inventario para todo lo pedido/)).toBeInTheDocument()
    await user.click(within(check).getByRole('button', { name: 'Solicitar y reservar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/sales-orders/${DRAFT}/request`)[0]?.body).toEqual({ allowBackorder: false }))

    mock.requests.length = 0
    mock.availability = { lines: [], hasShortage: false, credit: { limit: 100, exposure: 60, newAmount: 55.75, available: 40, exceeded: true, message: 'El cliente excede su límite de crédito: límite 100.00, en curso 60.00, esta orden 55.75.' } }
    await user.click(await screen.findByText('OV-00001'))
    dialog = await screen.findByRole('dialog', { name: /Orden OV-00001/ })
    await user.click(within(dialog).getByRole('button', { name: 'Solicitar…' }))
    check = await screen.findByRole('dialog', { name: /Solicitar la orden/ })
    expect(within(check).getByText(/El cliente excede su límite de crédito/)).toBeInTheDocument()
    const confirm = within(check).getByRole('button', { name: 'Solicitar y reservar' })
    expect(confirm).toBeDisabled()
    await user.click(within(check).getByRole('switch', { name: 'Autorizar el crédito excedido' }))
    expect(confirm).toBeEnabled()
    await user.click(confirm)
    await waitFor(() => expect(sent('POST', `/api/v1/sales-orders/${DRAFT}/request`)[0]?.body).toEqual({ allowBackorder: false, overrideCredit: true }))
  })

  it('una orden solicitada muestra lo reservado y lo pendiente, deja cambiar la cantidad de una línea y cancelar', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('OV-00002'))
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00002/ })
    expect(within(dialog).getByText('Entregar el viernes')).toBeInTheDocument()
    const row = within(dialog).getByText('MED').closest('tr, li') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Cambiar cantidad' }))
    const qty = await screen.findByRole('dialog', { name: /Cambiar la cantidad de MED/ })
    const input = within(qty).getByLabelText('Cantidad pedida')
    await user.clear(input)
    await user.type(input, '12')
    await user.click(within(qty).getByRole('switch'))
    await user.click(within(qty).getByRole('button', { name: 'Guardar cantidad' }))
    await waitFor(() => expect(sent('PATCH', `/api/v1/sales-orders/${REQ}/lines/20`)[0]?.body).toEqual({ quantity: 12, allowBackorder: true }))

    await user.click(within(dialog).getByRole('button', { name: 'Cancelar orden' }))
    const confirm = await screen.findByRole('dialog', { name: /Cancelar la orden OV-00002/ })
    await user.click(within(confirm).getByRole('button', { name: 'Cancelar orden' }))
    await waitFor(() => expect(sent('POST', `/api/v1/sales-orders/${REQ}/cancel`)).toHaveLength(1))
  })

  it('despachar: por partes (la cantidad por línea), luego «¿qué sigue?» y la entrega con el mismo formulario del empaque', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('OV-00002'))
    const dialog = await screen.findByRole('dialog', { name: /Orden OV-00002/ })
    expect(within(dialog).getByText('EMP-00001')).toBeInTheDocument()   // la lista de despachos de la orden
    expect(within(dialog).getAllByText('Despachado').length).toBeGreaterThan(1)   // el encabezado de la columna y el estatus del despacho
    await user.click(within(dialog).getByRole('button', { name: 'Despachar' }))
    const modal = await screen.findByRole('dialog', { name: /Despachar la orden OV-00002/ })
    const input = within(modal).getByLabelText('Cantidad a despachar de MED')
    expect(input).toHaveValue('7')
    await user.clear(input)
    await user.type(input, '9')
    await user.click(within(modal).getByRole('button', { name: 'Despachar' }))
    expect(await within(modal).findByText('No se puede despachar más de lo reservado: MED tiene 7 reservadas.')).toBeInTheDocument()
    expect(sent('POST', `/api/v1/sales-orders/${REQ}/dispatch`)).toHaveLength(0)
    await user.clear(input)
    await user.type(input, '3')
    await user.click(within(modal).getByRole('button', { name: 'Despachar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/sales-orders/${REQ}/dispatch`)[0]?.body).toEqual({ lines: [{ lineId: 20, quantity: 3 }] }))
    const next = await screen.findByRole('dialog', { name: /Despacho EMP-00009/ })
    await user.click(within(next).getByRole('button', { name: /Entrega: crear la orden de transporte/ }))
    expect(await screen.findByRole('dialog', { name: /Empacar EMP-00009/ })).toBeInTheDocument()
  })

  it('backorders: la vista y «Surtir todo»', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Backorders' }))
    expect(await screen.findByText('OV-00002')).toBeInTheDocument()
    expect(screen.getByText('5 d')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Surtir todo' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-orders/backorders/fulfill')).toHaveLength(1))
  })
})
