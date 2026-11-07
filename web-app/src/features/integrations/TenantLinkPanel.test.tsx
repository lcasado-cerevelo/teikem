// Pruebas de «Compañías enlazadas» (Lote 227, P7) sobre un fetch simulado: los enlaces con su estado, proponer (cuerpo y validación), aceptar eligiendo el cliente, terminar con motivo, las entregas
// con Reintentar y «Revisar ahora», la entrega enlazada en la ficha de la orden y los permisos (sin intercompany.manage no hay acciones).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { LinkedDeliveryInfo } from './LinkedDeliveryInfo'
import { TenantLinkPanel } from './TenantLinkPanel'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    const p = url.pathname
    const base = '/api/v1/integrations/tenant-links'
    if (req.method === 'GET' && p === base) return json(LINKS)
    if (req.method === 'GET' && p === `${base}/options`) return json({ companies: [{ publicId: 'co-2', name: 'Advance Logistics', hasModule: true, hasTransport: true }], weHaveTransport: false })
    if (req.method === 'GET' && p === `${base}/deliveries`) return json({ items: DELIVERIES, total: 2 })
    if (req.method === 'GET' && p === '/api/v1/clients') return json([{ publicId: 'cl-1', code: 'ADV', name: 'Advance Solutions (cliente)' }])
    if (req.method === 'GET' && p === `${base}/deliveries/d2/proof`) return json({ recipientName: 'María Rivera', deliveredAtUtc: '2026-11-07T15:00:00Z', files: [{ publicId: 'f1', fileName: 'firma.png' }] })
    if (req.method === 'POST' && p === base) return json({ ...LINKS[0], publicId: 'new' })
    if (req.method === 'POST' && p === `${base}/l2/accept`) return json({ ...LINKS[1], status: 'ACTIVE' })
    if (req.method === 'POST' && p === `${base}/l1/end`) return json({ ...LINKS[0], status: 'ENDED' })
    if (req.method === 'POST' && p === `${base}/deliveries/d1/retry`) return json({ ...DELIVERIES[0], state: 'PENDING' })
    if (req.method === 'POST' && p === `${base}/deliveries/d2/sync`) return json(DELIVERIES[1])
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const LINKS = [
  { publicId: 'l1', role: 'CLIENT', otherCompanyPublicId: 'co-2', otherCompanyName: 'Advance Logistics', status: 'ACTIVE', proposedByUs: true, proposedByName: 'Luis', proposedAtUtc: '2026-11-01T12:00:00Z', providerClientName: null, canRespond: false, canEnd: true, deliveryCount: 2 },
  { publicId: 'l2', role: 'PROVIDER', otherCompanyPublicId: 'co-3', otherCompanyName: 'Advance Depot', status: 'PROPOSED', proposedByUs: false, proposedByName: 'Ana', proposedAtUtc: '2026-11-02T12:00:00Z', canRespond: true, canEnd: false, deliveryCount: 0 },
]
const DELIVERIES = [
  { publicId: 'd1', state: 'FAILED', error: 'No hay tarifa vigente para STANDARD/BOX.', attempts: 1, providerCompanyName: 'Advance Logistics', salesOrderNumber: 'OV-00012', pickBatchNumber: 'EMP-00031', createdAtUtc: '2026-11-05T12:00:00Z' },
  {
    publicId: 'd2', state: 'CREATED', attempts: 1, providerCompanyName: 'Advance Logistics', providerOrderPublicId: 'po2', providerOrderNumber: 'PR0123-000045', providerStatusLabel: 'Entregada', isFinal: true,
    deliveredAtUtc: '2026-11-07T15:00:00Z', deliveredTo: 'María Rivera', salesOrderNumber: 'OV-00013', pickBatchNumber: 'EMP-00032', createdAtUtc: '2026-11-06T12:00:00Z',
  },
]

const MODULES = ['INTEGRATIONS', 'INTERCOMPANY', 'LTL_GROUND']

function sessionOf(perms: string[]) {
  return {
    me: { tenantName: 'Advance Solutions' }, isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
    permissions: new Set(perms), modules: new Set(MODULES), reloadMe: async () => {},
  } as unknown as Session
}

