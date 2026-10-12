// Lote F-D2 — Flota, pestaña Vehículos: pantalla (cabecera, tarjetas, pestañas), lista (contador, filas, buscador y filtros que van al
// servidor, inactivos, selección por la URL), alta (request, validaciones del manual, errores del servidor), ficha (PATCH mínimo con
// rowVersion, 409, las tres cosas distintas: Activo, Disponibilidad y Dar de baja definitiva, vehículo dado de baja de solo lectura) y permisos.
// Sobre un fetch simulado con estado (`fleetMock`).
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, MANAGE, mock, problem, resetKit, VIEW, wrap } from './fleetTestKit'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./fleetMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const section = (name: string) => screen.getByRole('heading', { level: 2, name }).closest('section') as HTMLElement
const list = () => screen.findByRole('list', { name: 'Vehículos' })
const ficha = async () => within(await screen.findByRole('heading', { level: 2, name: 'Ficha del vehículo' }).then((h) => h.closest('section') as HTMLElement))

describe('Flota — pantalla', () => {
  it('cabecera, nota, las cuatro tarjetas resumen de la maqueta y las siete pestañas', async () => {
    wrap(ALL)
    expect(screen.getByRole('heading', { level: 1, name: 'Flota y mantenimiento' })).toBeInTheDocument()
    expect(screen.getByText(/Este módulo es identidad y flota, no compensación/)).toBeInTheDocument()
    const vehicles = await screen.findByRole('button', { name: /Vehículos activos/ })
    await waitFor(() => expect(vehicles).toHaveTextContent('2'))
    const due = screen.getByRole('button', { name: /Documentos por vencer \(≤ 30 d\)/ })
    await waitFor(() => expect(due).toHaveTextContent('3'))
    const overdue = screen.getByRole('button', { name: /Mantenimiento preventivo \(vencidos\)/ })
    await waitFor(() => expect(overdue).toHaveTextContent('1'))
    const open = screen.getByRole('button', { name: /Órdenes de trabajo \(abiertas\)/ })
    await waitFor(() => expect(open).toHaveTextContent('2'))
    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Vehículos', 'Mantenimiento', 'Órdenes de trabajo', 'Combustible', 'Documentos por vencer', 'Zonas de despacho', 'Disponibilidad'])
    expect(screen.getByRole('tab', { name: 'Vehículos' })).toHaveAttribute('aria-selected', 'true')
    expect(callsTo('GET', '/api/v1/fleet/expiring-documents')[0].url.searchParams.get('withinDays')).toBe('30')
  })

  it('las pestañas se abren desde la URL (?tab=), una pestaña desconocida abre Vehículos y la tarjeta de documentos lleva a su pestaña', async () => {
    const user = userEvent.setup()
    wrap(ALL, '/catalog/fleet?tab=zones')
    expect(await screen.findByRole('heading', { level: 2, name: 'Zonas de despacho' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Zonas de despacho' })).toHaveAttribute('aria-selected', 'true')
    await user.click(screen.getByRole('button', { name: /Documentos por vencer \(≤ 30 d\)/ }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Documentos por vencer' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Vehículos' }))
    expect(await list()).toBeInTheDocument()
  })

  it('una pestaña desconocida abre la primera', async () => {
    wrap(ALL, '/catalog/fleet?tab=payroll')
    expect(await list()).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Vehículos' })).toHaveAttribute('aria-selected', 'true')
  })
})

describe('Flota — lista de vehículos', () => {
  it('contador, código, placa, tipo y chip; solo activos por omisión; la ficha del primero se abre sola', async () => {
    wrap(ALL)
    const rows = await list()
    expect(callsTo('GET', '/api/v1/vehicles')[0].url.searchParams.get('includeInactive')).toBe('false')
    const r1 = within(rows).getByRole('button', { name: /HGR-204/ })
    expect(within(r1).getByText('V-01')).toBeInTheDocument()
    expect(within(r1).getByText('Disponible')).toBeInTheDocument()
    expect(within(r1).getByText('Van · Propio · Gasolina')).toBeInTheDocument()
    const r2 = within(rows).getByRole('button', { name: /HGP-118/ })
    expect(within(r2).getByText('En mantenimiento')).toBeInTheDocument()
    expect(within(rows).queryByText('V-03')).toBeNull()
    expect(within(section('Vehículos')).getByText('2')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 2, name: 'Ficha del vehículo' })).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/vehicles/v-1').length).toBeGreaterThan(0))
  })

  it('el buscador manda `search` al servidor (con espera entre teclas)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.type(screen.getByRole('searchbox'), 'hgp')
    await waitFor(() => expect(callsTo('GET', '/api/v1/vehicles').some((c) => c.url.searchParams.get('search') === 'hgp')).toBe(true))
    const rows = await list()
    await waitFor(() => expect(within(rows).queryByText('V-01')).toBeNull())
    expect(within(rows).getByText('V-02')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'zzz')
    expect(await screen.findByText('Sin resultados')).toBeInTheDocument()
  })

  it('los filtros Tipo, Propiedad, Combustible y Estatus van al servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    const roster = within(section('Vehículos'))
    const sent = (name: string, value: string) => callsTo('GET', '/api/v1/vehicles').some((c) => c.url.searchParams.getAll(name).join() === value)
    await user.selectOptions(roster.getByLabelText('Tipo'), 'Camión')
    await waitFor(() => expect(sent('vehicleType', 'TRUCK')).toBe(true))
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Vehículos' })).queryByText('V-01')).toBeNull())
    await user.selectOptions(roster.getByLabelText('Tipo'), 'Todos')
    await user.selectOptions(roster.getByLabelText('Propiedad'), 'Arrendado')
    await waitFor(() => expect(sent('ownership', 'LEASED')).toBe(true))
    await user.selectOptions(roster.getByLabelText('Propiedad'), 'Todas')
    await user.selectOptions(roster.getByLabelText('Combustible'), 'Gasolina')
    await waitFor(() => expect(sent('fuelType', 'GASOLINE')).toBe(true))
    await user.selectOptions(roster.getByLabelText('Combustible'), 'Todos')
    await user.selectOptions(roster.getByLabelText('Estatus'), 'En mantenimiento')
    await waitFor(() => expect(sent('status', 'MAINTENANCE')).toBe(true))
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Vehículos' })).queryByText('V-01')).toBeNull())
  })

  it('«Mostrar inactivos» trae inactivos y dados de baja con su chip; filtrar por «Baja definitiva» los pide solo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivos' }))
    const rows = await list()
    const inactive = await within(rows).findByRole('button', { name: /HFS-902/ })
    expect(within(inactive).getByText('Inactivo')).toBeInTheDocument()
    expect(within(within(rows).getByRole('button', { name: /OLD-001/ })).getByText('Baja definitiva')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/vehicles').some((c) => c.url.searchParams.get('includeInactive') === 'true')).toBe(true)
    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivos' }))
    mock.calls = []
    await user.selectOptions(within(section('Vehículos')).getByLabelText('Estatus'), 'Baja definitiva')
    await waitFor(() => expect(callsTo('GET', '/api/v1/vehicles').some((c) => c.url.searchParams.get('includeInactive') === 'true' && c.url.searchParams.getAll('status').join() === 'INACTIVE')).toBe(true))
  })

  it('selecciona por la URL (?vehicle=) y al elegir otra fila cambia la ficha', async () => {
    const user = userEvent.setup()
    wrap(ALL, '/catalog/fleet?vehicle=v-2')
    expect(await screen.findByDisplayValue('HGP-118')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/vehicles/v-1')).toHaveLength(0)
    await user.click(within(await list()).getByRole('button', { name: /HGR-204/ }))
    expect(await screen.findByDisplayValue('HGR-204')).toBeInTheDocument()
  })

  it('sin vehículos: mensaje; un error del servidor se muestra tal cual', async () => {
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/vehicles' ? [] : undefined)
    const first = wrap(ALL)
    expect(await screen.findByText('No hay vehículos todavía — cree el primero con «Nuevo vehículo»')).toBeInTheDocument()
    first.unmount()
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/vehicles' ? problem(500, 'Error del servidor de prueba', 'internal') : undefined)
    wrap(ALL)
    expect((await screen.findAllByText('Error del servidor de prueba')).length).toBeGreaterThan(0)
  })
})

