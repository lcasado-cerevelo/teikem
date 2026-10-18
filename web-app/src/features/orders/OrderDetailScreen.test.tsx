// Lote F-C2 — Expediente de la orden: acciones de la cabecera (cada una con su permiso Y su capacidad), edición dentro del Detalle
// (PATCH solo con lo cambiado, 400 por campo, 409 de edición concurrente), confirmar con crédito excedido y autorización, re-cotizar,
// cancelar, eliminar, asignar chofer a una entrega especial, laterales con regreso exacto, Cotización solo con orders.view_price,
// COD, campos personalizados y contactos. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import OrderDetailScreen from './OrderDetailScreen'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
  headers: Headers
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], override: null as null | ((call: Call) => unknown) }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    let body: Record<string, unknown> | undefined
    try {
      body = text ? (JSON.parse(text) as Record<string, unknown>) : undefined
    } catch {
      body = undefined
    }
    const call = { method: req.method, url: new URL(req.url), body, headers: req.headers }
    mock.calls.push(call)
    const out = mock.override?.(call) ?? route(call)
    if (out instanceof Response) return out
    if (out === null) return new Response(null, { status: 204 })
    return new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const DRAFT_CAPS = { canEditCargo: true, canCancel: true, canDelete: true, canConfirm: true, canReprice: false, canReopen: false }
const CONFIRMED_CAPS = { canEditCargo: false, canCancel: true, canDelete: false, canConfirm: false, canReprice: true, canReopen: false }
const BASE_ORDER = {
  id: 7,
  publicId: 'o-7',
  orderNumber: 'ORD-00007',
  clientInvoiceNumber: 'FAC-00007',
  packBatchNumber: 'EMP-00007',
  clientPublicId: 'c-1',
  clientName: 'Acme Corp',
  serviceType: 'STANDARD',
  serviceTypeLabel: 'Estándar',
  priority: 'NORMAL',
  status: 'SUBMITTED',
  statusLabel: 'Sometida',
  isInitialStatus: true,
  isActive: true,
  currency: 'USD',
  quotedAmount: null as number | null,
  quotedAtUtc: null as string | null,
  codAmount: 50 as number | null,
  codStatus: 'PENDING',
  codStatusLabel: 'Pendiente',
  codType: null,
  totalPieces: 3,
  isSpecialDelivery: false,
  notes: 'Llamar antes',
  requestedDate: '2026-10-12T00:00:00',
  promisedDate: '2026-10-14T00:00:00',
  rowVersion: 'AAA=',
  pickup: { id: 1, stopType: 'PICKUP', name: 'Almacén Acme', city: 'Bayamón', status: 'PENDING', statusLabel: 'Pendiente' },
  delivery: { id: 2, stopType: 'DELIVERY', locationPublicId: 'l-1', name: 'Farmacia Central', line1: 'Calle Sol 1', city: 'San Juan', postalCode: '00901', status: 'PENDING', statusLabel: 'Pendiente' },
  packages: [
    { id: 1, packageType: 'BOX', packageTypeLabel: 'Caja', description: 'Ropa', pieces: 2 },
    { id: 2, packageType: 'ENV', packageTypeLabel: 'Sobre', description: '', pieces: 1 },
  ],
  references: [],
  capabilities: DRAFT_CAPS,
}
type Order = typeof BASE_ORDER & Record<string, unknown>
let current: Order = { ...BASE_ORDER }

const STATUSES = [
  { code: 'SUBMITTED', label: 'Sometida', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 10 },
  { code: 'PICKUP', label: 'Recogida', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 20 },
  { code: 'UNASSIGNED', label: 'Sin asignar', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 30 },
  { code: 'ASSIGNED', label: 'Asignada', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 40 },
  { code: 'IN_TRANSIT', label: 'En tránsito', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 50 },
  { code: 'DELIVERED', label: 'Entregada', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 60 },
  { code: 'FAILED', label: 'Intento fallido', stageKind: 'LATERAL', isInitial: false, isEnabled: true, sortOrder: 70 },
  { code: 'CANCELLED', label: 'Cancelada', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 80 },
  { code: 'IN_WAREHOUSE', label: 'En almacén', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 90 },
]
const BASE_LOC = { locationType: 'DELIVERY', country: 'PR', defaultServiceMinutes: 0, isActive: true, rowVersion: 'AAA=', clientPublicId: 'c-1', isShared: false, allowDupInvoice: false }
const L1 = { ...BASE_LOC, id: 100, publicId: 'l-1', code: 'C-001', name: 'Farmacia Central', line1: 'Calle Sol 1', city: 'San Juan', postalCode: '00901' }
const L2 = { ...BASE_LOC, id: 101, publicId: 'l-2', code: 'C-002', name: 'Colmado Boricua', line1: 'Carr. 14 km 2', city: 'Ponce', postalCode: '00716' }
const HISTORY = [{ id: 1, fromCode: null, toCode: 'SUBMITTED', toLabel: 'Sometida', changedAtUtc: '2026-10-12T10:00:00', changedByName: 'Ana' }]
const DRIVERS = [
  { id: 1, publicId: 'd-1', code: 'CH-001', fullName: 'Pedro Rivera', zoneCode: 'SJ', status: 'Disponible', isActive: true },
  { id: 2, publicId: 'd-2', code: 'CH-002', fullName: 'María Soto', zoneCode: 'PO', status: 'Disponible', isActive: true },
]
let history: unknown[] = HISTORY
let proof: unknown = null
let points: unknown[] = [{ id: 40, ownerEntity: 'TRANSPORT_ORDER', ownerId: 7, contactType: 'PHONE', contactTypeLabel: 'Teléfono', value: '7875550142', label: 'Recibe: Carmen Ortiz', isPrimary: true, isActive: true }]
let definitions: unknown[] = []

