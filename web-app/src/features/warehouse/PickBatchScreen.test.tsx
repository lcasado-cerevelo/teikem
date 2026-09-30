// Lote 13 — pantalla Recolección y empaque (`PickBatchListScreen`) sobre un fetch simulado: dos paneles (Recolección y
// Recolecciones) con warehouse.pick, varias líneas en un solo POST (fila vacía automática, pista FEFO, errores del servidor
// en su fila), filtro "No. de orden" que sugiere números existentes, Empacar y Eliminar desde la fila, ficha en un modal,
// sin warehouse.pick solo la lista, y la pestaña Reabasto sin cambios.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import PickBatchListScreen from './PickBatchListScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], postResponse: null as null | { status: number; body: unknown }, created: false }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.clone().text()
    const body = text ? JSON.parse(text) : null
    mock.calls.push({ method: req.method, url, body })
    const [status, out] = route(req.method, url)
    return new Response(JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'
const P2 = 'aaaaaaaa-0000-0000-0000-000000000002'
const B1 = 'bbbbbbbb-0000-0000-0000-000000000001'
const B2 = 'bbbbbbbb-0000-0000-0000-000000000002'
const NEW = 'bbbbbbbb-0000-0000-0000-000000000009'

const PRODUCTS = [
  { id: 1, publicId: P1, sku: 'A-1', name: 'Tornillo', isOwn: true, trackingTypeCode: 'NONE', qtyAvailable: 8, isActive: true },
  { id: 2, publicId: P2, sku: 'B-2', name: 'Tuerca', isOwn: true, trackingTypeCode: 'NONE', qtyAvailable: 3, isActive: true },
]
const batch = (over: Record<string, unknown>) => ({
  id: 1,
  publicId: B1,
  number: 'PB-00001',
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  statusCode: 'COLLECTED',
  status: 'Recolectada',
  collectedAtUtc: '2026-09-30T14:00:00',
  canPack: true,
  canDelete: true,
  totalQty: 3,
  lines: [
    { id: 1, productPublicId: P1, sku: 'A-1', productName: 'Tornillo', quantity: 2, binId: 10, binCode: 'A-01' },
    { id: 2, productPublicId: P2, sku: 'B-2', productName: 'Tuerca', quantity: 1, binId: 10, binCode: 'A-01' },
  ],
  isActive: true,
  rowVersion: 'AA==',
  ...over,
})
const BATCHES = [
  batch({}),
  batch({
    id: 2,
    publicId: B2,
    number: 'PB-00002',
    statusCode: 'PACKED',
    status: 'Empacada',
    canPack: false,
    canDelete: true,
    orderNumber: 'OR-0001',
    clientInvoiceNumber: 'FAC-1',
    displayNumbers: 'OR-0001 · FAC-1',
    clientName: 'Cliente Uno',
  }),
]

function route(method: string, url: URL): [number, unknown] {
  const p = url.pathname
  if (p === '/api/v1/pick-batches' && method === 'POST') {
    if (mock.postResponse) return [mock.postResponse.status, mock.postResponse.body]
    mock.created = true
    return [200, batch({ id: 9, publicId: NEW, number: 'PB-00009' })]
  }
  if (p === '/api/v1/pick-batches') {
    const order = url.searchParams.get('orderNumber')
    const all = mock.created ? [batch({ id: 9, publicId: NEW, number: 'PB-00009' }), ...BATCHES] : BATCHES
    const items = order ? all.filter((b) => String((b as Record<string, unknown>).orderNumber ?? '').toLowerCase().includes(order.toLowerCase())) : all
    return [200, { total: items.length, skip: 0, take: 25, items }]
  }
  if (p.startsWith('/api/v1/pick-batches/')) return [200, BATCHES.find((b) => p.endsWith(b.publicId)) ?? BATCHES[0]]
  if (p === '/api/v1/warehouses') return [200, [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]]
  if (p === `/api/v1/warehouses/${WH}`) return [200, { id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) return [200, { total: 0, skip: 0, take: 50, items: [] }]
  if (p === '/api/v1/products') return [200, { total: PRODUCTS.length, skip: 0, take: 20, items: PRODUCTS }]
  if (p.startsWith('/api/v1/products/')) return [200, { product: PRODUCTS.find((x) => p.endsWith(x.publicId)) }]
  if (p === '/api/v1/inventory/balances') {
    const pid = url.searchParams.get('productPublicIds')
    const items = pid === P1 ? [{ binId: 10, binCode: 'A-01', zoneTypeCode: 'PICKING', productPublicId: P1, qtyAvailable: 8, lotId: null, expiryDate: null }] : []
    return [200, { total: items.length, skip: 0, take: 200, items }]
  }
  if (p === '/api/v1/warehouse-tasks')
    return [200, { total: 1, skip: 0, take: 25, items: [{ id: 2, typeCode: 'REPLENISH', type: 'Reabasto', statusCode: 'PENDING', status: 'Pendiente', warehousePublicId: WH, warehouseCode: 'ALM-01' }] }]
  if (p === '/api/v1/status/PickBatchStatus')
    return [
      200,
      [
        { code: 'COLLECTED', label: 'Recolectada', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 },
        { code: 'PACKED', label: 'Empacada', stageKind: 'PIPELINE', isEnabled: true, sortOrder: 2 },
      ],
    ]
  if (p === '/api/v1/tenant/settings') return [200, { id: 1, name: 'Advance', defaultServiceType: null, defaultPackageType: null }]
  return [200, []]
}

function wrap(permissions: string[], path = '/warehouse/pick-batches') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/pick-batches" element={<PickBatchListScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const posts = () => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === '/api/v1/pick-batches')
const listCalls = () => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === '/api/v1/pick-batches')

async function pickProduct(user: ReturnType<typeof userEvent.setup>, n: number, name: RegExp) {
  await user.click(screen.getByRole('combobox', { name: `Producto de la línea ${n}` }))
  await user.click(await screen.findByRole('option', { name }))
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.postResponse = null
  mock.created = false
  window.localStorage.clear()
})

