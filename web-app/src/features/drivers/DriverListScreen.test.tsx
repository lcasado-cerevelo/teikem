// Lote F-D1 — Choferes y tarifas: lista maestra (contador, filas, buscador y filtros que van al servidor, inactivos, selección por la URL),
// alta (request, validaciones del manual, errores del servidor bajo el campo), ficha (edición con PATCH mínimo y rowVersion, 409, las
// tres cosas distintas: Activo, Disponibilidad y Eliminar chofer, chofer eliminado de solo lectura, usuario vinculado) y permisos.
// Sobre un fetch simulado con estado (`driversMock`).
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, FLEET, mock, problem, resetKit, wrap } from './driversTestKit'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./driversMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const section = (name: string) => screen.getByRole('heading', { level: 2, name }).closest('section') as HTMLElement
const list = () => screen.findByRole('list', { name: 'Choferes' })

describe('Choferes — lista', () => {
  it('contador, código, nombre, chip y «zona · área»; solo activos por omisión; la ficha del primero se abre sola', async () => {
    wrap(ALL)
    const rows = await list()
    const first = callsTo('GET', '/api/v1/drivers')[0].url.searchParams
    expect(first.get('includeInactive')).toBe('false')
    const r1 = within(rows).getByRole('button', { name: /Ramón Díaz/ })
    expect(within(r1).getByText('010')).toBeInTheDocument()
    expect(within(r1).getByText('Activo')).toBeInTheDocument()
    expect(within(r1).getByText('R-01 · Toa Baja · Bayamón')).toBeInTheDocument()
    const r2 = within(rows).getByRole('button', { name: /José Pérez/ })
    expect(within(r2).getByText('No disponible')).toBeInTheDocument()
    expect(within(rows).queryByText('Ana Cruz')).toBeNull()
    expect(within(section('Choferes')).getByText('2')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Choferes y tarifas' })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Ficha del chofer' })).toBeInTheDocument()
    // la elección por omisión queda explícita en la dirección: la ficha no salta a otro chofer si este sale de la lista
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1').length).toBeGreaterThan(0))
  })

  it('el buscador manda `search` al servidor (con espera entre teclas)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.type(screen.getByRole('searchbox'), 'pérez')
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers').some((c) => c.url.searchParams.get('search') === 'pérez')).toBe(true))
    const rows = await list()
    await waitFor(() => expect(within(rows).queryByText('Ramón Díaz')).toBeNull())
    expect(within(rows).getByText('José Pérez')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'zzz')
    expect(await screen.findByText('Sin resultados')).toBeInTheDocument()
  })

  it('los filtros de estatus y de zona van al servidor (status y dispatchZoneId)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    const roster = within(section('Choferes'))
    await user.selectOptions(roster.getByLabelText('Estatus'), 'No disponible')
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers').some((c) => c.url.searchParams.getAll('status').join() === 'UNAVAILABLE')).toBe(true))
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Choferes' })).queryByText('Ramón Díaz')).toBeNull())
    await user.selectOptions(roster.getByLabelText('Estatus'), 'Todos')
    await user.selectOptions(roster.getByLabelText('Zona'), 'R-01 · Toa Baja · Bayamón')
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers').some((c) => c.url.searchParams.get('dispatchZoneId') === '1')).toBe(true))
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Choferes' })).queryByText('José Pérez')).toBeNull())
  })

  it('«Mostrar inactivos» trae inactivos y eliminados con su chip; filtrar por «Eliminado» los pide solo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivos' }))
    const rows = await list()
    const inactive = await within(rows).findByRole('button', { name: /Ana Cruz/ })
    expect(within(inactive).getByText('Inactivo')).toBeInTheDocument()
    expect(within(within(rows).getByRole('button', { name: /Libre Eliminado/ })).getByText('Eliminado')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/drivers').some((c) => c.url.searchParams.get('includeInactive') === 'true')).toBe(true)
    // sin el interruptor, elegir el estatus Eliminado pide includeInactive por su cuenta
    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivos' }))
    mock.calls = []
    await user.selectOptions(within(section('Choferes')).getByLabelText('Estatus'), 'Eliminado')
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers').some((c) => c.url.searchParams.get('includeInactive') === 'true' && c.url.searchParams.getAll('status').join() === 'INACTIVE')).toBe(true))
  })

  it('selecciona por la URL (?driver=) y al elegir otra fila cambia la ficha', async () => {
    const user = userEvent.setup()
    wrap(ALL, '/catalog/drivers?driver=d-2')
    expect(await screen.findByDisplayValue('José Pérez')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/drivers/d-1')).toHaveLength(0)
    await user.click(within(await list()).getByRole('button', { name: /Ramón Díaz/ }))
    expect(await screen.findByDisplayValue('Ramón Díaz')).toBeInTheDocument()
  })

  it('sin choferes: mensaje; un error del servidor se muestra tal cual', async () => {
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/drivers' ? [] : undefined)
    const first = wrap(ALL)
    expect(await screen.findByText('No hay choferes todavía — cree el primero con «Nuevo chofer»')).toBeInTheDocument()
    first.unmount()
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/drivers' ? problem(500, 'Error del servidor de prueba', 'internal') : undefined)
    wrap(ALL)
    expect(await screen.findByText('Error del servidor de prueba')).toBeInTheDocument()
  })
})

