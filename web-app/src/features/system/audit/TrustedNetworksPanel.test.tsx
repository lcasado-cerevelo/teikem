// IP preferida (2026-10-12): panel de la lista de IPs preferidas de la compañía sobre un fetch simulado — lista con estado, «Usted está aquí» y avisos,
// alta con ayuda de CIDR (vista previa, ajuste, avisos), confirmación obligatoria de «solo red interna», errores del servidor junto al campo, activar /
// desactivar / eliminar, máximo de entradas, «Probar una IP» y que sin admin.tenant no se pinte.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../../kernel/access'
import { resetFormatSettings } from '../../../kernel/format'
import { setLang } from '../../../kernel/i18n/i18n'
import { TrustedNetworksPanel } from './TrustedNetworksPanel'

type Item = {
  publicId: string
  name: string
  cidr: string
  isActive: boolean
  isExpired: boolean
  expiresAtUtc: string | null
  note: string | null
  addressCount: string
  containsCurrentIp: boolean
  internalOnlyConfirmedAtUtc: string | null
  internalOnlyConfirmedByName: string | null
  createdAtUtc: string
}

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; body: Record<string, unknown> | null }[],
  items: [] as unknown[],
  max: null as number | null,
  featureEnabled: true,
  currentIp: '203.0.113.25' as string | null,
  nextError: null as Response | null,
}))

const item = (over: Partial<Item>): Item => ({
  publicId: 'a0000000-0000-0000-0000-000000000001',
  name: 'Almacén Bayamón',
  cidr: '203.0.113.0/24',
  isActive: true,
  isExpired: false,
  expiresAtUtc: null,
  note: null,
  addressCount: '256',
  containsCurrentIp: true,
  internalOnlyConfirmedAtUtc: '2026-10-12T15:00:00',
  internalOnlyConfirmedByName: 'Luis Casado',
  createdAtUtc: '2026-10-12T15:00:00',
  ...over,
})

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const json = (b: unknown, status = 200) => new Response(JSON.stringify(b), { status, headers: { 'Content-Type': 'application/json' } })
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    let body: Record<string, unknown> | null = null
    try {
      body = req.method === 'GET' || req.method === 'DELETE' ? null : ((await req.clone().json()) as Record<string, unknown>)
    } catch {
      body = null
    }
    mock.requests.push({ method: req.method, path: url.pathname, body })
    if (!url.pathname.startsWith('/api/v1/tenant/trusted-networks')) return new Response(null, { status: 404 })
    const state = () => {
      const items = mock.items as Item[]
      const covering = items.find((i) => i.containsCurrentIp && i.isActive && !i.isExpired)
      return { items, maxEntries: mock.max, featureEnabled: mock.featureEnabled, currentIp: mock.currentIp, currentIpNetworkName: covering?.name ?? null }
    }
    if (url.pathname === '/api/v1/tenant/trusted-networks' && req.method === 'GET') return json(state())
    if (mock.nextError && req.method !== 'GET') {
      const e = mock.nextError
      mock.nextError = null
      return e
    }
    if (url.pathname === '/api/v1/tenant/trusted-networks' && req.method === 'POST') {
      const created = item({ publicId: `b000000${mock.items.length}-0000-0000-0000-000000000000`, name: String(body?.name), cidr: String(body?.cidr), containsCurrentIp: false, addressCount: '1' })
      mock.items = [...mock.items, created]
      return json(created)
    }
    if (url.pathname === '/api/v1/tenant/trusted-networks/limit' && req.method === 'PUT') {
      mock.max = (body?.maxEntries as number | null) ?? null
      return json(state())
    }
    const m = /\/api\/v1\/tenant\/trusted-networks\/([^/]+)$/.exec(url.pathname)
    if (m && req.method === 'PUT') {
      mock.items = (mock.items as Item[]).map((i) => (i.publicId === m[1] ? { ...i, ...(body?.isActive !== undefined && body?.isActive !== null ? { isActive: body.isActive as boolean } : {}), ...(body?.name ? { name: String(body.name) } : {}) } : i))
      return json((mock.items as Item[]).find((i) => i.publicId === m[1]))
    }
    if (m && req.method === 'DELETE') {
      mock.items = (mock.items as Item[]).filter((i) => i.publicId !== m[1])
      return new Response(null, { status: 204 })
    }
    return new Response(null, { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, body: Record<string, unknown>) => new Response(JSON.stringify({ status, ...body }), { status, headers: { 'Content-Type': 'application/problem+json' } })

function wrap(ui: ReactNode, permissions = ['admin.tenant']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={[]}>
        {ui}
      </AccessProvider>
    </QueryClientProvider>,
  )
}

