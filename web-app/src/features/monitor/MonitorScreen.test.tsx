// Lote F-E3 — Monitoreo de ruta con API simulado: totales, lista y búsqueda, selección con ?trip, auto-refresco con temporizadores falsos y pausa con la
// pestaña oculta, apagar el seguimiento, error de red conservando los datos, el caso con ping y paradas completadas (aún sin forma de sembrarlo en el
// servidor), vacíos, filtros y permisos.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { callsTo, mock, problem, resetMock, stop, tripDetail, type Call } from '../dispatch/dispatchMock'
import type { MonitorTrip } from './monitorRules'
import MonitorScreen from './MonitorScreen'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('../dispatch/dispatchMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

const NOW = Date.parse('2026-10-12T15:00:00Z') // 11:00 a. m. en Puerto Rico
const DAY = '2026-10-12'
const ALL = ['trips.view', 'trips.dispatch', 'dispatch.view', 'fleet.view']
const MODULES = ['LTL_GROUND', 'CATALOG']
const utc = (msFromNow: number) => new Date(NOW + msFromNow).toISOString().replace('Z', '') // como lo manda el API: sin zona
const ping = (secondsAgo: number, over: Record<string, unknown> = {}) => ({
  point: { lat: 18.31, lng: -66.1 },
  speedKmh: 42.4,
  headingDeg: 135,
  capturedAtUtc: utc(-secondsAgo * 1000),
  receivedAtUtc: utc(-secondsAgo * 1000 + 700),
  linkedToTrip: true,
  ...over,
})
const mtrip = (over: Record<string, unknown>): MonitorTrip => ({
  publicId: 't-1',
  code: '2026-0001',
  planDate: DAY,
  zoneCode: 'R-01',
  driverCode: 'D-01',
  driverName: 'Ana Pérez',
  vehicleCode: 'V-01',
  statusCode: 'IN_PROGRESS',
  status: 'En curso',
  totalStops: 4,
  completedStops: 0,
  failedStops: 0,
  pendingStops: 4,
  approximateStops: 0,
  nextEtaUtc: utc(40 * 60_000),
  plannedEndUtc: utc(4 * 3_600_000),
  overStopLimit: false,
  lastPing: undefined,
  ...over,
})

/** Lo que responde el monitor: los totales salen de TODAS las rutas del servidor (antes de la búsqueda); la lista, filtrada. */
const server = {
  trips: [] as MonitorTrip[],
  fail: false,
}
function monitorResponse(call: Call): unknown {
  if (call.method !== 'GET' || call.url.pathname !== '/api/v1/trips/monitor') return undefined
  if (server.fail) return problem(500, 'Error interno.', 'internal')
  const q = call.url.searchParams
  let all = server.trips
  if (q.get('includeCompleted') !== 'true') all = all.filter((x) => x.statusCode !== 'COMPLETED')
  if (q.get('dispatchZoneId')) all = all.filter((x) => String(x.zoneCode === 'R-01' ? 1 : 2) === q.get('dispatchZoneId'))
  const sum = (k: 'totalStops' | 'completedStops' | 'failedStops' | 'pendingStops') => all.reduce((s, x) => s + (x[k] ?? 0), 0)
  const search = (q.get('search') ?? '').toLowerCase()
  const shown = search ? all.filter((x) => [x.code, x.driverName, x.zoneCode].join(' ').toLowerCase().includes(search)) : all
  return {
    date: q.get('date'),
    totals: {
      trips: all.length,
      totalStops: sum('totalStops'),
      completedStops: sum('completedStops'),
      failedStops: sum('failedStops'),
      pendingStops: sum('pendingStops'),
      overStopLimitTrips: all.filter((x) => x.overStopLimit).length,
      tripsWithoutPing: all.filter((x) => !x.lastPing).length,
    },
    trips: shown,
  }
}

function Where() {
  const loc = useLocation()
  return <output data-testid="where">{loc.pathname + loc.search}</output>
}

function wrap(options: { permissions?: string[]; modules?: string[]; url?: string } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[options.url ?? '/ops/monitor']}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={options.permissions ?? ALL} modules={options.modules ?? MODULES}>
          <Routes>
            <Route path="/ops/monitor" element={<MonitorScreen />} />
            <Route path="/ops/dispatch" element={<div>Sala de despacho</div>} />
          </Routes>
          <Where />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const T1 = () =>
  tripDetail({
    publicId: 't-1',
    code: '2026-0001',
    statusCode: 'IN_PROGRESS',
    status: 'En curso',
    isEditable: false,
    stops: [
      stop(1, 1, 'OT-0001', { point: { lat: 18.4, lng: -66.15 }, geocodeAccuracyCode: 'MANUAL', statusCode: 'PENDING', status: 'Pendiente', windowStartUtc: utc(0), windowEndUtc: utc(3_600_000) }),
      stop(2, 2, 'OT-0002', { point: { lat: 18.3, lng: -66.05 }, geocodeAccuracyCode: 'ZIP_CENTROID', isApproximate: true, statusCode: 'PENDING', status: 'Pendiente' }),
      stop(3, 3, 'OT-0003', { statusCode: 'PENDING', status: 'Pendiente' }),
    ],
  })
const T2 = () => tripDetail({ publicId: 't-2', code: '2026-0002', zoneCode: 'R-02', driverCode: 'D-02', driverName: 'Luis Soto', statusCode: 'DISPATCHED', status: 'Despachado', isEditable: false, stops: [stop(5, 1, 'OT-0005', { point: { lat: 18.2, lng: -66.0 }, geocodeAccuracyCode: 'EXACT' })] })

function seed() {
  server.fail = false
  server.trips = [
    mtrip({ publicId: 't-1', totalStops: 3, pendingStops: 3, approximateStops: 1, lastPing: ping(12) }),
    mtrip({ publicId: 't-2', code: '2026-0002', zoneCode: 'R-02', driverCode: 'D-02', driverName: 'Luis Soto', statusCode: 'DISPATCHED', status: 'Despachado', totalStops: 4, completedStops: 2, failedStops: 0, pendingStops: 2, nextEtaUtc: utc(10 * 60_000), overStopLimit: true, lastPing: undefined }),
  ]
  resetMock([T1(), T2()])
  mock.override = monitorResponse
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(NOW)
  window.localStorage.clear()
  seed()
})
afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' })
})

