// Lote F-D3 — Flota, pestaña Órdenes de trabajo: lista (columnas de la maqueta, filtros al servidor, buscador, TODAS las órdenes del filtro
// con orden sobre todo lo filtrado), alta, ficha (edición mínima con rowVersion, solo lectura en Cerrada/Cancelada), tareas (alta, completar,
// quitar, costos calculados), transiciones (Iniciar, Cerrar con odómetro y los mensajes exactos del servidor, Cancelar) y permisos.
// Sobre el fetch simulado con estado de `fleetMock`.
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, mock, problem, resetKit, VIEW, wrap } from './fleetTestKit'
import { maintenanceState } from './maintenanceMock'
import { typeDate } from '../../kernel/ui/typeDate'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./fleetMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const open = (permissions = ALL, entry = '/catalog/fleet?tab=workOrders') => wrap(permissions, entry)
const listPanel = async () => within((await screen.findByRole('heading', { level: 2, name: 'Órdenes de trabajo' })).closest('section') as HTMLElement)
const rowOf = (scope: ReturnType<typeof within>, text: string | RegExp) => scope.getByText(text).closest('tr') as HTMLElement
const table = async () => within(await (await listPanel()).findByRole('table'))
const detail = async (number: string) => within((await screen.findByRole('heading', { level: 2, name: `Orden ${number}` })).closest('section') as HTMLElement)
const tasksPanel = async () => within((await screen.findByRole('heading', { level: 2, name: 'Tareas' })).closest('section') as HTMLElement)

