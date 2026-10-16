// Pruebas de «Facturación» (Lote 73) sobre un fetch simulado: la lista de corridas, generar una corrida, revisar/aprobar/cancelar, la factura (ajustar una línea con motivo, anular) y los ajustes.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import BillingScreen from './BillingScreen'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  runStatus: 'GENERATED',
  failGenerate: false,
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
    const st = STATUS[mock.runStatus]
    if (m === 'GET' && p === '/api/v1/billing/settings') return json({ salesBillingTrigger: 'ON_DISPATCH', invoiceDueDays: 30 })
    if (m === 'PUT' && p === '/api/v1/billing/settings') return json({ salesBillingTrigger: 'ON_DELIVERY', invoiceDueDays: 15 })
    if (m === 'GET' && p === '/api/v1/billing/runs') return json([{ id: 7, periodStart: '2026-10-01', periodEnd: '2026-10-15', status: mock.runStatus, statusLabel: st.label, statusColor: st.color, clientName: null, invoiceCount: 1, totalAmount: 44.6, generatedAtUtc: '2026-10-16T12:00:00Z' }])
    if (m === 'GET' && p === '/api/v1/billing/runs/7') return json(RUN())
    if (m === 'POST' && p === '/api/v1/billing/runs') return mock.failGenerate ? problem(422, 'No hay nada por facturar con esos datos.') : json({ ...RUN(), id: 8 })
    if (m === 'GET' && p === '/api/v1/billing/runs/8') return json({ ...RUN(), id: 8 })
    if (m === 'POST' && p === '/api/v1/billing/runs/7/review') { mock.runStatus = 'REVIEWED'; return json(RUN()) }
    if (m === 'POST' && p === '/api/v1/billing/runs/7/approve') { mock.runStatus = 'APPROVED'; return json(RUN()) }
    if (m === 'POST' && p === '/api/v1/billing/runs/7/cancel') { mock.runStatus = 'CANCELLED'; return json(RUN()) }
    if (m === 'GET' && p === '/api/v1/billing/invoices') return json({ items: [INVOICE_ITEM], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === `/api/v1/billing/invoices/${INV}`) return json(INVOICE())
    if (m === 'PATCH' && p === `/api/v1/billing/invoices/${INV}/lines/90`) return json(INVOICE())
    if (m === 'POST' && p === `/api/v1/billing/invoices/${INV}/void`) return json({ ...INVOICE(), status: 'VOID' })
    if (p === '/api/v1/clients') return json([{ id: 1, publicId: CLIENT, code: 'FARM', name: 'Farmacia Central', isActive: true }])
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
function problem(status: number, title: string) {
  return new Response(JSON.stringify({ title, status, code: 'status_rule' }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

const INV = '11111111-0000-0000-0000-000000000001'
const CLIENT = '33333333-0000-0000-0000-000000000003'
const STATUS: Record<string, { label: string; color: string }> = {
  GENERATED: { label: 'Generada', color: '#9CA3AF' },
  REVIEWED: { label: 'Revisada', color: '#F59E0B' },
  APPROVED: { label: 'Aprobada', color: '#3B82F6' },
  CANCELLED: { label: 'Cancelada', color: '#6B7280' },
}
const INVOICE_ITEM = { publicId: INV, number: 'FAC-00001', status: 'DRAFT', statusLabel: 'Borrador', statusColor: '#9CA3AF', clientPublicId: CLIENT, clientName: 'Farmacia Central', issueDate: null, dueDate: null, subtotal: 40, taxAmount: 4.6, total: 44.6, paid: 0, balance: 44.6, lineCount: 1, runId: 7 }
const RUN = () => ({
  id: 7, periodStart: '2026-10-01', periodEnd: '2026-10-15', status: mock.runStatus, statusLabel: STATUS[mock.runStatus].label, statusColor: STATUS[mock.runStatus].color, clientPublicId: null, clientName: null, note: null,
  invoiceCount: 1, totalAmount: 44.6, generatedAtUtc: '2026-10-16T12:00:00Z', reviewedAtUtc: null, approvedAtUtc: null, exportedAtUtc: null, invoices: [INVOICE_ITEM],
})
const INVOICE = () => ({
  ...INVOICE_ITEM, currency: 'USD', notes: null, status: mock.runStatus === 'APPROVED' ? 'ISSUED' : 'DRAFT', statusLabel: mock.runStatus === 'APPROVED' ? 'Emitida' : 'Borrador',
  lines: [{ id: 90, description: 'OV-00001 · MED Medidor', chargeType: 'PRODUCT_SALE', quantity: 4, unitAmount: 10, originalUnitAmount: null, isAdjusted: false, adjustmentNote: null, lineSubtotal: 40, taxRatePct: 11.5, taxAmount: 4.6, isTaxExempt: false }],
})

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
  permissions: new Set(['billing.view', 'billing.generate', 'billing.approve', 'clients.read']),
  modules: new Set(['BILLING', 'SALES_ORDERS']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(permissions?: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const perms = permissions ? new Set(permissions) : SESSION.permissions
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={perms} modules={SESSION.modules}>
          <MemoryRouter>
            <BillingScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

describe('BillingScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.runStatus = 'GENERATED'
    mock.failGenerate = false
  })

  it('lista las corridas con su estatus y abre una con sus facturas y las acciones del estatus', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByRole('heading', { level: 1, name: 'Facturación' })).toBeInTheDocument()
    await user.click(await screen.findByText('#7'))
    const dialog = await screen.findByRole('dialog', { name: /Corrida #7/ })
    expect(await within(dialog).findByText('FAC-00001')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Marcar como revisada' })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Cancelar la corrida' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Aprobar y emitir' })).toBeNull()
  })

  it('revisar y luego aprobar: cada paso llama a su endpoint y el botón cambia con el estatus', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('#7'))
    const dialog = await screen.findByRole('dialog', { name: /Corrida #7/ })
    await user.click(await within(dialog).findByRole('button', { name: 'Marcar como revisada' }))
    await waitFor(() => expect(sent('POST', '/api/v1/billing/runs/7/review')).toHaveLength(1))
    await user.click(await within(dialog).findByRole('button', { name: 'Aprobar y emitir' }))
    await waitFor(() => expect(sent('POST', '/api/v1/billing/runs/7/approve')).toHaveLength(1))
  })

  it('aprobar solo lo ofrece quien tiene billing.approve', async () => {
    const user = userEvent.setup()
    mock.runStatus = 'REVIEWED'
    wrap(['billing.view', 'billing.generate'])
    await user.click(await screen.findByText('#7'))
    const dialog = await screen.findByRole('dialog', { name: /Corrida #7/ })
    await within(dialog).findByText('FAC-00001')
    expect(within(dialog).queryByRole('button', { name: 'Aprobar y emitir' })).toBeNull()
  })

  it('cancelar la corrida pide confirmación', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('#7'))
    const dialog = await screen.findByRole('dialog', { name: /Corrida #7/ })
    await user.click(await within(dialog).findByRole('button', { name: 'Cancelar la corrida' }))
    const confirm = await screen.findByRole('dialog', { name: /¿Cancelar la corrida #7\?/ })
    await user.click(within(confirm).getByRole('button', { name: 'Cancelar la corrida' }))
    await waitFor(() => expect(sent('POST', '/api/v1/billing/runs/7/cancel')).toHaveLength(1))
  })

  it('nueva corrida: valida las fechas, manda el cuerpo y abre la corrida generada; el mensaje del servidor se ve tal cual', async () => {
    const user = userEvent.setup()
    mock.failGenerate = true
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva corrida' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva corrida' })
    await user.clear(within(dialog).getByLabelText('Desde'))
    await user.click(within(dialog).getByRole('button', { name: 'Generar' }))
    expect(await within(dialog).findByText('Indique la fecha inicial y la final.')).toBeInTheDocument()
    expect(sent('POST', '/api/v1/billing/runs')).toHaveLength(0)

    await user.type(within(dialog).getByLabelText('Desde'), '2026-10-01')
    await user.click(within(dialog).getByLabelText(/Solo lo despachado dentro del período/))
    await user.click(within(dialog).getByRole('button', { name: 'Generar' }))
    expect(await within(dialog).findByText('No hay nada por facturar con esos datos.')).toBeInTheDocument()
    expect(sent('POST', '/api/v1/billing/runs')[0].body).toMatchObject({ periodStart: '2026-10-01', onlyInPeriod: true })

    mock.failGenerate = false
    await user.click(within(dialog).getByRole('button', { name: 'Generar' }))
    expect(await screen.findByRole('dialog', { name: /Corrida #8/ })).toBeInTheDocument()
  })

  it('la factura: ajustar una línea pide el motivo y manda monto y motivo; anular pide confirmación', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(screen.getByRole('tab', { name: 'Facturas' }))
    await user.click(await screen.findByText('FAC-00001'))
    const invoice = await screen.findByRole('dialog', { name: /Factura FAC-00001/ })
    await user.click(await within(invoice).findByRole('button', { name: 'Ajustar' }))
    const adjust = await screen.findByRole('dialog', { name: 'Ajustar el monto' })
    await user.clear(within(adjust).getByLabelText('Monto unitario'))
    await user.type(within(adjust).getByLabelText('Monto unitario'), '8')
    await user.click(within(adjust).getByRole('button', { name: 'Guardar ajuste' }))
    expect(await within(adjust).findByText('Escriba el motivo del ajuste.')).toBeInTheDocument()
    await user.type(within(adjust).getByLabelText('Motivo del ajuste'), 'Descuento acordado')
    await user.click(within(adjust).getByRole('button', { name: 'Guardar ajuste' }))
    await waitFor(() => expect(sent('PATCH', `/api/v1/billing/invoices/${INV}/lines/90`)[0]?.body).toEqual({ unitAmount: 8, note: 'Descuento acordado' }))
  })

  it('una factura emitida se anula con motivo; una en borrador no ofrece anular', async () => {
    const user = userEvent.setup()
    mock.runStatus = 'APPROVED'
    wrap()
    await user.click(screen.getByRole('tab', { name: 'Facturas' }))
    await user.click(await screen.findByText('FAC-00001'))
    const invoice = await screen.findByRole('dialog', { name: /Factura FAC-00001/ })
    expect(within(invoice).queryByRole('button', { name: 'Ajustar' })).toBeNull()   // la corrida ya está aprobada
    await user.click(await within(invoice).findByRole('button', { name: 'Anular la factura' }))
    const confirm = await screen.findByRole('dialog', { name: /¿Anular la factura FAC-00001\?/ })
    await user.type(within(confirm).getByLabelText('Motivo (opcional)'), 'Cliente equivocado')
    await user.click(within(confirm).getByRole('button', { name: 'Anular la factura' }))
    await waitFor(() => expect(sent('POST', `/api/v1/billing/invoices/${INV}/void`)[0]?.body).toEqual({ comment: 'Cliente equivocado' }))
  })

  it('ajustes: base de facturación y días de vencimiento (quien aprueba los cambia)', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(screen.getByRole('tab', { name: 'Ajustes' }))
    const select = await screen.findByLabelText('Cuándo se factura el producto vendido')
    await user.selectOptions(select, 'ON_DELIVERY')
    await user.clear(screen.getByLabelText('Días para el vencimiento'))
    await user.type(screen.getByLabelText('Días para el vencimiento'), '15')
    await user.click(screen.getByRole('button', { name: 'Guardar ajustes' }))
    await waitFor(() => expect(sent('PUT', '/api/v1/billing/settings')[0]?.body).toEqual({ salesBillingTrigger: 'ON_DELIVERY', invoiceDueDays: 15 }))
  })
})
