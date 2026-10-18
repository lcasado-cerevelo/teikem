// Lote F-E2a — Sala de despacho sobre un fetch simulado: las tres columnas, la ruta activa con su estatus real, agregar órdenes (con el 422 y el
// 409 del servidor), ▲ ▼ y quitar, chofer y vehículo con disponibilidad y 409, optimizar (con y sin corrida anterior), «Rutas por despachar» con
// bloqueantes y avisos y lote con resultados mixtos, iniciar, cerrar (422), eliminar, nueva ruta, planificar el día, reasignar por zona, fecha,
// filtros al servidor y los permisos (con y sin trips.plan / optimize / dispatch / close / view y sin fleet.view).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { tenantToday } from '../../kernel/api/tenantZone'
import { setLang } from '../../kernel/i18n/i18n'
import DispatchScreen from './DispatchScreen'
import { callsMatching, callsTo, mock, problem, resetMock, stop, tripDetail, U1, U2, U3, type Call } from './dispatchMock'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./dispatchMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

const ALL = ['trips.view', 'trips.plan', 'trips.optimize', 'trips.dispatch', 'trips.close', 'fleet.view', 'clients.read']
const MODULES = ['LTL_GROUND', 'CATALOG']

function Where() {
  const l = useLocation()
  return <span data-testid="where">{l.pathname + l.search}</span>
}

function wrap(permissions: string[] = ALL, modules: string[] = MODULES, initial = '/ops/dispatch') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[initial]}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={modules}>
          <Routes>
            <Route path="/ops/dispatch" element={<DispatchScreen />} />
          </Routes>
          <Where />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const T1 = () => tripDetail({ stops: [stop(1, 1, 'OT-0101'), stop(2, 2, 'OT-0102')] })
const btn = (name: string | RegExp) => screen.getByRole('button', { name })
const where = () => screen.getByTestId('where').textContent
const stopsList = () => screen.getByRole('list', { name: 'Órdenes en esta ruta' })

beforeAll(() => setLang('es'))
beforeEach(() => resetMock([T1()]))

describe('Sala de despacho — pantalla', () => {
  it('título, las tres columnas de la maqueta, la ruta activa con su estatus y su plano, sin botones falsos', async () => {
    wrap()
    expect(screen.getByRole('heading', { level: 1, name: 'Sala de despacho' })).toBeInTheDocument()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    for (const title of ['Órdenes sin asignar', 'Choferes', 'Rutas de hoy']) expect(screen.getByRole('heading', { name: title })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('2026-0001')
    expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('Borrador')
    expect(screen.getByRole('heading', { name: /Ruta activa/ }).closest('.panel')).toHaveTextContent('R-01 · 2 paradas')
    // el plano esquemático (F-E2b) está; el conmutador 2D/Sat y el tracking en vivo (F-E3) no existen
    expect(screen.getByRole('group', { name: 'Plano de la ruta 2026-0001' })).toBeInTheDocument()
    for (const fake of [/^2D$/, /^Sat$/, /Tracking/i]) expect(screen.queryByText(fake)).toBeNull()
    for (const name of ['Planificar el día', 'Nueva ruta', 'Reasignar por zona', 'Optimizar', 'Despachar ruta']) expect(btn(name)).toBeInTheDocument()
  })

  it('las órdenes de la ruta, con ETA, y las órdenes sin asignar con consignatario, pueblo · CP y zona (sin COD: el servidor no manda montos)', async () => {
    wrap()
    const list = await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(within(list).getAllByRole('listitem')).toHaveLength(2)
    expect(list).toHaveTextContent('OT-0101')
    expect(list).toHaveTextContent('Consignatario OT-0101')
    expect(list).toHaveTextContent('Bayamón · 00959')
    const row = (await screen.findByLabelText('OT-0001')).closest('label')!
    expect(row).toHaveTextContent('Taller Díaz')
    expect(row).toHaveTextContent('Bayamón · 00959 · R-01')
    expect(row).not.toHaveTextContent('$') // la lista del servidor no trae montos (sin dinero en trips)
    expect((await screen.findByLabelText('OT-0002')).closest('label')).not.toHaveTextContent('$')
    expect((await screen.findByLabelText('OT-0003')).closest('label')).toHaveTextContent('Sin zona')
  })

  it('sin rutas en la fecha explica qué hacer; los botones de arriba siguen ahí', async () => {
    resetMock([])
    wrap()
    expect(await screen.findByText('No hay rutas para esta fecha. Use «Planificar el día» o «Nueva ruta».')).toBeInTheDocument()
    expect(screen.getByText('No hay rutas para esta fecha.')).toBeInTheDocument()
    expect(btn('Planificar el día')).toBeEnabled()
    expect(btn('Optimizar')).toBeDisabled()
  })

  it('«Rutas de hoy»: elegir una tarjeta la pone en ?trip= y cambia la ruta activa; sin elección, la primera', async () => {
    const user = userEvent.setup()
    resetMock([T1(), tripDetail({ id: 2, publicId: 't-2', code: '2026-0002', zoneCode: 'R-02', driverCode: 'D-02', statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, stops: [stop(5, 1, 'OT-0201')] })])
    wrap()
    const today = await screen.findByRole('list', { name: 'Rutas de hoy' })
    await within(today).findByText('2026-0002')
    expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('2026-0001')
    expect(where()).toBe('/ops/dispatch')
    await user.click(within(today).getByRole('button', { name: /2026-0002/ }))
    expect(where()).toBe('/ops/dispatch?trip=t-2')
    await waitFor(() => expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('2026-0002'))
    expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('Despachada')
    expect(within(today).getByRole('button', { name: /2026-0002/ })).toHaveAttribute('aria-current', 'true')
  })

  it('una ruta en ?trip= abre directo y el chip de estatus usa el nombre real del servidor', async () => {
    resetMock([T1(), tripDetail({ id: 2, publicId: 't-2', code: '2026-0002', statusCode: 'PLANNED', status: 'Planificada', stops: [stop(5, 1, 'OT-0201')] })])
    wrap(ALL, MODULES, '/ops/dispatch?trip=t-2')
    await waitFor(() => expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('2026-0002'))
    expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('Planificada')
  })

  it('«Choferes»: código, nombre, zona y «≤ máximo»; el no disponible lleva su marca', async () => {
    wrap()
    const list = await screen.findByRole('list', { name: 'Choferes' })
    await waitFor(() => expect(within(list).getAllByRole('listitem')).toHaveLength(3))
    const d2 = within(list).getByText('Luis Soto').closest('li')!
    expect(d2).toHaveTextContent('D-02')
    expect(d2).toHaveTextContent('R-02')
    expect(d2).toHaveTextContent('≤12')
    await waitFor(() => expect(d2).toHaveTextContent('No disponible'))
    expect(within(list).getByText('Ana Pérez').closest('li')).toHaveTextContent('≤25')
  })

  it('la barra de avance cuenta las paradas atendidas de la ruta', async () => {
    resetMock([tripDetail({ stops: [stop(1, 1, 'OT-1', { statusCode: 'COMPLETED' }), stop(2, 2, 'OT-2', { statusCode: 'FAILED' }), stop(3, 3, 'OT-3'), stop(4, 4, 'OT-4')], statusCode: 'IN_PROGRESS', status: 'En curso', isEditable: false })])
    wrap()
    expect(await screen.findByText('2/4 paradas')).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Avance de la ruta' })).toHaveAttribute('aria-valuenow', '50')
  })
})

describe('aviso de máximo de paradas', () => {
  it('banner rojo con N > M y el origen del máximo: override del chofer o default de la compañía', async () => {
    resetMock([tripDetail({ driverPublicId: 'd-2', driverCode: 'D-02', driverName: 'Luis Soto', stopCount: 15, effectiveMaxStops: 12, overStopLimit: true, stops: [stop(1, 1, 'OT-1')] })])
    wrap()
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('2026-0001 sobrepasa el máximo de paradas: 15 > 12 (override del chofer)')
    // la tarjeta de «Rutas de hoy» también marca el aviso
    expect(screen.getByRole('img', { name: 'Sobrepasa el máximo de paradas' })).toBeInTheDocument()
  })

  it('con el default de la compañía', async () => {
    resetMock([tripDetail({ stopCount: 30, effectiveMaxStops: 25, overStopLimit: true, stops: [stop(1, 1, 'OT-1')] })])
    wrap()
    expect(await screen.findByRole('alert')).toHaveTextContent('30 > 25 (default de la compañía)')
  })

  it('sin fleet.view no se afirma el origen (no se conoce al chofer)', async () => {
    resetMock([tripDetail({ stopCount: 30, effectiveMaxStops: 25, overStopLimit: true, stops: [stop(1, 1, 'OT-1')] })])
    wrap(['trips.view', 'trips.dispatch'])
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('30 > 25')
    expect(banner).not.toHaveTextContent('default de la compañía')
  })

  it('no hay banner si la ruta no lo sobrepasa', async () => {
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.queryByRole('alert')).toBeNull()
  })
})