describe('Flota — alta de vehículo', () => {
  it('«Nuevo vehículo» manda el código en mayúsculas, los catálogos y las capacidades; el nuevo queda elegido', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo vehículo/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo vehículo' })
    await user.type(within(dialog).getByLabelText(/^Código/), 'v-900')
    await user.type(within(dialog).getByLabelText('Placa'), '  AB-123 ')
    await user.selectOptions(within(dialog).getByLabelText('Tipo'), 'Camión')
    await user.selectOptions(within(dialog).getByLabelText('Propiedad'), 'Arrendado')
    await user.selectOptions(within(dialog).getByLabelText('Combustible'), 'Diésel')
    await user.type(within(dialog).getByLabelText('Año del modelo'), '2024')
    await user.type(within(dialog).getByLabelText('Marca'), 'Isuzu')
    await user.type(within(dialog).getByLabelText('VIN'), ' jalc4b 16 ')
    await user.clear(within(dialog).getByLabelText('Odómetro (km)'))
    await user.type(within(dialog).getByLabelText('Odómetro (km)'), '1500.5')
    await user.type(within(dialog).getByLabelText('Tope de paradas'), '30')
    await user.type(within(dialog).getByLabelText('Peso máx (kg)'), '4500')
    await user.type(within(dialog).getByLabelText('Volumen máx (m³)'), '28.5')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/vehicles')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/vehicles')[0].body).toEqual({
      code: 'V-900',
      plateNumber: 'AB-123',
      vehicleType: 'TRUCK',
      ownership: 'LEASED',
      fuelType: 'DIESEL',
      make: 'Isuzu',
      model: null,
      modelYear: 2024,
      vin: 'JALC4B16',
      maxWeightKg: 4500,
      maxVolumeM3: 28.5,
      maxStops: 30,
      currentOdometerKm: 1500.5,
    })
    expect(await screen.findByText('Vehículo creado.')).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/vehicles/v-new').length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo vehículo' })).toBeNull())
  })

  it('no ofrece el almacén base de la maqueta ni campos de archivos', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo vehículo/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo vehículo' })
    expect(within(dialog).queryByLabelText(/Almacén base/)).toBeNull()
    expect(dialog.querySelector('input[type="file"]')).toBeNull()
  })

  it('validaciones con los mensajes del manual, sin llamar al servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo vehículo/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo vehículo' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El código del vehículo es obligatorio.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Código/), 'X'.repeat(31))
    await user.type(within(dialog).getByLabelText('Año del modelo'), '1899')
    await user.type(within(dialog).getByLabelText('Tope de paradas'), '0')
    await user.type(within(dialog).getByLabelText('Peso máx (kg)'), '-5')
    await user.type(within(dialog).getByLabelText('Volumen máx (m³)'), '1.23456')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El código no puede exceder 30 caracteres.')).toBeInTheDocument()
    expect(within(dialog).getByText(/^El año del modelo debe estar entre 1900 y \d{4}\.$/)).toBeInTheDocument()
    expect(within(dialog).getByText('El tope de paradas debe ser mayor o igual a 1.')).toBeInTheDocument()
    expect(within(dialog).getByText('La capacidad no puede ser negativa.')).toBeInTheDocument()
    expect(within(dialog).getByText('El valor admite como máximo 4 decimales y debe ser menor que 100,000,000.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/vehicles')).toHaveLength(0)
  })

  it('el código repetido (409) y el VIN repetido (400 por campo) salen con el mensaje exacto del servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await list()
    await user.click(screen.getByRole('button', { name: /Nuevo vehículo/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo vehículo' })
    await user.type(within(dialog).getByLabelText(/^Código/), 'V-01')
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/vehicles' ? problem(409, 'Ya existe un vehículo con ese código.', 'conflict') : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Ya existe un vehículo con ese código.')).toBeInTheDocument()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/vehicles' ? problem(400, 'Hay errores de validación.', 'validation', { vin: ['El VIN admite como máximo 40 caracteres.'] }) : undefined
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El VIN admite como máximo 40 caracteres.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Nuevo vehículo' })).toBeInTheDocument()
  })
})

