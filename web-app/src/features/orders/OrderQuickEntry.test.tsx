// Lote F-C3 — Entrada rápida de órdenes: Enter (o «Guardar fila») crea UNA orden de 1 paquete y 1 pieza en Borrador, la fila guardada queda
// de solo lectura con su número y aparece una fila nueva con el servicio conservado; validaciones con los mensajes del servidor; errores
// del servidor por fila sin perder lo capturado; cliente que asigna el número de orden; Idempotency-Key por intento. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
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
    return { id: 50 + seq, publicId: `o-${seq}`, orderNumber: `ORD-0000${seq}`, status: 'DRAFT', quotedAmount: 999, codAmount: body?.codAmount ?? null }
  }
  if (p === '/api/v1/clients' && method === 'GET') return [C1, C2]
  if (p === '/api/v1/clients/c-1') return C1
  if (p === '/api/v1/clients/c-2') return C2
  if (p === '/api/v1/tenant/settings') return settings
  if (p === '/api/v1/catalogs/ServiceType') return [{ code: 'STANDARD', label: 'Estándar', isEnabled: true }, { code: 'EXPRESS', label: 'Express', isEnabled: true }]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Caja', isEnabled: true }, { code: 'ENV', label: 'Sobre', isEnabled: true }]
  if (p === '/api/v1/locations' && method === 'GET') return [L1, L2]
  if (p === '/api/v1/locations' && method === 'POST') return { ...BASE, id: 110, publicId: 'l-new', isShared: false, clientPublicId: body?.clientPublicId, code: null, name: body?.name, line1: body?.line1, city: body?.city, allowDupInvoice: false }
  if (p.startsWith('/api/v1/contacts/')) return []
  if (p === '/api/v1/postal-localities') return []
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
  it('arranca con servicio y paquete predeterminados, el N.º de orden «(automático)» y el consignatario esperando al cliente', async () => {
    wrap()
    await waitFor(() => expect(screen.getByLabelText('Servicio')).toHaveValue('STANDARD'))
    await waitFor(() => expect(screen.getByLabelText('Paquete')).toHaveValue('BOX'))
    const order = screen.getByLabelText('N.º de orden')
    expect(order).toBeDisabled()
    expect(order).toHaveAttribute('placeholder', '(automático)')
    expect(screen.getByRole('combobox', { name: 'Consignatario' })).toBeDisabled()
    expect(screen.getByTestId('qk-lines')).toHaveTextContent('0')
    expect(screen.getByTestId('qk-cod')).toHaveTextContent('$0.00')
    // no hay interruptor de label (F-G) ni columna de factura
    expect(screen.queryByText(/label/i)).toBeNull()
    expect(screen.getByRole('button', { name: 'Guardar fila' })).toBeEnabled()
  })

  it('Enter guarda UNA orden de 1 paquete y 1 pieza en Borrador, con Idempotency-Key, y abre una fila nueva con el servicio conservado', async () => {
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
      confirmNow: false,
    })
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
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Consignatario' })).toHaveFocus())
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
    expect(screen.getByText('2 entradas guardadas')).toBeInTheDocument()
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
    const alert = await screen.findByRole('alert', { name: '' })
    expect(alert).toHaveTextContent('no permite facturas repetidas')
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

describe('Entrada rápida — cliente que asigna el número de orden', () => {
  it('habilita el N.º de orden y lo manda; con un cliente que no lo asigna no lo manda', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user, 'Beta')
    await waitFor(() => expect(screen.getByLabelText('N.º de orden')).toBeEnabled())
    await pickConsignee(user, 'Colmado')
    await waitFor(() => expect(screen.getByLabelText('Paquete')).toHaveValue('BOX'))
    await user.type(screen.getByLabelText('N.º de orden'), 'PO-778')
    await user.keyboard('{Enter}')
    await screen.findByRole('link', { name: 'ORD-00001' })
    expect(posts()[0].body).toMatchObject({ clientPublicId: 'c-2', orderNumber: 'PO-778' })
    // N.º de orden vacío = lo asigna Teikem: no se manda
    await pickConsignee(user, 'Colmado')
    await user.keyboard('{Enter}')
    await screen.findByRole('link', { name: 'ORD-00002' })
    expect(posts()[1].body).not.toHaveProperty('orderNumber')
    // cambiar a un cliente que no asigna: el campo vuelve a «(automático)» y se limpia
    await pickClient(user, 'Acme')
    await waitFor(() => expect(screen.getByLabelText('N.º de orden')).toBeDisabled())
    expect(screen.getByLabelText('N.º de orden')).toHaveValue('')
  })

  it('un número de orden de más de 40 caracteres no se acepta', async () => {
    const user = userEvent.setup()
    wrap()
    await pickClient(user, 'Beta')
    await waitFor(() => expect(screen.getByLabelText('N.º de orden')).toBeEnabled())
    await pickConsignee(user, 'Colmado')
    // el input corta a 40; el esquema lo repite por si llega por pegado programático
    expect(screen.getByLabelText('N.º de orden')).toHaveAttribute('maxlength', '40')
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