describe('Choferes — alta', () => {
  it('«Nuevo chofer» manda el código en mayúsculas, el nombre, la zona y el tope; el nuevo queda elegido', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo chofer/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo chofer' })
    await user.type(within(dialog).getByLabelText(/^Código/), 'd-900')
    await user.type(within(dialog).getByLabelText(/^Nombre/), '  Marta Ríos ')
    await user.selectOptions(within(dialog).getByLabelText('Zona'), 'R-02 · Caguas · Aguas Buenas')
    await user.type(within(dialog).getByLabelText('Tope de paradas'), '18')
    await user.type(within(dialog).getByLabelText('Fecha de alta'), '2026-10-01')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers')[0].body).toEqual({ code: 'D-900', fullName: 'Marta Ríos', dispatchZoneId: 2, maxStopsPerRoute: 18, hireDate: '2026-10-01', userId: undefined })
    expect(await screen.findByText('Chofer creado.')).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-new').length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo chofer' })).toBeNull())
  })

  it('validaciones con los mensajes del manual, sin llamar al servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo chofer/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo chofer' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El código del chofer es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('El nombre del chofer es obligatorio.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Código/), 'X'.repeat(31))
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Ana')
    await user.type(within(dialog).getByLabelText('Tope de paradas'), '0')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El código no puede exceder 30 caracteres.')).toBeInTheDocument()
    expect(within(dialog).getByText('El tope de paradas debe ser mayor o igual a 1.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/drivers')).toHaveLength(0)
  })

  it('el código repetido (409) y la zona inactiva (400) salen con el mensaje exacto del servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo chofer/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo chofer' })
    await user.type(within(dialog).getByLabelText(/^Código/), 'D-1')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Ana')
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/drivers' ? problem(409, 'Ya existe un chofer con ese código.', 'conflict') : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Ya existe un chofer con ese código.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Nuevo chofer' })).toBeInTheDocument()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/drivers' ? problem(400, 'La zona de despacho está inactiva.', 'validation', { dispatchZoneId: ['La zona de despacho está inactiva.'] }) : undefined
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect((await within(dialog).findAllByText('La zona de despacho está inactiva.')).length).toBeGreaterThan(0)
  })

  it('«Usuario vinculado» solo se ofrece con admin.users (internos y activos) y viaja como userId', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo chofer/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo chofer' })
    const select = within(dialog).getByLabelText('Usuario vinculado')
    await waitFor(() => expect(within(select).getByRole('option', { name: 'Carlos Rivera · carlos@x.com' })).toBeInTheDocument())
    expect(within(select).queryByRole('option', { name: /Portal Pedro/ })).toBeNull()
    await user.type(within(dialog).getByLabelText(/^Código/), 'D-2')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Ana')
    await user.selectOptions(select, 'Carlos Rivera · carlos@x.com')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers')[0].body).toMatchObject({ userId: 2 })
  })

  it('sin admin.users no hay selector de usuario ni se pide la lista de usuarios', async () => {
    const user = userEvent.setup()
    wrap([...FLEET])
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo chofer/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo chofer' })
    expect(within(dialog).queryByLabelText('Usuario vinculado')).toBeNull()
    expect(callsTo('GET', '/api/v1/users')).toHaveLength(0)
  })
})