describe('Flota — ficha del vehículo', () => {
  it('muestra los datos, el código fijo y deshabilitado, y «Guardar cambios» apagado hasta que algo cambia', async () => {
    wrap(ALL)
    const f = await ficha()
    expect(f.getByLabelText('Código')).toHaveValue('V-01')
    expect(f.getByLabelText('Código')).toBeDisabled()
    expect(f.getByLabelText('Placa')).toHaveValue('HGR-204')
    expect(f.getByLabelText('Tipo')).toHaveValue('VAN')
    expect(f.getByLabelText('Propiedad')).toHaveValue('OWNED')
    expect(f.getByLabelText('Combustible')).toHaveValue('GASOLINE')
    expect(f.getByLabelText('Marca')).toHaveValue('Ford')
    expect(f.getByLabelText('Año del modelo')).toHaveValue(2022)
    expect(f.getByLabelText('VIN')).toHaveValue('1FTBW3XM5JKA12345')
    expect(f.getByLabelText('Odómetro (km)')).toHaveValue(48210)
    expect(f.getByLabelText('Peso máx (kg)')).toHaveValue(1200)
    expect(f.getByLabelText('Volumen máx (m³)')).toHaveValue(9)
    expect(f.getByLabelText('Tope de paradas')).toHaveValue(40)
    expect(f.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()
  })

  it('edita con un solo «Guardar cambios»: PATCH solo de lo cambiado y el rowVersion; el botón vuelve a apagarse', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const f = await ficha()
    await user.clear(f.getByLabelText('Placa'))
    await user.type(f.getByLabelText('Placa'), '  NEW-1 ')
    await user.selectOptions(f.getByLabelText('Combustible'), 'Diésel')
    await user.clear(f.getByLabelText('Odómetro (km)'))
    await user.type(f.getByLabelText('Odómetro (km)'), '50000')
    await user.click(f.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/vehicles/v-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/vehicles/v-1')[0].body).toEqual({ rowVersion: 'AAA=', plateNumber: 'NEW-1', fuelType: 'DIESEL', currentOdometerKm: 50000 })
    expect(await screen.findByText('Cambios guardados.')).toBeInTheDocument()
    await waitFor(() => expect(f.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled())
    expect(f.getByLabelText('Placa')).toHaveValue('NEW-1')
  })

  it('un texto vacío se manda como cadena vacía (borra); un número guardado no se puede vaciar', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const f = await ficha()
    await user.clear(f.getByLabelText('Marca'))
    await user.clear(f.getByLabelText('Tope de paradas'))
    await user.click(f.getByRole('button', { name: 'Guardar cambios' }))
    expect(await f.findByText('Este valor no se puede dejar vacío una vez guardado; escriba otro.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/vehicles/v-1')).toHaveLength(0)
    await user.type(f.getByLabelText('Tope de paradas'), '35')
    await user.click(f.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/vehicles/v-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/vehicles/v-1')[0].body).toEqual({ rowVersion: 'AAA=', make: '', maxStops: 35 })
  })

  it('409 de rowVersion: muestra el mensaje, relee la ficha y conserva lo escrito; el siguiente intento usa el rowVersion nuevo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const f = await ficha()
    await user.clear(f.getByLabelText('Placa'))
    await user.type(f.getByLabelText('Placa'), 'OTRA-1')
    const before = callsTo('GET', '/api/v1/vehicles/v-1').length
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/vehicles/v-1' ? problem(409, 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', 'conflict') : undefined
    await user.click(f.getByRole('button', { name: 'Guardar cambios' }))
    expect(await f.findByText('El registro fue modificado por otro usuario; recargue e intente de nuevo.')).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/vehicles/v-1').length).toBeGreaterThan(before))
    expect(f.getByLabelText('Placa')).toHaveValue('OTRA-1')
    // recargar la ficha no es una edición: el aviso sigue a la vista; escribir algo sí lo quita
    expect(f.getByText('El registro fue modificado por otro usuario; recargue e intente de nuevo.')).toBeInTheDocument()
    await user.type(f.getByLabelText('Placa'), 'X')
    expect(f.queryByText('El registro fue modificado por otro usuario; recargue e intente de nuevo.')).toBeNull()
  })

  it('el error del servidor por campo (odómetro) sale bajo el campo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const f = await ficha()
    await user.clear(f.getByLabelText('Odómetro (km)'))
    await user.type(f.getByLabelText('Odómetro (km)'), '10')
    const message = 'El odómetro no puede ser menor que la última lectura registrada (48000 km el 2026-10-01).'
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/vehicles/v-1' ? problem(400, 'Hay errores de validación.', 'validation', { currentOdometerKm: [message] }) : undefined
    await user.click(f.getByRole('button', { name: 'Guardar cambios' }))
    expect(await f.findByText(message)).toBeInTheDocument()
  })

  it('Disponibilidad (Disponible / En mantenimiento) manda POST /status y muestra el aviso; un error del servidor sale como aviso', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const f = await ficha()
    await user.click(f.getByRole('button', { name: 'En mantenimiento' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/vehicles/v-1/status')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/vehicles/v-1/status')[0].body).toEqual({ toCode: 'MAINTENANCE', comment: null })
    expect(await screen.findByText('El vehículo quedó en mantenimiento.')).toBeInTheDocument()
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Vehículos' })).getAllByText('En mantenimiento').length).toBe(2))
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/vehicles/v-1/status' ? problem(422, 'No se puede pasar a ese estatus.', 'status_rule') : undefined)
    await user.click(f.getByRole('button', { name: 'Disponible' }))
    expect(await screen.findByText('No se puede pasar a ese estatus.')).toBeInTheDocument()
  })

  it('la casilla «Activo» desactiva y reactiva (deactivate / reactivate) y responde al instante; si falla vuelve atrás con el mensaje', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const f = await ficha()
    const sw = f.getByRole('switch', { name: 'Activo' })
    expect(sw).toBeChecked()
    await user.click(sw)
    await waitFor(() => expect(callsTo('POST', '/api/v1/vehicles/v-1/deactivate')).toHaveLength(1))
    expect(await screen.findByText(/Vehículo desactivado/)).toBeInTheDocument()
    await waitFor(() => expect(f.getByRole('switch', { name: 'Activo' })).not.toBeChecked())
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/vehicles/v-1/reactivate' ? problem(409, 'Ya existe un vehículo activo con ese VIN.', 'conflict') : undefined
    await user.click(f.getByRole('switch', { name: 'Activo' }))
    expect(await screen.findByText('Ya existe un vehículo activo con ese VIN.')).toBeInTheDocument()
    await waitFor(() => expect(f.getByRole('switch', { name: 'Activo' })).not.toBeChecked())
  })

  it('«Dar de baja definitiva» explica los efectos, pide confirmar y manda INACTIVE con el comentario', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const f = await ficha()
    await user.click(f.getByRole('button', { name: /Dar de baja definitiva/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Dar de baja al vehículo V-01' })
    expect(dialog).toHaveTextContent('Es una baja definitiva.')
    expect(dialog).toHaveTextContent('sale de la disponibilidad para despacho;')
    expect(dialog).toHaveTextContent('No se puede deshacer')
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), 'Vendido')
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja definitiva' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/vehicles/v-1/status')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/vehicles/v-1/status')[0].body).toEqual({ toCode: 'INACTIVE', comment: 'Vendido' })
    expect(await screen.findByText('Vehículo dado de baja.')).toBeInTheDocument()
    expect(await screen.findByText('El vehículo fue dado de baja; solo se consulta su historial.')).toBeInTheDocument()
    expect(f.queryByRole('button', { name: /Dar de baja definitiva/ })).toBeNull()
    expect(f.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
  })

  it('si la baja falla, el mensaje del servidor queda dentro del diálogo y no se cierra', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const f = await ficha()
    await user.click(f.getByRole('button', { name: /Dar de baja definitiva/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Dar de baja al vehículo V-01' })
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/vehicles/v-1/status' ? problem(409, 'Error de prueba de la baja.', 'conflict') : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja definitiva' }))
    expect(await within(dialog).findByText('Error de prueba de la baja.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Dar de baja al vehículo V-01' })).toBeInTheDocument()
  })

  it('un vehículo dado de baja solo se consulta: sin guardar, sin baja, casilla y disponibilidad apagadas', async () => {
    wrap(ALL, '/catalog/fleet?vehicle=v-4')
    const f = await ficha()
    expect(await screen.findByText('El vehículo fue dado de baja; solo se consulta su historial.')).toBeInTheDocument()
    expect(f.getByLabelText('Placa')).toBeDisabled()
    expect(f.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    expect(f.queryByRole('button', { name: /Dar de baja definitiva/ })).toBeNull()
    expect(f.getByRole('switch', { name: 'Activo' })).toBeDisabled()
    expect(f.getByText('Baja definitiva')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Agregar documento' })).toBeNull()
  })

  it('vehículo inexistente: mensaje del servidor', async () => {
    wrap(ALL, '/catalog/fleet?vehicle=zzz')
    expect(await screen.findByText('Vehículo no encontrado.')).toBeInTheDocument()
  })
})

describe('Flota — permisos', () => {
  it('con fleet.manage se ve «Nuevo vehículo»; solo con fleet.view no se pinta ninguna acción y la ficha es de lectura', async () => {
    const manage = wrap(MANAGE)
    expect(await screen.findByRole('button', { name: /Nuevo vehículo/ })).toBeInTheDocument()
    manage.unmount()
    wrap(VIEW)
    const f = await ficha()
    expect(screen.queryByRole('button', { name: /Nuevo vehículo/ })).toBeNull()
    expect(f.getByLabelText('Placa')).toBeDisabled()
    expect(f.getByLabelText('Placa')).toHaveValue('HGR-204')
    expect(f.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    expect(f.queryByRole('button', { name: /Dar de baja definitiva/ })).toBeNull()
    expect(f.getByRole('switch', { name: 'Activo' })).toBeDisabled()
    expect(f.getByRole('button', { name: 'En mantenimiento' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Agregar documento' })).toBeNull()
  })
})