const monitorCalls = () => callsTo('GET', '/api/v1/trips/monitor')
const tick = (ms: number) => act(async () => { await vi.advanceTimersByTimeAsync(ms) })
const ready = async () => {
  await screen.findByRole('list', { name: 'Rutas activas' })
  await screen.findByRole('list', { name: 'Paradas de la ruta' })
}
const river = () => screen.getByRole('group', { name: 'Totales de las rutas' })
const card = (code: string) => screen.getByRole('button', { name: new RegExp(code) })
function hideTab(hidden: boolean) {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => (hidden ? 'hidden' : 'visible') })
  fireEvent(document, new Event('visibilitychange'))
}

describe('totales, lista y ruta elegida', () => {
  it('el río trae los totales REALES del servidor y las tarjetas el avance, el estatus y el último ping', async () => {
    wrap()
    await ready()
    const r = within(river())
    expect(r.getByText('Rutas activas').closest('.node')).toHaveTextContent('2')
    expect(r.getByText('Paradas restantes').closest('.node')).toHaveTextContent('5') // 3 + 2
    expect(r.getByText('Progreso promedio').closest('.node')).toHaveTextContent('29%') // 2 de 7 paradas atendidas
    expect(r.getByText('Progreso promedio').closest('.node')).toHaveTextContent('2 de 7 paradas atendidas')
    const eta = r.getByText('Próxima en completar').closest('.node')!
    expect(eta).toHaveTextContent('2026-0002') // la llegada más próxima es la de la ruta 2 (en 10 min)
    expect(eta).toHaveTextContent('11:10')
    expect(r.getByText('Sin ping').closest('.node')).toHaveTextContent('1')
    expect(r.getByText('Sobre el máximo de paradas').closest('.node')).toHaveTextContent('1')

    const c1 = card('2026-0001')
    expect(c1).toHaveTextContent('D-01')
    expect(c1).toHaveTextContent('R-01')
    expect(c1).toHaveTextContent('0%')
    expect(c1).toHaveTextContent('En curso')
    expect(c1).toHaveTextContent('Ana Pérez · Último ping hace 12 s')
    const c2 = card('2026-0002')
    expect(c2).toHaveTextContent('50%')
    expect(c2).toHaveTextContent('Despachado')
    expect(c2).toHaveTextContent('Luis Soto · Sin ping')
    expect(within(c2).getByRole('img', { name: 'Sobrepasa el máximo de paradas' })).toBeInTheDocument()
    expect(within(c1).queryByRole('img', { name: 'Sobrepasa el máximo de paradas' })).toBeNull()
    expect(within(c2).getByRole('progressbar', { name: 'Avance de la ruta 2026-0002' })).toHaveAttribute('aria-valuenow', '50')
    // sin includeCompleted explícito el servidor lo incluye: la pantalla manda false
    expect(monitorCalls()[0].url.searchParams.get('includeCompleted')).toBe('false')
    expect(monitorCalls()[0].url.searchParams.get('date')).toBe(DAY)
  })

  it('la primera ruta queda elegida: plano con sus pines, «Sin ubicación», paradas con su estatus y avisos', async () => {
    wrap()
    await ready()
    const plane = screen.getByRole('group', { name: 'Plano de la ruta 2026-0001' })
    expect(within(plane).getByRole('button', { name: /Parada 1 · Consignatario OT-0001/ })).toBeInTheDocument()
    expect(within(plane).getByRole('button', { name: /Parada 2/ })).toBeInTheDocument()
    expect(within(plane).queryByRole('button', { name: /Parada 3/ })).toBeNull()
    expect(screen.getByText('Sin ubicación: 1')).toBeInTheDocument()
    const list = screen.getByRole('list', { name: 'Paradas de la ruta' })
    const rows = within(list).getAllByRole('listitem')
    expect(rows).toHaveLength(3)
    expect(rows[0]).toHaveTextContent('Consignatario OT-0001')
    expect(rows[0]).toHaveTextContent('Pendiente')
    expect(rows[0]).toHaveTextContent(/Ventana 11:00.*12:00/) // 15:00–16:00 UTC en Puerto Rico
    expect(rows[1]).toHaveTextContent('Bayamón')
    expect(rows[1]).toHaveTextContent('Llegada estimada 9:00')
    expect(screen.getByText('Sin pin exacto: 1')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /2026-0001 · Ana Pérez/ })).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/trips/t-1').length).toBeGreaterThan(0)
  })

  it('?trip= elige la ruta; elegir otra tarjeta cambia la dirección y pide su detalle', async () => {
    wrap({ url: '/ops/monitor?trip=t-2' })
    await screen.findByRole('group', { name: 'Plano de la ruta 2026-0002' })
    expect(card('2026-0002')).toHaveAttribute('aria-current', 'true')
    fireEvent.click(card('2026-0001'))
    await screen.findByRole('group', { name: 'Plano de la ruta 2026-0001' })
    expect(screen.getByTestId('where')).toHaveTextContent('/ops/monitor?trip=t-1')
    expect(callsTo('GET', '/api/v1/trips/t-1').length).toBeGreaterThan(0)
  })

  it('un ?trip= que ya no está en la lista cae a la primera ruta', async () => {
    wrap({ url: '/ops/monitor?trip=t-9' })
    await screen.findByRole('group', { name: 'Plano de la ruta 2026-0001' })
    expect(card('2026-0001')).toHaveAttribute('aria-current', 'true')
  })

  it('elegir una parada de la lista marca su pin y al revés', async () => {
    wrap()
    await ready()
    const plane = screen.getByRole('group', { name: 'Plano de la ruta 2026-0001' })
    fireEvent.click(screen.getByRole('button', { name: 'Parada 2: Consignatario OT-0002' }))
    expect(within(plane).getByRole('button', { name: /Parada 2/ })).toHaveAttribute('aria-pressed', 'true')
    fireEvent.click(within(plane).getByRole('button', { name: /Parada 1/ }))
    expect(screen.getByRole('button', { name: 'Parada 1: Consignatario OT-0001' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Parada 2: Consignatario OT-0002' })).toHaveAttribute('aria-pressed', 'false')
  })
})

describe('búsqueda, zona, completadas y fecha (todo al servidor)', () => {
  it('buscar espera 300 ms, manda search y los totales del río NO cambian', async () => {
    wrap()
    await ready()
    const before = monitorCalls().length
    fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar ruta, chofer o zona…' }), { target: { value: 'luis' } })
    await tick(250)
    expect(monitorCalls()).toHaveLength(before) // todavía no
    await tick(100)
    await waitFor(() => expect(monitorCalls().at(-1)!.url.searchParams.get('search')).toBe('luis'))
    await waitFor(() => expect(screen.queryByRole('button', { name: /2026-0001/ })).toBeNull())
    expect(card('2026-0002')).toBeInTheDocument()
    expect(within(river()).getByText('Rutas activas').closest('.node')).toHaveTextContent('2') // el total sigue en 2
    expect(within(river()).getByText('Paradas restantes').closest('.node')).toHaveTextContent('5')
  })

  it('una búsqueda sin resultados lo dice sin culpar a la fecha', async () => {
    wrap()
    await ready()
    fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar ruta, chofer o zona…' }), { target: { value: 'zzz' } })
    await tick(400)
    expect(await screen.findByText('Ninguna ruta coincide con la búsqueda.')).toBeInTheDocument()
    expect(screen.queryByText('No hay rutas para esta fecha')).toBeNull()
  })

  it('«Incluir completadas» pide includeCompleted=true y el río pasa a «Rutas (con completadas)»', async () => {
    server.trips.push(mtrip({ publicId: 't-3', code: '2026-0003', statusCode: 'COMPLETED', status: 'Completada', totalStops: 2, completedStops: 2, pendingStops: 0, nextEtaUtc: null }))
    wrap()
    await ready()
    expect(screen.queryByRole('button', { name: /2026-0003/ })).toBeNull()
    fireEvent.click(screen.getByRole('switch', { name: 'Incluir completadas' }))
    await waitFor(() => expect(monitorCalls().at(-1)!.url.searchParams.get('includeCompleted')).toBe('true'))
    expect(await screen.findByRole('button', { name: /2026-0003/ })).toHaveTextContent('100%')
    expect(within(river()).getByText('Rutas (con completadas)').closest('.node')).toHaveTextContent('3')
  })

  it('el filtro de zona manda dispatchZoneId; sin elegir no manda nada', async () => {
    wrap()
    await ready()
    await screen.findByRole('option', { name: 'R-02 · Caguas' })
    expect(monitorCalls()[0].url.searchParams.has('dispatchZoneId')).toBe(false)
    fireEvent.change(screen.getByRole('combobox', { name: 'Zona' }), { target: { value: '2' } })
    await waitFor(() => expect(monitorCalls().at(-1)!.url.searchParams.get('dispatchZoneId')).toBe('2'))
    await waitFor(() => expect(screen.queryByRole('button', { name: /2026-0001/ })).toBeNull())
  })

  it('cambiar la fecha pide esa fecha y limpia la ruta elegida; «Hoy» vuelve', async () => {
    wrap({ url: '/ops/monitor?trip=t-2' })
    await ready()
    fireEvent.change(screen.getByLabelText('Fecha de las rutas'), { target: { value: '2026-10-14' } })
    await waitFor(() => expect(monitorCalls().at(-1)!.url.searchParams.get('date')).toBe('2026-10-14'))
    expect(screen.getByTestId('where')).toHaveTextContent('/ops/monitor?date=2026-10-14')
    fireEvent.click(screen.getByRole('button', { name: 'Hoy' }))
    await waitFor(() => expect(monitorCalls().at(-1)!.url.searchParams.get('date')).toBe(DAY))
    expect(screen.getByTestId('where')).toHaveTextContent(/^\/ops\/monitor$/)
  })
})

describe('refresco', () => {
  it('«Actualizar» vuelve a pedir el monitor y el detalle y dice «Actualizado hace N s»', async () => {
    wrap()
    await ready()
    expect(await screen.findByText('Actualizado hace 0 s')).toBeInTheDocument()
    await tick(5000)
    expect(screen.getByText('Actualizado hace 5 s')).toBeInTheDocument()
    const m = monitorCalls().length
    const d = callsTo('GET', '/api/v1/trips/t-1').length
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar' }))
    await waitFor(() => expect(monitorCalls().length).toBe(m + 1))
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips/t-1').length).toBe(d + 1))
    await waitFor(() => expect(screen.getByText('Actualizado hace 0 s')).toBeInTheDocument())
  })

  it('cada 30 s consulta el monitor y el detalle de la ruta elegida mientras la pestaña se ve', async () => {
    wrap()
    await ready()
    const m = monitorCalls().length
    const d = callsTo('GET', '/api/v1/trips/t-1').length
    await tick(29_000)
    expect(monitorCalls()).toHaveLength(m)
    await tick(1500)
    await waitFor(() => expect(monitorCalls().length).toBe(m + 1))
    expect(callsTo('GET', '/api/v1/trips/t-1').length).toBe(d + 1)
    await tick(30_000)
    await waitFor(() => expect(monitorCalls().length).toBe(m + 2))
  })

  it('con la pestaña oculta no consulta; al volver, refresca al instante si pasó más de 30 s', async () => {
    wrap()
    await ready()
    const m = monitorCalls().length
    act(() => hideTab(true))
    await tick(95_000)
    expect(monitorCalls()).toHaveLength(m)
    act(() => hideTab(false))
    await waitFor(() => expect(monitorCalls().length).toBe(m + 1))
  })

  it('al apagar «Siguiendo en vivo» no hay sondeo, se recuerda y «Actualizar» sigue funcionando', async () => {
    wrap()
    await ready()
    const follow = screen.getByRole('switch', { name: 'Siguiendo en vivo' })
    expect(follow).toBeChecked()
    fireEvent.click(follow)
    expect(follow).not.toBeChecked()
    expect(window.localStorage.getItem('teikem.monitor.follow')).toBe('off')
    const m = monitorCalls().length
    await tick(95_000)
    expect(monitorCalls()).toHaveLength(m)
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar' }))
    await waitFor(() => expect(monitorCalls().length).toBe(m + 1))
    fireEvent.click(follow)
    expect(window.localStorage.getItem('teikem.monitor.follow')).toBe('on')
  })

  it('arranca apagado si así quedó guardado', async () => {
    window.localStorage.setItem('teikem.monitor.follow', 'off')
    wrap()
    await ready()
    expect(screen.getByRole('switch', { name: 'Siguiendo en vivo' })).not.toBeChecked()
    const m = monitorCalls().length
    await tick(65_000)
    expect(monitorCalls()).toHaveLength(m)
  })

  it('si falla el refresco conserva lo último, avisa con la hora y se recupera cuando el servidor responde', async () => {
    wrap()
    await ready()
    server.fail = true
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar' }))
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('No se pudo actualizar. Se muestra lo último que se recibió, a las 11:00')
    // los datos siguen a la vista
    expect(card('2026-0001')).toBeInTheDocument()
    expect(within(river()).getByText('Rutas activas').closest('.node')).toHaveTextContent('2')
    expect(screen.getByRole('group', { name: 'Plano de la ruta 2026-0001' })).toBeInTheDocument()
    server.fail = false
    await tick(31_000)
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
  })

  it('si la primera carga falla no hay datos que conservar: lo dice y deja «Reintentar»', async () => {
    server.fail = true
    wrap()
    expect(await screen.findByText('No se pudieron cargar las rutas.')).toBeInTheDocument()
    server.fail = false
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByRole('button', { name: /2026-0001/ })).toBeInTheDocument()
  })
})

