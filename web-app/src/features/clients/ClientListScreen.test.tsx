// F-A1 — Clientes: lista maestra (búsqueda al API, inactivos), alta (éxito, 409 de código, validaciones) y ficha (perfil con
// 409 de rowVersion, teléfonos y correos, personas de contacto, numeración con ejemplo en vivo y errores 400, baja y
// reactivación, transición de estatus, solo lectura sin permisos). Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ClientListScreen from './ClientListScreen'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
}
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  /** Respuesta forzada por una prueba (se consulta antes que las rutas normales). */
  override: null as null | ((call: Call) => unknown),
}))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = mock.override?.(call) ?? route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const LIST = [
  { id: 10, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', status: 'ACTIVE', statusLabel: 'Activo', isActive: true, billingSummary: 'Por servicio', contractsCount: 1 },
  { id: 11, publicId: 'c-2', code: 'BETA', name: 'Beta SA', status: 'ACTIVE', statusLabel: 'Activo', isActive: false, billingSummary: 'Por servicio + COD', contractsCount: 0 },
]
const CLIENT_BASE = {
  id: 10,
  publicId: 'c-1',
  code: 'ACME',
  name: 'Acme Corp',
  legalName: 'Acme LLC',
  taxId: '66-123',
  creditLimit: 1000,
  paymentTerm: 'NET30',
  currency: 'USD',
  status: 'ACTIVE',
  statusLabel: 'Activo',
  isActive: true,
  physicalAddress: { id: 1, publicId: 'a-1', line1: 'Calle 1', city: 'San Juan', state: 'PR', postalCode: '00901', countryLabel: 'Puerto Rico', isActive: true },
  postalAddress: null,
  pickupAddress: { id: 1, publicId: 'a-1', isDefaultFromCorporate: true, isActive: true },
  pickupLocations: [{ id: 5, publicId: 'loc-1', code: 'ALM1', name: 'Almacén 1', isActive: true }],
  numberSettings: {
    clientAssignsOrderNumber: true,
    clientAssignsInvoiceNumber: false,
    orderNumberFormat: 'AX-#####',
    invoiceNumberFormat: null,
    packageNumberFormat: null,
    orderNumberPreview: 'AX-00001',
    invoiceNumberPreview: 'FAC-00001',
    packageNumberPreview: 'PQT-00001',
  },
  contacts: [{ id: 3, fullName: 'Ana Pérez', role: 'Compras', isPrimary: true, isActive: true, contactPoints: [{ id: 30, contactType: 'PHONE', value: '7875551234', isPrimary: true, isActive: true }, { id: 31, contactType: 'EMAIL', value: 'ana@acme.com', isPrimary: true, isActive: true }] }],
  contactPoints: [
    { id: 20, contactType: 'PHONE', contactTypeLabel: 'Teléfono', value: '7875550000', isPrimary: true, isActive: true },
    { id: 21, contactType: 'EMAIL', contactTypeLabel: 'Correo', value: 'info@acme.com', isPrimary: true, isActive: true },
  ],
  contracts: [{ id: 1, publicId: 'k-1', contractNumber: 'ACME-001', title: 'Contrato marco', startDate: '2026-01-15', endDate: null, status: 'DRAFT', statusLabel: 'Borrador', isActive: true, isCurrent: true }],
  billingSummary: 'Por servicio',
  rowVersion: 'AAA=',
}
const STATUSES = [
  { code: 'ACTIVE', label: 'Activo', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 10 },
  { code: 'REVIEW', label: 'En revisión', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 20 },
]

let client = { ...CLIENT_BASE } as Record<string, unknown>

function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/clients' && method === 'GET') {
    const search = (url.searchParams.get('search') ?? '').toLowerCase()
    const inactive = url.searchParams.get('includeInactive') === 'true'
    return LIST.filter((c) => (inactive || c.isActive) && (!search || c.name.toLowerCase().includes(search)))
  }
  if (p === '/api/v1/clients' && method === 'POST') return { ...CLIENT_BASE, id: 12, publicId: 'c-new', code: 'NUEVO', name: String(body?.name) }
  if (p === '/api/v1/clients/number-format/preview') {
    const pattern = url.searchParams.get('pattern') ?? ''
    if (pattern.includes('!')) return problem(400, 'Solicitud inválida', 'validation', { pattern: [`Carácter no permitido en el patrón: '!'. Use letras, dígitos y - _ / . # @.`] })
    return { pattern, seq: 1, value: pattern.replace(/#+/, (m) => '1'.padStart(m.length, '0')) }
  }
  if (p === '/api/v1/clients/c-new') return { ...CLIENT_BASE, id: 12, publicId: 'c-new', code: 'NUEVO', name: 'Nuevo SA' }
  if (p === '/api/v1/clients/c-1' && method === 'GET') return client
  if (p === '/api/v1/clients/c-2' && method === 'GET') return { ...CLIENT_BASE, id: 11, publicId: 'c-2', code: 'BETA', name: 'Beta SA', isActive: false }
  if (p === '/api/v1/clients/c-1/profile') return { ...client, ...body, rowVersion: 'BBB=' }
  if (p === '/api/v1/clients/c-1/number-settings') return { ...client, rowVersion: 'CCC=' }
  if (p === '/api/v1/clients/c-1/status') return { ...client, status: 'REVIEW', statusLabel: 'En revisión' }
  if (p.endsWith('/deactivate') || p.endsWith('/reactivate')) return new Response(null, { status: 200 })
  if (p === '/api/v1/clients/c-1/contacts') return { id: 4, fullName: 'Luis', isPrimary: false, isActive: true }
  if (p === '/api/v1/clients/c-1/contacts/3') return { id: 3, isActive: false }
  if (p.startsWith('/api/v1/contacts/')) return { id: 99 }
  if (p === '/api/v1/catalogs/PaymentTerm') return [{ code: 'NET30', label: 'Neto 30', isEnabled: true }, { code: 'NET60', label: 'Neto 60', isEnabled: true }]
  if (p === '/api/v1/catalogs/Currency') return [{ code: 'USD', label: 'Dólar', isEnabled: true }]
  if (p === '/api/v1/catalogs/ContactType')
    return [{ code: 'PHONE', label: 'Teléfono', isEnabled: true }, { code: 'MOBILE', label: 'Móvil', isEnabled: true }, { code: 'EMAIL', label: 'Correo', isEnabled: true }]
  if (p === '/api/v1/status/ClientStatus') return STATUSES
  if (p === '/api/v1/status/ClientStatus/validate') return { isValid: true, errors: [], warnings: [] }
  if (p.startsWith('/api/v1/status/lateral-entries/') || p.startsWith('/api/v1/status/history/') || p.startsWith('/api/v1/status/')) return []
  if (p.startsWith('/api/v1/catalogs/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(permissions: string[], modules = ['CATALOG'], entry = '/catalog/clients') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={modules}>
          <ClientListScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const ALL = ['clients.read', 'clients.create', 'clients.update', 'contacts.manage']
const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  client = { ...CLIENT_BASE }
})

describe('Clientes — lista', () => {
  it('muestra los clientes activos con contador, código, estatus y resumen de facturación', async () => {
    wrap(ALL)
    const list = await screen.findByRole('list', { name: 'Clientes' })
    expect(within(list).getByText('Acme Corp')).toBeInTheDocument()
    expect(within(list).getByText('ACME')).toBeInTheDocument()
    expect(within(list).getByText('Por servicio')).toBeInTheDocument()
    // por omisión no trae inactivos
    expect(within(list).queryByText('Beta SA')).toBeNull()
    expect(callsTo('GET', '/api/v1/clients')[0].url.searchParams.get('includeInactive')).toBe('false')
    // la ficha del primero se abre sola
    expect(await screen.findByRole('heading', { name: 'Acme Corp' })).toBeInTheDocument()
  })

  it('el buscador consulta al API con `search`', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Clientes' })
    await user.type(screen.getByRole('searchbox'), 'zzz')
    await waitFor(() => expect(mock.calls.some((c) => c.url.pathname === '/api/v1/clients' && c.url.searchParams.get('search') === 'zzz')).toBe(true))
    expect(await screen.findByText('Ningún cliente coincide con la búsqueda.')).toBeInTheDocument()
  })

  it('«Mostrar inactivos» pide includeInactive y marca la fila como Inactivo; elegirla abre su ficha en la URL', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Clientes' })
    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivos' }))
    const row = await screen.findByRole('button', { name: /Beta SA/ })
    expect(within(row).getByText('Inactivo')).toBeInTheDocument()
    await user.click(row)
    await waitFor(() => expect(callsTo('GET', '/api/v1/clients/c-2').length).toBeGreaterThan(0))
    expect(await screen.findByRole('heading', { name: 'Beta SA' })).toBeInTheDocument()
    // baja: ofrece Reactivar en vez de Dar de baja
    expect(screen.getByRole('button', { name: /Reactivar/ })).toBeInTheDocument()
  })

  it('selecciona por la URL (?client=)', async () => {
    wrap(ALL, ['CATALOG'], '/catalog/clients?client=c-2')
    expect(await screen.findByRole('heading', { name: 'Beta SA' })).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/clients/c-1')).toHaveLength(0)
  })
})

