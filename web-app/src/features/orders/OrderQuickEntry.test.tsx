// Lote F-C3 — Entrada rápida de órdenes: Enter (o «Guardar fila») crea UNA orden de 1 paquete y 1 pieza en Borrador, la fila guardada queda
// de solo lectura con su número y aparece una fila nueva con el servicio conservado; validaciones con los mensajes del servidor; errores
// del servidor por fila sin perder lo capturado; cliente que asigna el número de envío; Idempotency-Key por intento. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { tenantToday } from '../../kernel/api/tenantZone'
import { setLang } from '../../kernel/i18n/i18n'
import { shownDate, typeDate } from '../../kernel/ui/typeDate'
import { shiftDay } from '../../kernel/format/dateInput'
import { OrderQuickEntry } from './OrderQuickEntry'

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
    if (out instanceof Error) throw out
    return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const C1 = { id: 1, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', isActive: true, numberSettings: { clientAssignsInvoiceNumber: false, clientAssignsOrderNumber: false } }
const C3 = { id: 3, publicId: 'c-3', code: 'GAMA', name: 'Gama SRL', isActive: true, defaultServiceType: 'EXPRESS', defaultPackageType: 'ENV', numberSettings: { clientAssignsInvoiceNumber: true, clientAssignsOrderNumber: false } }
const C2 = { id: 2, publicId: 'c-2', code: 'BETA', name: 'Beta SA', isActive: true, numberSettings: { clientAssignsInvoiceNumber: false, clientAssignsOrderNumber: true } }
const BASE = { locationType: 'DELIVERY', country: 'PR', countryLabel: 'Puerto Rico', defaultServiceMinutes: 0, isActive: true, rowVersion: 'AAA=' }
const L1 = { ...BASE, id: 100, publicId: 'l-1', clientPublicId: 'c-1', isShared: false, code: 'C-001', name: 'Farmacia Central', line1: 'Calle Sol 1', city: 'San Juan', postalCode: '00901', allowDupInvoice: false }
const L2 = { ...BASE, id: 101, publicId: 'l-2', clientPublicId: null, isShared: true, code: null, name: 'Colmado Boricua', line1: 'Carr. 14 km 2', city: 'Ponce', postalCode: '00716', allowDupInvoice: true }
let seq = 0
let settings: Record<string, unknown> = { defaultServiceType: 'STANDARD', defaultPackageType: 'BOX' }

function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/orders' && method === 'POST') {
    seq += 1
    return { id: 50 + seq, publicId: `o-${seq}`, orderNumber: `ORD-0000${seq}`, clientInvoiceNumber: `FAC-0000${seq}`, status: 'DRAFT', quotedAmount: 999, codAmount: body?.codAmount ?? null }
  }
  if (p === '/api/v1/clients' && method === 'GET') return [C1, C2, C3]
  if (p === '/api/v1/clients/c-3') return C3
  if (p === '/api/v1/clients/c-1') return C1
  if (p === '/api/v1/clients/c-2') return C2
  if (p === '/api/v1/tenant/settings') return settings
  if (p === '/api/v1/catalogs/ServiceType') return [{ code: 'STANDARD', label: 'Estándar', isEnabled: true }, { code: 'EXPRESS', label: 'Express', isEnabled: true }]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Caja', isEnabled: true }, { code: 'ENV', label: 'Sobre', isEnabled: true }]
  if (p === '/api/v1/locations' && method === 'GET') return [L1, L2]
  if (p === '/api/v1/locations' && method === 'POST') return { ...BASE, id: 110, publicId: 'l-new', isShared: false, clientPublicId: body?.clientPublicId, code: null, name: body?.name, line1: body?.line1, city: body?.city, allowDupInvoice: false }
  if (p.startsWith('/api/v1/contacts/')) return []
  if (p === '/api/v1/locations/l-new') return { ...BASE, id: 110, publicId: 'l-new', isShared: false, clientPublicId: 'c-1', code: null, name: 'Tienda Nueva', line1: 'Calle Luna 3', city: 'San Juan', allowDupInvoice: false }
  if (p === '/api/v1/postal-localities') return [{ id: 1, city: 'SAN JUAN', municipality: 'San Juan', postalCode: '00901', state: 'PR', countryCode: 'PR', country: 'Puerto Rico' }]
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/custom-fields/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(permissions: string[] = ['orders.create', 'locations.read', 'clients.read'], props: Parameters<typeof OrderQuickEntry>[0] = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['LTL_GROUND', 'CATALOG']}>
          <OrderQuickEntry {...props} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

