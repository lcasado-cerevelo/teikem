// Pruebas de «Cobros» (Lote 76) sobre un fetch simulado: registrar y anular pagos en la ficha de la factura, la pestaña de cuentas por cobrar y el estado de cuenta.
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
  status: 'ISSUED',
  payments: [] as { id: number; amount: number; method: string; paidDate: string; reference: string | null; voidedAtUtc: string | null; voidReason: string | null }[],
  failPay: false,
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
    if (m === 'GET' && p === '/api/v1/billing/runs') return json([])
    if (m === 'GET' && p === '/api/v1/billing/invoices') return json({ items: [ITEM()], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === `/api/v1/billing/invoices/${INV}`) return json(INVOICE())
    if (m === 'POST' && p === `/api/v1/billing/invoices/${INV}/payments`) {
      if (mock.failPay) return problem(422, 'El pago (500.00) es mayor que el saldo de la factura (100.00).')
      const b = body as { amount: number; method: string; paidDate?: string; reference?: string }
      mock.payments.push({ id: 5, amount: b.amount, method: b.method, paidDate: b.paidDate ?? '2026-10-16', reference: b.reference ?? null, voidedAtUtc: null, voidReason: null })
      return json(INVOICE())
    }
    if (m === 'POST' && p === '/api/v1/billing/payments/5/void') {
      mock.payments[0].voidedAtUtc = '2026-10-16T15:00:00Z'
      mock.payments[0].voidReason = (body as { reason: string }).reason
      return json(INVOICE())
    }
    if (m === 'GET' && p === '/api/v1/billing/receivables') return json(RECEIVABLES)
    if (m === 'GET' && p === `/api/v1/billing/clients/${CLIENT}/statement`) return json(STATEMENT)
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
const ITEM = () => ({
  publicId: INV, number: 'FAC-00001', status: mock.status, statusLabel: mock.status === 'PAID' ? 'Pagada' : 'Emitida', statusColor: '#3B82F6', clientPublicId: CLIENT, clientName: 'Farmacia Central',
  issueDate: '2026-10-01', dueDate: '2026-10-31', subtotal: 100, taxAmount: 0, total: 100, paid: 0, balance: 100, lineCount: 1, runId: 7,
})
const INVOICE = () => ({
  ...ITEM(), currency: 'USD', notes: null, runId: 7,
  lines: [{ id: 90, description: 'T-1 · Flete', chargeType: 'FREIGHT', quantity: 1, unitAmount: 100, originalUnitAmount: null, isAdjusted: false, adjustmentNote: null, lineSubtotal: 100, taxRatePct: 0, taxAmount: 0, isTaxExempt: true }],
  payments: mock.payments.map((p) => ({ ...p, methodLabel: p.method })),
})
const AGING = { current: 30, days1To30: 70, days31To60: 0, days61To90: 0, over90: 0, total: 100 }
const RECEIVABLES = {
  asOf: '2026-10-16',
  rows: [{ clientPublicId: CLIENT, clientName: 'Farmacia Central', openInvoices: 2, balance: 100, aging: AGING, oldestDueDate: '2026-10-10' }],
  total: AGING,
}
const STATEMENT = {
  clientPublicId: CLIENT, clientName: 'Farmacia Central', from: '2026-07-18', to: '2026-10-16', openingBalance: 0, totalInvoiced: 130, totalPaid: 30, closingBalance: 100,
  lines: [
    { date: '2026-10-01', kind: 'INVOICE', reference: 'FAC-00001', description: 'Factura FAC-00001', debit: 130, credit: 0, balance: 130, invoicePublicId: INV },
    { date: '2026-10-05', kind: 'PAYMENT', reference: 'CH-7', description: 'Pago Cheque de la factura FAC-00001', debit: 0, credit: 30, balance: 100, invoicePublicId: INV },
  ],
  aging: AGING,
}

const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(['billing.view', 'billing.generate', 'billing.approve', 'clients.read']),
  modules: new Set(['BILLING']),
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

async function openInvoice(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('tab', { name: 'Facturas' }))
  await user.click(await screen.findByText('FAC-00001'))
  return screen.findByRole('dialog', { name: /Factura FAC-00001/ })
}

