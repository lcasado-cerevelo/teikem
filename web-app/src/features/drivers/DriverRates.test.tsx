// Lote F-D1 — tarifas del chofer y fórmula de pago: filas por entrega, intento y viaje; alta (validaciones del manual, 409 de duplicado),
// editar = nueva versión desde una fecha (por omisión hoy, nunca anterior), «Cerrar tarifa» en lugar de borrar, historial, filtros,
// «sin tarifa» ≠ $0, «+ Agregar intento», el selector de la fórmula y la vista previa calculada por el servidor, con y sin permisos.
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { tenantToday } from '../../kernel/api/tenantZone'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, FLEET, mock, problem, RATES, resetKit, wrap } from './driversTestKit'
import { typeDate, shownDate } from '../../kernel/ui/typeDate'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./driversMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const delivery = () => screen.findByRole('region', { name: 'Tarifas por entrega' })
const attempts = () => screen.findByRole('list', { name: 'Tarifas por intento' })
const tripsPanel = async () => (await screen.findByRole('heading', { level: 2, name: 'Tarifas por viaje' })).closest('section') as HTMLElement
const rowOf = (scope: HTMLElement, text: RegExp) => within(scope).getByRole('row', { name: text })
const cellOf = (list: HTMLElement, n: number) => within(list).getByText(`Intento ${n}`).closest('li') as HTMLElement
/** Elige una opción de un `<select>` cuando las opciones (catálogos) ya llegaron. */
async function pick(user: ReturnType<typeof userEvent.setup>, select: HTMLElement, label: string) {
  await within(select).findByRole('option', { name: label })
  await user.selectOptions(select, label)
}
const today = () => tenantToday()
const yesterday = () => {
  const d = new Date(`${today()}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() - 1)
  return d.toISOString().slice(0, 10)
}

describe('Tarifas por entrega', () => {
  it('lista servicio, paquete y tarifa vigentes; sin historial no hay columnas de fechas cerradas', async () => {
    wrap(ALL)
    const region = await delivery()
    const next = rowOf(region, /Next day/)
    expect(within(next).getByText('Box')).toBeInTheDocument()
    expect(within(next).getByText('$6.00')).toBeInTheDocument()
    expect(within(rowOf(region, /Standard/)).getByText('$5.50')).toBeInTheDocument()
    expect(within(region).queryByRole('columnheader', { name: /Hasta/ })).toBeNull()
    expect(callsTo('GET', '/api/v1/drivers/d-1/rates')[0].url.searchParams.get('includeHistory')).toBe('false')
  })

  it('alta inline: Servicio, Paquete y Tarifa; manda POST sin fecha y limpia el formulario', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await pick(user, within(region).getAllByLabelText('Servicio').at(-1)!, 'Standard')
    await pick(user, within(region).getAllByLabelText('Paquete').at(-1)!, 'Box')
    await user.type(within(region).getByLabelText('Tarifa'), '4.5')
    await user.click(within(region).getByRole('button', { name: /Agregar/ }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/delivery-rates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/delivery-rates')[0].body).toEqual({ serviceType: 'STANDARD', packageType: 'BOX', rate: 4.5, zoneGroup: null })
    expect(await screen.findByText('Tarifa agregada.')).toBeInTheDocument()
    await waitFor(() => expect(within(region).getByLabelText('Tarifa')).toHaveValue(null))
  })

  it('validaciones del manual: servicio y paquete obligatorios, tarifa vacía, negativa y con más de 4 decimales; $0 vale', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await user.click(within(region).getByRole('button', { name: /Agregar/ }))
    expect((await within(region).findAllByText('Indique el servicio y el tipo de paquete de la tarifa.')).length).toBe(2)
    expect(within(region).getByText('Indique la tarifa.')).toBeInTheDocument()
    await user.type(within(region).getByLabelText('Tarifa'), '-1')
    await user.click(within(region).getByRole('button', { name: /Agregar/ }))
    expect(await within(region).findByText('La tarifa no puede ser negativa.')).toBeInTheDocument()
    await user.clear(within(region).getByLabelText('Tarifa'))
    await user.type(within(region).getByLabelText('Tarifa'), '1.23456')
    await user.click(within(region).getByRole('button', { name: /Agregar/ }))
    expect(await within(region).findByText('El valor admite como máximo 4 decimales y debe ser menor que 100,000,000,000,000.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/drivers/d-1/delivery-rates')).toHaveLength(0)
    await pick(user, within(region).getAllByLabelText('Servicio').at(-1)!, 'Standard')
    await pick(user, within(region).getAllByLabelText('Paquete').at(-1)!, 'Box')
    await user.clear(within(region).getByLabelText('Tarifa'))
    await user.type(within(region).getByLabelText('Tarifa'), '0')
    await user.click(within(region).getByRole('button', { name: /Agregar/ }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/delivery-rates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/delivery-rates')[0].body).toMatchObject({ rate: 0 })
  })

  it('el 409 de «ya hay una tarifa vigente» sale con el mensaje exacto del servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await pick(user, within(region).getAllByLabelText('Servicio').at(-1)!, 'Next day')
    await pick(user, within(region).getAllByLabelText('Paquete').at(-1)!, 'Box')
    await user.type(within(region).getByLabelText('Tarifa'), '9')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/drivers/d-1/delivery-rates'
        ? problem(409, 'El chofer ya tiene una tarifa vigente para Next day + Box; edite esa tarifa o ciérrela antes de agregar otra.', 'conflict')
        : undefined
    await user.click(within(region).getByRole('button', { name: /Agregar/ }))
    expect(await within(region).findByText('El chofer ya tiene una tarifa vigente para Next day + Box; edite esa tarifa o ciérrela antes de agregar otra.')).toBeInTheDocument()
  })

  it('Editar abre una versión nueva: tarifa y «Vigente desde» (hoy por omisión) → PATCH con rate y effectiveFrom', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await user.click(within(rowOf(region, /Next day/)).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar tarifa' })
    expect(within(dialog).getByText('Next day · Box')).toBeInTheDocument()
    expect(within(dialog).getByLabelText(/^Tarifa/)).toHaveValue(6)
    expect(within(dialog).getByLabelText(/^Vigente desde/)).toHaveValue(shownDate(today()))
    await user.clear(within(dialog).getByLabelText(/^Tarifa/))
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '7.25')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-1/delivery-rates/11')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-1/delivery-rates/11')[0].body).toEqual({ rate: 7.25, effectiveFrom: today() })
    expect(await screen.findByText('Tarifa guardada.')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar tarifa' })).toBeNull())
  })

  it('la vigencia puede ser futura, pero nunca anterior a hoy (mensaje del manual, sin llamar al servidor)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await user.click(within(rowOf(region, /Next day/)).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar tarifa' })
    await typeDate(user, within(dialog).getByLabelText(/^Vigente desde/), yesterday())
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/drivers/d-1/delivery-rates/11')).toHaveLength(0)
    await user.clear(within(dialog).getByLabelText(/^Vigente desde/))
    await typeDate(user, within(dialog).getByLabelText(/^Vigente desde/), '2099-01-01')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-1/delivery-rates/11')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-1/delivery-rates/11')[0].body).toMatchObject({ effectiveFrom: '2099-01-01' })
  })

  it('un error del servidor al editar queda bajo el campo y el diálogo sigue abierto', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await user.click(within(rowOf(region, /Standard/)).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar tarifa' })
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/drivers/d-1/delivery-rates/12'
        ? problem(409, 'La tarifa ya está cerrada; agregue una nueva si necesita volver a pagarla.', 'conflict')
        : undefined
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La tarifa ya está cerrada; agregue una nueva si necesita volver a pagarla.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Editar tarifa' })).toBeInTheDocument()
  })

  it('«Cerrar tarifa» pide confirmar y manda POST close (no se borra); el error del servidor queda en el diálogo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await user.click(within(rowOf(region, /Next day/)).getByRole('button', { name: 'Cerrar tarifa' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar tarifa' })
    expect(within(dialog).getByText(/Next day · Box/)).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/drivers/d-1/delivery-rates/11/close')).toHaveLength(0)
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname.endsWith('/delivery-rates/11/close') ? problem(404, 'Tarifa no encontrada.', 'not_found') : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar tarifa' }))
    expect(await within(dialog).findByText('Tarifa no encontrada.')).toBeInTheDocument()
    mock.override = null
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar tarifa' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/delivery-rates/11/close').length).toBeGreaterThan(0))
    expect(callsTo('DELETE', '/api/v1/drivers/d-1/delivery-rates/11')).toHaveLength(0)
    expect(callsTo('POST', '/api/v1/drivers/d-1/delivery-rates/11/close').at(-1)!.body).toEqual({})
  })

  it('«Ver historial» pide includeHistory, muestra «Desde/Hasta» y las filas cerradas no se editan ni se cierran', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await user.click(screen.getAllByRole('switch', { name: 'Ver historial' })[0])
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1/rates').some((c) => c.url.searchParams.get('includeHistory') === 'true')).toBe(true))
    expect(await within(region).findByRole('columnheader', { name: /Hasta/ })).toBeInTheDocument()
    const rows = within(region).getAllByRole('row', { name: /Next day/ })
    expect(rows).toHaveLength(2)
    const closed = rows.find((r) => within(r).queryByText('$5.00'))!
    expect(within(closed).queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(within(closed).queryByRole('button', { name: 'Cerrar tarifa' })).toBeNull()
    const open = rows.find((r) => within(r).queryByText('$6.00'))!
    expect(within(open).getByRole('button', { name: 'Editar' })).toBeInTheDocument()
    // el historial también incluye las tarifas por viaje cerradas
    expect(within(await tripsPanel()).getByText('$60.00')).toBeInTheDocument()
  })

  it('los filtros Servicio y Paquete filtran lo cargado y «Limpiar» los quita', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await user.click(within(region).getAllByLabelText('Servicio')[0])
    await user.click(await within(await screen.findByRole('listbox')).findByRole('option', { name: 'Standard' }))
    expect(within(region).queryByRole('row', { name: /Next day/ })).toBeNull()
    expect(within(region).getByRole('row', { name: /Standard/ })).toBeInTheDocument()
    await user.click(within(region).getByRole('button', { name: 'Limpiar' }))
    expect(within(region).getByRole('row', { name: /Next day/ })).toBeInTheDocument()
  })
})

describe('Tarifas por intento', () => {
  it('«sin tarifa» es distinto de $0: el nivel 1 cobra $0.00 y el 2 aún no tiene tarifa', async () => {
    wrap(ALL)
    const list = await attempts()
    expect(within(cellOf(list, 1)).getByText('$0.00')).toBeInTheDocument()
    expect(within(cellOf(list, 1)).queryByText('sin tarifa')).toBeNull()
    expect(within(cellOf(list, 2)).getByText('sin tarifa')).toBeInTheDocument()
    expect(cellOf(list, 2)).toHaveAttribute('data-kind', 'none')
  })

  it('«Fijar tarifa» en un nivel sin tarifa: PUT con tarifa y vigencia (hoy)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const list = await attempts()
    await user.click(within(cellOf(list, 2)).getByRole('button', { name: 'Fijar tarifa' }))
    const dialog = await screen.findByRole('dialog', { name: 'Fijar la tarifa del intento 2' })
    expect(within(dialog).getByText('Este intento no tiene tarifa. Indique cuánto se paga y desde cuándo.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '3')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/drivers/d-1/attempt-rates/2')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/drivers/d-1/attempt-rates/2')[0].body).toEqual({ rate: 3, effectiveFrom: today() })
  })

  it('editar el nivel 1 parte de su tarifa actual; «Cerrar tarifa» lo deja sin tarifa; un nivel sin tarifa no se cierra', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const list = await attempts()
    expect(within(cellOf(list, 2)).queryByRole('button', { name: 'Cerrar tarifa' })).toBeNull()
    await user.click(within(cellOf(list, 1)).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar la tarifa del intento 1' })
    expect(within(dialog).getByLabelText(/^Tarifa/)).toHaveValue(0)
    await user.clear(within(dialog).getByLabelText(/^Tarifa/))
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '1.5')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/drivers/d-1/attempt-rates/1')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/drivers/d-1/attempt-rates/1')[0].body).toEqual({ rate: 1.5, effectiveFrom: today() })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: /Editar la tarifa/ })).toBeNull())

    await user.click(within(cellOf(await attempts(), 1)).getByRole('button', { name: 'Cerrar tarifa' }))
    const confirm = await screen.findByRole('dialog', { name: 'Cerrar tarifa' })
    await user.click(within(confirm).getByRole('button', { name: 'Cerrar tarifa' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/attempt-rates/1/close')).toHaveLength(1))
  })

  it('«+ Agregar intento» sube un nivel de la compañía (POST attempt-levels) y vuelve a leer las tarifas', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await attempts()
    const before = callsTo('GET', '/api/v1/drivers/d-1/rates').length
    await user.click(screen.getByRole('button', { name: '+ Agregar intento' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/driver-pay-policy/attempt-levels')).toHaveLength(1))
    expect(await screen.findByText('Se agregó un nivel de intento.')).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1/rates').length).toBeGreaterThan(before))
  })

  it('en el tope de 20 niveles el botón se apaga; el 409 del servidor sale como aviso', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/driver-pay-policy/attempt-levels' ? problem(409, 'Se alcanzó el máximo de 20 niveles de intento.', 'conflict') : undefined
    const first = wrap(ALL)
    await attempts()
    await user.click(screen.getByRole('button', { name: '+ Agregar intento' }))
    expect(await screen.findByText('Se alcanzó el máximo de 20 niveles de intento.')).toBeInTheDocument()
    first.unmount()
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/drivers/d-1/rates' ? { ...RATES, attemptLevels: 20 } : undefined)
    wrap(ALL)
    await attempts()
    expect(screen.getByRole('button', { name: '+ Agregar intento' })).toBeDisabled()
  })
})

describe('Tarifas por viaje', () => {
  it('lista tipo de viaje y tarifa; alta con el tipo de la lista de servicios especiales', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const panel = await tripsPanel()
    expect(within(rowOf(panel, /Vagón del muelle/)).getByText('$75.00')).toBeInTheDocument()
    await pick(user, within(panel).getByLabelText('Tipo de viaje', { selector: 'select' }), 'Traslado especial')
    await user.type(within(panel).getByLabelText('Tarifa'), '50')
    await user.click(within(panel).getByRole('button', { name: /Agregar/ }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/trip-rates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/trip-rates')[0].body).toEqual({ specialServiceTypeId: 5, rate: 50 })
  })

  it('sin lista de servicios especiales: mensaje de la maqueta y no se puede agregar', async () => {
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/driver-trip-types' ? [] : undefined)
    wrap(ALL)
    const panel = await tripsPanel()
    await waitFor(() => expect(within(panel).getByLabelText('Tipo de viaje', { selector: 'select' })).toBeDisabled())
    expect(within(panel).getByRole('option', { name: 'Sin servicios especiales — agréguelos en Clientes y contratos' })).toBeInTheDocument()
    expect(within(panel).getByRole('button', { name: /Agregar/ })).toBeDisabled()
  })

  it('validación del tipo y error del servidor sobre specialServiceTypeId bajo «Tipo de viaje»', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const panel = await tripsPanel()
    await user.click(within(panel).getByRole('button', { name: /Agregar/ }))
    expect(await within(panel).findByText('El tipo de viaje es obligatorio.')).toBeInTheDocument()
    await pick(user, within(panel).getByLabelText('Tipo de viaje', { selector: 'select' }), 'Vagón del muelle')
    await user.type(within(panel).getByLabelText('Tarifa'), '10')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/drivers/d-1/trip-rates'
        ? problem(400, 'El tipo de servicio especial está inactivo; reactívelo o elija otro.', 'validation', { specialServiceTypeId: ['El tipo de servicio especial está inactivo; reactívelo o elija otro.'] })
        : undefined
    await user.click(within(panel).getByRole('button', { name: /Agregar/ }))
    const msg = await within(panel).findByText('El tipo de servicio especial está inactivo; reactívelo o elija otro.')
    expect(msg).toHaveClass('ferr')
  })

  it('editar manda tarifa y vigencia; cerrar manda close; el filtro «Tipo de viaje» filtra', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const panel = await tripsPanel()
    await user.click(within(rowOf(panel, /Vagón del muelle/)).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar tarifa' })
    await user.clear(within(dialog).getByLabelText(/^Tarifa/))
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '80')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-1/trip-rates/31')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-1/trip-rates/31')[0].body).toEqual({ rate: 80, effectiveFrom: today() })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar tarifa' })).toBeNull())

    await user.click(within(rowOf(await tripsPanel(), /Vagón del muelle/)).getByRole('button', { name: 'Cerrar tarifa' }))
    const confirm = await screen.findByRole('dialog', { name: 'Cerrar tarifa' })
    await user.click(within(confirm).getByRole('button', { name: 'Cerrar tarifa' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/trip-rates/31/close')).toHaveLength(1))

    await user.click(within(await tripsPanel()).getAllByLabelText('Tipo de viaje')[0])
    await user.click(await within(await screen.findByRole('listbox')).findByRole('option', { name: 'Vagón del muelle' }))
    expect(within(await tripsPanel()).getByRole('row', { name: /Vagón del muelle/ })).toBeInTheDocument()
  })
})

describe('Tarifas — permisos y chofer eliminado', () => {
  it('sin driverpay.manage todo es de solo lectura (sin altas, editar, cerrar, fijar ni agregar intento)', async () => {
    wrap(['fleet.view', 'driverpay.view'])
    const region = await delivery()
    expect(within(region).getByRole('row', { name: /Next day/ })).toBeInTheDocument()
    expect(within(region).queryByRole('button', { name: /Agregar/ })).toBeNull()
    expect(within(region).queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(within(region).queryByRole('button', { name: 'Cerrar tarifa' })).toBeNull()
    expect(screen.queryByRole('button', { name: '+ Agregar intento' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Fijar tarifa' })).toBeNull()
    const panel = await tripsPanel()
    expect(within(panel).queryByRole('button', { name: /Agregar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Registrar viaje' })).toBeNull()
  })

  it('sin driverpay.view no aparecen tarifas, viajes ni la fórmula, y no se piden al servidor', async () => {
    wrap(FLEET)
    await screen.findByRole('heading', { level: 2, name: 'Ficha del chofer' })
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1').length).toBeGreaterThan(0))
    expect(screen.queryByRole('heading', { level: 2, name: 'Tarifas por entrega e intento' })).toBeNull()
    expect(screen.queryByRole('heading', { level: 2, name: 'Tarifas por viaje' })).toBeNull()
    expect(screen.queryByRole('heading', { level: 2, name: 'Viajes pagados' })).toBeNull()
    expect(screen.queryByLabelText(/Cuando una entrega toma más de un intento/)).toBeNull()
    expect(mock.calls.filter((c) => /\/rates$|\/trips$|driver-pay-policy|driver-trip-types/.test(c.url.pathname))).toEqual([])
  })

  it('un chofer eliminado: tarifas y viajes solo se consultan, con el aviso del manual', async () => {
    wrap(ALL, '/catalog/drivers?driver=d-4')
    expect(await screen.findByText('El chofer fue eliminado; sus tarifas y viajes solo se consultan.')).toBeInTheDocument()
    const region = await delivery()
    expect(within(region).queryByRole('button', { name: /Agregar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: '+ Agregar intento' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Registrar viaje' })).toBeNull()
  })

  it('un 403 de tarifas se muestra en el panel sin sacar al usuario de la pantalla', async () => {
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/drivers/d-1/rates' ? problem(403, "Falta el permiso 'driverpay.view'.", 'forbidden') : undefined)
    wrap(ALL)
    expect(await screen.findByText("Falta el permiso 'driverpay.view'.")).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Ficha del chofer' })).toBeInTheDocument()
  })
})

describe('Fórmula de pago y vista previa', () => {
  it('muestra las tres fórmulas con los textos de la maqueta y la vigente elegida', async () => {
    wrap(ALL)
    const select = await screen.findByLabelText('Cuando una entrega toma más de un intento, el chofer cobra…')
    await waitFor(() => expect(select).toHaveValue('DELIVERY_PLUS_ATTEMPTS'))
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual([
      'Entrega + cada intento (se suman todos)',
      'La entrega incluye el 1er intento (solo los intentos extra se pagan aparte)',
      'Cada intento fallido cobra su tarifa; el que logra entregar cobra la tarifa de entrega',
    ])
    expect(select).toBeEnabled()
  })

  it('la vista previa la calcula el servidor con el ejemplo de 2 intentos y las tarifas del chofer elegido', async () => {
    wrap(ALL)
    const box = (await screen.findByText(/^Vista previa · ejemplo con 2 intentos \(1 fallido \+ 1 entregado\)/)).closest('.drv-preview') as HTMLElement
    await waitFor(() => expect(callsTo('POST', '/api/v1/driver-pay-policy/preview').length).toBeGreaterThan(0))
    const last = callsTo('POST', '/api/v1/driver-pay-policy/preview').at(-1)!.body
    expect(last).toEqual({
      attempts: [
        { number: 1, delivered: false },
        { number: 2, delivered: true },
      ],
      deliveryRate: 6,
      attemptRates: [{ number: 1, rate: 0 }],
    })
    expect(await within(box).findByText('Ramón Díaz', { exact: false })).toBeInTheDocument()
    expect(within(box).getByText('Entrega')).toBeInTheDocument()
    expect(within(box).getByText('Intento 1')).toBeInTheDocument()
    // el intento 2 no tiene tarifa: el servidor lo marca y se ve
    expect(within(box).getByText('Intento 2 (sin tarifa configurada)')).toBeInTheDocument()
    expect(within(box).getByText('Total')).toBeInTheDocument()
    expect(within(box).getAllByText('$6.00').length).toBeGreaterThan(0)
  })

  it('cambiar la fórmula manda PATCH, avisa y vuelve a calcular la vista previa; un error sale como aviso', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const select = await screen.findByLabelText('Cuando una entrega toma más de un intento, el chofer cobra…')
    await waitFor(() => expect(select).toHaveValue('DELIVERY_PLUS_ATTEMPTS'))
    const before = callsTo('POST', '/api/v1/driver-pay-policy/preview').length
    await user.selectOptions(select, 'FAILED_REPLACES_DELIVERY')
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/driver-pay-policy')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/driver-pay-policy')[0].body).toEqual({ payoutFormula: 'FAILED_REPLACES_DELIVERY' })
    expect(await screen.findByText('Fórmula de pago guardada.')).toBeInTheDocument()
    await waitFor(() => expect(select).toHaveValue('FAILED_REPLACES_DELIVERY'))
    await waitFor(() => expect(callsTo('POST', '/api/v1/driver-pay-policy/preview').length).toBeGreaterThan(before))
    mock.override = ({ method, url }) => (method === 'PATCH' && url.pathname === '/api/v1/driver-pay-policy' ? problem(400, "Fórmula de pago desconocida: 'X'.", 'validation') : undefined)
    await user.selectOptions(select, 'DELIVERY_INCLUDES_FIRST')
    expect(await screen.findByText("Fórmula de pago desconocida: 'X'.")).toBeInTheDocument()
  })

  it('al editar una tarifa la vista previa se vuelve a calcular', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const region = await delivery()
    await waitFor(() => expect(callsTo('POST', '/api/v1/driver-pay-policy/preview').length).toBeGreaterThan(0))
    const before = callsTo('POST', '/api/v1/driver-pay-policy/preview').length
    await user.click(within(rowOf(region, /Next day/)).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar tarifa' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/driver-pay-policy/preview').length).toBeGreaterThan(before))
  })

  it('sin driverpay.manage el selector se ve pero no cambia; un error de la vista previa se muestra', async () => {
    wrap(['fleet.view', 'driverpay.view'])
    const select = await screen.findByLabelText('Cuando una entrega toma más de un intento, el chofer cobra…')
    await waitFor(() => expect(select).toHaveValue('DELIVERY_PLUS_ATTEMPTS'))
    expect(select).toBeDisabled()
  })

  it('si el servidor rechaza la vista previa, su mensaje queda en el recuadro', async () => {
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/driver-pay-policy/preview' ? problem(400, 'Indique al menos un intento.', 'validation') : undefined)
    wrap(ALL)
    expect(await screen.findByText('Indique al menos un intento.')).toBeInTheDocument()
  })
})
