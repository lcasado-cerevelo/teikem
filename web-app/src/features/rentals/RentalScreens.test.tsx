// Lote F17 (Rentas F-R1) — pantallas de rentas sobre un fetch simulado: lista (filtros de la URL al API, vencimiento calculado,
// "Nueva renta" solo con rental.manage, `?rental=` abre la ficha), ficha (acciones según estatus y permiso, confirmaciones con
// el error del servidor tal cual, extensión con los 400 de fecha y motivo, equipos inactivos de una cancelada), selector de
// equipos por serie (solo SERIAL propios, series disponibles del almacén sin repetir, 409 del servidor) y alta (mensajes).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import RentalDetailScreen from './RentalDetailScreen'
import RentalListScreen from './RentalListScreen'
import type { RentalDto } from './rentalRules'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  rental: null as unknown,
  post: null as null | ((call: { method: string; url: URL; body: unknown }) => Response | unknown),
  clientsForbidden: false,
}))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const R = '99999999-0000-0000-0000-000000000001'
const WH = '11111111-1111-1111-1111-111111111111'
const P_SERIAL = 'aaaaaaaa-0000-0000-0000-000000000001'
const P_NONE = 'aaaaaaaa-0000-0000-0000-000000000002'

function rental(statusCode: string, extra: Partial<RentalDto> = {}): RentalDto {
  const status: Record<string, string> = { DRAFT: 'Borrador', SCHEDULED: 'Programada', ON_RENT: 'En renta', CANCELLED: 'Cancelada' }
  return {
    rental: {
      id: 7,
      publicId: R,
      number: 'REN-00007',
      clientPublicId: 'c1',
      clientName: 'Hospital Damas',
      locationPublicId: 'l1',
      locationName: 'Sala 3',
      locationCity: 'Ponce',
      warehousePublicId: WH,
      warehouseCode: 'ALM-01',
      startDate: '2026-10-05',
      pickupDate: '2026-10-10',
      originalPickupDate: '2026-10-10',
      daysToPickup: 5,
      isOverdue: false,
      statusCode,
      status: status[statusCode],
      units: 1,
      extensionCount: 0,
    },
    canEdit: statusCode === 'DRAFT' || statusCode === 'SCHEDULED',
    canSchedule: statusCode === 'DRAFT',
    canDispatch: statusCode === 'SCHEDULED',
    canExtend: statusCode === 'SCHEDULED' || statusCode === 'ON_RENT',
    canCancel: statusCode === 'DRAFT' || statusCode === 'SCHEDULED',
    rowVersion: 'AAAA',
    lines: [
      {
        id: 31,
        productPublicId: P_SERIAL,
        sku: 'CAMA-1',
        productName: 'Cama de hospital',
        serialNumber: 'SN-1',
        fromBinCode: 'A-01',
        isActive: statusCode !== 'CANCELLED',
        rate: { frequencyCode: 'MONTHLY', frequency: 'Mensual', amount: 150, currencyCode: 'USD' },
      },
    ],
    ...extra,
  }
}

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, status, code, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

