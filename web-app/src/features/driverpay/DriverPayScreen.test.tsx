// Pruebas de «Liquidación a choferes» (Lote 96) sobre un fetch simulado: la lista, nueva liquidación con vista previa, la ficha con su flujo (revisar, aprobar), el ajuste de una línea, cancelar, exportar y los intentos.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import DriverPayScreen from './DriverPayScreen'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  status: 'GENERATED',
  download: vi.fn(),
  failGenerate: false,
}))

vi.mock('../billing/exportFile', () => ({ downloadExportFile: mock.download }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    const m = req.method
    if (m === 'GET' && p === '/api/v1/driver-settlements') return json({ items: [ITEM()], total: 1, skip: 0, take: 25 })
    if (m === 'POST' && p === '/api/v1/driver-settlements/preview') return json({ payoutFormulaCode: 'DELIVERY_PLUS_ATTEMPTS', lineCount: 2, totalAmount: 58, skippedNoDriver: 1, lines: LINES })
    if (m === 'POST' && p === '/api/v1/driver-settlements') {
      if (mock.failGenerate) return problem(422, 'No hay nada por liquidar en el período.')
      return json({ ...DTO(), publicId: SET })
    }
    if (m === 'GET' && p === `/api/v1/driver-settlements/${SET}`) return json(DTO())
    const act = /\/api\/v1\/driver-settlements\/[^/]+\/(review|approve|cancel)$/.exec(p)
    if (m === 'POST' && act) {
      mock.status = { review: 'REVIEWED', approve: 'APPROVED', cancel: 'CANCELLED' }[act[1] as 'review'] as string
      return json(DTO())
    }
    if (m === 'PATCH' && p === `/api/v1/driver-settlements/${SET}/lines/1`) return json(DTO())
    if (m === 'POST' && p === `/api/v1/driver-settlements/${SET}/export`) return json({ fileName: 'liquidacion-LIQ-00001.csv', contentType: 'text/csv', text: 'a,b', rowCount: 2, templateName: 'Formato estándar' })
    if (m === 'GET' && p === '/api/v1/export-templates') return json([{ publicId: TPL, scope: 'DRIVER_SETTLEMENT', name: 'Formato estándar', format: 'CSV', delimiter: ',', dateFormat: 'YYYY-MM-DD', includeHeader: true, columns: [] }])
    if (m === 'GET' && p === '/api/v1/drivers') return json([{ id: 1, publicId: DRIVER, code: 'D1', fullName: 'Ana Chofer', isActive: true }])
    if (m === 'GET' && p === '/api/v1/delivery-attempts') return json({ items: [ATTEMPT], total: 1, skip: 0, take: 25 })
    if (m === 'POST' && p === '/api/v1/delivery-attempts') return json(ATTEMPT)
    if (m === 'POST' && /\/api\/v1\/delivery-attempts\/[^/]+\/void$/.test(p)) return json({ ...ATTEMPT, isActive: false, voidReason: 'x' })
    if (m === 'GET' && p === '/api/v1/orders') return json({ items: [{ publicId: ORDER, orderNumber: 'ORD-00001', clientInvoiceNumber: 'FAC-1', consigneeName: 'Hospital Norte' }], total: 1 })
    if (m === 'GET' && p === '/api/v1/catalogs/DeliveryFailureReason') return json([{ internalCode: 'ABSENT', label: 'Ausente', sortOrder: 1, isEnabled: true }])
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

const SET = '99999999-0000-0000-0000-000000000009'
const TPL = 'aaaaaaaa-0000-0000-0000-00000000000a'
const DRIVER = '88888888-0000-0000-0000-000000000008'
const ORDER = '77777777-0000-0000-0000-000000000007'
const LABEL: Record<string, string> = { GENERATED: 'Generada', REVIEWED: 'Revisada', APPROVED: 'Aprobada', EXPORTED: 'Exportada', CANCELLED: 'Cancelada' }
const LINES = [
  {
    id: 1, driverPublicId: DRIVER, driverCode: 'D1', driverName: 'Ana Chofer', kind: 'DELIVERY', serviceDate: '2026-10-16', orderPublicId: ORDER, orderNumber: 'ORD-00001', description: 'Orden ORD-00001', attemptCount: 2,
    breakdown: [{ kind: 'DELIVERY', attemptNumber: 2, amount: 10 }, { kind: 'ATTEMPT', attemptNumber: 1, amount: 5 }, { kind: 'ATTEMPT', attemptNumber: 2, amount: 3 }], rateMissing: false, calculatedAmount: 18, amount: 18, adjusted: false, adjustmentNote: null,
  },
  {
    id: 2, driverPublicId: DRIVER, driverCode: 'D1', driverName: 'Ana Chofer', kind: 'TRIP', serviceDate: '2026-10-17', description: 'Viaje: Traer vagón', attemptCount: 0,
    breakdown: [{ kind: 'TRIP', amount: 40 }], rateMissing: false, calculatedAmount: 40, amount: 40, adjusted: false, adjustmentNote: null,
  },
]
const ITEM = () => ({ publicId: SET, number: 'LIQ-00001', statusCode: mock.status, status: LABEL[mock.status], statusColor: '#3B82F6', driverPublicId: DRIVER, driverCode: 'D1', driverName: 'Ana Chofer', periodStart: '2026-10-10', periodEnd: '2026-10-17', lineCount: 2, totalAmount: 58, payoutFormulaCode: 'DELIVERY_PLUS_ATTEMPTS', generatedAtUtc: '2026-10-17T10:00:00Z' })
const DTO = () => ({
  ...ITEM(), currency: 'USD', deliveriesAmount: 18, tripsAmount: 40, skippedNoDriver: 0, note: null, reviewedBy: null, reviewedAtUtc: null, approvedBy: null, approvedAtUtc: null, exportedAtUtc: null, rowVersion: 'AAA=', lines: LINES,
})
const ATTEMPT = {
  id: 1, publicId: 'a1', orderPublicId: ORDER, orderNumber: 'ORD-00001', clientInvoiceNumber: 'FAC-1', attemptNumber: 1, driverPublicId: DRIVER, driverCode: 'D1', driverName: 'Ana Chofer', tripCode: null, attemptedAtUtc: '2026-10-16T12:00:00Z',
  delivered: false, failureReasonCode: 'ABSENT', failureReason: 'Ausente', notes: null, source: 'MANUAL', settled: false, settledRunId: null, isActive: true, voidReason: null,
}

const ALL = ['driverpay.view', 'driverpay.manage', 'driverpay.approve', 'driverpay.export', 'billing.view', 'fleet.view']
const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(ALL),
  modules: new Set(['LTL_GROUND', 'BILLING']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(permissions: string[] = ALL) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={new Set(permissions)} modules={SESSION.modules}>
          <MemoryRouter>
            <DriverPayScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

async function openSettlement(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByText('LIQ-00001'))
  return screen.findByRole('dialog', { name: /Liquidación LIQ-00001/ })
}

describe('Liquidación a choferes', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.status = 'GENERATED'
    mock.failGenerate = false
    mock.download.mockReset()
  })

  it('la lista muestra el número, el chofer, el período, el total y el estatus', async () => {
    wrap()
    expect(await screen.findByText('LIQ-00001')).toBeInTheDocument()
    expect(screen.getAllByText('D1 · Ana Chofer').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Generada').length).toBeGreaterThan(0)   // el chip de la fila (y la opción del filtro)
    expect(screen.getByText('$58.00')).toBeInTheDocument()
  })

  it('la ficha agrupa las líneas por día con el desglose y los subtotales', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openSettlement(user)
    expect(await within(dialog).findByText('Orden ORD-00001')).toBeInTheDocument()
    expect(within(dialog).getByText(/entrega \$10\.00 · intento 1 \$5\.00 · intento 2 \$3\.00/)).toBeInTheDocument()
    expect(within(dialog).getByText('Subtotal $18.00')).toBeInTheDocument()
    expect(within(dialog).getByText('Subtotal $40.00')).toBeInTheDocument()
    expect(within(dialog).getByText('Viaje')).toBeInTheDocument()
    expect(within(dialog).getByText(/Revise las líneas/)).toBeInTheDocument()
  })

  it('revisar y aprobar mandan cada acción; aprobar solo aparece revisada y con driverpay.approve', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openSettlement(user)
    expect(within(dialog).queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument()
    await user.click(await within(dialog).findByRole('button', { name: 'Marcar revisada' }))
    await waitFor(() => expect(sent('POST', `/api/v1/driver-settlements/${SET}/review`)).toHaveLength(1))
    await user.click(await within(dialog).findByRole('button', { name: 'Aprobar' }))   // ya revisada: aparece Aprobar
    await waitFor(() => expect(sent('POST', `/api/v1/driver-settlements/${SET}/approve`)).toHaveLength(1))
  })

  it('sin driverpay.approve no se ve Aprobar; sin driverpay.manage no se ve Ajustar ni Cancelar', async () => {
    mock.status = 'REVIEWED'
    const user = userEvent.setup()
    wrap(['driverpay.view'])
    const dialog = await openSettlement(user)
    await within(dialog).findByText('Orden ORD-00001')
    expect(within(dialog).queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Ajustar' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Cancelar liquidación' })).not.toBeInTheDocument()
  })

  it('ajustar una línea pide el motivo si el monto cambia y manda monto y motivo', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openSettlement(user)
    await user.click((await within(dialog).findAllByRole('button', { name: 'Ajustar' }))[0])
    const adj = await screen.findByRole('dialog', { name: /Ajustar/ })
    const amount = within(adj).getByLabelText('A pagar')
    await user.clear(amount)
    await user.type(amount, '20')
    await user.click(within(adj).getByRole('button', { name: 'Guardar ajuste' }))
    expect(await within(adj).findByText('Indique el motivo del ajuste.')).toBeInTheDocument()
    expect(sent('PATCH', `/api/v1/driver-settlements/${SET}/lines/1`)).toHaveLength(0)
    await user.type(within(adj).getByLabelText('Motivo del ajuste'), 'Bono por zona')
    await user.click(within(adj).getByRole('button', { name: 'Guardar ajuste' }))
    await waitFor(() => expect(sent('PATCH', `/api/v1/driver-settlements/${SET}/lines/1`)[0]?.body).toEqual({ amount: 20, note: 'Bono por zona' }))
  })

  it('cancelar pide confirmación y manda la cancelación', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openSettlement(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Cancelar liquidación' }))
    const confirm = await screen.findByRole('dialog', { name: /Cancelar LIQ-00001/ })
    await user.click(within(confirm).getByRole('button', { name: 'Cancelar liquidación' }))
    await waitFor(() => expect(sent('POST', `/api/v1/driver-settlements/${SET}/cancel`)).toHaveLength(1))
  })

  it('nueva liquidación muestra lo que entraría, avisa de los intentos sin chofer y genera', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva liquidación' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva liquidación' })
    expect(await within(dialog).findByText('Lo que entraría (2 líneas)')).toBeInTheDocument()
    expect(within(dialog).getByText(/1 intento\(s\) del período no tienen chofer/)).toBeInTheDocument()
    expect(within(dialog).getByText(/Fórmula de pago vigente: Entrega \+ cada intento/)).toBeInTheDocument()
    expect(sent('POST', '/api/v1/driver-settlements/preview').length).toBeGreaterThan(0)
    await user.click(within(dialog).getByRole('button', { name: 'Generar liquidación' }))
    await waitFor(() => expect(sent('POST', '/api/v1/driver-settlements')).toHaveLength(1))
    const body = sent('POST', '/api/v1/driver-settlements')[0].body as { periodStart: string; periodEnd: string }
    expect(body.periodStart <= body.periodEnd).toBe(true)
    expect(await screen.findByRole('dialog', { name: /Liquidación LIQ-00001/ })).toBeInTheDocument()
  })

  it('el período al revés se avisa y no genera', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva liquidación' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva liquidación' })
    await user.clear(within(dialog).getByLabelText('Hasta'))
    await user.type(within(dialog).getByLabelText('Hasta'), '2000-01-01')
    expect(await within(dialog).findByText('La fecha final no puede ser anterior a la inicial.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Generar liquidación' })).toBeDisabled()
  })

  it('exportar descarga el archivo con la plantilla elegida', async () => {
    mock.status = 'APPROVED'
    const user = userEvent.setup()
    wrap()
    const dialog = await openSettlement(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Exportar' }))
    const exp = await screen.findByRole('dialog', { name: /Exportar LIQ-00001/ })
    await user.selectOptions(await within(exp).findByLabelText('Plantilla'), TPL)
    await user.click(within(exp).getByRole('button', { name: 'Descargar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/driver-settlements/${SET}/export`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/driver-settlements/${SET}/export`)[0].search).toContain(`templatePublicId=${TPL}`)
    await waitFor(() => expect(mock.download).toHaveBeenCalledTimes(1))
  })

  it('los intentos se listan, se registran a mano y se anulan con motivo', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Intentos de entrega' }))
    expect(await screen.findByText('ORD-00001')).toBeInTheDocument()
    expect(screen.getByText(/No entregó · Ausente/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Registrar intento' }))
    const dialog = await screen.findByRole('dialog', { name: 'Registrar un intento de entrega' })
    await user.type(within(dialog).getByLabelText('Orden'), 'ORD-0000')
    await user.click(await within(dialog).findByRole('button', { name: /ORD-00001 · FAC-1 · Hospital Norte/ }))
    await user.selectOptions(within(dialog).getByLabelText('Chofer'), DRIVER)
    await user.click(within(dialog).getByRole('button', { name: 'Registrar' }))
    await waitFor(() => expect(sent('POST', '/api/v1/delivery-attempts')[0]?.body).toMatchObject({ orderPublicId: ORDER, driverPublicId: DRIVER, delivered: true }))

    await user.click(await screen.findByRole('button', { name: 'Anular' }))
    const voiding = await screen.findByRole('dialog', { name: /Anular el intento 1 de ORD-00001/ })
    await user.click(within(voiding).getByRole('button', { name: 'Anular' }))
    expect(await within(voiding).findByText('Indique el motivo de la anulación.')).toBeInTheDocument()
    await user.type(within(voiding).getByLabelText('Motivo de la anulación'), 'error de captura')
    await user.click(within(voiding).getByRole('button', { name: 'Anular' }))
    await waitFor(() => expect(sent('POST', '/api/v1/delivery-attempts/a1/void')[0]?.body).toEqual({ reason: 'error de captura' }))
  })
})
