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

function baseHandler(): Handler {
  return (path) => {
    if (path === '/api/v1/analytics/data-sources') return DATA_SOURCES
    if (path.startsWith('/api/v1/catalogs/')) {
      const entity = path.split('/').pop() ?? ''
      return LOOKUPS[entity] ?? []
    }
    if (path === '/api/v1/analytics/indicators') return { id: 99 }
    return []
  }
}

function renderEditor(kind: 'indicator' | 'chart', onClose = vi.fn()) {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={['analytics.manage']} modules={['ANALYTICS']}>
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
})
