// Lote F-D3 — Flota, pestaña Combustible: tabla paginada por el SERVIDOR con total (skip/take), cálculos que muestra (Km recorridos, Km/L,
// Costo/km), resumen por vehículo de todo el filtro, filtros Vehículo/Chofer/Fechas, ordenar y exportar todo lo filtrado (`exportRows`),
// registrar (validaciones del manual, odómetro monótono del servidor), editar (sin cambiar el vehículo), quitar y permisos.
// Sobre el fetch simulado con estado de `fleetMock`.
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, mock, problem, resetKit, VIEW, wrap } from './fleetTestKit'
import { maintenanceState } from './maintenanceMock'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./fleetMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const open = (permissions = ALL) => wrap(permissions, '/catalog/fleet?tab=fuel')
const panel = async (name: string) => within((await screen.findByRole('heading', { level: 2, name })).closest('section') as HTMLElement)
const logPanel = () => panel('Bitácora de combustible')
const table = async () => within(await (await logPanel()).findByRole('table'))
/** Fila de la tabla principal que contiene ese texto. */
const rowWith = (scope: ReturnType<typeof within>, text: string | RegExp) => scope.getAllByRole('row').find((r) => within(r).queryByText(text)) as HTMLElement

describe('Combustible — bitácora', () => {
  it('Fecha, Vehículo, Chofer, Estación, Litros, Costo, Km recorridos, Km/L y Costo/km; lo calculado lo trae el servidor', async () => {
    open()
    const t = await table()
    await t.findByText('Puma Bayamón')
    const rows = t.getAllByRole('row').slice(1)
    expect(rows).toHaveLength(4)
    // más reciente primero: la del V-01 del 11 de oct. = (48210 - 47780) = 430 km con 38.2 L
    const last = within(rows[0])
    expect(last.getByText('HGR-204')).toBeInTheDocument()
    expect(last.getByText('38.2')).toBeInTheDocument()
    expect(last.getByText('$172.40')).toBeInTheDocument()
    expect(last.getByText('430')).toBeInTheDocument()
    expect(last.getByText('11.26')).toBeInTheDocument() // 430 / 38.2
    expect(last.getByText('$0.40')).toBeInTheDocument() // 172.40 / 430
    // la primera carga de un vehículo no tiene con qué compararse
    const first = within(rowWith(t, 'Puma Bayamón'))
    expect(first.getByText('Ramón Díaz')).toBeInTheDocument()
    expect(first.getAllByText('—').length).toBeGreaterThanOrEqual(3)
    expect((await logPanel()).getByText('4')).toBeInTheDocument() // contador = total
  })

  it('el resumen por vehículo suma todo el filtro, no solo la página', async () => {
    open()
    const summary = await panel('Resumen por vehículo')
    const v1 = within((await summary.findByText('HGR-204')).closest('tr') as HTMLElement)
    expect(v1.getByText('3')).toBeInTheDocument() // cargas
    expect(v1.getByText('111.7')).toBeInTheDocument() // litros
    expect(v1.getByText('$503.60')).toBeInTheDocument()
    expect(v1.getByText('790')).toBeInTheDocument() // 48210 - 47420
    const v2 = within(summary.getByText('HGP-118').closest('tr') as HTMLElement)
    expect(v2.getByText('1')).toBeInTheDocument()
    expect(summary.getByText('Suma las cargas de todo el filtro, no solo las de esta página.')).toBeInTheDocument()
  })

  it('pagina en el servidor con skip/take y total, y cambia el tamaño de página', async () => {
    const user = userEvent.setup()
    const make = (n: number) => ({
      id: 100 + n,
      vehiclePublicId: 'v-1',
      vehicleCode: 'V-01',
      fillDateUtc: `2026-09-${String((n % 28) + 1).padStart(2, '0')}T10:00:00`,
      liters: n,
      totalCost: n * 4,
      odometerKm: null,
      isActive: true,
    })
    maintenanceState.fuel = Array.from({ length: 60 }, (_, i) => make(i + 1))
    open()
    const panelMain = await logPanel()
    await waitFor(() => expect(panelMain.getByText('1–25 de 60')).toBeInTheDocument())
    expect(callsTo('GET', '/api/v1/fuel-logs')[0].url.searchParams.get('skip')).toBe('0')
    expect(callsTo('GET', '/api/v1/fuel-logs')[0].url.searchParams.get('take')).toBe('25')
    await user.click(panelMain.getByRole('button', { name: 'Página siguiente' }))
    await waitFor(() => expect(panelMain.getByText('26–50 de 60')).toBeInTheDocument())
    expect(callsTo('GET', '/api/v1/fuel-logs').some((c) => c.url.searchParams.get('skip') === '25')).toBe(true)
    await user.selectOptions(panelMain.getByLabelText('Filas por página'), '50')
    await waitFor(() => expect(panelMain.getByText('1–50 de 60')).toBeInTheDocument())
    expect(callsTo('GET', '/api/v1/fuel-logs').some((c) => c.url.searchParams.get('take') === '50' && c.url.searchParams.get('skip') === '0')).toBe(true)
  })

  it('ordenar lee TODO el filtro (de a 200) y ordena y pagina en pantalla', async () => {
    const user = userEvent.setup()
    const make = (n: number) => ({
      id: 100 + n,
      vehiclePublicId: 'v-1',
      vehicleCode: 'V-01',
      fillDateUtc: `2026-0${(n % 9) + 1}-10T10:00:00`,
      liters: n,
      totalCost: n * 4,
      odometerKm: null,
      isActive: true,
    })
    maintenanceState.fuel = Array.from({ length: 230 }, (_, i) => make(i + 1))
    open()
    const panelMain = await logPanel()
    await waitFor(() => expect(panelMain.getByText('1–25 de 230')).toBeInTheDocument())
    await user.click(panelMain.getByRole('button', { name: 'Litros' }))
    await waitFor(() => expect(callsTo('GET', '/api/v1/fuel-logs').filter((c) => c.url.searchParams.get('take') === '200')).toHaveLength(2))
    const reads = callsTo('GET', '/api/v1/fuel-logs').filter((c) => c.url.searchParams.get('take') === '200')
    expect(reads.map((c) => c.url.searchParams.get('skip'))).toEqual(['0', '200'])
    // los litros más bajos de TODAS las cargas (la página 1 del servidor traía las más recientes, no las menores)
    await waitFor(() => expect(within(panelMain.getAllByRole('row')[1]).getAllByRole('cell')[4]).toHaveTextContent(/^1$/))
    await user.click(panelMain.getByRole('button', { name: 'Litros' }))
    await waitFor(() => expect(within(panelMain.getAllByRole('row')[1]).getAllByRole('cell')[4]).toHaveTextContent(/^230$/))
    expect(panelMain.getByText(/de 230$/)).toBeInTheDocument()
  })

  it('los filtros Vehículo, Chofer y Fechas van al servidor y vuelven a la primera página', async () => {
    const user = userEvent.setup()
    open()
    const panelMain = await logPanel()
    await (await table()).findByText('Puma Bayamón')
    await user.selectOptions(panelMain.getByLabelText('Vehículo'), 'V-02 · HGP-118')
    await waitFor(() => expect(callsTo('GET', '/api/v1/fuel-logs').some((c) => c.url.searchParams.get('vehiclePublicId') === 'v-2')).toBe(true))
    await waitFor(() => expect(panelMain.queryByText('Puma Bayamón')).toBeNull())
    expect(panelMain.getAllByRole('row')).toHaveLength(2)
    await user.click(panelMain.getByRole('button', { name: 'Limpiar' }))
    await user.selectOptions(panelMain.getByLabelText('Chofer'), '010 · Ramón Díaz')
    await waitFor(() => expect(callsTo('GET', '/api/v1/fuel-logs').some((c) => c.url.searchParams.get('driverPublicId') === 'd-1')).toBe(true))
    await waitFor(() => expect(panelMain.getAllByRole('row')).toHaveLength(2))
    await user.click(panelMain.getByRole('button', { name: 'Limpiar' }))
    await user.type(panelMain.getByLabelText('Desde'), '2026-10-01')
    await user.type(panelMain.getByLabelText('Hasta'), '2026-10-05')
    // «hasta» es exclusivo en el servidor: se manda la medianoche local del día SIGUIENTE (Puerto Rico = UTC-4)
    await waitFor(() => expect(callsTo('GET', '/api/v1/fuel-logs').some((c) => c.url.searchParams.get('fromUtc') === '2026-10-01T04:00:00.000Z' && c.url.searchParams.get('toUtc') === '2026-10-06T04:00:00.000Z')).toBe(true))
    await waitFor(() => expect(panelMain.getAllByRole('row')).toHaveLength(2))
    expect(panelMain.getByText('36.5')).toBeInTheDocument() // solo la del 4 de oct.
  })

  it('sin cargas muestra el estado vacío', async () => {
    maintenanceState.fuel = []
    open()
    expect(await screen.findByText('No hay cargas de combustible.')).toBeInTheDocument()
    expect(screen.getByText('Sin cargas que resumir.')).toBeInTheDocument()
  })

  it('un error del servidor al leer se muestra con su mensaje', async () => {
    mock.override = (call) => (call.url.pathname === '/api/v1/fuel-logs' && call.method === 'GET' ? problem(400, 'take debe estar entre 1 y 500.', 'validation') : undefined)
    open()
    expect(await screen.findByText('take debe estar entre 1 y 500.')).toBeInTheDocument()
  })
})