describe('Choferes — ficha', () => {
  it('muestra código (fijo), nombre, zona, el «Área» como nombre de la zona, tope y fecha de alta', async () => {
    wrap(ALL)
    const ficha = within(await screen.findByRole('heading', { name: 'Ficha del chofer' }).then((h) => h.closest('section') as HTMLElement))
    expect(await ficha.findByDisplayValue('Ramón Díaz')).toBeInTheDocument()
    expect(ficha.getByLabelText('Código')).toBeDisabled()
    expect(ficha.getByLabelText('Código')).toHaveValue('010')
    expect(ficha.getByLabelText('Zona')).toHaveValue('1')
    expect(ficha.getByTestId('driver-area')).toHaveTextContent('Toa Baja · Bayamón')
    // sin tope propio: rige el de la compañía
    expect(ficha.getByLabelText('Tope de paradas')).toHaveValue(null)
    expect(ficha.getByLabelText('Tope de paradas')).toHaveAttribute('placeholder', '25')
    expect(ficha.getByText('Sin tope propio: rige el de la compañía (25 paradas).')).toBeInTheDocument()
    expect(ficha.getByLabelText('Fecha de alta')).toHaveValue('2024-03-01')
  })

  it('edita: manda en el PATCH solo lo cambiado, con rowVersion, y deja la ficha con el rowVersion nuevo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    const name = await ficha.findByLabelText(/^Nombre/)
    const save = ficha.getByRole('button', { name: 'Guardar cambios' })
    expect(save).toBeDisabled()
    await user.clear(name)
    await user.type(name, '  Ramón D. ')
    await user.type(ficha.getByLabelText('Tope de paradas'), '30')
    expect(save).toBeEnabled()
    await user.click(save)
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-1')[0].body).toEqual({ fullName: 'Ramón D.', maxStopsPerRoute: 30, rowVersion: 'AAA=' })
    expect(await screen.findByText('Cambios guardados.')).toBeInTheDocument()
    await waitFor(() => expect(ficha.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled())
    // la lista se refresca con el nombre nuevo
    expect(await within(await list()).findByText('Ramón D.')).toBeInTheDocument()
  })

  it('cambiar la zona manda el id (el «Área» sigue a la zona elegida); quitarla manda clearZone, no null', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    await ficha.findByDisplayValue('Ramón Díaz')
    await user.selectOptions(ficha.getByLabelText('Zona'), 'R-02 · Caguas · Aguas Buenas')
    expect(ficha.getByTestId('driver-area')).toHaveTextContent('Caguas · Aguas Buenas')
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-1')[0].body).toEqual({ dispatchZoneId: 2, rowVersion: 'AAA=' })

    await waitFor(() => expect(ficha.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled())
    await user.selectOptions(ficha.getByLabelText('Zona'), 'Sin zona')
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-1')).toHaveLength(2))
    expect(callsTo('PATCH', '/api/v1/drivers/d-1')[1].body).toEqual({ clearZone: true, rowVersion: 'ZZZ=' })
    expect(ficha.getByTestId('driver-area')).toHaveTextContent('—')
  })

  it('vaciar el tope propio manda clearMaxStopsPerRoute', async () => {
    const user = userEvent.setup()
    wrap(ALL, '/catalog/drivers?driver=d-2')
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    const stops = await ficha.findByDisplayValue('20')
    await user.clear(stops)
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-2')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-2')[0].body).toEqual({ clearMaxStopsPerRoute: true, rowVersion: 'BBB=' })
  })

  it('validaciones con los mensajes del manual: nombre vacío, tope 0 y fecha de alta que no se puede quitar', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    await user.clear(await ficha.findByLabelText(/^Nombre/))
    await user.type(ficha.getByLabelText('Tope de paradas'), '0')
    await user.clear(ficha.getByLabelText('Fecha de alta'))
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    expect(await ficha.findByText('El nombre del chofer es obligatorio.')).toBeInTheDocument()
    expect(ficha.getByText('El tope de paradas debe ser mayor o igual a 1.')).toBeInTheDocument()
    expect(ficha.getByText('La fecha de alta no se puede quitar; elija otra.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/drivers/d-1')).toHaveLength(0)
  })

  it('409 de rowVersion: el mensaje exacto del servidor, lo escrito se conserva y la ficha se vuelve a leer', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    const name = await ficha.findByLabelText(/^Nombre/)
    await user.type(name, ' Jr.')
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/drivers/d-1' ? problem(409, 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', 'conflict') : undefined
    const before = callsTo('GET', '/api/v1/drivers/d-1').length
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    expect(await ficha.findByText('El registro fue modificado por otro usuario; recargue e intente de nuevo.')).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1').length).toBeGreaterThan(before))
    expect(ficha.getByLabelText(/^Nombre/)).toHaveValue('Ramón Díaz Jr.')
  })

  it('«Activo» (casilla) apaga y enciende sin tocar el estatus; un error sale como aviso', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    const toggle = await ficha.findByRole('switch', { name: 'Activo' })
    expect(toggle).toBeChecked()
    await user.click(toggle)
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/deactivate')).toHaveLength(1))
    expect(await screen.findByText('Chofer desactivado. Para verlo en la lista, active «Mostrar inactivos».')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/drivers/d-1/status')).toHaveLength(0)
    expect(callsTo('DELETE', '/api/v1/drivers/d-1')).toHaveLength(0)
    await waitFor(() => expect(ficha.getByRole('switch', { name: 'Activo' })).not.toBeChecked())
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/drivers/d-1/reactivate' ? problem(409, 'El chofer fue eliminado; no se puede reactivar.', 'conflict') : undefined)
    await user.click(ficha.getByRole('switch', { name: 'Activo' }))
    expect(await screen.findByText('El chofer fue eliminado; no se puede reactivar.')).toBeInTheDocument()
  })

  it('«Disponibilidad» cambia el estatus ACTIVE ↔ UNAVAILABLE por /status', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    const available = await ficha.findByRole('button', { name: 'Disponible' })
    expect(available).toHaveAttribute('aria-pressed', 'true')
    await user.click(ficha.getByRole('button', { name: 'No disponible' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/status')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/status')[0].body).toMatchObject({ toCode: 'UNAVAILABLE' })
    expect(await screen.findByText('El chofer quedó no disponible.')).toBeInTheDocument()
    await waitFor(() => expect(ficha.getByRole('button', { name: 'No disponible' })).toHaveAttribute('aria-pressed', 'true'))
    // la fila de la lista cambia de chip
    expect(within(within(await list()).getByRole('button', { name: /Ramón Díaz/ })).getByText('No disponible')).toBeInTheDocument()
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/drivers/d-1/status' ? problem(422, 'El registro ya está en ese estatus.', 'status_rule') : undefined)
    await user.click(ficha.getByRole('button', { name: 'Disponible' }))
    expect(await screen.findByText('El registro ya está en ese estatus.')).toBeInTheDocument()
  })

  it('«Eliminar chofer» explica los efectos, pide un comentario opcional y manda DELETE con {comment}', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    await user.click(await ficha.findByRole('button', { name: /Eliminar chofer/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Eliminar al chofer Ramón Díaz' })
    for (const effect of ['se desvincula su usuario;', 'sus dispositivos quedan inactivos;', 'pierde su zona de despacho;', 'sus tarifas abiertas se cierran hoy;', 'sus viajes pagados se conservan para pagarle lo pendiente.']) {
      expect(within(dialog).getByText(effect)).toBeInTheDocument()
    }
    expect(within(dialog).getByText(/No se puede deshacer/)).toBeInTheDocument()
    expect(callsTo('DELETE', '/api/v1/drivers/d-1')).toHaveLength(0)
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), 'Renunció')
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar chofer' }))
    await waitFor(() => expect(callsTo('DELETE', '/api/v1/drivers/d-1')).toHaveLength(1))
    expect(callsTo('DELETE', '/api/v1/drivers/d-1')[0].body).toEqual({ comment: 'Renunció' })
    expect(await screen.findByText('Chofer eliminado.')).toBeInTheDocument()
    // la ficha queda de solo lectura con el aviso
    expect(await screen.findByText('El chofer fue eliminado; solo se consulta su historial.')).toBeInTheDocument()
    expect(ficha.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    expect(ficha.queryByRole('button', { name: /Eliminar chofer/ })).toBeNull()
  })

  it('si el servidor rechaza la baja, su mensaje queda dentro del diálogo, que sigue abierto', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    mock.override = ({ method, url }) => (method === 'DELETE' && url.pathname === '/api/v1/drivers/d-1' ? problem(409, 'El chofer ya fue eliminado.', 'conflict') : undefined)
    await user.click(await ficha.findByRole('button', { name: /Eliminar chofer/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Eliminar al chofer Ramón Díaz' })
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar chofer' }))
    expect(await within(dialog).findByText('El chofer ya fue eliminado.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Eliminar al chofer Ramón Díaz' })).toBeInTheDocument()
  })

  it('un chofer eliminado se ve en solo lectura: campos apagados, sin guardar, sin eliminar, con «Eliminado»', async () => {
    wrap(ALL, '/catalog/drivers?driver=d-4')
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    expect(await ficha.findByText('El chofer fue eliminado; solo se consulta su historial.')).toBeInTheDocument()
    expect(ficha.getByLabelText(/^Nombre/)).toBeDisabled()
    expect(ficha.getByLabelText('Zona')).toBeDisabled()
    expect(ficha.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    expect(ficha.queryByRole('button', { name: /Eliminar chofer/ })).toBeNull()
    expect(ficha.getByRole('switch', { name: 'Activo' })).toBeDisabled()
    expect(ficha.getByText('Eliminado')).toBeInTheDocument()
    expect(ficha.queryByRole('button', { name: 'No disponible' })).toBeNull()
  })

  it('con admin.users: vincula, cambia y quita el usuario (userId / clearUser); sin él se ve de solo lectura', async () => {
    const user = userEvent.setup()
    wrap(ALL, '/catalog/drivers?driver=d-2')
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    const select = await ficha.findByLabelText('Usuario vinculado')
    expect(select).toHaveValue('5')
    await user.selectOptions(select, 'Carlos Rivera · carlos@x.com')
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-2')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-2')[0].body).toEqual({ userId: 2, rowVersion: 'BBB=' })
    await waitFor(() => expect(ficha.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled())
    await user.selectOptions(ficha.getByLabelText('Usuario vinculado'), 'Sin usuario')
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-2')).toHaveLength(2))
    expect(callsTo('PATCH', '/api/v1/drivers/d-2')[1].body).toEqual({ clearUser: true, rowVersion: 'ZZZ=' })
  })

  it('sin admin.users el usuario vinculado es texto, no se pide la lista de usuarios y el PATCH no lo toca', async () => {
    const user = userEvent.setup()
    wrap([...FLEET], '/catalog/drivers?driver=d-2')
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    expect(await ficha.findByText('Ana Cruz · ana@x.com')).toBeInTheDocument()
    expect(ficha.queryByLabelText('Usuario vinculado')).toBeNull()
    expect(callsTo('GET', '/api/v1/users')).toHaveLength(0)
    await user.type(ficha.getByLabelText(/^Nombre/), '!')
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-2')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-2')[0].body).not.toHaveProperty('userId')
    expect(callsTo('PATCH', '/api/v1/drivers/d-2')[0].body).not.toHaveProperty('clearUser')
  })

  it('el servidor puede rechazar el usuario (409 «ya vinculado») y el mensaje queda bajo el aviso del formulario', async () => {
    const user = userEvent.setup()
    wrap(ALL, '/catalog/drivers?driver=d-2')
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    await user.selectOptions(await ficha.findByLabelText('Usuario vinculado'), 'Carlos Rivera · carlos@x.com')
    mock.override = ({ method, url }) => (method === 'PATCH' && url.pathname === '/api/v1/drivers/d-2' ? problem(409, 'El usuario ya está vinculado a otro chofer.', 'conflict') : undefined)
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    expect(await ficha.findByText('El usuario ya está vinculado a otro chofer.')).toBeInTheDocument()
  })

  it('sin fleet.manage: nada se escribe (sin Nuevo chofer, campos apagados, sin eliminar ni guardar)', async () => {
    wrap(['fleet.view'])
    await list()
    expect(screen.queryByRole('button', { name: /Nuevo chofer/ })).toBeNull()
    const ficha = within((await screen.findByRole('heading', { name: 'Ficha del chofer' })).closest('section') as HTMLElement)
    expect(await ficha.findByLabelText(/^Nombre/)).toBeDisabled()
    expect(ficha.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    expect(ficha.queryByRole('button', { name: /Eliminar chofer/ })).toBeNull()
    expect(ficha.getByRole('switch', { name: 'Activo' })).toBeDisabled()
    expect(ficha.getByRole('button', { name: 'Disponible' })).toBeDisabled()
  })
})
