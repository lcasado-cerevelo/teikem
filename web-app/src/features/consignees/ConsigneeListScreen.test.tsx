// Lote F-B — Consignatarios: lista maestra (contador, filas, buscador, filtro de cliente, compartidos, inactivos, selección por
// la URL), alta (valores del request, validaciones del manual sin llamar al servidor, 400 por campo, 409, teléfono), ficha
// (campos, teléfono de ContactPoint, edición con PATCH solo de lo cambiado y rowVersion, 409, factura repetida, baja y
// reactivación) y permisos. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ConsigneeListScreen from './ConsigneeListScreen'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
}
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  override: null as null | ((call: Call) => unknown),
}))
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
    const call = { method: req.method, url: new URL(req.url), body }
    mock.calls.push(call)
    const out = mock.override?.(call) ?? route(call)
    return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const BASE = {
  locationType: 'DELIVERY',
  country: 'PR',
  countryLabel: 'Puerto Rico',
  defaultServiceMinutes: 0,
  allowDupInvoice: false,
  isActive: true,
  rowVersion: 'AAA=',
}
const L1 = { ...BASE, id: 100, publicId: 'l-1', clientPublicId: 'c-1', clientName: 'Acme Corp', isShared: false, code: 'C-001', name: 'Farmacia Central', line1: 'Calle Sol 1', city: 'San Juan', state: 'PR', postalCode: '00901' }
const L2 = { ...BASE, id: 101, publicId: 'l-2', clientPublicId: null, clientName: null, isShared: true, code: null, name: 'Colmado Boricua', line1: 'Carr. 14 km 2', city: 'Ponce' }
const L3 = { ...BASE, id: 102, publicId: 'l-3', clientPublicId: 'c-2', clientName: 'Beta SA', isShared: false, code: 'C-003', name: 'Tienda Vieja', line1: 'Calle Luna 3', city: 'Arecibo', isActive: false }
const ALL_LOCS = [L1, L2, L3]

let current: Record<string, unknown> = { ...L1 }
let phones: Record<string, unknown>[] = []