describe('Recolección y empaque: dos paneles', () => {
  it('con warehouse.pick: panel "Recolección" a la izquierda, barra arrastrable y "Recolecciones" a la derecha', async () => {
    wrap(['inventory.view', 'warehouse.pick'])
    expect(await screen.findByRole('heading', { name: 'Recolección' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Recolecciones' })).toBeInTheDocument()
    expect(screen.getByRole('separator')).toHaveAttribute('aria-valuenow', '60')
    // ya no hay botón "Recolectar" en la cabecera ni modal de alta: el botón vive en el panel
    expect(screen.getByRole('button', { name: 'Recolectar (bajar de inventario)' })).toBeInTheDocument()
    expect(await screen.findByText('PB-00001')).toBeInTheDocument()
    // almacén único: ya elegido
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Almacén/ })).toHaveValue('ALM-01 · Almacén principal'))
  })

  it('sin warehouse.pick no hay panel izquierdo ni barra: la lista ocupa todo el ancho, sin Empacar ni Eliminar', async () => {
    wrap(['inventory.view', 'orders.create', 'orders.cancel'])
    expect(await screen.findByText('PB-00001')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Recolecciones' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Recolección' })).toBeNull()
    expect(screen.queryByRole('separator')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Empacar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Eliminar' })).toBeNull()
  })
})

describe('Panel "Recolección"', () => {
  it('varias líneas en un solo POST: fila vacía automática, pista de disponible y FEFO, la nueva se resalta sin navegar', async () => {
    const user = userEvent.setup()
    const { container } = wrap(['inventory.view', 'warehouse.pick'])
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Almacén/ })).toHaveValue('ALM-01 · Almacén principal'))

    await pickProduct(user, 1, /A-1 · Tornillo/)
    // al elegir producto en la última fila aparece otra vacía; la cantidad arranca en 1
    expect(await screen.findByRole('combobox', { name: 'Producto de la línea 2' })).toBeInTheDocument()
    expect(screen.getByLabelText('Cantidad de la línea 1')).toHaveValue(1)
    expect(await screen.findByText('8 disp.')).toBeInTheDocument()
    expect(await screen.findByText('FEFO: A-01')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Cantidad de la línea 1'))
    await user.type(screen.getByLabelText('Cantidad de la línea 1'), '3')

    await pickProduct(user, 2, /B-2 · Tuerca/)
    expect(await screen.findByRole('combobox', { name: 'Producto de la línea 3' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Recolectar (bajar de inventario)' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toEqual({
      warehousePublicId: WH,
      lines: [
        { productPublicId: P1, quantity: 3, binId: null, lotId: null, serialNumbers: null },
        { productPublicId: P2, quantity: 1, binId: null, lotId: null, serialNumbers: null },
      ],
    })
    // líneas limpias (se queda el almacén) y la nueva resaltada en la lista
    await waitFor(() => expect(screen.queryByRole('combobox', { name: 'Producto de la línea 2' })).toBeNull())
    expect(screen.getByRole('combobox', { name: /^Almacén/ })).toHaveValue('ALM-01 · Almacén principal')
    const created = await screen.findByText('PB-00009')
    expect(created.closest('tr')).toHaveClass('collect-new')
    expect(container.querySelector('[role="dialog"]')).toBeNull()
  })

  it('sin líneas no llama al API; los errores del servidor vuelven a su fila', async () => {
    const user = userEvent.setup()
    wrap(['inventory.view', 'warehouse.pick'])
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Almacén/ })).toHaveValue('ALM-01 · Almacén principal'))
    await user.click(screen.getByRole('button', { name: 'Recolectar (bajar de inventario)' }))
    expect(await screen.findByText('Indique al menos una línea a recolectar.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)

    mock.postResponse = { status: 409, body: { title: 'No hay existencia suficiente.', code: 'insufficient_stock', errors: { 'lines[0]': ['No hay existencia suficiente de A-1 en ALM-01.'] } } }
    await pickProduct(user, 1, /A-1 · Tornillo/)
    await user.click(screen.getByRole('button', { name: 'Recolectar (bajar de inventario)' }))
    const msg = await screen.findByText('No hay existencia suficiente de A-1 en ALM-01.')
    expect(screen.getByLabelText('Cantidad de la línea 1')).toHaveAttribute('aria-invalid', 'true')
    expect(msg).toHaveAttribute('role', 'alert')
    // la línea sigue ahí para corregirla
    expect(screen.getByRole('combobox', { name: 'Producto de la línea 1' })).toHaveValue('A-1 · Tornillo')
  })
})