describe('agregar órdenes a la ruta', () => {
  it('«Añadir a la ruta (N)» manda las marcadas con rowVersion e Idempotency-Key; las órdenes pasan a la ruta y salen de la lista', async () => {
    const user = userEvent.setup()
    wrap()
    const add = () => btn(/^Añadir a la ruta/)
    await screen.findByLabelText('OT-0001')
    expect(add()).toBeDisabled()
    await user.click(screen.getByLabelText('OT-0001'))
    await user.click(screen.getByLabelText('OT-0002'))
    expect(add()).toHaveTextContent('Añadir a la ruta (2)')
    expect(screen.getByText('Se añadirán a la ruta 2026-0001.')).toBeInTheDocument()
    await user.click(add())
    await waitFor(() => expect(callsTo('POST', '/api/v1/trips/t-1/orders')).toHaveLength(1))
    const call = callsTo('POST', '/api/v1/trips/t-1/orders')[0]
    expect(call.body).toEqual({ orderPublicIds: ['u-1', 'u-2'], rowVersion: 'rv-1' })
    expect(call.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
    await waitFor(() => expect(within(stopsList()).getAllByRole('listitem')).toHaveLength(4))
    expect(screen.queryByLabelText('OT-0001')).toBeNull()
    expect(screen.getByLabelText('OT-0003')).toBeInTheDocument()
    expect(add()).toBeDisabled()
  })

  it('«Marcar todas» marca lo que se ve (con el buscador, solo lo que coincide) y de nuevo las desmarca', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByLabelText('OT-0001')
    await user.click(screen.getByLabelText('Marcar todas'))
    expect(btn(/^Añadir a la ruta/)).toHaveTextContent('Añadir a la ruta (3)')
    await user.click(screen.getByLabelText('Marcar todas'))
    expect(btn(/^Añadir a la ruta/)).toHaveTextContent(/^Añadir a la ruta$/)
    await user.type(screen.getByRole('searchbox', { name: 'Buscar en lo cargado…' }), 'caguas')
    expect(screen.queryByLabelText('OT-0001')).toBeNull()
    await user.click(screen.getByLabelText('Marcar todas'))
    expect(btn(/^Añadir a la ruta/)).toHaveTextContent('Añadir a la ruta (1)')
  })

  it('422: «Hay órdenes que no se pueden asignar» con cada número y su motivo; la selección se conserva', async () => {
    const user = userEvent.setup()
    wrap()
    mock.override = (c: Call) =>
      c.method === 'POST' && c.url.pathname.endsWith('/orders')
        ? problem(422, 'Hay órdenes que no se pueden asignar a la ruta.', 'status_rule', { 'OT-0001': ['La orden está en Sometida; márquela como recogida antes de asignarla a una ruta.'] })
        : undefined
    await user.click(await screen.findByLabelText('OT-0001'))
    await user.click(screen.getByLabelText('OT-0002'))
    await user.click(btn(/^Añadir a la ruta/))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Hay órdenes que no se pueden asignar a la ruta.')
    expect(alert).toHaveTextContent('OT-0001: La orden está en Sometida; márquela como recogida antes de asignarla a una ruta.')
    expect(btn(/^Añadir a la ruta/)).toHaveTextContent('Añadir a la ruta (2)')
    expect(screen.getByLabelText('OT-0001').closest('label')).toHaveClass('err')
    await user.click(btn('Entendido'))
    expect(screen.queryByText(/Hay órdenes que no se pueden/)).toBeNull()
  })

  it('409: «La orden ya está asignada a la ruta …» tal cual', async () => {
    const user = userEvent.setup()
    wrap()
    mock.override = (c: Call) => (c.method === 'POST' && c.url.pathname.endsWith('/orders') ? problem(409, 'La orden ya está asignada a la ruta 2026-0009.', 'conflict') : undefined)
    await user.click(await screen.findByLabelText('OT-0001'))
    await user.click(btn(/^Añadir a la ruta/))
    expect(await screen.findByRole('alert')).toHaveTextContent('La orden ya está asignada a la ruta 2026-0009.')
  })

  it('más de 200 marcadas: el botón se bloquea con el mensaje del servidor y no llama', async () => {
    const user = userEvent.setup()
    resetMock([T1()], Array.from({ length: 201 }, (_, i) => ({ id: 100 + i, publicId: `x-${i}`, orderNumber: `X-${i}`, clientName: 'C', consigneeName: 'K', city: 'Bayamón', postalCode: '00959', zoneAmbiguous: false })))
    wrap()
    await screen.findByLabelText('X-0')
    await user.click(screen.getByLabelText('Marcar todas'))
    expect(btn(/^Añadir a la ruta/)).toBeDisabled()
    expect(screen.getByRole('alert')).toHaveTextContent('Agregue como máximo 200 órdenes por solicitud.')
    expect(callsTo('POST', '/api/v1/trips/t-1/orders')).toHaveLength(0)
  })

  it('una ruta despachada no recibe órdenes: el botón queda deshabilitado y lo explica', async () => {
    resetMock([tripDetail({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, stops: [stop(1, 1, 'OT-1')] })])
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByLabelText('OT-0001'))
    expect(btn(/^Añadir a la ruta/)).toBeDisabled()
    expect(screen.getByText('Elija una ruta abierta (borrador o planificada) para añadir órdenes.')).toBeInTheDocument()
  })

  it('los filtros van al servidor (CP, pueblo y «sin zona») y el contador sigue al total', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByLabelText('OT-0001')
    await user.type(screen.getByLabelText('Código postal'), '007')
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips/unassigned-orders').some((c) => c.url.searchParams.get('PostalCode') === '007')).toBe(true))
    await waitFor(() => expect(screen.queryByLabelText('OT-0001')).toBeNull())
    expect(screen.getByLabelText('OT-0002')).toBeInTheDocument()
    await user.click(btn('Limpiar'))
    await user.selectOptions(screen.getByLabelText('Zona'), 'none')
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips/unassigned-orders').some((c) => c.url.searchParams.get('NoZone') === 'true')).toBe(true))
    expect(await screen.findByLabelText('OT-0003')).toBeInTheDocument()
    expect(screen.queryByLabelText('OT-0001')).toBeNull()
  })

  it('con más de 500 avisa que se muestran las primeras', async () => {
    mock.override = (c: Call) =>
      c.url.pathname === '/api/v1/trips/unassigned-orders' ? { total: 731, skip: 0, take: 500, items: [U1] } : undefined
    wrap()
    expect(await screen.findByText('Se muestran las primeras 1 de 731. Afine los filtros para ver el resto.')).toBeInTheDocument()
    expect(U2).toBeDefined()
    expect(U3).toBeDefined()
  })
})