describe('Combustible — registrar carga', () => {
  const openForm = async () => {
    const user = userEvent.setup()
    open()
    const panelMain = await logPanel()
    await (await table()).findByText('Puma Bayamón')
    await user.click(panelMain.getByRole('button', { name: /Registrar carga/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Registrar carga' })
    await within(dialog).findByLabelText(/^Vehículo/)
    return { user, dialog }
  }

  it('valida con los mensajes del manual: vehículo, litros, costo, fecha futura', async () => {
    const { user, dialog } = await openForm()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El vehículo de la carga es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('Los litros deben ser mayores que 0.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el costo total de la carga.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Litros/), '0')
    await user.type(within(dialog).getByLabelText(/^Costo total/), '-3')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Los litros deben ser mayores que 0.')).toBeInTheDocument()
    expect(within(dialog).getByText('El costo total no puede ser negativo.')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText(/^Fecha/))
    await user.type(within(dialog).getByLabelText(/^Fecha/), '2099-01-01')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La fecha de la carga no puede ser futura.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/fuel-logs')).toHaveLength(0)
  })

  it('manda la alta con la fecha y hora de la compañía en UTC y refresca la bitácora y el resumen', async () => {
    const { user, dialog } = await openForm()
    await user.selectOptions(within(dialog).getByLabelText(/^Vehículo/), 'V-02 · HGP-118')
    await user.clear(within(dialog).getByLabelText(/^Fecha/))
    await user.type(within(dialog).getByLabelText(/^Fecha/), '2026-10-11')
    fireEvent.change(within(dialog).getByLabelText(/^Hora/), { target: { value: '09:30' } })
    await user.type(within(dialog).getByLabelText(/^Litros/), '90')
    await user.type(within(dialog).getByLabelText(/^Costo total/), '400.5')
    await user.type(within(dialog).getByLabelText(/^Odómetro/), '102800')
    await user.selectOptions(within(dialog).getByLabelText('Chofer'), '001 · José Pérez')
    await user.type(within(dialog).getByLabelText('Estación'), ' Shell ')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/fuel-logs')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/fuel-logs')[0].body).toEqual({
      vehiclePublicId: 'v-2',
      fillDateUtc: '2026-10-11T13:30:00.000Z',
      liters: 90,
      totalCost: 400.5,
      odometerKm: 102800,
      driverPublicId: 'd-2',
      station: 'Shell',
    })
    expect(await screen.findByText('Carga registrada.')).toBeInTheDocument()
    const t = await table()
    const added = within(await waitFor(() => rowWith(t, 'Shell')))
    expect(added.getByText('José Pérez')).toBeInTheDocument()
    expect(added.getByText('460')).toBeInTheDocument() // 102800 - 102340
    expect(added.getByText('5.11')).toBeInTheDocument() // 460 / 90
  })

  it('un odómetro menor que el de una carga anterior lo rechaza el servidor y el mensaje sale bajo el campo', async () => {
    const { user, dialog } = await openForm()
    await user.selectOptions(within(dialog).getByLabelText(/^Vehículo/), 'V-01 · HGR-204')
    await user.clear(within(dialog).getByLabelText(/^Fecha/))
    await user.type(within(dialog).getByLabelText(/^Fecha/), '2026-10-11')
    fireEvent.change(within(dialog).getByLabelText(/^Hora/), { target: { value: '23:00' } })
    await user.type(within(dialog).getByLabelText(/^Litros/), '30')
    await user.type(within(dialog).getByLabelText(/^Costo total/), '120')
    await user.type(within(dialog).getByLabelText(/^Odómetro/), '40000')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(
      await within(dialog).findByText('La lectura de odómetro (40000 km) es menor que la de una carga anterior del mismo vehículo (48210 km el 2026-10-11).'),
    ).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Registrar carga' })).toBeInTheDocument()
    // corregido, se guarda
    await user.clear(within(dialog).getByLabelText(/^Odómetro/))
    await user.type(within(dialog).getByLabelText(/^Odómetro/), '48600')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Carga registrada.')).toBeInTheDocument()
  })

  it('sin fleet.maintenance no hay «Registrar carga», «Editar» ni «Quitar»', async () => {
    open(VIEW)
    await (await table()).findByText('Puma Bayamón')
    expect(screen.queryByRole('button', { name: /Registrar carga/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Quitar' })).toBeNull()
  })
})

describe('Combustible — editar y quitar', () => {
  it('editar abre la carga con sus datos, el vehículo no se cambia y solo viaja lo cambiado', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Puma Bayamón')
    await user.click(within(rowWith(t, 'Puma Bayamón')).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar carga' })
    await waitFor(() => expect(within(dialog).getByLabelText(/^Vehículo/)).toHaveValue('v-1'))
    expect(within(dialog).getByLabelText(/^Vehículo/)).toBeDisabled()
    expect(within(dialog).getByText('El vehículo de una carga no se cambia; quítela y registre otra.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText(/^Fecha/)).toHaveValue('2026-09-27')
    expect(within(dialog).getByLabelText(/^Hora/)).toHaveValue('10:00')
    expect(within(dialog).getByLabelText(/^Litros/)).toHaveValue(37)
    expect(within(dialog).getByLabelText('Chofer')).toHaveValue('d-1')
    // sin cambios no hay petición
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar carga' })).toBeNull())
    expect(callsTo('PATCH', '/api/v1/fuel-logs/1')).toHaveLength(0)

    await user.click(within(rowWith(await table(), 'Puma Bayamón')).getByRole('button', { name: 'Editar' }))
    const again = await screen.findByRole('dialog', { name: 'Editar carga' })
    await user.clear(within(again).getByLabelText(/^Litros/))
    await user.type(within(again).getByLabelText(/^Litros/), '38')
    await user.clear(within(again).getByLabelText(/^Odómetro/))
    await user.selectOptions(within(again).getByLabelText('Chofer'), 'Sin chofer')
    await user.clear(within(again).getByLabelText('Estación'))
    await user.click(within(again).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/fuel-logs/1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/fuel-logs/1')[0].body).toEqual({ liters: 38, clearOdometer: true, clearDriver: true, station: '' })
    expect(await screen.findByText('Carga guardada.')).toBeInTheDocument()
  })

  it('un odómetro mayor que el de una carga posterior lo rechaza el servidor al editar', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Puma Bayamón')
    await user.click(within(rowWith(t, 'Puma Bayamón')).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar carga' })
    await user.clear(within(dialog).getByLabelText(/^Odómetro/))
    await user.type(within(dialog).getByLabelText(/^Odómetro/), '49000')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText(/^La lectura de odómetro \(49000 km\) es mayor que la de una carga posterior del mismo vehículo/)).toBeInTheDocument()
  })

  it('una carga ya desactivada no se corrige: el 409 del servidor se ve en el diálogo', async () => {
    const user = userEvent.setup()
    mock.override = (call) => (call.method === 'PATCH' && call.url.pathname === '/api/v1/fuel-logs/1' ? problem(409, 'La carga de combustible está inactiva; no se puede corregir.', 'conflict') : undefined)
    open()
    const t = await table()
    await t.findByText('Puma Bayamón')
    await user.click(within(rowWith(t, 'Puma Bayamón')).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar carga' })
    await user.clear(within(dialog).getByLabelText('Estación'))
    await user.type(within(dialog).getByLabelText('Estación'), 'Otra')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La carga de combustible está inactiva; no se puede corregir.')).toBeInTheDocument()
  })

  it('quitar pide confirmación, no borra (deactivate) y la carga sale de la bitácora y del resumen', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Puma Bayamón')
    await user.click(within(rowWith(t, 'Puma Bayamón')).getByRole('button', { name: 'Quitar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Quitar carga' })
    expect(dialog).toHaveTextContent('Se quita la carga de 37 L del 09/27/2026 (HGR-204 (V-01)).')
    expect(dialog).toHaveTextContent('el odómetro del vehículo no baja')
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/fuel-logs/1/deactivate')).toHaveLength(1))
    expect(await screen.findByText('Carga quitada.')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByText('Puma Bayamón')).toBeNull())
    expect((await logPanel()).getAllByRole('row')).toHaveLength(4)
    expect(mock.calls.filter((c) => c.method === 'DELETE')).toHaveLength(0)
  })

  it('al quitar la última carga de la última página vuelve a la página anterior', async () => {
    const user = userEvent.setup()
    maintenanceState.fuel = Array.from({ length: 26 }, (_, i) => ({
      id: 100 + i,
      vehiclePublicId: 'v-1',
      vehicleCode: 'V-01',
      fillDateUtc: `2026-09-${String(i + 1).padStart(2, '0')}T10:00:00`,
      liters: 10 + i,
      totalCost: 40,
      station: `Est ${i + 1}`,
      odometerKm: null,
      isActive: true,
    }))
    open()
    const panelMain = await logPanel()
    await waitFor(() => expect(panelMain.getByText('1–25 de 26')).toBeInTheDocument())
    await user.click(panelMain.getByRole('button', { name: 'Página siguiente' }))
    await waitFor(() => expect(panelMain.getByText('26–26 de 26')).toBeInTheDocument())
    const t = await table()
    await user.click(within(t.getAllByRole('row')[1]).getByRole('button', { name: 'Quitar' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Quitar carga' })).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(panelMain.getByText('1–25 de 25')).toBeInTheDocument())
  })
})