describe('Panel "Recolecciones"', () => {
  it('"No. de orden" sugiere números existentes y al elegir uno filtra la lista por ese número', async () => {
    const user = userEvent.setup()
    wrap(['inventory.view'])
    await screen.findByText('PB-00001')
    await user.type(screen.getByRole('combobox', { name: 'No. de orden' }), 'or-')
    await user.click(await screen.findByRole('option', { name: 'OR-0001' }))
    expect(screen.getByRole('combobox', { name: 'No. de orden' })).toHaveValue('OR-0001')
    expect(listCalls().some((c) => c.url.searchParams.get('orderNumber') === 'or-' && c.url.searchParams.get('take') === '20')).toBe(true)
    await waitFor(() => expect(listCalls().at(-1)?.url.searchParams.get('orderNumber')).toBe('OR-0001'))
    expect(listCalls().at(-1)?.url.searchParams.get('skip')).toBe('0')
    await waitFor(() => expect(screen.queryByText('PB-00001')).toBeNull())
    expect(screen.getByText('PB-00002')).toBeInTheDocument()
  })

  it('tabla de 5 columnas con productos "SKU ×cant"; Empacar desde la fila abre el empaque de esa recolección', async () => {
    const user = userEvent.setup()
    wrap(['inventory.view', 'warehouse.pick', 'orders.create'])
    await screen.findByText('PB-00001')
    const table = screen.getByRole('table', { name: 'Recolecciones' })
    const headers = within(table).getAllByRole('columnheader').map((h) => h.textContent?.trim())
    expect(headers.slice(0, 5)).toEqual(['Número', 'Productos', 'Orden y factura', 'Cliente', 'Recolectada'])
    expect(within(table).getAllByText('A-1 ×2, B-2 ×1').length).toBeGreaterThan(0)
    // Empacar solo en la que se puede empacar (la empacada no lo ofrece)
    const packs = within(table).getAllByRole('button', { name: 'Empacar' })
    expect(packs).toHaveLength(1)
    await user.click(packs[0])
    expect(await screen.findByRole('dialog', { name: 'Empacar PB-00001' })).toBeInTheDocument()
  })

  it('Eliminar: la empacada exige orders.cancel; el diálogo avisa que se borra la orden', async () => {
    const user = userEvent.setup()
    const first = wrap(['inventory.view', 'warehouse.pick'])
    await screen.findByText('PB-00002')
    expect(screen.getAllByRole('button', { name: 'Eliminar' })).toHaveLength(1)
    first.unmount()

    wrap(['inventory.view', 'warehouse.pick', 'orders.cancel'])
    await screen.findByText('PB-00002')
    const dels = screen.getAllByRole('button', { name: 'Eliminar' })
    expect(dels).toHaveLength(2)
    await user.click(dels[1])
    const dialog = await screen.findByRole('dialog', { name: 'Eliminar recolección' })
    expect(dialog).toHaveTextContent('Se borra también su orden OR-0001')
  })

  it('clic en la fila abre la ficha en un modal con sus líneas y acciones', async () => {
    const user = userEvent.setup()
    wrap(['inventory.view', 'warehouse.pick', 'orders.create'])
    await user.click(await screen.findByText('PB-00001'))
    const dialog = await screen.findByRole('dialog', { name: /PB-00001/ })
    expect(await within(dialog).findByRole('heading', { name: /Líneas/ })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Empacar' })).toBeInTheDocument()
    // la X de la cabecera y el botón del pie se llaman igual: el del pie es el último
    await user.click(within(dialog).getAllByRole('button', { name: 'Cerrar' }).at(-1) as HTMLElement)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })
})

describe('Pestaña Reabasto (sin cambios)', () => {
  it("pide solo tareas REPLENISH y ofrece 'Correr reabasto' con warehouse.pick", async () => {
    const user = userEvent.setup()
    wrap(['inventory.view', 'warehouse.pick'])
    await user.click(await screen.findByRole('tab', { name: 'Reabasto' }))
    const tasks = () => mock.calls.filter((c) => c.url.pathname === '/api/v1/warehouse-tasks')
    await waitFor(() => expect(tasks().length).toBeGreaterThan(0))
    expect(tasks().every((c) => c.url.searchParams.getAll('types').join() === 'REPLENISH')).toBe(true)
    expect(screen.getByRole('button', { name: 'Correr reabasto' })).toBeInTheDocument()
    expect(screen.queryByRole('separator')).toBeNull()
  })
})