const writes = () => mock.requests.filter((r) => r.method !== 'GET')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.items = [item({}), item({ publicId: 'a0000000-0000-0000-0000-000000000002', name: 'Oficina vieja', cidr: '198.51.100.8/29', addressCount: '8', isActive: false, containsCurrentIp: false })]
  mock.max = null
  mock.featureEnabled = true
  mock.currentIp = '203.0.113.25'
  mock.nextError = null
  resetFormatSettings()
})
afterEach(() => resetFormatSettings())

describe('TrustedNetworksPanel · lista', () => {
  it('muestra las entradas con su estado, «Usted está aquí», cuántas direcciones cubre y la IP actual cubierta', async () => {
    wrap(<TrustedNetworksPanel />)
    const table = await screen.findByRole('table', { name: 'IPs preferidas (red de confianza)' })
    const rows = within(table).getAllByRole('row')
    expect(within(rows[1]).getByText('Almacén Bayamón')).toBeInTheDocument()
    expect(within(rows[1]).getByText('203.0.113.0/24')).toBeInTheDocument()
    expect(within(rows[1]).getByText('256')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Usted está aquí')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Activa')).toBeInTheDocument()
    expect(within(rows[2]).getByText('Inactiva')).toBeInTheDocument()
    expect(screen.getByText(/Su IP actual es/)).toHaveTextContent('203.0.113.25')
    expect(screen.getByText(/Su IP actual es/)).toHaveTextContent('la cubre «Almacén Bayamón»')
    // cubierta: no ofrece «Agregar esta IP»
    expect(screen.queryByRole('button', { name: 'Agregar esta IP' })).not.toBeInTheDocument()
    expect(screen.getByRole('note')).toHaveTextContent('Cargue solo la red interna')
  })

  it('con el interruptor del servidor apagado lo dice; sin admin.tenant no se pinta ni consulta', async () => {
    mock.featureEnabled = false
    const { unmount } = wrap(<TrustedNetworksPanel />)
    expect(await screen.findByText(/Esta función está apagada en el servidor/)).toBeInTheDocument()
    unmount()
    mock.requests = []
    wrap(<TrustedNetworksPanel />, ['inventory.view'])
    expect(screen.queryByText('IPs preferidas (red de confianza)')).not.toBeInTheDocument()
    expect(mock.requests).toEqual([])
  })

  it('vacía: dice que falta y «Agregar esta IP» usa la IP actual (con el rango, abre el diálogo con ella)', async () => {
    mock.items = []
    const user = userEvent.setup()
    wrap(<TrustedNetworksPanel />)
    expect(await screen.findByText('Todavía no hay IPs preferidas.')).toBeInTheDocument()
    expect(screen.getByText(/no está en la lista/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Agregar esta IP' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar IP preferida' })
    expect(within(dialog).getByLabelText('IP')).toHaveValue('203.0.113.25')
    expect(within(dialog).getByRole('status')).toHaveTextContent('Se guardará como 203.0.113.25/32 (una sola dirección).')
  })
})