describe('Clientes — alta', () => {
  it('crea con contrato inicial por omisión y selecciona el cliente nuevo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Clientes' })
    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' })
    // contrato inicial encendido y título por omisión
    expect(within(dialog).getByRole('switch', { name: /Crear contrato inicial/ })).toBeChecked()
    expect(within(dialog).getByLabelText(/Título del contrato/)).toHaveValue('Contrato marco')
    expect((within(dialog).getByLabelText(/Cliente desde/) as HTMLInputElement).value).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Nuevo SA')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients')).toHaveLength(1))
    const sent = callsTo('POST', '/api/v1/clients')[0].body as Record<string, unknown>
    expect(sent).toMatchObject({ name: 'Nuevo SA', code: null, legalName: null, creditLimit: null, contract: { title: 'Contrato marco' } })
    expect((sent.contract as { startDate: string }).startDate).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    // el cliente nuevo queda elegido
    await waitFor(() => expect(callsTo('GET', '/api/v1/clients/c-new').length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo cliente' })).toBeNull())
  })

  it('sin contrato inicial no manda `contract`', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Clientes' })
    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Sin Contrato')
    await user.click(within(dialog).getByRole('switch', { name: /Crear contrato inicial/ }))
    expect(within(dialog).queryByLabelText(/Título del contrato/)).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/clients')[0].body).not.toHaveProperty('contract')
  })

  it('validaciones en pantalla: nombre obligatorio y límite no negativo (sin llamar al servidor)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Clientes' })
    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' })
    await user.type(within(dialog).getByLabelText(/Límite de crédito/), '-5')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El nombre es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('El límite de crédito no puede ser negativo.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/clients')).toHaveLength(0)
  })

  it('409 de código duplicado: mensaje del servidor bajo el código y el modal sigue abierto', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/clients' ? problem(409, 'Ya existe un cliente con ese código.', 'conflict') : undefined
    wrap(ALL)
    await screen.findByRole('list', { name: 'Clientes' })
    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Otro')
    await user.type(within(dialog).getByLabelText(/^Código/), 'acme')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect((await within(dialog).findAllByText('Ya existe un cliente con ese código.')).length).toBeGreaterThan(0)
    expect(screen.getByRole('dialog', { name: 'Nuevo cliente' })).toBeInTheDocument()
  })

  it('400 con errores por campo: se ven bajo cada campo (incluido el del contrato)', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/clients'
        ? problem(400, 'Solicitud inválida', 'validation', { name: ['No puede exceder 200 caracteres.'], 'contract.title': ['El título no puede exceder 200 caracteres.'] })
        : undefined
    wrap(ALL)
    await screen.findByRole('list', { name: 'Clientes' })
    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'X')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('No puede exceder 200 caracteres.')).toBeInTheDocument()
    expect(within(dialog).getByText('El título no puede exceder 200 caracteres.')).toBeInTheDocument()
  })

  it('sin clients.create no hay «Nuevo cliente»', async () => {
    wrap(['clients.read'])
    await screen.findByRole('list', { name: 'Clientes' })
    expect(screen.queryByRole('button', { name: 'Nuevo cliente' })).toBeNull()
  })
})