function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/orders/o-7' && method === 'GET') return current
  if (p === '/api/v1/orders/o-7' && method === 'PATCH') {
    const lines = (body?.packages as { packageType: string; description: string | null; pieces: number }[] | undefined)?.map((l, i) => ({ id: i + 1, packageType: l.packageType, packageTypeLabel: l.packageType, description: l.description, pieces: l.pieces }))
    current = { ...current, ...body, ...(lines ? { packages: lines } : {}), rowVersion: 'BBB=' } as Order
    return current
  }
  if (p === '/api/v1/orders/o-7' && method === 'DELETE') return null
  if (p === '/api/v1/orders/o-7/quote') return { total: 130, lines: [{ serviceType: 'STANDARD', packageType: 'BOX', pieces: 2, baseRate: 60, lineTotal: 120 }], dispatchFee: 10, codFee: 0, credit: { creditLimit: 1000, pendingBalance: 200, orderTotal: 130, available: 800, exceeds: false } }
  if (/^\/api\/v1\/orders\/o-7\/(confirm|reprice|cancel|reopen|status|driver)$/.test(p)) return { ...current, rowVersion: 'CCC=' }
  if (p === '/api/v1/orders/o-7/return' && method === 'POST') return { returnOrderPublicId: 'o-8', returnOrderNumber: 'ORD-00008', returnWaybillNumber: 'W8', originalOrderNumber: 'ORD-00007', pieces: (body?.pieceNumbers as string[] | null)?.length ?? 3, message: 'La orden ORD-00007 quedó «Devuelta»: se creó la orden de vuelta ORD-00008.' }
  if (p === '/api/v1/orders/o-7/delivery' && method === 'GET') return proof
  if (p === '/api/v1/orders/o-7/delivery' && method === 'POST') return { publicId: 'pf-1', orderPublicId: 'o-7', orderNumber: 'ORD-00007', deliveredAtUtc: '2026-10-18T20:00:00Z', recipientName: body?.recipientName, notes: body?.notes ?? null, source: 'STAFF_WEB', signatureKind: 'USER', registeredByName: 'Dalys', signedByName: 'Dalys', geoStatus: 'NONE', geoAccuracy: null, latitude: null, longitude: null, orderStatusCode: 'DELIVERED', orderStatusLabel: 'Entregada', message: 'Entrega registrada.' }
  if (p === '/api/v1/status/OrderStatus') return STATUSES
  if (p === '/api/v1/status/OrderStatus/validate') return { isValid: true, errors: [], warnings: [] }
  if (p === '/api/v1/status/lateral-entries/TRANSPORT_ORDER') return []
  if (p === '/api/v1/status/history/TRANSPORT_ORDER/7') return history
  if (p === '/api/v1/status/StopStatus') return [{ code: 'PENDING', label: 'Pendiente', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 }]
  if (p === '/api/v1/catalogs/ServiceType') return [{ code: 'STANDARD', label: 'Estándar', isEnabled: true }, { code: 'EXPRESS', label: 'Express', isEnabled: true }]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Caja', isEnabled: true }, { code: 'ENV', label: 'Sobre', isEnabled: true }]
  if (p === '/api/v1/catalogs/OrderPriority') return [{ code: 'HIGH', label: 'Alta', isEnabled: true }, { code: 'NORMAL', label: 'Normal', isEnabled: true }]
  if (p === '/api/v1/catalogs/ContactType') return [{ code: 'PHONE', label: 'Teléfono', isEnabled: true }, { code: 'EMAIL', label: 'Correo', isEnabled: true }]
  if (p === '/api/v1/locations' && method === 'GET') return [L1, L2]
  if (p === '/api/v1/drivers' && method === 'GET') return DRIVERS
  if (p === '/api/v1/contacts/TRANSPORT_ORDER/7' && method === 'GET') return points
  if (p === '/api/v1/contacts/TRANSPORT_ORDER/7' && method === 'POST') return { id: 41 }
  if (p === '/api/v1/custom-fields/definitions') return definitions
  if (p.startsWith('/api/v1/custom-fields/values/')) return []
  if (p.startsWith('/api/v1/catalogs/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

const EDITOR = ['orders.view', 'orders.edit', 'orders.cancel', 'locations.read', 'clients.read']
const ALL = [...EDITOR, 'orders.credit_override', 'orders.view_price', 'trips.dispatch', 'fleet.view', 'contacts.manage']

function wrap(permissions: string[] = ALL, modules = ['LTL_GROUND', 'CATALOG']) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={['/orders/o-7']}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={modules}>
          <Routes>
            <Route path="/orders/:publicId" element={<OrderDetailScreen />} />
            <Route path="/orders" element={<p>LISTA DE ÓRDENES</p>} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)
const header = () => screen.getByRole('heading', { level: 1 }).closest('.head') as HTMLElement
const headerBtn = (name: string | RegExp) => within(header()).queryByRole('button', { name })
async function loaded() {
  await screen.findByRole('heading', { level: 1, name: /ORD-00007/ })
}
async function openDialog(button: string | RegExp) {
  const user = userEvent.setup()
  await user.click(within(header()).getByRole('button', { name: button }))
  return { user, dialog: await screen.findByRole('dialog') }
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  current = { ...BASE_ORDER }
  history = HISTORY
  definitions = []
  points = [{ id: 40, ownerEntity: 'TRANSPORT_ORDER', ownerId: 7, contactType: 'PHONE', contactTypeLabel: 'Teléfono', value: '7875550142', label: 'Recibe: Carmen Ortiz', isPrimary: true, isActive: true }]
})

