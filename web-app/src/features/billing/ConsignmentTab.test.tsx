// Pruebas de la pestaña «Consignación» (Lote 224, P6) sobre un fetch simulado: lista y filtros, ficha, registrar un pago (validación contra el saldo y mensaje del servidor),
// anular con motivo, «Consignado por cliente» y permisos.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import BillingScreen from './BillingScreen'
import { consignmentCanPay, consignmentPayError } from './consignmentRules'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  failPay: false,
  payments: [] as { publicId: string; amount: number; isVoided: boolean }[],
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
    if (m === 'GET' && p === '/api/v1/billing/consignment/invoices') return json({ items: [ITEM()], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === `/api/v1/billing/consignment/invoices/${INV}`) return json(DETAIL())
    if (m === 'GET' && p === '/api/v1/billing/consignment/by-client') return json(BY_CLIENT)
    if (m === 'GET' && p === '/api/v1/clients') return json([])
    if (m === 'POST' && p === `/api/v1/billing/consignment/invoices/${INV}/payments`) {
      if (mock.failPay) return problem(422, 'El pago (60.00) es mayor que el saldo de la factura (40.00).')
      const b = body as { amount: number }
      mock.payments.push({ publicId: PAY, amount: b.amount, isVoided: false })
      return json(DETAIL())
    }
    if (m === 'POST' && p === `/api/v1/billing/consignment/payments/${PAY}/void`) {
      mock.payments[0].isVoided = true
      return json(DETAIL())
    }
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
const PAY = '22222222-0000-0000-0000-000000000002'
const CLIENT = '33333333-0000-0000-0000-000000000003'
const ITEM = () => ({
  publicId: INV, number: 'FAC-00007', status: 'ISSUED', statusLabel: 'Emitida', statusColor: '#3B82F6', clientPublicId: CLIENT, clientCode: 'C-1', clientName: 'Farmacia Central',
  issueDate: '2026-10-01', dueDate: '2026-10-31', orderNumbers: ['OV-100'], consigned: 100, returned: 10, paid: mock.payments.filter((x) => !x.isVoided).reduce((a, x) => a + x.amount, 0),
  balance: 90 - mock.payments.filter((x) => !x.isVoided).reduce((a, x) => a + x.amount, 0), invoiceTotal: 100, invoiceBalance: 100, paymentCount: mock.payments.length, lastPaidDate: null,
})
const DETAIL = () => ({
  invoice: ITEM(),
  lines: [{ invoiceLineId: 1, orderNumber: 'OV-100', description: 'Caja de gasas', quantity: 10, amount: 100, returned: 10 }],
  payments: mock.payments.map((x) => ({ ...x, paidDate: '2026-10-05', method: 'CHECK', methodLabel: 'Cheque', reference: 'CH-1', recordedByName: 'Ana', recordedAtUtc: '2026-10-05T15:00:00', linkedToInvoicePayment: true })),
})
const BY_CLIENT = {
  items: [{ clientPublicId: CLIENT, clientCode: 'C-1', clientName: 'Farmacia Central', invoiceCount: 2, openInvoiceCount: 1, consigned: 300, returned: 30, paid: 100, balance: 170, oldestOpenIssueDate: '2026-09-20' }],
  consigned: 300, returned: 30, paid: 100, balance: 170, openInvoiceCount: 1,
}

const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(['billing.view']),
  modules: new Set(['BILLING']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={new Set(permissions)} modules={SESSION.modules}>
          <MemoryRouter>
            <BillingScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)
const PAYER = ['billing.view', 'billing.consignment.pay']

async function openTab(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('tab', { name: 'Consignación' }))
  expect(await screen.findByText('FAC-00007')).toBeInTheDocument()
}
async function openDetail(user: ReturnType<typeof userEvent.setup>) {
  await openTab(user)
  await user.click(screen.getByText('FAC-00007'))
  return screen.findByRole('dialog', { name: /Consignación de la factura FAC-00007/ })
}

