// Lote F18 (Rentas F-R2) — pantallas de devoluciones, proceso y reportes sobre un fetch simulado: "Registrar devolución" desde la
// ficha (solo con rental.return y una renta En renta; "Otro" sin notas con el mensaje del servidor; devolución parcial con
// condición, destino y proceso por equipo; 409 del servidor tal cual), lista de devoluciones (filtros de la URL al API, enlaces a la
// renta), ficha de la devolución (equipos con su proceso y enlace a la renta), cola de proceso (acciones por permiso, "Dar de baja"
// solo con inventory.adjust y confirmación escribiendo la serie, 422 del motor tal cual), resumen de la lista de rentas y reportes
// de rentas (vistas de Análisis con totales, indicadores).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import RentalDetailScreen from './RentalDetailScreen'
import RentalListScreen from './RentalListScreen'
import RentalProcessListScreen from './RentalProcessListScreen'
import RentalReportsScreen from './RentalReportsScreen'
import RentalReturnDetailScreen from './RentalReturnDetailScreen'
import RentalReturnListScreen from './RentalReturnListScreen'
import type { RentalDto } from './rentalRules'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  post: null as null | ((call: { method: string; url: URL; body: unknown }) => Response | unknown),
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
const RET = '88888888-0000-0000-0000-000000000001'
const WH = '11111111-1111-1111-1111-111111111111'

const onRent: RentalDto = {
  rental: {
    id: 7,
    publicId: R,
    number: 'REN-00007',
    clientName: 'Hospital Damas',
    warehousePublicId: WH,
    warehouseCode: 'ALM-01',
    startDate: '2026-01-05',
    pickupDate: '2099-12-31',
    originalPickupDate: '2099-12-31',
    daysToPickup: 30,
    isOverdue: false,
    statusCode: 'ON_RENT',
    status: 'En renta',
    units: 2,
  },
  canExtend: true,
  rowVersion: 'RV',
  lines: [
    { id: 31, sku: 'CAMA-1', productName: 'Cama', serialNumber: 'SN-1', fromBinCode: 'A-01', isActive: true, dispatchedAtUtc: '2026-10-02T10:00:00' },
    { id: 32, sku: 'CAMA-1', productName: 'Cama', serialNumber: 'SN-2', fromBinCode: 'A-02', isActive: true, dispatchedAtUtc: '2026-10-02T10:00:00' },
  ],
}

const retItem = {
  id: 3,
  publicId: RET,
  number: 'DRN-00003',
  rentalPublicId: R,
  rentalNumber: 'REN-00007',
  clientName: 'Hospital Damas',
  returnedOn: '2026-10-05',
  reasonCode: 'EARLY_DAMAGE',
  reason: 'Anticipada por daño',
  isEarly: true,
  units: 1,
  openProcesses: 1,
  createdAtUtc: '2026-10-05T14:00:00',
}

