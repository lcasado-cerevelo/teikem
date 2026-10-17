// Pruebas de «Remesas» (Lote 87) sobre un fetch simulado: la lista, nueva remesa con los candidatos y la comisión, la ficha con su flujo (cuadrar, aprobar, remitir), cancelar, exportar y la comisión por omisión.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import CodScreen from './CodScreen'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  status: 'OPEN',
  download: vi.fn(),
  failCreate: false,
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
    if (m === 'GET' && p === '/api/v1/cod/orders') return json({ items: [], total: 0, skip: 0, take: 25 })
    if (m === 'GET' && p === '/api/v1/cod/settings') return json({ defaultCommissionPct: 8 })
    if (m === 'PUT' && p === '/api/v1/cod/settings') return json({ defaultCommissionPct: (body as { defaultCommissionPct: number }).defaultCommissionPct })
    if (m === 'GET' && p === '/api/v1/cod/remittances') return json({ items: [ITEM()], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === '/api/v1/cod/remittances/candidates') return json(CANDIDATES(url.searchParams.get('commissionPct')))
    if (m === 'POST' && p === '/api/v1/cod/remittances') {
      if (mock.failCreate) return problem(422, 'No hay cobros cuadrados de ese cliente en ese período para remitir.')
      return json({ ...DTO(), publicId: REM })
    }
    if (m === 'GET' && p === `/api/v1/cod/remittances/${REM}`) return json(DTO())
    const act = /\/api\/v1\/cod\/remittances\/[^/]+\/(review|approve|remit|cancel)$/.exec(p)
    if (m === 'POST' && act) {
      mock.status = { review: 'RECONCILED', approve: 'APPROVED', remit: 'REMITTED', cancel: mock.status }[act[1] as 'review'] as string
      return json(DTO())
    }
    if (m === 'GET' && p === `/api/v1/cod/remittances/${REM}/export`) return json({ fileName: 'remesa-REM-00001.csv', contentType: 'text/csv', text: 'a,b', rowCount: 3, templateName: 'Formato estándar' })
    if (m === 'GET' && p === '/api/v1/export-templates') return json([{ publicId: TPL, scope: 'COD_REMITTANCE', name: 'Formato estándar', format: 'CSV', delimiter: ',', dateFormat: 'YYYY-MM-DD', includeHeader: true, columns: [] }])
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

const REM = '99999999-0000-0000-0000-000000000009'
const TPL = 'aaaaaaaa-0000-0000-0000-00000000000a'
const CLIENT = '88888888-0000-0000-0000-000000000008'
const LABEL: Record<string, string> = { OPEN: 'Abierta', RECONCILED: 'Cuadrada', APPROVED: 'Aprobada', REMITTED: 'Remitida' }
const LINES = [
  { id: 1, collectionId: 11, orderPublicId: 'o1', orderNumber: 'ORD-00001', invoiceNumber: 'FAC-00001', collectedAtUtc: '2026-10-16T12:00:00Z', method: 'CASH', methodLabel: 'Efectivo', checkNumber: null, reference: null, amountCollected: 60, commissionAmount: 4.8, netAmount: 55.2 },
  { id: 2, collectionId: 12, orderPublicId: 'o1', orderNumber: 'ORD-00001', invoiceNumber: 'FAC-00001', collectedAtUtc: '2026-10-16T12:30:00Z', method: 'CHECK', methodLabel: 'Cheque', checkNumber: '5521', reference: null, amountCollected: 40, commissionAmount: 3.2, netAmount: 36.8 },
]
const ITEM = () => ({
  publicId: REM, number: 'REM-00001', status: mock.status, statusLabel: LABEL[mock.status], statusColor: '#3B82F6', clientPublicId: CLIENT, clientName: 'Farmacia Central', periodStart: '2026-10-01', periodEnd: '2026-10-17',
  grossCollected: 100, commissionPct: 8, commissionAmount: 8, netRemitted: 92, lineCount: 2, generatedAtUtc: '2026-10-17T10:00:00Z', remittedAtUtc: null,
})
const DTO = () => ({ ...ITEM(), currency: 'USD', approvedAtUtc: null, approvedByName: null, exportRef: null, lines: LINES })
const CANDIDATES = (pct: string | null) => ({
  clientPublicId: CLIENT, clientName: 'Farmacia Central', periodStart: '2026-10-01', periodEnd: '2026-10-17', commissionPct: pct ? Number(pct) : 8, commissionSource: pct ? 'REQUEST' : 'TENANT',
  grossCollected: 100, commissionAmount: pct ? Number(pct) : 8, netRemitted: pct ? 100 - Number(pct) : 92, lines: LINES,
})

const ALL = ['cod.view', 'cod.collect', 'cod.reconcile', 'cod.remit', 'billing.view', 'clients.read']
const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(ALL),
  modules: new Set(['COD', 'BILLING']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(permissions: string[] = ALL) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={new Set(permissions)} modules={SESSION.modules}>
          <MemoryRouter>
            <CodScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

async function openRemittance(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('tab', { name: 'Remesas' }))
  await user.click(await screen.findByText('REM-00001'))
  return screen.findByRole('dialog', { name: /Remesa REM-00001/ })
}

describe('Remesas COD', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.status = 'OPEN'
    mock.failCreate = false
    mock.download.mockReset()
  })

  it('la lista muestra cobrado, comisión y neto y filtra por estatus', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Remesas' }))
    expect(await screen.findByText('REM-00001')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Filtrar por estatus de la remesa'), 'APPROVED')
    await waitFor(() => expect(sent('GET', '/api/v1/cod/remittances').some((r) => r.search.includes('status=APPROVED'))).toBe(true))
  })

  it('nueva remesa: muestra los cobros que entrarían y la comisión, valida y crea con el cuerpo', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Remesas' }))
    await user.click(await screen.findByRole('button', { name: 'Nueva remesa' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva remesa' })
    expect(within(dialog).getByRole('button', { name: 'Crear remesa' })).toBeDisabled()   // sin cliente
    await user.type(within(dialog).getByLabelText('Cliente'), 'Farmacia')
    await user.click(await screen.findByRole('option', { name: /Farmacia Central/ }))
    expect(await within(dialog).findByText('Cobros que entrarían (2)')).toBeInTheDocument()
    expect(within(dialog).getByText('Se usa la comisión por omisión de la compañía: 8%.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Comisión (%)'), '150')
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('entre 0 y 100')
    await user.clear(within(dialog).getByLabelText('Comisión (%)'))
    await user.type(within(dialog).getByLabelText('Comisión (%)'), '10')
    expect(await within(dialog).findByText('Se usa la comisión que escribiste: 10%.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Crear remesa' }))
    await waitFor(() => expect(sent('POST', '/api/v1/cod/remittances')).toHaveLength(1))
    expect(sent('POST', '/api/v1/cod/remittances')[0].body).toMatchObject({ clientPublicId: CLIENT, commissionPct: 10 })
    expect(await screen.findByRole('dialog', { name: /Remesa REM-00001/ })).toBeInTheDocument()
  })

  it('si el servidor no tiene nada que remitir muestra su mensaje', async () => {
    mock.failCreate = true
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Remesas' }))
    await user.click(await screen.findByRole('button', { name: 'Nueva remesa' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva remesa' })
    await user.type(within(dialog).getByLabelText('Cliente'), 'Farmacia')
    await user.click(await screen.findByRole('option', { name: /Farmacia Central/ }))
    await within(dialog).findByText('Cobros que entrarían (2)')
    await user.click(within(dialog).getByRole('button', { name: 'Crear remesa' }))
    expect(await within(dialog).findByText('No hay cobros cuadrados de ese cliente en ese período para remitir.')).toBeInTheDocument()
  })

  it('la ficha sigue el flujo: cuadrar, aprobar y remitir con la referencia del pago', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openRemittance(user)
    expect((await within(dialog).findAllByText('FAC-00001')).length).toBeGreaterThan(0)
    await user.click(await within(dialog).findByRole('button', { name: 'Cuadrar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/cod/remittances/${REM}/review`)).toHaveLength(1))
    await user.click(await within(dialog).findByRole('button', { name: 'Aprobar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/cod/remittances/${REM}/approve`)).toHaveLength(1))
    await user.click(await within(dialog).findByRole('button', { name: 'Remitir' }))
    const remit = await screen.findByRole('dialog', { name: 'Remitir REM-00001' })
    await user.type(within(remit).getByLabelText('Referencia del pago (opcional)'), 'CHK-9001')
    await user.click(within(remit).getByRole('button', { name: 'Remitir' }))
    await waitFor(() => expect(sent('POST', `/api/v1/cod/remittances/${REM}/remit`)[0]?.body).toEqual({ exportRef: 'CHK-9001' }))
  })

  it('cancelar una abierta pide confirmación; una aprobada ya no se cancela', async () => {
    const user = userEvent.setup()
    const { unmount } = wrap()
    const dialog = await openRemittance(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Cancelar remesa' }))
    const confirm = await screen.findByRole('dialog', { name: 'Cancelar REM-00001' })
    await user.click(within(confirm).getByRole('button', { name: 'Cancelar remesa' }))
    await waitFor(() => expect(sent('POST', `/api/v1/cod/remittances/${REM}/cancel`)).toHaveLength(1))
    unmount()
    mock.status = 'APPROVED'
    wrap()
    const approved = await openRemittance(user)
    expect(await within(approved).findByRole('button', { name: 'Remitir' })).toBeInTheDocument()
    expect(within(approved).queryByRole('button', { name: 'Cancelar remesa' })).toBeNull()
  })

  it('exportar: con permisos de facturación se elige la plantilla y se descarga el archivo', async () => {
    mock.status = 'APPROVED'
    const user = userEvent.setup()
    wrap()
    const dialog = await openRemittance(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Exportar archivo' }))
    const exp = await screen.findByRole('dialog', { name: 'Exportar REM-00001' })
    await user.selectOptions(await within(exp).findByLabelText('Plantilla'), TPL)
    await user.click(within(exp).getByRole('button', { name: 'Descargar' }))
    await waitFor(() => expect(mock.download).toHaveBeenCalledTimes(1))
    expect(sent('GET', `/api/v1/cod/remittances/${REM}/export`)[0].search).toContain(`templatePublicId=${TPL}`)
  })

  it('sin permisos de facturación no se pide la lista de plantillas y se usa la estándar', async () => {
    mock.status = 'REMITTED'
    const user = userEvent.setup()
    wrap(['cod.view', 'cod.remit', 'clients.read'])
    const dialog = await openRemittance(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Exportar archivo' }))
    const exp = await screen.findByRole('dialog', { name: 'Exportar REM-00001' })
    expect(within(exp).getByText(/Se usa la plantilla estándar/)).toBeInTheDocument()
    expect(sent('GET', '/api/v1/export-templates')).toHaveLength(0)
  })

  it('la comisión por omisión se cambia con cod.remit y se valida; sin él solo se ve', async () => {
    const user = userEvent.setup()
    const { unmount } = wrap()
    await user.click(await screen.findByRole('tab', { name: 'Remesas' }))
    const input = await screen.findByLabelText('Comisión por omisión de la compañía (%)')
    await user.clear(input)
    await user.type(input, '101')
    await user.click(screen.getByRole('button', { name: 'Guardar comisión' }))
    expect(await screen.findByText('Escribe un número de 0 a 100 (hasta 2 decimales).')).toBeInTheDocument()
    await user.clear(input)
    await user.type(input, '10')
    await user.click(screen.getByRole('button', { name: 'Guardar comisión' }))
    await waitFor(() => expect(sent('PUT', '/api/v1/cod/settings')[0]?.body).toEqual({ defaultCommissionPct: 10 }))
    unmount()
    wrap(['cod.view'])
    await user.click(await screen.findByRole('tab', { name: 'Remesas' }))
    expect(await screen.findByLabelText('Comisión por omisión de la compañía (%)')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar comisión' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Nueva remesa' })).toBeNull()
  })
})