describe('Órdenes de trabajo — lista', () => {
  it('columnas de la maqueta: Orden, Vehículo, Tipo, Proveedor, Labor, Partes, Total y Estado con los estatus reales', async () => {
    open()
    const t = await table()
    const closed = await t.findByText('OT-00001').then((c) => within(c.closest('tr') as HTMLElement))
    expect(closed.getByText('HGP-118')).toBeInTheDocument()
    expect(closed.getByText('V-02')).toBeInTheDocument()
    expect(closed.getByText('Preventivo')).toBeInTheDocument()
    expect(closed.getByText('Taller Díaz')).toBeInTheDocument()
    expect(closed.getByText('$120.00')).toBeInTheDocument()
    expect(closed.getByText('$340.00')).toBeInTheDocument()
    expect(closed.getByText('$460.00')).toBeInTheDocument()
    expect(closed.getByText('Cerrada')).toBeInTheDocument()
    const opened = within(rowOf(t, 'OT-00002'))
    expect(opened.getByText('Abierta')).toBeInTheDocument()
    expect(opened.getByText('Correctivo')).toBeInTheDocument()
    expect(opened.getByText('$290.00')).toBeInTheDocument() // 80 + 210 de sus tareas
    expect(within(rowOf(t, 'OT-00003')).getByText('En proceso')).toBeInTheDocument()
    expect(within(rowOf(t, 'OT-00004')).getByText('Cancelada')).toBeInTheDocument()
    expect(within((await listPanel()).getByRole('heading', { level: 2 }).closest('section') as HTMLElement).getByText('4')).toBeInTheDocument()
  })

  it('las más recientes salen primero y ordenar por una columna ordena todas', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('OT-00001')
    const order = () => t.getAllByRole('row').slice(1).map((r) => within(r).getAllByRole('cell')[0].textContent)
    expect(order()).toEqual(['OT-00004', 'OT-00003', 'OT-00002', 'OT-00001'])
    await user.click(t.getByRole('button', { name: 'Total' }))
    expect(order()).toEqual(['OT-00004', 'OT-00003', 'OT-00002', 'OT-00001']) // 0, 60, 290, 460
    await user.click(t.getByRole('button', { name: 'Total' }))
    expect(order()).toEqual(['OT-00001', 'OT-00002', 'OT-00003', 'OT-00004'])
  })

  it('lee TODAS las órdenes del filtro de a 200 (el API no devuelve total) y las pagina y ordena en pantalla', async () => {
    const user = userEvent.setup()
    const make = (n: number) => ({
      id: 1000 + n,
      publicId: `wo-x${n}`,
      number: `OT-${String(100 + n).padStart(5, '0')}`,
      vehiclePublicId: 'v-1',
      vehicleCode: 'V-01',
      maintenanceTypeCode: 'CORRECTIVE',
      maintenanceType: 'Correctivo',
      statusCode: 'OPEN',
      status: 'Abierta',
      totalCost: n,
      laborCost: n,
      isActive: true,
    })
    mock.override = (call) => {
      if (call.method !== 'GET' || call.url.pathname !== '/api/v1/maintenance-work-orders' || call.url.searchParams.getAll('status').length) return undefined
      const skip = Number(call.url.searchParams.get('skip'))
      const take = Number(call.url.searchParams.get('take'))
      const all = Array.from({ length: 250 }, (_, i) => make(i + 1)).reverse() // más reciente primero
      return all.slice(skip, skip + take)
    }
    open()
    const panel = await listPanel()
    await waitFor(() => expect(panel.getByText('1–25 de 250')).toBeInTheDocument(), { timeout: 5000 })
    const reads = callsTo('GET', '/api/v1/maintenance-work-orders').filter((c) => !c.url.searchParams.getAll('status').length)
    expect(reads.map((c) => [c.url.searchParams.get('skip'), c.url.searchParams.get('take')])).toEqual([['0', '200'], ['200', '200']])
    // ordenar por Total ascendente: lo más barato de TODAS (no solo de la página) sale primero
    await user.click(panel.getByRole('button', { name: 'Total' }))
    const first = within(panel.getAllByRole('row')[1]).getAllByRole('cell')[0]
    expect(first).toHaveTextContent('OT-00101')
    await user.click(panel.getByRole('button', { name: 'Total' }))
    expect(within(panel.getAllByRole('row')[1]).getAllByRole('cell')[0]).toHaveTextContent('OT-00350')
  })

  it('los filtros Estado, Tipo, Vehículo y Fechas van al servidor', async () => {
    const user = userEvent.setup()
    open()
    const panel = await listPanel()
    await (await table()).findByText('OT-00001')
    await user.click(panel.getByLabelText('Estado'))
    await user.click(await screen.findByRole('option', { name: 'Abierta' }))
    await user.keyboard('{Escape}')
    await waitFor(() => expect(callsTo('GET', '/api/v1/maintenance-work-orders').some((c) => c.url.searchParams.getAll('status').join() === 'OPEN')).toBe(true))
    await waitFor(() => expect(panel.queryByText('OT-00001')).toBeNull())
    expect(panel.getByText('OT-00002')).toBeInTheDocument()
    await user.click(panel.getByRole('button', { name: 'Limpiar' }))
    await user.selectOptions(panel.getByLabelText('Tipo'), 'Preventivo')
    await waitFor(() => expect(callsTo('GET', '/api/v1/maintenance-work-orders').some((c) => c.url.searchParams.getAll('maintenanceType').join() === 'PREVENTIVE')).toBe(true))
    await waitFor(() => expect(panel.queryByText('OT-00002')).toBeNull())
    await user.click(panel.getByRole('button', { name: 'Limpiar' }))
    await user.selectOptions(panel.getByLabelText('Vehículo'), 'V-01 · HGR-204')
    await waitFor(() => expect(callsTo('GET', '/api/v1/maintenance-work-orders').some((c) => c.url.searchParams.get('vehiclePublicId') === 'v-1')).toBe(true))
    await waitFor(() => expect(panel.queryByText('OT-00001')).toBeNull())
    await user.click(panel.getByRole('button', { name: 'Limpiar' }))
    await user.selectOptions(panel.getByRole('group', { name: 'Fechas' }).querySelector('select[id$="-preset"]') as HTMLSelectElement, 'custom')   // el filtro de fecha es un desplegable de rangos: con «Personalizado» aparecen Desde / Hasta
    await typeDate(user, panel.getByLabelText('Desde'), '2026-10-01')
    await typeDate(user, panel.getByLabelText('Hasta'), '2026-10-31')
    await waitFor(() => expect(callsTo('GET', '/api/v1/maintenance-work-orders').some((c) => c.url.searchParams.get('from') === '2026-10-01' && c.url.searchParams.get('to') === '2026-10-31')).toBe(true))
  })

  it('el buscador filtra lo cargado por número, placa, proveedor o estado', async () => {
    const user = userEvent.setup()
    open()
    const panel = await listPanel()
    await (await table()).findByText('OT-00001')
    await user.type(panel.getByRole('searchbox'), 'autozone')
    await waitFor(() => expect(panel.queryByText('OT-00001')).toBeNull())
    expect(panel.getByText('OT-00003')).toBeInTheDocument()
    await user.clear(panel.getByRole('searchbox'))
    await user.type(panel.getByRole('searchbox'), 'cancelada')
    await waitFor(() => expect(panel.queryByText('OT-00003')).toBeNull())
    expect(panel.getByText('OT-00004')).toBeInTheDocument()
    await user.clear(panel.getByRole('searchbox'))
    await user.type(panel.getByRole('searchbox'), 'zzz')
    expect(await panel.findByText('Sin resultados')).toBeInTheDocument()
  })

  it('un error del servidor al leer se muestra con su mensaje', async () => {
    mock.override = (call) => (call.url.pathname === '/api/v1/maintenance-work-orders' && call.method === 'GET' ? problem(400, 'take debe estar entre 1 y 500.', 'validation') : undefined)
    open()
    expect(await screen.findAllByText('take debe estar entre 1 y 500.')).not.toHaveLength(0)
  })
})