function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/locations' && method === 'GET') {
    const inactive = url.searchParams.get('includeInactive') === 'true'
    const clientId = url.searchParams.get('clientId')
    return ALL_LOCS.filter((l) => (inactive || l.isActive) && (!clientId || (l.clientPublicId === clientId && url.searchParams.get('includeShared') !== 'true')))
  }
  if (p === '/api/v1/locations' && method === 'POST') return { ...BASE, id: 110, publicId: 'l-new', isShared: !body?.clientPublicId, clientPublicId: body?.clientPublicId ?? null, code: body?.code ?? null, name: body?.name, line1: body?.line1, city: body?.city }
  if (p === '/api/v1/locations/l-new') return { ...BASE, id: 110, publicId: 'l-new', isShared: true, name: 'Nuevo Cliente SA', line1: 'Calle 9', city: 'Ponce' }
  if (/^\/api\/v1\/locations\/l-\d$/.test(p) && method === 'GET') {
    if (p === '/api/v1/locations/l-1') return current
    return ALL_LOCS.find((l) => `/api/v1/locations/${l.publicId}` === p) ?? problem(404, 'Localización no encontrada', 'not_found')
  }
  if (p === '/api/v1/locations/l-1' && method === 'PATCH') return { ...current, ...body, rowVersion: 'BBB=' }
  if (p.endsWith('/deactivate') || p.endsWith('/reactivate')) return new Response(null, { status: 204 })
  if (p.startsWith('/api/v1/contacts/LOCATION/') && method === 'GET') return p.endsWith('/100') ? phones : []
  if (p.startsWith('/api/v1/contacts/')) return { id: 77, ...body }
  if (p === '/api/v1/clients') return [{ id: 1, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', isActive: true }, { id: 2, publicId: 'c-2', code: 'BETA', name: 'Beta SA', isActive: true }]
  if (p === '/api/v1/catalogs/Country') return [{ code: 'PR', label: 'Puerto Rico', isEnabled: true }, { code: 'US', label: 'Estados Unidos', isEnabled: true }]
  if (p === '/api/v1/postal-localities') return [{ id: 1, city: 'PONCE', postalCode: '00716', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Ponce' }]
  if (p.startsWith('/api/v1/custom-fields/')) return []
  if (p.startsWith('/api/v1/catalogs/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(permissions: string[], entry = '/catalog/consignees') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['CATALOG']}>
          <ConsigneeListScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const ALL = ['locations.read', 'locations.create', 'locations.update', 'contacts.manage', 'clients.read']
const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  current = { ...L1 }
  phones = [{ id: 50, ownerEntity: 'LOCATION', ownerId: 100, contactType: 'PHONE', value: '7875550100', isPrimary: true, isActive: true }]
})

describe('Consignatarios — lista', () => {
  it('pide solo DELIVERY y muestra contador, número, nombre, chip y «pueblo · dueño» o «Compartido»', async () => {
    wrap(ALL)
    const list = await screen.findByRole('list', { name: 'Consignatarios' })
    const first = callsTo('GET', '/api/v1/locations')[0].url.searchParams
    expect(first.get('locationType')).toBe('DELIVERY')
    expect(first.get('includeInactive')).toBe('false')
    const row1 = within(list).getByRole('button', { name: /Farmacia Central/ })
    expect(within(row1).getByText('C-001')).toBeInTheDocument()
    expect(within(row1).getByText('Activo')).toBeInTheDocument()
    expect(within(row1).getByText('San Juan · Acme Corp')).toBeInTheDocument()
    expect(within(list).getByText('Ponce · Compartido (todos los clientes)')).toBeInTheDocument()
    // los inactivos no vienen por omisión
    expect(within(list).queryByText('Tienda Vieja')).toBeNull()
    expect(screen.getByRole('heading', { level: 1, name: 'Consignatarios' })).toBeInTheDocument()
    // la ficha del primero se abre sola
    expect(await screen.findByRole('heading', { name: 'Ficha del consignatario' })).toBeInTheDocument()
  })

  it('el buscador filtra por pueblo y por «Compartido» sin volver a llamar al servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const list = await screen.findByRole('list', { name: 'Consignatarios' })
    const before = callsTo('GET', '/api/v1/locations').length
    await user.type(screen.getByRole('searchbox'), 'ponce')
    expect(within(list).queryByText('Farmacia Central')).toBeNull()
    expect(within(list).getByText('Colmado Boricua')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/locations')).toHaveLength(before)
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'zzz')
    expect(await screen.findByText('Sin resultados')).toBeInTheDocument()
  })

  it('«Mostrar inactivos» pide includeInactive y marca la fila Inactivo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Consignatarios' })
    await user.click(screen.getByRole('switch', { name: 'Mostrar inactivos' }))
    const row = await screen.findByRole('button', { name: /Tienda Vieja/ })
    expect(within(row).getByText('Inactivo')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/locations').some((c) => c.url.searchParams.get('includeInactive') === 'true')).toBe(true)
  })

  it('el filtro «Cliente» manda clientId sin compartidos; «Compartidos» se resuelve en el cliente', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const list = await screen.findByRole('list', { name: 'Consignatarios' })
    const filter = screen.getByLabelText('Cliente')
    await user.selectOptions(filter, 'Acme Corp')
    await waitFor(() => expect(callsTo('GET', '/api/v1/locations').some((c) => c.url.searchParams.get('clientId') === 'c-1' && c.url.searchParams.get('includeShared') === 'false')).toBe(true))
    await waitFor(() => expect(within(list).queryByText('Colmado Boricua')).toBeNull())
    expect(within(list).getByText('Farmacia Central')).toBeInTheDocument()
    await user.selectOptions(filter, 'Compartidos (sin dueño)')
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Consignatarios' })).queryByText('Farmacia Central')).toBeNull())
    expect(within(screen.getByRole('list', { name: 'Consignatarios' })).getByText('Colmado Boricua')).toBeInTheDocument()
  })

  it('selecciona por la URL (?consignee=) y al elegir una fila cambia la ficha', async () => {
    const user = userEvent.setup()
    wrap(ALL, '/catalog/consignees?consignee=l-2')
    expect(await screen.findByText('Colmado Boricua', { selector: '.cs-fact div' })).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/locations/l-1')).toHaveLength(0)
    await user.click(within(screen.getByRole('list', { name: 'Consignatarios' })).getByRole('button', { name: /Farmacia Central/ }))
    expect(await screen.findByText('Calle Sol 1')).toBeInTheDocument()
  })
})