describe('secuencia: ▲ ▼ y quitar', () => {
  it('bajar la primera manda la secuencia exacta de ids con rowVersion y la lista cambia de orden', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(screen.getByRole('button', { name: 'Bajar la orden OT-0101' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/trips/t-1/route/sequence')[0].body).toEqual({ routeStopIds: [2, 1], rowVersion: 'rv-1' })
    await waitFor(() => expect(within(stopsList()).getAllByRole('listitem')[0]).toHaveTextContent('OT-0102'))
  })

  it('la primera no sube y la última no baja', async () => {
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.getByRole('button', { name: 'Subir la orden OT-0101' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Bajar la orden OT-0102' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Subir la orden OT-0102' })).toBeEnabled()
  })

  it('quitar manda DELETE con rowVersion y la orden vuelve a «sin asignar» (se recarga la lista)', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    const before = callsTo('GET', '/api/v1/trips/unassigned-orders').length
    await user.click(screen.getByRole('button', { name: 'Quitar la orden OT-0101 de la ruta' }))
    await waitFor(() => expect(callsMatching('DELETE', /\/orders\//)).toHaveLength(1))
    expect(callsMatching('DELETE', /\/orders\//)[0].url.pathname).toBe('/api/v1/trips/t-1/orders/o-OT-0101')
    expect(callsMatching('DELETE', /\/orders\//)[0].body).toEqual({ rowVersion: 'rv-1' })
    await waitFor(() => expect(within(stopsList()).getAllByRole('listitem')).toHaveLength(1))
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips/unassigned-orders').length).toBeGreaterThan(before))
  })

  it('un error del servidor al reordenar o quitar se muestra tal cual', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) => (c.method === 'PUT' ? problem(400, 'La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.', 'validation', { routeStopIds: ['La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.'] }) : undefined)
    await user.click(screen.getByRole('button', { name: 'Bajar la orden OT-0101' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.')
    mock.override = (c: Call) => (c.method === 'DELETE' ? problem(404, 'La orden no está en esta ruta.', 'not_found') : undefined)
    await user.click(screen.getByRole('button', { name: 'Quitar la orden OT-0102 de la ruta' }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('La orden no está en esta ruta.'))
  })

  it('en una ruta despachada no hay ▲ ▼ ni quitar', async () => {
    resetMock([tripDetail({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, stops: [stop(1, 1, 'OT-1'), stop(2, 2, 'OT-2')] })])
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.queryByRole('button', { name: /Bajar la orden/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Quitar la orden/ })).toBeNull()
  })
})

describe('chofer y vehículo de la ruta', () => {
  it('muestra los actuales y marca los no disponibles; cambiar el chofer manda PATCH solo con ese campo y rowVersion', async () => {
    const user = userEvent.setup()
    wrap()
    const driver = await screen.findByLabelText('Chofer')
    await waitFor(() => expect(within(driver).getAllByRole('option').length).toBeGreaterThan(2))
    expect(driver).toHaveValue('d-1')
    expect(within(driver).getByRole('option', { name: 'D-02 · Luis Soto — no disponible' })).toBeInTheDocument()
    await user.selectOptions(driver, 'd-3')
    await waitFor(() => expect(callsMatching('PATCH', /\/trips\/t-1$/)).toHaveLength(1))
    expect(callsMatching('PATCH', /\/trips\/t-1$/)[0].body).toEqual({ driverPublicId: 'd-3', rowVersion: 'rv-1' })
    await waitFor(() => expect(screen.getByLabelText('Chofer')).toHaveValue('d-3'))
  })

  it('cambiar el vehículo; «Sin vehículo» manda clearVehicle', async () => {
    const user = userEvent.setup()
    wrap()
    const vehicle = await screen.findByLabelText('Vehículo')
    await waitFor(() => expect(within(vehicle).getAllByRole('option').length).toBeGreaterThan(2))
    await user.selectOptions(vehicle, 'v-2')
    await waitFor(() => expect(callsMatching('PATCH', /\/trips\/t-1$/)).toHaveLength(1))
    expect(callsMatching('PATCH', /\/trips\/t-1$/)[0].body).toEqual({ vehiclePublicId: 'v-2', rowVersion: 'rv-1' })
    await waitFor(() => expect(screen.getByLabelText('Vehículo')).toHaveValue('v-2'))
    await user.selectOptions(screen.getByLabelText('Vehículo'), '')
    await waitFor(() => expect(callsMatching('PATCH', /\/trips\/t-1$/)).toHaveLength(2))
    expect(callsMatching('PATCH', /\/trips\/t-1$/)[1].body).toEqual({ clearVehicle: true, rowVersion: expect.any(String) })
  })

  it('elegir un chofer no disponible: el 409 del servidor se muestra tal cual y el select vuelve al chofer de la ruta', async () => {
    const user = userEvent.setup()
    wrap()
    const driver = await screen.findByLabelText('Chofer')
    await waitFor(() => expect(within(driver).getAllByRole('option').length).toBeGreaterThan(2))
    mock.override = (c: Call) => (c.method === 'PATCH' ? problem(409, 'El chofer no está disponible para despacho: La licencia está vencida.', 'conflict') : undefined)
    await user.selectOptions(driver, 'd-2')
    expect(await screen.findByRole('alert')).toHaveTextContent('El chofer no está disponible para despacho: La licencia está vencida.')
    await waitFor(() => expect(screen.getByLabelText('Chofer')).toHaveValue('d-1'))
  })

  it('409 de edición concurrente: el aviso del servidor y la ruta se vuelve a pedir', async () => {
    const user = userEvent.setup()
    wrap()
    const driver = await screen.findByLabelText('Chofer')
    await waitFor(() => expect(within(driver).getAllByRole('option').length).toBeGreaterThan(2))
    const before = callsTo('GET', '/api/v1/trips/t-1').length
    mock.override = (c: Call) => (c.method === 'PATCH' ? problem(409, 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', 'conflict') : undefined)
    await user.selectOptions(driver, 'd-3')
    expect(await screen.findByRole('alert')).toHaveTextContent('El registro fue modificado por otro usuario; recargue e intente de nuevo.')
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips/t-1').length).toBeGreaterThan(before))
  })

  it('una ruta despachada sin EDIT_TRIP muestra el chofer y el vehículo como texto y lo explica', async () => {
    resetMock([tripDetail({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, canEditHeader: false, stops: [stop(1, 1, 'OT-1')] })])
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.queryByRole('combobox', { name: 'Chofer' })).toBeNull()
    expect(screen.getByText('D-01 · Ana Pérez')).toBeInTheDocument()
    expect(screen.getByText('El chofer y el vehículo de esta ruta no se pueden cambiar en su estatus actual.')).toBeInTheDocument()
  })

  it('una ruta despachada con EDIT_TRIP permite cambiar pero no quitar (sin la opción «Sin chofer»)', async () => {
    resetMock([tripDetail({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, canEditHeader: true, stops: [stop(1, 1, 'OT-1')] })])
    wrap()
    const driver = await screen.findByLabelText('Chofer')
    await waitFor(() => expect(within(driver).getAllByRole('option').length).toBeGreaterThan(1))
    expect(within(driver).queryByRole('option', { name: 'Sin chofer' })).toBeNull()
  })

  it('sin fleet.view: texto en lugar de selects, con la explicación, y el panel de choferes lo dice', async () => {
    wrap(['trips.view', 'trips.plan', 'trips.dispatch'])
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.queryByRole('combobox', { name: 'Chofer' })).toBeNull()
    expect(screen.getByText('Su usuario no puede consultar choferes ni vehículos, así que no puede cambiarlos desde aquí.')).toBeInTheDocument()
    expect(screen.getByText('Su usuario no tiene acceso para consultar los choferes. Pídalo a su administrador.')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/drivers')).toHaveLength(0)
    expect(callsTo('GET', '/api/v1/fleet/availability')).toHaveLength(0)
    expect(callsTo('GET', '/api/v1/dispatch-zones')).toHaveLength(0)
    expect(screen.queryByLabelText('Zona')).toBeNull()
  })
})

describe('optimizar', () => {
  it('manda rowVersion y muestra el resultado; sin corrida anterior lo dice y no inventa un porcentaje', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Optimizar'))
    await waitFor(() => expect(callsTo('POST', '/api/v1/trips/t-1/optimize')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/trips/t-1/optimize')[0].body).toEqual({ rowVersion: 'rv-1' })
    const note = await screen.findByText(/Ruta optimizada: 2 paradas ordenadas y 0 que no cupieron\./)
    expect(note).toHaveTextContent('Es la primera optimización de esta ruta')
    expect(note).not.toHaveTextContent('%')
    // la ruta pasó a Planificada
    await waitFor(() => expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('Planificada'))
  })

  it('con una corrida anterior compara distancia y duración', async () => {
    const user = userEvent.setup()
    wrap()
    mock.runs = [
      { id: 2, statusCode: 'OK', totalDistanceKm: 44, totalDurationMin: 82 },
      { id: 1, statusCode: 'OK', totalDistanceKm: 50, totalDurationMin: 90 },
    ]
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Optimizar'))
    const note = await screen.findByText(/ordenadas y 0 que no cupieron/)
    expect(note).toHaveTextContent('Distancia -12 % contra la corrida anterior.')
    expect(note).toHaveTextContent('Duración -8 min.')
  })

  it('sin distancia en las corridas (direcciones sin coordenadas) compara solo la duración; sin nada comparable lo dice', async () => {
    const user = userEvent.setup()
    wrap()
    mock.runs = [
      { id: 2, statusCode: 'OK', totalDurationMin: 25 },
      { id: 1, statusCode: 'OK', totalDurationMin: 30 },
    ]
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Optimizar'))
    const note = await screen.findByText(/ordenadas y 0 que no cupieron/)
    expect(note).toHaveTextContent('Duración -5 min.')
    expect(note).not.toHaveTextContent('Distancia')
    mock.runs = [{ id: 4, statusCode: 'OK' }, { id: 3, statusCode: 'OK' }]
    await user.click(btn('Optimizar'))
    await waitFor(() => expect(screen.getByText(/ordenadas y 0 que no cupieron/)).toHaveTextContent('la corrida anterior no trae distancia ni duración'))
  })

  it('lo que no cupo se ve con su motivo (última corrida)', async () => {
    resetMock([tripDetail({ stops: [stop(1, 1, 'OT-1')], lastRunUnassigned: [{ orderPublicId: 'o-9', orderNumber: 'OT-0009', reasonCode: 'OVER_WEIGHT', reason: 'La carga excede la capacidad del vehículo.' }] })])
    wrap()
    expect(await screen.findByText('No cupieron (1)')).toBeInTheDocument()
    expect(screen.getByText('OT-0009')).toBeInTheDocument()
    expect(screen.getByText(/La carga excede la capacidad del vehículo\./)).toBeInTheDocument()
  })

  it('el 422 del servidor se muestra en la ruta', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) => (c.url.pathname.endsWith('/optimize') ? problem(422, 'La ruta no tiene paradas que optimizar.', 'status_rule') : undefined)
    await user.click(btn('Optimizar'))
    expect(await screen.findByRole('alert')).toHaveTextContent('La ruta no tiene paradas que optimizar.')
  })

  it('sin paradas o con la ruta despachada el botón está deshabilitado y dice por qué', async () => {
    resetMock([tripDetail({ stops: [] })])
    wrap()
    await screen.findByText('Todavía no tiene órdenes asignadas.')
    expect(btn('Optimizar')).toBeDisabled()
    expect(btn('Optimizar')).toHaveAttribute('title', 'La ruta no tiene paradas que optimizar.')
  })

  it('una ruta despachada no se optimiza', async () => {
    resetMock([tripDetail({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, stops: [stop(1, 1, 'OT-1')] })])
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(btn('Optimizar')).toBeDisabled()
    expect(btn('Optimizar')).toHaveAttribute('title', 'Solo se optimiza una ruta en borrador o planificada.')
  })
})

describe('avisos y bloqueantes de la ruta', () => {
  it('los bloqueantes primero y en rojo; los avisos en ámbar', async () => {
    resetMock([
      tripDetail({
        stops: [stop(1, 1, 'OT-1')],
        issues: [
          { code: 'NO_PLANNED_START', message: 'La ruta no tiene hora de salida; no se calculan las ETAs.', blocking: false },
          { code: 'NO_VEHICLE', message: 'La ruta no tiene vehículo asignado.', blocking: true },
        ],
      }),
    ])
    wrap()
    const list = await screen.findByRole('list', { name: 'Avisos y bloqueantes' })
    const items = within(list).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Bloquea')
    expect(items[0]).toHaveTextContent('La ruta no tiene vehículo asignado.')
    expect(items[1]).toHaveTextContent('Aviso')
    expect(items[1]).toHaveAttribute('data-blocking', 'false')
  })
})

describe('Rutas por despachar', () => {
  const planned = (code: string, id: string, issues: Array<{ code: string; message: string; blocking: boolean }> = [], canDispatch = true) => ({
    trip: { publicId: id, code, driverCode: 'D-01', driverName: 'Ana Pérez', zoneCode: 'R-01', stopCount: 3 },
    canDispatch,
    issues,
  })

  it('lista con chofer · zona · paradas; el bloqueante pinta rojo y no se puede marcar; el aviso sí', async () => {
    const user = userEvent.setup()
    wrap()
    mock.dispatchable = [
      planned('2026-0001', 't-1'),
      planned('2026-0002', 't-2', [{ code: 'NO_DRIVER', message: 'La ruta no tiene chofer asignado.', blocking: true }], false),
      planned('2026-0003', 't-3', [{ code: 'OVER_WEIGHT', message: 'La carga (900 kg) excede la capacidad de peso del vehículo (800 kg).', blocking: false }]),
    ]
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Despachar ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Rutas por despachar' })
    expect(await within(dialog).findByText('2026-0002')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('2026-0002')).toBeDisabled()
    expect(within(dialog).getByLabelText('2026-0001')).toBeEnabled()
    expect(within(dialog).getByLabelText('2026-0003')).toBeEnabled()
    const blocked = within(dialog).getByLabelText('2026-0002').closest('label')!
    expect(blocked).toHaveTextContent('Bloquea')
    expect(blocked).toHaveTextContent('La ruta no tiene chofer asignado.')
    expect(blocked).toHaveTextContent('Ana Pérez · R-01 · 3 paradas')
    expect(within(dialog).getByLabelText('2026-0003').closest('label')).toHaveTextContent('Aviso')
    expect(within(dialog).getByRole('button', { name: /Despachar seleccionadas \(1\)/ })).toBeEnabled() // la ruta activa viene marcada
  })

  it('la ruta activa llega marcada; «Seleccionar todas» marca solo las despachables', async () => {
    const user = userEvent.setup()
    wrap()
    mock.dispatchable = [planned('2026-0001', 't-1'), planned('2026-0002', 't-2', [{ code: 'NO_DRIVER', message: 'La ruta no tiene chofer asignado.', blocking: true }], false), planned('2026-0004', 't-4')]
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Despachar ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Rutas por despachar' })
    await within(dialog).findByText('2026-0004')
    expect(within(dialog).getByLabelText('2026-0001')).toBeChecked()
    expect(within(dialog).getByRole('button', { name: /Despachar seleccionadas \(1\)/ })).toBeEnabled()
    await user.click(within(dialog).getByLabelText('Seleccionar todas'))
    expect(within(dialog).getByRole('button', { name: /Despachar seleccionadas \(2\)/ })).toBeEnabled()
    expect(within(dialog).getByLabelText('2026-0002')).not.toBeChecked()
    await user.click(within(dialog).getByLabelText('Seleccionar todas'))
    expect(within(dialog).getByRole('button', { name: /Despachar seleccionadas/ })).toBeDisabled()
  })

  it('lote con resultados mixtos: lo despachado y el error exacto de la que falló', async () => {
    const user = userEvent.setup()
    wrap()
    mock.dispatchable = [planned('2026-0001', 't-1'), planned('2026-0004', 't-4')]
    mock.override = (c: Call) =>
      c.method === 'POST' && c.url.pathname === '/api/v1/trips/dispatch'
        ? {
            requested: 2,
            dispatched: 1,
            items: [
              { tripPublicId: 't-1', code: '2026-0001', dispatched: true, error: null, issues: [] },
              { tripPublicId: 't-4', code: '2026-0004', dispatched: false, error: 'La ruta 2026-0004 no se puede despachar: La ruta no tiene paradas.', issues: [{ code: 'NO_STOPS', message: 'La ruta no tiene paradas.', blocking: true }] },
            ],
          }
        : undefined
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Despachar ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Rutas por despachar' })
    await within(dialog).findByText('2026-0004')
    await user.click(within(dialog).getByLabelText('Seleccionar todas'))
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), 'salida de la mañana')
    await user.click(within(dialog).getByRole('button', { name: /Despachar seleccionadas \(2\)/ }))
    const dialog2 = await screen.findByRole('dialog', { name: 'Resultado del despacho' })
    expect(dialog2).toHaveTextContent('Se despacharon 1 de 2 rutas.')
    const lines = within(dialog2).getAllByRole('listitem').filter((li) => li.className.includes('-line'))
    expect(lines[0]).toHaveTextContent('2026-0001 — despachada')
    expect(lines[1]).toHaveTextContent('2026-0004 — La ruta 2026-0004 no se puede despachar: La ruta no tiene paradas.')
    const post = callsTo('POST', '/api/v1/trips/dispatch')[0]
    expect(post.body).toEqual({ tripPublicIds: ['t-1', 't-4'], comment: 'salida de la mañana' })
    expect(post.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
    await user.click(within(dialog2).getByRole('button', { name: 'Listo' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('con avisos hay que confirmar: el primer clic no despacha, el segundo sí', async () => {
    const user = userEvent.setup()
    wrap()
    mock.dispatchable = [planned('2026-0003', 't-3', [{ code: 'OVER_WEIGHT', message: 'La carga (900 kg) excede la capacidad de peso del vehículo (800 kg).', blocking: false }])]
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Despachar ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Rutas por despachar' })
    await user.click(await within(dialog).findByLabelText('2026-0003'))
    await user.click(within(dialog).getByRole('button', { name: /Despachar seleccionadas \(1\)/ }))
    expect(callsTo('POST', '/api/v1/trips/dispatch')).toHaveLength(0)
    expect(within(dialog).getByText(/1 de las rutas elegidas traen avisos que no bloquean/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: /Despachar con avisos \(1\)/ }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/trips/dispatch')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/trips/dispatch')[0].body).toEqual({ tripPublicIds: ['t-3'], comment: null })
  })

  it('sin rutas pendientes lo dice', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Despachar ruta'))
    expect(await screen.findByText('No hay rutas pendientes por despachar')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/trips/dispatchable')[0].url.searchParams.get('date')).toBe(tenantToday())
  })
})

describe('iniciar, cerrar y eliminar la ruta', () => {
  it('«Iniciar ruta» solo con la ruta despachada y trips.dispatch; confirma con comentario opcional y manda rowVersion', async () => {
    resetMock([tripDetail({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, canEditHeader: false, stops: [stop(1, 1, 'OT-1')] })])
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.queryByRole('button', { name: 'Cerrar ruta' })).toBeNull()
    await user.click(btn('Iniciar ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Iniciar la ruta 2026-0001' })
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), 'sale a las 8')
    await user.click(within(dialog).getByRole('button', { name: 'Iniciar ruta' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/trips/t-1/start')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/trips/t-1/start')[0].body).toEqual({ comment: 'sale a las 8', rowVersion: 'rv-1' })
    await waitFor(() => expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('En curso'))
    // ya en curso: aparece «Cerrar ruta» y desaparece «Iniciar ruta»
    expect(btn('Cerrar ruta')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Iniciar ruta' })).toBeNull()
  })

  it('«Cerrar ruta»: el 422 con las órdenes sin terminar se muestra dentro de la ventana y no la cierra', async () => {
    resetMock([tripDetail({ statusCode: 'IN_PROGRESS', status: 'En curso', isEditable: false, canEditHeader: false, stops: [stop(1, 1, 'OT-1')] })])
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) => (c.url.pathname.endsWith('/complete') ? problem(422, 'La ruta 2026-0001 no se puede cerrar: hay órdenes sin terminar (OT-0101, OT-0102).', 'status_rule') : undefined)
    await user.click(btn('Cerrar ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar la ruta 2026-0001' })
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar ruta' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('La ruta 2026-0001 no se puede cerrar: hay órdenes sin terminar (OT-0101, OT-0102).')
    expect(screen.getByRole('dialog', { name: 'Cerrar la ruta 2026-0001' })).toBeInTheDocument()
    // sin el bloqueo, cierra y la ruta queda Completada
    mock.override = null
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar ruta' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(screen.getByRole('heading', { name: /Ruta activa/ })).toHaveTextContent('Completada'))
    expect(screen.queryByRole('button', { name: 'Cerrar ruta' })).toBeNull()
  })

  it('«Eliminar ruta» (papelera) con comentario de hasta 500: manda DELETE con comentario y rowVersion y quita la ruta de la lista', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(screen.getByRole('button', { name: 'Eliminar ruta' }))
    const dialog = await screen.findByRole('dialog', { name: 'Eliminar la ruta 2026-0001' })
    const comment = within(dialog).getByLabelText('Comentario (opcional)')
    await user.click(comment)
    await user.paste('x'.repeat(501))
    expect(within(dialog).getByRole('button', { name: 'Eliminar ruta' })).toBeDisabled()
    expect(within(dialog).getByText(/500/)).toBeInTheDocument()
    await user.clear(comment)
    await user.type(comment, 'duplicada')
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar ruta' }))
    await waitFor(() => expect(callsMatching('DELETE', /\/trips\/t-1$/)).toHaveLength(1))
    expect(callsMatching('DELETE', /\/trips\/t-1$/)[0].body).toEqual({ comment: 'duplicada', rowVersion: 'rv-1' })
    await waitFor(() => expect(screen.getByText('No hay rutas para esta fecha. Use «Planificar el día» o «Nueva ruta».')).toBeInTheDocument())
  })

  it('«Eliminar ruta» no aparece con la ruta despachada y el 422 del servidor se muestra tal cual', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) => (c.method === 'DELETE' ? problem(422, 'La ruta 2026-0001 ya fue despachada; no se puede editar ni eliminar.', 'status_rule') : undefined)
    await user.click(screen.getByRole('button', { name: 'Eliminar ruta' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar ruta' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('La ruta 2026-0001 ya fue despachada; no se puede editar ni eliminar.')
  })

  it('despachada no se elimina', async () => {
    resetMock([tripDetail({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, stops: [stop(1, 1, 'OT-1')] })])
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.queryByRole('button', { name: 'Eliminar ruta' })).toBeNull()
  })
})

describe('nueva ruta', () => {
  it('valida la fecha con el mensaje del servidor y crea con lo elegido; la ruta nueva queda activa en su fecha', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Nueva ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva ruta' })
    const date = within(dialog).getByLabelText(/Fecha de la ruta/)
    await user.clear(date)
    await user.click(within(dialog).getByRole('button', { name: 'Crear ruta' }))
    expect(await within(dialog).findByText('Indique la fecha de la ruta.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/trips')).toHaveLength(0)
    await user.type(date, '2020-01-01')
    await user.click(within(dialog).getByRole('button', { name: 'Crear ruta' }))
    expect(await within(dialog).findByText('La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.')).toBeInTheDocument()
    await user.clear(date)
    await user.type(date, tenantToday())
    await waitFor(() => expect(within(dialog).getByLabelText('Zona de despacho')).toBeInTheDocument())
    await waitFor(() => expect(within(within(dialog).getByLabelText('Chofer')).getAllByRole('option').length).toBeGreaterThan(1))
    await user.selectOptions(within(dialog).getByLabelText('Zona de despacho'), '2')
    await user.selectOptions(within(dialog).getByLabelText('Chofer'), 'd-3')
    await user.selectOptions(within(dialog).getByLabelText('Vehículo'), 'v-2')
    await user.type(within(dialog).getByLabelText('Hora de salida'), '0830')
    await user.click(within(dialog).getByRole('button', { name: 'Crear ruta' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/trips')).toHaveLength(1))
    const body = callsTo('POST', '/api/v1/trips')[0].body
    expect(body).toMatchObject({ planDate: tenantToday(), dispatchZoneId: 2, driverPublicId: 'd-3', vehiclePublicId: 'v-2' })
    expect(body?.plannedStartUtc).toMatch(/T12:30:00/)
    expect(callsTo('POST', '/api/v1/trips')[0].headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(where()).toMatch(/\?trip=t-new-/))
  })

  it('el error del servidor sale tal cual (409 de disponibilidad)', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) => (c.method === 'POST' && c.url.pathname === '/api/v1/trips' ? problem(409, 'El vehículo no está disponible para despacho: Mantenimiento vencido.', 'conflict') : undefined)
    await user.click(btn('Nueva ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva ruta' })
    await user.click(within(dialog).getByRole('button', { name: 'Crear ruta' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El vehículo no está disponible para despacho: Mantenimiento vencido.')
  })

  it('sin fleet.view se crea sin zona, chofer ni vehículo y lo explica', async () => {
    const user = userEvent.setup()
    wrap(['trips.view', 'trips.plan', 'trips.dispatch'])
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Nueva ruta'))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva ruta' })
    expect(within(dialog).getByText(/Su usuario no puede consultar zonas, choferes ni vehículos/)).toBeInTheDocument()
    expect(within(dialog).queryByLabelText('Chofer')).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Crear ruta' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/trips')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/trips')[0].body).toEqual({ planDate: tenantToday() })
  })
})

describe('planificar el día', () => {
  it('sin zonas manda todas (null), muestra el resumen y los avisos por zona', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) =>
      c.url.pathname === '/api/v1/trips/plan-day'
        ? {
            planDate: tenantToday(),
            tripsCreated: 1,
            ordersAssigned: 5,
            ordersSkipped: 2,
            ordersWithoutZone: 3,
            zones: [
              { dispatchZoneId: 1, zoneCode: 'R-01', tripPublicId: 't-1', tripCode: '2026-0001', tripCreated: false, ordersAssigned: 5, ordersSkipped: 2, issues: [{ code: 'NOT_ELIGIBLE', message: 'Orden OT-0007: La orden está en Sometida; márquela como recogida antes de asignarla a una ruta.', blocking: false }] },
              { dispatchZoneId: 2, zoneCode: 'R-02', tripPublicId: 't-9', tripCode: '2026-0009', tripCreated: true, ordersAssigned: 0, ordersSkipped: 0, issues: [] },
            ],
          }
        : undefined
    await user.click(btn('Planificar el día'))
    const dialog = await screen.findByRole('dialog', { name: 'Planificar el día' })
    await user.click(within(dialog).getByLabelText(/Crear rutas vacías/))
    await user.click(within(dialog).getByRole('button', { name: 'Planificar' }))
    const result = await screen.findByRole('dialog', { name: /Día planificado/ })
    expect(callsTo('POST', '/api/v1/trips/plan-day')[0].body).toEqual({ planDate: tenantToday(), dispatchZoneIds: null, createEmptyTrips: true })
    const summary = within(result).getByRole('group', { name: 'Resumen de la planificación' })
    expect(summary).toHaveTextContent('1Rutas creadas')
    expect(summary).toHaveTextContent('5Órdenes asignadas')
    expect(summary).toHaveTextContent('2Órdenes omitidas')
    expect(summary).toHaveTextContent('3Órdenes sin zona')
    expect(result).toHaveTextContent('R-01 · ruta 2026-0001 · 5 asignadas, 2 omitidas')
    expect(result).toHaveTextContent('R-02 · ruta 2026-0009 creada')
    expect(result).toHaveTextContent('Orden OT-0007: La orden está en Sometida; márquela como recogida antes de asignarla a una ruta.')
    await user.click(within(result).getByRole('button', { name: 'Listo' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('con zonas elegidas manda sus ids; el error del servidor sale tal cual', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    await user.click(btn('Planificar el día'))
    const dialog = await screen.findByRole('dialog', { name: 'Planificar el día' })
    await user.click(await within(dialog).findByLabelText(/^Zonas/))
    await user.click(within(dialog).getByLabelText(/R-02/))
    mock.override = (c: Call) => (c.url.pathname === '/api/v1/trips/plan-day' ? problem(400, 'La zona de despacho está inactiva.', 'validation', { dispatchZoneIds: ['La zona de despacho está inactiva.'] }) : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Planificar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/trips/plan-day')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/trips/plan-day')[0].body).toMatchObject({ dispatchZoneIds: [2] })
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('La zona de despacho está inactiva.')
  })
})

describe('reasignar por zona', () => {
  it('pide zonas y chofer, informa las rutas actualizadas y los avisos', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) =>
      c.url.pathname === '/api/v1/trips/reassign-zone'
        ? { driverPublicId: 'd-3', driverCode: 'D-03', driverName: 'Rosa Vega', tripsUpdated: 2, trips: [{ publicId: 't-1', code: '2026-0001', zoneCode: 'R-01' }, { publicId: 't-2', code: '2026-0002', zoneCode: 'R-01' }], issues: [{ code: 'DRIVER_DOUBLE_BOOKED', message: 'El chofer también está en la ruta 2026-0005 ese mismo día.', blocking: false }] }
        : undefined
    await user.click(btn('Reasignar por zona'))
    const dialog = await screen.findByRole('dialog', { name: 'Reasignar por zona' })
    await user.click(within(dialog).getByRole('button', { name: 'Reasignar' }))
    expect(await within(dialog).findByText('Indique al menos una zona.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el chofer.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/trips/reassign-zone')).toHaveLength(0)
    await user.click(within(dialog).getByLabelText(/^Zonas/))
    await user.click(within(dialog).getByLabelText(/R-01/))
    await waitFor(() => expect(within(within(dialog).getByLabelText(/^Nuevo chofer/)).getAllByRole('option').length).toBeGreaterThan(1))
    await user.selectOptions(within(dialog).getByLabelText(/^Nuevo chofer/), 'd-3')
    await user.click(within(dialog).getByRole('button', { name: 'Reasignar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/trips/reassign-zone')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/trips/reassign-zone')[0].body).toEqual({ planDate: tenantToday(), dispatchZoneIds: [1], driverPublicId: 'd-3' })
    const result = await screen.findByRole('dialog', { name: 'Reasignación terminada' })
    expect(result).toHaveTextContent('2 rutas pasaron al chofer D-03 · Rosa Vega.')
    expect(result).toHaveTextContent('2026-0002')
    expect(result).toHaveTextContent('El chofer también está en la ruta 2026-0005 ese mismo día.')
  })

  it('cero rutas no es un error: se informa que no se cambió nada', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) => (c.url.pathname === '/api/v1/trips/reassign-zone' ? { driverPublicId: 'd-3', tripsUpdated: 0, trips: [], issues: [] } : undefined)
    await user.click(btn('Reasignar por zona'))
    const dialog = await screen.findByRole('dialog', { name: 'Reasignar por zona' })
    await user.click(within(dialog).getByLabelText(/^Zonas/))
    await user.click(within(dialog).getByLabelText(/R-02/))
    await waitFor(() => expect(within(within(dialog).getByLabelText(/^Nuevo chofer/)).getAllByRole('option').length).toBeGreaterThan(1))
    await user.selectOptions(within(dialog).getByLabelText(/^Nuevo chofer/), 'd-3')
    await user.click(within(dialog).getByRole('button', { name: 'Reasignar' }))
    const result = await screen.findByRole('dialog', { name: 'Reasignación terminada' })
    expect(result).toHaveTextContent('No había rutas abiertas en esas zonas y fecha; no se cambió nada.')
  })

  it('el 409 de disponibilidad del chofer sale tal cual', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    mock.override = (c: Call) => (c.url.pathname === '/api/v1/trips/reassign-zone' ? problem(409, 'El chofer no está disponible para despacho: La licencia está vencida.', 'conflict') : undefined)
    await user.click(btn('Reasignar por zona'))
    const dialog = await screen.findByRole('dialog', { name: 'Reasignar por zona' })
    await user.click(within(dialog).getByLabelText(/^Zonas/))
    await user.click(within(dialog).getByLabelText(/R-02/))
    await waitFor(() => expect(within(within(dialog).getByLabelText(/^Nuevo chofer/)).getAllByRole('option').length).toBeGreaterThan(1))
    await user.selectOptions(within(dialog).getByLabelText(/^Nuevo chofer/), 'd-2')
    await user.click(within(dialog).getByRole('button', { name: 'Reasignar' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El chofer no está disponible para despacho: La licencia está vencida.')
  })
})

describe('fecha de las rutas', () => {
  it('cambiar la fecha la pone en ?date=, vuelve a pedir las rutas de ese día y «Hoy» regresa', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(callsTo('GET', '/api/v1/trips')[0].url.searchParams.get('date')).toBe(tenantToday())
    const date = screen.getByLabelText('Fecha de las rutas')
    expect(date).toHaveValue(tenantToday())
    expect(screen.queryByRole('button', { name: 'Hoy' })).toBeNull()
    fireEvent.change(date, { target: { value: '2026-11-02' } })
    expect(where()).toContain('date=2026-11-02')
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips').some((c) => c.url.searchParams.get('date') === '2026-11-02')).toBe(true))
    await user.click(btn('Hoy'))
    expect(where()).toBe('/ops/dispatch')
  })
})

describe('permisos', () => {
  it('solo trips.view + trips.dispatch: ve todo y puede despachar, pero no hay Planificar, Nueva ruta, Reasignar, Optimizar, Añadir, ▲ ▼, quitar ni papelera', async () => {
    wrap(['trips.view', 'trips.dispatch', 'fleet.view'])
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(btn('Despachar ruta')).toBeInTheDocument()
    for (const name of ['Planificar el día', 'Nueva ruta', 'Reasignar por zona', 'Optimizar', /^Añadir a la ruta/, /Bajar la orden/, /Quitar la orden/, 'Eliminar ruta']) {
      expect(screen.queryByRole('button', { name })).toBeNull()
    }
    // el chofer y el vehículo se ven como texto: cambiarlos exige trips.plan
    expect(screen.queryByRole('combobox', { name: 'Chofer' })).toBeNull()
    expect(screen.queryByText('Su usuario no puede consultar choferes ni vehículos, así que no puede cambiarlos desde aquí.')).toBeNull()
  })

  it('trips.optimize sin trips.plan: solo Optimizar', async () => {
    wrap(['trips.view', 'trips.dispatch', 'trips.optimize'])
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(btn('Optimizar')).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Nueva ruta' })).toBeNull()
  })

  it('«Iniciar ruta» sin trips.dispatch no existe (la pantalla pide ese permiso en la ruta, pero la acción también lo exige)', async () => {
    resetMock([tripDetail({ statusCode: 'DISPATCHED', status: 'Despachada', isEditable: false, stops: [stop(1, 1, 'OT-1')] })])
    wrap(['trips.view', 'trips.plan'])
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.queryByRole('button', { name: 'Iniciar ruta' })).toBeNull()
  })

  it('«Cerrar ruta» exige trips.close', async () => {
    resetMock([tripDetail({ statusCode: 'IN_PROGRESS', status: 'En curso', isEditable: false, stops: [stop(1, 1, 'OT-1')] })])
    const first = wrap(['trips.view', 'trips.dispatch'])
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(screen.queryByRole('button', { name: 'Cerrar ruta' })).toBeNull()
    first.unmount()
    wrap(['trips.view', 'trips.dispatch', 'trips.close'])
    expect(await screen.findByRole('button', { name: 'Cerrar ruta' })).toBeInTheDocument()
  })

  it('sin trips.view no se pide nada de rutas y se explica', async () => {
    wrap(['trips.dispatch', 'trips.plan'])
    expect(await screen.findByText('Su usuario no tiene acceso para consultar las rutas. Pídalo a su administrador.')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/trips')).toHaveLength(0)
    expect(callsTo('GET', '/api/v1/trips/unassigned-orders')).toHaveLength(0)
  })

  it('sin el módulo CATALOG no se piden choferes, vehículos ni zonas', async () => {
    wrap(ALL, ['LTL_GROUND'])
    await screen.findByRole('list', { name: 'Órdenes en esta ruta' })
    expect(callsTo('GET', '/api/v1/drivers')).toHaveLength(0)
    expect(callsTo('GET', '/api/v1/fleet/availability')).toHaveLength(0)
    expect(screen.getByText('Su usuario no tiene acceso para consultar los choferes. Pídalo a su administrador.')).toBeInTheDocument()
  })
})

describe('inglés', () => {
  it('los mismos controles con sus textos en inglés', async () => {
    setLang('en')
    try {
      wrap()
      expect(await screen.findByRole('heading', { level: 1, name: 'Dispatch room' })).toBeInTheDocument()
      for (const name of ['Plan the day', 'New route', 'Reassign by zone', 'Optimize', 'Dispatch route']) expect(btn(name)).toBeInTheDocument()
      expect(screen.getByRole('heading', { name: 'Unassigned orders' })).toBeInTheDocument()
    } finally {
      setLang('es')
    }
  })
})
