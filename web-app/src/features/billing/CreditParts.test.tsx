// Pruebas de «Notas de crédito» (Lote 80) sobre un fetch simulado: la lista, la ficha (aprobar, aplicar, anular), el editor sobre una factura y libre, y el botón desde la ficha de la factura.
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
  noteStatus: 'DRAFT',
  applied: 0,
  failApprove: false,
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
    if (m === 'GET' && p === '/api/v1/billing/credit-notes') return json({ items: [NOTE_ITEM()], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === `/api/v1/billing/credit-notes/${NOTE}`) return json(NOTE_DTO())
    if (m === 'POST' && p === '/api/v1/billing/credit-notes') return json({ ...NOTE_DTO(), publicId: NOTE })
    if (m === 'PUT' && p === `/api/v1/billing/credit-notes/${NOTE}`) return json(NOTE_DTO())
    if (m === 'POST' && p === `/api/v1/billing/credit-notes/${NOTE}/approve`) {
      if (mock.failApprove) return problem(422, 'No se puede acreditar más de lo facturado: de «Flete» quedan 60.00 por acreditar.')
      mock.noteStatus = 'APPROVED'
      return json(NOTE_DTO())
    }
    if (m === 'POST' && p === `/api/v1/billing/credit-notes/${NOTE}/apply`) {
      mock.applied += (body as { amount: number }).amount
      return json(NOTE_DTO())
    }
    if (m === 'POST' && p === `/api/v1/billing/credit-notes/${NOTE}/void`) {
      mock.noteStatus = 'VOID'
      return json(NOTE_DTO())
    }
    if (m === 'GET' && p === '/api/v1/billing/invoices') return json({ items: [INVOICE_ITEM], total: 1, skip: 0, take: 50 })
    if (m === 'GET' && p === `/api/v1/billing/invoices/${INV}`) return json(INVOICE)
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
const NOTE = '22222222-0000-0000-0000-000000000002'
const CLIENT = '33333333-0000-0000-0000-000000000003'
const LABEL: Record<string, string> = { DRAFT: 'Borrador', APPROVED: 'Aprobada', APPLIED: 'Aplicada', VOID: 'Anulada' }
const INVOICE_ITEM = {
  publicId: INV, number: 'FAC-00001', status: 'ISSUED', statusLabel: 'Emitida', statusColor: '#3B82F6', clientPublicId: CLIENT, clientName: 'Farmacia Central', issueDate: '2026-10-01', dueDate: '2026-10-31',
  subtotal: 100, taxAmount: 10, total: 110, paid: 0, balance: 110, lineCount: 1, runId: 7, credited: 0,
}
const INVOICE = {
  ...INVOICE_ITEM, currency: 'USD', notes: null, payments: [],
  lines: [{ id: 90, description: 'T-1 · Flete', chargeType: 'FREIGHT', quantity: 1, unitAmount: 100, originalUnitAmount: null, isAdjusted: false, adjustmentNote: null, lineSubtotal: 100, taxRatePct: 10, taxAmount: 10, isTaxExempt: false }],
}
const NOTE_ITEM = () => ({
  publicId: NOTE, number: 'NC-00001', status: mock.noteStatus, statusLabel: LABEL[mock.noteStatus], statusColor: '#3B82F6', clientPublicId: CLIENT, clientName: 'Farmacia Central', invoiceNumber: 'FAC-00001',
  reason: 'PRICE_ADJUSTMENT', reasonLabel: 'Ajuste de precio', total: 44, applied: mock.applied, balance: mock.noteStatus === 'APPROVED' ? 44 - mock.applied : 0, createdAtUtc: '2026-10-16T12:00:00Z',
})
const NOTE_DTO = () => ({
  ...NOTE_ITEM(), invoicePublicId: INV, externalInvoiceRef: null, currency: 'USD', subtotal: 40, taxAmount: 4, note: 'Descuento pactado', approvedAtUtc: mock.noteStatus === 'APPROVED' ? '2026-10-16T13:00:00Z' : null,
  lines: [{ id: 5, invoiceLineId: 90, description: 'T-1 · Flete', chargeType: 'FREIGHT', sku: null, quantity: 1, unitAmount: 40, lineSubtotal: 40, taxRatePct: 10, taxAmount: 4, isTaxExempt: false }],
  applications: mock.applied > 0 ? [{ id: 1, invoicePublicId: INV, invoiceNumber: 'FAC-00001', amount: mock.applied, appliedDate: '2026-10-16' }] : [],
})

const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(['billing.view', 'billing.generate', 'billing.approve', 'billing.credit', 'clients.read']),
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

async function openNote(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('tab', { name: 'Notas de crédito' }))
  await user.click(await screen.findByText('NC-00001'))
  return screen.findByRole('dialog', { name: /Nota de crédito NC-00001/ })
}