describe('telemetría y estado en vivo', () => {
  it('sin ping de nadie: insignia neutra «Sin señal de choferes», velocidad y rumbo en «—», «Sin ping» y la explicación', async () => {
    server.trips = server.trips.map((x) => ({ ...x, lastPing: undefined }))
    wrap()
    await ready()
    expect(screen.getByText('Sin señal de choferes')).toBeInTheDocument()
    expect(screen.queryByText('EN VIVO')).toBeNull()
    const speed = screen.getByText('Velocidad').closest('.telemcell')!
    const heading = screen.getByText('Rumbo').closest('.telemcell')!
    expect(speed).toHaveTextContent('—')
    expect(heading).toHaveTextContent('—')
    expect(screen.getByText(/Todavía no hay ubicación del chofer para esta ruta/)).toBeInTheDocument()
    expect(screen.getAllByText('Sin ping').length).toBeGreaterThan(0)
    // no hay señal GPS ni batería: no existen en el servidor, no se inventan
    expect(screen.queryByText('Señal GPS')).toBeNull()
    expect(screen.queryByText(/Batería/)).toBeNull()
    // sin punto vivo en el plano
    expect(screen.queryByRole('img', { name: /Posición del chofer/ })).toBeNull()
  })

  it('con ping reciente: EN VIVO, velocidad, rumbo con punto cardinal, último ping y punto vivo en el plano', async () => {
    wrap()
    await ready()
    expect(screen.getByText('EN VIVO')).toBeInTheDocument()
    expect(screen.getByText('Velocidad').closest('.telemcell')).toHaveTextContent('42 km/h')
    expect(screen.getByText('Rumbo').closest('.telemcell')).toHaveTextContent('135° SE')
    expect(screen.getAllByText('Último ping hace 12 s').length).toBeGreaterThan(0)
    expect(screen.getByRole('img', { name: 'Posición del chofer (hace 12 s)' })).toBeInTheDocument()
    expect(screen.getByText('Posición del chofer')).toBeInTheDocument() // leyenda del plano
    expect(screen.queryByText('Sin señal reciente')).toBeNull()
  })

  it('el ping envejece con el reloj: a los 2 minutos pasa a «Sin señal reciente» y la insignia se apaga', async () => {
    wrap()
    await ready()
    expect(screen.getByText('EN VIVO')).toBeInTheDocument()
    // el servidor sigue mandando el mismo ping (capturado a las 10:59:48): con el reloj corriendo, ya es viejo
    await tick(115_000)
    await waitFor(() => expect(screen.getByText('Sin señal reciente')).toBeInTheDocument())
    expect(screen.queryByText('EN VIVO')).toBeNull()
    expect(screen.getByText('Sin señal de choferes')).toBeInTheDocument()
    expect(screen.getAllByText(/Último ping hace 2 min/).length).toBeGreaterThan(0)
  })

  it('un ping que no viene marcado con la ruta lo avisa', async () => {
    server.trips[0] = mtrip({ publicId: 't-1', totalStops: 3, pendingStops: 3, lastPing: ping(20, { linkedToTrip: false }) })
    wrap()
    await ready()
    expect(screen.getByText('El último ping es del chofer, pero no viene marcado con esta ruta.')).toBeInTheDocument()
  })

  it('ruta con paradas completadas, una fallida y una en camino: estatus de la lista, pines por estado y avance', async () => {
    const done = tripDetail({
      publicId: 't-1',
      code: '2026-0001',
      statusCode: 'IN_PROGRESS',
      status: 'En curso',
      isEditable: false,
      stops: [
        stop(1, 1, 'OT-0001', { point: { lat: 18.4, lng: -66.15 }, geocodeAccuracyCode: 'EXACT', statusCode: 'COMPLETED', status: 'Completada', actualArrivalUtc: utc(-3_000_000) }),
        stop(2, 2, 'OT-0002', { point: { lat: 18.35, lng: -66.1 }, geocodeAccuracyCode: 'EXACT', statusCode: 'FAILED', status: 'Fallida' }),
        stop(3, 3, 'OT-0003', { point: { lat: 18.3, lng: -66.05 }, geocodeAccuracyCode: 'EXACT', statusCode: 'ON_THE_WAY', status: 'En camino' }),
        stop(4, 4, 'OT-0004', { point: { lat: 18.25, lng: -66.0 }, geocodeAccuracyCode: 'EXACT', statusCode: 'PENDING', status: 'Pendiente' }),
      ],
    })
    resetMock([done, T2()])
    mock.override = monitorResponse
    server.trips[0] = mtrip({ publicId: 't-1', totalStops: 4, completedStops: 1, failedStops: 1, pendingStops: 2, lastPing: ping(8, { speedKmh: 0, headingDeg: 350 }) })
    wrap()
    await ready()
    const rows = within(screen.getByRole('list', { name: 'Paradas de la ruta' })).getAllByRole('listitem')
    expect(rows.map((r) => r.getAttribute('data-state'))).toEqual(['done', 'failed', 'current', 'pending'])
    expect(rows[0]).toHaveTextContent('Completada')
    expect(rows[0]).toHaveTextContent('Llegó a las 10:10')
    expect(rows[1]).toHaveTextContent('Fallida')
    expect(rows[2]).toHaveTextContent('En camino')
    expect(rows[3]).toHaveTextContent('Pendiente')
    const plane = screen.getByRole('group', { name: 'Plano de la ruta 2026-0001' })
    const states = within(plane).getAllByRole('button', { name: /^Parada/ }).map((b) => b.getAttribute('data-state'))
    expect(states).toEqual(['done', 'failed', 'current', 'pending'])
    expect(card('2026-0001')).toHaveTextContent('50%')
    expect(screen.getByText('Velocidad').closest('.telemcell')).toHaveTextContent('0 km/h')
    expect(screen.getByText('Rumbo').closest('.telemcell')).toHaveTextContent('350° N')
    expect(screen.getByText('Entregada')).toBeInTheDocument() // leyenda del plano (estado entregada)
  })

  it('una ruta sin ninguna coordenada usa el mensaje del plano y lista todas en «Sin ubicación»', async () => {
    resetMock([tripDetail({ publicId: 't-1', code: '2026-0001', statusCode: 'IN_PROGRESS', status: 'En curso', stops: [stop(1, 1, 'OT-0001'), stop(2, 2, 'OT-0002')] }), T2()])
    mock.override = monitorResponse
    server.trips[0] = mtrip({ publicId: 't-1', totalStops: 2, pendingStops: 2, lastPing: undefined }) // con un ping habría un punto vivo y el plano no estaría vacío
    wrap()
    await ready()
    expect(screen.getByText('Ninguna parada tiene coordenadas todavía')).toBeInTheDocument()
    expect(screen.getByText(/Quien planifica puede ponerlos desde la Sala de despacho/)).toBeInTheDocument()
    expect(screen.getByText('Sin ubicación: 2')).toBeInTheDocument()
  })

  it('«Sobrepasa el máximo de paradas» aparece en los avisos de la ruta elegida', async () => {
    wrap({ url: '/ops/monitor?trip=t-2' })
    await screen.findByRole('group', { name: 'Plano de la ruta 2026-0002' })
    expect(screen.getByRole('alert')).toHaveTextContent('Sobrepasa el máximo de paradas')
  })
})

