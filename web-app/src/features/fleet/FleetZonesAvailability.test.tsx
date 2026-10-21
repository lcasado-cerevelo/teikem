// Lote F-D2 — Flota: pestaña «Zonas de despacho» (lista, alta, código repetido, edición del nombre, inactivar con el 409 del servidor visible,
// reactivar, permisos) y pestaña «Disponibilidad» (fecha, «Solo disponibles», choferes y vehículos con chip y motivos en texto claro,
// los que bloquean aparte de los que avisan, clic que abre la ficha). Sobre el fetch simulado con estado de `fleetMock`.
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, mock, problem, resetKit, VIEW, wrap } from './fleetTestKit'
import { typeDate } from '../../kernel/ui/typeDate'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./fleetMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const rowOf = (scope: ReturnType<typeof within>, text: string) => scope.getByText(text).closest('tr') as HTMLElement

describe('Flota — zonas de despacho', () => {
  const open = (permissions = ALL) => wrap(permissions, '/catalog/fleet?tab=zones')
  const table = async () => within(await screen.findByRole('table'))

  it('lista las zonas activas con código, nombre, choferes y estado; las inactivas solo con «Mostrar inactivas»', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    const r1 = rowOf(t, 'R-01')
    expect(within(r1).getByText('Toa Baja · Bayamón')).toBeInTheDocument()
    expect(within(r1).getByText('2')).toBeInTheDocument()
    expect(within(r1).getByText('Activo')).toBeInTheDocument()
    expect(t.queryByText('R-09')).toBeNull()
    expect(callsTo('GET', '/api/v1/dispatch-zones')[0].url.searchParams.get('includeInactive')).toBe('false')
    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivas' }))
    expect(within(rowOf(await table(), 'R-09')).getByText('Inactivo')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/dispatch-zones').some((c) => c.url.searchParams.get('includeInactive') === 'true')).toBe(true)
  })

  it('el buscador filtra por código o nombre, sin acentos ni mayúsculas, y el contador sigue lo que se ve', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('R-02')
    await user.type(screen.getByRole('searchbox'), 'CÁGUAS')
    await waitFor(() => expect(t.queryByText('R-01')).toBeNull())
    expect(t.getByText('R-02')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'zzz')
    expect(await screen.findByText('No hay zonas de despacho.')).toBeInTheDocument()
  })

  it('«Nueva zona»: el código es obligatorio, va en mayúsculas y el nombre es opcional', async () => {
    const user = userEvent.setup()
    open()
    await table()
    await user.click(screen.getByRole('button', { name: /Nueva zona/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva zona' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El código de la zona es obligatorio.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Código/), 'z'.repeat(21))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El código no puede exceder 20 caracteres.')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText(/^Código/))
    await user.type(within(dialog).getByLabelText(/^Código/), ' z-77 ')
    await user.type(within(dialog).getByLabelText('Nombre'), ' Norte ')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/dispatch-zones')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/dispatch-zones')[0].body).toEqual({ code: 'Z-77', name: 'Norte', zoneGroupId: null })
    expect(await screen.findByText('Zona creada.')).toBeInTheDocument()
    expect(within(rowOf(await table(), 'Z-77')).getByText('Norte')).toBeInTheDocument()
  })

  it('el código repetido (409) sale con el mensaje exacto del servidor y el diálogo sigue abierto', async () => {
    const user = userEvent.setup()
    open()
    await table()
    await user.click(screen.getByRole('button', { name: /Nueva zona/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva zona' })
    await user.type(within(dialog).getByLabelText(/^Código/), 'R-01')
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/dispatch-zones' ? problem(409, 'Ya existe una zona de despacho con ese código.', 'conflict') : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Ya existe una zona de despacho con ese código.')).toBeInTheDocument()
  })

  it('«Editar» cambia solo el nombre (el código queda fijo y deshabilitado)', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await user.click(within(rowOf(t, 'R-02')).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar zona' })
    expect(within(dialog).getByLabelText('Código')).toBeDisabled()
    expect(within(dialog).getByLabelText('Código')).toHaveValue('R-02')
    await user.clear(within(dialog).getByLabelText('Nombre'))
    await user.type(within(dialog).getByLabelText('Nombre'), 'Caguas Norte')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/dispatch-zones/2')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/dispatch-zones/2')[0].body).toEqual({ name: 'Caguas Norte' })
    expect(await screen.findByText('Zona guardada.')).toBeInTheDocument()
    expect(await (await table()).findByText('Caguas Norte')).toBeInTheDocument()
  })

  it('inactivar una zona con choferes: el 409 del servidor se ve dentro del diálogo; otra zona sí se inactiva', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await user.click(within(rowOf(t, 'R-01')).getByRole('button', { name: 'Inactivar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Inactivar zona' })
    expect(dialog).toHaveTextContent('La zona R-01 (Toa Baja · Bayamón) deja de ofrecerse al asignar choferes.')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/dispatch-zones/1/deactivate' ? problem(409, 'La zona tiene choferes asignados; reasígnelos antes de inactivarla.', 'conflict') : undefined
    await user.click(within(dialog).getByRole('button', { name: 'Inactivar' }))
    expect(await within(dialog).findByText('La zona tiene choferes asignados; reasígnelos antes de inactivarla.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    mock.override = null
    await user.click(within(rowOf(await table(), 'R-02')).getByRole('button', { name: 'Inactivar' }))
    const second = await screen.findByRole('dialog', { name: 'Inactivar zona' })
    await user.click(within(second).getByRole('button', { name: 'Inactivar' }))
    expect(await screen.findByText('Zona inactivada.')).toBeInTheDocument()
    await waitFor(() => expect(within(screen.getByRole('table')).queryByText('R-02')).toBeNull())
  })

  it('«Reactivar» vuelve a ofrecer la zona; un error del servidor sale como aviso', async () => {
    const user = userEvent.setup()
    open()
    await table()
    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivas' }))
    const old = rowOf(await table(), 'R-09')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/dispatch-zones/3/reactivate' ? problem(409, "El valor '00901' ya pertenece a la zona R-01.", 'conflict') : undefined
    await user.click(within(old).getByRole('button', { name: 'Reactivar' }))
    expect(await screen.findByText("El valor '00901' ya pertenece a la zona R-01.")).toBeInTheDocument()
    mock.override = null
    await user.click(within(rowOf(await table(), 'R-09')).getByRole('button', { name: 'Reactivar' }))
    expect(await screen.findByText('Zona reactivada.')).toBeInTheDocument()
    await waitFor(() => expect(within(rowOf(within(screen.getByRole('table')), 'R-09')).getByText('Activo')).toBeInTheDocument())
  })

  it('solo con fleet.view no hay «Nueva zona» ni acciones por fila', async () => {
    open(VIEW)
    const t = await table()
    expect(screen.queryByRole('button', { name: /Nueva zona/ })).toBeNull()
    expect(within(rowOf(t, 'R-01')).queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(within(rowOf(t, 'R-01')).queryByRole('button', { name: 'Inactivar' })).toBeNull()
  })

  it('aclara que los miembros de la zona se arman en Despacho', async () => {
    open()
    await table()
    expect(screen.getByText(/Los municipios y códigos postales de cada zona se arman en Despacho\./)).toBeInTheDocument()
  })
})

describe('Flota — disponibilidad', () => {
  const open = () => wrap(ALL, '/catalog/fleet?tab=availability')
  const section = async (name: string) => within((await screen.findByRole('heading', { level: 2, name })).closest('section') as HTMLElement)

  it('pide hoy por omisión y pinta choferes y vehículos con chip Disponible / No disponible', async () => {
    open()
    const drivers = await section('Choferes')
    const ramon = (await drivers.findByText('Ramón Díaz')).closest('tr') as HTMLElement
    expect(within(ramon).getByText('Disponible')).toBeInTheDocument()
    const jose = drivers.getByText('José Pérez').closest('tr') as HTMLElement
    expect(within(jose).getByText('No disponible')).toBeInTheDocument()
    const vehicles = await section('Vehículos')
    expect(within((await vehicles.findByText('V-01')).closest('tr') as HTMLElement).getByText('No disponible')).toBeInTheDocument()
    expect(within(vehicles.getByText('V-05').closest('tr') as HTMLElement).getByText('Disponible')).toBeInTheDocument()
    expect(within(vehicles.getByText('V-01').closest('tr') as HTMLElement).getByText('Van')).toBeInTheDocument()
    const first = callsTo('GET', '/api/v1/fleet/availability')[0].url.searchParams
    expect(first.get('date')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    expect(first.get('onlyAvailable')).toBe('false')
  })

  it('los motivos salen en texto claro: primero los que bloquean y después los avisos', async () => {
    open()
    const drivers = await section('Choferes')
    const jose = (await drivers.findByText('José Pérez')).closest('tr') as HTMLElement
    const items = within(jose).getAllByRole('listitem')
    expect(items.map((li) => li.textContent)).toEqual([
      'BloqueaEl chofer está en estatus «No disponible».',
      'BloqueaSu licencia venció el 08/30/2026.',
      'AvisoCertificación HazMat vencida el 09/01/2026.',
    ])
    const ramon = drivers.getByText('Ramón Díaz').closest('tr') as HTMLElement
    expect(within(ramon).getByRole('listitem')).toHaveTextContent('AvisoLicencia CDL-A vence el 10/20/2026.')
    const vehicles = await section('Vehículos')
    const v2 = (await vehicles.findByText('V-02')).closest('tr') as HTMLElement
    expect(within(v2).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['BloqueaTiene la orden de trabajo WO-501 en proceso.', 'BloqueaEl vehículo está en estatus «En mant.».'])
    expect(within(vehicles.getByText('V-05').closest('tr') as HTMLElement).getByRole('listitem')).toHaveTextContent('AvisoEl vehículo no tiene documentos registrados.')
  })

  it('«Solo disponibles» manda onlyAvailable=true y deja solo los que pueden salir', async () => {
    const user = userEvent.setup()
    open()
    const drivers = await section('Choferes')
    await drivers.findByText('José Pérez')
    await user.click(screen.getByRole('switch', { name: 'Solo disponibles' }))
    await waitFor(() => expect(callsTo('GET', '/api/v1/fleet/availability').some((c) => c.url.searchParams.get('onlyAvailable') === 'true')).toBe(true))
    await waitFor(() => expect(screen.queryByText('José Pérez')).toBeNull())
    expect(screen.getByText('Ramón Díaz')).toBeInTheDocument()
    expect(screen.queryByText('V-02')).toBeNull()
  })

  it('el buscador filtra las dos tablas por código, nombre o placa', async () => {
    const user = userEvent.setup()
    open()
    const drivers = await section('Choferes')
    await drivers.findByText('José Pérez')
    await user.type(screen.getByRole('searchbox'), 'hgp')
    await waitFor(() => expect(screen.queryByText('José Pérez')).toBeNull())
    expect(screen.queryByText('Ramón Díaz')).toBeNull()
    expect(screen.queryByText('V-01')).toBeNull()
    expect(screen.getByText('V-02')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'jose')
    await waitFor(() => expect(screen.queryByText('Ramón Díaz')).toBeNull())
    expect(screen.getByText('José Pérez')).toBeInTheDocument()
    expect(screen.getByText('No hay vehículos para mostrar.')).toBeInTheDocument()
  })

  it('cambiar la fecha vuelve a pedir esa fecha; una fecha vaciada no pide nada', async () => {
    const user = userEvent.setup()
    open()
    await section('Choferes')
    const date = screen.getByLabelText('Fecha')
    await user.clear(date)
    expect(callsTo('GET', '/api/v1/fleet/availability').every((c) => c.url.searchParams.get('date') !== '')).toBe(true)
    await typeDate(user, date, '2026-11-02')
    await waitFor(() => expect(callsTo('GET', '/api/v1/fleet/availability').some((c) => c.url.searchParams.get('date') === '2026-11-02')).toBe(true))
  })

  it('es de solo lectura: sin botones de acción; un clic en el chofer o el vehículo abre su ficha', async () => {
    const user = userEvent.setup()
    open()
    const drivers = await section('Choferes')
    await drivers.findByText('Ramón Díaz')
    expect(screen.queryByRole('button', { name: /Editar|Quitar|Guardar|Reactivar|Inactivar/ })).toBeNull()
    await user.click(drivers.getByText('Ramón Díaz'))
    expect(await screen.findByTestId('drivers-screen')).toHaveTextContent('/catalog/drivers?driver=d-1')
  })

  it('un error del servidor se muestra tal cual', async () => {
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/fleet/availability' ? problem(400, 'Fecha inválida de prueba.', 'validation') : undefined)
    open()
    expect(await screen.findByText('Fecha inválida de prueba.')).toBeInTheDocument()
  })
})