describe('Consignación', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.failPay = false
    mock.payments = []
  })

  it('reglas puras: el pago no pasa del saldo y pide factura', () => {
    const form = { amount: '50', method: 'CHECK', paidDate: '2026-10-05', reference: '' }
    expect(consignmentPayError(null, form, 90, '2026-10-06', '2026-10-01')).toBe('invoice')
    expect(consignmentPayError('x', form, 40, '2026-10-06', '2026-10-01')).toBe('exceeds')
    expect(consignmentPayError('x', form, 90, '2026-10-06', '2026-10-01')).toBeNull()
    expect(consignmentCanPay(0)).toBe(false)
    expect(consignmentCanPay(5)).toBe(true)
  })

  it('lista dejado, devuelto, pagado y saldo; por omisión pide solo las facturas con saldo y el filtro cambia la consulta', async () => {
    const user = userEvent.setup()
    wrap(PAYER)
    await openTab(user)
    expect(screen.getByText('OV-100')).toBeInTheDocument()
    expect(new URLSearchParams(sent('GET', '/api/v1/billing/consignment/invoices')[0].search).get('balance')).toBe('OPEN')
    await user.selectOptions(screen.getByLabelText('Filtrar por saldo'), 'PAID')
    await waitFor(() => expect(sent('GET', '/api/v1/billing/consignment/invoices').some((r) => new URLSearchParams(r.search).get('balance') === 'PAID')).toBe(true))
  })

  it('registra un pago parcial: valida contra el saldo en consignación y manda monto y método', async () => {
    const user = userEvent.setup()
    wrap(PAYER)
    const detail = await openDetail(user)
    await user.click(within(detail).getByRole('button', { name: 'Registrar pago' }))
    const pay = await screen.findByRole('dialog', { name: 'Registrar pago' })
    expect(pay).toHaveTextContent('saldo en consignación')
    await user.clear(within(pay).getByLabelText('Monto'))
    await user.type(within(pay).getByLabelText('Monto'), '95')
    await user.click(within(pay).getByRole('button', { name: 'Guardar pago' }))
    expect(await within(pay).findByText('El pago no puede pasar del saldo en consignación de la factura.')).toBeInTheDocument()
    expect(sent('POST', `/api/v1/billing/consignment/invoices/${INV}/payments`)).toHaveLength(0)
    await user.clear(within(pay).getByLabelText('Monto'))
    await user.type(within(pay).getByLabelText('Monto'), '30')
    await user.type(within(pay).getByLabelText('Referencia'), 'CH-1')
    await user.click(within(pay).getByRole('button', { name: 'Guardar pago' }))
    await waitFor(() => expect(sent('POST', `/api/v1/billing/consignment/invoices/${INV}/payments`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/billing/consignment/invoices/${INV}/payments`)[0].body).toMatchObject({ amount: 30, method: 'CHECK', reference: 'CH-1' })
  })

  it('muestra el mensaje del servidor si rechaza el pago', async () => {
    const user = userEvent.setup()
    mock.failPay = true
    wrap(PAYER)
    const detail = await openDetail(user)
    await user.click(within(detail).getByRole('button', { name: 'Registrar pago' }))
    const pay = await screen.findByRole('dialog', { name: 'Registrar pago' })
    await user.click(within(pay).getByRole('button', { name: 'Guardar pago' }))
    expect(await within(pay).findByText(/El pago \(60\.00\) es mayor que el saldo de la factura \(40\.00\)\./)).toBeInTheDocument()
  })

  it('anula un pago con motivo obligatorio', async () => {
    const user = userEvent.setup()
    mock.payments = [{ publicId: PAY, amount: 30, isVoided: false }]
    wrap(PAYER)
    const detail = await openDetail(user)
    await user.click(await within(detail).findByRole('button', { name: 'Anular el pago' }))
    const confirm = await screen.findByRole('dialog', { name: /¿Anular el pago de/ })
    await user.click(within(confirm).getByRole('button', { name: 'Anular el pago' }))
    expect(await within(confirm).findAllByText('Escriba el motivo para anular el pago.')).not.toHaveLength(0)
    expect(sent('POST', `/api/v1/billing/consignment/payments/${PAY}/void`)).toHaveLength(0)
    await user.type(within(confirm).getByLabelText('Motivo'), 'Cheque devuelto')
    await user.click(within(confirm).getByRole('button', { name: 'Anular el pago' }))
    await waitFor(() => expect(sent('POST', `/api/v1/billing/consignment/payments/${PAY}/void`)[0]?.body).toEqual({ reason: 'Cheque devuelto' }))
    expect(await within(detail).findByText('Anulado')).toBeInTheDocument()
  })

  it('«Consignado por cliente» muestra los totales y al tocar un cliente filtra sus facturas', async () => {
    const user = userEvent.setup()
    wrap(PAYER)
    await openTab(user)
    await user.click(screen.getByRole('button', { name: 'Consignado por cliente' }))
    expect(await screen.findByText('Farmacia Central')).toBeInTheDocument()
    expect(screen.getByText(/Dejado .*300.* · Devuelto .*30.* · Pagado .*100.* · Saldo .*170/)).toBeInTheDocument()
    await user.click(screen.getByText('Farmacia Central'))
    await waitFor(() => expect(sent('GET', '/api/v1/billing/consignment/invoices').some((r) => new URLSearchParams(r.search).get('clientPublicId') === CLIENT)).toBe(true))
  })

  it('sin billing.consignment.pay solo se ve la consignación: ni Registrar pago ni Anular', async () => {
    const user = userEvent.setup()
    mock.payments = [{ publicId: PAY, amount: 30, isVoided: false }]
    wrap(['billing.view'])
    const detail = await openDetail(user)
    expect(await within(detail).findByText('Cheque')).toBeInTheDocument()
    expect(within(detail).queryByRole('button', { name: 'Registrar pago' })).toBeNull()
    expect(within(detail).queryByRole('button', { name: 'Anular el pago' })).toBeNull()
  })
})
