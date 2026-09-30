// Lote F8a (P3) — el editor serializa el filtro y envía el cuerpo `IndicatorUpsertRequest`/`ChartUpsertRequest` correcto;
// al cambiar de fuente se limpian campo y filtro.
import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../app/queryClient'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { DefinitionEditor } from './DefinitionEditor'

beforeAll(() => {
  setLang('es')
})

type Handler = (path: string, method: string, url: URL, body: unknown) => unknown
const mock = vi.hoisted(() => ({ writes: [] as { method: string; path: string; body: unknown }[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const path = url.pathname
    const body = req.method !== 'GET' ? await req.clone().json().catch(() => null) : null
    if (req.method !== 'GET') mock.writes.push({ method: req.method, path, body })
    const result = (mock.handler as Handler)(path, req.method, url, body)
    if (result instanceof Response) return result
    return new Response(JSON.stringify(result ?? null), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const DATA_SOURCES = [
  {
    key: 'ORDERS',
    label: 'Órdenes',
    entityType: 'TRANSPORT_ORDER',
    dateField: 'createdAt',
    defaultBusinessModule: 'OPERATIONS',
    fields: [
      { key: 'status', label: 'Estado', type: 'Text', isMoney: false },
      { key: 'cod', label: 'COD', type: 'Number', isMoney: true },
    ],
    relations: [],
    customFields: [],
  },
]

const LOOKUPS: Record<string, unknown> = {
  AggregateFn: [
    { code: 'COUNT', label: 'Cantidad', sortOrder: 1, isEnabled: true },
    { code: 'SUM', label: 'Suma', sortOrder: 2, isEnabled: true },
  ],
  BusinessModule: [{ code: 'OPERATIONS', label: 'Operación', sortOrder: 1, isEnabled: true }],
  ReportVisibility: [
    { code: 'PRIVATE', label: 'Solo yo', sortOrder: 1, isEnabled: true },
    { code: 'TENANT', label: 'Toda la organización', sortOrder: 2, isEnabled: true },
    { code: 'SHARED', label: 'Personas específicas', sortOrder: 3, isEnabled: true },
  ],
  DateRangeMode: [
    { code: 'LAST7', label: 'Últimos 7 días', sortOrder: 1, isEnabled: true },
    { code: 'ALL', label: 'Todo el tiempo', sortOrder: 2, isEnabled: true },
  ],
  ReportChartType: [
    { code: 'BAR', label: 'Barras', sortOrder: 1, isEnabled: true },
    { code: 'DONUT', label: 'Dona', sortOrder: 2, isEnabled: true },
    { code: 'LINE', label: 'Línea', sortOrder: 3, isEnabled: true },
  ],
}

const ROLES = [
  { id: 3, name: 'Supervisores', isActive: true },
  { id: 4, name: 'Rol viejo', isActive: false },
]
const USERS = [{ id: 7, fullName: 'Beto Bodega', email: 'beto@teikem.local' }]

function baseHandler(): Handler {
  return (path) => {
    if (path === '/api/v1/analytics/data-sources') return DATA_SOURCES
    if (path.startsWith('/api/v1/catalogs/')) {
      const entity = path.split('/').pop() ?? ''
      return LOOKUPS[entity] ?? []
    }
    if (path === '/api/v1/analytics/indicators') return { id: 99 }
    if (path === '/api/v1/analytics/charts') return { id: 98 }
    if (path === '/api/v1/roles') return ROLES
    if (path === '/api/v1/users') return USERS
    if (path === '/api/v1/analytics/reports/ORDERS/preview') return { rows: [{ status: 'OPEN', count_rows: 4 }], total: 1 }
    return []
  }
}

function renderEditor(kind: 'indicator' | 'chart', onClose = vi.fn(), permissions = ['analytics.manage']) {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={['ANALYTICS']}>
        <DefinitionEditor kind={kind} definition={null} onClose={onClose} />
      </AccessProvider>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  mock.writes.length = 0
  mock.handler = baseHandler()
})

describe('DefinitionEditor', () => {
  it('arma y envía el POST del indicador con el filtro serializado', async () => {
    const user = userEvent.setup()
    renderEditor('indicator')

    await user.type(screen.getByLabelText(/Nombre del indicador/), 'Órdenes entregadas hoy')
    await user.selectOptions(screen.getByLabelText(/Fuente de datos/), 'ORDERS')
    // El módulo de negocio se autocompleta con el de la fuente (OPERATIONS); cálculo por defecto COUNT.
    await user.selectOptions(screen.getByLabelText(/Cálculo/), 'SUM')
    await waitFor(() => expect(screen.getByLabelText(/^Campo/)).toBeInTheDocument())
    await user.selectOptions(screen.getByLabelText(/^Campo/), 'cod')

    await user.click(screen.getByText('Agregar filtro'))

    const submit = screen.getByRole('button', { name: 'Guardar indicador' })
    await user.click(submit)

    await waitFor(() => expect(mock.writes.some((w) => w.path === '/api/v1/analytics/indicators' && w.method === 'POST')).toBe(true))
    const write = mock.writes.find((w) => w.path === '/api/v1/analytics/indicators')
    const body = write?.body as {
      name: string
      dataSource: string
      aggregateFn: string
      field: string
      businessModule: string
      filterJson: string | null
    }
    expect(body.name).toBe('Órdenes entregadas hoy')
    expect(body.dataSource).toBe('ORDERS')
    expect(body.aggregateFn).toBe('SUM')
    expect(body.field).toBe('cod')
    expect(body.businessModule).toBe('OPERATIONS')
    expect(JSON.parse(body.filterJson ?? '""')).toEqual({ and: [{ field: 'status', op: 'eq', value: '' }] })
  })

  it('al cambiar de fuente se limpian el campo y el filtro', async () => {
    const user = userEvent.setup()
    renderEditor('indicator')
    await waitFor(() => expect(screen.getByRole('option', { name: 'Órdenes' })).toBeInTheDocument())
    await user.selectOptions(screen.getByLabelText(/Fuente de datos/), 'ORDERS')
    await user.selectOptions(screen.getByLabelText(/Cálculo/), 'SUM')
    await waitFor(() => expect(screen.getByLabelText(/^Campo/)).toBeInTheDocument())
    await user.selectOptions(screen.getByLabelText(/^Campo/), 'cod')
    await user.click(screen.getByText('Agregar filtro'))
    expect(screen.getAllByText('es igual a').length).toBeGreaterThan(0)

    // Vuelve a "seleccione" (limpia dataSource): la fila de filtro agregada se descarta y "Campo" se queda sin
    // opciones (no hay fuente elegida de la que sacarlas).
    await user.selectOptions(screen.getByLabelText(/Fuente de datos/), '')
    await waitFor(() => expect(screen.queryByText('es igual a')).not.toBeInTheDocument())
    expect(screen.getByLabelText(/^Campo/)).toHaveDisplayValue('Seleccione…')
  })

  it('Fase 10a: "Compartido" comparte con roles aunque no pueda listar usuarios (indicador)', async () => {
    const user = userEvent.setup()
    renderEditor('indicator')
    await user.type(screen.getByLabelText(/Nombre del indicador/), 'Por rol')
    await waitFor(() => expect(screen.getByRole('option', { name: 'Órdenes' })).toBeInTheDocument())
    await user.selectOptions(screen.getByLabelText(/Fuente de datos/), 'ORDERS')
    await user.selectOptions(screen.getByLabelText(/Quién puede verlo/), 'SHARED')

    // Sin admin.users: no hay selector de usuarios (aviso), pero sí el de roles.
    expect(screen.getByText('Para compartir con personas específicas se necesita el permiso de administrar usuarios.')).toBeInTheDocument()
    await user.click(screen.getByLabelText('Compartir con (roles)'))
    // Solo roles activos.
    expect(screen.queryByRole('option', { name: 'Rol viejo' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('option', { name: 'Supervisores' }))

    await user.click(screen.getByRole('button', { name: 'Guardar indicador' }))
    await waitFor(() => expect(mock.writes.some((w) => w.path === '/api/v1/analytics/indicators')).toBe(true))
    const body = mock.writes.find((w) => w.path === '/api/v1/analytics/indicators')?.body as { visibility: string; shares: unknown[] }
    expect(body.visibility).toBe('SHARED')
    expect(body.shares).toEqual([{ userId: null, roleId: 3, canEdit: false }])
  })

  it('Fase 10a: con admin.users combina usuarios y roles en `shares` (gráfico)', async () => {
    const user = userEvent.setup()
    renderEditor('chart', vi.fn(), ['analytics.manage', 'admin.users'])
    await user.type(screen.getByLabelText(/Nombre del gráfico/), 'Compartido')
    await waitFor(() => expect(screen.getByRole('option', { name: 'Órdenes' })).toBeInTheDocument())
    await user.selectOptions(screen.getByLabelText(/Fuente de datos/), 'ORDERS')
    await user.selectOptions(screen.getByLabelText(/Agrupar por/), 'status')
    await user.selectOptions(screen.getByLabelText(/Quién puede verlo/), 'SHARED')

    await user.click(screen.getByLabelText('Compartir con (usuarios)'))
    await user.click(await screen.findByRole('option', { name: /Beto Bodega/ }))
    await user.click(screen.getByLabelText('Compartir con (roles)'))
    await user.click(screen.getByRole('option', { name: 'Supervisores' }))

    await user.click(screen.getByRole('button', { name: 'Guardar gráfico' }))
    await waitFor(() => expect(mock.writes.some((w) => w.path === '/api/v1/analytics/charts')).toBe(true))
    const body = mock.writes.find((w) => w.path === '/api/v1/analytics/charts')?.body as { shares: unknown[] }
    expect(body.shares).toEqual([
      { userId: 7, roleId: null, canEdit: false },
      { userId: null, roleId: 3, canEdit: false },
    ])
  })

  it('Fase 10b: el gráfico no tiene selector de módulo (lo pone la fuente) y muestra la vista previa en vivo', async () => {
    const user = userEvent.setup()
    renderEditor('chart')
    expect(screen.queryByLabelText(/Módulo de negocio/)).not.toBeInTheDocument()
    expect(screen.getByText('Elija la fuente de datos, el campo para agrupar y el cálculo para ver el gráfico.')).toBeInTheDocument()

    await user.type(screen.getByLabelText(/Nombre del gráfico/), 'Órdenes por estado')
    await waitFor(() => expect(screen.getByRole('option', { name: 'Órdenes' })).toBeInTheDocument())
    await user.selectOptions(screen.getByLabelText(/Fuente de datos/), 'ORDERS')
    await user.selectOptions(screen.getByLabelText(/Agrupar por/), 'status')

    // La vista previa consulta el endpoint de vista previa con la agrupación y el cálculo del formulario.
    // Lote 15: siempre el gráfico (también con pocos puntos); sus valores van en la etiqueta accesible.
    expect(await screen.findByRole('img', { name: /OPEN: / })).toBeInTheDocument()
    const preview = mock.writes.find((w) => w.path === '/api/v1/analytics/reports/ORDERS/preview')
    const previewBody = preview?.body as { groupJson?: string } | undefined
    expect(JSON.parse(previewBody?.groupJson ?? '""')).toEqual({ by: ['status'], aggregates: [{ fn: 'COUNT', field: null }] })

    await user.click(screen.getByRole('button', { name: 'Guardar gráfico' }))
    await waitFor(() => expect(mock.writes.some((w) => w.path === '/api/v1/analytics/charts')).toBe(true))
    const body = mock.writes.find((w) => w.path === '/api/v1/analytics/charts')?.body as { businessModule: string; groupByField: string }
    expect(body.businessModule).toBe('OPERATIONS')
    expect(body.groupByField).toBe('status')
  })

  it('Fase 10b: el indicador conserva el selector de módulo y no tiene vista previa', async () => {
    renderEditor('indicator')
    expect(screen.getByLabelText(/Módulo de negocio/)).toBeInTheDocument()
    expect(screen.queryByText('Vista previa')).not.toBeInTheDocument()
  })
})
