// Lote F-C1 — Alta detallada de una orden: predeterminados, validaciones (mensajes del servidor), consignatario existente y nuevo
// (modal del catálogo), borrador vs «Guardar y confirmar», factura repetida (R36), crédito excedido con y sin override, errores
// del servidor sin cerrar el formulario, entrega especial y permisos. Sobre un fetch simulado; el precio nunca se muestra.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { OrderEntryForm } from './OrderEntryForm'

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
    return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const C1 = { id: 1, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', isActive: true, numberSettings: { clientAssignsInvoiceNumber: false, clientAssignsOrderNumber: true } }
const C2 = { id: 2, publicId: 'c-2', code: 'BETA', name: 'Beta SA', isActive: true, numberSettings: { clientAssignsInvoiceNumber: true, clientAssignsOrderNumber: false } }
const BASE = { locationType: 'DELIVERY', country: 'PR', countryLabel: 'Puerto Rico', defaultServiceMinutes: 0, isActive: true, rowVersion: 'AAA=' }
const L1 = { ...BASE, id: 100, publicId: 'l-1', clientPublicId: 'c-1', isShared: false, code: 'C-001', name: 'Farmacia Central', line1: 'Calle Sol 1', city: 'San Juan', postalCode: '00901', allowDupInvoice: false }
const L2 = { ...BASE, id: 101, publicId: 'l-2', clientPublicId: null, isShared: true, code: null, name: 'Colmado Boricua', line1: 'Carr. 14 km 2', city: 'Ponce', postalCode: '00716', allowDupInvoice: true }
const CREATED = { id: 50, publicId: 'o-new', orderNumber: 'ORD-00050', status: 'DRAFT', quotedAmount: 999 }
let specials: Record<string, unknown> = { componentEnabled: true, items: [{ id: 12, typeId: 3, typeName: 'Traer vagón', rate: 75, isCurrent: true }] }
let settings: Record<string, unknown> = { defaultServiceType: 'STANDARD', defaultPackageType: 'BOX' }

function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/orders' && method === 'POST') return CREATED
  if (p === '/api/v1/clients' && method === 'GET') return [C1, C2]
  if (p === '/api/v1/clients/c-1') return C1
  if (p === '/api/v1/clients/c-2') return C2
  if (p === '/api/v1/clients/c-1/special-services') return specials
  if (p === '/api/v1/tenant/settings') return settings
  if (p === '/api/v1/catalogs/ServiceType') return [{ code: 'STANDARD', label: 'Estándar', isEnabled: true }, { code: 'EXPRESS', label: 'Express', isEnabled: true }]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Caja', isEnabled: true }, { code: 'ENV', label: 'Sobre', isEnabled: true }]
  if (p === '/api/v1/locations' && method === 'GET') return [L1, L2]
  if (p === '/api/v1/locations' && method === 'POST') return { ...BASE, id: 110, publicId: 'l-new', isShared: false, clientPublicId: body?.clientPublicId, code: null, name: body?.name, line1: body?.line1, city: body?.city, allowDupInvoice: false }
  if (p === '/api/v1/locations/l-new') return { ...BASE, id: 110, publicId: 'l-new', isShared: false, clientPublicId: 'c-1', code: null, name: 'Tienda Nueva', line1: 'Calle 9', city: 'PONCE', postalCode: '00716', allowDupInvoice: false }
  if (p === '/api/v1/contacts/LOCATION/100' && method === 'GET') return [{ id: 50, ownerEntity: 'LOCATION', ownerId: 100, contactType: 'PHONE', value: '7875550100', isPrimary: true, isActive: true }]
  if (p.startsWith('/api/v1/contacts/LOCATION/')) return []
  if (p === '/api/v1/postal-localities') return [{ id: 1, city: 'PONCE', postalCode: '00716', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Ponce' }]
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/custom-fields/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

const onCreated = vi.fn()
function wrap(permissions: string[] = ['orders.create', 'locations.read', 'clients.read'], labelOnSave?: { value: boolean; onChange: (on: boolean) => void }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['LTL_GROUND', 'CATALOG']}>
          <OrderEntryForm onCreated={onCreated} labelOnSave={labelOnSave} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const ALL = ['orders.create', 'locations.read', 'locations.create', 'locations.update', 'clients.read', 'contracts.read', 'contacts.manage']
const posts = () => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === '/api/v1/orders')

async function pickClient(user: ReturnType<typeof userEvent.setup>, text = 'Acme') {
  const box = screen.getByRole('combobox', { name: /^Cliente/ })
  await user.click(box)
  await user.type(box, text)
  await user.click(await screen.findByRole('option', { name: new RegExp(text) }))
}
async function pickConsignee(user: ReturnType<typeof userEvent.setup>, text = 'Farmacia') {
  const box = screen.getByRole('combobox', { name: /^Nombre/ })
  await user.click(box)
  await user.click(await screen.findByRole('option', { name: new RegExp(text) }))
}
/** Cliente Acme + consignatario Farmacia Central + 2 cajas: lo mínimo para guardar. */
async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  await pickClient(user)
  await pickConsignee(user)
  await waitFor(() => expect(screen.getByLabelText(/^Piezas/)).toBeInTheDocument())
  await user.clear(screen.getByLabelText(/^Piezas/))
  await user.type(screen.getByLabelText(/^Piezas/), '2')
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  onCreated.mockReset()
  specials = { componentEnabled: true, items: [{ id: 12, typeId: 3, typeName: 'Traer vagón', rate: 75, isCurrent: true }] }
  settings = { defaultServiceType: 'STANDARD', defaultPackageType: 'BOX' }
})