describe('Notas de crédito', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.noteStatus = 'DRAFT'
    mock.applied = 0
    mock.failApprove = false
  })

  it('la pestaña lista las notas con su estatus y el saldo, y filtra por estatus', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Notas de crédito' }))
    expect(await screen.findByText('NC-00001')).toBeInTheDocument()
    expect(screen.getByText('Ajuste de precio')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Filtrar por estatus'), 'APPROVED')
    await waitFor(() => expect(sent('GET', '/api/v1/billing/credit-notes').some((r) => r.search.includes('status=APPROVED'))).toBe(true))
  })

  it('una nota en borrador se aprueba desde su ficha y luego se puede aplicar o anular', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openNote(user)
    expect(await within(dialog).findByText('Descuento pactado')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Cambiar' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Aplicar a una factura' })).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Aprobar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/billing/credit-notes/${NOTE}/approve`)).toHaveLength(1))
    expect(await within(dialog).findByRole('button', { name: 'Aplicar a una factura' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Aprobar' })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Cambiar' })).toBeNull()
    expect(within(dialog).getByRole('button', { name: 'Anular la nota' })).toBeInTheDocument()
  })

  it('muestra el mensaje del servidor si no deja aprobar', async () => {
    const user = userEvent.setup()
    mock.failApprove = true
    wrap()
    const dialog = await openNote(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Aprobar' }))
    expect(await within(dialog).findByText(/No se puede acreditar más de lo facturado/)).toBeInTheDocument()
  })

  it('aplicar: elige la factura abierta del cliente, propone el menor saldo y valida el tope', async () => {
    const user = userEvent.setup()
    mock.noteStatus = 'APPROVED'
    wrap()
    const dialog = await openNote(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Aplicar a una factura' }))
    const apply = await screen.findByRole('dialog', { name: /Aplicar la nota NC-00001/ })
    await user.selectOptions(await within(apply).findByLabelText('Factura'), INV)
    expect((within(apply).getByLabelText('Monto') as HTMLInputElement).value).toBe('44')   // el saldo de la nota (44) es menor que el de la factura (110)
    await user.clear(within(apply).getByLabelText('Monto'))
    await user.type(within(apply).getByLabelText('Monto'), '50')
    await user.click(within(apply).getByRole('button', { name: 'Aplicar' }))
    expect(await within(apply).findByText('El monto no puede pasar del saldo de la nota.')).toBeInTheDocument()
    expect(sent('POST', `/api/v1/billing/credit-notes/${NOTE}/apply`)).toHaveLength(0)
    await user.clear(within(apply).getByLabelText('Monto'))
    await user.type(within(apply).getByLabelText('Monto'), '20')
    await user.click(within(apply).getByRole('button', { name: 'Aplicar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/billing/credit-notes/${NOTE}/apply`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/billing/credit-notes/${NOTE}/apply`)[0].body).toEqual({ invoicePublicId: INV, amount: 20 })
    expect(await within(dialog).findByText('Aplicaciones')).toBeInTheDocument()
  })

  it('anular pide el motivo y no se ofrece si la nota ya se aplicó', async () => {
    const user = userEvent.setup()
    mock.noteStatus = 'APPROVED'
    wrap()
    const dialog = await openNote(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Anular la nota' }))
    const confirm = await screen.findByRole('dialog', { name: /¿Anular la nota NC-00001\?/ })
    await user.click(within(confirm).getByRole('button', { name: 'Anular la nota' }))
    expect((await within(confirm).findAllByText('Escriba el motivo para anular la nota.')).length).toBeGreaterThan(0)
    expect(sent('POST', `/api/v1/billing/credit-notes/${NOTE}/void`)).toHaveLength(0)
    await user.type(within(confirm).getByLabelText('Motivo'), 'Se pidió por error')
    await user.click(within(confirm).getByRole('button', { name: 'Anular la nota' }))
    await waitFor(() => expect(sent('POST', `/api/v1/billing/credit-notes/${NOTE}/void`)[0]?.body).toEqual({ reason: 'Se pidió por error' }))
  })

  it('una nota ya aplicada no se anula', async () => {
    const user = userEvent.setup()
    mock.noteStatus = 'APPROVED'
    mock.applied = 10
    wrap()
    const dialog = await openNote(user)
    expect(await within(dialog).findByText('Aplicaciones')).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Anular la nota' })).toBeNull()
  })

  it('nueva nota sobre una factura: elige la factura, deja marcadas sus líneas, ajusta el monto y crea el borrador', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Notas de crédito' }))
    await user.click(await screen.findByRole('button', { name: 'Nueva nota de crédito' }))
    const editor = await screen.findByRole('dialog', { name: 'Nueva nota de crédito' })
    await user.click(await within(editor).findByRole('button', { name: /FAC-00001/ }))
    expect(await within(editor).findByText(/Sobre la factura FAC-00001 de Farmacia Central/)).toBeInTheDocument()
    await user.click(within(editor).getByRole('button', { name: 'Guardar borrador' }))
    expect(await within(editor).findByText('Elija el motivo de la nota.')).toBeInTheDocument()
    await user.selectOptions(within(editor).getByLabelText('Motivo'), 'PRICE_ADJUSTMENT')
    const unit = await within(editor).findByLabelText('Monto unitario de la línea 1')
    expect((unit as HTMLInputElement).value).toBe('100')
    await user.clear(unit)
    await user.type(unit, '40')
    expect(within(editor).getByText(/Valor de las líneas \(sin impuesto\): .*40/)).toBeInTheDocument()
    await user.click(within(editor).getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(sent('POST', '/api/v1/billing/credit-notes')).toHaveLength(1))
    expect(sent('POST', '/api/v1/billing/credit-notes')[0].body).toEqual({
      reason: 'PRICE_ADJUSTMENT',
      invoicePublicId: INV,
      lines: [{ invoiceLineId: 90, description: 'T-1 · Flete', quantity: 1, unitAmount: 40 }],
    })
    expect(await screen.findByRole('dialog', { name: /Nota de crédito NC-00001/ })).toBeInTheDocument()
  })

  it('sin ninguna línea marcada avisa antes de enviar', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Notas de crédito' }))
    await user.click(await screen.findByRole('button', { name: 'Nueva nota de crédito' }))
    const editor = await screen.findByRole('dialog', { name: 'Nueva nota de crédito' })
    await user.click(await within(editor).findByRole('button', { name: /FAC-00001/ }))
    await user.selectOptions(await within(editor).findByLabelText('Motivo'), 'RETURN')
    await user.click(await within(editor).findByLabelText('Acreditar la línea 1'))
    await user.click(within(editor).getByRole('button', { name: 'Guardar borrador' }))
    expect(await within(editor).findByText('Agregue o marque al menos una línea.')).toBeInTheDocument()
    expect(sent('POST', '/api/v1/billing/credit-notes')).toHaveLength(0)
  })

  it('nota libre: pide cliente, descripción, cantidad, monto y tipo de cargo', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Notas de crédito' }))
    await user.click(await screen.findByRole('button', { name: 'Nueva nota de crédito' }))
    const editor = await screen.findByRole('dialog', { name: 'Nueva nota de crédito' })
    await user.click(within(editor).getByLabelText(/Libre/))
    await user.selectOptions(within(editor).getByLabelText('Motivo'), 'SERVICE_FAILURE')
    await user.click(within(editor).getByRole('button', { name: 'Guardar borrador' }))
    expect(await within(editor).findByText('Elija el cliente de la nota.')).toBeInTheDocument()
    expect(sent('POST', '/api/v1/billing/credit-notes')).toHaveLength(0)
  })

  it('desde la ficha de la factura se crea la nota con esa factura', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Facturas' }))
    await user.click(await screen.findByText('FAC-00001'))
    const invoice = await screen.findByRole('dialog', { name: /Factura FAC-00001/ })
    await user.click(await within(invoice).findByRole('button', { name: 'Crear nota de crédito' }))
    const editor = await screen.findByRole('dialog', { name: 'Nueva nota de crédito' })
    expect(await within(editor).findByText(/Sobre la factura FAC-00001/)).toBeInTheDocument()
    expect(within(editor).queryByText('Cambiar de factura')).toBeNull()   // ya viene de una factura
  })

  it('sin billing.credit no hay botones para crear ni para cambiar el estado', async () => {
    const user = userEvent.setup()
    wrap(['billing.view'])
    await user.click(await screen.findByRole('tab', { name: 'Notas de crédito' }))
    expect(await screen.findByText('NC-00001')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Nueva nota de crédito' })).toBeNull()
    await user.click(screen.getByText('NC-00001'))
    const dialog = await screen.findByRole('dialog', { name: /Nota de crédito NC-00001/ })
    expect(within(dialog).queryByRole('button', { name: 'Aprobar' })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Anular la nota' })).toBeNull()
  })
})
