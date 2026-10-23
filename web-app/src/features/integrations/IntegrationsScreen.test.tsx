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
    if (req.method === 'GET' && p === '/api/v1/integrations/proposals') return json({ items: url.searchParams.get('status') === 'PENDING' ? PROPOSALS : [], total: url.searchParams.get('status') === 'PENDING' ? 1 : 0, pending: 1 })
    if (req.method === 'POST' && p === '/api/v1/integrations/proposals/p1/confirm') return json({ ...PROPOSALS[0], status: 'CONFIRMED', note: (body as { note?: string })?.note ?? null })
    if (req.method === 'POST' && p === '/api/v1/integrations/proposals/p1/reject') return json({ ...PROPOSALS[0], status: 'REJECTED', note: (body as { note?: string })?.note ?? null })
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
    id: 1, publicId: 'i1', name: 'Envíos de AxisCare', description: 'SFTP de AxisCare', direction: 'INBOUND', clientPublicId: 'c1', clientName: 'AxisCare', connectorKind: 'FOLDER', folder: 'axiscare',
    templatePublicId: 't1', templateName: 'Plantilla AxisCare', scheduleKind: 'INTERVAL', intervalSeconds: 300, nextRunUtc: null, scopes: ['files.orders_import'], serviceUserName: 'Integración Envíos de AxisCare',
    notifyUsers: [], retentionDays: 30, isActive: true, lastStartedUtc: null, lastFinishedUtc: '2026-10-19T12:00:00Z', lastStatus: 'PARTIAL', lastMessage: '1 archivo(s): 14 envío(es) creada(s), 3 a la bandeja.', pendingInbox: 3, createdAtUtc: '2026-10-19T10:00:00Z',
  },
  {
    id: 2, publicId: 'i2', name: 'Subidas de Truss', direction: 'INBOUND', clientPublicId: 'c2', clientName: 'Truss', connectorKind: 'MANUAL', scheduleKind: 'MANUAL', scopes: ['files.orders_import'],
    notifyUsers: [], retentionDays: 30, isActive: true, lastStatus: 'NEVER', pendingInbox: 0, createdAtUtc: '2026-10-19T10:00:00Z',
  },
]
const OPTIONS = {
  scopes: [{ code: 'files.orders_import', family: 'FILES', label: 'Entrada de envíos por archivo', moduleKey: 'LTL_GROUND', available: true }],
  connectors: ['FOLDER', 'MANUAL'],
  users: [{ id: 3, name: 'Dalyshamel', email: 'd@x.com' }],
}
const INBOX = [{ id: 5, integrationPublicId: 'i1', integrationName: 'Envíos de AxisCare', runId: 9, fileName: 'ordenes.csv', sourceRow: 6, referenceKey: 'C-100', reason: 'El envío no trae ninguna pieza (todas sus líneas son descripción o factura).', status: 'PENDING', createdAtUtc: '2026-10-19T12:00:00Z', hasData: true }]
const PROPOSALS = [{
  publicId: 'p1', kind: 'COMPLETE_ORDER', referenceKey: '202608241109', orderNumber: 'ORD-77', orderPublicId: 'o1', toStatus: 'PICKUP', status: 'PENDING', summary: 'Llevar la orden ORD-77 a PICKUP según ruta-completada.pdf.',
  document: 'ruta-completada.pdf', integrationName: 'Cierres de CMS', createdAtUtc: '2026-10-23T12:00:00Z',
}]
const INBOX_ITEM = { ...INBOX[0], header: ['envío', 'tipo'], rows: [{ row: 6, cells: ['C-100', 'Drum'] }] }

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
    const row = (await screen.findByText('Envíos de AxisCare')).closest('tr') as HTMLElement
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
    const folder = (await screen.findByText('Envíos de AxisCare')).closest('tr') as HTMLElement
    const manual = (await screen.findByText('Subidas de Truss')).closest('tr') as HTMLElement
    expect(within(folder).getByRole('button', { name: 'Correr ahora' })).toBeInTheDocument()
    expect(within(manual).queryByRole('button', { name: 'Correr ahora' })).toBeNull()
    expect(within(manual).getByRole('button', { name: 'Subir archivo' })).toBeInTheDocument()
  })

  it('sin permisos de crear, administrar ni bandeja no hay alta, acciones ni pestaña de bandeja', async () => {
    wrap(['integrations.view'])
    const row = (await screen.findByText('Envíos de AxisCare')).closest('tr') as HTMLElement
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
    expect(within(dialog).getByText('Indique el cliente de los envíos de esta integración.')).toBeInTheDocument()
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
    await user.click(await within(dialog).findByRole('switch', { name: 'Entrada de envíos por archivo' }))
    await user.click(within(dialog).getByRole('button', { name: 'Crear integración' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/integrations')).toBe(true))
    const post = mock.requests.find((r) => r.method === 'POST' && r.path === '/api/v1/integrations')!.body
    expect(post).toMatchObject({
      name: 'Nueva de prueba', clientPublicId: 'c1', templatePublicId: 't1', connectorKind: 'FOLDER', folder: 'nueva', scheduleKind: 'MANUAL', scopes: ['files.orders_import'], includeMe: true, retentionDays: 30,
    })
  })

  it('«Usar un modelo» agrega el lector de CMS ya armado: destino, texto que empieza cada registro y el campo que se lee del encabezado', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva integración' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva integración' })
    await user.selectOptions(within(dialog).getByLabelText(/^Conector/), 'EMAIL')
    await user.click(await within(dialog).findByTestId('reader-preset-cms-referral-order'))
    expect(await within(dialog).findByDisplayValue('Hoja de ruta de CMS')).toBeInTheDocument()
    expect(within(dialog).getByDisplayValue('Order #:')).toBeInTheDocument()   // el texto que empieza cada orden del documento
    expect(within(dialog).getByLabelText(/Texto que empieza cada registro/)).toHaveValue('Order #:')
    expect(within(dialog).getAllByRole('switch', { name: /del encabezado/ }).length).toBeGreaterThan(0)
    // un segundo clic no repite el nombre: se numera
    await user.click(within(dialog).getByTestId('reader-preset-cms-referral-order'))
    expect(await within(dialog).findByDisplayValue('Hoja de ruta de CMS 2')).toBeInTheDocument()
  })

  it('la pestaña Propuestas muestra lo que dejó el lector de cierre y confirmar o rechazar lleva la nota; sin integrations.inbox no hay pestaña', async () => {
    const user = userEvent.setup()
    wrap(undefined, '/system/integrations?tab=proposals')
    expect(await screen.findByRole('tab', { name: 'Propuestas (1)' })).toBeInTheDocument()
    const row = (await screen.findByText('ORD-77')).closest('tr') as HTMLElement
    expect(within(row).getByText('Por confirmar')).toBeInTheDocument()
    expect(within(row).getByText('Recogida')).toBeInTheDocument()
    expect(within(row).getByText('ruta-completada.pdf')).toBeInTheDocument()

    await user.click(within(row).getByRole('button', { name: 'Confirmar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Confirmar el cierre de la orden ORD-77' })
    await user.type(within(dialog).getByLabelText(/Nota/), 'Verificado')
    await user.click(within(dialog).getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/integrations/proposals/p1/confirm')).toBe(true))
    expect(mock.requests.find((r) => r.path === '/api/v1/integrations/proposals/p1/confirm')!.body).toEqual({ note: 'Verificado' })

    await user.click(within((await screen.findByText('ORD-77')).closest('tr') as HTMLElement).getByRole('button', { name: 'Rechazar' }))
    const reject = await screen.findByRole('dialog', { name: 'Rechazar la propuesta de la orden ORD-77' })
    await user.click(within(reject).getByRole('button', { name: 'Rechazar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/integrations/proposals/p1/reject')).toBe(true))
  })

  it('sin integrations.inbox no hay pestaña de propuestas', async () => {
    wrap(['integrations.view'])
    await screen.findByText('Envíos de AxisCare')
    expect(screen.queryByRole('tab', { name: /Propuestas/ })).toBeNull()
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