describe('Alta detallada — captura', () => {
  it('arranca con el servicio y el tipo de paquete predeterminados y la factura en «(automático)»', async () => {
    wrap()
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Servicio/ })).toHaveValue('STANDARD'))
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Tipo de paquete/ })).toHaveValue('BOX'))
    const invoice = screen.getByLabelText(/N\.º de factura/)
    expect(invoice).toBeDisabled()
    expect(screen.getByText('(automático)')).toBeInTheDocument()
    expect(screen.getByText('Ticket de la orden')).toBeInTheDocument()
    // el segmentado Rápida/Detallada y los botones de labels no están (F-C3 / F-G)
    expect(screen.queryByRole('button', { name: /Rápida/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /label/i })).toBeNull()
  })

  it('un cliente que asigna su factura habilita el campo', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user, 'Beta')
    await waitFor(() => expect(screen.getByLabelText(/N\.º de factura/)).toBeEnabled())
    expect(screen.queryByText('(automático)')).toBeNull()
  })

  it('el consignatario existente rellena nombre, número, dirección, pueblo, ZIP y teléfono (solo lectura) y avisa de la factura repetida', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user)
    await pickConsignee(user)
    await waitFor(() => expect(screen.getByLabelText('Dirección')).toHaveValue('Calle Sol 1'))
    expect(screen.getByLabelText('Número de consignatario')).toHaveValue('C-001')
    expect(screen.getByLabelText('Pueblo')).toHaveValue('San Juan')
    expect(screen.getByLabelText('ZIP')).toHaveValue('00901')
    await waitFor(() => expect(screen.getByLabelText('Teléfono')).toHaveValue('(787) 555-0100'))
    for (const label of ['Dirección', 'Pueblo', 'ZIP', 'Teléfono', 'Número de consignatario']) expect(screen.getByLabelText(label)).toBeDisabled()
    expect(screen.getByText('Este consignatario no admite repetir el número de factura.')).toBeInTheDocument()
    // pide los del cliente MÁS los compartidos, solo de entrega
    const q = mock.calls.find((c) => c.url.pathname === '/api/v1/locations' && c.url.searchParams.get('clientId') === 'c-1')!.url.searchParams
    expect(q.get('includeShared')).toBe('true')
    expect(q.get('locationType')).toBe('DELIVERY')
  })

  it('el selector ofrece dirección · pueblo y marca el que admite factura repetida', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user)
    await user.click(screen.getByRole('combobox', { name: /^Nombre/ }))
    const opt = await screen.findByRole('option', { name: /Colmado Boricua/ })
    expect(opt).toHaveTextContent('Carr. 14 km 2 · Ponce')
    expect(opt).toHaveTextContent('admite factura repetida')
    await user.click(opt)
    expect(await screen.findByText('Este consignatario admite repetir el número de factura.')).toBeInTheDocument()
  })

  it('el ticket resume cliente, servicio, consignatario, pueblo, paquetes ×piezas y COD, sin precio', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.type(screen.getByLabelText('Monto COD'), '50')
    const ticket = screen.getByRole('complementary', { name: 'Ticket de la orden' })
    await waitFor(() => expect(within(ticket).getByText('Acme Corp')).toBeInTheDocument())
    expect(within(ticket).getByText('Estándar')).toBeInTheDocument()
    expect(within(ticket).getByText('Farmacia Central')).toBeInTheDocument()
    expect(within(ticket).getByText('San Juan')).toBeInTheDocument()
    expect(within(ticket).getByText('Caja ×2')).toBeInTheDocument()
    expect(within(ticket).getByText('$50.00')).toBeInTheDocument()
    // la única cifra es el COD capturado: ningún precio ni cotización
    expect(within(ticket).getAllByText(/\$/)).toHaveLength(1)
    expect(ticket).not.toHaveTextContent(/999|Cotizado/)
    expect(mock.calls.some((c) => c.url.pathname.endsWith('/quote'))).toBe(false)
  })

  it('añade y quita líneas de paquete (mínimo una)', async () => {
    const user = userEvent.setup()
    wrap()
    expect(screen.getByRole('button', { name: 'Quitar paquete 1' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: /Añadir paquete/ }))
    expect(screen.getAllByLabelText(/^Piezas/)).toHaveLength(2)
    // la línea nueva nace con el tipo predeterminado
    await waitFor(() => expect(screen.getAllByRole('combobox', { name: /^Tipo de paquete/ })[1]).toHaveValue('BOX'))
    await user.click(screen.getByRole('button', { name: 'Quitar paquete 2' }))
    expect(screen.getAllByLabelText(/^Piezas/)).toHaveLength(1)
  })
})

