// Lote 246 — «QuickBooks» en Órdenes de compra sobre un fetch simulado: la lista de por pasar con proveedor/producto nuevo y «ver datos», el aviso de las ya exportadas,
// exportar (baja el .iif con los bytes exactos y pregunta «¿Ya las importaste?»), «Sí, ya están», «No pasó» y «Todavía no» (sin llamada), y marcar. Más la franja de la ficha.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { PurchaseOrderQuickBooksBar } from './PurchaseOrderQuickBooksBar'
import { PurchaseOrderQuickBooksModal } from './PurchaseOrderQuickBooksModal'

const A = 'aaaaaaaa-0000-0000-0000-000000000001'
const B = 'bbbbbbbb-0000-0000-0000-000000000002'
const IIF_B64 = btoa(String.fromCharCode(0x21, 0x54, 0x52, 0x4e, 0x53, 0x09, 0xd1, 0x0d, 0x0a))

const ORD_A = { publicId: A, number: 'PO-00010', orderDate: '2026-11-02', statusCode: 'SENT', supplierId: 5, supplierName: 'Acme Medical', quickBooksVendor: 'ACME MEDICAL INC', originCode: 'TEIKEM', total: 111.5, fileTotal: 111.5, lines: 2, exportedAtUtc: '2026-11-05T14:00:00', exportedByName: 'Marta Oficina', newSupplier: true, newProductSkus: ['NECH-1001'] }
const ORD_B = { publicId: B, number: 'PO-00011', orderDate: '2026-11-03', statusCode: 'RECEIVED', supplierId: 6, supplierName: 'Zeta Corp', quickBooksVendor: 'Zeta Corp', originCode: 'EXCEL', total: 50, fileTotal: 50, lines: 1, exportedAtUtc: null, exportedByName: null, newSupplier: false, newProductSkus: [] }
const PENDING = {
  count: 2, total: 161.5, alreadyExportedCount: 1, lastExportedAtUtc: '2026-11-05T14:00:00', orders: [ORD_A, ORD_B],
  newSuppliers: [{ supplierId: 5, name: 'Acme Medical', quickBooksName: 'Acme Medical', contactName: 'Ana', phone: '(787) 555-0101', email: 'ana@acme.test', paymentTerm: 'NET30', quickBooksTerms: 'Net 30' }],
  newProducts: [{ publicId: 'p1', sku: 'NECH-1001', quickBooksName: 'NECH-1001', name: 'Guantes de nitrilo', cost: 7 }],
  warnings: ['La orden de compra PO-00010 suma 111.50 en Teikem y 111.49 en el archivo: QuickBooks redondea cada línea a centavos.'], purchaseOrderAccount: 'Purchase Orders',
}

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/purchase-orders/quickbooks') return json(PENDING)
    if (req.method === 'POST' && p === '/api/v1/purchase-orders/quickbooks/export')
      return json({ fileName: 'teikem-quickbooks-compras-20261107-0930.iif', contentType: 'application/octet-stream', contentBase64: IIF_B64, exportedAtUtc: '2026-11-07T13:30:00', orderCount: 2, total: 161.5, alreadyExportedCount: 1, lastExportedAtUtc: '2026-11-05T14:00:00', includedNew: (body as { includeNew: boolean }).includeNew, newSupplierCount: 1, newProductCount: 1, warnings: [], orders: [ORD_A, ORD_B] })
    if (req.method === 'POST' && (p.endsWith('/confirm') || p.endsWith('/mark') || p.endsWith('/unmark')))
      return json({ marked: (body as { all?: boolean; purchaseOrderIds?: string[] }).all ? 2 : ((body as { purchaseOrderIds?: string[] }).purchaseOrderIds ?? []).length, alreadyMarked: 0, suppliersSent: 0, productsSent: 0, atUtc: '2026-11-07T13:31:00' })
    if (req.method === 'POST' && p.endsWith('/not-passed')) return json({ reset: ((body as { purchaseOrderIds?: string[] }).purchaseOrderIds ?? []).length, unchanged: 0, atUtc: '2026-11-07T13:31:00' })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function wrap(ui: React.ReactElement, perms = ['purchasing.view', 'purchasing.quickbooks']) {
  const session = { me: { userId: 9 }, isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', permissions: new Set(perms), modules: new Set(['PURCHASING']) } as unknown as Session
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={session}>
        <AccessProvider permissions={perms} modules={['PURCHASING']}>
          <MemoryRouter>{ui}</MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (path: string) => mock.requests.filter((r) => r.method === 'POST' && r.path === `/api/v1/purchase-orders/quickbooks${path}`)
const blobs: Blob[] = []

describe('QuickBooks de órdenes de compra (Lote 246)', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    blobs.length = 0
    Object.assign(URL, { createObjectURL: (b: Blob) => (blobs.push(b), 'blob:qb'), revokeObjectURL: () => {} })
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  })
  afterEach(() => vi.restoreAllMocks())

  it('lista las por pasar con lo nuevo, el aviso de las ya exportadas y los avisos; «ver datos» abre el proveedor y el producto', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderQuickBooksModal onClose={() => {}} />)
    expect(await screen.findByText('PO-00010')).toBeInTheDocument()
    expect(screen.getByText('PO-00011')).toBeInTheDocument()
    expect(screen.getByText('En QuickBooks como ACME MEDICAL INC')).toBeInTheDocument()
    expect(screen.getByText('Proveedor nuevo')).toBeInTheDocument()
    expect(screen.getByText(/1 de estas ya salió en un archivo el/)).toBeInTheDocument()
    expect(screen.getByText(/QuickBooks redondea cada línea a centavos/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Ver los datos nuevos de la orden PO-00010' }))
    const dialogs = await screen.findAllByRole('dialog')
    const dialog = dialogs[dialogs.length - 1]
    expect(within(dialog).getByText('ana@acme.test')).toBeInTheDocument()
    expect(within(dialog).getByText('Net 30')).toBeInTheDocument()
    expect(within(dialog).getByText('Guantes de nitrilo')).toBeInTheDocument()
  })

  it('exportar baja el .iif con los bytes exactos, pregunta y «Sí, ya están» confirma solo las marcadas; «Todavía no» no llama', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderQuickBooksModal onClose={() => {}} />)
    await screen.findByText('PO-00010')
    await user.click(screen.getByRole('button', { name: 'Exportar 2' }))
    await screen.findByText('¿Ya las importaste en QuickBooks? (2)')
    expect(sent('/export')[0].body).toEqual({ all: true, includeNew: true })
    expect(blobs).toHaveLength(1)
    expect(Array.from(new Uint8Array(await blobs[0].arrayBuffer()))).toEqual([0x21, 0x54, 0x52, 0x4e, 0x53, 0x09, 0xd1, 0x0d, 0x0a])

    await user.click(screen.getByRole('button', { name: 'Todavía no' }))
    expect(sent('/confirm')).toHaveLength(0)
    expect(sent('/not-passed')).toHaveLength(0)

    await user.click(await screen.findByRole('button', { name: 'Exportar 2' }))
    await screen.findByText('¿Ya las importaste en QuickBooks? (2)')
    await user.click(screen.getByRole('checkbox', { name: /PO-00011/ }))
    await user.click(screen.getByRole('button', { name: 'Sí, ya están (1)' }))
    await waitFor(() => expect(sent('/confirm')).toHaveLength(1))
    expect(sent('/confirm')[0].body).toEqual({ purchaseOrderIds: [A] })
  })

  it('«No pasó» manda las marcadas a not-passed', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderQuickBooksModal onClose={() => {}} />)
    await screen.findByText('PO-00010')
    await user.click(screen.getByRole('button', { name: 'Exportar 2' }))
    await screen.findByText('¿Ya las importaste en QuickBooks? (2)')
    await user.click(screen.getByRole('button', { name: 'No pasó (2)' }))
    await waitFor(() => expect(sent('/not-passed')).toHaveLength(1))
    expect(sent('/not-passed')[0].body).toEqual({ purchaseOrderIds: [A, B] })
  })

  it('sin proveedores nuevos (desmarcar «Incluir…») exporta con includeNew false y marcar elegidas usa mark', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderQuickBooksModal onClose={() => {}} />)
    await screen.findByText('PO-00010')
    await user.click(screen.getByRole('checkbox', { name: 'Incluir proveedores y productos nuevos' }))
    await user.click(screen.getByRole('checkbox', { name: 'Elegir la orden PO-00011' }))
    await user.click(screen.getByRole('button', { name: 'Marcar 1 como en QuickBooks' }))
    await waitFor(() => expect(sent('/mark')).toHaveLength(1))
    expect(sent('/mark')[0].body).toEqual({ purchaseOrderIds: [A] })
    await user.click(screen.getByRole('button', { name: 'Exportar 2' }))
    await waitFor(() => expect(sent('/export')).toHaveLength(1))
    expect(sent('/export')[0].body).toEqual({ all: true, includeNew: false })
  })

  it('«Marcar todas» pide confirmación y manda all', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderQuickBooksModal onClose={() => {}} />)
    await screen.findByText('PO-00010')
    await user.click(screen.getByRole('button', { name: 'Marcar todas' }))
    expect(sent('/mark')).toHaveLength(0)
    const dialogs = await screen.findAllByRole('dialog')
    await user.click(within(dialogs[dialogs.length - 1]).getByRole('button', { name: 'Marcar todas' }))
    await waitFor(() => expect(sent('/mark')).toHaveLength(1))
    expect(sent('/mark')[0].body).toEqual({ all: true })
  })

  it('la franja de la ficha: estado, quién y cuándo, acciones por estado y aviso al editar', async () => {
    const user = userEvent.setup()
    const po = { publicId: A, number: 'PO-00010', originCode: 'EXCEL', origin: 'Excel / archivo', quickBooksState: 'EXPORTED', quickBooksExportedAtUtc: '2026-11-05T14:00:00', quickBooksExportedByName: 'Marta Oficina' }
    wrap(<PurchaseOrderQuickBooksBar po={po} />)
    expect(screen.getByText('Excel / archivo')).toBeInTheDocument()
    expect(screen.getByText('Exportada, por confirmar')).toBeInTheDocument()
    expect(screen.getByText(/Marta Oficina/)).toBeInTheDocument()
    expect(screen.getByText(/editarla o cancelarla aquí no cambia nada allá/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'No pasó' }))
    await waitFor(() => expect(sent('/not-passed')).toHaveLength(1))
    expect(sent('/not-passed')[0].body).toEqual({ purchaseOrderIds: [A] })
  })

  it('sin purchasing.quickbooks la franja muestra los chips pero ninguna acción', () => {
    wrap(<PurchaseOrderQuickBooksBar po={{ publicId: A, number: 'PO-1', originCode: 'TEIKEM', quickBooksState: 'PENDING' }} />, ['purchasing.view'])
    expect(screen.getByText('Por pasar')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Pasar|Marcar|Quitar|No pasó|Sí, ya está/ })).toBeNull()
  })
})
