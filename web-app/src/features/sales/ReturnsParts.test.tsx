// Pruebas de «Devoluciones de venta» (Lote 82) sobre un fetch simulado: la lista, la ficha por estatus y permiso, pedir (con ventana), recibir y acreditar.
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
  status: 'REQUESTED',
  outside: false,
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
    if (m === 'GET' && p === '/api/v1/sales-orders') return json({ items: [ORDER_ITEM], total: 1, skip: 0, take: 15 })
    if (m === 'GET' && p === '/api/v1/sales-returns/settings') return json({ windowDays: 30 })
    if (m === 'PUT' && p === '/api/v1/sales-returns/settings') return json({ windowDays: (body as { windowDays: number }).windowDays })
    if (m === 'GET' && p === '/api/v1/sales-returns') return json({ items: [ITEM()], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === '/api/v1/sales-returns/returnable') return json(RETURNABLE())
    if (m === 'GET' && p === `/api/v1/sales-returns/${RET}`) return json(DTO())
    if (m === 'POST' && p === '/api/v1/sales-returns') return json({ ...DTO(), publicId: RET })
    if (m === 'POST' && p === `/api/v1/sales-returns/${RET}/authorize`) {
      mock.status = 'AUTHORIZED'
      return json(DTO())
    }
    if (m === 'POST' && p === `/api/v1/sales-returns/${RET}/reject`) {
      mock.status = 'REJECTED'
      return json(DTO())
    }
    if (m === 'POST' && p === `/api/v1/sales-returns/${RET}/receive`) {
      mock.status = 'RECEIVED'
      return json(DTO())
    }
    if (m === 'POST' && p === `/api/v1/sales-returns/${RET}/credit`) {
      mock.status = 'CREDITED'
      return json(DTO())
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const RET = '44444444-0000-0000-0000-000000000004'
const ORD = '55555555-0000-0000-0000-000000000005'
const LABEL: Record<string, string> = { REQUESTED: 'Solicitada', AUTHORIZED: 'Autorizada', RECEIVED: 'Recibida', CREDITED: 'Acreditada', REJECTED: 'Rechazada' }
const ORDER_ITEM = { publicId: ORD, number: 'OV-00007', status: 'SHIPPED', statusLabel: 'Despachada', clientName: 'Farmacia Central', requestedDate: '2026-10-01', total: 100, lineCount: 1 }
const ITEM = () => ({
  publicId: RET, number: 'DV-00001', status: mock.status, statusLabel: LABEL[mock.status], statusColor: '#3B82F6', orderNumber: 'OV-00007', clientName: 'Farmacia Central',
  reasonLabel: 'Producto dañado', qtyRequested: 3, qtyReceived: mock.status === 'RECEIVED' ? 3 : 0, outsideWindow: false, requestedAtUtc: '2026-10-16T12:00:00Z',
})
const DTO = () => ({
  ...ITEM(), warehouseName: 'Principal', note: 'Cajas rotas', authorizationNote: null, authorizedAtUtc: null, receipts: [], creditNotes: [],
  lines: [{ id: 11, sku: 'MED-1', productName: 'Medidor', qtyRequested: 3, qtyReceived: mock.status === 'RECEIVED' ? 3 : 0, qtyDamaged: 0, qtyCredited: 0, pending: mock.status === 'RECEIVED' ? 0 : 3 }],
})
const RETURNABLE = () => ({
  orderNumber: 'OV-00007', windowDays: 30, outsideWindow: mock.outside, lastDispatchDate: '2026-08-01',
  lines: [
    { lineId: 11, sku: 'MED-1', productName: 'Medidor', shipped: 8, returned: 0, pending: 0, returnable: 8, isKit: false, isSerial: false },
    { lineId: 12, sku: 'KIT-1', productName: 'Kit', shipped: 1, returned: 0, pending: 0, returnable: 1, isKit: true, isSerial: false },
  ],
})

const ALL = ['sales.view', 'sales.return', 'sales.return.authorize', 'warehouse.receive', 'billing.credit']
const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(ALL),
  modules: new Set(['SALES_ORDERS']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(permissions: string[] = ALL) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={new Set(permissions)} modules={SESSION.modules}>
          <MemoryRouter>
            <SalesOrdersScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

async function openReturn(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('tab', { name: 'Devoluciones' }))
  await user.click(await screen.findByText('DV-00001'))
  return screen.findByRole('dialog', { name: /Devolución DV-00001/ })
}

describe('Devoluciones de venta', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.status = 'REQUESTED'
    mock.outside = false
  })

  it('la pestaña lista las devoluciones y filtra por estatus', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Devoluciones' }))
    expect(await screen.findByText('DV-00001')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Filtrar por estatus'), 'AUTHORIZED')
    await waitFor(() => expect(sent('GET', '/api/v1/sales-returns').some((r) => r.search.includes('status=AUTHORIZED'))).toBe(true))
  })

  it('una solicitada se autoriza con una nota y luego se recibe lo bueno y lo dañado', async () => {
    const user = userEvent.setup()
    wrap()
    let dialog = await openReturn(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Autorizar' }))
    const confirm = await screen.findByRole('dialog', { name: /Autorizar DV-00001/ })
    await user.type(within(confirm).getByLabelText('Nota de la autorización'), 'La recoge el chofer')
    await user.click(within(confirm).getByRole('button', { name: 'Autorizar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/sales-returns/${RET}/authorize`)[0]?.body).toEqual({ note: 'La recoge el chofer' }))

    dialog = await screen.findByRole('dialog', { name: /Devolución DV-00001/ })
    await user.click(await within(dialog).findByRole('button', { name: 'Recibir' }))
    const rec = await screen.findByRole('dialog', { name: /Recibir DV-00001/ })
    await user.type(within(rec).getByLabelText('Bueno de MED-1 Medidor'), '2')
    await user.type(within(rec).getByLabelText('Dañado de MED-1 Medidor'), '1')
    await user.click(within(rec).getByRole('button', { name: 'Recibir' }))
    expect(await within(rec).findByText('Describe qué le pasó a lo dañado.')).toBeInTheDocument()
    await user.type(within(rec).getByLabelText('Descripción del daño de MED-1 Medidor'), 'Caja rota')
    await user.click(within(rec).getByRole('button', { name: 'Recibir' }))
    await waitFor(() => expect(sent('POST', `/api/v1/sales-returns/${RET}/receive`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/sales-returns/${RET}/receive`)[0].body).toMatchObject({ lines: [{ lineId: 11, goodQty: 2, damagedQty: 1, damageNote: 'Caja rota' }] })
  })

  it('rechazar pide el motivo', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openReturn(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Rechazar' }))
    const confirm = await screen.findByRole('dialog', { name: /Rechazar DV-00001/ })
    await user.click(within(confirm).getByRole('button', { name: 'Rechazar' }))
    expect(await within(confirm).findByText('Escribe el motivo.')).toBeInTheDocument()
    expect(sent('POST', `/api/v1/sales-returns/${RET}/reject`)).toHaveLength(0)
    await user.type(within(confirm).getByLabelText('Motivo del rechazo'), 'Fuera de política')
    await user.click(within(confirm).getByRole('button', { name: 'Rechazar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/sales-returns/${RET}/reject`)[0]?.body).toEqual({ reason: 'Fuera de política' }))
  })

  it('una recibida se acredita con billing.credit y sin ese permiso no aparece el botón', async () => {
    mock.status = 'RECEIVED'
    const user = userEvent.setup()
    const { unmount } = wrap(ALL.filter((p) => p !== 'billing.credit'))
    let dialog = await openReturn(user)
    expect(within(dialog).queryByRole('button', { name: 'Acreditar' })).toBeNull()
    unmount()
    wrap()
    dialog = await openReturn(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Acreditar' }))
    const confirm = await screen.findByRole('dialog', { name: /Acreditar DV-00001/ })
    await user.click(within(confirm).getByRole('button', { name: 'Acreditar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/sales-returns/${RET}/credit`)).toHaveLength(1))
  })

  it('pedir: elige la orden, escribe la cantidad y no deja pasar de lo devolvible; el kit no se devuelve', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Devoluciones' }))
    await user.click(await screen.findByRole('button', { name: 'Pedir devolución' }))
    const dialog = await screen.findByRole('dialog', { name: 'Pedir una devolución' })
    await user.click(await within(dialog).findByRole('button', { name: /OV-00007/ }))
    const qty = await within(dialog).findByLabelText('Cantidad a devolver de MED-1')
    expect(within(dialog).getByLabelText('Cantidad a devolver de KIT-1')).toBeDisabled()
    await user.selectOptions(within(dialog).getByLabelText('Motivo'), 'DAMAGED')
    await user.type(qty, '9')
    await user.click(within(dialog).getByRole('button', { name: 'Pedir devolución' }))
    expect(await within(dialog).findByText('Una cantidad pasa de lo que se puede devolver.')).toBeInTheDocument()
    await user.clear(qty)
    await user.type(qty, '3')
    await user.click(within(dialog).getByRole('button', { name: 'Pedir devolución' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-returns')).toHaveLength(1))
    expect(sent('POST', '/api/v1/sales-returns')[0].body).toMatchObject({ salesOrderPublicId: ORD, reason: 'DAMAGED', lines: [{ salesOrderLineId: 11, quantity: 3 }] })
  })

  it('fuera de la ventana pide la confirmación a quien autoriza; sin ese permiso no puede', async () => {
    mock.outside = true
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Devoluciones' }))
    await user.click(await screen.findByRole('button', { name: 'Pedir devolución' }))
    const dialog = await screen.findByRole('dialog', { name: 'Pedir una devolución' })
    await user.click(await within(dialog).findByRole('button', { name: /OV-00007/ }))
    await user.selectOptions(await within(dialog).findByLabelText('Motivo'), 'OTHER')
    await user.type(within(dialog).getByLabelText('Cantidad a devolver de MED-1'), '1')
    await user.click(within(dialog).getByRole('button', { name: 'Pedir devolución' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('la debe confirmar quien autoriza')
    expect(sent('POST', '/api/v1/sales-returns')).toHaveLength(0)
    await user.click(within(dialog).getByLabelText('Confirmo recibirla fuera de la ventana'))
    await user.click(within(dialog).getByRole('button', { name: 'Pedir devolución' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-returns')[0]?.body).toMatchObject({ confirmOutsideWindow: true }))
  })

  it('la ventana se cambia con sales.return.authorize y se valida', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Devoluciones' }))
    const input = await screen.findByLabelText(/Ventana de devolución/)
    await user.clear(input)
    await user.type(input, '4000')
    await user.click(screen.getByRole('button', { name: 'Guardar ventana' }))
    expect(await screen.findByText('Escribe un número entero de 0 a 3650.')).toBeInTheDocument()
    await user.clear(input)
    await user.type(input, '45')
    await user.click(screen.getByRole('button', { name: 'Guardar ventana' }))
    await waitFor(() => expect(sent('PUT', '/api/v1/sales-returns/settings')[0]?.body).toEqual({ windowDays: 45 }))
  })
})