describe('Alta detallada — guardar', () => {
  it('las validaciones usan los mensajes del servidor y no llaman al API', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(screen.getByRole('combobox', { name: /^Servicio/ }))
    await user.selectOptions(screen.getByRole('combobox', { name: /^Servicio/ }), '')
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    expect(await screen.findByText('El cliente es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('El tipo de servicio es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.')).toBeInTheDocument()
    await user.selectOptions(screen.getByRole('combobox', { name: /^Tipo de paquete/ }), '')
    await user.clear(screen.getByLabelText(/^Piezas/))
    await user.type(screen.getByLabelText(/^Piezas/), '0')
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    expect(await screen.findByText('El tipo de paquete es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('La cantidad de piezas debe ser al menos 1.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })

  it('«Guardar borrador» manda confirmNow=false con el consignatario del directorio, paquetes, COD, Lista/Límite e Idempotency-Key', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.type(screen.getByLabelText('Descripción'), 'Ropa')
    await user.type(screen.getByLabelText('Monto COD'), '50')
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    const post = posts()[0]
    expect(post.body).toMatchObject({
      clientPublicId: 'c-1',
      consigneeLocationPublicId: 'l-1',
      serviceType: 'STANDARD',
      confirmNow: false,
      clientInvoiceNumber: null,
      codAmount: 50,
      packages: [{ packageType: 'BOX', description: 'Ropa', pieces: 2 }],
    })
    expect(post.body?.requestedDate).toMatch(/^\d{4}-\d{2}-\d{2}T00:00:00$/)
    expect(post.body?.promisedDate).toMatch(/^\d{4}-\d{2}-\d{2}T00:00:00$/)
    expect(post.body).not.toHaveProperty('newConsignee')
    expect(post.body).not.toHaveProperty('overrideCredit')
    expect(post.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/)
    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(expect.objectContaining({ publicId: 'o-new', orderNumber: 'ORD-00050' }), false))
  })

  it('«Guardar y confirmar» manda confirmNow=true', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: 'Guardar y confirmar' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body?.confirmNow).toBe(true)
    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(expect.anything(), true))
  })

  it('con una factura tecleada (el cliente la asigna) la manda', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user, 'Beta')
    mock.override = (c) => (c.url.pathname === '/api/v1/locations' && c.method === 'GET' ? [{ ...L1, clientPublicId: 'c-2' }] : undefined)
    await waitFor(() => expect(screen.getByLabelText(/N\.º de factura/)).toBeEnabled())
    await pickConsignee(user)
    await user.type(screen.getByLabelText(/N\.º de factura/), 'AX-77')
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body?.clientInvoiceNumber).toBe('AX-77')
  })

  it('un error de línea del servidor (packages[0].pieces) cae bajo su campo y el formulario no se cierra', async () => {
    const user = userEvent.setup()
    mock.override = (c) =>
      c.method === 'POST' && c.url.pathname === '/api/v1/orders'
        ? problem(400, 'Hay datos por corregir.', 'validation', { 'packages[0].pieces': ['La cantidad de piezas debe ser al menos 1.'] })
        : undefined
    wrap()
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    expect(await screen.findByText('La cantidad de piezas debe ser al menos 1.')).toBeInTheDocument()
    expect(onCreated).not.toHaveBeenCalled()
  })

  it('«sin tarifa vigente» (422) se muestra con el texto del servidor y el formulario sigue abierto con lo capturado', async () => {
    const user = userEvent.setup()
    const msg = 'No hay tarifa vigente para STANDARD/BOX; configure la tarifa en el contrato del cliente antes de confirmar.'
    mock.override = (c) => (c.method === 'POST' && c.url.pathname === '/api/v1/orders' ? problem(422, msg, 'status_rule') : undefined)
    wrap()
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: 'Guardar y confirmar' }))
    expect((await screen.findAllByText(msg)).length).toBeGreaterThan(0)
    expect(onCreated).not.toHaveBeenCalled()
    expect(screen.getByLabelText(/^Piezas/)).toHaveValue(2)
    expect(screen.getByRole('button', { name: 'Guardar y confirmar' })).toBeEnabled()
  })

  it('cliente suspendido o dado de baja: el mensaje del servidor, sin crear', async () => {
    const user = userEvent.setup()
    mock.override = (c) =>
      c.method === 'POST' && c.url.pathname === '/api/v1/orders' ? problem(409, 'El cliente está dado de baja; solo se consulta su historial.', 'conflict') : undefined
    wrap()
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    expect((await screen.findAllByText('El cliente está dado de baja; solo se consulta su historial.')).length).toBeGreaterThan(0)
    expect(onCreated).not.toHaveBeenCalled()
  })
})