type User = ReturnType<typeof userEvent.setup>
const posts = () => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === '/api/v1/orders')

async function pickClient(user: User, text = 'Acme') {
  const box = screen.getByRole('combobox', { name: 'Cliente' })
  await user.click(box)
  await user.type(box, text)
  await user.click(await screen.findByRole('option', { name: new RegExp(text) }))
}
async function pickConsignee(user: User, text = 'Farmacia') {
  const box = screen.getByRole('combobox', { name: 'Consignatario' })
  await user.click(box)
  await user.click(await screen.findByRole('option', { name: new RegExp(text) }))
}
/** Cliente Acme + consignatario Farmacia Central (servicio y paquete salen de los predeterminados). */
async function fillValid(user: User) {
  await pickClient(user)
  await pickConsignee(user)
  await waitFor(() => expect(screen.getByLabelText('Paquete')).toHaveValue('BOX'))
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  seq = 0
  settings = { defaultServiceType: 'STANDARD', defaultPackageType: 'BOX' }
})

describe('Entrada rápida — rejilla', () => {
  it('arranca con servicio y paquete predeterminados, el N.º de envío «(automático)» y el consignatario esperando al cliente', async () => {
    wrap()
    await waitFor(() => expect(screen.getByLabelText('Servicio')).toHaveValue('STANDARD'))
    await waitFor(() => expect(screen.getByLabelText('Paquete')).toHaveValue('BOX'))
    const order = screen.getByLabelText('N.º de envío')
    expect(order).toBeDisabled()
    expect(order).toHaveAttribute('placeholder', '(automático)')
    expect(screen.getByRole('combobox', { name: 'Consignatario' })).toBeDisabled()
    expect(screen.getByTestId('qk-lines')).toHaveTextContent('0')
    expect(screen.getByTestId('qk-cod')).toHaveTextContent('$0.00')
    // no hay interruptor de label (F-G); la fecha es el primer campo y la factura sigue a la orden
    expect(screen.queryByText(/label/i)).toBeNull()
    expect(screen.getByRole('button', { name: 'Guardar fila' })).toBeEnabled()
  })

  it('Enter guarda UNA orden de 1 paquete y 1 pieza Sometida, con Idempotency-Key, y abre una fila nueva con el servicio conservado', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.selectOptions(screen.getByLabelText('Servicio'), 'EXPRESS')
    await user.type(screen.getByLabelText('COD ($)'), '50')
    await user.type(screen.getByLabelText('Comentario'), 'Llamar antes')
    await user.keyboard('{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(1))
    const call = posts()[0]
    expect(call.body).toMatchObject({
      clientPublicId: 'c-1',
      consigneeLocationPublicId: 'l-1',
      serviceType: 'EXPRESS',
      packages: [{ packageType: 'BOX', pieces: 1 }],
      codAmount: 50,
      notes: 'Llamar antes',
    })
    expect(call.body).not.toHaveProperty('confirmNow')
    expect(call.body).not.toHaveProperty('orderNumber')
    expect(call.body).not.toHaveProperty('overrideCredit')
    expect(call.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)

    // la fila guardada: su número, consignatario, dirección · pueblo, paquete, COD, servicio y comentario
    const saved = await screen.findByRole('link', { name: 'ORD-00001' })
    const row = saved.closest('tr')!
    expect(saved).toHaveAttribute('href', '/orders/o-1')
    for (const text of ['Farmacia Central', 'Calle Sol 1 · San Juan', 'Caja', '$50.00', 'Express', 'Llamar antes', 'Guardada']) expect(within(row).getByText(text)).toBeInTheDocument()
    expect(within(row).queryByRole('textbox')).toBeNull()
    // contadores
    expect(screen.getByTestId('qk-lines')).toHaveTextContent('1')
    expect(screen.getByTestId('qk-cod')).toHaveTextContent('$50.00')
    // fila nueva: cliente (en la barra) y servicio conservados, consignatario y COD vacíos
    expect(screen.getByRole('combobox', { name: 'Cliente' })).toHaveValue('ACME · Acme Corp')
    expect(screen.getByLabelText('Servicio')).toHaveValue('EXPRESS')
    expect(screen.getByLabelText('COD ($)')).toHaveValue('')
    expect(screen.getByRole('combobox', { name: 'Consignatario' })).toHaveValue('')
    // el cursor vuelve a la fecha, que es la de la última orden guardada (hoy)
    await waitFor(() => expect(screen.getByLabelText('Fecha')).toHaveFocus())
    expect(screen.getByLabelText('Fecha')).toHaveValue(shownDate(tenantToday()))
  })

  it('varias filas seguidas: cada Enter es otra orden con otra Idempotency-Key y los contadores suman', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.type(screen.getByLabelText('COD ($)'), '10.50')
    await user.keyboard('{Enter}')
    await screen.findByRole('link', { name: 'ORD-00001' })
    await pickConsignee(user, 'Colmado')
    await user.type(screen.getByLabelText('COD ($)'), '20')
    await user.click(screen.getByRole('button', { name: 'Guardar fila' }))
    await screen.findByRole('link', { name: 'ORD-00002' })
    expect(posts()).toHaveLength(2)
    expect(posts()[1].body).toMatchObject({ consigneeLocationPublicId: 'l-2', codAmount: 20 })
    expect(posts()[0].headers.get('Idempotency-Key')).not.toBe(posts()[1].headers.get('Idempotency-Key'))
    expect(screen.getByTestId('qk-lines')).toHaveTextContent('2')
    expect(screen.getByTestId('qk-cod')).toHaveTextContent('$30.50')
    expect(screen.getByText('2 de 2 entradas guardadas')).toBeInTheDocument()
  })

  it('la tabla de lo capturado: filtro de un día (hoy), búsqueda libre y resumen por paquete y servicio con el COD total en el pie', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.type(screen.getByLabelText('COD ($)'), '10.50')
    await user.keyboard('{Enter}')
    await screen.findByRole('link', { name: 'ORD-00001' })
    await pickConsignee(user, 'Colmado')
    await user.selectOptions(screen.getByLabelText('Servicio'), 'EXPRESS')
    await user.type(screen.getByLabelText('COD ($)'), '20')
    await user.keyboard('{Enter}')
    await screen.findByRole('link', { name: 'ORD-00002' })

    // el día por omisión es hoy y se ven las dos
    expect(screen.getByLabelText('Desde')).toHaveValue(shownDate(tenantToday()))
    expect(screen.getByLabelText('Hasta')).toHaveValue(shownDate(tenantToday()))   // por omisión desde = hasta = hoy
    expect(screen.getByText('2 de 2 entradas guardadas')).toBeInTheDocument()
    const summary = screen.getByTestId('qk-summary')
    const lines = within(summary).getAllByTestId('qk-summary-line').map((x) => x.textContent)
    expect(lines).toEqual(['1 × Caja · Estándar', '1 × Caja · Express'])
    expect(within(summary).getByTestId('qk-summary-cod')).toHaveTextContent('COD total: $30.50')
    expect(within(summary).getByTestId('qk-summary-packages')).toHaveTextContent('Total de paquetes: 2')
    expect(within(summary).getByTestId('qk-summary-invoices')).toHaveTextContent('Facturas distintas: 2')

    // la búsqueda libre filtra la tabla y el resumen
    await user.type(screen.getByRole('searchbox', { name: 'Buscar en las entradas guardadas' }), 'colmado')
    expect(screen.queryByRole('link', { name: 'ORD-00001' })).toBeNull()
    expect(screen.getByRole('link', { name: 'ORD-00002' })).toBeInTheDocument()
    expect(screen.getByText('1 de 2 entradas guardadas')).toBeInTheDocument()
    expect(within(screen.getByTestId('qk-summary')).getByTestId('qk-summary-cod')).toHaveTextContent('COD total: $20.00')

    // sin coincidencias, un aviso
    await user.type(screen.getByRole('searchbox', { name: 'Buscar en las entradas guardadas' }), ' zzz')
    expect(screen.getByText('Ninguna entrada guardada coincide con las fechas y la búsqueda.')).toBeInTheDocument()
  })

  it('una orden con una fecha más adelantada ensancha el rango: el desde se queda en hoy y el hasta sigue a esa fecha, y se ve', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    const tomorrow = shiftDay(tenantToday(), 1)
    await typeDate(user, screen.getByLabelText('Fecha'), tomorrow)
    await user.keyboard('{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(await screen.findByRole('link', { name: 'ORD-00001' })).toBeInTheDocument()
    expect(screen.getByText('1 de 1 entradas guardadas')).toBeInTheDocument()
    expect(screen.getByLabelText('Desde')).toHaveValue(shownDate(tenantToday()))
    expect(screen.getByLabelText('Hasta')).toHaveValue(shownDate(tomorrow))
    // acortar el rango a hoy esconde esa orden (el «hasta» se mueve a mano)
    await typeDate(user, screen.getByLabelText('Hasta'), tenantToday())
    await waitFor(() => expect(screen.queryByRole('link', { name: 'ORD-00001' })).toBeNull())
  })

  it('Enter para elegir un consignatario de la lista abierta no guarda la fila', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user)
    const box = screen.getByRole('combobox', { name: 'Consignatario' })
    await user.click(box)
    await user.type(box, 'Farmacia')
    await screen.findByRole('option', { name: /Farmacia Central/ })
    await user.keyboard('{Enter}')
    await waitFor(() => expect(box).toHaveValue('C-001 · Farmacia Central'))
    expect(posts()).toHaveLength(0)
  })

  it('el consignatario elegido muestra «dirección · pueblo» en solo lectura', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    expect(await screen.findByText('Calle Sol 1 · San Juan')).toBeInTheDocument()
  })
})