describe('Clientes — ficha', () => {
  it('arma los paneles de la ficha en orden', async () => {
    wrap(ALL)
    await screen.findByRole('heading', { name: 'Acme Corp' })
    const titles = (await screen.findAllByRole('heading', { level: 2 })).map((h) => h.textContent)
    const order = ['Acme Corp', 'Perfil del cliente', 'Teléfonos y correos', 'Personas de contacto', 'Numeración', 'Contratos', 'Historial de estatus']
    const idx = order.map((o) => titles.indexOf(o))
    expect(idx.every((n) => n >= 0)).toBe(true)
    expect([...idx].sort((a, b) => a - b)).toEqual(idx)
    // sin el módulo CUSTOM_FIELDS no hay panel de campos personalizados
    expect(titles).not.toContain('Campos personalizados')
  })

  it('perfil: guarda con rowVersion; las direcciones son de solo lectura y la postal igual a la física se avisa', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('heading', { name: 'Perfil del cliente' })
    expect(await screen.findByText('Calle 1 · San Juan, PR 00901 · Puerto Rico')).toBeInTheDocument()
    expect(screen.getByText('La dirección postal es la misma que la física')).toBeInTheDocument()
    const legal = screen.getByLabelText('Razón social')
    await user.clear(legal)
    await user.type(legal, 'Acme Inc')
    await user.selectOptions(screen.getByLabelText('Punto de recogido por defecto'), 'loc-1')
    await user.click(screen.getAllByRole('button', { name: 'Guardar' })[0])
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/clients/c-1/profile')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/clients/c-1/profile')[0].body).toMatchObject({
      legalName: 'Acme Inc',
      taxId: '66-123',
      creditLimit: 1000,
      paymentTerm: 'NET30',
      currency: 'USD',
      defaultPickupLocationPublicId: 'loc-1',
      rowVersion: 'AAA=',
    })
  })

  it('perfil: 409 de rowVersion muestra el mensaje exacto del servidor y relee la ficha', async () => {
    const user = userEvent.setup()
    const msg = 'El registro fue modificado por otro usuario; recargue e intente de nuevo.'
    mock.override = ({ method, url }) => (method === 'PATCH' && url.pathname === '/api/v1/clients/c-1/profile' ? problem(409, msg, 'conflict') : undefined)
    wrap(ALL)
    await screen.findByRole('heading', { name: 'Perfil del cliente' })
    const before = callsTo('GET', '/api/v1/clients/c-1').length
    await user.type(await screen.findByLabelText('Identificación fiscal'), '9')
    await user.click(screen.getAllByRole('button', { name: 'Guardar' })[0])
    expect(await screen.findByText(msg)).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/clients/c-1').length).toBeGreaterThan(before))
  })

  it('perfil: límite de crédito vacío o negativo no se envía', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const limit = await screen.findByLabelText('Límite de crédito')
    await user.clear(limit)
    await user.click(screen.getAllByRole('button', { name: 'Guardar' })[0])
    expect(await screen.findByText('El límite de crédito no se puede dejar vacío; escriba 0 si no aplica.')).toBeInTheDocument()
    await user.type(limit, '-1')
    await user.click(screen.getAllByRole('button', { name: 'Guardar' })[0])
    expect(await screen.findByText('El límite de crédito no puede ser negativo.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/clients/c-1/profile')).toHaveLength(0)
  })

  it('teléfonos y correos: muestra con máscara y agrega un teléfono guardado solo con dígitos', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    expect(await screen.findByText('(787) 555-0000')).toBeInTheDocument()
    expect(screen.getByText('info@acme.com')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Agregar teléfono' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar teléfono' })
    await user.type(within(dialog).getByLabelText(/^Teléfono/), '7875559999')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contacts/CLIENT/10')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/contacts/CLIENT/10')[0].body).toMatchObject({ contactType: 'PHONE', value: '7875559999', isPrimary: false })
  })

  it('teléfonos y correos: correo inválido y teléfono incompleto se rechazan en pantalla; quitar usa DELETE', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('info@acme.com')
    await user.click(screen.getByRole('button', { name: 'Agregar correo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar correo' })
    await user.type(within(dialog).getByLabelText(/^Correo/), 'no-es-correo')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Correo inválido.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.url.pathname.startsWith('/api/v1/contacts/') && c.method === 'POST')).toBe(false)
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))

    await user.click(screen.getByRole('button', { name: 'Quitar info@acme.com' }))
    const confirm = await screen.findByRole('dialog', { name: 'Quitar el medio de contacto' })
    await user.click(within(confirm).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('DELETE', '/api/v1/contacts/21')).toHaveLength(1))
  })

  it('personas de contacto: agrega con teléfono y correo, y «Quitar» manda isActive:false', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const panel = (await screen.findByRole('heading', { name: 'Personas de contacto' })).closest('section') as HTMLElement
    expect(within(panel).getByText('Ana Pérez')).toBeInTheDocument()
    expect(within(panel).getByText('(787) 555-1234')).toBeInTheDocument()
    expect(within(panel).getAllByText('Principal').length).toBeGreaterThan(1) // encabezado de la columna y chip de la fila

    await user.click(within(panel).getByRole('button', { name: 'Agregar contacto' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar contacto' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Luis Ríos')
    await user.type(within(dialog).getByLabelText('Puesto'), 'Gerente')
    await user.type(within(dialog).getByLabelText('Teléfono'), '9395550000')
    await user.type(within(dialog).getByLabelText('Correo'), 'Luis@Acme.com')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/contacts')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/clients/c-1/contacts')[0].body).toEqual({
      fullName: 'Luis Ríos',
      role: 'Gerente',
      isPrimary: false,
      contactPoints: [
        { contactType: 'PHONE', value: '9395550000', isPrimary: true },
        { contactType: 'EMAIL', value: 'luis@acme.com', isPrimary: true },
      ],
    })

    await user.click(within(panel).getAllByRole('button', { name: 'Quitar' })[0])
    const confirm = await screen.findByRole('dialog', { name: 'Quitar el contacto' })
    await user.click(within(confirm).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/clients/c-1/contacts/3')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/clients/c-1/contacts/3')[0].body).toEqual({ isActive: false })
  })

  it('personas de contacto: editar cambia el teléfono con PUT y marca principal con PATCH', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const panel = (await screen.findByRole('heading', { name: 'Personas de contacto' })).closest('section') as HTMLElement
    await user.click(within(panel).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar contacto' })
    const phone = within(dialog).getByLabelText('Teléfono')
    expect(phone).toHaveValue('(787) 555-1234')
    await user.clear(phone)
    await user.type(phone, '7875557777')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/contacts/30')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/contacts/30')[0].body).toMatchObject({ contactType: 'PHONE', value: '7875557777' })
    expect(callsTo('PATCH', '/api/v1/clients/c-1/contacts/3')[0].body).toMatchObject({ fullName: 'Ana Pérez', isPrimary: true })
  })

  it('numeración: el ejemplo sale del servidor, un 400 del ejemplo se muestra y guarda con «Teikem asigna»', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const orderPattern = await screen.findByLabelText('Número de orden')
    expect(orderPattern).toHaveValue('AX-#####')
    // ejemplo guardado mientras no se edita
    expect(await screen.findByText('AX-00001')).toBeInTheDocument()

    await user.clear(orderPattern)
    await user.type(orderPattern, 'ZZ-###')
    await waitFor(() => expect(mock.calls.some((c) => c.url.pathname === '/api/v1/clients/number-format/preview' && c.url.searchParams.get('pattern') === 'ZZ-###')).toBe(true))
    expect(await screen.findByText('ZZ-001')).toBeInTheDocument()

    // error 400 del servidor al pedir el ejemplo
    await user.clear(orderPattern)
    await user.type(orderPattern, 'A!-#')
    expect(await screen.findByText(`Carácter no permitido en el patrón: '!'. Use letras, dígitos y - _ / . # @.`)).toBeInTheDocument()

    // patrón vacío = el del sistema
    await user.clear(orderPattern)
    expect(await screen.findByText('ORD-00001')).toBeInTheDocument()

    await user.type(orderPattern, 'XY-####')
    await user.selectOptions(screen.getByLabelText('¿Quién asigna el número de orden?'), 'teikem')
    const panel = screen.getByRole('heading', { name: 'Numeración' }).closest('section') as HTMLElement
    await user.click(within(panel).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/clients/c-1/number-settings')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/clients/c-1/number-settings')[0].body).toEqual({
      clientAssignsOrderNumber: false,
      clientAssignsInvoiceNumber: false,
      orderNumberFormat: 'XY-####',
      invoiceNumberFormat: '',
      packageNumberFormat: '',
    })
  })

  it('numeración: un patrón inválido no se envía y un 400 del guardado se ve bajo el campo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const panel = (await screen.findByRole('heading', { name: 'Numeración' })).closest('section') as HTMLElement
    const invoice = within(panel).getByLabelText('Número de factura')
    await user.type(invoice, 'SIN-DIGITO')
    await user.click(within(panel).getByRole('button', { name: 'Guardar' }))
    expect(await within(panel).findByText("El patrón debe incluir al menos un '#' para el consecutivo.")).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/clients/c-1/number-settings')).toHaveLength(0)

    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/clients/c-1/number-settings'
        ? problem(400, 'Solicitud inválida', 'validation', { packageNumberFormat: ['El patrón no puede exceder 40 caracteres.'] })
        : undefined
    await user.clear(invoice)
    await user.type(within(panel).getByLabelText('Número de paquete'), 'PQ-##')
    await user.click(within(panel).getByRole('button', { name: 'Guardar' }))
    expect(await within(panel).findByText('El patrón no puede exceder 40 caracteres.')).toBeInTheDocument()
  })

  it('contratos: sin contracts.read la sección avisa, muestra el resumen y no rompe la ficha (el detalle va en ClientContracts.test)', async () => {
    wrap(ALL)
    const panel = (await screen.findByRole('heading', { name: 'Contratos' })).closest('section') as HTMLElement
    expect(within(panel).getByText('Por servicio')).toBeInTheDocument()
    expect(within(panel).getByText(/contracts\.read/)).toBeInTheDocument()
    expect(within(panel).queryByRole('tablist')).toBeNull()
    expect(callsTo('GET', '/api/v1/contracts/k-1')).toHaveLength(0)
    expect(await screen.findByRole('heading', { name: 'Historial de estatus' })).toBeInTheDocument()
  })

  it('baja y reactivación con confirmación (no es DELETE)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await user.click(await screen.findByRole('button', { name: /Dar de baja/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Dar de baja el cliente' })
    expect(within(dialog).getByText(/Acme Corp quedará inactivo/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/deactivate')).toHaveLength(1))
    expect(mock.calls.some((c) => c.method === 'DELETE')).toBe(false)
  })

  it('reactivar un cliente dado de baja', async () => {
    const user = userEvent.setup()
    client = { ...CLIENT_BASE, isActive: false }
    wrap(ALL)
    await user.click(await screen.findByRole('button', { name: /Reactivar/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Reactivar el cliente' })
    expect(screen.getByText(/Este cliente está dado de baja/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Reactivar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/reactivate')).toHaveLength(1))
  })

  it('la baja rechazada por el servidor deja el diálogo abierto con su mensaje', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname.endsWith('/deactivate') ? problem(422, 'El cliente tiene órdenes abiertas.', 'status_rule') : undefined)
    wrap(ALL)
    await user.click(await screen.findByRole('button', { name: /Dar de baja/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Dar de baja el cliente' })
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja' }))
    expect(await within(dialog).findByText('El cliente tiene órdenes abiertas.')).toBeInTheDocument()
  })

  it('transición de estatus por el pipeline: POST /status con el código destino y el comentario', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await user.click(await screen.findByRole('button', { name: 'Avanzar a En revisión' }))
    await user.type(screen.getByLabelText('Comentario (opcional)'), 'Pide auditoría')
    await user.click(screen.getByRole('button', { name: 'Cambiar estatus' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/status')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/clients/c-1/status')[0].body).toEqual({ toCode: 'REVIEW', comment: 'Pide auditoría' })
  })

  it('solo lectura (clients.read): sin altas, bajas, guardados ni transiciones', async () => {
    wrap(['clients.read'])
    await screen.findByRole('heading', { name: 'Perfil del cliente' })
    await screen.findByText('(787) 555-0000')
    expect(screen.queryByRole('button', { name: 'Nuevo cliente' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Dar de baja/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Guardar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Agregar teléfono' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Agregar contacto' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Quitar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Editar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Avanzar a/ })).toBeNull()
    expect(screen.getByLabelText('Razón social')).toBeDisabled()
    expect(screen.getByLabelText('Número de orden')).toBeDisabled()
  })

  it('con clients.update pero sin contacts.manage no se escriben teléfonos ni correos', async () => {
    wrap(['clients.read', 'clients.update'])
    await screen.findByText('(787) 555-0000')
    expect(screen.queryByRole('button', { name: 'Agregar teléfono' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Agregar correo' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Agregar contacto' })).toBeInTheDocument()
  })
})
