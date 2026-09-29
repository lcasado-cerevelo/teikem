// Lote F8a (P3) — agrupación por módulo y permisos de "Nuevo gráfico" en la pantalla de Gráficos (mismo patrón que
// `IndicatorsPage.test.tsx`).
import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../app/queryClient'
import { SessionContext, type MeDto, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import type { AnalyticsDefinition } from './api'
import ChartsPage from './ChartsPage'

beforeAll(() => {
  setLang('es')
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
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

const ME: MeDto = { userId: 1, fullName: 'Ana Admin', email: 'admin@teikem.local', tenantId: 1, tenantName: 'Demo', lang: 'es' }

function session(): Session {
  return {
    me: ME,
    isAuthenticated: true,
    isLoading: false,
    error: null,
    tenantId: 1,
    lang: 'es',
    setLang: vi.fn(),
    logout: vi.fn(async () => {}),
    switchTenant: vi.fn(async () => {}),
    permissions: new Set(),
    modules: new Set(),
    reloadMe: vi.fn(async () => {}),
  }
}

function renderPage(access: { permissions: string[]; modules: string[] } = FULL) {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <SessionContext.Provider value={session()}>
          <AccessProvider permissions={access.permissions} modules={access.modules}>
            <ChartsPage />
          </AccessProvider>
        </SessionContext.Provider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const FULL = { permissions: ['analytics.view', 'analytics.manage'], modules: ['ANALYTICS'] }
const VIEW_ONLY = { permissions: ['analytics.view'], modules: ['ANALYTICS'] }

function chart(over: Partial<AnalyticsDefinition> = {}): AnalyticsDefinition {
  return {
    id: 1,
    name: 'Órdenes por estado',
    businessModule: 'OPERATIONS',
    chartType: 'BAR',
    isMoney: false,
    isSystem: true,
    visibility: 'TENANT',
    canEdit: false,
    canChangeDate: false,
    dateRangeApplies: false,
    effectiveShowInPulse: false,
    showInPulse: false,
    sortOrder: 10,
    shares: [],
    ...over,
  }
}

const LOOKUPS: Record<string, unknown> = {
  BusinessModule: [
    { code: 'OPERATIONS', label: 'Operación', sortOrder: 1, isEnabled: true },
    { code: 'WAREHOUSE', label: 'Almacén', sortOrder: 2, isEnabled: true },
  ],
  DateRangeMode: [{ code: 'LAST7', label: 'Últimos 7 días', sortOrder: 1, isEnabled: true }],
}

beforeEach(() => {
  mock.writes.length = 0
})

function baseHandler(charts: AnalyticsDefinition[]): Handler {
  return (path, method) => {
    if (path === '/api/v1/analytics/charts') return charts
    if (path.startsWith('/api/v1/catalogs/')) {
      const entity = path.split('/').pop() ?? ''
      return LOOKUPS[entity] ?? []
    }
    const dataMatch = /^\/api\/v1\/analytics\/charts\/(\d+)\/data$/.exec(path)
    if (dataMatch) return { id: Number(dataMatch[1]), name: 'x', chartType: 'BAR', isMoney: false, points: [] }
    const pulseMatch = /^\/api\/v1\/analytics\/charts\/(\d+)\/my-pulse$/.exec(path)
    if (pulseMatch && method === 'PUT') return charts.find((c) => c.id === Number(pulseMatch[1])) ?? {}
    return []
  }
}

describe('ChartsPage', () => {
  it('agrupa las tarjetas por módulo de negocio', async () => {
    mock.handler = baseHandler([
      chart({ id: 1, name: 'Órdenes por estado', businessModule: 'OPERATIONS' }),
      chart({ id: 2, name: 'Disponible por categoría', businessModule: 'WAREHOUSE' }),
    ])
    renderPage()
    expect(await screen.findByText('Órdenes por estado')).toBeInTheDocument()
    expect(screen.getByText('Operación')).toBeInTheDocument()
    expect(screen.getByText('Almacén')).toBeInTheDocument()
    expect(screen.getByText('Disponible por categoría')).toBeInTheDocument()
  })

  it('sin analytics.manage no aparece "Nuevo gráfico"', async () => {
    mock.handler = baseHandler([chart({ id: 1 })])
    renderPage(VIEW_ONLY)
    await screen.findByText('Órdenes por estado')
    expect(screen.queryByText('Nuevo gráfico')).not.toBeInTheDocument()
  })

  it('con analytics.manage aparece "Nuevo gráfico"', async () => {
    mock.handler = baseHandler([chart({ id: 1 })])
    renderPage(FULL)
    await screen.findByText('Órdenes por estado')
    expect(screen.getByText('Nuevo gráfico')).toBeInTheDocument()
  })

  it('vacío: "Todavía no hay gráficos."', async () => {
    mock.handler = baseHandler([])
    renderPage()
    expect(await screen.findByText('Todavía no hay gráficos.')).toBeInTheDocument()
  })

  it('el switch "Mostrar en Pulso del día" llama a my-pulse y limpia la caché del Pulso', async () => {
    mock.handler = baseHandler([chart({ id: 1, effectiveShowInPulse: false })])
    const client = createQueryClient()
    client.setDefaultOptions({ queries: { retry: false } })
    client.setQueryData(['/api/v1/analytics/pulse'], { panels: [], indicators: [], charts: [], hasPersonalLayout: false, canOrganizeCompany: false })
    const invalidateSpy = vi.spyOn(client, 'invalidateQueries')
    render(
      <MemoryRouter>
        <QueryClientProvider client={client}>
          <SessionContext.Provider value={session()}>
            <AccessProvider permissions={FULL.permissions} modules={FULL.modules}>
              <ChartsPage />
            </AccessProvider>
          </SessionContext.Provider>
        </QueryClientProvider>
      </MemoryRouter>,
    )
    await screen.findByText('Órdenes por estado')
    const user = userEvent.setup()
    const mineSwitch = screen.getByText('Mostrar en Pulso del día').closest('label')?.querySelector('input')
    expect(mineSwitch).toBeTruthy()
    await user.click(mineSwitch as HTMLInputElement)

    await waitFor(() => expect(mock.writes.some((w) => w.path === '/api/v1/analytics/charts/1/my-pulse')).toBe(true))
    const write = mock.writes.find((w) => w.path === '/api/v1/analytics/charts/1/my-pulse')
    expect(write?.body).toEqual({ showInPulse: true })
    await waitFor(() =>
      expect(invalidateSpy.mock.calls.some((c) => JSON.stringify(c[0]?.queryKey) === JSON.stringify(['/api/v1/analytics/pulse']))).toBe(true),
    )
  })
})