describe('Consignatarios — ficha', () => {
  it('muestra número, nombre, dirección, pueblo, teléfono con formato, dueño y estado', async () => {
    wrap(ALL)
    await screen.findByRole('heading', { name: 'Ficha del consignatario' })
    expect(await screen.findByText('(787) 555-0100')).toBeInTheDocument()
    expect(screen.getByText('Calle Sol 1')).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: 'Estado' })).toBeChecked()
    expect(screen.getByRole('switch', { name: 'Número de factura repetido' })).not.toBeChecked()
    expect(screen.getByText('No permitido (bloquea)')).toBeInTheDocument()
    // las opciones de entrega van plegadas
    expect(screen.getByText('Opciones de entrega').closest('details')).not.toHaveAttribute('open')
  })

  it('«Número de factura repetido» guarda al instante con rowVersion', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('(787) 555-0100')
    await user.click(screen.getByRole('switch', { name: 'Número de factura repetido' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/locations/l-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/locations/l-1')[0].body).toEqual({ allowDupInvoice: true, rowVersion: 'AAA=' })
  })

  it('edita: manda en el PATCH solo lo cambiado, con rowVersion, y guarda el teléfono por ContactPoint', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('(787) 555-0100')
    await user.click(screen.getByRole('button', { name: /Editar/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar consignatario' })
    expect(within(dialog).getByLabelText(/^Nombre/)).toHaveValue('Farmacia Central')
    expect(within(dialog).getByLabelText('Teléfono')).toHaveValue('(787) 555-0100')
    await user.clear(within(dialog).getByLabelText(/^Nombre/))
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Farmacia Norte')
    await user.clear(within(dialog).getByLabelText('Teléfono'))
    await user.type(within(dialog).getByLabelText('Teléfono'), '7875559999')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/locations/l-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/locations/l-1')[0].body).toEqual({ name: 'Farmacia Norte', rowVersion: 'AAA=' })
    await waitFor(() => expect(callsTo('PUT', '/api/v1/contacts/50')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/contacts/50')[0].body).toMatchObject({ contactType: 'PHONE', value: '7875559999', isPrimary: true })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar consignatario' })).toBeNull())
  })

  it('cambiar a compartido manda makeShared; solo el teléfono no manda PATCH', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('(787) 555-0100')
    await user.click(screen.getByRole('button', { name: /Editar/ }))
    let dialog = await screen.findByRole('dialog', { name: 'Editar consignatario' })
    await user.clear(within(dialog).getByLabelText('Teléfono'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('DELETE', '/api/v1/contacts/50')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/locations/l-1')).toHaveLength(0)
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar consignatario' })).toBeNull())

    await user.click(screen.getByRole('button', { name: /Editar/ }))
    dialog = await screen.findByRole('dialog', { name: 'Editar consignatario' })
    await user.selectOptions(within(dialog).getByLabelText('Cliente dueño'), 'Compartido (todos los clientes)')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/locations/l-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/locations/l-1')[0].body).toMatchObject({ makeShared: true, rowVersion: 'AAA=' })
  })

  it('409 de rowVersion: el mensaje exacto del servidor queda en el modal, que sigue abierto', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/locations/l-1' ? problem(409, 'La localización fue modificada por otro usuario; recargue e intente de nuevo.', 'conflict') : undefined
    wrap(ALL)
    await screen.findByText('(787) 555-0100')
    await user.click(screen.getByRole('button', { name: /Editar/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar consignatario' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'X')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La localización fue modificada por otro usuario; recargue e intente de nuevo.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Editar consignatario' })).toBeInTheDocument()
  })

  it('baja con confirmación y reactivación (nunca DELETE de la localización)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('(787) 555-0100')
    await user.click(screen.getByRole('switch', { name: 'Estado' }))
    const confirm = await screen.findByRole('dialog', { name: 'Dar de baja al consignatario' })
    expect(callsTo('POST', '/api/v1/locations/l-1/deactivate')).toHaveLength(0)
    await user.click(within(confirm).getByRole('button', { name: 'Dar de baja' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/l-1/deactivate')).toHaveLength(1))
    expect(callsTo('DELETE', '/api/v1/locations/l-1')).toHaveLength(0)
  })

  it('una ficha inactiva ofrece reactivar', async () => {
    const user = userEvent.setup()
    wrap(ALL, '/catalog/consignees?consignee=l-3')
    const toggle = await screen.findByRole('switch', { name: 'Estado' })
    expect(toggle).not.toBeChecked()
    await user.click(toggle)
    const confirm = await screen.findByRole('dialog', { name: 'Reactivar al consignatario' })
    await user.click(within(confirm).getByRole('button', { name: 'Reactivar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/l-3/reactivate')).toHaveLength(1))
  })
})

describe('Consignatarios — alta', () => {
  it('crea con tipo DELIVERY y país PR, sin dueño (compartido) y con teléfono; el nuevo queda elegido', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Consignatarios' })
    await user.click(screen.getByRole('button', { name: /Nuevo consignatario/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo consignatario' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Nuevo Cliente SA')
    await user.type(within(dialog).getByLabelText(/^Dirección\s*\*?$/), 'Calle 9')
    await user.click(within(dialog).getByRole('combobox', { name: /Pueblo/ }))
    await user.click(await within(dialog).findByRole('option', { name: /PONCE/ }))
    await user.type(within(dialog).getByLabelText('Teléfono'), '7875551234')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/locations')[0].body).toMatchObject({
      clientPublicId: null,
      code: null,
      name: 'Nuevo Cliente SA',
      locationType: 'DELIVERY',
      line1: 'Calle 9',
      city: 'Ponce',
      postalCode: '00716',
      country: 'PR',
      defaultServiceMinutes: 0,
      defaultWindowStart: null,
      defaultWindowEnd: null,
      allowDupInvoice: false,
    })
    await waitFor(() => expect(callsTo('POST', '/api/v1/contacts/LOCATION/110')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/contacts/LOCATION/110')[0].body).toEqual({ contactType: 'PHONE', value: '7875551234', isPrimary: true })
    await waitFor(() => expect(callsTo('GET', '/api/v1/locations/l-new').length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo consignatario' })).toBeNull())
  })

  it('validaciones con los mensajes del manual, sin llamar al servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Consignatarios' })
    await user.click(screen.getByRole('button', { name: /Nuevo consignatario/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo consignatario' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El nombre es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('La dirección (línea 1) es obligatoria.')).toBeInTheDocument()
    expect(within(dialog).getByText('La ciudad es obligatoria.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/locations')).toHaveLength(0)
  })

  it('la ventana horaria exige inicio y fin, y el fin posterior; los minutos no pueden ser negativos (la sección se abre sola)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Consignatarios' })
    await user.click(screen.getByRole('button', { name: /Nuevo consignatario/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo consignatario' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'X')
    await user.type(within(dialog).getByLabelText(/^Dirección\s*\*?$/), 'Y')
    await user.click(within(dialog).getByRole('combobox', { name: /Pueblo/ }))
    await user.click(await within(dialog).findByRole('option', { name: /PONCE/ }))
    // abre la sección para escribir la ventana
    await user.click(within(dialog).getByText('Opciones de entrega'))
    await user.type(within(dialog).getByLabelText('Ventana: desde'), '1000')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La ventana horaria requiere hora de inicio y hora de fin.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Ventana: hasta'), '0900')
    await user.clear(within(dialog).getByLabelText('Minutos de servicio'))
    await user.type(within(dialog).getByLabelText('Minutos de servicio'), '-3')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La hora de fin de la ventana debe ser posterior a la de inicio.')).toBeInTheDocument()
    expect(within(dialog).getByText('Los minutos de servicio no pueden ser negativos.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/locations')).toHaveLength(0)
  })

  it('con ventana válida manda HH:mm:00 y los minutos', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Consignatarios' })
    await user.click(screen.getByRole('button', { name: /Nuevo consignatario/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo consignatario' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'X')
    await user.type(within(dialog).getByLabelText(/^Dirección\s*\*?$/), 'Y')
    await user.click(within(dialog).getByRole('combobox', { name: /Pueblo/ }))
    await user.click(await within(dialog).findByRole('option', { name: /PONCE/ }))
    await user.click(within(dialog).getByText('Opciones de entrega'))
    await user.type(within(dialog).getByLabelText('Ventana: desde'), '0800')
    await user.type(within(dialog).getByLabelText('Ventana: hasta'), '1200')
    await user.clear(within(dialog).getByLabelText('Minutos de servicio'))
    await user.type(within(dialog).getByLabelText('Minutos de servicio'), '15')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/locations')[0].body).toMatchObject({ defaultWindowStart: '08:00:00', defaultWindowEnd: '12:00:00', defaultServiceMinutes: 15 })
  })

  it('400 del servidor con errores por campo: se ven bajo cada campo y el modal sigue abierto', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/locations'
        ? problem(400, 'Solicitud inválida', 'validation', { name: ['Máximo 200 caracteres.'], country: ['Catálogo Country \'XX\' no encontrado.'] })
        : undefined
    wrap(ALL)
    await screen.findByRole('list', { name: 'Consignatarios' })
    await user.click(screen.getByRole('button', { name: /Nuevo consignatario/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo consignatario' })
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'X')
    await user.type(within(dialog).getByLabelText(/^Dirección\s*\*?$/), 'Y')
    await user.click(within(dialog).getByRole('combobox', { name: /Pueblo/ }))
    await user.click(await within(dialog).findByRole('option', { name: /PONCE/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Máximo 200 caracteres.')).toBeInTheDocument()
    expect(within(dialog).getByText("Catálogo Country 'XX' no encontrado.")).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Nuevo consignatario' })).toBeInTheDocument()
  })

  it('el filtro de cliente preselecciona el dueño del alta', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Consignatarios' })
    await user.selectOptions(screen.getByLabelText('Cliente'), 'Acme Corp')
    await user.click(screen.getByRole('button', { name: /Nuevo consignatario/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo consignatario' })
    await waitFor(() => expect(within(dialog).getByLabelText('Cliente dueño')).toHaveValue('c-1'))
  })
})

describe('Consignatarios — permisos', () => {
  it('solo locations.read: sin Nuevo, Importar, Editar ni interruptores activos', async () => {
    wrap(['locations.read'])
    await screen.findByRole('heading', { name: 'Ficha del consignatario' })
    expect(screen.queryByRole('button', { name: /Nuevo consignatario/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Importar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Editar/ })).toBeNull()
    expect(screen.getByRole('switch', { name: 'Estado' })).toBeDisabled()
    expect(screen.getByRole('switch', { name: 'Número de factura repetido' })).toBeDisabled()
  })

  it('con locations.create ve Nuevo e Importar; sin contacts.manage el teléfono del modal es de solo lectura', async () => {
    const user = userEvent.setup()
    wrap(['locations.read', 'locations.create', 'locations.update'])
    await screen.findByText('(787) 555-0100')
    expect(screen.getByRole('button', { name: /Importar/ })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Editar/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar consignatario' })
    expect(within(dialog).getByLabelText('Teléfono')).toBeDisabled()
    expect(within(dialog).getByText(/Para editar el teléfono se necesitan/)).toBeInTheDocument()
  })
})
