import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient, setAccessDeniedHandler } from '../../app/queryClient'
import { SessionContext, type MeDto, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { apiDateToYmd, chartKind, customRangeDays, formatValue, formatYmd } from './format'
import Pulse from './Pulse'

// Cliente de la app sobre un fetch simulado (misma política que el real).
type Handler = (path: string, method: string) => Response | unknown
const mock = vi.hoisted(() => ({ calls: [] as string[], writes: [] as { method: string; path: string; body: unknown }[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const path = new URL(req.url).pathname
    mock.calls.push(path)
    if (req.method !== 'GET') mock.writes.push({ method: req.method, path, body: await req.clone().json().catch(() => null) })
    const result = (mock.handler as Handler)(path, req.method)
    if (result instanceof Response) return result
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } })
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

function renderPulse(access: { permissions: string[]; modules: string[] }) {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <SessionContext.Provider value={session()}>
          <AccessProvider permissions={access.permissions} modules={access.modules}>
            <Pulse />
          </AccessProvider>
        </SessionContext.Provider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const FULL = { permissions: ['analytics.view'], modules: ['ANALYTICS'] }
const onAccessDenied = vi.fn()

beforeAll(() => {
  setLang('es')
  setAccessDeniedHandler(onAccessDenied)
  // Recharts (ResponsiveContainer) necesita ResizeObserver, que jsdom no trae.
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
})
afterAll(() => {
  setAccessDeniedHandler(null)
  vi.unstubAllGlobals()
})
beforeEach(() => {
  mock.calls = []
  mock.writes = []
  onAccessDenied.mockReset()
})

describe('formatValue / chartKind', () => {
  it('miles con coma; dos decimales y $ si es dinero; decimales si no es entero; — si es nulo', () => {
    expect(formatValue(1234567, false)).toBe('1,234,567')
    expect(formatValue(1234.5, true)).toBe('$1,234.50')
    expect(formatValue(0, true)).toBe('$0.00')
    expect(formatValue(2.345, false)).toBe('2.35')
    expect(formatValue(null, false)).toBe('—')
    expect(formatValue(undefined, true)).toBe('—')
  })

  it('rango CUSTOM: toUtc es exclusivo (se resta un día) y las fechas se leen como UTC', () => {
    expect(customRangeDays('2026-09-01T00:00:00', '2026-09-16T00:00:00')).toEqual({ from: '2026-09-01', to: '2026-09-15' })
    expect(customRangeDays('2026-09-01T00:00:00Z', '2026-10-01T00:00:00Z')).toEqual({ from: '2026-09-01', to: '2026-09-30' })
    expect(apiDateToYmd(null)).toBe('')
    expect(apiDateToYmd('no-es-fecha')).toBe('')
    expect(formatYmd('', 'es')).toBe('…')
    expect(formatYmd('2026-09-15', 'en')).toBe('9/15/2026')
  })

  it('elige el gráfico por chartType: LINE, DONUT, PIE; el resto barras', () => {
    expect(chartKind('LINE')).toBe('line')
    expect(chartKind('donut')).toBe('donut')
    expect(chartKind('PIE')).toBe('pie')
    expect(chartKind('BAR')).toBe('bar')
    expect(chartKind(null)).toBe('bar')
  })
})

describe('Pulse', () => {
  it('sin analytics.view: bienvenida sin datos y sin consultar el API', async () => {
    mock.handler = () => ({ indicators: [], charts: [] })
    renderPulse({ permissions: [], modules: ['ANALYTICS'] })
    expect(screen.getByText('Bienvenido, Ana Admin')).toBeInTheDocument()
    expect(screen.getByText(/no tiene permiso/)).toBeInTheDocument()
    expect(mock.calls).toEqual([])
  })

  it('módulo ANALYTICS apagado: bienvenida sin datos, sin consultar el API ni ir a "Módulo apagado"', async () => {
    mock.handler = () => ({ indicators: [], charts: [] })
    renderPulse({ permissions: ['analytics.view'], modules: [] })
    expect(screen.getByText('Bienvenido, Ana Admin')).toBeInTheDocument()
    expect(screen.getByText(/módulo de Análisis/)).toBeInTheDocument()
    expect(mock.calls).toEqual([])
    expect(onAccessDenied).not.toHaveBeenCalled()
  })

  it('un 403 module_disabled del API no redirige (evita el ciclo con /module-off): muestra el error en su lugar', async () => {
    mock.handler = () =>
      new Response(JSON.stringify({ status: 403, code: 'module_disabled', title: 'El módulo ANALYTICS no está activo.' }), {
        status: 403,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    renderPulse(FULL)
    expect(await screen.findByText('El módulo ANALYTICS no está activo.')).toBeInTheDocument()
    expect(onAccessDenied).not.toHaveBeenCalled()
  })

  it('sin indicadores ni gráficos en Pulso: estado vacío', async () => {
    mock.handler = () => ({ indicators: [], charts: [] })
    renderPulse(FULL)
    expect(await screen.findByText('Todavía no hay nada en Pulso')).toBeInTheDocument()
  })

  it('pinta indicadores (valor formateado y rango con la etiqueta del catálogo) y gráficos', async () => {
    mock.handler = (path) => {
      if (path === '/api/v1/catalogs/DateRangeMode')
        return [
          { code: 'LAST7', label: 'Últimos 7 días', sortOrder: 1, isEnabled: true },
          { code: 'LAST90', label: 'Últimos 90 días', sortOrder: 2, isEnabled: true },
        ]
      return {
        indicators: [
          { id: 1, name: 'Ventas', value: 15230.5, isMoney: true, dateRangeMode: 'LAST90' },
          { id: 2, name: 'Órdenes', value: 1200, isMoney: false, dateRangeMode: 'ALL' },
        ],
        charts: [{ id: 3, name: 'Órdenes por día', chartType: 'LINE', isMoney: false, points: [{ label: 'Lun', value: 3 }] }],
      }
    }
    renderPulse(FULL)
    expect(await screen.findByText('$15,230.50')).toBeInTheDocument()
    expect(screen.getByText('1,200')).toBeInTheDocument()
    expect(await screen.findByText('Últimos 90 días')).toBeInTheDocument()
    expect(screen.getByText('Órdenes por día')).toBeInTheDocument()
    // Rejilla auto-fill con mínimo de 220 px: a 360 px (stage de ~336 px) cabe una sola columna, las tarjetas se apilan.
    const grid = screen.getByRole('heading', { name: 'Indicadores' }).nextElementSibling as HTMLElement
    expect(grid.getAttribute('style')).toContain('minmax(220px, 1fr)')
  })

  it('rango CUSTOM: muestra el Hasta que eligió el usuario (no el límite exclusivo del servidor)', async () => {
    mock.handler = () => ({
      indicators: [{ id: 1, name: 'Ventas', value: 10, isMoney: false, dateRangeMode: 'CUSTOM', fromUtc: '2026-09-01T00:00:00', toUtc: '2026-09-16T00:00:00' }],
      charts: [],
    })
    renderPulse(FULL)
    expect(await screen.findByText(`${formatYmd('2026-09-01', 'es')} → ${formatYmd('2026-09-15', 'es')}`)).toBeInTheDocument()
  })

  it('"Rango" cambia MI rango de la tarjeta (PUT my-date-range) con Desde/Hasta y recalcula Pulso', async () => {
    let saved = false
    mock.handler = (path: string, method: string) => {
      if (path === '/api/v1/catalogs/DateRangeMode')
        return [
          { code: 'LAST7', label: 'Últimos 7 días', sortOrder: 1, isEnabled: true },
          { code: 'CUSTOM', label: 'Personalizado', sortOrder: 2, isEnabled: true },
        ]
      if (method === 'PUT') {
        saved = true
        return { id: 1, name: 'Ventas' }
      }
      return {
        indicators: [
          saved
            ? { id: 1, name: 'Ventas', value: 5, isMoney: false, dateRangeMode: 'CUSTOM', fromUtc: '2026-09-01T00:00:00', toUtc: '2026-09-16T00:00:00' }
            : { id: 1, name: 'Ventas', value: 10, isMoney: false, dateRangeMode: 'LAST7' },
        ],
        charts: [],
      }
    }
    const user = userEvent.setup()
    renderPulse(FULL)
    await user.click(await screen.findByRole('button', { name: 'Cambiar mi rango de fecha de Ventas' }))
    const dialog = await screen.findByRole('dialog')
    const mode = within(dialog).getByLabelText(/^Rango/)
    await waitFor(() => expect(within(mode).getByRole('option', { name: 'Personalizado' })).toBeInTheDocument())
    await user.selectOptions(mode, 'CUSTOM')
    // Validación en cliente: Desde y Hasta obligatorios en el personalizado.
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Indique la fecha Desde.')).toBeInTheDocument()
    expect(mock.writes).toEqual([])
    await user.type(within(dialog).getByLabelText(/^Desde/), '2026-09-01')
    await user.type(within(dialog).getByLabelText(/^Hasta/), '2026-09-15')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() =>
      expect(mock.writes).toEqual([
        {
          method: 'PUT',
          path: '/api/v1/analytics/indicators/1/my-date-range',
          body: { dateRangeMode: 'CUSTOM', dateFrom: '2026-09-01', dateTo: '2026-09-15' },
        },
      ]),
    )
    expect(await screen.findByText(`${formatYmd('2026-09-01', 'es')} → ${formatYmd('2026-09-15', 'es')}`)).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