describe('Cobros', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.status = 'ISSUED'
    mock.payments = []
    mock.failPay = false
  })

  it('registra un pago: valida antes de enviar, no deja pasar del saldo y manda monto, método, referencia y la fecha solo si no es hoy', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openInvoice(user)
    expect(await within(dialog).findByText('Todavía no hay pagos.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Registrar pago' }))
    const pay = await screen.findByRole('dialog', { name: 'Registrar pago' })
    expect((within(pay).getByLabelText('Monto') as HTMLInputElement).value).toBe('100')   // el saldo por omisión

    await user.clear(within(pay).getByLabelText('Monto'))
    await user.type(within(pay).getByLabelText('Monto'), '150')
    await user.click(within(pay).getByRole('button', { name: 'Guardar pago' }))
    expect(await within(pay).findByText('El pago no puede pasar del saldo de la factura.')).toBeInTheDocument()
    expect(sent('POST', `/api/v1/billing/invoices/${INV}/payments`)).toHaveLength(0)

    await user.clear(within(pay).getByLabelText('Monto'))
    await user.type(within(pay).getByLabelText('Monto'), '30,5')
    await user.type(within(pay).getByLabelText('Referencia'), 'CH-100')
    await user.click(within(pay).getByRole('button', { name: 'Guardar pago' }))
    await waitFor(() => expect(sent('POST', `/api/v1/billing/invoices/${INV}/payments`)).toHaveLength(1))
    const body = sent('POST', `/api/v1/billing/invoices/${INV}/payments`)[0].body as Record<string, unknown>
    expect(body).toMatchObject({ amount: 30.5, method: 'CHECK', reference: 'CH-100' })
    expect(body).not.toHaveProperty('paidDate')
    expect(await within(dialog).findByText('CH-100')).toBeInTheDocument()
  })

  it('muestra el mensaje del servidor si rechaza el pago', async () => {
    const user = userEvent.setup()
    mock.failPay = true
    wrap()
    const dialog = await openInvoice(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Registrar pago' }))
    const pay = await screen.findByRole('dialog', { name: 'Registrar pago' })
    await user.click(within(pay).getByRole('button', { name: 'Guardar pago' }))
    expect(await within(pay).findByText('El pago (500.00) es mayor que el saldo de la factura (100.00).')).toBeInTheDocument()
  })

  it('anula un pago con motivo obligatorio y lo deja tachado con su estatus', async () => {
    const user = userEvent.setup()
    mock.payments = [{ id: 5, amount: 30, method: 'CHECK', paidDate: '2026-10-05', reference: 'CH-7', voidedAtUtc: null, voidReason: null }]
    wrap()
    const dialog = await openInvoice(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Anular el pago' }))
    const confirm = await screen.findByRole('dialog', { name: /¿Anular el pago de/ })
    await user.click(within(confirm).getByRole('button', { name: 'Anular el pago' }))
    expect(await within(confirm).findAllByText('Escriba el motivo para anular el pago.')).not.toHaveLength(0)
    expect(sent('POST', '/api/v1/billing/payments/5/void')).toHaveLength(0)
    await user.type(within(confirm).getByLabelText('Motivo'), 'Cheque devuelto')
    await user.click(within(confirm).getByRole('button', { name: 'Anular el pago' }))
    await waitFor(() => expect(sent('POST', '/api/v1/billing/payments/5/void')[0]?.body).toEqual({ reason: 'Cheque devuelto' }))
    expect(await within(dialog).findByText('Anulado')).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Anular el pago' })).toBeNull()
  })

  it('una factura pagada ya no admite registrar ni anular pagos', async () => {
    const user = userEvent.setup()
    mock.status = 'PAID'
    mock.payments = [{ id: 5, amount: 100, method: 'ACH', paidDate: '2026-10-05', reference: null, voidedAtUtc: null, voidReason: null }]
    wrap()
    const dialog = await openInvoice(user)
    expect(await within(dialog).findByText('ACH')).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Registrar pago' })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Anular el pago' })).toBeNull()
  })

  it('sin permisos de generar ni aprobar solo se ven los pagos', async () => {
    const user = userEvent.setup()
    mock.payments = [{ id: 5, amount: 30, method: 'CASH', paidDate: '2026-10-05', reference: null, voidedAtUtc: null, voidReason: null }]
    wrap(['billing.view'])
    const dialog = await openInvoice(user)
    expect(await within(dialog).findByText('Efectivo')).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Registrar pago' })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Anular el pago' })).toBeNull()
  })

  it('cuentas por cobrar: una fila por cliente con su antigüedad y, al abrirla, el estado de cuenta del período', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Cobros' }))
    expect(await screen.findByText('Farmacia Central')).toBeInTheDocument()
    expect(await screen.findByText(/Por cobrar: .*100.* \(vencido: .*70.*\)\./)).toBeInTheDocument()
    await user.click(screen.getByText('Farmacia Central'))
    const stmt = await screen.findByRole('dialog', { name: /Estado de cuenta · Farmacia Central/ })
    expect(await within(stmt).findByText('Factura FAC-00001')).toBeInTheDocument()
    expect(within(stmt).getByText('Pago Cheque de la factura FAC-00001')).toBeInTheDocument()
    const call = sent('GET', `/api/v1/billing/clients/${CLIENT}/statement`)[0]
    expect(new URLSearchParams(call.search).get('from')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    expect(new URLSearchParams(call.search).get('to')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    // un movimiento abre su factura
    await user.click(within(stmt).getByText('Factura FAC-00001'))
    expect(await screen.findByRole('dialog', { name: /Factura FAC-00001/ })).toBeInTheDocument()
  })
})
