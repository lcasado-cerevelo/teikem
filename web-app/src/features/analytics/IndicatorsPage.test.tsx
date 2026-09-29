// Lote F8a (P3) — agrupación por módulo, permisos de "Nuevo"/editar y el switch mío llamando a `my-pulse` e
// invalidando el Pulso (misma clave que usa `usePulse`, `PULSE_QUERY_KEY`). Mismo patrón de cliente simulado que
// `Pulse.test.tsx`.
import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../app/queryClient'
import { SessionContext, type MeDto, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import IndicatorsPage from './IndicatorsPage'
import type { AnalyticsDefinition } from './api'

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
            <IndicatorsPage />
          </AccessProvider>
        </SessionContext.Provider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const FULL = { permissions: ['analytics.view', 'analytics.manage'], modules: ['ANALYTICS'] }
const VIEW_ONLY = { permissions: ['analytics.view'], modules: ['ANALYTICS'] }

function ind(over: Partial<AnalyticsDefinition> = {}): AnalyticsDefinition {
  return {
    id: 1,
    name: 'Órdenes entregadas hoy',
    businessModule: 'OPERATIONS',
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
    { code: 'ACCOUNTING', label: 'Contabilidad', sortOrder: 3, isEnabled: true },
  ],
  DateRangeMode: [{ code: 'LAST7', label: 'Últimos 7 días', sortOrder: 1, isEnabled: true }],
}

beforeEach(() => {
  mock.writes.length = 0
})

function baseHandler(indicators: AnalyticsDefinition[]): Handler {
  return (path, method) => {
    if (path === '/api/v1/analytics/indicators') return indicators
    if (path.startsWith('/api/v1/catalogs/')) {
      const entity = path.split('/').pop() ?? ''
      return LOOKUPS[entity] ?? []
    }
    const valueMatch = /^\/api\/v1\/analytics\/indicators\/(\d+)\/value$/.exec(path)
    if (valueMatch) return { id: Number(valueMatch[1]), name: 'x', value: 42, isMoney: false }
    const pulseMatch = /^\/api\/v1\/analytics\/indicators\/(\d+)\/my-pulse$/.exec(path)
    if (pulseMatch && method === 'PUT') return indicators.find((i) => i.id === Number(pulseMatch[1])) ?? {}
    return []
  }
}

describe('IndicatorsPage', () => {
  it('agrupa las tarjetas por módulo de negocio, con su contador', async () => {
    mock.handler = baseHandler([
      ind({ id: 1, name: 'Órdenes entregadas hoy', businessModule: 'OPERATIONS' }),
      ind({ id: 2, name: 'Productos bajo mínimo', businessModule: 'WAREHOUSE' }),
      ind({ id: 3, name: 'Facturación del mes', businessModule: 'ACCOUNTING' }),
    ])
    renderPage()
    expect(await screen.findByText('Órdenes entregadas hoy')).toBeInTheDocument()
    expect(screen.getByText('Operación')).toBeInTheDocument()
    expect(screen.getByText('Almacén')).toBeInTheDocument()
    expect(screen.getByText('Contabilidad')).toBeInTheDocument()
    expect(screen.getByText('Productos bajo mínimo')).toBeInTheDocument()
    expect(screen.getByText('Facturación del mes')).toBeInTheDocument()
  })

  it('sin analytics.manage no aparece "Nuevo indicador" ni editar/eliminar', async () => {
    mock.handler = baseHandler([ind({ id: 1, canEdit: false })])
    renderPage(VIEW_ONLY)
    await screen.findByText('Órdenes entregadas hoy')
    expect(screen.queryByText('Nuevo indicador')).not.toBeInTheDocument()
    expect(screen.queryByText('Editar')).not.toBeInTheDocument()
    expect(screen.queryByText('Eliminar')).not.toBeInTheDocument()
  })

  it('con analytics.manage aparece "Nuevo indicador"', async () => {
    mock.handler = baseHandler([ind({ id: 1 })])
    renderPage(FULL)
    await screen.findByText('Órdenes entregadas hoy')
    expect(screen.getByText('Nuevo indicador')).toBeInTheDocument()
  })

  it('el switch "Mostrar en Pulso del día" llama a my-pulse y limpia la caché del Pulso', async () => {
    mock.handler = baseHandler([ind({ id: 1, effectiveShowInPulse: false })])
    const client = createQueryClient()
    client.setDefaultOptions({ queries: { retry: false } })
    client.setQueryData(['/api/v1/analytics/pulse'], { panels: [], indicators: [], charts: [], hasPersonalLayout: false, canOrganizeCompany: false })
    const invalidateSpy = vi.spyOn(client, 'invalidateQueries')
    render(
      <MemoryRouter>
        <QueryClientProvider client={client}>
          <SessionContext.Provider value={session()}>
            <AccessProvider permissions={FULL.permissions} modules={FULL.modules}>
              <IndicatorsPage />
            </AccessProvider>
          </SessionContext.Provider>
        </QueryClientProvider>
      </MemoryRouter>,
    )
    await screen.findByText('Órdenes entregadas hoy')
    const user = userEvent.setup()
    const mineSwitch = screen.getByText('Mostrar en Pulso del día').closest('label')?.querySelector('input')
    expect(mineSwitch).toBeTruthy()
    await user.click(mineSwitch as HTMLInputElement)

    await waitFor(() => expect(mock.writes.some((w) => w.path === '/api/v1/analytics/indicators/1/my-pulse')).toBe(true))
    const write = mock.writes.find((w) => w.path === '/api/v1/analytics/indicators/1/my-pulse')
    expect(write?.body).toEqual({ showInPulse: true })
    await waitFor(() =>
      expect(invalidateSpy.mock.calls.some((c) => JSON.stringify(c[0]?.queryKey) === JSON.stringify(['/api/v1/analytics/pulse']))).toBe(true),
    )
  })

  it('vacío: "Todavía no hay indicadores."', async () => {
    mock.handler = baseHandler([])
    renderPage()
    expect(await screen.findByText('Todavía no hay indicadores.')).toBeInTheDocument()
  })
})