const proc = (id: number, serial: string, statusCode: string, extra: Record<string, unknown> = {}) => ({
  id,
  serialNumber: serial,
  sku: 'CAMA-1',
  productName: 'Cama',
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  binCode: 'CUA-1',
  statusCode,
  status: { PENDING: 'Pendiente', INSPECTION: 'Inspección', READY: 'Lista' }[statusCode] ?? statusCode,
  isFinished: statusCode === 'READY',
  returnPublicId: RET,
  returnNumber: 'DRN-00003',
  rentalPublicId: R,
  rentalNumber: 'REN-00007',
  conditionCode: 'DAMAGED',
  startedAtUtc: '2026-10-01T14:00:00',
  rowVersion: 'PV',
  ...extra,
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, status, code, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const lookups: Record<string, { code: string; label: string }[]> = {
  RentalReturnReason: [
    { code: 'END_OF_CONTRACT', label: 'Fin del contrato' },
    { code: 'EARLY_DAMAGE', label: 'Anticipada por daño' },
    { code: 'OTHER', label: 'Otro' },
  ],
  RentalReturnCondition: [
    { code: 'GOOD', label: 'Buena' },
    { code: 'DAMAGED', label: 'Dañado' },
  ],
}
const processStatuses = [
  { code: 'PENDING', label: 'Pendiente', stageKind: 'PIPELINE', isInitial: true, sortOrder: 10 },
  { code: 'INSPECTION', label: 'Inspección', stageKind: 'PIPELINE', sortOrder: 20 },
  { code: 'TESTING', label: 'Pruebas', stageKind: 'PIPELINE', sortOrder: 40 },
  { code: 'READY', label: 'Lista', stageKind: 'TERMINAL', sortOrder: 50 },
  { code: 'REPAIR', label: 'Reparación', stageKind: 'LATERAL', sortOrder: 60 },
  { code: 'SCRAPPED', label: 'Dada de baja', stageKind: 'TERMINAL', sortOrder: 80 },
]

function route(call: Call): unknown {
  const { method, url } = call
  const p = url.pathname
  if (method !== 'GET' && mock.post) return mock.post(call)
  if (method === 'GET' && p === `/api/v1/rentals/${R}`) return onRent
  if (method === 'GET' && p === '/api/v1/rentals') {
    const q = url.searchParams
    if (q.get('take') === '1') return { total: q.get('status') === 'ON_RENT' ? 4 : q.get('overdue') === 'true' ? 1 : 2, items: [] }
    return { total: 1, skip: 0, take: 25, items: [onRent.rental] }
  }
  if (method === 'GET' && p === '/api/v1/rental-returns') return { total: 1, skip: 0, take: 25, items: [retItem] }
  if (method === 'GET' && p === `/api/v1/rental-returns/${RET}`)
    return {
      return: retItem,
      rentalStatusCode: 'ON_RENT',
      notes: 'Pantalla rota',
      createdByName: 'Admin',
      lines: [
        {
          id: 1,
          sku: 'CAMA-1',
          productName: 'Cama',
          serialNumber: 'SN-1',
          conditionCode: 'DAMAGED',
          condition: 'Dañado',
          toWarehouseCode: 'ALM-01',
          toBinCode: 'CUA-1',
          requiresProcess: true,
          processId: 5,
          processStatusCode: 'PENDING',
          processStatus: 'Pendiente',
        },
      ],
    }
  if (method === 'GET' && p === '/api/v1/rental-processes') return { total: 2, skip: 0, take: 25, items: [proc(5, 'SN-1', 'INSPECTION'), proc(6, 'SN-9', 'READY', { completedAtUtc: '2026-10-03T14:00:00' })] }
  if (method === 'GET' && p === '/api/v1/analytics/reports')
    return [
      { id: 40, name: 'Equipos en renta por cliente', baseEntityType: 'RENTAL', isSystem: true, description: 'Por cliente' },
      { id: 41, name: 'Rentas vencidas', baseEntityType: 'RENTAL', isSystem: true },
      { id: 9, name: 'Órdenes', baseEntityType: 'TRANSPORT_ORDER', isSystem: true },
    ]
  if (method === 'GET' && p === '/api/v1/analytics/indicators')
    return [
      { id: 70, name: 'Rentas vencidas', dataSource: 'RENTAL', isSystem: true },
      { id: 71, name: 'Órdenes abiertas', dataSource: 'TRANSPORT_ORDER', isSystem: true },
    ]
  if (method === 'GET' && p === '/api/v1/analytics/indicators/70/value') return { id: 70, value: 3 }
  if (method === 'GET' && p === '/api/v1/analytics/charts') return []
  if (method === 'GET' && p.startsWith('/api/v1/catalogs/')) return lookups[p.split('/').pop() ?? ''] ?? []
  if (method === 'GET' && p === '/api/v1/status/RentalProcessStatus') return processStatuses
  if (p.startsWith('/api/v1/status/')) return []
  if (method === 'POST' && p === '/api/v1/analytics/reports/40/run')
    return {
      columns: [
        { key: 'ClientName', label: 'Cliente', type: 'Text' },
        { key: 'count', label: 'Rentas', type: 'Number' },
        { key: 'sum_UnitsOnRent', label: 'Equipos en el cliente', type: 'Number' },
      ],
      rows: [{ ClientName: 'Hospital Damas', count: 2, sum_UnitsOnRent: 5 }],
      totals: { count: 2, sum_UnitsOnRent: 5 },
      total: 1,
    }
  if (method === 'GET') return []
  return {}
}

function wrap(path: string, permissions: string[], modules = ['RENTAL_EQUIPMENT', 'WMS_LOTSERIAL', 'ANALYTICS']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={modules}>
          <Routes>
            <Route path="/warehouse/rentals" element={<RentalListScreen />} />
            <Route path="/warehouse/rentals/:publicId" element={<RentalDetailScreen />} />
            <Route path="/warehouse/rental-returns" element={<RentalReturnListScreen />} />
            <Route path="/warehouse/rental-returns/:publicId" element={<RentalReturnDetailScreen />} />
            <Route path="/warehouse/rental-processes" element={<RentalProcessListScreen />} />
            <Route path="/warehouse/rental-reports" element={<RentalReportsScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const posts = (suffix: string) => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname.endsWith(suffix))
const gets = (path: string) => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.post = null
})

describe('Registrar devolución (ficha de la renta)', () => {
  it('solo con rental.return; "Otro" sin notas da el mensaje del servidor; parcial con condición, proceso y destino por equipo', async () => {
    const user = userEvent.setup()
    const { unmount } = wrap(`/warehouse/rentals/${R}`, ['rental.view'])
    await screen.findByRole('heading', { level: 1, name: /REN-00007/ })
    expect(screen.queryByRole('button', { name: 'Registrar devolución' })).toBeNull()
    // panel de devoluciones de la renta con enlace a la ficha de la devolución
    expect(await screen.findByRole('link', { name: 'DRN-00003' })).toHaveAttribute('href', `/warehouse/rental-returns/${RET}`)
    expect(gets('/api/v1/rental-returns')[0].url.searchParams.get('rentalPublicId')).toBe(R)
    unmount()

    wrap(`/warehouse/rentals/${R}`, ['rental.view', 'rental.return'])
    await user.click(await screen.findByRole('button', { name: 'Registrar devolución' }))
    const dialog = await screen.findByRole('dialog', { name: 'Registrar devolución de la renta REN-00007' })
    expect(within(dialog).getByText('Equipos que vuelven (2 de 2)')).toBeInTheDocument()
    expect(within(dialog).getByText(/Devolución anticipada/)).toBeInTheDocument()
    await waitFor(() => expect(within(dialog).getAllByRole('option', { name: 'Otro' }).length).toBeGreaterThan(0))
    await user.selectOptions(within(dialog).getByLabelText(/^Motivo/), 'OTHER')
    await user.click(within(dialog).getByRole('button', { name: 'Registrar devolución (2)' }))
    expect(await within(dialog).findByText("Con el motivo 'Otro' describa la devolución en las notas.")).toBeInTheDocument()
    expect(posts('/returns')).toHaveLength(0)

    await user.selectOptions(within(dialog).getByLabelText(/^Motivo/), 'EARLY_DAMAGE')
    // devolución parcial: solo SN-2, dañado y sin proceso
    await user.click(within(dialog).getByRole('checkbox', { name: /SN-1/ }))
    expect(within(dialog).getByText('Equipos que vuelven (1 de 2)')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Condición', { selector: '#ret-l32-cond' }), 'DAMAGED')
    await user.click(within(dialog).getByRole('switch', { name: '¿Pasa por proceso? SN-2' }))
    mock.post = (call) =>
      call.url.pathname.endsWith('/returns') ? problem(409, 'La serie SN-2 no está en renta en REN-00007.', 'conflict') : {}
    await user.click(within(dialog).getByRole('button', { name: 'Registrar devolución (1)' }))
    expect(await within(dialog).findByText('La serie SN-2 no está en renta en REN-00007.')).toBeInTheDocument()
    expect(posts('/returns')[0].body).toMatchObject({
      reason: 'EARLY_DAMAGE',
      toBinId: null,
      rowVersion: 'RV',
      lines: [{ serialNumber: 'SN-2', condition: 'DAMAGED', toBinId: null, requiresProcess: false, notes: null }],
    })

    mock.post = () => ({ return: { ...retItem, number: 'DRN-00004', units: 1 }, rentalStatusCode: 'ON_RENT', lines: [] })
    await user.click(within(dialog).getByRole('button', { name: 'Registrar devolución (1)' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(await screen.findByText('Devolución DRN-00004 registrada: 1 equipo(s).')).toBeInTheDocument()
  })
})

describe('Devoluciones: lista y ficha', () => {
  it('los filtros de la URL van al API; renta fijada con "Quitar este filtro"; enlaces a la renta', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/rental-returns?rentalPublicId=${R}&early=true&reason=EARLY_DAMAGE`, ['rental.view'])
    expect((await screen.findAllByText('DRN-00003')).length).toBeGreaterThan(0)
    const q = gets('/api/v1/rental-returns')[0].url.searchParams
    expect(q.get('rentalPublicId')).toBe(R)
    expect(q.get('early')).toBe('true')
    expect(q.getAll('reason')).toEqual(['EARLY_DAMAGE'])
    expect(await screen.findByText('Solo las devoluciones de la renta REN-00007.')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'REN-00007' })[0]).toHaveAttribute('href', `/warehouse/rentals/${R}`)
    expect(screen.getAllByText('Anticipada').length).toBeGreaterThan(0)
    await user.click(screen.getByRole('button', { name: 'Quitar este filtro' }))
    await waitFor(() => expect(gets('/api/v1/rental-returns').at(-1)!.url.searchParams.get('rentalPublicId')).toBeNull())
    // pestañas del submódulo
    expect(screen.getByRole('tab', { name: 'Devoluciones' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Proceso de equipos' })).toHaveAttribute('aria-selected', 'false')
  })

  it('ficha: equipos con condición, destino y proceso (enlace a la cola con la serie) y enlace a la renta de origen', async () => {
    wrap(`/warehouse/rental-returns/${RET}`, ['rental.view'])
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('DRN-00003')
    expect(screen.getByRole('link', { name: 'Renta REN-00007' })).toHaveAttribute('href', `/warehouse/rentals/${R}`)
    expect(screen.getAllByText('Dañado').length).toBeGreaterThan(0)
    expect(screen.getAllByText('ALM-01 · CUA-1').length).toBeGreaterThan(0)
    expect(screen.getAllByRole('link', { name: 'Ver proceso' })[0]).toHaveAttribute('href', '/warehouse/rental-processes?search=SN-1&open=all')
    expect(screen.getByText('Pantalla rota')).toBeInTheDocument()
  })
})

describe('Proceso de equipos', () => {
  it('acciones por permiso: sin inventory.adjust no hay "Dar de baja" y una nota lo explica; terminados solo con Historial', async () => {
    wrap('/warehouse/rental-processes', ['rental.view', 'rental.maintenance'])
    expect((await screen.findAllByText('SN-1')).length).toBeGreaterThan(0)
    expect(gets('/api/v1/rental-processes')[0].url.searchParams.get('open')).toBe('true')
    expect(screen.getAllByRole('button', { name: 'Avanzar' })).toHaveLength(1)
    expect(screen.getAllByRole('button', { name: 'Completar' })).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Dar de baja' })).toBeNull()
    expect(screen.getAllByRole('button', { name: 'Historial' })).toHaveLength(2)
    expect(screen.getByText(/Dar de baja exige además el permiso inventory.adjust/)).toBeInTheDocument()
  })

  it('solo lectura: sin acciones salvo Historial', async () => {
    wrap('/warehouse/rental-processes?open=all', ['rental.view'])
    expect((await screen.findAllByText('SN-1')).length).toBeGreaterThan(0)
    expect(gets('/api/v1/rental-processes')[0].url.searchParams.get('open')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Avanzar' })).toBeNull()
    expect(screen.queryByText(/inventory.adjust/)).toBeNull()
  })

  it('avanzar: sugiere el siguiente paso y muestra el 422 del motor tal cual', async () => {
    const user = userEvent.setup()
    wrap('/warehouse/rental-processes', ['rental.view', 'rental.maintenance'])
    await user.click((await screen.findAllByRole('button', { name: 'Avanzar' }))[0])
    const dialog = await screen.findByRole('dialog', { name: 'Avanzar el proceso de SN-1' })
    const select = within(dialog).getByLabelText('Pasa a')
    await waitFor(() => expect(select).toHaveValue('TESTING'))
    expect(within(dialog).getByRole('option', { name: 'Pruebas (siguiente)' })).toBeInTheDocument()
    // terminales con acción propia: no se ofrecen
    expect(within(dialog).queryByRole('option', { name: 'Lista' })).toBeNull()
    await user.selectOptions(select, 'PENDING')
    mock.post = () => problem(422, "Salto ilegal: de 'INSPECTION' solo se puede avanzar a 'TESTING'.", 'status_rule')
    await user.click(within(dialog).getByRole('button', { name: 'Avanzar' }))
    expect(await within(dialog).findByText("Salto ilegal: de 'INSPECTION' solo se puede avanzar a 'TESTING'.")).toBeInTheDocument()
    expect(posts('/advance')[0].body).toMatchObject({ status: 'PENDING', rowVersion: 'PV' })
  })

  it('dar de baja: confirmación fuerte escribiendo la serie; completar sin traslado', async () => {
    const user = userEvent.setup()
    wrap('/warehouse/rental-processes', ['rental.view', 'rental.maintenance', 'inventory.adjust'])
    await user.click((await screen.findAllByRole('button', { name: 'Dar de baja' }))[0])
    const dialog = await screen.findByRole('dialog', { name: 'Dar de baja SN-1' })
    expect(within(dialog).getByText(/motivo «Daño»/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja' }))
    expect(await within(dialog).findByText('Escriba la serie SN-1 para confirmar la baja.')).toBeInTheDocument()
    expect(posts('/scrap')).toHaveLength(0)
    mock.post = () => proc(5, 'SN-1', 'SCRAPPED')
    await user.type(within(dialog).getByLabelText('Para confirmar, escriba la serie SN-1'), 'sn-1')
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja' }))
    await waitFor(() => expect(posts('/scrap')).toHaveLength(1))
    expect(await screen.findByText('SN-1 quedó dada de baja.')).toBeInTheDocument()

    await user.click(screen.getAllByRole('button', { name: 'Completar' })[0])
    const done = await screen.findByRole('dialog', { name: 'Completar el proceso de SN-1' })
    mock.post = () => proc(5, 'SN-1', 'READY')
    await user.click(within(done).getByRole('button', { name: 'Completar' }))
    await waitFor(() => expect(posts('/complete')).toHaveLength(1))
    expect(posts('/complete')[0].body).toEqual({ binId: null, comment: null, rowVersion: 'PV' })
  })
})

describe('Resumen de la lista y reportes de rentas', () => {
  it('tarjetas En renta hoy / Por vencer / Vencidas con el total del API; un clic aplica su filtro', async () => {
    const user = userEvent.setup()
    wrap('/warehouse/rentals', ['rental.view', 'analytics.view'])
    const group = await screen.findByRole('group', { name: 'Resumen de rentas' })
    await waitFor(() => expect(within(group).getByRole('button', { name: /En renta hoy/ })).toHaveTextContent('4'))
    expect(within(group).getByRole('button', { name: /Vencidas/ })).toHaveTextContent('1')
    const summary = gets('/api/v1/rentals').filter((c) => c.url.searchParams.get('take') === '1')
    expect(summary.map((c) => [c.url.searchParams.get('status'), c.url.searchParams.get('dueWithinDays'), c.url.searchParams.get('overdue')])).toEqual([
      ['ON_RENT', null, null],
      [null, '7', null],
      [null, null, 'true'],
    ])
    await user.click(within(group).getByRole('button', { name: /Vencidas/ }))
    await waitFor(() => {
      const last = gets('/api/v1/rentals').filter((c) => c.url.searchParams.get('take') !== '1').at(-1)!
      expect(last.url.searchParams.get('overdue')).toBe('true')
    })
    expect(within(group).getByRole('button', { name: /Vencidas/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('link', { name: 'Reportes de rentas' })).toHaveAttribute('href', '/warehouse/rental-reports')
  })

  it('reportes: solo las vistas e indicadores de rentas; la vista elegida se corre con el motor y muestra sus totales', async () => {
    wrap('/warehouse/rental-reports', ['rental.view', 'analytics.view'])
    expect(await screen.findByRole('button', { name: /Equipos en renta por cliente/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Órdenes/ })).toBeNull()
    expect(await screen.findByText('Hospital Damas')).toBeInTheDocument()
    expect(posts('/api/v1/analytics/reports/40/run')[0].body).toEqual({ dateRangeMode: 'ALL', take: 500 })
    expect(screen.getByRole('group', { name: 'Totales' })).toHaveTextContent('5')
    const card = await screen.findByRole('group', { name: 'Rentas vencidas' })
    await waitFor(() => expect(card).toHaveTextContent('3'))
    expect(screen.queryByText('Órdenes abiertas')).toBeNull()
  })

  it('reportes sin analytics.view: "Sin permiso" y sin pestaña Reportes', async () => {
    wrap('/warehouse/rental-reports', ['rental.view'])
    expect(await screen.findByRole('tab', { name: 'Proceso de equipos' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Reportes' })).toBeNull()
    expect(gets('/api/v1/analytics/reports')).toHaveLength(0)
  })
})
