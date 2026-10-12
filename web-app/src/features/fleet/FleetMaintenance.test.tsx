// Lote F-D3 — Flota, pestaña Mantenimiento: panel «Mantenimiento preventivo» (estado por vehículo, filtros que van al servidor, buscador,
// crear la orden de trabajo del programa) y «Programas de mantenimiento» (alta, edición mínima, desactivar con confirmación, reactivar,
// errores del servidor) con y sin `fleet.maintenance`. Sobre el fetch simulado con estado de `fleetMock`.
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, mock, problem, resetKit, VIEW, wrap } from './fleetTestKit'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./fleetMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const open = (permissions = ALL) => wrap(permissions, '/catalog/fleet?tab=maintenance')
const panel = async (name: string) => within((await screen.findByRole('heading', { level: 2, name })).closest('section') as HTMLElement)
const rowOf = (scope: ReturnType<typeof within>, text: string | RegExp) => scope.getByText(text).closest('tr') as HTMLElement

describe('Mantenimiento — panel «Mantenimiento preventivo»', () => {
  it('muestra Vehículo (placa y código), Programa, Base, Próximo y Estado de cada programa por vehículo', async () => {
    open()
    const due = await panel('Mantenimiento preventivo')
    await due.findByText('Cambio de aceite')
    const aceite = rowOf(due, 'Cambio de aceite')
    expect(within(aceite).getByText('HGR-204')).toBeInTheDocument()
    expect(within(aceite).getByText('V-01')).toBeInTheDocument()
    expect(within(aceite).getByText('Kilometraje')).toBeInTheDocument()
    expect(within(aceite).getByText('50,500 km')).toBeInTheDocument()
    expect(within(aceite).getByText('Faltan 2,290 km')).toBeInTheDocument()
    expect(within(aceite).getByText('Al día')).toBeInTheDocument()
    const frenos = rowOf(due, 'Frenos')
    expect(within(frenos).getByText('Ambos')).toBeInTheDocument()
    expect(within(frenos).getByText('48,500 km · 10/19/2026')).toBeInTheDocument()
    expect(within(frenos).getByText('Faltan 290 km · Faltan 7 días')).toBeInTheDocument()
    expect(within(frenos).getByText('Por vencer')).toBeInTheDocument()
    const revision = within(due.getAllByText('Revisión general')[0].closest('tr') as HTMLElement)
    expect(revision.getByText('HGP-118')).toBeInTheDocument()
    expect(revision.getByText('Tiempo')).toBeInTheDocument()
    expect(revision.getByText('Vencido hace 10 días')).toBeInTheDocument()
    expect(revision.getByText('Vencido')).toBeInTheDocument()
    expect(within(due.getByRole('table')).getByText('Sin historial')).toBeInTheDocument()
  })

  it('lo más urgente sale primero y el contador sigue lo que se ve', async () => {
    open()
    const due = await panel('Mantenimiento preventivo')
    await due.findByText('Cambio de aceite')
    const states = due.getAllByRole('row').slice(1).map((r) => within(r).queryByText(/^(Al día|Por vencer|Vencido|Sin historial)$/)?.textContent)
    expect(states).toEqual(['Vencido', 'Por vencer', 'Sin historial', 'Al día'])
    expect(due.getByText('4')).toBeInTheDocument()
  })

  it('el filtro de Estado y el de Vehículo van al servidor', async () => {
    const user = userEvent.setup()
    open()
    const due = await panel('Mantenimiento preventivo')
    await due.findByText('Cambio de aceite')
    await user.selectOptions(due.getByLabelText('Estado'), 'Vencido')
    await waitFor(() => expect(callsTo('GET', '/api/v1/maintenance-schedules/due').some((c) => c.url.searchParams.getAll('status').join() === 'OVERDUE')).toBe(true))
    await waitFor(() => expect(due.queryByText('Cambio de aceite')).toBeNull())
    expect(due.getAllByText('Revisión general')).toHaveLength(1)
    await user.selectOptions(due.getByLabelText('Estado'), 'Todos')
    await user.selectOptions(due.getByLabelText('Vehículo'), 'V-01 · HGR-204')
    await waitFor(() => expect(callsTo('GET', '/api/v1/maintenance-schedules/due').some((c) => c.url.searchParams.get('vehiclePublicId') === 'v-1')).toBe(true))
    await waitFor(() => expect(within(due.getByRole('table')).queryByText('Sin historial')).toBeNull())
    expect(due.getByText('Frenos')).toBeInTheDocument()
  })

  it('el buscador filtra por placa, programa o estado, sin acentos ni mayúsculas', async () => {
    const user = userEvent.setup()
    open()
    const due = await panel('Mantenimiento preventivo')
    await due.findByText('Cambio de aceite')
    await user.type(due.getByRole('searchbox'), 'REVISION')
    await waitFor(() => expect(due.queryByText('Cambio de aceite')).toBeNull())
    expect(due.getAllByText('Revisión general')).toHaveLength(2)
    await user.clear(due.getByRole('searchbox'))
    await user.type(due.getByRole('searchbox'), 'hgr-204')
    await waitFor(() => expect(due.queryByText('Revisión general')).toBeNull())
    expect(due.getByText('Frenos')).toBeInTheDocument()
    await user.clear(due.getByRole('searchbox'))
    await user.type(due.getByRole('searchbox'), 'zzz')
    expect(await due.findByText('Sin resultados')).toBeInTheDocument()
  })

  it('«Crear orden de trabajo» abre el alta con el vehículo y el programa elegidos y la manda como preventiva', async () => {
    const user = userEvent.setup()
    open()
    const due = await panel('Mantenimiento preventivo')
    await due.findByText('Cambio de aceite')
    const revision = within(due.getAllByText('Revisión general')[0].closest('tr') as HTMLElement)
    await user.click(revision.getByRole('button', { name: 'Crear orden de trabajo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden de trabajo' })
    await waitFor(() => expect(within(dialog).getByLabelText(/^Vehículo/)).toHaveValue('v-2'))
    await waitFor(() => expect(within(dialog).getByLabelText('Programa preventivo')).toHaveValue('2'))
    await waitFor(() => expect(within(dialog).getByLabelText(/^Tipo de mantenimiento/)).toHaveValue('PREVENTIVE'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-work-orders')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/maintenance-work-orders')[0].body).toEqual({ vehiclePublicId: 'v-2', maintenanceType: 'PREVENTIVE', scheduleId: 2 })
    expect(await screen.findByText('Orden OT-00005 creada.')).toBeInTheDocument()
  })

  it('sin fleet.maintenance no se pinta «Crear orden de trabajo»', async () => {
    open(VIEW)
    const due = await panel('Mantenimiento preventivo')
    await due.findByText('Cambio de aceite')
    expect(screen.queryByRole('button', { name: 'Crear orden de trabajo' })).toBeNull()
  })

  it('un error del servidor al leer se muestra con su mensaje', async () => {
    mock.override = (call) => (call.url.pathname === '/api/v1/maintenance-schedules/due' ? problem(400, "Estatus desconocido: 'X'.", 'validation') : undefined)
    open()
    expect(await screen.findByText("Estatus desconocido: 'X'.")).toBeInTheDocument()
  })
})

describe('Mantenimiento — programas', () => {
  it('lista los programas activos; los inactivos solo con «Mostrar inactivos»', async () => {
    const user = userEvent.setup()
    open()
    const programs = await panel('Programas de mantenimiento')
    await programs.findByText('Cambio de aceite')
    const aceite = rowOf(programs, 'Cambio de aceite')
    expect(within(aceite).getByText('HGR-204 · V-01')).toBeInTheDocument()
    expect(within(aceite).getByText('Kilometraje')).toBeInTheDocument()
    expect(within(aceite).getByText('5,000 km')).toBeInTheDocument()
    expect(within(aceite).getByText('45,500 km')).toBeInTheDocument()
    expect(within(aceite).getByText('Activo')).toBeInTheDocument()
    const revision = rowOf(programs, 'Revisión general')
    expect(within(revision).getByText('Tipo: Camión')).toBeInTheDocument()
    expect(within(revision).getByText('90 días')).toBeInTheDocument()
    expect(programs.queryByText('Rotación de gomas')).toBeNull()
    expect(callsTo('GET', '/api/v1/maintenance-schedules')[0].url.searchParams.get('includeInactive')).toBe('false')
    await user.click(programs.getByRole('switch', { name: 'Mostrar inactivos' }))
    expect(within(rowOf(await panel('Programas de mantenimiento'), 'Rotación de gomas')).getByText('Inactivo')).toBeInTheDocument()
  })

  it('«Nuevo programa»: valida con los mensajes del manual y manda la alta por vehículo', async () => {
    const user = userEvent.setup()
    open()
    const programs = await panel('Programas de mantenimiento')
    await user.click(programs.getByRole('button', { name: /Nuevo programa/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo programa' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El nombre del programa es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el vehículo o el tipo de vehículo del programa, no ambos.')).toBeInTheDocument()
    expect(within(dialog).getByText('Un programa por kilometraje exige un intervalo en km mayor que 0.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/maintenance-schedules')).toHaveLength(0)
    await user.type(within(dialog).getByLabelText(/^Nombre/), ' Cambio de bujías ')
    await user.selectOptions(within(dialog).getByLabelText(/^Vehículo/), 'V-02 · HGP-118')
    await user.type(within(dialog).getByLabelText('Cada (km)'), '8000')
    await user.type(within(dialog).getByLabelText('Último servicio (km)'), '100000')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-schedules')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/maintenance-schedules')[0].body).toEqual({
      name: 'Cambio de bujías',
      vehiclePublicId: 'v-2',
      trigger: 'MILEAGE',
      intervalKm: 8000,
      intervalDays: null,
      lastServiceKm: 100000,
      lastServiceDate: null,
    })
    expect(await screen.findByText('Programa creado.')).toBeInTheDocument()
  })

  it('un programa por tipo manda el tipo y no pide el último servicio; con Tiempo exige los días', async () => {
    const user = userEvent.setup()
    open()
    const programs = await panel('Programas de mantenimiento')
    await user.click(programs.getByRole('button', { name: /Nuevo programa/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo programa' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Revisión de camiones')
    await user.selectOptions(within(dialog).getByLabelText(/^Aplica a/), 'Un tipo de vehículo')
    expect(within(dialog).queryByLabelText('Último servicio (km)')).toBeNull()
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de vehículo/), 'Camión')
    await user.selectOptions(within(dialog).getByLabelText(/^Disparador/), 'Tiempo')
    await user.type(within(dialog).getByLabelText('Cada (km)'), '0')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Un programa por tiempo exige un intervalo en días mayor que 0.')).toBeInTheDocument()
    expect(within(dialog).getByText('El intervalo en km debe ser mayor que 0.')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('Cada (km)'))
    await user.type(within(dialog).getByLabelText('Cada (días)'), '36501')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El intervalo en días admite como máximo 36500.')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('Cada (días)'))
    await user.type(within(dialog).getByLabelText('Cada (días)'), '90')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-schedules')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/maintenance-schedules')[0].body).toEqual({ name: 'Revisión de camiones', vehicleType: 'TRUCK', trigger: 'TIME', intervalKm: null, intervalDays: 90 })
  })

  it('el error del servidor sale bajo su campo y el diálogo sigue abierto', async () => {
    const user = userEvent.setup()
    mock.override = (call) =>
      call.method === 'POST' && call.url.pathname === '/api/v1/maintenance-schedules'
        ? problem(400, 'Datos inválidos.', 'validation', { name: ['El nombre del programa admite como máximo 150 caracteres.'] })
        : undefined
    open()
    const programs = await panel('Programas de mantenimiento')
    await user.click(programs.getByRole('button', { name: /Nuevo programa/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo programa' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Aceite')
    await user.selectOptions(within(dialog).getByLabelText(/^Vehículo/), 'V-01 · HGR-204')
    await user.type(within(dialog).getByLabelText('Cada (km)'), '5000')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El nombre del programa admite como máximo 150 caracteres.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Nuevo programa' })).toBeInTheDocument()
  })

  it('editar manda solo lo cambiado; un intervalo vaciado va con clearIntervalDays', async () => {
    const user = userEvent.setup()
    open()
    const programs = await panel('Programas de mantenimiento')
    await programs.findByText('Frenos')
    await user.click(within(rowOf(programs, 'Frenos')).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar programa' })
    expect(within(dialog).getByLabelText(/^Nombre/)).toHaveValue('Frenos')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar programa' })).toBeNull())
    expect(callsTo('PATCH', '/api/v1/maintenance-schedules/4')).toHaveLength(0) // sin cambios no hay petición

    await user.click(within(rowOf(await panel('Programas de mantenimiento'), 'Frenos')).getByRole('button', { name: 'Editar' }))
    const again = await screen.findByRole('dialog', { name: 'Editar programa' })
    await user.clear(within(again).getByLabelText(/^Nombre/))
    await user.type(within(again).getByLabelText(/^Nombre/), 'Frenos delanteros')
    await user.selectOptions(within(again).getByLabelText(/^Disparador/), 'Kilometraje')
    await user.clear(within(again).getByLabelText('Cada (días)'))
    await user.click(within(again).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/maintenance-schedules/4')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/maintenance-schedules/4')[0].body).toEqual({ name: 'Frenos delanteros', trigger: 'MILEAGE', clearIntervalDays: true })
    expect(await screen.findByText('Programa guardado.')).toBeInTheDocument()
  })

  it('el último servicio guardado no se puede dejar vacío', async () => {
    const user = userEvent.setup()
    open()
    const programs = await panel('Programas de mantenimiento')
    await programs.findByText('Cambio de aceite')
    await user.click(within(rowOf(programs, 'Cambio de aceite')).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar programa' })
    await user.clear(within(dialog).getByLabelText('Último servicio (km)'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Este valor no se puede dejar vacío una vez guardado; escriba otro.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/maintenance-schedules/1')).toHaveLength(0)
  })

  it('desactivar pide confirmación y reactivar es directo; ninguna acción borra', async () => {
    const user = userEvent.setup()
    open()
    const programs = await panel('Programas de mantenimiento')
    await programs.findByText('Cambio de aceite')
    await user.click(within(rowOf(programs, 'Cambio de aceite')).getByRole('button', { name: 'Desactivar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar programa' })
    expect(dialog).toHaveTextContent('El programa «Cambio de aceite» deja de evaluarse')
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-schedules/1/deactivate')).toHaveLength(1))
    expect(await screen.findByText('Programa desactivado.')).toBeInTheDocument()
    await waitFor(() => expect(within(screen.getByRole('heading', { level: 2, name: 'Programas de mantenimiento' }).closest('section') as HTMLElement).queryByText('Cambio de aceite')).toBeNull())

    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivos' }))
    const inactive = within(rowOf(await panel('Programas de mantenimiento'), 'Rotación de gomas'))
    await user.click(inactive.getByRole('button', { name: 'Reactivar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-schedules/3/reactivate')).toHaveLength(1))
    expect(await screen.findByText('Programa reactivado.')).toBeInTheDocument()
    expect(mock.calls.filter((c) => c.method === 'DELETE')).toHaveLength(0)
  })

  it('sin fleet.maintenance los programas solo se consultan', async () => {
    open(VIEW)
    const programs = await panel('Programas de mantenimiento')
    await programs.findByText('Cambio de aceite')
    expect(programs.queryByRole('button', { name: /Nuevo programa/ })).toBeNull()
    expect(programs.queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(programs.queryByRole('button', { name: 'Desactivar' })).toBeNull()
  })
})