describe('vacíos y permisos', () => {
  it('sin rutas en la fecha: mensaje honesto y enlace a la Sala de despacho (con dispatch.view)', async () => {
    server.trips = []
    resetMock([])
    mock.override = monitorResponse
    wrap()
    expect(await screen.findByText('No hay rutas para esta fecha')).toBeInTheDocument()
    expect(screen.getByText('Seleccione una ruta de la lista')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Abrir la Sala de despacho' })).toHaveAttribute('href', `/ops/dispatch?date=${DAY}`)
    expect(within(river()).getByText('Rutas activas').closest('.node')).toHaveTextContent('0')
    expect(screen.getByText('Sin señal de choferes')).toBeInTheDocument()
  })

  it('sin rutas y sin dispatch.view: no hay enlace a la Sala de despacho', async () => {
    server.trips = []
    resetMock([])
    mock.override = monitorResponse
    wrap({ permissions: ['trips.view', 'fleet.view'] })
    expect(await screen.findByText('No hay rutas para esta fecha')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Abrir la Sala de despacho' })).toBeNull()
  })

  it('«Abrir en Sala de despacho» lleva la fecha y la ruta, solo con dispatch.view', async () => {
    const view = wrap({ url: '/ops/monitor?trip=t-2' })
    await screen.findByRole('group', { name: 'Plano de la ruta 2026-0002' })
    expect(screen.getByRole('link', { name: 'Abrir en Sala de despacho' })).toHaveAttribute('href', `/ops/dispatch?date=${DAY}&trip=t-2`)
    view.unmount()
    resetMock([T1(), T2()])
    mock.override = monitorResponse
    wrap({ permissions: ['trips.view', 'fleet.view'], url: '/ops/monitor?trip=t-2' })
    await screen.findByRole('group', { name: 'Plano de la ruta 2026-0002' })
    expect(screen.queryByRole('link', { name: 'Abrir en Sala de despacho' })).toBeNull()
  })

  it('es de solo lectura: no hay más botones de acción que Actualizar, Hoy y los interruptores; ni datos de contacto del chofer', async () => {
    wrap()
    await ready()
    const names = screen.getAllByRole('button').map((b) => b.getAttribute('aria-label') ?? b.textContent ?? '')
    for (const n of names) expect(n).not.toMatch(/Despachar|Iniciar|Cerrar ruta|Eliminar|Optimizar|Poner pin|Mover pin|Llamar|Teléfono/)
    expect(document.body.textContent).not.toMatch(/\(\d{3}\) \d{3}-\d{4}/)
    expect(callsTo('POST', '/api/v1/trips/t-1/start')).toHaveLength(0)
  })

  it('sin fleet.view no ofrece el filtro de zona, lo explica y no consulta las zonas', async () => {
    wrap({ permissions: ['trips.view', 'trips.dispatch', 'dispatch.view'] })
    await ready()
    expect(screen.queryByRole('combobox', { name: 'Zona' })).toBeNull()
    expect(screen.getByText('Su usuario no puede consultar las zonas, así que no se ofrece el filtro por zona.')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/dispatch-zones')).toHaveLength(0)
  })

  it('sin trips.view no consulta nada y lo dice', async () => {
    wrap({ permissions: ['fleet.view'] })
    expect(await screen.findByText('Su usuario no tiene acceso para consultar las rutas. Pídalo a su administrador.')).toBeInTheDocument()
    expect(monitorCalls()).toHaveLength(0)
  })

  it('en inglés se lee en inglés', async () => {
    setLang('en')
    try {
      wrap()
      await screen.findByRole('list', { name: 'Active routes' })
      expect(screen.getByRole('heading', { level: 1, name: 'Route monitoring' })).toBeInTheDocument()
      expect(screen.getAllByText('Last ping 12 s ago').length).toBeGreaterThan(0)
      expect(screen.getByText('LIVE')).toBeInTheDocument()
    } finally {
      setLang('es')
    }
  })
})