describe('Órdenes de trabajo — nueva orden', () => {
  it('valida con los mensajes del manual y manda el alta; la orden nueva se abre en su ficha', async () => {
    const user = userEvent.setup()
    open()
    const panel = await listPanel()
    await user.click(panel.getByRole('button', { name: /Nueva orden de trabajo/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden de trabajo' })
    await user.click(await within(dialog).findByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El vehículo de la orden de trabajo es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('El tipo de mantenimiento es obligatorio.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText(/^Vehículo/), 'V-01 · HGR-204')
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de mantenimiento/), 'Correctivo')
    await user.type(within(dialog).getByLabelText('Labor'), '-5')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Los costos no pueden ser negativos.')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('Labor'))
    await user.type(within(dialog).getByLabelText('Proveedor'), 'Taller Díaz')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-work-orders')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/maintenance-work-orders')[0].body).toEqual({ vehiclePublicId: 'v-1', maintenanceType: 'CORRECTIVE', vendor: 'Taller Díaz' })
    expect(await screen.findByText('Orden OT-00005 creada.')).toBeInTheDocument()
    expect(await detail('OT-00005')).toBeDefined()
  })

  it('elegir un programa que aplica al vehículo vuelve la orden preventiva; uno correctivo con programa se rechaza', async () => {
    const user = userEvent.setup()
    open()
    await user.click((await listPanel()).getByRole('button', { name: /Nueva orden de trabajo/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden de trabajo' })
    await user.selectOptions(await within(dialog).findByLabelText(/^Vehículo/), 'V-02 · HGP-118')
    // al V-02 (camión) le aplica «Revisión general» (por tipo); «Cambio de aceite» es de V-01
    const programs = within(dialog).getByLabelText('Programa preventivo')
    expect(within(programs).getAllByRole('option').map((o) => o.textContent)).toEqual(['Sin programa', 'Revisión general'])
    await user.selectOptions(programs, 'Revisión general')
    await waitFor(() => expect(within(dialog).getByLabelText(/^Tipo de mantenimiento/)).toHaveValue('PREVENTIVE'))
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de mantenimiento/), 'Correctivo')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Una orden correctiva no se asocia a un programa preventivo.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/maintenance-work-orders')).toHaveLength(0)
  })

  it('un error del servidor sale bajo su campo y el diálogo sigue abierto', async () => {
    const user = userEvent.setup()
    mock.override = (call) =>
      call.method === 'POST' && call.url.pathname === '/api/v1/maintenance-work-orders'
        ? problem(409, 'El vehículo está inactivo; reactívelo para registrar órdenes de trabajo o cargas de combustible.', 'conflict')
        : undefined
    open()
    await user.click((await listPanel()).getByRole('button', { name: /Nueva orden de trabajo/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden de trabajo' })
    await user.selectOptions(await within(dialog).findByLabelText(/^Vehículo/), 'V-01 · HGR-204')
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de mantenimiento/), 'Correctivo')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El vehículo está inactivo; reactívelo para registrar órdenes de trabajo o cargas de combustible.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Nueva orden de trabajo' })).toBeInTheDocument()
  })

  it('sin fleet.maintenance no se pinta «Nueva orden de trabajo»', async () => {
    open(VIEW)
    await (await table()).findByText('OT-00001')
    expect(screen.queryByRole('button', { name: /Nueva orden de trabajo/ })).toBeNull()
  })
})

describe('Órdenes de trabajo — ficha', () => {
  it('un clic en la fila abre la ficha (?workOrder=) con sus datos, estatus, vehículo y programa', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await user.click(await t.findByText('OT-00003'))
    const ficha = await detail('OT-00003')
    expect(ficha.getByText('En proceso')).toBeInTheDocument()
    expect(ficha.getByText('HGR-204')).toBeInTheDocument()
    expect(ficha.getByText('Cambio de aceite')).toBeInTheDocument()
    expect(ficha.getByLabelText('Proveedor')).toHaveValue('AutoZone PR')
    expect(ficha.getByLabelText('Fecha programada')).toHaveValue('10/10/2026')
    expect(await screen.findByRole('heading', { level: 2, name: 'Tareas' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Historial de estatus' })).toBeInTheDocument()
    await user.click(ficha.getByRole('button', { name: 'Cerrar ficha' }))
    await waitFor(() => expect(screen.queryByRole('heading', { level: 2, name: 'Orden OT-00003' })).toBeNull())
  })

  it('se abre directo desde la URL', async () => {
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    expect(await detail('OT-00002')).toBeDefined()
  })

  it('una orden que no existe muestra el mensaje del servidor', async () => {
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=nope')
    expect(await screen.findByText('Orden de trabajo no encontrada.')).toBeInTheDocument()
  })

  it('editar manda solo lo cambiado con rowVersion; con tareas, Labor y Partes no se capturan', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const ficha = await detail('OT-00002')
    await waitFor(() => expect(ficha.getByLabelText('Proveedor')).toHaveValue('Taller Díaz'))
    expect(ficha.getByLabelText('Labor')).toBeDisabled()
    expect(ficha.getByLabelText('Partes')).toBeDisabled()
    expect(ficha.getByLabelText('Total')).toHaveValue('$290.00')
    expect(ficha.getByText('Con tareas, Labor y Partes son la suma de las tareas: no se capturan aquí.')).toBeInTheDocument()
    const save = ficha.getByRole('button', { name: 'Guardar cambios' })
    expect(save).toBeDisabled()
    await user.clear(ficha.getByLabelText('Proveedor'))
    await user.type(ficha.getByLabelText('Proveedor'), ' AutoZone PR ')
    await user.type(ficha.getByLabelText('Odómetro (km)'), '48100')
    await user.click(save)
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/maintenance-work-orders/wo-2')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/maintenance-work-orders/wo-2')[0].body).toEqual({ rowVersion: 'R2', vendor: 'AutoZone PR', odometerKm: 48100 })
    expect(await screen.findByText('Cambios guardados.')).toBeInTheDocument()
    await waitFor(() => expect(ficha.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled())
  })

  it('un 409 de rowVersion se muestra con su mensaje y conserva lo escrito', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const ficha = await detail('OT-00002')
    await waitFor(() => expect(ficha.getByLabelText('Proveedor')).toHaveValue('Taller Díaz'))
    maintenanceState.orders.find((o) => o.publicId === 'wo-2')!.rowVersion = 'OTRO'
    await user.type(ficha.getByLabelText('Proveedor'), ' 2')
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    expect(await ficha.findByText('El registro fue modificado por otro usuario; recargue e intente de nuevo.')).toBeInTheDocument()
    expect(ficha.getByLabelText('Proveedor')).toHaveValue('Taller Díaz 2')
  })

  it('una orden sin tareas captura Labor y Partes en el encabezado', async () => {
    const user = userEvent.setup()
    open()
    await user.click((await listPanel()).getByRole('button', { name: /Nueva orden de trabajo/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden de trabajo' })
    await user.selectOptions(await within(dialog).findByLabelText(/^Vehículo/), 'V-01 · HGR-204')
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de mantenimiento/), 'Correctivo')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    const ficha = await detail('OT-00005')
    await user.type(ficha.getByLabelText('Labor'), '50')
    await user.type(ficha.getByLabelText('Partes'), '25.5')
    await user.click(ficha.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/maintenance-work-orders/wo-new-5')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/maintenance-work-orders/wo-new-5')[0].body).toMatchObject({ laborCost: 50, partsCost: 25.5 })
    await waitFor(() => expect(ficha.getByLabelText('Total')).toHaveValue('$75.50'))
  })

  it('una orden Cerrada o Cancelada solo se consulta: sin acciones, sin guardar y con el aviso', async () => {
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-1')
    const ficha = await detail('OT-00001')
    expect(await ficha.findByText('Esta orden está en estatus «Cerrada» y solo se consulta.')).toBeInTheDocument()
    expect(ficha.getByLabelText('Proveedor')).toBeDisabled()
    expect(ficha.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    for (const name of ['Iniciar', 'Cerrar orden', 'Cancelar orden']) expect(ficha.queryByRole('button', { name })).toBeNull()
    expect(ficha.getByText('Fecha de cierre')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Agregar tarea' })).toBeNull()
  })

  it('sin fleet.maintenance la ficha se consulta sin acciones ni edición', async () => {
    open(VIEW, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const ficha = await detail('OT-00002')
    await waitFor(() => expect(ficha.getByLabelText('Proveedor')).toHaveValue('Taller Díaz'))
    expect(ficha.getByLabelText('Proveedor')).toBeDisabled()
    for (const name of ['Iniciar', 'Cerrar orden', 'Cancelar orden', 'Guardar cambios']) expect(ficha.queryByRole('button', { name })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Agregar tarea' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Quitar' })).toBeNull()
  })
})

describe('Órdenes de trabajo — tareas', () => {
  it('lista las tareas con sus costos, los totales que calcula el servidor y cuántas faltan por completar', async () => {
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const tasks = await tasksPanel()
    const row = within((await tasks.findByText('Cambio de pastillas de freno')).closest('tr') as HTMLElement)
    expect(row.getByText('$210.00')).toBeInTheDocument()
    expect(row.getByText('$80.00')).toBeInTheDocument()
    expect(row.getByRole('checkbox', { name: 'Completada: Cambio de pastillas de freno' })).not.toBeChecked()
    expect(tasks.getByText('1 sin completar')).toBeInTheDocument()
    expect(tasks.getByText('Totales').closest('p')).toHaveTextContent('Labor $80.00')
    expect(tasks.getByText('Totales').closest('p')).toHaveTextContent('Total $290.00')
  })

  it('agregar una tarea valida (descripción obligatoria, costos no negativos) y suma a los costos de la orden', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const tasks = await tasksPanel()
    await tasks.findByText('Cambio de pastillas de freno')
    await user.click(tasks.getByRole('button', { name: /Agregar tarea/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar tarea' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La descripción de la tarea es obligatoria.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Descripción/), 'Cambio de aceite')
    await user.type(within(dialog).getByLabelText('Partes'), '-1')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Los costos no pueden ser negativos.')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('Partes'))
    await user.type(within(dialog).getByLabelText('Partes'), '40')
    await user.type(within(dialog).getByLabelText('Labor'), '20')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-2/tasks')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-2/tasks')[0].body).toEqual({ description: 'Cambio de aceite', partCost: 40, laborCost: 20 })
    expect(await screen.findByText('Tarea guardada.')).toBeInTheDocument()
    await tasks.findByText('Cambio de aceite')
    expect(tasks.getByText('Totales').closest('p')).toHaveTextContent('Labor $100.00')
    expect(tasks.getByText('Totales').closest('p')).toHaveTextContent('Partes $250.00')
    expect(tasks.getByText('Totales').closest('p')).toHaveTextContent('Total $350.00')
    await waitFor(() => expect((screen.getByLabelText('Total') as HTMLInputElement).value).toBe('$350.00'))
  })

  it('marcar una tarea como completada manda solo isCompleted', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const tasks = await tasksPanel()
    await user.click(await tasks.findByRole('checkbox', { name: 'Completada: Cambio de pastillas de freno' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/maintenance-work-orders/wo-2/tasks/1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/maintenance-work-orders/wo-2/tasks/1')[0].body).toEqual({ isCompleted: true })
    await waitFor(() => expect(tasks.queryByText('1 sin completar')).toBeNull())
  })

  it('editar una tarea manda solo lo cambiado', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const tasks = await tasksPanel()
    await tasks.findByText('Cambio de pastillas de freno')
    await user.click(tasks.getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar tarea' })
    await user.clear(within(dialog).getByLabelText('Labor'))
    await user.type(within(dialog).getByLabelText('Labor'), '95')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/maintenance-work-orders/wo-2/tasks/1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/maintenance-work-orders/wo-2/tasks/1')[0].body).toEqual({ laborCost: 95 })
  })

  it('quitar una tarea pide confirmación y no la borra (baja lógica)', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const tasks = await tasksPanel()
    await tasks.findByText('Cambio de pastillas de freno')
    await user.click(tasks.getByRole('button', { name: 'Quitar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Quitar tarea' })
    expect(dialog).toHaveTextContent('Se quita la tarea «Cambio de pastillas de freno» de la orden y sus costos dejan de sumar.')
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-2/tasks/1/deactivate')).toHaveLength(1))
    expect(await screen.findByText('Tarea quitada.')).toBeInTheDocument()
    await waitFor(() => expect(tasks.getByText('Esta orden no tiene tareas.')).toBeInTheDocument())
    expect(mock.calls.filter((c) => c.method === 'DELETE')).toHaveLength(0)
  })

  it('el servidor puede negar la edición (422 EDIT_WORK_ORDER) y el mensaje se ve en el diálogo', async () => {
    const user = userEvent.setup()
    mock.override = (call) =>
      call.method === 'POST' && call.url.pathname === '/api/v1/maintenance-work-orders/wo-2/tasks'
        ? problem(422, "El estatus actual no permite la acción 'EDIT_WORK_ORDER'.", 'status_rule')
        : undefined
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const tasks = await tasksPanel()
    await tasks.findByText('Cambio de pastillas de freno')
    await user.click(tasks.getByRole('button', { name: /Agregar tarea/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar tarea' })
    await user.type(within(dialog).getByLabelText(/^Descripción/), 'X')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText("El estatus actual no permite la acción 'EDIT_WORK_ORDER'.")).toBeInTheDocument()
  })
})

describe('Órdenes de trabajo — Iniciar, Cerrar y Cancelar', () => {
  it('abierta: ofrece Iniciar, Cerrar orden y Cancelar orden; en proceso, solo Cerrar y Cancelar', async () => {
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const ficha = await detail('OT-00002')
    expect(await ficha.findByRole('button', { name: 'Iniciar' })).toBeInTheDocument()
    expect(ficha.getByRole('button', { name: 'Cerrar orden' })).toBeInTheDocument()
    expect(ficha.getByRole('button', { name: 'Cancelar orden' })).toBeInTheDocument()
  })

  it('en proceso: sin Iniciar', async () => {
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-3')
    const ficha = await detail('OT-00003')
    expect(await ficha.findByRole('button', { name: 'Cerrar orden' })).toBeInTheDocument()
    expect(ficha.queryByRole('button', { name: 'Iniciar' })).toBeNull()
  })

  it('Iniciar explica el efecto en el vehículo, pide confirmar y manda IN_PROGRESS con rowVersion', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const ficha = await detail('OT-00002')
    await user.click(await ficha.findByRole('button', { name: 'Iniciar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Iniciar la orden OT-00002' })
    expect(dialog).toHaveTextContent('el vehículo V-01 pasa a «En mantenimiento» y sale de la disponibilidad para despacho')
    await user.click(within(dialog).getByRole('button', { name: 'Iniciar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-2/status')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-2/status')[0].body).toEqual({ toCode: 'IN_PROGRESS', rowVersion: 'R2' })
    expect(await screen.findByText('Orden iniciada.')).toBeInTheDocument()
    await waitFor(() => expect(ficha.getByText('En proceso')).toBeInTheDocument())
    expect(ficha.queryByRole('button', { name: 'Iniciar' })).toBeNull()
  })

  it('cerrar con tareas sin completar: el 422 del servidor se ve en el diálogo y no se cierra', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const ficha = await detail('OT-00002')
    await user.click(await ficha.findByRole('button', { name: 'Cerrar orden' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar la orden OT-00002' })
    expect(within(dialog).getByLabelText<HTMLInputElement>('Fecha de cierre').value).toMatch(/^\d{2}\/\d{2}\/\d{4}$/) // hoy de la compañía, con su formato
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar orden' }))
    expect(await within(dialog).findByText('La orden tiene 1 tarea(s) sin completar; márquelas como completadas o quítelas antes de cerrarla.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Cerrar la orden OT-00002' })).toBeInTheDocument()
  })

  it('cerrar una orden de un programa por kilometraje exige el odómetro (aviso del navegador y del servidor)', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-3')
    const ficha = await detail('OT-00003')
    await user.click(await ficha.findByRole('button', { name: 'Cerrar orden' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar la orden OT-00003' })
    expect(within(dialog).getByText('Obligatorio: el programa de esta orden es por kilometraje.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar orden' }))
    expect(await within(dialog).findByText('Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-3/status')).toHaveLength(0)
    await user.type(within(dialog).getByLabelText(/^Odómetro al cerrar/), '48300')
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), ' Listo ')
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar orden' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-3/status')).toHaveLength(1))
    const body = callsTo('POST', '/api/v1/maintenance-work-orders/wo-3/status')[0].body
    expect(body).toMatchObject({ toCode: 'CLOSED', odometerKm: 48300, comment: 'Listo', rowVersion: 'R3' })
    expect(body?.completedDate).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    expect(await screen.findByText('Orden cerrada.')).toBeInTheDocument()
    await waitFor(() => expect(ficha.getByText('Esta orden está en estatus «Cerrada» y solo se consulta.')).toBeInTheDocument())
  })

  it('la fecha de cierre no puede ser futura', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-3')
    const ficha = await detail('OT-00003')
    await user.click(await ficha.findByRole('button', { name: 'Cerrar orden' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar la orden OT-00003' })
    await user.clear(within(dialog).getByLabelText('Fecha de cierre'))
    await typeDate(user, within(dialog).getByLabelText('Fecha de cierre'), '2099-01-01')
    await user.type(within(dialog).getByLabelText(/^Odómetro al cerrar/), '48300')
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar orden' }))
    expect(await within(dialog).findByText('La fecha de cierre no puede ser futura.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-3/status')).toHaveLength(0)
  })

  it('cancelar explica el efecto, acepta un comentario y manda CANCELLED', async () => {
    const user = userEvent.setup()
    open(ALL, '/catalog/fleet?tab=workOrders&workOrder=wo-2')
    const ficha = await detail('OT-00002')
    await user.click(await ficha.findByRole('button', { name: 'Cancelar orden' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la orden OT-00002' })
    expect(dialog).toHaveTextContent('ya no se puede editar ni reabrir')
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), 'Ya no hace falta')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar orden' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-2/status')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/maintenance-work-orders/wo-2/status')[0].body).toEqual({ toCode: 'CANCELLED', comment: 'Ya no hace falta', rowVersion: 'R2' })
    expect(await screen.findByText('Orden cancelada.')).toBeInTheDocument()
    await waitFor(() => expect(ficha.getByText('Esta orden está en estatus «Cancelada» y solo se consulta.')).toBeInTheDocument())
    expect(ficha.queryByRole('button', { name: 'Cancelar orden' })).toBeNull()
  })
})
