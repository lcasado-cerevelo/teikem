import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient, setAccessDeniedHandler } from '../../app/queryClient'
import { SessionContext, type MeDto, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { dismissToast, getToast } from '../../kernel/ui/toastStore'
import { apiDateToYmd, chartKind, customRangeDays, formatValue, formatYmd, pulseDateTitle } from './format'
import Pulse from './Pulse'
import type { PulseDto, PulsePanelDto } from './pulseLayout'

// Cliente de la app sobre un fetch simulado (misma política que el real).
type Handler = (path: string, method: string, url: URL) => Response | unknown
const mock = vi.hoisted(() => ({ calls: [] as string[], urls: [] as string[], writes: [] as { method: string; path: string; body: unknown }[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const path = url.pathname
    mock.calls.push(path)
    mock.urls.push(`${path}${url.search}`)
    if (req.method !== 'GET') mock.writes.push({ method: req.method, path, body: await req.clone().json().catch(() => null) })
    const result = (mock.handler as Handler)(path, req.method, url)
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

function renderPulse(access: { permissions: string[]; modules: string[] } = FULL) {
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

/** Panel del DTO (visible y con fuente por defecto salvo que se diga otra cosa). */
const panel = (key: string, sortOrder: number, isVisible = true): PulsePanelDto => ({ key, sortOrder, isVisible, source: 'default' })
const IND_AND_CHARTS = [panel('INDICATORS', 20), panel('CHARTS', 30)]

/** Pulso del API con valores por defecto: solo lo que cada prueba necesita cambiar. */
function pulse(over: Partial<PulseDto> = {}): PulseDto {
  return { indicators: [], charts: [], panels: IND_AND_CHARTS, hasPersonalLayout: false, canOrganizeCompany: false, ...over }
}

/** Orden de las secciones pintadas (atributo `data-panel` de cada una). */
const paintedPanels = (container: HTMLElement) => Array.from(container.querySelectorAll('[data-panel]')).map((el) => el.getAttribute('data-panel'))

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
  mock.urls = []
  mock.writes = []
  onAccessDenied.mockReset()
  dismissToast()
})

describe('formatValue / chartKind / pulseDateTitle', () => {
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
    expect(formatYmd('2026-09-15', 'en')).toBe('09/15/2026')
  })

  it('elige el gráfico por chartType: LINE, DONUT, PIE; el resto barras', () => {
    expect(chartKind('LINE')).toBe('line')
    expect(chartKind('donut')).toBe('donut')
    expect(chartKind('PIE')).toBe('pie')
    expect(chartKind('BAR')).toBe('bar')
    expect(chartKind(null)).toBe('bar')
  })

  it('título = fecha del día en el idioma, solo la primera letra en mayúscula', () => {
    const day = new Date(2026, 8, 28, 10, 0, 0)
    expect(pulseDateTitle(day, 'es')).toBe('Lunes, 28 de septiembre')
    expect(pulseDateTitle(day, 'en')).toBe('Monday, September 28')
  })
})

describe('Pulse — cabecera', () => {
  it('fecha del día como título, saludo, "Pulso de la compañía" y solo "Organizar mi Pulso" sin canOrganizeCompany', async () => {
    mock.handler = () => pulse({ indicators: [{ id: 1, name: 'Ventas', value: 10, isMoney: false, isVisible: true }] })
    renderPulse()
    expect(await screen.findByText('Ventas')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(pulseDateTitle(new Date(), 'es'))
    expect(screen.getByText('Bienvenido, Ana Admin. Así viene el día en su compañía.')).toBeInTheDocument()
    expect(screen.getByText('Pulso de la compañía')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Organizar mi Pulso' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Organizar el de la compañía' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Volver al de la compañía' })).not.toBeInTheDocument()
  })

  it('con canOrganizeCompany aparece "Organizar el de la compañía" y abre el modo compañía', async () => {
    mock.handler = () => pulse({ canOrganizeCompany: true, indicators: [{ id: 1, name: 'Ventas', value: 10, isVisible: true }] })
    const user = userEvent.setup()
    renderPulse()
    await user.click(await screen.findByRole('button', { name: 'Organizar el de la compañía' }))
    expect(screen.getByRole('region', { name: /Organizando el Pulso de la compañía/ })).toBeInTheDocument()
    // mientras organiza, la cabecera no ofrece los botones de organizar
    expect(screen.queryByRole('button', { name: 'Organizar mi Pulso' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))
    expect(await screen.findByRole('button', { name: 'Organizar mi Pulso' })).toBeInTheDocument()
    expect(screen.getByText('Ventas')).toBeInTheDocument()
    expect(mock.writes).toEqual([])
  })

  it('"Organizar el de la compañía" parte del Pulso de LA COMPAÑÍA (GET ?scope=company), no del personal de quien lo abre', async () => {
    // Hallazgo P1: si el admin tiene su propio orden/ocultos, "Organizar el de la compañía" no debe partir de ESO.
    mock.handler = (_path: string, _method: string, url: URL) =>
      url.search.includes('scope=company')
        ? pulse({ canOrganizeCompany: true, indicators: [{ id: 9, name: 'De la compañía', value: 1, isVisible: true }] })
        : pulse({ canOrganizeCompany: true, hasPersonalLayout: true, indicators: [{ id: 1, name: 'Personal del admin', value: 1, isVisible: true }] })
    const user = userEvent.setup()
    renderPulse()
    await user.click(await screen.findByRole('button', { name: 'Organizar el de la compañía' }))
    expect(mock.urls).toContain('/api/v1/analytics/pulse?scope=company')
    expect(await screen.findByText('De la compañía')).toBeInTheDocument()
    expect(screen.queryByText('Personal del admin')).toBeNull()
  })

  it('"Volver al de la compañía" pide confirmación y llama al DELETE layout/mine', async () => {
    let personal = true
    mock.handler = (_path: string, method: string) => {
      if (method === 'DELETE') {
        personal = false
        return new Response(null, { status: 204 })
      }
      return pulse({ hasPersonalLayout: personal, indicators: [{ id: 1, name: 'Ventas', value: 10, isVisible: true }] })
    }
    const user = userEvent.setup()
    renderPulse()
    expect(await screen.findByText('Pulso personal')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Volver al de la compañía' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Sus rangos de fecha se conservan/)).toBeInTheDocument()
    expect(mock.writes).toEqual([])
    await user.click(within(dialog).getByRole('button', { name: 'Volver al de la compañía' }))
    await waitFor(() => expect(mock.writes).toEqual([{ method: 'DELETE', path: '/api/v1/analytics/pulse/layout/mine', body: null }]))
    expect(await screen.findByText('Pulso de la compañía')).toBeInTheDocument()
    expect(getToast()?.message).toBe('Ahora ve el Pulso de la compañía.')
  })
})

describe('Pulse — paneles del API', () => {
  it('pinta los paneles en su sortOrder, ignora claves desconocidas y no pinta los ocultos (ni consulta sus datos)', async () => {
    mock.handler = () =>
      pulse({
        panels: [panel('INDICATORS', 30), panel('RADAR', 5), panel('CHARTS', 10), panel('WAREHOUSE', 20, false), panel('ACTIVITY', 40, false)],
        indicators: [{ id: 1, name: 'Ventas', value: 10, isVisible: true }],
        charts: [{ id: 3, name: 'Órdenes por día', chartType: 'BAR', isVisible: true, points: [{ label: 'Lun', value: 3 }] }],
      })
    const { container } = renderPulse({ permissions: ['analytics.view', 'inventory.view'], modules: ['ANALYTICS', 'WMS_LOTSERIAL'] })
    expect(await screen.findByText('Ventas')).toBeInTheDocument()
    expect(paintedPanels(container)).toEqual(['CHARTS', 'INDICATORS'])
    expect(screen.getByRole('heading', { name: 'Tus gráficos' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Tus indicadores' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Almacén' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Actividad reciente' })).not.toBeInTheDocument()
    // ni almacén ni actividad: solo lo que devuelve el Pulso
    expect(mock.calls.filter((c) => c !== '/api/v1/analytics/pulse')).toEqual([])
  })

  it('río de indicadores: orden por sortOrder, sin los ocultos, azul cantidad / naranja dinero, el nodo lleva a Indicadores', async () => {
    mock.handler = (path: string) => {
      if (path === '/api/v1/catalogs/DateRangeMode') return [{ code: 'LAST90', label: 'Últimos 90 días', sortOrder: 1, isEnabled: true }]
      return pulse({
        indicators: [
          { id: 1, name: 'Ventas', value: 15230.5, isMoney: true, dateRangeMode: 'LAST90', isVisible: true, sortOrder: 20, businessModule: 'ACCOUNTING' },
          { id: 2, name: 'Oculto', value: null, isVisible: false, sortOrder: 0 },
          { id: 3, name: 'Órdenes', value: 1200, isMoney: false, dateRangeMode: 'ALL', isVisible: true, sortOrder: 10, businessModule: 'WAREHOUSE' },
        ],
      })
    }
    renderPulse()
    expect(await screen.findByText('$15,230.50')).toBeInTheDocument()
    expect(screen.queryByText('Oculto')).not.toBeInTheDocument()
    const links = screen.getAllByRole('link').filter((a) => a.getAttribute('href') === '/analytics/indicators')
    expect(links.map((a) => a.textContent)).toEqual([expect.stringContaining('Órdenes'), expect.stringContaining('Ventas')])
    // subtítulo: el rango (etiqueta del catálogo); sin rango, nada (Lote 15: el módulo ya lo dice la línea)
    expect(await screen.findByText('Últimos 90 días')).toBeInTheDocument()
    expect(within(links[0]).queryByText('Almacén')).not.toBeInTheDocument()
    expect(links[0].querySelector('.sub')).toBeNull()
    expect(links[0].closest('.node')).toHaveClass('flow')
    expect(links[1].closest('.node')).toHaveClass('money')
  })

  it('Lote 15 (D8): una línea por módulo en el orden del menú, con su etiqueta h3; dentro, el orden de siempre', async () => {
    mock.handler = () =>
      pulse({
        indicators: [
          { id: 1, name: 'COD por cobrar', value: 5, isMoney: true, isVisible: true, sortOrder: 0, businessModule: 'ACCOUNTING' },
          { id: 2, name: 'Unidades recibidas', value: 12, isVisible: true, sortOrder: 10, businessModule: 'WAREHOUSE' },
          { id: 3, name: 'Órdenes abiertas', value: 3, isVisible: true, sortOrder: 20, businessModule: 'OPERATIONS' },
          { id: 4, name: 'Bajo mínimo', value: 1, isVisible: true, sortOrder: 30, businessModule: 'WAREHOUSE' },
          { id: 5, name: 'Usuarios activos', value: 9, isVisible: true, sortOrder: 40 },
        ],
      })
    const { container } = renderPulse()
    expect(await screen.findByText('COD por cobrar')).toBeInTheDocument()
    // "Tus indicadores" sigue siendo el h2 de la sección; las líneas son h3 (nunca h2 "Almacén", hallazgo 13)
    const section = screen.getByRole('heading', { level: 2, name: 'Tus indicadores' }).closest('section') as HTMLElement
    expect(within(section).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['Operación', 'Almacén', 'Contabilidad'])
    expect(within(section).queryByRole('heading', { level: 2, name: 'Almacén' })).not.toBeInTheDocument()
    const lines = Array.from(container.querySelectorAll('.pulse-line'))
    expect(lines.map((l) => l.getAttribute('data-line'))).toEqual(['ops', 'warehouse', 'money'])
    const names = (el: Element) => Array.from(el.querySelectorAll('.node .ph span')).map((s) => s.textContent)
    expect(names(lines[0])).toEqual(['Órdenes abiertas', 'Usuarios activos'])
    expect(names(lines[1])).toEqual(['Unidades recibidas', 'Bajo mínimo'])
    expect(names(lines[2])).toEqual(['COD por cobrar'])
    // cada línea es su propio río
    expect(lines.every((l) => l.querySelector(':scope > .river') != null)).toBe(true)
  })

  it('2026-10-01: máximo 5 indicadores por fila; el sexto en adelante pasa a otra fila de la misma línea', async () => {
    mock.handler = () =>
      pulse({
        indicators: Array.from({ length: 7 }, (_, i) => ({ id: 100 + i, name: `Ind ${i + 1}`, value: i, isVisible: true, sortOrder: i, businessModule: 'WAREHOUSE' })),
      })
    const { container } = renderPulse()
    expect(await screen.findByText('Ind 7')).toBeInTheDocument()
    const line = container.querySelector('.pulse-line[data-line="warehouse"]') as HTMLElement
    const rows = Array.from(line.querySelectorAll(':scope > .river'))
    expect(rows.map((r) => r.querySelectorAll('.node').length)).toEqual([5, 2])
    // la línea punteada no cruza filas: cada fila tiene un tubo menos que tarjetas
    expect(rows.map((r) => r.querySelectorAll('.pipe').length)).toEqual([4, 1])
  })

  it('Lote 15 (D8): sin indicadores de Contabilidad no hay línea Contabilidad', async () => {
    mock.handler = () => pulse({ indicators: [{ id: 2, name: 'Unidades recibidas', value: 12, isVisible: true, businessModule: 'WAREHOUSE' }] })
    renderPulse()
    expect(await screen.findByText('Unidades recibidas')).toBeInTheDocument()
    expect(screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['Almacén'])
  })

  it('sin analytics.view el nodo no es enlace (Indicadores le daría "Sin permiso") y no hay "Rango"', async () => {
    mock.handler = () => pulse({ indicators: [{ id: 1, name: 'Ventas', value: 10, isVisible: true }] })
    renderPulse({ permissions: ['pulse.indicators'], modules: ['ANALYTICS'] })
    expect(await screen.findByText('Ventas')).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Cambiar mi rango/ })).not.toBeInTheDocument()
  })

  it('gráficos (Lote 15): SIEMPRE el gráfico, también con 1–3 puntos (nunca la lista); sin puntos, el aviso; 2 por fila como mucho', async () => {
    mock.handler = () =>
      pulse({
        charts: [
          { id: 3, name: 'Pocos', chartType: 'BAR', isMoney: true, isVisible: true, sortOrder: 0, points: [{ label: 'Lun', value: 3 }, { label: 'Mar', value: 4.5 }] },
          { id: 4, name: 'Muchos', chartType: 'DONUT', isVisible: true, sortOrder: 10, points: [1, 2, 3, 4].map((v) => ({ label: `P${v}`, value: v })) },
          { id: 5, name: 'Vacío', chartType: 'LINE', isVisible: true, sortOrder: 20, points: [] },
          { id: 6, name: 'Un día', chartType: 'LINE', isVisible: true, sortOrder: 30, points: [{ label: '2026-09-30', value: 7 }] },
        ],
      })
    const { container } = renderPulse()
    // ChartVisual: envoltorio role="img" con los valores (el gráfico, no una lista)
    const pocos = await screen.findByRole('img', { name: 'Pocos. Lun: $3.00; Mar: $4.50' })
    expect(pocos).toHaveAttribute('data-chart-kind', 'bar')
    expect(pocos).toHaveAttribute('data-points', '2')
    expect(screen.getByRole('img', { name: /^Muchos\./ })).toHaveAttribute('data-chart-kind', 'donut')
    expect(screen.getByRole('img', { name: 'Un día. miércoles, 30 de septiembre de 2026: 7' })).toHaveAttribute('data-chart-kind', 'line')
    expect(screen.queryByRole('list', { name: 'Pocos' })).not.toBeInTheDocument()
    expect(container.querySelector('.pulse-pts')).toBeNull()
    expect(screen.getByText('Este gráfico no tiene datos en el rango configurado.')).toBeInTheDocument()
    const grid = screen.getByRole('heading', { name: 'Tus gráficos' }).nextElementSibling as HTMLElement
    expect(grid).toHaveClass('pulse-charts')
    expect(grid.querySelectorAll('.pulse-chart')).toHaveLength(4)
    // nunca más de 2 por fila (cada columna mide al menos la mitad menos el hueco); una columna en celular (min 100%)
    expect(grid.style.gridTemplateColumns).toBe('repeat(auto-fill, minmax(min(100%, max(380px, calc(50% - 8px))), 1fr))')
  })

  it('rango CUSTOM: muestra el Hasta que eligió el usuario (no el límite exclusivo del servidor)', async () => {
    mock.handler = () =>
      pulse({
        indicators: [{ id: 1, name: 'Ventas', value: 10, isMoney: false, isVisible: true, dateRangeMode: 'CUSTOM', fromUtc: '2026-09-01T00:00:00', toUtc: '2026-09-16T00:00:00' }],
      })
    renderPulse()
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
      return pulse({
        indicators: [
          saved
            ? { id: 1, name: 'Ventas', value: 5, isMoney: false, isVisible: true, dateRangeMode: 'CUSTOM', fromUtc: '2026-09-01T00:00:00', toUtc: '2026-09-16T00:00:00' }
            : { id: 1, name: 'Ventas', value: 10, isMoney: false, isVisible: true, dateRangeMode: 'LAST7' },
        ],
      })
    }
    const user = userEvent.setup()
    renderPulse()
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

  it('un 403 module_disabled del API no redirige (evita el ciclo con /module-off): muestra el error en su lugar', async () => {
    mock.handler = () =>
      new Response(JSON.stringify({ status: 403, code: 'module_disabled', title: 'El módulo ANALYTICS no está activo.' }), {
        status: 403,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    renderPulse()
    expect(await screen.findByText('El módulo ANALYTICS no está activo.')).toBeInTheDocument()
    expect(onAccessDenied).not.toHaveBeenCalled()
  })
})

describe('Pulse — estados vacíos', () => {
  it('sin paneles: bienvenida, sin botones de organizar ni chip', async () => {
    mock.handler = () => pulse({ panels: [], canOrganizeCompany: true })
    renderPulse({ permissions: [], modules: [] })
    expect(await screen.findByText('Bienvenido, Ana Admin')).toBeInTheDocument()
    expect(screen.getByText(/todavía no tiene paneles/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Organizar/ })).not.toBeInTheDocument()
    expect(screen.queryByText('Pulso de la compañía')).not.toBeInTheDocument()
    // la pantalla de inicio siempre consulta el Pulso: el servidor decide qué ve el usuario
    expect(mock.calls).toEqual(['/api/v1/analytics/pulse'])
  })

  it('solo un panel desconocido: también es la bienvenida', async () => {
    mock.handler = () => pulse({ panels: [panel('RADAR', 60)] })
    renderPulse()
    expect(await screen.findByText('Bienvenido, Ana Admin')).toBeInTheDocument()
  })

  it('paneles de indicadores y gráficos sin elementos visibles: mensaje con enlace a Indicadores', async () => {
    mock.handler = () => pulse({ indicators: [{ id: 1, name: 'Oculto', isVisible: false }] })
    const { container } = renderPulse()
    expect(await screen.findByText('Aún no tienes indicadores ni gráficos en tu Pulso')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir a Indicadores' })).toHaveAttribute('href', '/analytics/indicators')
    expect(paintedPanels(container)).toEqual([])
    // se puede organizar para volver a mostrar lo oculto
    expect(screen.getByRole('button', { name: 'Organizar mi Pulso' })).toBeInTheDocument()
  })

  it('sin elementos pero con el panel Almacén: la sección vacía no se pinta y no hay mensaje', async () => {
    mock.handler = warehouseHandler
    const { container } = renderPulse(WAREHOUSE_ACCESS)
    expect(await screen.findByRole('heading', { name: 'Almacén' })).toBeInTheDocument()
    expect(paintedPanels(container)).toEqual(['WAREHOUSE'])
    expect(screen.queryByText('Aún no tienes indicadores ni gráficos en tu Pulso')).not.toBeInTheDocument()
  })
})

// ---------------------------------------------------------------------------------------------------------------------
// Panel Almacén (F6/F7A): entrada WAREHOUSE del registro, sin cambios de comportamiento.
// ---------------------------------------------------------------------------------------------------------------------
const WAREHOUSE_ACCESS = { permissions: ['analytics.view', 'inventory.view'], modules: ['ANALYTICS', 'WMS_LOTSERIAL'] }
const TASKS_BY_TYPE: Record<string, number> = { PUTAWAY: 4, REPLENISH: 2, COUNT: 1, CROSSDOCK: 0 }
let warehousePulse: PulseDto = pulse({ panels: [panel('INDICATORS', 20), panel('WAREHOUSE', 40)] })

function warehouseHandler(path: string, _method: string, url: URL): unknown {
  if (path === '/api/v1/analytics/pulse') return warehousePulse
  if (path === '/api/v1/catalogs/WarehouseTaskType')
    return [
      { code: 'PUTAWAY', label: 'Acomodo', sortOrder: 1, isEnabled: true },
      { code: 'REPLENISH', label: 'Reabasto', sortOrder: 2, isEnabled: true },
    ]
  if (path === '/api/v1/inventory/balances') return { total: 37, skip: 0, take: 1, totalOnHand: 1250.5, totalAvailable: 1000, items: [] }
  if (path === '/api/v1/receipts') return { total: 3, skip: 0, take: 1, items: [] }
  if (path === '/api/v1/warehouse-tasks') {
    const type = url.searchParams.get('types')
    return { total: type ? TASKS_BY_TYPE[type] : 7, skip: 0, take: 1, items: [] }
  }
  if (path === '/api/v1/cycle-counts/page') return { total: 2, skip: 0, take: 1, items: [] }
  return []
}

describe('Pulse — panel Almacén (Lote F6)', () => {
  const tile = (name: string) => screen.findByRole('group', { name })
  beforeEach(() => {
    warehousePulse = pulse({ panels: [panel('INDICATORS', 20), panel('WAREHOUSE', 40)] })
  })

  it('con el panel WAREHOUSE del API: saldo, recibos abiertos, tareas por tipo y conteos abiertos, sin rango de fecha', async () => {
    warehousePulse = pulse({
      panels: [panel('INDICATORS', 20), panel('WAREHOUSE', 40)],
      indicators: [{ id: 1, name: 'Ventas', value: 10, isMoney: false, isVisible: true }],
    })
    mock.handler = warehouseHandler
    const { container } = renderPulse(WAREHOUSE_ACCESS)
    expect(await screen.findByRole('heading', { name: 'Almacén' })).toBeInTheDocument()
    await waitFor(async () => expect(within(await tile('En almacén')).getByText('1,250.50')).toBeInTheDocument())
    await waitFor(async () => expect(within(await tile('Disponible')).getByText('1,000')).toBeInTheDocument())
    await waitFor(async () => expect(within(await tile('Recibos abiertos')).getByText('3')).toBeInTheDocument())
    await waitFor(async () => expect(within(await tile('Conteos abiertos')).getByText('2')).toBeInTheDocument())
    const tasks = await tile('Tareas pendientes')
    await waitFor(() => expect(within(tasks).getByText('7')).toBeInTheDocument())
    // por tipo: etiqueta del catálogo del tenant; sin etiqueta, el código
    await waitFor(() => expect(within(within(tasks).getByText('Acomodo').closest('li') as HTMLElement).getByText('4')).toBeInTheDocument())
    expect(within(within(tasks).getByText('Reabasto').closest('li') as HTMLElement).getByText('2')).toBeInTheDocument()
    expect(within(within(tasks).getByText('CROSSDOCK').closest('li') as HTMLElement).getByText('0')).toBeInTheDocument()
    // consultas acotadas (take=1: solo los totales) y sin fecha
    expect(mock.urls).toContain('/api/v1/inventory/balances?includeZero=false&take=1')
    expect(mock.urls).toContain('/api/v1/receipts?phase=OPEN&take=1')
    expect(mock.urls).toContain('/api/v1/warehouse-tasks?includeClosed=false&take=1')
    expect(mock.urls).toContain('/api/v1/warehouse-tasks?includeClosed=false&take=1&types=PUTAWAY')
    // Lote 14 (hallazgo 14): total de la página con los estatus abiertos (Pendiente y Contado), no el largo de una lista cortada en 200
    expect(mock.urls).toContain('/api/v1/cycle-counts/page?status=OPEN&status=COUNTED&take=1')
    expect(mock.urls.some((u) => /from|to=|fromUtc|toUtc/.test(u) && !u.includes('analytics'))).toBe(false)
    // Recibos abiertos abre el río (clases de la maqueta); las tarjetas restantes, una columna a 360 px: la columna
    // mínima nunca excede el ancho del panel
    expect((await tile('Recibos abiertos')).parentElement).toHaveClass('river')
    const grid = (await tile('Tareas pendientes')).parentElement as HTMLElement
    expect(grid.getAttribute('style')).toContain('minmax(min(100%, 200px), 1fr)')
    // los indicadores del API siguen arriba (orden del registro)
    expect(paintedPanels(container)).toEqual(['INDICATORS', 'WAREHOUSE'])
  })

  it('el API no devuelve WAREHOUSE (sin pulse.warehouse, inventory.view o WMS_LOTSERIAL): ni panel ni consultas de almacén', async () => {
    warehousePulse = pulse({ indicators: [{ id: 1, name: 'Ventas', value: 10, isVisible: true }] })
    mock.handler = warehouseHandler
    renderPulse(WAREHOUSE_ACCESS)
    expect(await screen.findByText('Ventas')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Almacén' })).not.toBeInTheDocument()
    expect(mock.calls.filter((c) => c !== '/api/v1/analytics/pulse' && !c.startsWith('/api/v1/catalogs/'))).toEqual([])
  })

  it('un 403 de un endpoint de almacén no saca de Pulso: la tarjeta muestra —', async () => {
    mock.handler = (path: string, method: string, url: URL) =>
      path === '/api/v1/receipts'
        ? new Response(JSON.stringify({ status: 403, code: 'forbidden', title: 'Sin permiso.' }), {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        : warehouseHandler(path, method, url)
    renderPulse(WAREHOUSE_ACCESS)
    await waitFor(async () => expect(within(await tile('Recibos abiertos')).getByText('—')).toBeInTheDocument())
    await waitFor(async () => expect(within(await tile('Conteos abiertos')).getByText('2')).toBeInTheDocument())
    expect(onAccessDenied).not.toHaveBeenCalled()
  })
})

// ---------------------------------------------------------------------------------------------------------------------
// Lote 15 (P4, P6): franja "Almacén hoy" (WAREHOUSE_DAY) y filas fijas.
// ---------------------------------------------------------------------------------------------------------------------
const DAY_DATES = ['2026-09-24', '2026-09-25', '2026-09-26', '2026-09-27', '2026-09-28', '2026-09-29', '2026-09-30']
const DAYS_DTO = {
  timeZone: 'America/Puerto_Rico',
  today: '2026-09-30',
  fromUtc: '2026-09-24T04:00:00Z',
  toUtc: '2026-10-01T04:00:00Z',
  days: DAY_DATES.map((date, i) => ({ date, receivedUnits: i === 6 ? 12 : i, outboundUnits: i === 6 ? 5 : 0, countsWithVariance: i === 6 ? 1 : 0 })),
  receivedToday: 12,
  receivedTotal: 1234,
  outboundToday: 5,
  outboundTotal: 5,
  countsWithVarianceToday: 1,
  countsWithVarianceTotal: 1,
  belowMinProducts: 3,
  countsAlert: true,
  belowMinAlert: true,
}
let dayPulse: PulseDto = pulse()

function dayHandler(path: string): unknown {
  if (path === '/api/v1/analytics/pulse') return dayPulse
  if (path === '/api/v1/inventory/pulse/days') return DAYS_DTO
  return []
}

const withIndicator = (panels: PulsePanelDto[]) => pulse({ panels, indicators: [{ id: 1, name: 'Ventas', value: 10, isVisible: true }] })

describe('Pulse — franja "Almacén hoy" (Lote 15)', () => {
  const card = (container: HTMLElement, key: string) => container.querySelector(`[data-card="${key}"]`) as HTMLElement
  beforeEach(() => {
    window.localStorage.clear()
    dayPulse = withIndicator([panel('WAREHOUSE_DAY', -10), panel('INDICATORS', 20)])
  })

  it('4 tarjetas: número de HOY, total de 7 días, 7 barritas (bajo mínimo sin gráfico), naranja y enlaces al detalle', async () => {
    mock.handler = dayHandler
    const { container } = renderPulse(WAREHOUSE_ACCESS)
    // título que no es "Almacén" a secas (los recorridos buscan ese h2 del panel Almacén)
    const heading = await screen.findByRole('heading', { level: 2, name: /^Almacén hoy/ })
    // solo "Almacén hoy": los 7 días los muestra cada tarjeta (pedido de Luis, 2026-09-30)
    expect(heading).toHaveTextContent(/^Almacén hoy$/)
    expect(screen.queryByRole('heading', { level: 2, name: 'Almacén' })).not.toBeInTheDocument()
    const band = heading.closest('section') as HTMLElement
    await waitFor(() => expect(within(card(container, 'received')).getByText('12')).toBeInTheDocument())
    expect(Array.from(band.querySelectorAll('[data-card]')).map((n) => n.getAttribute('data-card'))).toEqual([
      'received',
      'outbound',
      'countsVariance',
      'belowMin',
    ])
    expect(within(card(container, 'received')).getByText('Unidades recibidas')).toBeInTheDocument()
    expect(within(card(container, 'received')).getByText('7 días: 1,234')).toBeInTheDocument()
    expect(within(card(container, 'outbound')).getByText('5', { selector: '.big' })).toBeInTheDocument()
    expect(within(card(container, 'countsVariance')).getByText('Conteos con diferencia')).toBeInTheDocument()
    expect(within(card(container, 'belowMin')).getByText('3', { selector: '.big' })).toBeInTheDocument()
    expect(within(card(container, 'belowMin')).getByText('en este momento')).toBeInTheDocument()
    // 7 barritas por tarjeta (ChartVisual mini, con la fecha y la cantidad en su etiqueta accesible); bajo mínimo sin gráfico
    for (const key of ['received', 'outbound', 'countsVariance']) {
      const chart = within(card(container, key)).getByRole('img')
      expect(chart).toHaveAttribute('data-points', '7')
      expect(chart).toHaveClass('pulse-chartbox', 'mini')
    }
    expect(within(card(container, 'received')).getByRole('img')).toHaveAccessibleName(/^Unidades recibidas\. .*miércoles, 30 de septiembre de 2026: 12$/)
    expect(within(card(container, 'belowMin')).queryByRole('img')).toBeNull()
    // naranja (D3): conteos con diferencia hoy y productos bajo mínimo; recibido y salida, violeta
    expect(card(container, 'countsVariance')).toHaveClass('node', 'wh', 'alert')
    expect(card(container, 'belowMin')).toHaveClass('alert')
    expect(card(container, 'received')).not.toHaveClass('alert')
    expect(within(card(container, 'countsVariance')).getByText('Hoy hubo conteos con diferencia.')).toHaveClass('sr-only')
    // clic (D4): el detalle filtrado con los 7 días (todos los almacenes: sin warehousePublicIds)
    const hrefs = Array.from(band.querySelectorAll('[data-card] a')).map((a) => a.getAttribute('href'))
    expect(hrefs).toEqual([
      '/warehouse/kardex?types=RECEIPT&from=2026-09-24&to=2026-09-30',
      '/warehouse/kardex?types=ISSUE&types=CROSSDOCK&from=2026-09-24&to=2026-09-30',
      '/warehouse/cycle-counts?status=RECONCILED_VARIANCE',
      '/warehouse/products?kpi=low',
    ])
    expect(mock.urls).toContain('/api/v1/inventory/pulse/days?days=7')
  })

  it('sin tono naranja cuando el servidor no lo pide', async () => {
    mock.handler = (path: string) => (path === '/api/v1/inventory/pulse/days' ? { ...DAYS_DTO, countsAlert: false, belowMinAlert: false } : dayHandler(path))
    const { container } = renderPulse(WAREHOUSE_ACCESS)
    await waitFor(() => expect(within(card(container, 'received')).getByText('12')).toBeInTheDocument())
    expect(container.querySelectorAll('[data-card].alert')).toHaveLength(0)
  })

  it('un 403 de la franja no saca del Pulso: "—" en las tarjetas y sin barritas', async () => {
    mock.handler = (path: string) =>
      path === '/api/v1/inventory/pulse/days'
        ? new Response(JSON.stringify({ status: 403, code: 'forbidden', title: 'Sin permiso.' }), {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        : dayHandler(path)
    const { container } = renderPulse(WAREHOUSE_ACCESS)
    await waitFor(() => expect(within(card(container, 'received')).getByText('—')).toBeInTheDocument())
    expect(container.querySelectorAll('[data-card] [role="img"]')).toHaveLength(0)
    expect(onAccessDenied).not.toHaveBeenCalled()
  })

  it('encabezado fijo (D6): fecha, saludo y chip quedan fijos; "Almacén hoy" no se fija; solo en .pulse-home', async () => {
    mock.handler = dayHandler
    const { container } = renderPulse(WAREHOUSE_ACCESS)
    await screen.findByRole('heading', { level: 2, name: /^Almacén hoy/ })
    expect(container.querySelector('.wrap.pulse')).toHaveClass('pulse-home')
    expect(container.querySelector('[data-pinned]')).toBeNull()
    // el encabezado completo es el fijo: fecha + Organizar, saludo y chip van juntos en el mismo contenedor fijo
    const head = screen.getByRole('heading', { level: 1 }).closest('.pulse-pin-wrap') as HTMLElement
    expect(head).toHaveClass('pinned')
    expect(within(head).getByRole('button', { name: 'Organizar mi Pulso' })).toBeInTheDocument()
    expect(within(head).getByText('Pulso de la compañía')).toBeInTheDocument()
    expect(head.querySelector('.pulse-greet')).toHaveTextContent('Bienvenido')
  })

  it('la franja Almacén hoy, esté donde esté, no se fija (solo la fecha)', async () => {
    dayPulse = withIndicator([panel('INDICATORS', 0), panel('WAREHOUSE_DAY', 10)])
    mock.handler = dayHandler
    const { container } = renderPulse(WAREHOUSE_ACCESS)
    await screen.findByRole('heading', { level: 2, name: /^Almacén hoy/ })
    expect(paintedPanels(container)).toEqual(['INDICATORS', 'WAREHOUSE_DAY'])
    expect(container.querySelector('[data-pinned]')).toBeNull()
    expect(container.querySelector('.pulse-pin-wrap')).toHaveClass('pinned')
  })

  it('organizando: la .orgbar es la fija (ni la franja ni la fecha)', async () => {
    mock.handler = dayHandler
    const user = userEvent.setup()
    const { container } = renderPulse(WAREHOUSE_ACCESS)
    await user.click(await screen.findByRole('button', { name: 'Organizar mi Pulso' }))
    expect(screen.queryByText(/se queda fija/)).toBeNull()
    expect(screen.getByRole('button', { name: 'Ocultar Almacén hoy' })).toBeInTheDocument()
    expect(container.querySelector('[data-pinned]')).toBeNull()
    expect(container.querySelector('.pulse-pin-wrap')).not.toHaveClass('pinned')
  })
})
