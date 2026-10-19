// Pruebas de «Integraciones» (Lote 132) sobre un fetch simulado: la lista con conector, horario y último resultado; el contador de la bandeja; la bandeja con su motivo, corregir y descartar; el
// alta (validaciones y cuerpo que se manda) y los permisos (sin integrations.create no hay «Nueva integración»; sin integrations.inbox no hay pestaña de bandeja).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import IntegrationsScreen from './IntegrationsScreen'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text && !text.startsWith('--') ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/integrations') return json(INTEGRATIONS)
    if (req.method === 'GET' && p === '/api/v1/integrations/options') return json(OPTIONS)
    if (req.method === 'GET' && p === '/api/v1/integrations/inbox') return json({ items: INBOX, total: 1, pending: 1 })
    if (req.method === 'GET' && p === '/api/v1/integrations/inbox/5') return json(INBOX_ITEM)
    if (req.method === 'GET' && p === '/api/v1/clients') return json([{ publicId: 'c1', code: 'AXC', name: 'AxisCare' }])
    if (req.method === 'GET' && p === '/api/v1/import-templates') return json([{ publicId: 't1', name: 'Plantilla AxisCare', isActive: true, clientPublicId: 'c1', columns: [] }])
    if (req.method === 'POST' && p === '/api/v1/integrations') return json({ ...INTEGRATIONS[0], name: (body as { name: string }).name })
    if (req.method === 'PATCH' && p === '/api/v1/integrations/inbox/5') return json(INBOX_ITEM)
    if (req.method === 'POST' && p === '/api/v1/integrations/inbox/5/discard') return json({ ...INBOX[0], status: 'DISCARDED' })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const INTEGRATIONS = [
  {
    id: 1, publicId: 'i1', name: 'Órdenes de AxisCare', description: 'SFTP de AxisCare', direction: 'INBOUND', clientPublicId: 'c1', clientName: 'AxisCare', connectorKind: 'FOLDER', folder: 'axiscare',
    templatePublicId: 't1', templateName: 'Plantilla AxisCare', scheduleKind: 'INTERVAL', intervalSeconds: 300, nextRunUtc: null, scopes: ['files.orders_import'], serviceUserName: 'Integración Órdenes de AxisCare',
    notifyUsers: [], retentionDays: 30, isActive: true, lastStartedUtc: null, lastFinishedUtc: '2026-10-19T12:00:00Z', lastStatus: 'PARTIAL', lastMessage: '1 archivo(s): 14 orden(es) creada(s), 3 a la bandeja.', pendingInbox: 3, createdAtUtc: '2026-10-19T10:00:00Z',
  },
  {
    id: 2, publicId: 'i2', name: 'Subidas de Truss', direction: 'INBOUND', clientPublicId: 'c2', clientName: 'Truss', connectorKind: 'MANUAL', scheduleKind: 'MANUAL', scopes: ['files.orders_import'],
    notifyUsers: [], retentionDays: 30, isActive: true, lastStatus: 'NEVER', pendingInbox: 0, createdAtUtc: '2026-10-19T10:00:00Z',
  },
]
const OPTIONS = {
  scopes: [{ code: 'files.orders_import', family: 'FILES', label: 'Entrada de órdenes por archivo', moduleKey: 'LTL_GROUND', available: true }],
  connectors: ['FOLDER', 'MANUAL'],
  users: [{ id: 3, name: 'Dalyshamel', email: 'd@x.com' }],
}
const INBOX = [{ id: 5, integrationPublicId: 'i1', integrationName: 'Órdenes de AxisCare', runId: 9, fileName: 'ordenes.csv', sourceRow: 6, referenceKey: 'C-100', reason: 'La orden no trae ninguna pieza (todas sus líneas son descripción o factura).', status: 'PENDING', createdAtUtc: '2026-10-19T12:00:00Z', hasData: true }]
const INBOX_ITEM = { ...INBOX[0], header: ['orden', 'tipo'], rows: [{ row: 6, cells: ['C-100', 'Drum'] }] }

function sessionOf(perms: string[]) {
  return {
    me: { tenantName: 'Advance Logistics' }, isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
    permissions: new Set(perms), modules: new Set(['INTEGRATIONS', 'LTL_GROUND', 'CATALOG']), reloadMe: async () => {},
  } as unknown as Session
}

function wrap(perms: string[] = ['integrations.view', 'integrations.create', 'integrations.manage', 'integrations.inbox'], url = '/system/integrations') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={sessionOf(perms)}>
        <AccessProvider permissions={perms} modules={['INTEGRATIONS', 'LTL_GROUND', 'CATALOG']}>
          <MemoryRouter initialEntries={[url]}>
            <IntegrationsScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