function route(call: Call): unknown {
  const { method, url } = call
  const p = url.pathname
  if (method !== 'GET' && mock.post) return mock.post(call)
  if (method === 'GET' && p === '/api/v1/rentals')
    return {
      total: 2,
      skip: 0,
      take: 25,
      items: [
        rental('ON_RENT').rental,
        { ...rental('SCHEDULED').rental, publicId: 'r2', id: 8, number: 'REN-00008', daysToPickup: -2, isOverdue: true, pickupDate: '2026-10-03' },
      ],
    }
  if (method === 'GET' && p === `/api/v1/rentals/${R}`) return mock.rental ?? rental('DRAFT')
  if (method === 'GET' && p === `/api/v1/rentals/${R}/extensions`) return []
  if (method === 'GET' && p.startsWith('/api/v1/status/history/')) return []
  if (method === 'GET' && p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Principal', isActive: true }]
  if (method === 'GET' && p === '/api/v1/products')
    return {
      total: 2,
      items: [
        { publicId: P_SERIAL, sku: 'CAMA-1', name: 'Cama de hospital', trackingTypeCode: 'SERIAL', isOwn: true },
        { publicId: P_NONE, sku: 'SILLA-1', name: 'Silla de ruedas', trackingTypeCode: 'NONE', isOwn: true },
      ],
    }
  if (method === 'GET' && p === `/api/v1/products/${P_SERIAL}/serials`)
    return [
      { id: 1, serialNumber: 'SN-1', statusCode: 'AVAILABLE', warehousePublicId: WH, binId: 10, binCode: 'A-01' },
      { id: 2, serialNumber: 'SN-2', statusCode: 'AVAILABLE', warehousePublicId: WH, binId: 10, binCode: 'A-01' },
      { id: 3, serialNumber: 'SN-Q', statusCode: 'AVAILABLE', warehousePublicId: WH, binId: 11, binCode: 'CUA-1' },
      { id: 4, serialNumber: 'SN-OTRO', statusCode: 'AVAILABLE', warehousePublicId: 'otro', binId: 99, binCode: 'Z-9' },
    ]
  if (method === 'GET' && p === '/api/v1/inventory/balances')
    return {
      total: 2,
      items: [
        { binId: 10, binCode: 'A-01', zoneTypeCode: 'RESERVE', qtyOnHand: 2 },
        { binId: 11, binCode: 'CUA-1', zoneTypeCode: 'QUARANTINE', qtyOnHand: 1 },
      ],
    }
  if (method === 'GET' && p === '/api/v1/tenant/settings') return { currencyCode: 'USD' }
  if (method === 'GET' && p === '/api/v1/clients' && mock.clientsForbidden) return problem(403, 'Falta el permiso.', 'forbidden')
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  if (method === 'GET') return []
  return mock.rental ?? rental('DRAFT')
}

function wrap(path: string, permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['RENTAL_EQUIPMENT', 'WMS_LOTSERIAL', 'CATALOG']}>
          <Routes>
            <Route path="/warehouse/rentals" element={<RentalListScreen />} />
            <Route path="/warehouse/rentals/:publicId" element={<RentalDetailScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const VIEW = ['rental.view']
const MANAGE = ['rental.view', 'rental.manage', 'inventory.view']
const ALL = [...MANAGE, 'rental.extend']
const listGets = () => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === '/api/v1/rentals')
const posts = (suffix: string) => mock.calls.filter((c) => c.method !== 'GET' && c.url.pathname.endsWith(suffix))

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.rental = null
  mock.post = null
  mock.clientsForbidden = false
})

describe('Rentas: lista', () => {
  it('los filtros de la URL ("Ver todos" del aviso) van al API; vencimiento calculado y estatus con texto', async () => {
    wrap('/warehouse/rentals?dueWithinDays=7&overdue=true', VIEW)
    await screen.findAllByText('REN-00007')
    const q = listGets()[0].url.searchParams
    expect(q.get('dueWithinDays')).toBe('7')
    expect(q.get('overdue')).toBe('true')
    expect(screen.getByLabelText('Vencen en (días)')).toHaveValue(7)
    expect(screen.getByRole('switch', { name: 'Solo vencidas' })).toBeChecked()
    expect(screen.getAllByText('Vence en 5 días').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Vencida hace 2 días').length).toBeGreaterThan(0)
    expect(screen.getAllByText('En renta').length).toBeGreaterThan(0)
    expect(screen.getByText(/Con los dos filtros se ven las vencidas/)).toBeInTheDocument()
    // solo lectura: sin "Nueva renta"
    expect(screen.queryByRole('button', { name: 'Nueva renta' })).toBeNull()
  })

  it('estatus, "solo vencidas" y la búsqueda libre van al API (página 1); "Limpiar" quita todo', async () => {
    const user = userEvent.setup()
    wrap('/warehouse/rentals', MANAGE)
    await screen.findAllByText('REN-00007')
    expect(screen.getByRole('button', { name: 'Nueva renta' })).toBeInTheDocument()
    await user.click(screen.getByRole('switch', { name: 'Solo vencidas' }))
    await waitFor(() => expect(listGets().at(-1)!.url.searchParams.get('overdue')).toBe('true'))
    await user.type(screen.getByRole('searchbox'), 'SN-1')
    await waitFor(() => expect(listGets().at(-1)!.url.searchParams.get('search')).toBe('SN-1'))
    expect(listGets().at(-1)!.url.searchParams.get('skip')).toBe('0')
    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => {
      const last = listGets().at(-1)!.url.searchParams
      expect(last.get('overdue')).toBeNull()
      expect(last.get('search')).toBeNull()
    })
  })

  it('?rental=<publicId> ("Revisar" del aviso RENTAL_DUE) abre la ficha', async () => {
    wrap(`/warehouse/rentals?rental=${R}`, VIEW)
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('REN-00007 · Hospital Damas')
  })
})

