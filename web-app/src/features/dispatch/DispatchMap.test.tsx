// Lote F-E2b — Sala de despacho: el plano de la ruta activa, la selección pin↔fila, reordenar arrastrando (con vuelta atrás y 409), el pin manual
// (errores, coma decimal, desde la fila, desde el plano y desde el aviso de pines aproximados) y los permisos / ruta no editable.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import DispatchScreen from './DispatchScreen'
import { callsMatching, callsTo, mock, problem, resetMock, stop, tripDetail, type Call } from './dispatchMock'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./dispatchMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

const ALL = ['trips.view', 'trips.plan', 'trips.optimize', 'trips.dispatch', 'trips.close', 'fleet.view', 'clients.read']
const MODULES = ['LTL_GROUND', 'CATALOG']

function wrap(permissions: string[] = ALL) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={['/ops/dispatch']}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={MODULES}>
          <Routes>
            <Route path="/ops/dispatch" element={<DispatchScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const P1 = { lat: 18.4, lng: -66.1 }
const P2 = { lat: 18.2, lng: -66.0 }
// tres paradas: 101 y 102 con punto (exacto y aproximado), 103 sin punto
const three = () => [
  stop(1, 1, 'OT-0101', { point: P1, geocodeAccuracyCode: 'EXACT' }),
  stop(2, 2, 'OT-0102', { point: P2, geocodeAccuracyCode: 'ZIP_CENTROID', isApproximate: true }),
  stop(3, 3, 'OT-0103', { isApproximate: true }),
]
const T = (over: Record<string, unknown> = {}) => tripDetail({ stops: three(), ...over })

const stopsList = () => screen.getByRole('list', { name: 'Órdenes en esta ruta' })
const rows = () => within(stopsList()).getAllByRole('listitem')
const order = () => rows().map((r) => (r.querySelector('.ref') as HTMLElement).textContent)
const plane = () => screen.getByRole('group', { name: 'Plano de la ruta 2026-0001' })
const grip = (n: string) => screen.getByRole('button', { name: `Arrastrar la orden ${n} para reordenar (o use las flechas ↑ ↓)` })
const ready = () => screen.findByRole('list', { name: 'Órdenes en esta ruta' })

beforeAll(() => setLang('es'))
beforeEach(() => resetMock([T()]))
afterEach(() => vi.restoreAllMocks())

/** Cada fila mide 40 px y la primera empieza en 0: la fila bajo un `clientY` es entera(clientY / 40). */
function fakeRowRects() {
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const li = this.closest('li.disp-stop')
    const i = li ? Array.from(li.parentElement?.children ?? []).indexOf(li) : 0
    const top = li && this === li ? i * 40 : 0
    const height = li && this === li ? 40 : 0
    return { x: 0, y: top, top, bottom: top + height, left: 0, right: 0, width: 0, height, toJSON: () => ({}) } as DOMRect
  })
}

function drag(handle: HTMLElement, fromY: number, toY: number, end = true) {
  fireEvent.pointerDown(handle, { pointerId: 1, pointerType: 'mouse', button: 0, clientY: fromY })
  fireEvent.pointerMove(handle, { pointerId: 1, pointerType: 'mouse', clientY: toY })
  if (end) fireEvent.pointerUp(handle, { pointerId: 1, pointerType: 'mouse', clientY: toY })
}