function wrap(ui: React.ReactNode, perms: string[] = ['intercompany.view', 'intercompany.manage', 'integrations.view', 'clients.read']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={sessionOf(perms)}>
        <AccessProvider permissions={perms} modules={MODULES}>
          <MemoryRouter>{ui}</MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

describe('TenantLinkPanel', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('lista los enlaces con su estado y el papel de esta compañía', async () => {
    wrap(<TenantLinkPanel />)
    const active = (await screen.findByText('Advance Logistics', { selector: 'b' })).closest('tr') as HTMLElement
    expect(within(active).getByText('Activo')).toBeInTheDocument()
    expect(within(active).getByText('Somos cliente de Advance Logistics: sus entregas las hace esa compañía.')).toBeInTheDocument()
    const proposed = screen.getByText('Advance Depot', { selector: 'b' }).closest('tr') as HTMLElement
    expect(within(proposed).getByText('Propuesto')).toBeInTheDocument()
    expect(within(proposed).getByText('La propuso la otra compañía')).toBeInTheDocument()
  })

  it('propone un enlace: pide la compañía y manda papel y compañía', async () => {
    const user = userEvent.setup()
    wrap(<TenantLinkPanel />)
    await user.click(await screen.findByRole('button', { name: /Proponer enlace/ }))
    await user.click(screen.getByRole('button', { name: 'Proponer' }))
    expect(await screen.findByText('Indique la compañía con la que se quiere enlazar.')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.method === 'POST')).toBe(false)
    await user.selectOptions(await within(screen.getByRole('dialog')).findByLabelText(/^Compañía/), 'co-2')
    await user.click(screen.getByRole('button', { name: 'Proponer' }))
    await waitFor(() => expect(mock.requests.find((r) => r.method === 'POST')?.body).toMatchObject({ companyPublicId: 'co-2', role: 'CLIENT', clientPublicId: null }))
  })

  it('al aceptar como proveedor se elige el cliente y se manda', async () => {
    const user = userEvent.setup()
    wrap(<TenantLinkPanel />)
    const row = (await screen.findByText('Advance Depot', { selector: 'b' })).closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Aceptar' }))
    await user.click(screen.getByRole('button', { name: 'Aceptar enlace' }))
    expect(await screen.findByText(/Indique el cliente de su compañía/)).toBeInTheDocument()
    await user.selectOptions(await screen.findByLabelText(/^Cliente de esta compañía/), 'cl-1')
    await user.click(screen.getByRole('button', { name: 'Aceptar enlace' }))
    await waitFor(() => expect(mock.requests.find((r) => r.path.endsWith('/l2/accept'))?.body).toMatchObject({ clientPublicId: 'cl-1' }))
  })

  it('terminar pide el motivo', async () => {
    const user = userEvent.setup()
    wrap(<TenantLinkPanel />)
    const row = (await screen.findByText('Advance Logistics', { selector: 'b' })).closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Terminar' }))
    await user.click(screen.getByRole('button', { name: 'Terminar enlace' }))
    expect(await screen.findByText('Escriba el motivo.')).toBeInTheDocument()
    await user.type(screen.getByLabelText(/^Motivo/), 'Ya no despachamos por ahí')
    await user.click(screen.getByRole('button', { name: 'Terminar enlace' }))
    await waitFor(() => expect(mock.requests.find((r) => r.path.endsWith('/l1/end'))?.body).toEqual({ reason: 'Ya no despachamos por ahí' }))
  })

  it('entregas: Reintentar solo en las fallidas y «Revisar ahora» solo en las creadas', async () => {
    const user = userEvent.setup()
    wrap(<TenantLinkPanel />)
    const failed = (await screen.findByText('OV-00012')).closest('tr') as HTMLElement
    expect(within(failed).getByText('No hay tarifa vigente para STANDARD/BOX.')).toBeInTheDocument()
    expect(within(failed).queryByRole('button', { name: 'Revisar ahora' })).toBeNull()
    const created = screen.getByText('OV-00013').closest('tr') as HTMLElement
    expect(within(created).getByText('PR0123-000045')).toBeInTheDocument()
    expect(within(created).queryByRole('button', { name: 'Reintentar' })).toBeNull()
    await user.click(within(failed).getByRole('button', { name: 'Reintentar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path.endsWith('/deliveries/d1/retry'))).toBe(true))
    await user.click(within(created).getByRole('button', { name: 'Revisar ahora' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path.endsWith('/deliveries/d2/sync'))).toBe(true))
  })

  it('sin intercompany.manage no hay acciones', async () => {
    wrap(<TenantLinkPanel />, ['intercompany.view', 'integrations.view'])
    await screen.findByText('OV-00012')
    expect(screen.queryByRole('button', { name: /Proponer enlace/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reintentar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Aceptar' })).toBeNull()
  })

  it('la ficha de la orden de venta muestra la orden de allá, quién recibió y el comprobante', async () => {
    const user = userEvent.setup()
    wrap(<LinkedDeliveryInfo delivery={DELIVERIES[1]} />)
    expect(screen.getByText('PR0123-000045')).toBeInTheDocument()
    expect(screen.getAllByText('Entregada').length).toBeGreaterThan(0)
    expect(screen.getByText(/Recibió María Rivera/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Ver comprobante' }))
    expect(await screen.findByRole('button', { name: 'firma.png' })).toBeInTheDocument()
  })
})