describe('Rentas: ficha', () => {
  it('Borrador con rental.manage: Editar, Agregar equipos, Programar y Cancelar; sin Despachar ni Extender', async () => {
    wrap(`/warehouse/rentals/${R}`, ALL)
    await screen.findByRole('heading', { level: 1 })
    for (const name of ['Editar', 'Agregar equipos', 'Programar', 'Cancelar renta']) expect(screen.getByRole('button', { name })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Despachar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Extender' })).toBeNull()
    expect(screen.getAllByText('$150.00 · Mensual').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Por despachar').length).toBeGreaterThan(0)
  })

  it('solo rental.view: ninguna acción; En renta con rental.extend: solo Extender', async () => {
    const view = wrap(`/warehouse/rentals/${R}`, VIEW)
    await screen.findByRole('heading', { level: 1 })
    expect(screen.queryByRole('button', { name: /Editar|Programar|Despachar|Extender|Cancelar renta/ })).toBeNull()
    view.unmount()
    mock.rental = rental('ON_RENT')
    wrap(`/warehouse/rentals/${R}`, ['rental.view', 'rental.extend'])
    await screen.findByRole('heading', { level: 1 })
    expect(screen.getByRole('button', { name: 'Extender' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Editar|Programar|Despachar|Cancelar renta/ })).toBeNull()
  })

  it('Programar: confirmación con comentario; el 422 del servidor se muestra tal cual y luego programa', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/rentals/${R}`, MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Programar' }))
    const dialog = screen.getByRole('dialog', { name: '¿Programar la renta REN-00007?' })
    expect(within(dialog).getByText(/quedan reservados para esta renta/)).toBeInTheDocument()
    mock.post = () => problem(422, 'La renta no tiene equipos; agregue al menos uno.', 'status_rule')
    await user.type(within(dialog).getByLabelText(/Comentario/), 'Confirmado')
    await user.click(within(dialog).getByRole('button', { name: 'Programar' }))
    expect(await within(dialog).findByText('La renta no tiene equipos; agregue al menos uno.')).toBeInTheDocument()
    mock.post = () => rental('SCHEDULED')
    await user.click(within(dialog).getByRole('button', { name: 'Programar' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(posts('/schedule').at(-1)!.body).toEqual({ comment: 'Confirmado', rowVersion: 'AAAA' })
    expect(await screen.findByRole('button', { name: 'Despachar' })).toBeInTheDocument()
  })

  it('Cancelar una Programada avisa que libera las reservas; el 422 del servidor queda en el diálogo', async () => {
    const user = userEvent.setup()
    mock.rental = rental('SCHEDULED')
    wrap(`/warehouse/rentals/${R}`, MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Cancelar renta' }))
    const dialog = screen.getByRole('dialog', { name: '¿Cancelar la renta REN-00007?' })
    expect(within(dialog).getByText(/se liberan las reservas/)).toBeInTheDocument()
    mock.post = () => problem(422, 'Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución.', 'status_rule')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar la renta' }))
    expect(await within(dialog).findByText('Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución.')).toBeInTheDocument()
  })

  it('Extender: el 400 de fecha y el de motivo (en pantalla y del servidor) bajo su campo; luego extiende con tarifa', async () => {
    const user = userEvent.setup()
    mock.rental = rental('ON_RENT')
    wrap(`/warehouse/rentals/${R}`, ALL)
    await user.click(await screen.findByRole('button', { name: 'Extender' }))
    const dialog = screen.getByRole('dialog', { name: 'Extender la renta REN-00007' })
    const date = within(dialog).getByLabelText(/Nueva fecha de recogido/)
    await user.type(date, '2026-10-10')
    await user.click(within(dialog).getByRole('button', { name: 'Extender' }))
    expect(await within(dialog).findByText('La nueva fecha de recogido debe ser posterior a la actual (2026-10-10).')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el motivo de la extensión.')).toBeInTheDocument()
    expect(posts('/extensions')).toHaveLength(0)

    await user.clear(date)
    await user.type(date, '2026-10-20')
    await user.type(within(dialog).getByLabelText(/^Motivo/), 'Dos semanas más')
    // el servidor manda su 400 (p. ej. otro usuario ya extendió): queda bajo el campo
    mock.post = () => problem(400, 'La nueva fecha de recogido debe ser posterior a la actual (2026-10-25).', 'validation', {
      newPickupDate: ['La nueva fecha de recogido debe ser posterior a la actual (2026-10-25).'],
    })
    await user.click(within(dialog).getByRole('button', { name: 'Extender' }))
    expect(await within(dialog).findByText('La nueva fecha de recogido debe ser posterior a la actual (2026-10-25).')).toBeInTheDocument()

    mock.post = () => rental('ON_RENT', { rental: { ...rental('ON_RENT').rental, pickupDate: '2026-10-20' } })
    await user.selectOptions(within(dialog).getByLabelText('Frecuencia de cobro'), 'MONTHLY')
    await user.type(within(dialog).getByLabelText('Monto'), '165')
    await user.click(within(dialog).getByRole('button', { name: 'Extender' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(posts('/extensions').at(-1)!.body).toEqual({
      newPickupDate: '2026-10-20',
      reason: 'Dos semanas más',
      rates: [{ lineId: 31, frequency: 'MONTHLY', amount: 165, currency: null }],
      rowVersion: 'AAAA',
    })
  })

  it('Tarifa de un equipo (antes del despacho): monto negativo con el mensaje exacto; luego PUT con el rowVersion', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/rentals/${R}`, MANAGE)
    await user.click((await screen.findAllByRole('button', { name: 'Tarifa' }))[0])
    const dialog = screen.getByRole('dialog', { name: 'Tarifa del equipo SN-1' })
    expect(within(dialog).getByLabelText(/Frecuencia de cobro/)).toHaveValue('MONTHLY')
    const amount = within(dialog).getByLabelText('Monto')
    await user.clear(amount)
    await user.type(amount, '-1')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar tarifa' }))
    expect(within(dialog).getByText('La tarifa no puede ser negativa.')).toBeInTheDocument()
    await user.clear(amount)
    await user.type(amount, '99')
    await user.selectOptions(within(dialog).getByLabelText(/Frecuencia de cobro/), 'ONE_TIME')
    mock.post = () => rental('DRAFT')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar tarifa' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const put = mock.calls.find((c) => c.method === 'PUT')!
    expect(put.url.pathname).toBe(`/api/v1/rentals/${R}/lines/31/rate`)
    expect(put.body).toEqual({ frequency: 'ONE_TIME', amount: 99, currency: 'USD', rowVersion: 'AAAA' })
  })

  it('renta cancelada: sus equipos se ven inactivos (atenuados) y no hay acciones', async () => {
    mock.rental = rental('CANCELLED')
    wrap(`/warehouse/rentals/${R}`, ALL)
    await screen.findByRole('heading', { level: 1 })
    expect(screen.getAllByText('Inactivo').length).toBeGreaterThan(0)
    expect(screen.getByText(/Renta cancelada: estos son los equipos que tenía/)).toBeInTheDocument()
    expect(document.querySelector('tr.dim, .dt-card.dim')).not.toBeNull()
    expect(screen.queryByRole('button', { name: /Editar|Programar|Despachar|Extender|Cancelar renta/ })).toBeNull()
  })
})