describe('Entrada rápida — validaciones (mensajes del servidor)', () => {
  it('sin cliente: «El cliente es obligatorio.» y no llama al API', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(screen.getByRole('button', { name: 'Guardar fila' }))
    expect(await screen.findByText('El cliente es obligatorio.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })

  it('sin consignatario: mensaje bajo el campo, foco en él y nada se guarda', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user)
    await user.click(screen.getByRole('button', { name: 'Guardar fila' }))
    expect(await screen.findByText('El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Consignatario' })).toHaveFocus())
    expect(posts()).toHaveLength(0)
  })

  it('sin tipo de paquete ni servicio (la compañía no tiene predeterminados) pide los dos', async () => {
    settings = {}
    const user = userEvent.setup()
    wrap()
    await pickClient(user)
    await pickConsignee(user)
    await user.click(screen.getByRole('button', { name: 'Guardar fila' }))
    expect(await screen.findByText('El tipo de paquete es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('El tipo de servicio es obligatorio.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
    // al elegirlos, el mensaje de ese campo desaparece
    await user.selectOptions(screen.getByLabelText('Paquete'), 'ENV')
    expect(screen.queryByText('El tipo de paquete es obligatorio.')).toBeNull()
  })

  it('COD que no es un número o es negativo', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.type(screen.getByLabelText('COD ($)'), 'abc')
    await user.keyboard('{Enter}')
    expect(await screen.findByText('El monto COD debe ser un número.')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('COD ($)'))
    await user.type(screen.getByLabelText('COD ($)'), '-5')
    await user.keyboard('{Enter}')
    expect(await screen.findByText('El monto COD no puede ser negativo.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })
})

describe('Entrada rápida — errores del servidor por fila', () => {
  it('cliente suspendido (422): el título sale bajo la fila, lo capturado se conserva y no se agrega ninguna fila guardada', async () => {
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders' ? problem(422, 'El cliente está suspendido; no se pueden crear ni confirmar órdenes.', 'status_rule') : undefined
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.type(screen.getByLabelText('COD ($)'), '75')
    await user.type(screen.getByLabelText('Comentario'), 'Frágil')
    await user.keyboard('{Enter}')
    expect(await screen.findByText('El cliente está suspendido; no se pueden crear ni confirmar órdenes.')).toBeInTheDocument()
    expect(screen.getByLabelText('COD ($)')).toHaveValue('75')
    expect(screen.getByLabelText('Comentario')).toHaveValue('Frágil')
    expect(screen.getByRole('combobox', { name: 'Consignatario' })).toHaveValue('C-001 · Farmacia Central')
    expect(screen.getByTestId('qk-lines')).toHaveTextContent('0')
    expect(screen.queryByRole('link', { name: /ORD-/ })).toBeNull()
    // el reintento usa OTRA clave (el servidor guardó el rechazo)
    await user.keyboard('{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(2))
    expect(posts()[0].headers.get('Idempotency-Key')).not.toBe(posts()[1].headers.get('Idempotency-Key'))
  })

  it('un error de campo del servidor (packages[0].packageType) queda bajo el campo de la fila', async () => {
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders'
        ? problem(400, 'Hay errores de validación.', 'validation', { 'packages[0].packageType': ['Tipo de paquete desconocido: BOX.'] })
        : undefined
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.keyboard('{Enter}')
    expect(await screen.findByText('Tipo de paquete desconocido: BOX.')).toBeInTheDocument()
    expect(screen.getByLabelText('Paquete')).toHaveAttribute('aria-invalid', 'true')
    await waitFor(() => expect(screen.getByLabelText('Paquete')).toHaveFocus())
  })

  it('una factura repetida (409) se muestra como error de la fila, sin diálogo ni datos internos', async () => {
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders'
        ? problem(409, "El consignatario 'Farmacia Central' no permite facturas repetidas: la orden ORD-00007 ya usa el número F-1.", 'duplicate_invoice', {
            clientInvoiceNumber: ["El consignatario 'Farmacia Central' no permite facturas repetidas: la orden ORD-00007 ya usa el número F-1."],
            existingOrderNumber: ['ORD-00007'],
            existingOrderPublicId: ['o-7'],
          })
        : undefined
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.keyboard('{Enter}')
    const alerts = await screen.findAllByRole('alert')
    expect(alerts.some((a) => /no permite facturas repetidas/.test(a.textContent ?? ''))).toBe(true)
    expect(screen.queryByText('ORD-00007', { selector: 'li' })).toBeNull()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('un corte de red se reintenta con la MISMA Idempotency-Key (no duplica)', async () => {
    let failing = true
    mock.override = ({ method, url }) => {
      if (method === 'POST' && url.pathname === '/api/v1/orders' && failing) return new TypeError('Failed to fetch')
      return undefined
    }
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await user.keyboard('{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(1))
    await screen.findByRole('alert')
    failing = false
    await user.keyboard('{Enter}')
    await screen.findByRole('link', { name: 'ORD-00001' })
    expect(posts()).toHaveLength(2)
    expect(posts()[0].headers.get('Idempotency-Key')).toBe(posts()[1].headers.get('Idempotency-Key'))
  })
})

describe('Entrada rápida — cliente que asigna el número de envío', () => {
  it('habilita el N.º de envío y lo manda; con un cliente que no lo asigna no lo manda', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user, 'Beta')
    await waitFor(() => expect(screen.getByLabelText('N.º de envío')).toBeEnabled())
    await pickConsignee(user, 'Colmado')
    await waitFor(() => expect(screen.getByLabelText('Paquete')).toHaveValue('BOX'))
    await user.type(screen.getByLabelText('N.º de envío'), 'PO-778')
    await user.keyboard('{Enter}')
    await screen.findByRole('link', { name: 'ORD-00001' })
    expect(posts()[0].body).toMatchObject({ clientPublicId: 'c-2', orderNumber: 'PO-778' })
    // N.º de envío vacío = lo asigna Teikem: no se manda
    await pickConsignee(user, 'Colmado')
    await user.keyboard('{Enter}')
    await screen.findByRole('link', { name: 'ORD-00002' })
    expect(posts()[1].body).not.toHaveProperty('orderNumber')
    // cambiar a un cliente que no asigna: el campo vuelve a «(automático)» y se limpia
    await pickClient(user, 'Acme')
    await waitFor(() => expect(screen.getByLabelText('N.º de envío')).toBeDisabled())
    expect(screen.getByLabelText('N.º de envío')).toHaveValue('')
  })

  it('un número de envío de más de 40 caracteres no se acepta', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user, 'Beta')
    await waitFor(() => expect(screen.getByLabelText('N.º de envío')).toBeEnabled())
    await pickConsignee(user, 'Colmado')
    // el input corta a 40; el esquema lo repite por si llega por pegado programático
    expect(screen.getByLabelText('N.º de envío')).toHaveAttribute('maxlength', '40')
  })
})

describe('Entrada rápida — consignatario nuevo con «+»', () => {
  it('el botón «+» solo existe con locations.create y Enter dentro de su modal no guarda la orden', async () => {
    const user = userEvent.setup()
    const first = wrap()
    await pickClient(user)
    expect(screen.queryByRole('button', { name: 'Nuevo consignatario' })).toBeNull()
    first.unmount()
    wrap(['orders.create', 'locations.read', 'locations.create', 'clients.read'])
    await pickClient(user)
    await user.click(screen.getByRole('button', { name: 'Nuevo consignatario' }))
    const dialog = await screen.findByRole('dialog')
    const name = within(dialog).getByLabelText(/^Nombre/)
    await user.type(name, 'Tienda Nueva{Enter}')
    expect(posts()).toHaveLength(0)
  })
})

describe('Entrada rápida — consignatario nuevo en línea (Lote 213)', () => {
  const CREATE_PERMS = ['orders.create', 'locations.read', 'locations.create', 'contacts.manage', 'locations.update', 'clients.read']
  const active = () => document.activeElement as HTMLElement | null
  const locationPosts = () => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === '/api/v1/locations')

  async function typeNewName(user: User, name = 'Tienda Nueva') {
    await pickClient(user)
    const box = screen.getByRole('combobox', { name: 'Consignatario' })
    await user.click(box)
    await user.type(box, name)
    expect(await screen.findByRole('option', { name: /Nuevo consignatario/ })).toHaveClass('on')
    await user.keyboard('{Enter}')
    return await screen.findByTestId('consignee-inline')
  }

  it('escribir un nombre nuevo + Enter abre el formulario en línea con el foco en Dirección, sin mover la fila', async () => {
    const user = userEvent.setup()
    wrap(CREATE_PERMS)
    const panel = await typeNewName(user)
    expect(within(panel).getByLabelText(/^Nombre/)).toHaveValue('Tienda Nueva')
    expect(active()).toBe(within(panel).getByLabelText(/^Dirección/))
    // el formulario es una fila extra: la fila activa sigue siendo la misma
    expect(panel.closest('tr')).toHaveClass('qk-newcons')
    expect(screen.getByLabelText('Paquete')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })

  it('solo con teclado: faltante lleva el foco al primer faltante; completo crea, elige, foco en Paquete y Enter guarda la orden', async () => {
    const user = userEvent.setup()
    wrap(CREATE_PERMS)
    const panel = await typeNewName(user)
    // sin dirección ni pueblo: Enter lleva el foco al primer faltante y muestra el mensaje del esquema
    await user.keyboard('{Enter}')
    expect(await within(panel).findByText('La dirección (línea 1) es obligatoria.')).toBeInTheDocument()
    expect(active()).toBe(within(panel).getByLabelText(/^Dirección/))
    expect(locationPosts()).toHaveLength(0)
    await user.keyboard('Calle Luna 3{Enter}')
    expect(await within(panel).findByText('La ciudad es obligatoria.')).toBeInTheDocument()
    expect(active()).toBe(within(panel).getByRole('combobox'))
    await user.keyboard('san')
    await screen.findByRole('option', { name: /san juan/i })
    await user.keyboard('{Enter}')
    expect(locationPosts()).toHaveLength(0)
    await user.keyboard('{Enter}')
    await waitFor(() => expect(locationPosts()).toHaveLength(1))
    expect(locationPosts()[0].body).toMatchObject({ name: 'Tienda Nueva', line1: 'Calle Luna 3', city: 'San Juan', postalCode: '00901', country: 'PR', locationType: 'DELIVERY', clientPublicId: 'c-1' })
    await waitFor(() => expect(screen.queryByTestId('consignee-inline')).toBeNull())
    await waitFor(() => expect(active()).toBe(screen.getByLabelText('Paquete')))
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Consignatario' })).toHaveValue('Tienda Nueva'))
    // Enter en otro campo guarda la orden como hasta hoy, con el consignatario recién creado
    await user.keyboard('{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ consigneeLocationPublicId: 'l-new' })
  })

  it('el teléfono (con permisos) se guarda aparte como ContactPoint del consignatario', async () => {
    const user = userEvent.setup()
    wrap(CREATE_PERMS)
    const panel = await typeNewName(user)
    await user.keyboard('Calle Luna 3')
    await user.tab()
    await user.keyboard('san')
    await screen.findByRole('option', { name: /san juan/i })
    await user.keyboard('{Enter}')
    // elegido el pueblo, el foco pasa solo al teléfono
    expect(active()).toBe(within(panel).getByLabelText('Teléfono'))
    await user.keyboard('7875550142{Enter}')
    const isPhone = (c: Call) => c.method === 'POST' && c.url.pathname === '/api/v1/contacts/LOCATION/110'
    await waitFor(() => expect(mock.calls.some(isPhone)).toBe(true))
    expect(mock.calls.find(isPhone)!.body).toMatchObject({ contactType: 'PHONE', value: '7875550142', isPrimary: true })
  })

  it('Tab desde el último campo continúa al siguiente de la fila (Paquete) sin atrapar el foco ni grabar', async () => {
    const user = userEvent.setup()
    wrap(CREATE_PERMS)
    const panel = await typeNewName(user)
    await user.tab()
    await user.tab()
    expect(active()).toBe(within(panel).getByLabelText('Teléfono'))
    await user.tab()
    expect(active()).toBe(screen.getByLabelText('Paquete'))
    expect(locationPosts()).toHaveLength(0)
  })

  it('sin contacts.manage no hay campo de teléfono', async () => {
    const user = userEvent.setup()
    wrap(['orders.create', 'locations.read', 'locations.create', 'clients.read'])
    const panel = await typeNewName(user)
    expect(within(panel).queryByLabelText('Teléfono')).toBeNull()
  })

  it('Escape cancela y devuelve el foco al combo con el texto que había; no se crea nada', async () => {
    const user = userEvent.setup()
    wrap(CREATE_PERMS)
    await typeNewName(user, 'Zeta Nueva')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByTestId('consignee-inline')).toBeNull())
    const box = screen.getByRole('combobox', { name: 'Consignatario' })
    await waitFor(() => expect(active()).toBe(box))
    expect(box).toHaveValue('Zeta Nueva')
    expect(locationPosts()).toHaveLength(0)
  })

  it('un error del servidor sale en el mismo formulario y conserva lo escrito', async () => {
    const user = userEvent.setup()
    mock.override = (c) => (c.method === 'POST' && c.url.pathname === '/api/v1/locations' ? problem(422, 'No se pudo crear el consignatario.', 'validation', { Line1: ['La dirección es demasiado corta.'] }) : null)
    wrap(CREATE_PERMS)
    const panel = await typeNewName(user)
    await user.keyboard('Calle 1')
    await user.tab()
    await user.keyboard('san')
    await screen.findByRole('option', { name: /san juan/i })
    await user.keyboard('{Enter}{Enter}')
    expect(await within(panel).findByText('La dirección es demasiado corta.')).toBeInTheDocument()
    expect(within(panel).getByLabelText(/^Dirección/)).toHaveValue('Calle 1')
    expect(within(panel).getByLabelText(/^Nombre/)).toHaveValue('Tienda Nueva')
    expect(screen.getByTestId('consignee-inline')).toBeInTheDocument()
  })

  it('sin locations.create no se ofrece crear en línea', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user)
    const box = screen.getByRole('combobox', { name: 'Consignatario' })
    await user.click(box)
    await user.type(box, 'Tienda Nueva')
    expect(screen.queryByRole('option', { name: /Nuevo consignatario/ })).toBeNull()
  })
})

describe('Entrada rápida — «Label al guardar» (Lote F-G2)', () => {
  it('el interruptor está en la leyenda y cada fila guardada se entrega a quien imprime', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    const onChange = vi.fn()
    wrap(undefined, { onSaved, labelOnSave: { value: true, onChange } })
    const sw = screen.getByRole('switch', { name: 'Label al guardar' })
    expect(sw).toBeChecked()
    await user.click(sw)
    expect(onChange).toHaveBeenCalledWith(false)
    await fillValid(user)
    await user.keyboard('{Enter}')
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(onSaved.mock.calls[0][0]).toMatchObject({ orderNumber: expect.any(String) })
  })
})