describe('Alta detallada — factura repetida (R36)', () => {
  const dupErrors = { clientInvoiceNumber: ['mensaje'], existingOrderNumber: ['ORD-00007'], existingOrderPublicId: ['o-7'] }

  async function startDuplicate(user: ReturnType<typeof userEvent.setup>, code: string, title: string) {
    mock.override = (c) => {
      if (c.method === 'POST' && c.url.pathname === '/api/v1/orders' && c.body?.confirmDuplicateInvoice !== true) return problem(409, title, code, dupErrors)
      return undefined
    }
    wrap()
    await pickClient(user, 'Beta')
    mock.override = (c) => {
      if (c.url.pathname === '/api/v1/locations' && c.method === 'GET') return [{ ...L2, clientPublicId: 'c-2' }]
      if (c.method === 'POST' && c.url.pathname === '/api/v1/orders' && c.body?.confirmDuplicateInvoice !== true) return problem(409, title, code, dupErrors)
      return undefined
    }
    await waitFor(() => expect(screen.getByLabelText(/N\.º de factura/)).toBeEnabled())
    await pickConsignee(user, 'Colmado')
    await user.type(screen.getByLabelText(/N\.º de factura/), 'FAC-100')
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
  }

  it('confirmable: abre «Factura repetida…»; «Crear de todos modos» reenvía con confirmDuplicateInvoice=true', async () => {
    const user = userEvent.setup()
    await startDuplicate(user, 'duplicate_invoice_confirmable', 'Colmado Boricua ya usa la factura. Confirme si desea crear la orden de todos modos.')
    const dialog = await screen.findByRole('dialog', { name: 'Factura repetida para este consignatario' })
    expect(within(dialog).getByText(/Colmado Boricua ya tiene la orden ORD-00007 con el número de factura FAC-100/)).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: 'Ver la orden ORD-00007' })).toHaveAttribute('href', '/orders/o-7')
    expect(posts()).toHaveLength(1)
    await user.click(within(dialog).getByRole('button', { name: 'Crear de todos modos' }))
    await waitFor(() => expect(posts()).toHaveLength(2))
    expect(posts()[1].body?.confirmDuplicateInvoice).toBe(true)
    expect(posts()[1].body?.clientInvoiceNumber).toBe('FAC-100')
    await waitFor(() => expect(onCreated).toHaveBeenCalled())
  })

  it('confirmable: «No crear la orden» cierra sin crear', async () => {
    const user = userEvent.setup()
    await startDuplicate(user, 'duplicate_invoice_confirmable', 'x')
    const dialog = await screen.findByRole('dialog', { name: 'Factura repetida para este consignatario' })
    await user.click(within(dialog).getByRole('button', { name: 'No crear la orden' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(posts()).toHaveLength(1)
    expect(onCreated).not.toHaveBeenCalled()
  })

  it('bloqueada (no admite repetidas): el mensaje del servidor bajo la factura, sin modal ni orden', async () => {
    const user = userEvent.setup()
    const msg = "El consignatario 'Colmado Boricua' no permite facturas repetidas: la orden ORD-00007 ya usa el número FAC-100."
    mock.override = (c) =>
      c.method === 'POST' && c.url.pathname === '/api/v1/orders' ? problem(409, msg, 'duplicate_invoice', { ...dupErrors, clientInvoiceNumber: [msg] }) : undefined
    wrap()
    await pickClient(user, 'Beta')
    mock.override = (c) => {
      if (c.url.pathname === '/api/v1/locations' && c.method === 'GET') return [{ ...L1, clientPublicId: 'c-2' }]
      if (c.method === 'POST' && c.url.pathname === '/api/v1/orders') return problem(409, msg, 'duplicate_invoice', { ...dupErrors, clientInvoiceNumber: [msg] })
      return undefined
    }
    await waitFor(() => expect(screen.getByLabelText(/N\.º de factura/)).toBeEnabled())
    await pickConsignee(user)
    await user.type(screen.getByLabelText(/N\.º de factura/), 'FAC-100')
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    expect((await screen.findAllByText(msg)).length).toBeGreaterThan(0)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByText('ORD-00007')).toBeNull()
    expect(onCreated).not.toHaveBeenCalled()
  })
})

