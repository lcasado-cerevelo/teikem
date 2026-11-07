// Lote 225 (P5) — pestaña QuickBooks de Órdenes de venta sobre un fetch simulado: solo con los permisos de QuickBooks, la lista con «cliente nuevo» / «producto nuevo»
// y «ver datos», el aviso «N de estas ya salieron el …», exportar (pregunta de los nuevos, baja el .iif decodificando el base64, «¿Ya importaste estas N?» con la lista
// marcada y confirmar solo las que siguen marcadas), marcar una, varias o todas, y el histórico (quién y cuándo, quitar la marca). Más las reglas puras.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import SalesOrdersScreen from './SalesOrdersScreen'
import { base64ToBytes, filterPending, markBatches, newDataOf } from './quickBooksRules'

const A = 'aaaaaaaa-0000-0000-0000-000000000001'
const B = 'bbbbbbbb-0000-0000-0000-000000000002'
const CLIENT_NEW = 'cccccccc-0000-0000-0000-000000000003'
const CLIENT_OLD = 'dddddddd-0000-0000-0000-000000000004'
const PROD = 'eeeeeeee-0000-0000-0000-000000000005'

// «Ñ» en Windows-1252 = 0xD1: el archivo se baja con los bytes exactos, sin pasar por texto.
const IIF_BYTES = [0x21, 0x54, 0x52, 0x4e, 0x53, 0x09, 0xd1, 0x0d, 0x0a]
const IIF_B64 = btoa(String.fromCharCode(...IIF_BYTES))

