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
    expect(formatYmd('2026-09-15', 'en')).toBe('9/15/2026')
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
    // subtítulo: el rango (etiqueta del catálogo) o, sin rango, el módulo
    expect(await screen.findByText('Últimos 90 días')).toBeInTheDocument()
    expect(within(links[0]).getByText('Almacén')).toBeInTheDocument()
    expect(links[0].closest('.node')).toHaveClass('flow')
    expect(links[1].closest('.node')).toHaveClass('money')
  })

  it('sin analytics.view el nodo no es enlace (Indicadores le daría "Sin permiso") y no hay "Rango"', async () => {
    mock.handler = () => pulse({ indicators: [{ id: 1, name: 'Ventas', value: 10, isVisible: true }] })
    renderPulse({ permissions: ['pulse.indicators'], modules: ['ANALYTICS'] })
    expect(await screen.findByText('Ventas')).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Cambiar mi rango/ })).not.toBeInTheDocument()
  })

  it('gráficos: con ≤ 3 puntos, lista "etiqueta · valor"; con más, el gráfico; grilla de una columna a 360 px', async () => {
    mock.handler = () =>
      pulse({
        charts: [
          { id: 3, name: 'Pocos', chartType: 'BAR', isMoney: true, isVisible: true, sortOrder: 0, points: [{ label: 'Lun', value: 3 }, { label: 'Mar', value: 4.5 }] },
          { id: 4, name: 'Muchos', chartType: 'DONUT', isVisible: true, sortOrder: 10, points: [1, 2, 3, 4].map((v) => ({ label: `P${v}`, value: v })) },
          { id: 5, name: 'Vacío', chartType: 'LINE', isVisible: true, sortOrder: 20, points: [] },
        ],
      })
    renderPulse()
    const list = await screen.findByRole('list', { name: 'Pocos' })
    expect(within(list).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['Lun · $3.00', 'Mar · $4.50'])
    expect(screen.queryByRole('list', { name: 'Muchos' })).not.toBeInTheDocument()
    expect(screen.getByText('Este gráfico no tiene datos en el rango configurado.')).toBeInTheDocument()
    const grid = screen.getByRole('heading', { name: 'Tus gráficos' }).nextElementSibling as HTMLElement
    expect(grid).toHaveClass('pulse-charts')
    expect(grid.querySelectorAll('.pulse-chart')).toHaveLength(3)
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
  if (path === '/api/v1/cycle-counts') return [{ id: 1 }, { id: 2 }]
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
    expect(mock.urls).toContain('/api/v1/receipts?status=OPEN&take=1')
    expect(mock.urls).toContain('/api/v1/warehouse-tasks?includeClosed=false&take=1')
    expect(mock.urls).toContain('/api/v1/warehouse-tasks?includeClosed=false&take=1&types=PUTAWAY')
    expect(mock.urls).toContain('/api/v1/cycle-counts?status=OPEN')
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