describe('Alta detallada — crédito excedido', () => {
  const MSG = 'El cliente excede su límite de crédito: límite 1000.00, en curso 900.00, esta orden 200.00.'
  const credit = (c: Call) =>
    c.method === 'POST' && c.url.pathname === '/api/v1/orders' && c.body?.overrideCredit !== true
      ? problem(422, MSG, 'credit_exceeded', { limit: ['1000.00'], exposure: ['900.00'], newAmount: ['200.00'], available: ['100.00'] })
      : undefined

  it('con orders.credit_override: diálogo con el mensaje del servidor y «Autorizar y confirmar» reenvía con overrideCredit', async () => {
    const user = userEvent.setup()
    mock.override = credit
    wrap([...ALL, 'orders.credit_override'])
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: 'Guardar y confirmar' }))
    const dialog = await screen.findByRole('dialog', { name: 'El cliente excede su límite de crédito' })
    expect(within(dialog).getByText(MSG)).toBeInTheDocument()
    expect(onCreated).not.toHaveBeenCalled()
    await user.click(within(dialog).getByRole('button', { name: 'Autorizar y confirmar' }))
    await waitFor(() => expect(posts()).toHaveLength(2))
    expect(posts()[1].body).toMatchObject({ confirmNow: true, overrideCredit: true })
    expect(posts()[1].headers.get('Idempotency-Key')).not.toBe(posts()[0].headers.get('Idempotency-Key'))
    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(expect.anything(), true))
  })

  it('sin orders.credit_override: solo el mensaje, sin diálogo ni segundo envío, y la orden NO se crea', async () => {
    const user = userEvent.setup()
    mock.override = credit
    wrap(ALL)
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: 'Guardar y confirmar' }))
    expect((await screen.findAllByText(MSG)).length).toBeGreaterThan(0)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getByText(/La orden no se creó/)).toBeInTheDocument()
    expect(posts()).toHaveLength(1)
    expect(onCreated).not.toHaveBeenCalled()
  })

  it('el borrador no revisa crédito: nunca pide autorización', async () => {
    const user = userEvent.setup()
    wrap([...ALL, 'orders.credit_override'])
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(onCreated).toHaveBeenCalled())
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})