describe('Rentas: selector de equipos por serie', () => {
  it('solo SERIAL (mensaje exacto si no); series disponibles del almacén, sin cuarentena ni las ya rentadas; 409 del servidor tal cual', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/rentals/${R}`, MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Agregar equipos' }))
    const dialog = screen.getByRole('dialog', { name: 'Agregar equipos a la renta REN-00007' })
    const product = within(dialog).getByRole('combobox', { name: 'Equipo (SKU o nombre)' })
    await user.click(product)
    await user.click(await within(dialog).findByRole('option', { name: /SILLA-1/ }))
    expect(await within(dialog).findByText(/El producto SILLA-1 no se controla por serie; solo se rentan equipos con número de serie\./)).toBeInTheDocument()
    expect(within(dialog).getByText(/Convertir a serie/)).toBeInTheDocument()

    await user.clear(product)
    await user.type(product, 'CAMA')
    await user.click(await within(dialog).findByRole('option', { name: /CAMA-1/ }))
    // SN-1 ya está en la renta, SN-Q en cuarentena y SN-OTRO en otro almacén: solo SN-2
    expect(await within(dialog).findByText('Series disponibles en ALM-01 (1)')).toBeInTheDocument()
    expect(within(dialog).getByRole('checkbox', { name: /SN-2/ })).toBeInTheDocument()
    expect(within(dialog).queryByRole('checkbox', { name: /SN-1|SN-Q|SN-OTRO/ })).toBeNull()
    expect(within(dialog).getByText(/1 serie\(s\) en zonas que no se rentan/)).toBeInTheDocument()

    // escanear una serie que ya está en la renta avisa
    const scan = within(dialog).getByLabelText('Buscar o escanear serie')
    await user.type(scan, 'sn-1{Enter}')
    expect(within(dialog).getByText('La serie sn-1 ya está en esta renta.')).toBeInTheDocument()
    await user.clear(scan)
    await user.type(scan, 'SN-2{Enter}')
    expect(within(dialog).getByRole('checkbox', { name: /SN-2/ })).toBeChecked()

    mock.post = () => problem(409, 'La serie SN-2 ya está en la renta REN-00003.', 'conflict')
    await user.click(within(dialog).getByRole('button', { name: 'Agregar 1 equipo(s)' }))
    expect(await within(dialog).findByText('La serie SN-2 ya está en la renta REN-00003.')).toBeInTheDocument()
    expect(within(dialog).getByRole('checkbox', { name: /SN-2/ })).toBeChecked()
    expect(posts('/lines').at(-1)!.body).toEqual({ productPublicId: P_SERIAL, serialNumbers: ['SN-2'] })
  })
})

describe('Rentas: alta', () => {
  it('sin datos: los mensajes del servidor bajo cada campo, sin llamar al API', async () => {
    const user = userEvent.setup()
    wrap('/warehouse/rentals', MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Nueva renta' }))
    const dialog = screen.getByRole('dialog', { name: 'Nueva renta (Borrador)' })
    // con un solo almacén activo ya viene elegido
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /Almacén de origen/ })).toHaveValue('ALM-01 · Principal'))
    await user.type(within(dialog).getByLabelText(/Costo de transporte estimado/), '-5')
    await user.click(within(dialog).getByRole('button', { name: 'Crear renta con 0 equipo(s)' }))
    for (const msg of ['Indique el cliente de la renta.', 'Indique la localidad del cliente donde estará el equipo.', 'Indique la fecha de recogido.', 'El costo de transporte estimado no puede ser negativo.'])
      expect(await within(dialog).findByText(msg)).toBeInTheDocument()
    expect(mock.calls.filter((c) => c.method === 'POST')).toHaveLength(0)
  })

  it('sin clients.read (p. ej. Operador de almacén): el cliente avisa sin sacar de la pantalla', async () => {
    const user = userEvent.setup()
    mock.clientsForbidden = true
    wrap('/warehouse/rentals', MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Nueva renta' }))
    const dialog = screen.getByRole('dialog', { name: 'Nueva renta (Borrador)' })
    await user.click(within(dialog).getByRole('combobox', { name: /^Cliente/ }))
    expect(await within(dialog).findByText('Su usuario no puede consultar clientes.')).toBeInTheDocument()
    expect(within(dialog).getByText('Elija primero el cliente.')).toBeInTheDocument()
  })
})