describe('Entrada rápida — fecha, factura, valores del cliente y filas con otra fecha (dictado de Luis, 2026-10-21)', () => {
  it('la fecha es el primer campo y arranca en hoy; al elegir el cliente el cursor va a la fecha y su calendario se abre solo', async () => {
    const user = userEvent.setup()
    wrap()
    const heads = within(document.querySelector('table.qg thead')!).getAllByRole('columnheader').map((h) => h.textContent?.trim())
    expect(heads.slice(0, 3)).toEqual(['Fecha', 'N.º de envío', 'N.º de factura'])
    const date = screen.getByLabelText('Fecha')
    expect(date).toHaveValue(shownDate(tenantToday()))
    await pickClient(user)
    await waitFor(() => expect(date).toHaveFocus())
    expect(await screen.findByRole('group', { name: 'Calendario' })).toBeInTheDocument()
  })

  it('el N.º de factura está apagado («automático») si Teikem la genera; si el cliente la asigna, se escribe y viaja', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user, 'Acme')
    const invoice = screen.getByLabelText('N.º de factura')
    expect(invoice).toBeDisabled()
    expect(invoice).toHaveAttribute('placeholder', '(automático)')
    await pickClient(user, 'Gama')
    await waitFor(() => expect(screen.getByLabelText('N.º de factura')).toBeEnabled())
    await pickConsignee(user, 'Farmacia')
    await user.type(screen.getByLabelText('N.º de factura'), 'FAC-77')
    await user.keyboard('{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ clientInvoiceNumber: 'FAC-77' })
  })

  it('el servicio y el paquete salen del cliente (y no de la compañía) y siguen siéndolos en la fila nueva', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user, 'Gama')
    await waitFor(() => expect(screen.getByLabelText('Servicio')).toHaveValue('EXPRESS'))
    expect(screen.getByLabelText('Paquete')).toHaveValue('ENV')
    await pickConsignee(user, 'Farmacia')
    await user.keyboard('{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ serviceType: 'EXPRESS', packages: [{ packageType: 'ENV', pieces: 1 }] })
    await waitFor(() => expect(screen.getByLabelText('Paquete')).toHaveValue('ENV'))
    // otro cliente sin valores propios vuelve a los de la compañía
    await pickClient(user, 'Acme')
    await waitFor(() => expect(screen.getByLabelText('Servicio')).toHaveValue('STANDARD'))
    expect(screen.getByLabelText('Paquete')).toHaveValue('BOX')
  })

  it('una fecha anterior a hoy se avisa y se BLOQUEA: no se manda nada y el cursor queda en la fecha', async () => {
    const user = userEvent.setup()
    wrap()
    await fillValid(user)
    await typeDate(user, screen.getByLabelText('Fecha'), shiftDay(tenantToday(), -1))
    await user.keyboard('{Enter}')
    expect(await screen.findByText('La fecha del envío no puede ser anterior a hoy.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
    expect(screen.getByLabelText('Fecha')).toHaveFocus()
  })

  it('con la fecha de mañana: la orden viaja con esa fecha, la fila nueva sale con mañana y las dos filas se sombrean con su aviso', async () => {
    const user = userEvent.setup()
    wrap()
    const tomorrow = shiftDay(tenantToday(), 1)
    await fillValid(user)
    expect(document.querySelector('tr.qk-active')).not.toHaveClass('qk-offdate')
    await typeDate(user, screen.getByLabelText('Fecha'), tomorrow)
    expect(document.querySelector('tr.qk-active')).toHaveClass('qk-offdate')   // la fila que se está llenando ya se ve distinta
    await user.keyboard('{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ requestedDate: expect.stringContaining(tomorrow.slice(0, 10)) })
    await waitFor(() => expect(screen.getByLabelText('Fecha')).toHaveValue(shownDate(tomorrow)))   // la fila nueva: la fecha de la última orden
    expect(document.querySelectorAll('tr.qk-offdate').length).toBe(2)                                 // el rango se ensanchó a mañana: salen la nueva y la guardada
    expect(screen.getByText('Sombreado: fecha distinta de hoy')).toBeInTheDocument()
    // volver a hoy quita el sombreado de la fila activa
    await typeDate(user, screen.getByLabelText('Fecha'), tenantToday())
    expect(document.querySelector('tr.qk-active')).not.toHaveClass('qk-offdate')
  })
})