describe('TrustedNetworksPanel · alta', () => {
  it('una sola IP: exige el nombre y confirmar «solo red interna»; manda cidr normalizado y confirmInternalOnly', async () => {
    mock.items = []
    const user = userEvent.setup()
    wrap(<TrustedNetworksPanel />)
    await user.click(await screen.findByRole('button', { name: 'Agregar IP o rango' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar IP preferida' })
    await user.type(within(dialog).getByLabelText('IP'), '198.51.100.20')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Escriba un nombre para la red.')).toBeInTheDocument()
    expect(within(dialog).getByText('Confirme que es solo la red interna de la empresa.')).toBeInTheDocument()
    expect(writes()).toEqual([])

    await user.type(within(dialog).getByLabelText('Nombre de la red'), 'Taller')
    await user.click(within(dialog).getByRole('checkbox'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0]).toMatchObject({ method: 'POST', path: '/api/v1/tenant/trusted-networks' })
    expect(writes()[0].body).toMatchObject({ name: 'Taller', cidr: '198.51.100.20/32', confirmInternalOnly: true })
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Taller')).toBeInTheDocument()
  })

  it('rango: vista previa en vivo, ajuste a la red, avisos de privada y de que no incluye su IP, y un error del servidor junto al campo', async () => {
    mock.items = []
    const user = userEvent.setup()
    wrap(<TrustedNetworksPanel />)
    await user.click(await screen.findByRole('button', { name: 'Agregar IP o rango' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar IP preferida' })
    await user.click(within(dialog).getByRole('radio', { name: 'Un rango de IPs' }))
    await user.type(within(dialog).getByLabelText('IP de la red'), '192.168.10.5')
    // por omisión /24
    expect(within(dialog).getByRole('status')).toHaveTextContent('Se guardará como 192.168.10.0/24 — 256 direcciones, de 192.168.10.0 a 192.168.10.255.')
    expect(within(dialog).getByText(/Se ajustó a la dirección de la red: 192.168.10.0\/24/)).toBeInTheDocument()
    expect(within(dialog).getByText(/Es una IP privada/)).toBeInTheDocument()
    expect(within(dialog).getByText(/No incluye su IP actual \(203.0.113.25\)/)).toBeInTheDocument()
    // un atajo cambia el tamaño
    await user.click(within(dialog).getByRole('button', { name: '/29' }))
    expect(within(dialog).getByRole('status')).toHaveTextContent('192.168.10.0/29 — 8 direcciones')
    // pegar el rango completo en el campo pone el prefijo
    await user.clear(within(dialog).getByLabelText('IP de la red'))
    await user.type(within(dialog).getByLabelText('IP de la red'), '198.51.100.0/28')
    expect(within(dialog).getByLabelText('Tamaño')).toHaveValue('28')
    expect(within(dialog).getByRole('status')).toHaveTextContent('198.51.100.0/28 — 16 direcciones')

    await user.type(within(dialog).getByLabelText('Nombre de la red'), 'Bodega')
    await user.click(within(dialog).getByRole('checkbox'))
    mock.nextError = problem(409, { title: 'Esa IP o rango ya está en la lista.', code: 'conflict' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Esa IP o rango ya está en la lista.')
    expect(screen.getByRole('dialog')).toBeInTheDocument() // no se cierra
  })

  it('errores de IP en pantalla antes de guardar (demasiado ancho, formas sueltas) y un 400 del servidor en su campo', async () => {
    mock.items = []
    const user = userEvent.setup()
    wrap(<TrustedNetworksPanel />)
    await user.click(await screen.findByRole('button', { name: 'Agregar IP o rango' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar IP preferida' })
    await user.type(within(dialog).getByLabelText('IP'), '127.1')
    expect(await within(dialog).findByText(/No es una IP válida/)).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('IP'))
    await user.type(within(dialog).getByLabelText('IP'), '10.0.0.0/8')
    // escribir una barra pasa solo a «Un rango de IPs», conservando lo escrito
    expect(within(dialog).getByRole('radio', { name: 'Un rango de IPs' })).toHaveAttribute('aria-checked', 'true')
    expect(await within(dialog).findByText(/El rango es demasiado ancho/)).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('IP de la red'))
    await user.click(within(dialog).getByRole('radio', { name: 'Una sola IP (recomendado)' }))
    await user.type(within(dialog).getByLabelText('IP'), '203.0.113.99')
    await user.type(within(dialog).getByLabelText('Nombre de la red'), 'X')
    await user.click(within(dialog).getByRole('checkbox'))
    mock.nextError = problem(400, { title: 'Datos inválidos.', code: 'validation', errors: { note: ['La nota no puede exceder 300 caracteres.'] } })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La nota no puede exceder 300 caracteres.')).toBeInTheDocument()
  })
})

describe('TrustedNetworksPanel · cambios', () => {
  it('cambiar solo el nombre no pide confirmar de nuevo ni manda la IP; cambiar la IP sí lo pide', async () => {
    const user = userEvent.setup()
    wrap(<TrustedNetworksPanel />)
    const table = await screen.findByRole('table', { name: 'IPs preferidas (red de confianza)' })
    await user.click(within(within(table).getAllByRole('row')[1]).getByRole('button', { name: 'Cambiar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cambiar IP preferida' })
    expect(within(dialog).queryByRole('checkbox')).not.toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('Nombre de la red'))
    await user.type(within(dialog).getByLabelText('Nombre de la red'), 'Almacén principal')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].method).toBe('PUT')
    expect(writes()[0].body).toMatchObject({ name: 'Almacén principal', cidr: null, confirmInternalOnly: null })
    expect(writes()[0].body).not.toHaveProperty('clearExpiry')

    await user.click(within(within(await screen.findByRole('table', { name: 'IPs preferidas (red de confianza)' })).getAllByRole('row')[1]).getByRole('button', { name: 'Cambiar' }))
    const again = await screen.findByRole('dialog', { name: 'Cambiar IP preferida' })
    await user.clear(within(again).getByLabelText('IP de la red'))
    await user.type(within(again).getByLabelText('IP de la red'), '203.0.114.0')
    expect(within(again).getByRole('checkbox')).toBeInTheDocument()
  })

  it('desactivar y activar mandan isActive; eliminar pide confirmación', async () => {
    const user = userEvent.setup()
    wrap(<TrustedNetworksPanel />)
    const table = await screen.findByRole('table', { name: 'IPs preferidas (red de confianza)' })
    await user.click(within(within(table).getAllByRole('row')[1]).getByRole('button', { name: 'Desactivar' }))
    await waitFor(() => expect(writes().some((w) => w.method === 'PUT' && w.body?.isActive === false)).toBe(true))
    await user.click(within(within(table).getAllByRole('row')[2]).getByRole('button', { name: 'Activar' }))
    await waitFor(() => expect(writes().some((w) => w.method === 'PUT' && w.body?.isActive === true)).toBe(true))

    await user.click(within(within(table).getAllByRole('row')[2]).getByRole('button', { name: 'Eliminar' }))
    const confirm = await screen.findByRole('dialog', { name: 'Eliminar IP preferida' })
    expect(within(confirm).getByText(/volverá a pedirse el código/)).toBeInTheDocument()
    await user.click(within(confirm).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(writes().some((w) => w.method === 'DELETE')).toBe(true))
  })
})

describe('TrustedNetworksPanel · máximo y prueba de IP', () => {
  it('el máximo es de la compañía: guardar un número, vaciarlo = sin límite, y muestra cuánto se usa', async () => {
    const user = userEvent.setup()
    wrap(<TrustedNetworksPanel />)
    const input = await screen.findByLabelText('Máximo de entradas')
    expect(input).toHaveValue(null)
    expect(screen.getByText('2 entradas, sin límite.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Guardar máximo' })).toBeDisabled()
    await user.type(input, '5')
    await user.click(screen.getByRole('button', { name: 'Guardar máximo' }))
    await waitFor(() => expect(writes().some((w) => w.path.endsWith('/limit') && w.body?.maxEntries === 5)).toBe(true))
    expect(await screen.findByText('Usa 2 de 5.')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Máximo de entradas'))
    await user.click(screen.getByRole('button', { name: 'Guardar máximo' }))
    await waitFor(() => expect(writes().filter((w) => w.path.endsWith('/limit')).at(-1)?.body?.maxEntries).toBeNull())
  })

  it('«Probar una IP»: cubierta (solo activas y vigentes), no cubierta e inválida', async () => {
    const user = userEvent.setup()
    wrap(<TrustedNetworksPanel />)
    const input = await screen.findByLabelText('Probar una IP')
    await user.type(input, '203.0.113.77{enter}')
    expect(await screen.findByText('Sí: la cubre «Almacén Bayamón».')).toBeInTheDocument()
    await user.clear(input)
    await user.type(input, '198.51.100.9') // está en «Oficina vieja», que está inactiva
    await user.click(screen.getByRole('button', { name: 'Probar' }))
    expect(await screen.findByText('No está cubierta por ninguna entrada activa y vigente.')).toBeInTheDocument()
    await user.clear(input)
    await user.type(input, '127.1')
    await user.click(screen.getByRole('button', { name: 'Probar' }))
    expect(await screen.findByText('No es una IP válida.')).toBeInTheDocument()
  })
})
