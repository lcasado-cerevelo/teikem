// Lote F-D1 — Viajes pagados del chofer: lista con filtros de fecha y estatus (van al servidor), «Sin tarifa», enlace a la orden,
// «Registrar viaje» (validaciones del manual 04 §9, errores del servidor), «Cancelar viaje» con comentario y el 409 de un viaje que nace
// de una entrega especial, y permisos.
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { tenantToday } from '../../kernel/api/tenantZone'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, mock, problem, resetKit, TRIPS, wrap } from './driversTestKit'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./driversMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const panel = async () => (await screen.findByRole('heading', { level: 2, name: 'Viajes pagados' })).closest('section') as HTMLElement
const tripRow = (scope: HTMLElement, text: RegExp) => within(scope).getByRole('row', { name: text })
const today = () => tenantToday()
const tomorrow = () => {
  const d = new Date(`${today()}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + 1)
  return d.toISOString().slice(0, 10)
}

describe('Viajes pagados — lista', () => {
  it('fecha, tipo, monto, orden, estatus y notas; un viaje sin tarifa lo dice y el de una orden enlaza a ella', async () => {
    wrap(ALL)
    const p = await panel()
    const t1 = await within(p).findByRole('row', { name: /Vagón del muelle/ })
    expect(within(t1).getByText('$75.00')).toBeInTheDocument()
    expect(within(t1).getByText('Por liquidar')).toBeInTheDocument()
    expect(within(t1).getByText('Muelle 3')).toBeInTheDocument()
    const t2 = tripRow(p, /Traslado especial/)
    expect(within(t2).getByText('Sin tarifa')).toBeInTheDocument()
    expect(within(t2).getByText('$0.00')).toBeInTheDocument()
    expect(within(t2).getByRole('link', { name: 'ORD-0001' })).toHaveAttribute('href', '/orders/o-1')
    // por omisión solo vigentes
    expect(callsTo('GET', '/api/v1/drivers/d-1/trips')[0].url.searchParams.get('includeCancelled')).toBeNull()
  })

  it('el filtro de estatus y el de fechas van al servidor; «Limpiar» los quita', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const p = await panel()
    await within(p).findByRole('row', { name: /Vagón del muelle/ })
    await within(p).findByRole('option', { name: 'Cancelado' })
    await user.selectOptions(within(p).getByLabelText('Estatus'), 'Cancelado')
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1/trips').some((c) => c.url.searchParams.getAll('status').join() === 'CANCELLED')).toBe(true))
    expect(await within(p).findByText('No hay viajes pagados con estos filtros.')).toBeInTheDocument()
    await user.click(within(p).getByRole('button', { name: 'Limpiar' }))
    await within(p).findByRole('row', { name: /Vagón del muelle/ })
    await userEvent.setup().selectOptions(within(p).getByRole('group', { name: /Fechas|Fecha/ }).querySelector('select[id$="-preset"]') as HTMLSelectElement, 'custom')   // «Personalizado» muestra Desde / Hasta
    fireEvent.change(within(p).getByLabelText('Desde'), { target: { value: '2026-10-10' } })
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1/trips').some((c) => c.url.searchParams.get('from') === '2026-10-10')).toBe(true))
    await waitFor(() => expect(within(p).queryByRole('row', { name: /Traslado especial/ })).toBeNull())
    fireEvent.change(within(p).getByLabelText('Hasta'), { target: { value: '2026-10-11' } })
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1/trips').some((c) => c.url.searchParams.get('to') === '2026-10-11')).toBe(true))
  })

  it('un rango invertido lo rechaza el servidor y su mensaje se ve bajo los filtros', async () => {
    mock.override = ({ method, url }) =>
      method === 'GET' && url.pathname === '/api/v1/drivers/d-1/trips' && url.searchParams.get('from') ? problem(400, 'La fecha inicial no puede ser posterior a la final.', 'validation') : undefined
    wrap(ALL)
    const p = await panel()
    await within(p).findByRole('row', { name: /Vagón del muelle/ })
    await userEvent.setup().selectOptions(within(p).getByRole('group', { name: 'Fechas' }).querySelector('select[id$="-preset"]') as HTMLSelectElement, 'custom')
    fireEvent.change(within(p).getByLabelText('Desde'), { target: { value: '2026-10-10' } })
    expect(await within(p).findByText('La fecha inicial no puede ser posterior a la final.')).toBeInTheDocument()
  })
})

describe('Registrar viaje', () => {
  it('POST con tipo, fecha (hoy por omisión) y notas recortadas; avisa y vuelve a leer la lista', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const p = await panel()
    await within(p).findByRole('row', { name: /Vagón del muelle/ })
    await user.click(within(p).getByRole('button', { name: /Registrar viaje/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Registrar viaje pagado' })
    await within(dialog).findByRole('option', { name: 'Vagón del muelle' })
    expect(within(dialog).getByLabelText(/^Fecha/)).toHaveValue(today())
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de viaje/), 'Vagón del muelle')
    await user.type(within(dialog).getByLabelText('Notas'), '  Muelle 5 ')
    const before = callsTo('GET', '/api/v1/drivers/d-1/trips').length
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/trips')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/trips')[0].body).toEqual({ specialServiceTypeId: 4, tripDate: today(), notes: 'Muelle 5' })
    expect(await screen.findByText('Viaje registrado.')).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1/trips').length).toBeGreaterThan(before))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Registrar viaje pagado' })).toBeNull())
  })

  it('validaciones del manual sin llamar al servidor: tipo obligatorio, fecha no futura, notas hasta 500', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const p = await panel()
    await user.click(await within(p).findByRole('button', { name: /Registrar viaje/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Registrar viaje pagado' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El tipo de viaje es obligatorio.')).toBeInTheDocument()
    await within(dialog).findByRole('option', { name: 'Vagón del muelle' })
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de viaje/), 'Vagón del muelle')
    fireEvent.change(within(dialog).getByLabelText(/^Fecha/), { target: { value: tomorrow() } })
    fireEvent.change(within(dialog).getByLabelText('Notas'), { target: { value: 'x'.repeat(501) } })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La fecha del viaje no puede ser futura.')).toBeInTheDocument()
    expect(within(dialog).getByText('Las notas del viaje admiten como máximo 500 caracteres.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/drivers/d-1/trips')).toHaveLength(0)
  })

  it('un error del servidor sobre el tipo sale bajo «Tipo de viaje»; el modal sigue abierto', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const p = await panel()
    await user.click(await within(p).findByRole('button', { name: /Registrar viaje/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Registrar viaje pagado' })
    await within(dialog).findByRole('option', { name: 'Vagón del muelle' })
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de viaje/), 'Vagón del muelle')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/drivers/d-1/trips'
        ? problem(400, 'El tipo de servicio especial está inactivo; reactívelo o elija otro.', 'validation', { specialServiceTypeId: ['El tipo de servicio especial está inactivo; reactívelo o elija otro.'] })
        : undefined
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El tipo de servicio especial está inactivo; reactívelo o elija otro.')).toHaveClass('ferr')
    expect(screen.getByRole('dialog', { name: 'Registrar viaje pagado' })).toBeInTheDocument()
  })
})

describe('Cancelar viaje', () => {
  it('pide confirmar con un comentario opcional y manda POST cancel; avisa', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const p = await panel()
    await user.click(within(await within(p).findByRole('row', { name: /Vagón del muelle/ })).getByRole('button', { name: 'Cancelar viaje' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar viaje' })
    expect(within(dialog).getByText(/Vagón del muelle/)).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/drivers/d-1/trips/t-1/cancel')).toHaveLength(0)
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), 'Duplicado')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar viaje' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/trips/t-1/cancel')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/trips/t-1/cancel')[0].body).toEqual({ comment: 'Duplicado' })
    expect(await screen.findByText('Viaje cancelado.')).toBeInTheDocument()
  })

  it('sin comentario manda comment null; «Volver» cierra sin cancelar nada', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const p = await panel()
    await user.click(within(await within(p).findByRole('row', { name: /Vagón del muelle/ })).getByRole('button', { name: 'Cancelar viaje' }))
    let dialog = await screen.findByRole('dialog', { name: 'Cancelar viaje' })
    await user.click(within(dialog).getByRole('button', { name: 'Volver' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Cancelar viaje' })).toBeNull())
    expect(callsTo('POST', '/api/v1/drivers/d-1/trips/t-1/cancel')).toHaveLength(0)
    await user.click(within(tripRow(p, /Vagón del muelle/)).getByRole('button', { name: 'Cancelar viaje' }))
    dialog = await screen.findByRole('dialog', { name: 'Cancelar viaje' })
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar viaje' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/trips/t-1/cancel')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/trips/t-1/cancel')[0].body).toEqual({ comment: null })
  })

  it('un viaje que nace de una entrega especial no se cancela directo: el 409 del servidor queda en el diálogo', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/drivers/d-1/trips/t-2/cancel' ? problem(409, 'El viaje nace de una entrega especial; cancele la orden o reasigne el chofer.', 'conflict') : undefined
    wrap(ALL)
    const p = await panel()
    await user.click(within(await within(p).findByRole('row', { name: /Traslado especial/ })).getByRole('button', { name: 'Cancelar viaje' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar viaje' })
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar viaje' }))
    expect(await within(dialog).findByText('El viaje nace de una entrega especial; cancele la orden o reasigne el chofer.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Cancelar viaje' })).toBeInTheDocument()
  })

  it('un viaje liquidado o cancelado no ofrece «Cancelar viaje»', async () => {
    mock.override = ({ method, url }) =>
      method === 'GET' && url.pathname === '/api/v1/drivers/d-1/trips'
        ? [
            { ...TRIPS[0], publicId: 't-9', statusCode: 'SETTLED', status: 'Liquidado' },
            { ...TRIPS[1], publicId: 't-8', statusCode: 'CANCELLED', status: 'Cancelado', isActive: false },
          ]
        : undefined
    wrap(ALL)
    const p = await panel()
    await within(p).findByRole('row', { name: /Vagón del muelle/ })
    expect(within(p).queryByRole('button', { name: 'Cancelar viaje' })).toBeNull()
  })
})

describe('Viajes pagados — permisos', () => {
  it('sin driverpay.manage se ven los viajes pero no hay «Registrar viaje» ni «Cancelar viaje»', async () => {
    wrap(['fleet.view', 'driverpay.view'])
    const p = await panel()
    await within(p).findByRole('row', { name: /Vagón del muelle/ })
    expect(within(p).queryByRole('button', { name: /Registrar viaje/ })).toBeNull()
    expect(within(p).queryByRole('button', { name: 'Cancelar viaje' })).toBeNull()
  })

  it('un chofer eliminado conserva sus viajes a la vista pero sin botones', async () => {
    wrap(ALL, '/catalog/drivers?driver=d-4')
    const p = await panel()
    await waitFor(() => expect(within(p).queryByRole('button', { name: /Registrar viaje/ })).toBeNull())
    expect(within(p).queryByRole('button', { name: 'Cancelar viaje' })).toBeNull()
  })
})
