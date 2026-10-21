// Lote F-F2 — «Vistas e informes»: galería por fuente, resultado de una vista, constructor (crear, editar, validar, errores del servidor
// con el mensaje exacto) y permisos, con el API simulado.
import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../app/queryClient'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ReportsScreen from './ReportsScreen'
import { typeDate } from '../../kernel/ui/typeDate'

beforeAll(() => setLang('es'))

type Handler = (path: string, method: string, url: URL, body: unknown) => unknown
const mock = vi.hoisted(() => ({ calls: [] as { method: string; path: string; body: unknown; search: string }[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const body = req.method !== 'GET' ? await req.clone().json().catch(() => null) : null
    mock.calls.push({ method: req.method, path: url.pathname, body, search: url.search })
    const result = (mock.handler as Handler)(url.pathname, req.method, url, body)
    if (result instanceof Response) return result
    return new Response(JSON.stringify(result ?? null), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code?: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ status, title, code, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const SOURCES = [
  {
    key: 'TRANSPORT_ORDER',
    label: 'Órdenes',
    dateField: 'CreatedAtUtc',
    defaultBusinessModule: 'OPERATIONS',
    fields: [
      { key: 'OrderNumber', label: 'Número de orden', type: 'Text', isMoney: false },
      { key: 'ClientName', label: 'Cliente', type: 'Text', isMoney: false },
      { key: 'TotalPieces', label: 'Piezas', type: 'Number', isMoney: false },
    ],
    relations: [{ key: 'Client', targetSource: 'CLIENT', label: 'Cliente' }],
    customFields: [{ key: 'cf.ref', label: 'Referencia', type: 'Text', isMoney: false }],
  },
  {
    key: 'CLIENT',
    label: 'Clientes',
    dateField: null,
    defaultBusinessModule: 'OPERATIONS',
    fields: [
      { key: 'Code', label: 'Código', type: 'Text', isMoney: false },
      { key: 'Name', label: 'Nombre', type: 'Text', isMoney: false },
    ],
    relations: [],
    customFields: [],
  },
]

const LOOKUPS: Record<string, unknown> = {
  ReportVisibility: [
    { code: 'PRIVATE', label: 'Privado', sortOrder: 1, isEnabled: true },
    { code: 'TENANT', label: 'Toda la organización', sortOrder: 2, isEnabled: true },
    { code: 'SHARED', label: 'Personas específicas', sortOrder: 3, isEnabled: true },
  ],
  DateRangeMode: [
    { code: 'LAST7', label: 'Últimos 7 días', sortOrder: 1, isEnabled: true },
    { code: 'CUSTOM', label: 'Rango personalizado', sortOrder: 2, isEnabled: true },
    { code: 'ALL', label: 'Todo el tiempo', sortOrder: 3, isEnabled: true },
  ],
}

const SYSTEM = { id: 1, baseEntityType: 'CLIENT', name: 'Clientes', isSystem: true, canEdit: false, ownerUserId: null, columns: ['Code', 'Name'], isActive: true, visibility: 'TENANT' }
const MINE = {
  id: 2,
  baseEntityType: 'TRANSPORT_ORDER',
  name: 'Piezas por cliente',
  isSystem: false,
  canEdit: true,
  ownerUserId: 1,
  ownerName: 'Ana Admin',
  columns: [],
  secondary: [],
  filterJson: '{"and":[{"field":"ClientName","op":"contains","value":"FF2"}]}',
  groupJson: '{"by":["ClientName"],"aggregates":[{"fn":"COUNT"},{"fn":"SUM","field":"TotalPieces"}],"totals":true}',
  sortJson: '[{"field":"sum_TotalPieces","dir":"desc"}]',
  visibility: 'PRIVATE',
  shares: [],
  isActive: true,
}
const OTHERS = { id: 3, baseEntityType: 'CLIENT', name: 'De Beto', isSystem: false, canEdit: false, ownerUserId: 9, ownerName: 'Beto', columns: ['Name'], isActive: true, visibility: 'TENANT' }

const RUN = {
  columns: [
    { key: 'ClientName', label: 'Cliente', type: 'Text', isMoney: false },
    { key: 'count_rows', label: 'Cantidad', type: 'Number', isMoney: false },
    { key: 'sum_TotalPieces', label: 'Suma (TotalPieces)', type: 'Number', isMoney: false },
  ],
  rows: [{ ClientName: 'Alfa', count_rows: 3, sum_TotalPieces: 10 }, { ClientName: 'Bravo', count_rows: 2, sum_TotalPieces: 5 }],
  totals: { count_rows: 5, sum_TotalPieces: 15, $count: 5 },
  total: 2,
}

let reports: unknown[] = []
let run: unknown = RUN
let permissions = ['analytics.view', 'analytics.manage', 'admin.users']

function handler(path: string, method: string, _url: URL, body: unknown): unknown {
  if (path === '/api/v1/analytics/data-sources') return SOURCES
  if (path.startsWith('/api/v1/catalogs/')) return LOOKUPS[path.split('/').pop() ?? ''] ?? []
  if (path === '/api/v1/analytics/reports' && method === 'GET') return reports
  if (/\/reports\/\d+\/run$/.test(path)) return run
  if (path.endsWith('/preview')) return { columns: [{ key: 'OrderNumber', label: 'Número de orden', type: 'Text' }], rows: [{ OrderNumber: 'ORD-1' }], total: 1 }
  if (path === '/api/v1/roles') return [{ id: 3, name: 'Supervisores', isActive: true }]
  if (path === '/api/v1/users') return [{ id: 7, fullName: 'Beto Bodega', email: 'beto@x.com' }]
  if (method === 'POST' && path.startsWith('/api/v1/analytics/reports/')) return { id: 99, name: (body as { name: string }).name, baseEntityType: 'CLIENT' }
  if (method === 'PUT') return { id: 2, name: (body as { name: string }).name }
  if (method === 'DELETE') return new Response(null, { status: 204 })
  return []
}

const SESSION = { me: { userId: 1, fullName: 'Ana Admin' }, isAuthenticated: true, isLoading: false, error: null, tenantId: 1, lang: 'es' } as unknown as Session

function Where() {
  const l = useLocation()
  return <output data-testid="where">{l.pathname + l.search}</output>
}

function renderAt(url = '/analytics/reports') {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={permissions} modules={['ANALYTICS']}>
          <MemoryRouter initialEntries={[url]}>
            <ReportsScreen />
            <Where />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  mock.calls.length = 0
  mock.handler = handler
  reports = [SYSTEM, MINE, OTHERS]
  run = RUN
  permissions = ['analytics.view', 'analytics.manage', 'admin.users']
})

const writes = () => mock.calls.filter((c) => c.method !== 'GET' && !c.path.endsWith('/run') && !c.path.endsWith('/preview'))

describe('galería', () => {
  it('un panel por fuente, abiertos, con contador, subtítulo, insignias y el vacío', async () => {
    renderAt()
    const clients = await screen.findByRole('region', { name: 'Clientes' })
    expect(within(clients).getByText('Por defecto')).toBeInTheDocument()
    expect(within(clients).getByText('Creada por Beto')).toBeInTheDocument()
    expect(within(clients).getByText('2 columnas')).toBeInTheDocument()
    expect(within(clients).getByText('1 columna')).toBeInTheDocument()
    const orders = screen.getByRole('region', { name: 'Órdenes' })
    expect(within(orders).getByText('Piezas por cliente')).toBeInTheDocument()
    expect(within(orders).getByText('Agrupado por Cliente')).toBeInTheDocument()
    // las propias no llevan insignia
    expect(within(orders).queryByText(/Creada por/)).toBeNull()
    expect(within(clients).getByRole('button', { name: /^Clientes/, expanded: true })).toBeInTheDocument()
  })

  it('una fuente sin informes explica cómo crear el primero', async () => {
    reports = [SYSTEM]
    renderAt()
    const orders = await screen.findByRole('region', { name: 'Órdenes' })
    expect(within(orders).getByText('Todavía no hay informes para esta fuente — crea el primero con "Nueva vista".')).toBeInTheDocument()
  })

  it('contraer un panel esconde sus informes', async () => {
    const user = userEvent.setup()
    renderAt()
    const orders = await screen.findByRole('region', { name: 'Órdenes' })
    await user.click(within(orders).getByRole('button', { name: /Órdenes/, expanded: true }))
    expect(within(orders).queryByText('Piezas por cliente')).toBeNull()
  })

  it('Editar y Eliminar solo en lo propio (no en las de sistema ni las de otros) y solo con analytics.manage', async () => {
    renderAt()
    await screen.findByRole('region', { name: 'Clientes' })
    expect(screen.getAllByRole('button', { name: /^Editar la vista/ })).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Editar la vista Piezas por cliente' })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /^Nueva vista de/ })).toHaveLength(2)
  })

  it('sin analytics.manage solo se ve: ni «+», ni Editar ni Eliminar', async () => {
    permissions = ['analytics.view']
    renderAt()
    await screen.findByRole('region', { name: 'Clientes' })
    expect(screen.queryByRole('button', { name: /^Nueva vista de/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Editar la vista/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Eliminar la vista/ })).toBeNull()
  })

  it('el buscador filtra por vista o por fuente', async () => {
    const user = userEvent.setup()
    renderAt()
    await screen.findByRole('region', { name: 'Clientes' })
    await user.type(screen.getByRole('searchbox'), 'beto')
    expect(screen.queryByRole('region', { name: 'Órdenes' })).toBeNull()
    expect(screen.getByText('De Beto')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'zzzz')
    expect(await screen.findByText('Ninguna vista ni fuente coincide con la búsqueda.')).toBeInTheDocument()
  })

  it('abrir una vista va a ?view=<id>', async () => {
    const user = userEvent.setup()
    renderAt()
    await user.click(await screen.findByText('Piezas por cliente'))
    expect(screen.getByTestId('where')).toHaveTextContent('/analytics/reports?view=2')
    expect(await screen.findByRole('heading', { level: 1, name: 'Piezas por cliente' })).toBeInTheDocument()
  })

  it('eliminar desde la galería pide confirmación y llama DELETE', async () => {
    const user = userEvent.setup()
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Eliminar la vista Piezas por cliente' }))
    expect(screen.getByText(/Se eliminará la vista «Piezas por cliente»/)).toBeInTheDocument()
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(writes()).toEqual([expect.objectContaining({ method: 'DELETE', path: '/api/v1/analytics/reports/2' })]))
  })

  it('el 403 del servidor al eliminar sale en el diálogo con el mensaje exacto', async () => {
    const user = userEvent.setup()
    mock.handler = (p: string, m: string, u: URL, b: unknown) =>
      m === 'DELETE' ? problem(403, 'Solo el dueño puede editar o eliminar este elemento.', 'forbidden') : handler(p, m, u, b)
    renderAt()
    await user.click(await screen.findByRole('button', { name: 'Eliminar la vista Piezas por cliente' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Eliminar' }))
    expect(await within(screen.getByRole('dialog')).findByText('Solo el dueño puede editar o eliminar este elemento.')).toBeInTheDocument()
  })
})

describe('resultado de una vista', () => {
  it('título, fuente, resumen, filas rotuladas, totales, rango "Todo" y sin guardar el rango en la vista', async () => {
    renderAt('/analytics/reports?view=2')
    expect(await screen.findByRole('heading', { level: 1, name: 'Piezas por cliente' })).toBeInTheDocument()
    expect(screen.getByText('1 filtro(s) aplicado(s) · Agrupado por Cliente')).toBeInTheDocument()
    expect(await screen.findByText('Alfa')).toBeInTheDocument()
    // el agregado se rotula con el campo de la fuente
    expect(screen.getAllByText('Suma · Piezas').length).toBeGreaterThan(0)
    expect(screen.getByRole('group', { name: 'Totales' })).toHaveTextContent('15')
    const post = mock.calls.find((c) => c.path === '/api/v1/analytics/reports/2/run')
    expect(post?.body).toEqual({ dateRangeMode: 'ALL', take: 5000 })
    expect(screen.getByLabelText('Rango de fechas')).toHaveValue('ALL')
  })

  it('cambiar el rango vuelve a correr la vista; el personalizado espera las dos fechas', async () => {
    const user = userEvent.setup()
    renderAt('/analytics/reports?view=2')
    await screen.findByText('Alfa')
    await user.selectOptions(screen.getByLabelText('Rango de fechas'), 'LAST7')
    await waitFor(() => expect(mock.calls.some((c) => (c.body as { dateRangeMode?: string })?.dateRangeMode === 'LAST7')).toBe(true))
    const before = mock.calls.filter((c) => c.path.endsWith('/run')).length
    await user.selectOptions(screen.getByLabelText('Rango de fechas'), 'CUSTOM')
    expect(screen.getByText('Escoja las fechas Desde y Hasta para ver el resultado.')).toBeInTheDocument()
    expect(mock.calls.filter((c) => c.path.endsWith('/run')).length).toBe(before)
    await typeDate(user, screen.getByLabelText('Desde'), '2026-10-01')
    await typeDate(user, screen.getByLabelText('Hasta'), '2026-10-05')
    await waitFor(() => expect(mock.calls.some((c) => (c.body as { dateFrom?: string })?.dateFrom === '2026-10-01')).toBe(true))
  })

  it('una fuente sin campo de fecha no ofrece rango', async () => {
    renderAt('/analytics/reports?view=3')
    expect(await screen.findByRole('heading', { level: 1, name: 'De Beto' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Rango de fechas')).toBeNull()
    expect(mock.calls.find((c) => c.path.endsWith('/run'))?.body).toEqual({ dateRangeMode: 'ALL', take: 5000 })
  })

  it('buscar filtra lo cargado y avisa si nada coincide', async () => {
    const user = userEvent.setup()
    renderAt('/analytics/reports?view=2')
    await screen.findByText('Alfa')
    await user.type(screen.getByRole('searchbox', { name: 'Buscar en las filas cargadas' }), 'bravo')
    expect(screen.queryByText('Alfa')).toBeNull()
    expect(screen.getByText('Bravo')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox', { name: 'Buscar en las filas cargadas' }))
    await user.type(screen.getByRole('searchbox', { name: 'Buscar en las filas cargadas' }), 'zzz')
    expect(await screen.findByText('Ninguna fila coincide con la búsqueda.')).toBeInTheDocument()
  })

  it('avisa cuando el total es mayor que lo cargado', async () => {
    run = { ...RUN, total: 7000 }
    renderAt('/analytics/reports?view=2')
    expect(await screen.findByText(/Se muestran las primeras 2 de 7[.,]?000 filas/)).toBeInTheDocument()
  })

  it('el error del servidor al correr sale con su título', async () => {
    mock.handler = (p: string, m: string, u: URL, b: unknown) => (p.endsWith('/run') ? problem(400, 'Desde no puede ser mayor que Hasta.', 'validation') : handler(p, m, u, b))
    renderAt('/analytics/reports?view=2')
    expect(await screen.findByText('Desde no puede ser mayor que Hasta.')).toBeInTheDocument()
  })

  it('Editar y Eliminar solo si es editable y hay analytics.manage; «Volver a Vistas» siempre', async () => {
    const { unmount } = renderAt('/analytics/reports?view=1')
    expect(await screen.findByRole('heading', { level: 1, name: 'Clientes' })).toBeInTheDocument()
    expect(screen.getByText('Por defecto')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Eliminar' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Volver a Vistas' })).toBeInTheDocument()
    unmount()
    permissions = ['analytics.view']
    renderAt('/analytics/reports?view=2')
    await screen.findByRole('heading', { level: 1, name: 'Piezas por cliente' })
    expect(screen.queryByRole('button', { name: 'Editar' })).toBeNull()
  })

  it('una vista que no existe o no se puede ver', async () => {
    renderAt('/analytics/reports?view=77')
    expect(await screen.findByRole('heading', { level: 1, name: 'Vista no encontrada' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Volver a Vistas' }))
    expect(screen.getByTestId('where')).toHaveTextContent('/analytics/reports')
  })

  it('eliminar desde el resultado vuelve a la galería', async () => {
    const user = userEvent.setup()
    renderAt('/analytics/reports?view=2')
    await screen.findByText('Alfa')
    await user.click(screen.getByRole('button', { name: 'Eliminar' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent(/^\/analytics\/reports$/))
  })
})

describe('constructor', () => {
  const open = async (user: ReturnType<typeof userEvent.setup>, source = 'Clientes') => {
    renderAt()
    await user.click(await screen.findByRole('button', { name: `Nueva vista de ${source}` }))
    return screen.getByRole('dialog')
  }

  it('valida nombre y al menos una columna con los textos de la maqueta y no llama al API', async () => {
    const user = userEvent.setup()
    const dlg = await open(user)
    await user.click(within(dlg).getByRole('button', { name: 'Guardar vista' }))
    expect(within(dlg).getByText('Ponle un nombre a la vista')).toBeInTheDocument()
    expect(within(dlg).getByText('Escoge al menos una columna')).toBeInTheDocument()
    expect(writes()).toHaveLength(0)
    // se quitan solos al corregir
    await user.type(within(dlg).getByLabelText(/^Nombre de la vista/), 'Mi vista')
    await user.click(within(dlg).getByLabelText('Nombre'))
    expect(within(dlg).queryByText('Ponle un nombre a la vista')).toBeNull()
    expect(within(dlg).queryByText('Escoge al menos una columna')).toBeNull()
  })

  it('una fuente sin relaciones lo dice', async () => {
    const dlg = await open(userEvent.setup())
    expect(within(dlg).getByText('Esta fuente de datos no tiene otras fuentes asociadas para combinar.')).toBeInTheDocument()
  })

  it('crea la vista con columnas, fuente combinada, campo personalizado, filtro, orden y la vista previa con los datos reales', async () => {
    const user = userEvent.setup()
    const dlg = await open(user, 'Órdenes')
    await user.type(within(dlg).getByLabelText(/^Nombre de la vista/), '  Órdenes con cliente ')
    await user.click(within(dlg).getAllByLabelText('Cliente').find((e) => e.closest('.r2')) as HTMLElement)
    await user.click(within(within(dlg).getByRole('group', { name: 'Órdenes' })).getByLabelText('Número de orden'))
    await user.click(within(within(dlg).getByRole('group', { name: 'Campos personalizados' })).getByLabelText('Referencia'))
    await user.click(within(within(dlg).getByRole('group', { name: 'Cliente' })).getByLabelText('Nombre'))
    await user.click(within(dlg).getByRole('button', { name: 'Agregar filtro' }))
    await user.selectOptions(within(dlg).getByLabelText('Campo', { exact: true }), 'ClientName')
    await user.selectOptions(within(dlg).getByLabelText('Operador'), 'contains')
    await user.type(within(dlg).getByLabelText('Valor'), 'FF2')
    await user.click(within(dlg).getByRole('button', { name: 'Agregar orden' }))
    await user.selectOptions(within(dlg).getByLabelText('Ordenar por 1'), 'cf.ref')
    await user.selectOptions(within(dlg).getByLabelText('Dirección del orden 1'), 'desc')
    expect(await within(dlg).findByText('ORD-1', undefined, { timeout: 3000 })).toBeInTheDocument()
    await user.click(within(dlg).getByRole('button', { name: 'Guardar vista' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    const w = writes()[0]
    expect(w).toMatchObject({ method: 'POST', path: '/api/v1/analytics/reports/TRANSPORT_ORDER' })
    expect(w.body).toMatchObject({
      name: 'Órdenes con cliente',
      secondary: ['Client'],
      columns: ['OrderNumber', 'cf.ref', 'Client.Name'],
      sortJson: '[{"field":"cf.ref","dir":"desc"}]',
      groupJson: null,
      visibility: 'PRIVATE',
      shares: [],
    })
    expect(JSON.parse((w.body as { filterJson: string }).filterJson)).toEqual({ and: [{ field: 'ClientName', op: 'contains', value: 'FF2' }] })
    expect(await screen.findByText('Vista guardada — disponible para uso futuro: Órdenes con cliente')).toBeInTheDocument()
    // la vista previa se pidió con la misma configuración y con pocas filas
    const prev = mock.calls.filter((c) => c.path.endsWith('/preview')).at(-1)
    expect(prev?.search).toContain('take=10')
    expect(prev?.search).toContain('dateRangeMode=ALL')
  })

  it('agrupar: con agregados, totales y orden por agregado (sin columnas) arma groupJson', async () => {
    const user = userEvent.setup()
    const dlg = await open(user, 'Órdenes')
    await user.type(within(dlg).getByLabelText(/^Nombre de la vista/), 'Piezas')
    await user.click(within(dlg).getByRole('button', { name: 'Agregar campo de agrupación' }))
    await user.selectOptions(within(dlg).getByLabelText('Campo de agrupación 1', { exact: true }), 'ClientName')
    await user.click(within(dlg).getByRole('button', { name: 'Agregar agregado' }))
    await user.click(within(dlg).getByRole('button', { name: 'Agregar agregado' }))
    await user.selectOptions(within(dlg).getByLabelText('Función del agregado 2'), 'SUM')
    expect(within(dlg).getByLabelText('Campo del agregado 2')).toHaveValue('TotalPieces')
    await user.click(within(dlg).getByLabelText('Mostrar fila de totales'))
    await user.click(within(dlg).getByRole('button', { name: 'Agregar orden' }))
    await user.selectOptions(within(dlg).getByLabelText('Ordenar por 1'), 'sum_TotalPieces')
    await user.click(within(dlg).getByRole('button', { name: 'Guardar vista' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].body).toMatchObject({
      columns: [],
      groupJson: '{"by":["ClientName"],"aggregates":[{"fn":"COUNT"},{"fn":"SUM","field":"TotalPieces"}],"totals":true}',
      sortJson: '[{"field":"sum_TotalPieces","dir":"asc"}]',
    })
  })

  it('el 409 del servidor sale bajo el nombre y en el aviso, con el mensaje exacto, y el constructor sigue abierto', async () => {
    const user = userEvent.setup()
    mock.handler = (p: string, m: string, u: URL, b: unknown) => (m === 'POST' && !p.endsWith('/preview') ? problem(409, "Ya existe una vista 'Mi vista' para CLIENT.", 'conflict') : handler(p, m, u, b))
    const dlg = await open(user)
    await user.type(within(dlg).getByLabelText(/^Nombre de la vista/), 'Mi vista')
    await user.click(within(dlg).getByLabelText('Nombre'))
    await user.click(within(dlg).getByRole('button', { name: 'Guardar vista' }))
    expect(await within(dlg).findByText("Ya existe una vista 'Mi vista' para CLIENT.")).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('un 400 con errores por campo (fuente que no se combina) sale bajo su sección', async () => {
    const user = userEvent.setup()
    mock.handler = (p: string, m: string, u: URL, b: unknown) =>
      m === 'POST' && !p.endsWith('/preview') ? problem(400, 'Datos inválidos.', 'validation', { secondary: ["La fuente TRANSPORT_ORDER no se combina con 'Trip'."] }) : handler(p, m, u, b)
    const dlg = await open(user, 'Órdenes')
    await user.type(within(dlg).getByLabelText(/^Nombre de la vista/), 'X')
    await user.click(within(within(dlg).getByRole('group', { name: 'Órdenes' })).getByLabelText('Número de orden'))
    await user.click(within(dlg).getByRole('button', { name: 'Guardar vista' }))
    expect(await within(dlg).findByText("La fuente TRANSPORT_ORDER no se combina con 'Trip'.")).toBeInTheDocument()
  })

  it('compartir: con permiso de usuarios muestra personas y roles y arma los shares; con «pueden editar»', async () => {
    const user = userEvent.setup()
    const dlg = await open(user)
    await user.click(within(dlg).getByLabelText('Nombre'))
    await user.type(within(dlg).getByLabelText(/^Nombre de la vista/), 'Compartida')
    await user.selectOptions(within(dlg).getByLabelText('Quién puede verlo'), 'SHARED')
    await user.click(within(dlg).getByRole('button', { name: /Compartir con \(usuarios\)/ }))
    await user.click(await screen.findByText(/Beto Bodega/))
    await user.click(within(dlg).getByText('Visibilidad'))
    await user.click(within(dlg).getByLabelText('Las personas elegidas también pueden editar la vista'))
    await user.click(within(dlg).getByRole('button', { name: 'Guardar vista' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].body).toMatchObject({ visibility: 'SHARED', shares: [{ userId: 7, roleId: null, canEdit: true }] })
  })

  it('compartir sin admin.users explica qué permiso falta', async () => {
    permissions = ['analytics.view', 'analytics.manage']
    const user = userEvent.setup()
    const dlg = await open(user)
    await user.selectOptions(within(dlg).getByLabelText('Quién puede verlo'), 'SHARED')
    expect(within(dlg).getByText('Para compartir con personas específicas se necesita el permiso de administrar usuarios.')).toBeInTheDocument()
  })

  it('editar: trae lo guardado, conserva lo que la pantalla no edita y envía PUT; nombre repetido se avisa antes de enviar', async () => {
    const user = userEvent.setup()
    reports = [SYSTEM, { ...MINE, scheduleCron: '0 7 * * *', deliveryEmails: 'a@b.c' }, { ...MINE, id: 5, name: 'Otra de órdenes', groupJson: null, columns: ['OrderNumber'], sortJson: null }]
    renderAt('/analytics/reports?view=2')
    await user.click(await screen.findByRole('button', { name: 'Editar' }))
    const dlg = screen.getByRole('dialog')
    expect(within(dlg).getByRole('heading', { name: 'Editar vista' })).toBeInTheDocument()
    expect(within(dlg).getByLabelText(/^Nombre de la vista/)).toHaveValue('Piezas por cliente')
    expect(within(dlg).getByLabelText('Campo de agrupación 1', { exact: true })).toHaveValue('ClientName')
    expect(within(dlg).getByLabelText('Valor')).toHaveValue('FF2')
    expect(within(dlg).getByLabelText('Mostrar fila de totales')).toBeChecked()
    // nombre ya usado por otra vista de la fuente: lo dice el cliente con el mismo texto del servidor
    await user.clear(within(dlg).getByLabelText(/^Nombre de la vista/))
    await user.type(within(dlg).getByLabelText(/^Nombre de la vista/), 'otra de órdenes')
    await user.click(within(dlg).getByRole('button', { name: 'Guardar vista' }))
    expect(await within(dlg).findByText("Ya existe una vista 'otra de órdenes' para TRANSPORT_ORDER.")).toBeInTheDocument()
    expect(writes()).toHaveLength(0)
    await user.clear(within(dlg).getByLabelText(/^Nombre de la vista/))
    await user.type(within(dlg).getByLabelText(/^Nombre de la vista/), 'Piezas nuevas')
    await user.click(within(dlg).getByRole('button', { name: 'Guardar vista' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0]).toMatchObject({ method: 'PUT', path: '/api/v1/analytics/reports/2' })
    expect(writes()[0].body).toMatchObject({ name: 'Piezas nuevas', scheduleCron: '0 7 * * *', deliveryEmails: 'a@b.c', descriptions: null, groupJson: MINE.groupJson, sortJson: MINE.sortJson })
    expect(await screen.findByText('Vista actualizada: Piezas nuevas')).toBeInTheDocument()
  })

  it('el 403 al editar una vista ajena sale con el mensaje exacto', async () => {
    const user = userEvent.setup()
    mock.handler = (p: string, m: string, u: URL, b: unknown) => (m === 'PUT' ? problem(403, 'Los elementos por default de la plataforma no se editan ni se eliminan.', 'forbidden') : handler(p, m, u, b))
    renderAt('/analytics/reports?view=2')
    await user.click(await screen.findByRole('button', { name: 'Editar' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Guardar vista' }))
    expect(await within(screen.getByRole('dialog')).findByText('Los elementos por default de la plataforma no se editan ni se eliminan.')).toBeInTheDocument()
  })
})