describe('plano de la ruta activa', () => {
  it('va dentro de «Ruta activa», arriba de «Órdenes en esta ruta», con un pin por parada con punto y sin ubicación listadas aparte', async () => {
    wrap()
    await ready()
    const panel = screen.getByRole('heading', { name: /Ruta activa/ }).closest('.panel')!
    expect(panel).toContainElement(plane())
    expect(plane().compareDocumentPosition(stopsList()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(within(plane()).getByRole('button', { name: 'Parada 1 · Consignatario OT-0101 · Bayamón' })).toBeInTheDocument()
    expect(within(plane()).getByRole('button', { name: 'Parada 2 · Consignatario OT-0102 · Bayamón' })).toBeInTheDocument()
    expect(within(plane()).queryByRole('button', { name: /Parada 3/ })).toBeNull()
    expect(screen.getByText('Sin ubicación: 1')).toBeInTheDocument()
    // la parada sin punto sigue en la lista, con su marca
    expect(order()).toEqual(['OT-0101', 'OT-0102', 'OT-0103'])
    expect(rows()[2]).toHaveTextContent('Sin pin')
    expect(rows()[1]).toHaveTextContent('Ubicación aproximada')
    expect(screen.getByText(/Plano esquemático/)).toBeInTheDocument()
    // sin conmutador 2D / Sat ni «tracking»: no existen
    expect(screen.queryByText(/^2D$/)).toBeNull()
    expect(screen.queryByText(/^Sat$/)).toBeNull()
  })

  it('sin ninguna coordenada: mensaje claro con lo que hay que hacer (poner pin) y todas las paradas en «Sin ubicación»', async () => {
    resetMock([tripDetail({ stops: [stop(1, 1, 'OT-0101'), stop(2, 2, 'OT-0102')] })])
    wrap()
    await ready()
    expect(within(screen.getByRole('heading', { name: /Ruta activa/ }).closest('.panel') as HTMLElement).getByText('Ninguna parada tiene coordenadas todavía')).toBeInTheDocument()
    expect(screen.getByText('Ponga un pin manual a cada parada con «Poner pin» para verlas aquí.')).toBeInTheDocument()
    expect(screen.getByText('Sin ubicación: 2')).toBeInTheDocument()
  })

  it('sin trips.plan el mensaje de vacío manda a pedirlo a quien planifica', async () => {
    resetMock([tripDetail({ stops: [stop(1, 1, 'OT-0101')] })])
    wrap(['trips.view'])
    await ready()
    expect(screen.getByText('Pida a quien planifica la ruta que ponga el pin manual de cada parada.')).toBeInTheDocument()
  })

  it('un pin y su fila se eligen mutuamente (aria-pressed, resalte y resumen con «Poner pin»/«Mover pin»)', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    const pin2 = within(plane()).getByRole('button', { name: /Parada 2/ })
    expect(pin2).toHaveAttribute('aria-pressed', 'false')
    await user.click(pin2)
    expect(within(plane()).getByRole('button', { name: /Parada 2/ })).toHaveAttribute('aria-pressed', 'true')
    expect(rows()[1]).toHaveClass('on')
    expect(within(rows()[1]).getByRole('button', { name: 'Elegir la orden OT-0102' })).toHaveAttribute('aria-pressed', 'true')
    const strip = screen.getByText(/Parada 2 · OT-0102/).closest('.disp-map-sel') as HTMLElement
    expect(strip).toHaveTextContent('Pin aproximado')
    expect(within(strip).getByRole('button', { name: 'Mover pin' })).toBeInTheDocument()
    // desde la lista
    await user.click(within(rows()[0]).getByRole('button', { name: 'Elegir la orden OT-0101' }))
    expect(rows()[0]).toHaveClass('on')
    expect(rows()[1]).not.toHaveClass('on')
    expect(within(plane()).getByRole('button', { name: /Parada 1/ })).toHaveAttribute('aria-pressed', 'true')
    expect(within(plane()).getByRole('button', { name: /Parada 2/ })).toHaveAttribute('aria-pressed', 'false')
    // una parada sin ubicación: se elige en su chip y el resumen ofrece «Poner pin»
    await user.click(screen.getByRole('button', { name: /Parada 3/ }))
    expect(rows()[2]).toHaveClass('on')
    expect(screen.getByText(/Parada 3 · OT-0103/).closest('.disp-map-sel')).toHaveTextContent('Sin pin')
    expect(screen.getAllByRole('button', { name: 'Poner pin' }).length).toBeGreaterThan(0)
  })
})

describe('reordenar arrastrando', () => {
  it('soltar la primera sobre la tercera manda la secuencia completa con la versión vigente y la lista cambia al instante (optimista)', async () => {
    fakeRowRects()
    wrap()
    await ready()
    let release: (r: undefined) => void = () => undefined
    mock.override = (c: Call) => (c.method === 'PUT' ? new Promise((resolve) => { release = resolve as (r: undefined) => void }) : undefined)
    drag(grip('OT-0101'), 20, 100)
    await waitFor(() => expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')[0].body).toEqual({ routeStopIds: [2, 3, 1], rowVersion: 'rv-1' })
    // mientras viaja la petición ya se ve el orden nuevo, también en el plano (los pines siguen la secuencia mostrada)
    expect(order()).toEqual(['OT-0102', 'OT-0103', 'OT-0101'])
    expect(within(plane()).getByRole('button', { name: 'Parada 3 · Consignatario OT-0101 · Bayamón' })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Arrastrar la orden OT-0101 para reordenar (o use las flechas ↑ ↓)' })[0]).toBeDisabled()
    release(undefined) // responde el simulador
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Arrastrar la orden OT-0101 para reordenar (o use las flechas ↑ ↓)' })[0]).toBeEnabled())
    expect(order()).toEqual(['OT-0102', 'OT-0103', 'OT-0101'])
  })

  it('arrastrar hacia arriba: la última al principio', async () => {
    fakeRowRects()
    wrap()
    await ready()
    drag(grip('OT-0103'), 100, 5)
    await waitFor(() => expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')[0].body).toEqual({ routeStopIds: [3, 1, 2], rowVersion: 'rv-1' })
    await waitFor(() => expect(order()).toEqual(['OT-0103', 'OT-0101', 'OT-0102']))
  })

  it('mientras se arrastra se marca la fila de origen y la de destino; soltar en el mismo lugar no manda nada', async () => {
    fakeRowRects()
    wrap()
    await ready()
    drag(grip('OT-0101'), 20, 100, false)
    expect(rows()[0]).toHaveClass('drag')
    expect(rows()[2]).toHaveClass('over-down')
    fireEvent.pointerUp(grip('OT-0101'), { pointerId: 1, pointerType: 'mouse', clientY: 100 })
    await waitFor(() => expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')).toHaveLength(1))
    // en el mismo lugar
    mock.calls = []
    drag(grip('OT-0102'), 60, 62)
    expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')).toHaveLength(0)
  })

  it('cancelar el gesto (pointercancel) no manda nada y limpia las marcas', async () => {
    fakeRowRects()
    wrap()
    await ready()
    drag(grip('OT-0101'), 20, 100, false)
    fireEvent.pointerCancel(grip('OT-0101'), { pointerId: 1 })
    expect(rows()[0]).not.toHaveClass('drag')
    expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')).toHaveLength(0)
  })

  it('si el servidor lo niega, la lista vuelve al orden anterior y se muestra su texto exacto', async () => {
    fakeRowRects()
    wrap()
    await ready()
    const msg = 'La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.'
    mock.override = (c: Call) => (c.method === 'PUT' ? problem(400, msg, 'validation', { routeStopIds: [msg] }) : undefined)
    drag(grip('OT-0101'), 20, 100)
    expect(await screen.findByRole('alert')).toHaveTextContent(msg)
    expect(order()).toEqual(['OT-0101', 'OT-0102', 'OT-0103'])
    expect(within(plane()).getByRole('button', { name: 'Parada 1 · Consignatario OT-0101 · Bayamón' })).toBeInTheDocument()
  })

  it('409 (otra persona cambió la ruta): el aviso del servidor, el orden anterior y la ruta se vuelve a pedir', async () => {
    fakeRowRects()
    wrap()
    await ready()
    const before = callsTo('GET', '/api/v1/trips/t-1').length
    mock.override = (c: Call) => (c.method === 'PUT' ? problem(409, 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', 'conflict') : undefined)
    drag(grip('OT-0102'), 60, 5)
    expect(await screen.findByRole('alert')).toHaveTextContent('El registro fue modificado por otro usuario; recargue e intente de nuevo.')
    expect(order()).toEqual(['OT-0101', 'OT-0102', 'OT-0103'])
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips/t-1').length).toBeGreaterThan(before))
  })

  it('con el teclado: ↓ en el asa baja la orden y ↑ la sube; en los extremos no hace nada', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    grip('OT-0101').focus()
    await user.keyboard('{ArrowUp}')
    expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')).toHaveLength(0)
    await user.keyboard('{ArrowDown}')
    await waitFor(() => expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')[0].body).toEqual({ routeStopIds: [2, 1, 3], rowVersion: 'rv-1' })
    await waitFor(() => expect(order()).toEqual(['OT-0102', 'OT-0101', 'OT-0103']))
  })

  it('los ▲ ▼ siguen ahí y usan el mismo camino (también actualizan el plano)', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    await user.click(screen.getByRole('button', { name: 'Subir la orden OT-0102' }))
    await waitFor(() => expect(order()).toEqual(['OT-0102', 'OT-0101', 'OT-0103']))
    expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')[0].body).toEqual({ routeStopIds: [2, 1, 3], rowVersion: 'rv-1' })
    expect(within(plane()).getByRole('button', { name: 'Parada 1 · Consignatario OT-0102 · Bayamón' })).toBeInTheDocument()
  })
})

describe('pin manual', () => {
  const open = async (user: ReturnType<typeof userEvent.setup>, number = 'OT-0103') => {
    await user.click(screen.getByRole('button', { name: `Poner pin a la orden ${number}` }))
    return screen.getByRole('dialog', { name: `Pin manual · ${number}` })
  }

  it('el diálogo valida en pantalla con los textos del servidor y no llama mientras haya errores', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    const dlg = await open(user)
    await user.click(within(dlg).getByRole('button', { name: 'Guardar pin' }))
    expect(within(dlg).getAllByText('Indique la latitud y la longitud.')).toHaveLength(2)
    await user.type(within(dlg).getByLabelText(/^Latitud/), '95')
    await user.type(within(dlg).getByLabelText(/^Longitud/), '-200')
    await user.click(within(dlg).getByRole('button', { name: 'Guardar pin' }))
    expect(within(dlg).getByText('La latitud debe estar entre -90 y 90.')).toBeInTheDocument()
    expect(within(dlg).getByText('La longitud debe estar entre -180 y 180.')).toBeInTheDocument()
    expect(callsMatching('PUT', /location$/)).toHaveLength(0)
  })

  it('guarda con coma decimal: manda números y la versión vigente, la parada pasa a «Pin manual» y aparece en el plano', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    const dlg = await open(user)
    await user.type(within(dlg).getByLabelText(/^Latitud/), '18,45')
    await user.type(within(dlg).getByLabelText(/^Longitud/), '-66,05')
    await user.click(within(dlg).getByRole('button', { name: 'Guardar pin' }))
    await waitFor(() => expect(callsMatching('PUT', /location$/)).toHaveLength(1))
    const call = callsMatching('PUT', /location$/)[0]
    expect(call.url.pathname).toBe('/api/v1/trips/t-1/stops/3/location')
    expect(call.body).toEqual({ lat: 18.45, lng: -66.05, rowVersion: 'rv-1' })
    expect(call.headers.get('Idempotency-Key')).toBeTruthy()
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(within(plane()).getByRole('button', { name: 'Parada 3 · Consignatario OT-0103 · Bayamón' })).toBeInTheDocument())
    expect(rows()[2]).toHaveTextContent('Pin manual')
    expect(screen.queryByText(/Sin ubicación/)).toBeNull()
    expect(screen.getByRole('button', { name: 'Mover el pin de la orden OT-0103' })).toBeInTheDocument()
  })

  it('un error del servidor sale dentro del diálogo (ruta no editable 422) y el diálogo no se cierra', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    const dlg = await open(user)
    await user.type(within(dlg).getByLabelText(/^Latitud/), '18.4')
    await user.type(within(dlg).getByLabelText(/^Longitud/), '-66.1')
    mock.override = (c: Call) => (c.method === 'PUT' ? problem(422, 'La ruta ya fue despachada y no se puede modificar.', 'status_rule') : undefined)
    await user.click(within(dlg).getByRole('button', { name: 'Guardar pin' }))
    expect(await within(dlg).findByRole('alert')).toHaveTextContent('La ruta ya fue despachada y no se puede modificar.')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('el error por campo del servidor (400) sale debajo del campo, y el 404 de «Parada no encontrada» arriba', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    const dlg = await open(user)
    await user.type(within(dlg).getByLabelText(/^Latitud/), '18.4')
    await user.type(within(dlg).getByLabelText(/^Longitud/), '-66.1')
    mock.override = (c: Call) => (c.method === 'PUT' ? problem(400, 'Datos no válidos.', 'validation', { lat: ['La latitud debe estar entre -90 y 90.'] }) : undefined)
    await user.click(within(dlg).getByRole('button', { name: 'Guardar pin' }))
    expect(await within(dlg).findByText('La latitud debe estar entre -90 y 90.')).toBeInTheDocument()
    mock.override = (c: Call) => (c.method === 'PUT' ? problem(404, 'Parada no encontrada en esta ruta.', 'not_found') : undefined)
    await user.click(within(dlg).getByRole('button', { name: 'Guardar pin' }))
    expect(await within(dlg).findByRole('alert')).toHaveTextContent('Parada no encontrada en esta ruta.')
  })

  it('«Mover pin» parte del pin actual de la parada', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    await user.click(screen.getByRole('button', { name: 'Mover el pin de la orden OT-0101' }))
    const dlg = screen.getByRole('dialog', { name: 'Pin manual · OT-0101' })
    expect(within(dlg).getByLabelText(/^Latitud/)).toHaveValue('18.4')
    expect(within(dlg).getByLabelText(/^Longitud/)).toHaveValue('-66.1')
  })

  it('desde el plano: elegir una parada y «Poner pin» en el resumen abre el mismo diálogo', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    await user.click(screen.getByRole('button', { name: /Parada 3/ }))
    const strip = screen.getByText(/Parada 3 · OT-0103/).closest('.disp-map-sel') as HTMLElement
    await user.click(within(strip).getByRole('button', { name: 'Poner pin' }))
    expect(screen.getByRole('dialog', { name: 'Pin manual · OT-0103' })).toBeInTheDocument()
  })

  it('con puntos de referencia, un clic en el plano del diálogo llena las coordenadas y avisa que es aproximado', async () => {
    const user = userEvent.setup()
    wrap()
    await ready()
    const dlg = await open(user)
    const picker = within(dlg).getByRole('group', { name: 'Plano de referencia para tomar una posición' })
    fireEvent.click(picker.querySelector('rect.rmap-bg')!, { clientX: 300, clientY: 100 })
    const lat = Number((within(dlg).getByLabelText(/^Latitud/) as HTMLInputElement).value)
    const lng = Number((within(dlg).getByLabelText(/^Longitud/) as HTMLInputElement).value)
    expect(lat).toBeGreaterThan(18.1)
    expect(lat).toBeLessThan(18.5)
    expect(lng).toBeGreaterThan(-66.2)
    expect(lng).toBeLessThan(-65.9)
    expect(within(dlg).getByText(/Posición aproximada tomada del plano/)).toBeInTheDocument()
    // lo tomado es lo que se manda
    await user.click(within(dlg).getByRole('button', { name: 'Guardar pin' }))
    await waitFor(() => expect(callsMatching('PUT', /location$/)).toHaveLength(1))
    expect(callsMatching('PUT', /location$/)[0].body).toMatchObject({ lat, lng })
  })

  it('sin ningún punto de referencia no se ofrece tomar la posición del plano (solo los campos)', async () => {
    const user = userEvent.setup()
    resetMock([tripDetail({ stops: [stop(1, 1, 'OT-0101'), stop(2, 2, 'OT-0102')] })])
    wrap()
    await ready()
    const dlg = await open(user, 'OT-0101')
    expect(within(dlg).queryByRole('group', { name: /Plano de referencia/ })).toBeNull()
    expect(within(dlg).getByLabelText(/^Latitud/)).toBeInTheDocument()
  })

  it('el aviso de pines aproximados ofrece «Poner pin manual» para la primera parada aproximada', async () => {
    const user = userEvent.setup()
    resetMock([T({ issues: [{ code: 'APPROXIMATE_PINS', message: '2 paradas tienen pin aproximado.', blocking: false }] })])
    wrap()
    await ready()
    const issues = screen.getByRole('list', { name: 'Avisos y bloqueantes' })
    await user.click(within(issues).getByRole('button', { name: 'Poner pin manual' }))
    expect(screen.getByRole('dialog', { name: 'Pin manual · OT-0102' })).toBeInTheDocument()
    expect(rows()[1]).toHaveClass('on')
  })
})

describe('permisos y ruta no editable', () => {
  it('sin trips.plan se ve el plano pero no hay asa, «Poner pin», ▲ ▼ ni quitar', async () => {
    wrap(['trips.view'])
    await ready()
    expect(plane()).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Arrastrar la orden/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Poner pin|Mover/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Subir la orden|Bajar la orden|Quitar la orden/ })).toBeNull()
    // elegir una parada sigue funcionando y el resumen no ofrece pin
    await userEvent.setup().click(screen.getByRole('button', { name: /Parada 3/ }))
    expect(screen.queryByRole('button', { name: 'Poner pin' })).toBeNull()
  })

  it('con el aviso de pines aproximados pero sin trips.plan no hay botón para poner pin', async () => {
    resetMock([T({ issues: [{ code: 'APPROXIMATE_PINS', message: '2 paradas tienen pin aproximado.', blocking: false }] })])
    wrap(['trips.view', 'trips.dispatch'])
    await ready()
    expect(screen.queryByRole('button', { name: 'Poner pin manual' })).toBeNull()
  })

  it('una ruta despachada muestra el plano y elegir paradas, pero ninguna acción de edición', async () => {
    resetMock([T({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false })])
    wrap()
    await ready()
    expect(plane()).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Arrastrar la orden/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Poner pin|Mover/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Subir la orden|Bajar la orden|Quitar la orden/ })).toBeNull()
    await userEvent.setup().click(within(plane()).getByRole('button', { name: /Parada 1/ }))
    expect(rows()[0]).toHaveClass('on')
    expect(screen.queryByRole('button', { name: 'Mover pin' })).toBeNull()
  })

  it('en una ruta en curso los estados de las paradas se ven en el plano (entregada, fallida, en camino)', async () => {
    resetMock([
      tripDetail({
        statusCode: 'IN_PROGRESS',
        status: 'En curso',
        isEditable: false,
        stops: [
          stop(1, 1, 'OT-0101', { point: P1, geocodeAccuracyCode: 'EXACT', statusCode: 'COMPLETED' }),
          stop(2, 2, 'OT-0102', { point: P2, geocodeAccuracyCode: 'EXACT', statusCode: 'FAILED' }),
          stop(3, 3, 'OT-0103', { point: { lat: 18.3, lng: -66.3 }, geocodeAccuracyCode: 'EXACT', statusCode: 'ON_THE_WAY' }),
        ],
      }),
    ])
    wrap()
    await ready()
    expect(within(plane()).getByRole('button', { name: /Parada 1/ })).toHaveAttribute('data-state', 'done')
    expect(within(plane()).getByRole('button', { name: /Parada 2/ })).toHaveAttribute('data-state', 'failed')
    expect(within(plane()).getByRole('button', { name: /Parada 3/ })).toHaveAttribute('data-state', 'current')
  })
})