describe('Alta detallada — entrega especial', () => {
  it('el interruptor cambia la sección 03 a N.º de orden y Servicio especial («label · tarifa») y envía sin paquetes, COD ni chofer', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await pickClient(user)
    await pickConsignee(user)
    await user.click(screen.getByRole('switch', { name: 'Es una entrega especial' }))
    expect(screen.queryByLabelText('Monto COD')).toBeNull()
    expect(screen.queryByLabelText(/Chofer/)).toBeNull()
    expect(screen.getByLabelText(/N\.º de orden/)).toBeInTheDocument()
    const select = await screen.findByRole('combobox', { name: /^Servicio especial/ })
    await waitFor(() => expect(within(select).getByRole('option', { name: 'Traer vagón · $75.00' })).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    expect(await screen.findByText('El servicio especial es obligatorio en una entrega especial.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
    await user.selectOptions(select, 'Traer vagón · $75.00')
    // el ticket cambia de «Paquetes/COD» a «Servicio especial»
    const ticket = screen.getByRole('complementary', { name: 'Ticket de la orden' })
    expect(within(ticket).getByText('Traer vagón')).toBeInTheDocument()
    expect(within(ticket).queryByText('COD')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    const body = posts()[0].body!
    expect(body).toMatchObject({ isSpecialDelivery: true, specialServiceId: 12, consigneeLocationPublicId: 'l-1', confirmNow: false })
    expect(body).not.toHaveProperty('packages')
    expect(body).not.toHaveProperty('codAmount')
    expect(body).not.toHaveProperty('driverPublicId')
  })

  it('el cliente sin servicios especiales avisa «Este cliente no tiene servicios especiales configurados»', async () => {
    const user = userEvent.setup()
    specials = { componentEnabled: true, items: [] }
    wrap(ALL)
    await pickClient(user)
    await user.click(screen.getByRole('switch', { name: 'Es una entrega especial' }))
    const select = await screen.findByRole('combobox', { name: /^Servicio especial/ })
    await waitFor(() => expect(within(select).getByText('Este cliente no tiene servicios especiales configurados')).toBeInTheDocument())
    expect(select).toBeDisabled()
  })

  it('con el componente «Servicios especiales» apagado lo dice', async () => {
    const user = userEvent.setup()
    specials = { componentEnabled: false, items: [{ id: 12, typeName: 'Traer vagón', rate: 75, isCurrent: true }] }
    wrap(ALL)
    await pickClient(user)
    await user.click(screen.getByRole('switch', { name: 'Es una entrega especial' }))
    expect(await screen.findByText(/Servicios especiales.*apagado/)).toBeInTheDocument()
  })

  it('sin contracts.read el interruptor está deshabilitado con su razón y no se consultan los servicios', async () => {
    const user = userEvent.setup()
    wrap(['orders.create', 'locations.read', 'clients.read'])
    await pickClient(user)
    expect(screen.getByRole('switch', { name: 'Es una entrega especial' })).toBeDisabled()
    expect(screen.getByText(/permiso contracts\.read/)).toBeInTheDocument()
    expect(mock.calls.some((c) => c.url.pathname.endsWith('/special-services'))).toBe(false)
  })
})

describe('Alta detallada — consignatario nuevo y permisos', () => {
  it('«+» abre el modal de alta del catálogo (con teléfono) y deja elegido el recién creado, sin guardar la orden', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await pickClient(user)
    await user.click(screen.getByRole('button', { name: 'Nuevo consignatario' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo consignatario' })
    expect(within(dialog).getByLabelText('Teléfono')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Tienda Nueva')
    await user.type(within(dialog).getByLabelText(/^Dirección\s*\*?$/), 'Calle 9')
    await user.click(within(dialog).getByRole('combobox', { name: /Pueblo/ }))
    await user.click(await within(dialog).findByRole('option', { name: /PONCE/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === '/api/v1/locations')).toHaveLength(1))
    // el consignatario nace con el cliente de la orden como dueño
    expect(mock.calls.find((c) => c.method === 'POST' && c.url.pathname === '/api/v1/locations')!.body?.clientPublicId).toBe('c-1')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo consignatario' })).toBeNull())
    await waitFor(() => expect(screen.getByLabelText('Dirección')).toHaveValue('Calle 9'))
    expect(screen.getByRole('complementary', { name: 'Ticket de la orden' })).toHaveTextContent('Tienda Nueva')
    // guardar el consignatario NO guarda la orden
    expect(posts()).toHaveLength(0)
    // y la orden se manda con el consignatario recién creado
    await user.click(screen.getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ consigneeLocationPublicId: 'l-new' })
    expect(posts()[0].body).not.toHaveProperty('newConsignee')
  })

  it('sin locations.create no aparece el «+»', async () => {
    const user = userEvent.setup()
    wrap(['orders.create', 'locations.read', 'clients.read'])
    await pickClient(user)
    expect(screen.queryByRole('button', { name: 'Nuevo consignatario' })).toBeNull()
  })

  it('sin orders.create no hay botones de guardar', async () => {
    wrap(['locations.read', 'clients.read'])
    await screen.findByText('Ticket de la orden')
    expect(screen.queryByRole('button', { name: 'Guardar borrador' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Guardar y confirmar' })).toBeNull()
  })

  it('sin cliente el selector de consignatario espera y el «+» está deshabilitado', async () => {
    wrap(ALL)
    expect(screen.getByRole('combobox', { name: /^Nombre/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Nuevo consignatario' })).toBeDisabled()
  })
})

describe('Alta detallada — «Label al guardar» (Lote F-G2)', () => {
  it('sin el interruptor no se pinta; con él, junto a los botones de guardar y con la nota de la maqueta', async () => {
    const first = wrap()
    expect(screen.queryByRole('switch', { name: 'Label al guardar' })).toBeNull()
    first.unmount()
    const onChange = vi.fn()
    wrap(undefined, { value: true, onChange })
    const sw = screen.getByRole('switch', { name: 'Label al guardar' })
    expect(sw).toBeChecked()
    expect(screen.getByText(/El label sale automático en la impresora de esta estación al guardar/)).toBeInTheDocument()
    await userEvent.setup().click(sw)
    expect(onChange).toHaveBeenCalledWith(false)
  })
})