describe('Expediente — cabecera, paneles y capacidades', () => {
  it('en Sometida con todos los permisos: Marcar como recogida, Cancelar la orden y Eliminar la orden; sin Re-cotizar ni Asignar chofer', async () => {
    wrap()
    await loaded()
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('ORD-00007 · Farmacia Central')
    expect(header()).toHaveTextContent('San Juan · Acme Corp')
    for (const name of ['Marcar como recogida', 'Cancelar la orden', 'Eliminar la orden']) expect(headerBtn(name)).toBeInTheDocument()
    expect(headerBtn('Re-cotizar')).toBeNull()
    expect(headerBtn(/chofer/i)).toBeNull()
    // Lote F-G2: «Imprimir label» ya existe; POD (sin endpoint) no
    expect(headerBtn('Imprimir label')).toBeInTheDocument()
    expect(headerBtn(/pod/i)).toBeNull()
    // paneles de la columna derecha
    expect(screen.getByRole('heading', { name: 'COD' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Cotización' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Detalle' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Historial' })).toBeInTheDocument()
    expect(await screen.findByText('Recibe: Carmen Ortiz', { exact: false })).toBeInTheDocument()
  })

  it('recogida: Re-cotizar y Cancelar; sin Marcar como recogida, Eliminar ni edición (datos como referencia)', async () => {
    current = { ...BASE_ORDER, status: 'PICKUP', statusLabel: 'Recogida', isInitialStatus: false, quotedAmount: 120, quotedAtUtc: '2026-10-12T12:00:00', capabilities: CONFIRMED_CAPS }
    wrap()
    await loaded()
    expect(headerBtn('Re-cotizar')).toBeInTheDocument()
    expect(headerBtn('Cancelar la orden')).toBeInTheDocument()
    expect(headerBtn('Marcar como recogida')).toBeNull()
    expect(headerBtn('Eliminar la orden')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    expect(screen.getByText(/Esta orden ya avanzó de Sometida/)).toBeInTheDocument()
    // paquetes en tabla de solo lectura
    expect(screen.getByRole('cell', { name: 'Caja' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Monto COD')).toBeNull()
  })

  it('sin permisos de escritura la ficha es de lectura: ninguna acción ni edición', async () => {
    wrap(['orders.view'])
    await loaded()
    for (const name of ['Marcar como recogida', 'Cancelar la orden', 'Eliminar la orden', 'Re-cotizar']) expect(headerBtn(name)).toBeNull()
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    expect(screen.getByText('Su usuario no tiene permiso para editar órdenes: los datos se muestran como referencia.')).toBeInTheDocument()
  })

  it('una orden dada de baja no ofrece acciones', async () => {
    current = { ...BASE_ORDER, isActive: false }
    wrap()
    await loaded()
    expect(within(header()).queryAllByRole('button')).toHaveLength(0)
  })

  it('COD: monto, estado y la nota de que el tipo de cobro se registra al entregar (solo lectura)', async () => {
    wrap()
    await loaded()
    const panel = screen.getByRole('heading', { name: 'COD' }).closest('.panel') as HTMLElement
    expect(panel).toHaveTextContent('$50.00')
    expect(within(panel).getByText('Pendiente')).toBeInTheDocument()
    expect(within(panel).getByText('Se registra al entregar')).toBeInTheDocument()
    expect(within(panel).getByText(/se registra al momento de entregar/)).toBeInTheDocument()
    expect(within(panel).queryByRole('button')).toBeNull()
  })
})

describe('Expediente — COD sin monto y chofer sin viaje', () => {
  it('una orden sin COD muestra solo «Monto» (sin tipo ni estado ni la nota)', async () => {
    current = { ...BASE_ORDER, codAmount: null, codStatus: null, codStatusLabel: null }
    wrap()
    await loaded()
    const panel = screen.getByRole('heading', { name: 'COD' }).closest('.panel') as HTMLElement
    expect(within(panel).getByText('Monto')).toBeInTheDocument()
    expect(within(panel).queryByText('Tipo')).toBeNull()
    expect(within(panel).queryByText('Estado')).toBeNull()
    expect(within(panel).queryByText(/se registra al momento de entregar/)).toBeNull()
  })

  it('un chofer asignado a una entrega especial sin código de ruta no muestra la fila «Viaje»', async () => {
    current = { ...BASE_ORDER, isSpecialDelivery: true, packages: [], codAmount: null, status: 'IN_TRANSIT', statusLabel: 'En tránsito', isInitialStatus: false, assignedDriverPublicId: 'd-1', assignedDriverCode: 'CH-001', assignedDriverName: 'Pedro Rivera' }
    wrap()
    await loaded()
    const panel = screen.getByRole('heading', { name: 'Chofer asignado' }).closest('.panel') as HTMLElement
    expect(panel).toHaveTextContent('CH-001 · Pedro Rivera')
    expect(within(panel).queryByText('Viaje')).toBeNull()
    // en tránsito todavía se reasigna
    expect(headerBtn('Reasignar chofer')).toBeInTheDocument()
  })
})

describe('Expediente — Cotización (orders.view_price)', () => {
  it('con el permiso: «Cotizar» pide la vista previa y muestra líneas, despacho, total y crédito', async () => {
    wrap()
    await loaded()
    const panel = screen.getByRole('heading', { name: 'Cotización' }).closest('.panel') as HTMLElement
    expect(callsTo('GET', '/api/v1/orders/o-7/quote')).toHaveLength(0)
    const user = userEvent.setup()
    await user.click(within(panel).getByRole('button', { name: 'Cotizar' }))
    expect(await within(panel).findByText('Vista previa con las tarifas vigentes')).toBeInTheDocument()
    expect(panel).toHaveTextContent('Estándar · Caja ×2')
    expect(panel).toHaveTextContent('$120.00')
    expect(panel).toHaveTextContent('Despacho')
    expect(panel).toHaveTextContent('$130.00')
    expect(panel).toHaveTextContent('Dentro del límite')
    expect(callsTo('GET', '/api/v1/orders/o-7/quote')).toHaveLength(1)
  })

  it('crédito excedido en la vista previa y error del servidor sin tarifa', async () => {
    mock.override = ({ url }) =>
      url.pathname.endsWith('/quote') ? { total: 130, lines: [], dispatchFee: 0, codFee: 0, credit: { creditLimit: 100, pendingBalance: 90, orderTotal: 130, available: 10, exceeds: true } } : undefined
    wrap()
    await loaded()
    const panel = screen.getByRole('heading', { name: 'Cotización' }).closest('.panel') as HTMLElement
    const user = userEvent.setup()
    await user.click(within(panel).getByRole('button', { name: 'Cotizar' }))
    expect(await within(panel).findByText('Excede el crédito')).toBeInTheDocument()

    mock.override = ({ url }) => (url.pathname.endsWith('/quote') ? problem(422, 'No hay tarifa vigente para STANDARD/BOX; configure la tarifa en el contrato del cliente antes de confirmar.', 'status_rule') : undefined)
    await user.click(within(panel).getByRole('button', { name: 'Cotizar' }))
    expect(await within(panel).findByRole('alert')).toHaveTextContent('No hay tarifa vigente para STANDARD/BOX')
  })

  it('sin el permiso el panel no existe y nunca se pide la cotización (el 403 no aparece)', async () => {
    wrap(ALL.filter((p) => p !== 'orders.view_price'))
    await loaded()
    expect(screen.queryByRole('heading', { name: 'Cotización' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Cotizar' })).toBeNull()
    expect(callsTo('GET', '/api/v1/orders/o-7/quote')).toHaveLength(0)
  })

  it('muestra el monto congelado y cuándo se cotizó', async () => {
    current = { ...BASE_ORDER, status: 'PICKUP', statusLabel: 'Recogida', isInitialStatus: false, quotedAmount: 120, quotedAtUtc: '2026-10-12T12:00:00', capabilities: CONFIRMED_CAPS }
    wrap()
    await loaded()
    const panel = screen.getByRole('heading', { name: 'Cotización' }).closest('.panel') as HTMLElement
    expect(panel).toHaveTextContent('$120.00')
    expect(panel).toHaveTextContent(/Cotizada el/)
  })
})

describe('Expediente — edición dentro del Detalle', () => {
  it('«Guardar cambios» queda apagado hasta que algo cambia y manda SOLO lo cambiado con el rowVersion', async () => {
    const user = userEvent.setup()
    wrap()
    await loaded()
    expect(screen.getByText('Esta orden está en Sometida: puede corregir sus datos y sus paquetes.')).toBeInTheDocument()
    const save = screen.getByRole('button', { name: 'Guardar cambios' })
    expect(save).toBeDisabled()
    // el cliente, el N.º de orden, la factura y el empaque son de solo lectura
    expect(screen.queryByRole('combobox', { name: /^Cliente/ })).toBeNull()
    expect(screen.getByText('FAC-00007')).toBeInTheDocument()
    expect(screen.getByText('EMP-00007')).toBeInTheDocument()

    const notes = screen.getByLabelText('Comentario')
    await user.clear(notes)
    await user.type(notes, 'Dejar con el portero')
    const pieces = screen.getAllByLabelText(/^Piezas/)[0]
    await user.clear(pieces)
    await user.type(pieces, '5')
    expect(save).toBeEnabled()
    await user.click(save)
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/orders/o-7')).toHaveLength(1))
    const body = callsTo('PATCH', '/api/v1/orders/o-7')[0].body
    expect(body).toEqual({
      notes: 'Dejar con el portero',
      packages: [
        { packageType: 'BOX', description: 'Ropa', pieces: 5 },
        { packageType: 'ENV', description: null, pieces: 1 },
      ],
      rowVersion: 'AAA=',
    })
    expect(await screen.findByText('Cambios guardados.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled())
  })

  it('cambiar el consignatario, el servicio, la prioridad y el COD; quitar el COD manda clearCod', async () => {
    const user = userEvent.setup()
    wrap()
    await loaded()
    await screen.findByRole('option', { name: 'Express' })
    await user.selectOptions(screen.getByLabelText(/^Servicio/), 'EXPRESS')
    await screen.findByRole('option', { name: 'Alta' })
    await user.selectOptions(screen.getByLabelText('Prioridad'), 'HIGH')
    const box = screen.getByRole('combobox', { name: /^Consignatario/ })
    await user.click(box)
    await user.click(await screen.findByRole('option', { name: /Colmado Boricua/ }))
    // pueblo y ZIP salen de la dirección elegida
    await waitFor(() => expect(screen.getByText('Ponce')).toBeInTheDocument())
    expect(screen.getByText('00716')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Monto COD'))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/orders/o-7')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/orders/o-7')[0].body).toEqual({ serviceType: 'EXPRESS', priority: 'HIGH', consigneeLocationPublicId: 'l-2', clearCod: true, rowVersion: 'AAA=' })
  })

  it('añade y quita líneas de paquete (mínimo una) y valida sin llamar al API', async () => {
    const user = userEvent.setup()
    wrap()
    await loaded()
    await user.click(screen.getByRole('button', { name: /Añadir paquete/ }))
    expect(screen.getAllByLabelText(/^Piezas/)).toHaveLength(3)
    await user.click(screen.getByRole('button', { name: 'Quitar paquete 3' }))
    await user.click(screen.getByRole('button', { name: 'Quitar paquete 2' }))
    expect(screen.getByRole('button', { name: 'Quitar paquete 1' })).toBeDisabled()
    // piezas inválidas: mensaje del servidor, sin PATCH
    const pieces = screen.getByLabelText(/^Piezas/)
    await user.clear(pieces)
    await user.type(pieces, '0')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('La cantidad de piezas debe ser al menos 1.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/orders/o-7')).toHaveLength(0)
  })

  it('una fecha ya capturada no se puede quitar', async () => {
    const user = userEvent.setup()
    wrap()
    await loaded()
    await user.clear(screen.getByLabelText('Límite'))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('La fecha no se puede quitar; elija otra.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/orders/o-7')).toHaveLength(0)
  })

  it('400 del servidor: el error queda bajo el campo de la línea y en el aviso', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/orders/o-7' ? problem(400, 'Solicitud inválida', 'validation', { 'packages[0].packageType': ['Tipo de paquete desconocido: BOX.'] }) : undefined
    wrap()
    await loaded()
    await user.type(screen.getByLabelText('Comentario'), ' x')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('Tipo de paquete desconocido: BOX.')).toBeInTheDocument()
  })

  it('409 de edición concurrente: avisa con el texto del servidor, relee la ficha y conserva lo escrito', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/orders/o-7' ? problem(409, 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', 'conflict') : undefined
    wrap()
    await loaded()
    const notes = screen.getByLabelText('Comentario')
    await user.clear(notes)
    await user.type(notes, 'Mi cambio')
    const before = callsTo('GET', '/api/v1/orders/o-7').length
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    const alerts = await screen.findAllByRole('alert')
    expect(alerts.some((a) => a.textContent === 'El registro fue modificado por otro usuario; recargue e intente de nuevo.')).toBe(true)
    await waitFor(() => expect(callsTo('GET', '/api/v1/orders/o-7').length).toBeGreaterThan(before))
    expect(screen.getByLabelText('Comentario')).toHaveValue('Mi cambio')
  })

  it('factura repetida al cambiar de consignatario: ofrece «Guardar de todos modos» y reenvía con confirmDuplicateInvoice', async () => {
    const user = userEvent.setup()
    let first = true
    mock.override = ({ method, url, body }) => {
      if (method !== 'PATCH' || url.pathname !== '/api/v1/orders/o-7') return undefined
      if (first && !body?.confirmDuplicateInvoice) {
        first = false
        return problem(409, "El consignatario 'Colmado Boricua' admite facturas repetidas; la orden ORD-00001 ya usa el número FAC-1: confirme si desea guardar de todos modos.", 'duplicate_invoice_confirmable', {
          existingOrderNumber: ['ORD-00001'],
          existingOrderPublicId: ['o-1'],
        })
      }
      return undefined
    }
    wrap()
    await loaded()
    await user.click(screen.getByRole('combobox', { name: /^Consignatario/ }))
    await user.click(await screen.findByRole('option', { name: /Colmado Boricua/ }))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    const dialog = await screen.findByRole('dialog', { name: 'Factura repetida para este consignatario' })
    expect(dialog).toHaveTextContent('ORD-00001')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar de todos modos' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/orders/o-7')).toHaveLength(2))
    expect(callsTo('PATCH', '/api/v1/orders/o-7')[1].body).toMatchObject({ consigneeLocationPublicId: 'l-2', confirmDuplicateInvoice: true })
  })

  it('una entrega especial no edita paquetes ni COD', async () => {
    current = { ...BASE_ORDER, isSpecialDelivery: true, specialServiceName: 'Traer vagón', packages: [], codAmount: null }
    wrap()
    await loaded()
    expect(screen.queryByLabelText('Monto COD')).toBeNull()
    expect(screen.queryByRole('button', { name: /Añadir paquete/ })).toBeNull()
    expect(screen.getByText('Traer vagón')).toBeInTheDocument()
    expect(screen.getByLabelText('Comentario')).toBeInTheDocument()
  })
})

describe('Expediente — Confirmar y crédito', () => {
  it('«Marcar como recogida» pide confirmación y manda rowVersion con su Idempotency-Key, sin overrideCredit', async () => {
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Marcar como recogida')
    await user.click(within(dialog).getByRole('button', { name: 'Marcar como recogida' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/confirm')).toHaveLength(1))
    const call = callsTo('POST', '/api/v1/orders/o-7/confirm')[0]
    expect(call.body).toEqual({ rowVersion: 'AAA=' })
    expect(call.headers.get('Idempotency-Key')).toBeTruthy()
    expect(await screen.findByText('Orden ORD-00007 marcada como recogida.')).toBeInTheDocument()
  })

  it('crédito excedido con orders.credit_override: «Autorizar y marcar como recogida» reenvía con overrideCredit y otra clave', async () => {
    let n = 0
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders/o-7/confirm' && n++ === 0
        ? problem(422, 'El cliente excede su límite de crédito: límite 100.00, en curso 90.00, esta orden 130.00.', 'credit_exceeded')
        : undefined
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Marcar como recogida')
    await user.click(within(dialog).getByRole('button', { name: 'Marcar como recogida' }))
    const credit = await screen.findByRole('dialog', { name: 'El cliente excede su límite de crédito' })
    expect(credit).toHaveTextContent('límite 100.00, en curso 90.00')
    await user.click(within(credit).getByRole('button', { name: 'Autorizar y marcar como recogida' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/confirm')).toHaveLength(2))
    const [a, b] = callsTo('POST', '/api/v1/orders/o-7/confirm')
    expect(a.body).toEqual({ rowVersion: 'AAA=' })
    expect(b.body).toEqual({ rowVersion: 'AAA=', overrideCredit: true })
    expect(a.headers.get('Idempotency-Key')).not.toBe(b.headers.get('Idempotency-Key'))
  })

  it('crédito excedido sin orders.credit_override: solo el mensaje del servidor, sin autorizar', async () => {
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders/o-7/confirm' ? problem(422, 'El cliente excede su límite de crédito.', 'credit_exceeded') : undefined
    wrap(EDITOR)
    await loaded()
    const { user, dialog } = await openDialog('Marcar como recogida')
    await user.click(within(dialog).getByRole('button', { name: 'Marcar como recogida' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El cliente excede su límite de crédito.')
    expect(screen.queryByRole('button', { name: 'Autorizar y marcar como recogida' })).toBeNull()
  })

  it('Facturación (solo orders.credit_override): el botón autoriza directamente con overrideCredit', async () => {
    wrap(['orders.view', 'orders.credit_override', 'orders.view_price'])
    await loaded()
    const { user, dialog } = await openDialog('Marcar como recogida')
    await user.click(within(dialog).getByRole('button', { name: 'Autorizar y marcar como recogida' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/confirm')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/orders/o-7/confirm')[0].body).toEqual({ rowVersion: 'AAA=', overrideCredit: true })
  })

  it('otros errores del servidor (tarifa faltante, cliente suspendido) se muestran en el diálogo', async () => {
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders/o-7/confirm' ? problem(422, 'El cliente está suspendido; no se pueden crear ni confirmar órdenes.', 'status_rule') : undefined
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Marcar como recogida')
    await user.click(within(dialog).getByRole('button', { name: 'Marcar como recogida' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El cliente está suspendido; no se pueden crear ni confirmar órdenes.')
  })
})

describe('Expediente — Re-cotizar, cancelar y eliminar', () => {
  beforeEach(() => {
    current = { ...BASE_ORDER, status: 'PICKUP', statusLabel: 'Recogida', isInitialStatus: false, quotedAmount: 120, capabilities: CONFIRMED_CAPS }
  })

  it('Re-cotizar confirma y llama a POST /reprice sin cuerpo', async () => {
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Re-cotizar')
    await user.click(within(dialog).getByRole('button', { name: 'Re-cotizar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/reprice')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/orders/o-7/reprice')[0].body).toBeUndefined()
    expect(await screen.findByText('Orden ORD-00007 re-cotizada.')).toBeInTheDocument()
  })

  it('el error del servidor al re-cotizar se ve en el diálogo', async () => {
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname.endsWith('/reprice') ? problem(422, "El estatus actual no permite la acción 'REPRICE'.", 'status_rule') : undefined
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Re-cotizar')
    await user.click(within(dialog).getByRole('button', { name: 'Re-cotizar' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent("El estatus actual no permite la acción 'REPRICE'.")
  })

  it('Cancelar la orden manda la razón en comment y avisa', async () => {
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Cancelar la orden')
    await user.type(within(dialog).getByLabelText('Razón de la cancelación'), 'El cliente la anuló')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar la orden' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/cancel')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/orders/o-7/cancel')[0].body).toEqual({ comment: 'El cliente la anuló', rowVersion: 'AAA=' })
    expect(await screen.findByText('Orden ORD-00007 cancelada.')).toBeInTheDocument()
  })

  it('cancelar sin razón no envía y muestra «Indique la razón de la cancelación.»; más de 500 caracteres lo avisa', async () => {
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Cancelar la orden')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar la orden' }))
    expect(await within(dialog).findByText('Indique la razón de la cancelación.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/orders/o-7/cancel')).toHaveLength(0)
    fireLong(within(dialog).getByLabelText('Razón de la cancelación'), 501)
    expect(await within(dialog).findByText('El comentario admite como máximo 500 caracteres.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Cancelar la orden' })).toBeDisabled()
    expect(callsTo('POST', '/api/v1/orders/o-7/cancel')).toHaveLength(0)
  })

  it('el error del servidor al cancelar se ve en el diálogo, que sigue abierto', async () => {
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname.endsWith('/cancel') ? problem(400, 'Solicitud inválida', 'validation', { comment: ['Indique la razón de la cancelación.'] }) : undefined
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Cancelar la orden')
    await user.type(within(dialog).getByLabelText('Razón de la cancelación'), 'x')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar la orden' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Indique la razón de la cancelación.')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('Eliminar (solo en Sometida): DELETE y vuelve a la lista', async () => {
    current = { ...BASE_ORDER }
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Eliminar la orden')
    expect(dialog).toHaveTextContent('ORD-00007')
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(callsTo('DELETE', '/api/v1/orders/o-7')).toHaveLength(1))
    expect(await screen.findByText('LISTA DE ÓRDENES')).toBeInTheDocument()
  })

  it('el 422 de eliminar se muestra en el diálogo', async () => {
    current = { ...BASE_ORDER }
    mock.override = ({ method }) => (method === 'DELETE' ? problem(422, 'Solo se puede eliminar una orden en su estatus inicial; use la cancelación.', 'status_rule') : undefined)
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Eliminar la orden')
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Solo se puede eliminar una orden en su estatus inicial; use la cancelación.')
  })
})

/** Escribe `n` caracteres de golpe en un textarea (más rápido que teclearlos). */
function fireLong(el: HTMLElement, n: number) {
  const proto = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!
  proto.set!.call(el, 'x'.repeat(n))
  el.dispatchEvent(new Event('input', { bubbles: true }))
}

describe('Expediente — estatus laterales y regreso exacto', () => {
  it('en el pipeline ofrece solo los desvíos (nunca avanzar, cancelar ni confirmar) y los manda por POST /status', async () => {
    current = { ...BASE_ORDER, status: 'PICKUP', statusLabel: 'Recogida', isInitialStatus: false, capabilities: CONFIRMED_CAPS }
    history = [...HISTORY, { id: 2, fromCode: 'SUBMITTED', toCode: 'PICKUP', toLabel: 'Recogida', changedAtUtc: '2026-10-12T11:00:00' }]
    wrap()
    await loaded()
    const user = userEvent.setup()
    const hold = await screen.findByRole('button', { name: 'Pasar a Intento fallido' })
    expect(screen.queryByRole('button', { name: /Avanzar a|En tránsito|Cancelada|Entregada/ })).toBeNull()
    await user.click(hold)
    await user.type(screen.getByLabelText('Comentario (opcional)'), 'Cliente ausente')
    await user.click(screen.getByRole('button', { name: 'Cambiar estatus' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/status')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/orders/o-7/status')[0].body).toEqual({ toCode: 'FAILED', comment: 'Cliente ausente' })
    expect(callsTo('POST', '/api/v1/orders/o-7/status')[0].headers.get('Idempotency-Key')).toBeTruthy()
  })

  it('desde un lateral ofrece solo volver a la etapa de la que salió', async () => {
    current = { ...BASE_ORDER, status: 'FAILED', statusLabel: 'Intento fallido', isInitialStatus: false, capabilities: CONFIRMED_CAPS }
    history = [
      ...HISTORY,
      { id: 2, fromCode: 'SUBMITTED', toCode: 'PICKUP', toLabel: 'Recogida', changedAtUtc: '2026-10-12T11:00:00' },
      { id: 3, fromCode: 'PICKUP', toCode: 'FAILED', toLabel: 'Intento fallido', changedAtUtc: '2026-10-12T12:00:00' },
    ]
    wrap()
    await loaded()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Volver a Recogida' }))
    await user.click(screen.getByRole('button', { name: 'Cambiar estatus' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/status')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/orders/o-7/status')[0].body).toMatchObject({ toCode: 'PICKUP' })
    expect(screen.queryByRole('button', { name: /En tránsito|Cancelada|Entregada/ })).toBeNull()
  })

  it('sin orders.edit no hay botones de estatus', async () => {
    wrap(['orders.view'])
    await loaded()
    await screen.findByText('Sometida', { selector: '.lbl' })
    expect(screen.queryByRole('button', { name: /Pasar a|Volver a|Avanzar a/ })).toBeNull()
  })

  it('el historial es el real (StatusHistory), no una línea de tiempo ilustrativa', async () => {
    wrap()
    await loaded()
    const panel = screen.getByRole('heading', { name: 'Historial' }).closest('.panel') as HTMLElement
    expect(await within(panel).findByText(/Ana/)).toBeInTheDocument()
  })
})

describe('Expediente — Asignar chofer (entrega especial)', () => {
  beforeEach(() => {
    current = { ...BASE_ORDER, isSpecialDelivery: true, specialServiceName: 'Traer vagón', packages: [], codAmount: null }
  })

  it('el botón solo aparece en una entrega especial hasta IN_TRANSIT, con trips.dispatch y el módulo CATALOG', async () => {
    wrap()
    await loaded()
    expect(headerBtn('Asignar chofer')).toBeInTheDocument()
  })

  it('no se ofrece en una orden normal, después de IN_TRANSIT, en un lateral, sin trips.dispatch ni con CATALOG apagado', async () => {
    const check = async (order: Order, permissions: string[] = ALL, modules = ['LTL_GROUND', 'CATALOG']) => {
      current = order
      const { unmount } = wrap(permissions, modules)
      await loaded()
      await screen.findByText('Sometida', { selector: '.lbl' })
      expect(headerBtn(/chofer/i)).toBeNull()
      unmount()
    }
    await check({ ...BASE_ORDER })
    await check({ ...BASE_ORDER, isSpecialDelivery: true, status: 'ARRIVED', statusLabel: 'En destino', isInitialStatus: false })
    await check({ ...BASE_ORDER, isSpecialDelivery: true, status: 'FAILED', statusLabel: 'Intento fallido', isInitialStatus: false })
    await check({ ...BASE_ORDER, isSpecialDelivery: true }, ALL.filter((p) => p !== 'trips.dispatch'))
    await check({ ...BASE_ORDER, isSpecialDelivery: true }, ALL, ['LTL_GROUND'])
  })

  it('elige un chofer activo, escribe un comentario y manda POST /driver', async () => {
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Asignar chofer')
    await user.click(within(dialog).getByRole('combobox', { name: 'Chofer' }))
    await user.click(await screen.findByRole('option', { name: /Pedro Rivera/ }))
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), 'Urgente')
    await user.click(within(dialog).getByRole('button', { name: 'Asignar chofer' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/driver')).toHaveLength(1))
    const call = callsTo('POST', '/api/v1/orders/o-7/driver')[0]
    expect(call.body).toEqual({ driverPublicId: 'd-1', comment: 'Urgente', rowVersion: 'AAA=' })
    expect(call.headers.get('Idempotency-Key')).toBeTruthy()
    expect(await screen.findByText('Chofer asignado a la orden ORD-00007.')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/drivers')[0].url.searchParams.get('includeInactive')).toBe('false')
  })

  it('sin elegir chofer lo pide y no llama al API', async () => {
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Asignar chofer')
    await user.click(within(dialog).getByRole('button', { name: 'Asignar chofer' }))
    expect(await within(dialog).findByText('Elija el chofer.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/orders/o-7/driver')).toHaveLength(0)
  })

  it.each([
    [409, 'El chofer no está disponible para despacho: Licencia vencida.', 'conflict'],
    [422, 'La entrega especial ya llegó a destino o terminó; no se puede asignar ni reasignar el chofer.', 'status_rule'],
    [409, 'La orden ya está asignada a ese chofer.', 'conflict'],
    [404, 'Chofer no encontrado.', 'not_found'],
  ])('error %i del servidor, tal cual: %s', async (status, title, code) => {
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname.endsWith('/driver') ? problem(status, title, code) : undefined)
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Asignar chofer')
    await user.click(within(dialog).getByRole('combobox', { name: 'Chofer' }))
    await user.click(await screen.findByRole('option', { name: /María Soto/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Asignar chofer' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(title)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('crédito excedido con orders.credit_override: el botón pasa a «Autorizar y asignar» y reenvía con overrideCredit', async () => {
    let n = 0
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname.endsWith('/driver') && n++ === 0 ? problem(422, 'El cliente excede su límite de crédito.', 'credit_exceeded') : undefined
    wrap()
    await loaded()
    const { user, dialog } = await openDialog('Asignar chofer')
    await user.click(within(dialog).getByRole('combobox', { name: 'Chofer' }))
    await user.click(await screen.findByRole('option', { name: /Pedro Rivera/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Asignar chofer' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El cliente excede su límite de crédito.')
    await user.click(within(dialog).getByRole('button', { name: 'Autorizar y asignar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/driver')).toHaveLength(2))
    expect(callsTo('POST', '/api/v1/orders/o-7/driver')[1].body).toMatchObject({ driverPublicId: 'd-1', overrideCredit: true })
  })

  it('crédito excedido sin orders.credit_override: solo el mensaje, sin opción de autorizar', async () => {
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname.endsWith('/driver') ? problem(422, 'El cliente excede su límite de crédito.', 'credit_exceeded') : undefined)
    wrap(ALL.filter((p) => p !== 'orders.credit_override'))
    await loaded()
    const { user, dialog } = await openDialog('Asignar chofer')
    await user.click(within(dialog).getByRole('combobox', { name: 'Chofer' }))
    await user.click(await screen.findByRole('option', { name: /Pedro Rivera/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Asignar chofer' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El cliente excede su límite de crédito.')
    expect(within(dialog).queryByRole('button', { name: /Autorizar/ })).toBeNull()
  })

  it('sin fleet.view avisa que no puede cargar la lista y no la pide', async () => {
    wrap(ALL.filter((p) => p !== 'fleet.view'))
    await loaded()
    const { dialog } = await openDialog('Asignar chofer')
    expect(within(dialog).getByText(/no puede consultar los choferes/)).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Asignar chofer' })).toBeDisabled()
    expect(callsTo('GET', '/api/v1/drivers')).toHaveLength(0)
  })

  it('con chofer asignado muestra chofer y viaje en el panel y el botón dice «Reasignar chofer»', async () => {
    current = { ...current, assignedDriverPublicId: 'd-1', assignedDriverCode: 'CH-001', assignedDriverName: 'Pedro Rivera', assignedTripCode: 'VJ-00012' }
    wrap()
    await loaded()
    const panel = screen.getByRole('heading', { name: 'Chofer asignado' }).closest('.panel') as HTMLElement
    expect(panel).toHaveTextContent('CH-001 · Pedro Rivera')
    expect(panel).toHaveTextContent('VJ-00012')
    expect(headerBtn('Reasignar chofer')).toBeInTheDocument()
  })

  it('una orden normal sin viaje no muestra el panel «Chofer asignado»', async () => {
    current = { ...BASE_ORDER }
    wrap()
    await loaded()
    expect(screen.queryByRole('heading', { name: 'Chofer asignado' })).toBeNull()
  })
})

describe('Expediente — contactos y campos personalizados', () => {
  it('lista los contactos con su rótulo y permite agregar un teléfono (contacts.manage + orders.edit)', async () => {
    wrap()
    await loaded()
    const panel = (await screen.findByText('Recibe: Carmen Ortiz', { exact: false })).closest('.panel') as HTMLElement
    expect(panel).toHaveTextContent('(787) 555-0142')
    const user = userEvent.setup()
    await user.click(within(panel).getByRole('button', { name: 'Agregar teléfono' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText(/^Teléfono/), '7875559999')
    await user.type(within(dialog).getByLabelText('Etiqueta'), 'Recibe: Luis')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contacts/TRANSPORT_ORDER/7')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/contacts/TRANSPORT_ORDER/7')[0].body).toMatchObject({ value: '7875559999', label: 'Recibe: Luis' })
  })

  it('sin contacts.manage la lista es de solo lectura', async () => {
    wrap(ALL.filter((p) => p !== 'contacts.manage'))
    await loaded()
    const panel = (await screen.findByText('Recibe: Carmen Ortiz', { exact: false })).closest('.panel') as HTMLElement
    expect(within(panel).queryByRole('button')).toBeNull()
  })

  it('campos personalizados de TRANSPORT_ORDER: panel editable solo con el módulo y definiciones', async () => {
    definitions = [{ id: 1, fieldKey: 'cost_center', label: 'Centro de costo', dataType: 'TEXT', sortOrder: 1, isActive: true }]
    const first = wrap(ALL, ['LTL_GROUND', 'CATALOG', 'CUSTOM_FIELDS'])
    await loaded()
    const panel = (await screen.findByRole('heading', { name: 'Campos personalizados' })).closest('.panel') as HTMLElement
    expect(await within(panel).findByLabelText('Centro de costo')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/custom-fields/definitions')[0].url.searchParams.get('entityType')).toBe('TRANSPORT_ORDER')
    first.unmount()
    mock.calls = []
    wrap(ALL, ['LTL_GROUND', 'CATALOG'])
    await loaded()
    expect(screen.queryByRole('heading', { name: 'Campos personalizados' })).toBeNull()
    expect(callsTo('GET', '/api/v1/custom-fields/definitions')).toHaveLength(0)
  })

  it('sin definiciones el panel de campos no se pinta', async () => {
    wrap(ALL, ['LTL_GROUND', 'CATALOG', 'CUSTOM_FIELDS'])
    await loaded()
    await waitFor(() => expect(callsTo('GET', '/api/v1/custom-fields/definitions')).toHaveLength(1))
    expect(screen.queryByRole('heading', { name: 'Campos personalizados' })).toBeNull()
  })
})

describe('Expediente — no encontrada', () => {
  it('404: aviso y enlace a la lista', async () => {
    mock.override = ({ url }) => (url.pathname === '/api/v1/orders/o-7' ? problem(404, 'Orden no encontrado.', 'not_found') : undefined)
    wrap()
    expect(await screen.findByText('La orden no existe o no pertenece a su compañía.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Volver a órdenes' })).toBeInTheDocument()
  })
})

describe('Expediente — Reabrir', () => {
  const FINAL = { ...BASE_ORDER, status: 'DELIVERED', statusLabel: 'Entregada', isInitialStatus: false, capabilities: { ...CONFIRMED_CAPS, canReopen: true } }

  it('solo se ve con capabilities.canReopen Y el permiso orders.reopen', async () => {
    current = { ...FINAL }
    wrap([...ALL, 'orders.reopen'])
    await loaded()
    expect(headerBtn('Reabrir')).toBeInTheDocument()
  })

  it('sin el permiso orders.reopen, o sin canReopen, no se pinta', async () => {
    current = { ...FINAL }
    wrap(ALL)
    await loaded()
    expect(headerBtn('Reabrir')).toBeNull()
  })

  it('sin canReopen (orden activa) no se pinta aunque tenga el permiso', async () => {
    current = { ...BASE_ORDER }
    wrap([...ALL, 'orders.reopen'])
    await loaded()
    expect(headerBtn('Reabrir')).toBeNull()
  })

  it('la razón es obligatoria: vacía no llama al API', async () => {
    current = { ...FINAL }
    wrap([...ALL, 'orders.reopen'])
    await loaded()
    const { user, dialog } = await openDialog('Reabrir')
    await user.click(within(dialog).getByRole('button', { name: 'Reabrir la orden' }))
    expect(await within(dialog).findByText('Indique la razón para reabrir la orden.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/orders/o-7/reopen')).toHaveLength(0)
  })

  it('manda POST /reopen con la razón, el estatus destino elegido y su Idempotency-Key, y avisa', async () => {
    current = { ...FINAL }
    wrap([...ALL, 'orders.reopen'])
    await loaded()
    const { user, dialog } = await openDialog('Reabrir')
    await user.selectOptions(within(dialog).getByLabelText('Estatus destino'), 'IN_TRANSIT')
    await user.type(within(dialog).getByLabelText('Razón para reabrir'), 'Se entregó por error')
    await user.click(within(dialog).getByRole('button', { name: 'Reabrir la orden' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/reopen')).toHaveLength(1))
    const call = callsTo('POST', '/api/v1/orders/o-7/reopen')[0]
    expect(call.body).toEqual({ toCode: 'IN_TRANSIT', comment: 'Se entregó por error', rowVersion: 'AAA=' })
    expect(call.headers.get('Idempotency-Key')).toBeTruthy()
    expect(await screen.findByText('Orden ORD-00007 reabierta.')).toBeInTheDocument()
  })

  it('por omisión no manda toCode (el último en que estuvo)', async () => {
    current = { ...FINAL }
    wrap([...ALL, 'orders.reopen'])
    await loaded()
    const { user, dialog } = await openDialog('Reabrir')
    await user.type(within(dialog).getByLabelText('Razón para reabrir'), 'Error de captura')
    await user.click(within(dialog).getByRole('button', { name: 'Reabrir la orden' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/reopen')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/orders/o-7/reopen')[0].body).toEqual({ toCode: null, comment: 'Error de captura', rowVersion: 'AAA=' })
  })
})

describe('Expediente — Devolver (Lote 112)', () => {
  const RETURNABLE = {
    ...BASE_ORDER, status: 'UNASSIGNED', statusLabel: 'Sin asignar', isInitialStatus: false,
    packages: [{ id: 1, packageType: 'BOX', packageTypeLabel: 'Caja', description: 'Ropa', pieces: 3, pieceNumbers: ['P-1', 'P-2', 'P-3'] }],
  }

  it('solo se ve con el permiso orders.return y en una orden recogida que sigue en curso', async () => {
    current = { ...RETURNABLE }
    wrap([...ALL, 'orders.return'])
    await loaded()
    expect(headerBtn('Devolver')).toBeInTheDocument()
  })

  it('sin el permiso, en una orden Sometida o en una final no se pinta', async () => {
    current = { ...RETURNABLE }
    wrap(ALL)
    await loaded()
    expect(headerBtn('Devolver')).toBeNull()
  })

  it('manda POST /return con las piezas marcadas y avisa; con todas marcadas no manda números', async () => {
    current = { ...RETURNABLE }
    wrap([...ALL, 'orders.return'])
    await loaded()
    const { user, dialog } = await openDialog('Devolver')
    await user.click(within(dialog).getByLabelText('P-3'))
    await user.type(within(dialog).getByLabelText('Nota (opcional)'), 'Cerrado')
    await user.click(within(dialog).getByRole('button', { name: 'Devolver la orden' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/return')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/orders/o-7/return')[0].body).toEqual({ pieceNumbers: ['P-1', 'P-2'], notes: 'Cerrado' })
    expect(await screen.findByText(/se creó la orden de vuelta ORD-00008/)).toBeInTheDocument()
  })

  it('Lote 113: la ficha avisa que se sugiere devolverla y, en una orden de vuelta, de qué orden es', async () => {
    current = { ...RETURNABLE, suggestReturn: true, failedAttempts: 2, returnOfOrderNumber: 'ORD-00003' }
    wrap([...ALL, 'orders.return'])
    await loaded()
    expect(screen.getByTestId('suggest-return')).toHaveTextContent('Se sugiere devolverla: 2 intento(s) fallido(s)')
    expect(screen.getByTestId('return-of')).toHaveTextContent('Vuelta de ORD-00003')
  })

  it('Lote 113: sin sugerencia la ficha no muestra el aviso', async () => {
    current = { ...RETURNABLE, suggestReturn: false, failedAttempts: 1 }
    wrap([...ALL, 'orders.return'])
    await loaded()
    expect(screen.queryByTestId('suggest-return')).toBeNull()
  })

  it('sin ninguna pieza marcada no llama al API', async () => {
    current = { ...RETURNABLE }
    wrap([...ALL, 'orders.return'])
    await loaded()
    const { user, dialog } = await openDialog('Devolver')
    for (const n of ['P-1', 'P-2', 'P-3']) await user.click(within(dialog).getByLabelText(n))
    await user.click(within(dialog).getByRole('button', { name: 'Devolver la orden' }))
    expect(await within(dialog).findByText('Marque al menos una pieza que se devuelve.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/orders/o-7/return')).toHaveLength(0)
  })
})

describe('Expediente — Registrar entrega (Lote 118)', () => {
  const IN_TRANSIT = { ...BASE_ORDER, status: 'IN_TRANSIT', statusLabel: 'En tránsito', isInitialStatus: false }

  it('solo se ve con el permiso orders.deliver y en una orden que sigue en curso', async () => {
    current = { ...IN_TRANSIT }
    wrap([...ALL, 'orders.deliver'])
    await loaded()
    expect(headerBtn('Registrar entrega')).toBeInTheDocument()
  })

  it('sin el permiso no se pinta', async () => {
    current = { ...IN_TRANSIT }
    wrap(ALL)
    await loaded()
    expect(headerBtn('Registrar entrega')).toBeNull()
  })

  it('en una orden en estatus final no se pinta', async () => {
    current = { ...BASE_ORDER, status: 'CANCELLED', statusLabel: 'Cancelada', isInitialStatus: false }
    wrap([...ALL, 'orders.deliver'])
    await loaded()
    expect(headerBtn('Registrar entrega')).toBeNull()
  })

  it('el diálogo avisa que la firma es automática, exige quién recibió y manda POST /delivery', async () => {
    current = { ...IN_TRANSIT }
    wrap([...ALL, 'orders.deliver'])
    await loaded()
    const { user, dialog } = await openDialog('Registrar entrega')
    expect(within(dialog).getByText(/se firma automáticamente a su nombre/i)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Registrar la entrega' }))
    expect(await within(dialog).findByText('Indique quién recibió la entrega.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/orders/o-7/delivery')).toHaveLength(0)
    await user.type(within(dialog).getByLabelText('Quién recibió'), 'Ana Rivera')
    await user.type(within(dialog).getByLabelText('Nota (opcional)'), 'Con la vecina')
    await user.click(within(dialog).getByRole('button', { name: 'Registrar la entrega' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/o-7/delivery')).toHaveLength(1))
    const call = callsTo('POST', '/api/v1/orders/o-7/delivery')[0]
    expect(call.body).toEqual({ recipientName: 'Ana Rivera', notes: 'Con la vecina', deliveredAtUtc: null })
    expect(call.headers.get('Idempotency-Key')).toBeTruthy()
    expect(await screen.findByText('Entrega registrada.')).toBeInTheDocument()
  })

  it('una orden entregada con comprobante lo muestra: quién recibió, quién lo registró y que el consignatario no firmó', async () => {
    current = { ...BASE_ORDER, status: 'DELIVERED', statusLabel: 'Entregada', isInitialStatus: false }
    proof = { publicId: 'pf-1', orderPublicId: 'o-7', orderNumber: 'ORD-00007', deliveredAtUtc: '2026-10-18T20:00:00Z', recipientName: 'Ana Rivera', notes: 'Con la vecina', source: 'STAFF_WEB', signatureKind: 'USER', registeredByName: 'Dalys', signedByName: 'Dalys', geoStatus: 'PENDING', latitude: null, longitude: null }
    wrap(ALL)
    await loaded()
    const panel = await screen.findByTestId('delivery-proof')
    expect(within(panel).getByText('Ana Rivera')).toBeInTheDocument()
    expect(within(panel).getByText(/Firmada automáticamente por Dalys/)).toBeInTheDocument()
    expect(within(panel).getByText('Buscando…')).toBeInTheDocument()
    expect(within(panel).getByText('El consignatario no firmó: la entrega la registró el personal.')).toBeInTheDocument()
    proof = null
  })

  it('una orden entregada sin comprobante no pinta el panel', async () => {
    current = { ...BASE_ORDER, status: 'DELIVERED', statusLabel: 'Entregada', isInitialStatus: false }
    proof = null
    wrap(ALL)
    await loaded()
    expect(screen.queryByTestId('delivery-proof')).toBeNull()
  })
})