const INV_A = { publicId: A, number: 'FAC-00010', issueDate: '2026-11-02', dueDate: '2026-12-02', status: 'ISSUED', clientPublicId: CLIENT_NEW, clientCode: 'FARM9', clientName: 'Farmacia Nueva', quickBooksCustomer: 'Farmacia Nueva', subtotal: 100, taxAmount: 11.5, total: 111.5, orderNumbers: ['OV-00021'], exportedAtUtc: '2026-11-05T14:00:00', newClient: true, newProductSkus: ['NECH-1001'] }
const INV_B = { publicId: B, number: 'FAC-00011', issueDate: '2026-11-03', dueDate: null, status: 'ISSUED', clientPublicId: CLIENT_OLD, clientCode: 'FARM1', clientName: 'Farmacia Central', quickBooksCustomer: 'FARMACIA CENTRAL INC', subtotal: 50, taxAmount: 0, total: 50, orderNumbers: ['OV-00022'], exportedAtUtc: null, newClient: false, newProductSkus: [] }
const PENDING = {
  count: 2,
  total: 161.5,
  alreadyExportedCount: 1,
  lastExportedAtUtc: '2026-11-05T14:00:00',
  invoices: [INV_A, INV_B],
  newClients: [{ publicId: CLIENT_NEW, code: 'FARM9', quickBooksName: 'Farmacia Nueva', companyName: 'Farmacia Nueva LLC', billTo: ['PO Box 1', 'San Juan PR 00901'], shipTo: ['Calle 1'], phone1: '(787) 555-0101', phone2: null, email: 'compras@nueva.test', contact: 'Ana', paymentTerm: 'NET30', quickBooksTerms: 'Net 30', merchantRegistration: 'SURI-123', creditLimit: 500 }],
  newProducts: [{ publicId: PROD, sku: 'NECH-1001', quickBooksName: 'NECH-1001', name: 'Guantes de nitrilo', price: 12.5, cost: 7, taxable: true }],
  warnings: ['El término «COD» no tiene nombre en QuickBooks (Ajustes → Operación → QuickBooks): esas facturas salen sin término.'],
}
const HISTORY_ITEM = { publicId: A, number: 'FAC-00009', issueDate: '2026-10-30', clientCode: 'FARM1', clientName: 'Farmacia Central', total: 75, quickBooksAtUtc: '2026-11-04T15:00:00', quickBooksBy: 3, quickBooksByName: 'Marta Oficina', exportedAtUtc: '2026-11-04T14:00:00' }

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; search: string; body: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    const m = req.method
    if (m === 'GET' && p === '/api/v1/sales-orders/quickbooks') return json(PENDING)
    if (m === 'POST' && p === '/api/v1/sales-orders/quickbooks/export')
      return json({ fileName: 'teikem-quickbooks-20261107-0930.iif', contentType: 'application/octet-stream', contentBase64: IIF_B64, exportedAtUtc: '2026-11-07T13:30:00', invoiceCount: 2, total: 161.5, alreadyExportedCount: 1, lastExportedAtUtc: '2026-11-05T14:00:00', includedNew: (body as { includeNew: boolean }).includeNew, newClientCount: 1, newProductCount: 1, invoices: [INV_A, INV_B] })
    if (m === 'POST' && (p === '/api/v1/sales-orders/quickbooks/confirm' || p === '/api/v1/sales-orders/quickbooks/mark'))
      return json({ marked: (body as { all?: boolean; invoiceIds?: string[] }).all ? 2 : ((body as { invoiceIds?: string[] }).invoiceIds ?? []).length, alreadyMarked: 0, clientsSent: 0, productsSent: 0, atUtc: '2026-11-07T13:31:00' })
    if (m === 'POST' && p === '/api/v1/sales-orders/quickbooks/unmark') return json({ marked: 1, alreadyMarked: 0, clientsSent: 0, productsSent: 0, atUtc: '2026-11-07T13:32:00' })
    if (m === 'GET' && p === '/api/v1/sales-orders/quickbooks/history') return json({ items: [HISTORY_ITEM], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === '/api/v1/sales-orders') return json({ items: [], total: 0, skip: 0, take: 25 })
    if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return json([])
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const BASE = ['sales.view']
const ALL_QB = [...BASE, 'sales.quickbooks.view', 'sales.quickbooks.mark', 'sales.quickbooks.export']

function wrap(perms: string[], initial = '/sales-orders?tab=quickbooks') {
  const session = { me: { userId: 9, tenantName: 'Advance Solutions' }, isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', permissions: new Set(perms), modules: new Set(['SALES_ORDERS']) } as unknown as Session
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={session}>
        <AccessProvider permissions={perms} modules={['SALES_ORDERS']}>
          <MemoryRouter initialEntries={[initial]}>
            <SalesOrdersScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)
const blobs: Blob[] = []

describe('pestaña QuickBooks (Lote 225)', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    blobs.length = 0
    Object.assign(URL, { createObjectURL: (b: Blob) => (blobs.push(b), 'blob:qb'), revokeObjectURL: () => {} })
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  })
  afterEach(() => vi.restoreAllMocks())

  it('sin permisos de QuickBooks no hay pestaña y ?tab=quickbooks cae en Órdenes', async () => {
    wrap(BASE)
    expect(await screen.findByRole('tab', { name: 'Órdenes' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('tab', { name: 'QuickBooks' })).not.toBeInTheDocument()
    expect(sent('GET', '/api/v1/sales-orders/quickbooks')).toHaveLength(0)
  })

  it('con solo ver: la lista, el aviso de las que ya salieron, los avisos y «ver datos»; sin Exportar ni Marcar', async () => {
    const user = userEvent.setup()
    wrap([...BASE, 'sales.quickbooks.view'])
    expect(await screen.findByRole('tab', { name: 'QuickBooks' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByText('FAC-00010')).toBeInTheDocument()
    expect(screen.getByText('FAC-00011')).toBeInTheDocument()
    expect(screen.getByText('En QuickBooks: FARMACIA CENTRAL INC')).toBeInTheDocument()
    expect(screen.getByText('Cliente nuevo')).toBeInTheDocument()
    expect(screen.getByText('Producto nuevo')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('1 de estas ya salió el 11/05/2026')
    expect(screen.getByText(/El término «COD» no tiene nombre en QuickBooks/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Exportar a QuickBooks' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Marcar todas' })).not.toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: 'Elegir la factura FAC-00010' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Ver los datos nuevos de la factura FAC-00010' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Datos nuevos para QuickBooks · FAC-00010')).toBeInTheDocument()
    expect(within(dialog).getByText('Farmacia Nueva LLC')).toBeInTheDocument()
    expect(within(dialog).getByText('PO Box 1, San Juan PR 00901')).toBeInTheDocument()
    expect(within(dialog).getByText('Net 30')).toBeInTheDocument()
    expect(within(dialog).getByText('SURI-123')).toBeInTheDocument()
    expect(within(dialog).getByText('NECH-1001')).toBeInTheDocument()
    expect(within(dialog).getByText('Guantes de nitrilo')).toBeInTheDocument()
  })

  it('exportar: pregunta por los nuevos, baja el .iif con los bytes exactos y confirma solo las que siguen marcadas', async () => {
    const user = userEvent.setup()
    wrap(ALL_QB)
    await screen.findByText('FAC-00010')
    await user.click(screen.getByRole('button', { name: 'Exportar a QuickBooks' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Salen todas las facturas por pasar: 2/)).toBeInTheDocument()
    const include = within(dialog).getByRole('checkbox', { name: 'Incluir los clientes y productos nuevos' })
    expect(include).toBeChecked()
    await user.click(include)
    await user.click(within(dialog).getByRole('button', { name: 'Exportar y bajar el archivo' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-orders/quickbooks/export')).toHaveLength(1))
    expect(sent('POST', '/api/v1/sales-orders/quickbooks/export')[0].body).toEqual({ includeNew: false })

    // el archivo: bytes exactos (Windows-1252) y su nombre
    await waitFor(() => expect(blobs).toHaveLength(1))
    expect([...new Uint8Array(await blobs[0].arrayBuffer())]).toEqual(IIF_BYTES)
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled()

    // «¿Ya importaste estas 2?» con la lista marcada; se desmarca una
    expect(await screen.findByText('¿Ya importaste estas 2?')).toBeInTheDocument()
    const list = screen.getByRole('list', { name: 'Facturas del archivo' })
    const boxes = within(list).getAllByRole('checkbox')
    expect(boxes).toHaveLength(2)
    boxes.forEach((b) => expect(b).toBeChecked())
    await user.click(boxes[1])
    await user.click(screen.getByRole('button', { name: 'Sí, ya están (1)' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-orders/quickbooks/confirm')).toHaveLength(1))
    expect(sent('POST', '/api/v1/sales-orders/quickbooks/confirm')[0].body).toEqual({ invoiceIds: [A] })
    await waitFor(() => expect(screen.queryByText('¿Ya importaste estas 2?')).not.toBeInTheDocument())
  })

  it('marcar una (la casilla), y todas (con confirmación)', async () => {
    const user = userEvent.setup()
    wrap([...BASE, 'sales.quickbooks.mark'])
    await screen.findByText('FAC-00011')
    expect(screen.queryByRole('button', { name: 'Exportar a QuickBooks' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Marcar como en QuickBooks (0)' })).toBeDisabled()
    await user.click(screen.getByRole('checkbox', { name: 'Elegir la factura FAC-00011' }))
    await user.click(screen.getByRole('button', { name: 'Marcar como en QuickBooks (1)' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-orders/quickbooks/mark')).toHaveLength(1))
    expect(sent('POST', '/api/v1/sales-orders/quickbooks/mark')[0].body).toEqual({ invoiceIds: [B], all: false })

    await user.click(screen.getByRole('button', { name: 'Marcar todas' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Las 2 facturas por pasar quedan como ya pasadas/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Marcar todas' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-orders/quickbooks/mark')).toHaveLength(2))
    expect(sent('POST', '/api/v1/sales-orders/quickbooks/mark')[1].body).toEqual({ all: true })
  })

  it('«Ver las ya pasadas»: quién y cuándo, y quitar la marca', async () => {
    const user = userEvent.setup()
    wrap(ALL_QB)
    await screen.findByText('FAC-00010')
    await user.click(screen.getByRole('button', { name: 'Ver las ya pasadas' }))
    expect(await screen.findByText('FAC-00009')).toBeInTheDocument()
    expect(screen.getByText('Marta Oficina')).toBeInTheDocument()
    const get = sent('GET', '/api/v1/sales-orders/quickbooks/history')
    expect(get[0].search).toContain('skip=0')
    expect(get[0].search).toContain('take=25')
    await user.click(screen.getByRole('button', { name: 'Quitar la marca' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Quitar la marca' }))
    await waitFor(() => expect(sent('POST', '/api/v1/sales-orders/quickbooks/unmark')).toHaveLength(1))
    expect(sent('POST', '/api/v1/sales-orders/quickbooks/unmark')[0].body).toEqual({ invoiceIds: [A], all: false })
    await user.click(screen.getByRole('button', { name: 'Volver a las por pasar' }))
    expect(await screen.findByText('FAC-00010')).toBeInTheDocument()
  })
})

describe('reglas de QuickBooks (Lote 225)', () => {
  it('parte en tandas de 1000 sin repetidas', () => {
    const ids = Array.from({ length: 2500 }, (_, i) => `id-${i}`)
    expect(markBatches([...ids, 'id-1']).map((b) => b.length)).toEqual([1000, 1000, 500])
    expect(markBatches([])).toEqual([])
  })

  it('el base64 se lee como bytes (acentos de Windows-1252 intactos)', () => {
    expect([...base64ToBytes(IIF_B64)]).toEqual(IIF_BYTES)
  })

  it('buscador y «ver datos» por factura', () => {
    expect(filterPending(PENDING.invoices, 'ov-00022').map((r) => r.number)).toEqual(['FAC-00011'])
    expect(filterPending(PENDING.invoices, 'farmacia central inc').map((r) => r.number)).toEqual(['FAC-00011'])
    const a = newDataOf(INV_A, PENDING)
    expect(a.client?.code).toBe('FARM9')
    expect(a.products.map((p) => p.sku)).toEqual(['NECH-1001'])
    expect(newDataOf(INV_B, PENDING)).toEqual({ client: null, products: [] })
  })
})