describe('IntegrationsScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('lista las integraciones con conector, horario, último resultado y los pendientes de la bandeja', async () => {
    wrap()
    const row = (await screen.findByText('Órdenes de AxisCare')).closest('tr') as HTMLElement
    expect(within(row).getByText('Carpeta de entrada')).toBeInTheDocument()
    expect(within(row).getByText('axiscare')).toBeInTheDocument()
    expect(within(row).getByText('Cada 5 min')).toBeInTheDocument()
    expect(within(row).getByText('Con pendientes')).toBeInTheDocument()
    expect(within(row).getByText('3')).toBeInTheDocument()
    const manual = (await screen.findByText('Subidas de Truss')).closest('tr') as HTMLElement
    expect(within(manual).getByText('Subida manual')).toBeInTheDocument()
    expect(within(manual).getByText('Solo a mano')).toBeInTheDocument()
    expect(within(manual).getByText('Sin correr')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Bandeja (3)' })).toBeInTheDocument()
  })

  it('«Correr ahora» solo en las de carpeta; subir archivo en todas', async () => {
    wrap()
    const folder = (await screen.findByText('Órdenes de AxisCare')).closest('tr') as HTMLElement
    const manual = (await screen.findByText('Subidas de Truss')).closest('tr') as HTMLElement
    expect(within(folder).getByRole('button', { name: 'Correr ahora' })).toBeInTheDocument()
    expect(within(manual).queryByRole('button', { name: 'Correr ahora' })).toBeNull()
    expect(within(manual).getByRole('button', { name: 'Subir archivo' })).toBeInTheDocument()
  })

  it('sin permisos de crear, administrar ni bandeja no hay alta, acciones ni pestaña de bandeja', async () => {
    wrap(['integrations.view'])
    const row = (await screen.findByText('Órdenes de AxisCare')).closest('tr') as HTMLElement
    expect(screen.queryByRole('button', { name: 'Nueva integración' })).toBeNull()
    expect(screen.queryByRole('tab', { name: /Bandeja/ })).toBeNull()
    expect(within(row).queryByRole('button', { name: 'Correr ahora' })).toBeNull()
    expect(within(row).queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(within(row).getByRole('button', { name: 'Historial' })).toBeInTheDocument()
  })

  it('el alta valida en cliente con el mensaje exacto y manda el cuerpo con el cliente, la plantilla, la carpeta y los alcances', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva integración' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva integración' })
    await user.click(within(dialog).getByRole('button', { name: 'Crear integración' }))
    expect(await within(dialog).findByText('El nombre de la integración es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('Escoja al menos un alcance para la integración.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el cliente de las órdenes de esta integración.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el nombre de la carpeta de entrada.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Nueva de prueba')
    await user.selectOptions(within(dialog).getByLabelText(/^Cliente/), 'c1')
    await waitFor(() => expect(within(dialog).getByRole('option', { name: 'Plantilla AxisCare' })).toBeInTheDocument())
    await user.selectOptions(within(dialog).getByLabelText(/^Plantilla de importación/), 't1')
    await user.type(within(dialog).getByLabelText(/^Carpeta de entrada/), '../fuera')
    await user.click(within(dialog).getByRole('button', { name: 'Crear integración' }))
    expect(await within(dialog).findByText('El nombre de la carpeta solo admite letras, números, guion y guion bajo (sin rutas).')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText(/^Carpeta de entrada/))
    await user.type(within(dialog).getByLabelText(/^Carpeta de entrada/), 'nueva')
    await user.click(await within(dialog).findByRole('switch', { name: 'Entrada de órdenes por archivo' }))
    await user.click(within(dialog).getByRole('button', { name: 'Crear integración' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/integrations')).toBe(true))
    const post = mock.requests.find((r) => r.method === 'POST' && r.path === '/api/v1/integrations')!.body
    expect(post).toMatchObject({
      name: 'Nueva de prueba', clientPublicId: 'c1', templatePublicId: 't1', connectorKind: 'FOLDER', folder: 'nueva', scheduleKind: 'MANUAL', scopes: ['files.orders_import'], includeMe: true, retentionDays: 30,
    })
  })

  it('la bandeja muestra el motivo, deja corregir las celdas y descartar con nota', async () => {
    const user = userEvent.setup()
    wrap(undefined, '/system/integrations?tab=inbox')
    expect(await screen.findByText('C-100')).toBeInTheDocument()
    expect(screen.getByText(/no trae ninguna pieza/)).toBeInTheDocument()
    expect(screen.getAllByText('Pendiente').length).toBeGreaterThan(0)

    await user.click(screen.getByRole('button', { name: 'Corregir' }))
    const fix = await screen.findByRole('dialog', { name: /Corregir/ })
    const cell = await within(fix).findByLabelText('Fila 6, columna tipo')
    await user.clear(cell)
    await user.type(cell, 'Box')
    await user.click(within(fix).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PATCH' && r.path === '/api/v1/integrations/inbox/5')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PATCH')!.body).toEqual({ rows: [{ row: 6, cells: ['C-100', 'Box'] }] })

    await user.click(screen.getByRole('button', { name: 'Descartar' }))
    const discard = await screen.findByRole('dialog', { name: /Descartar/ })
    await user.type(within(discard).getByLabelText(/^Nota/), 'El cliente la manda de nuevo')
    await user.click(within(discard).getByRole('button', { name: 'Descartar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/integrations/inbox/5/discard')).toBe(true))
    expect(mock.requests.find((r) => r.path.endsWith('/discard'))!.body).toEqual({ note: 'El cliente la manda de nuevo' })
  })
})
