// Despacho manual (2026-10-11) dentro de «Recolección y empaque» (`PickBatchListScreen`) sobre un fetch simulado: la lista trae
// TODO (sin kind por omisión) con columna Tipo y estatus «Despachado», filtro Tipo (kind), el modo «Despacho manual (sin entrega)»
// del panel Recolección (solo con warehouse.issue; motivo obligatorio, un POST a /manual-issues con Idempotency-Key, 409 en su
// fila), la ficha de un manual (motivo, nota, sin Empacar) y Eliminar por DELETE /manual-issues con warehouse.issue.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import PickBatchDetailScreen from './PickBatchDetailScreen'
import PickBatchListScreen from './PickBatchListScreen'

interface Call {
  method: string
  url: URL
  body: unknown
  headers: Headers
}
const mock = vi.hoisted(() => ({ reasons: [] as unknown[], calls: [] as Call[], postResponse: null as null | { status: number; body: unknown }, deleteResponse: null as null | { status: number; body: unknown } }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.clone().text()
    const body = text ? JSON.parse(text) : null
    mock.calls.push({ method: req.method, url, body, headers: req.headers })
    const [status, out] = route(req.method, url)
    return new Response(status === 204 ? null : JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'
const D1 = 'dddddddd-0000-0000-0000-000000000001'
const D2 = 'dddddddd-0000-0000-0000-000000000002'
const NEW = 'dddddddd-0000-0000-0000-000000000009'

const PRODUCTS = [{ id: 1, publicId: P1, sku: 'A-1', name: 'Tornillo', isOwn: true, trackingTypeCode: 'NONE', qtyAvailable: 8, isActive: true }]
const issue = (over: Record<string, unknown>) => ({
  id: 1002,
  publicId: D1,
  number: 'DMA-00001',
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  statusCode: 'COLLECTED',
  status: 'Recolectada',
  collectedBy: 'Administrador Advance',
  collectedAtUtc: '2026-10-11T14:00:00',
  canPack: false,
  canDelete: true,
  totalQty: 2,
  totalCost: 4,
  lines: [{ id: 1, productPublicId: P1, sku: 'A-1', productName: 'Tornillo', quantity: 2, binId: 10, binCode: 'A-01', unitCost: 2, issueTxnId: 10003 }],
  isActive: true,
  rowVersion: 'AA==',
  isManual: true,
  reasonCode: 'SAMPLE',
  reasonLabel: 'Muestra',
  note: 'Feria de salud',
  ownerClientName: null,
  ...over,
})
const PACK = issue({ id: 1, publicId: 'bbbbbbbb-0000-0000-0000-000000000001', number: 'PB-00001', isManual: false, reasonCode: null, reasonLabel: null, note: null, canPack: true, status: 'Recolectada' })
const ISSUES = [
  issue({}),
  issue({ id: 1003, publicId: D2, number: 'DMA-00002', reasonCode: 'SALE', reasonLabel: 'Venta', note: null, statusCode: 'CANCELLED', status: 'Cancelada', isActive: false, canDelete: false }),
]
const ALL = [...ISSUES, PACK]
const REASONS = [
  { id: 1, code: 'SAMPLE', label: 'Muestra', isEnabled: true, sortOrder: 1 },
  { id: 2, code: 'SALE', label: 'Venta', isEnabled: true, sortOrder: 4 },
]
const BALANCES = [{ binId: 10, binCode: 'A-01', zoneCode: 'A', zoneTypeCode: 'PICKING', productPublicId: P1, qtyAvailable: 8, lotId: null, expiryDate: null }]

function route(method: string, url: URL): [number, unknown] {
  const p = url.pathname
  if (p === '/api/v1/manual-issues' && method === 'POST') return mock.postResponse ? [mock.postResponse.status, mock.postResponse.body] : [200, issue({ id: 9, publicId: NEW, number: 'DMA-00009' })]
  if (p === '/api/v1/manual-issues/reasons') return [200, mock.reasons]
  if (p.startsWith('/api/v1/manual-issues/') && method === 'DELETE') return mock.deleteResponse ? [mock.deleteResponse.status, mock.deleteResponse.body] : [204, null]
  if (p === '/api/v1/catalogs/ManualIssueReason') return [200, REASONS]
  if (p === '/api/v1/pick-batches') {
    const kind = url.searchParams.get('kind')
    const items = kind === 'MANUAL' ? ISSUES : kind === 'PACK' ? [PACK] : ALL
    return [200, { total: items.length, skip: 0, take: 25, items }]
  }
  if (p.startsWith('/api/v1/pick-batches/')) return [200, ALL.find((b) => p.endsWith(b.publicId)) ?? ISSUES[0]]
  if (p === '/api/v1/warehouses') return [200, [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]]
  if (p === `/api/v1/warehouses/${WH}`) return [200, { id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) return [200, { total: 0, skip: 0, take: 50, items: [] }]
  if (p === '/api/v1/products') return [200, { total: PRODUCTS.length, skip: 0, take: 20, items: PRODUCTS }]
  if (p.startsWith('/api/v1/products/')) return [200, { product: PRODUCTS[0] }]
  if (p === '/api/v1/inventory/balances') return [200, { total: 1, skip: 0, take: 200, items: BALANCES }]
  if (p === '/api/v1/warehouse-tasks') return [200, { total: 0, skip: 0, take: 25, items: [] }]
  if (p.startsWith('/api/v1/status/')) return [200, []]
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
            <Route path="/warehouse/pick-batches/:publicId" element={<PickBatchDetailScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const posts = () => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === '/api/v1/manual-issues')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.reasons = REASONS
  mock.postResponse = null
  mock.deleteResponse = null
  window.localStorage.clear()
})

const listGets = () => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === '/api/v1/pick-batches')

async function openManualMode(user: ReturnType<typeof userEvent.setup>) {
  await waitFor(() => expect(screen.getByRole('combobox', { name: /^Almacén/ })).toHaveValue('ALM-01 · Almacén principal'))
  // con warehouse.pick + warehouse.issue hay interruptor; con solo warehouse.issue el panel ya es de despacho manual
  const mode = screen.queryByRole('switch', { name: 'Despacho manual (sin entrega)' })
  if (mode) await user.click(mode)
  await user.click(screen.getByRole('combobox', { name: 'Producto de la línea 1' }))
  await user.click(await screen.findByRole('option', { name: /A-1 · Tornillo/ }))
}

describe('Lista de Recolección y empaque con despachos manuales', () => {
  it('trae todo (sin kind), con columna Tipo, el motivo del manual y estatus «Despachado» / «Cancelado»', async () => {
    wrap(['inventory.view'])
    expect(await screen.findByText('DMA-00001')).toBeInTheDocument()
    expect(listGets()[0].url.searchParams.get('kind')).toBeNull()
    const table = screen.getByRole('table', { name: 'Recolecciones' })
    expect(within(table).getByText('Despacho manual · Muestra')).toBeInTheDocument()
    expect(within(table).getAllByText('Empaque').length).toBeGreaterThan(0)
    expect(within(table).getByText('Despachado')).toBeInTheDocument()
    expect(within(table).getByText('Cancelado')).toBeInTheDocument()
    // un manual no se empaca: Empacar solo en la de empaque
    expect(screen.queryByRole('button', { name: 'Eliminar' })).toBeNull()
  })

  it('el filtro Tipo manda kind=MANUAL y deja solo los despachos manuales', async () => {
    const user = userEvent.setup()
    wrap(['inventory.view'])
    await screen.findByText('PB-00001')
    await user.selectOptions(screen.getByLabelText('Tipo'), 'MANUAL')
    await waitFor(() => expect(listGets().some((c) => c.url.searchParams.get('kind') === 'MANUAL')).toBe(true))
    await waitFor(() => expect(screen.queryByText('PB-00001')).toBeNull())
    expect(screen.getByText('DMA-00001')).toBeInTheDocument()
  })
})

describe('Modo «Despacho manual (sin entrega)» del panel Recolección', () => {
  it('sin warehouse.issue no aparece el modo', async () => {
    wrap(['inventory.view', 'warehouse.pick'])
    await screen.findByRole('button', { name: 'Recolectar (bajar de inventario)' })
    expect(screen.queryByRole('switch', { name: 'Despacho manual (sin entrega)' })).toBeNull()
  })

  it('motivo obligatorio y un solo POST a /manual-issues con Idempotency-Key, motivo y nota', async () => {
    const user = userEvent.setup()
    wrap(['inventory.view', 'warehouse.pick', 'warehouse.issue'])
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Almacén/ })).toHaveValue('ALM-01 · Almacén principal'))
    // apagado: «Recolectar» como siempre y sin motivo ni nota
    expect(screen.getByRole('button', { name: 'Recolectar (bajar de inventario)' })).toBeInTheDocument()
    expect(screen.queryByLabelText(/^Motivo/)).toBeNull()
    await openManualMode(user)
    expect(screen.queryByRole('button', { name: 'Recolectar (bajar de inventario)' })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Despachar (bajar de inventario)' }))
    expect(await screen.findByText('Indique el motivo del despacho manual.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)

    await user.selectOptions(screen.getByLabelText(/^Motivo/), 'SAMPLE')
    // la nota es opcional y está colapsada detrás de «Agregar nota»
    expect(screen.queryByLabelText('Nota')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Agregar nota' }))
    await user.type(screen.getByLabelText('Nota'), 'Feria de salud')
    await user.click(screen.getByRole('button', { name: 'Despachar (bajar de inventario)' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toEqual({
      warehousePublicId: WH,
      lines: [{ productPublicId: P1, quantity: 1, binId: null, lotId: null, serialNumbers: null }],
      reasonCode: 'SAMPLE',
      note: 'Feria de salud',
    })
    expect(posts()[0].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/)
    expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname === '/api/v1/pick-batches')).toBe(false)
  })

  it('el motivo llega preseleccionado con el default de la compañía (isDefault), sin nota, y se despacha sin tocarlo', async () => {
    const user = userEvent.setup()
    mock.reasons = REASONS.map((r) => ({ ...r, isDefault: r.code === 'SALE' }))
    wrap(['inventory.view', 'warehouse.issue'])
    await openManualMode(user)
    await waitFor(() => expect(screen.getByLabelText(/^Motivo/)).toHaveValue('SALE'))
    await user.click(screen.getByRole('button', { name: 'Despachar (bajar de inventario)' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ reasonCode: 'SALE', note: null })
  })

  it('el último motivo usado (si todavía existe) gana al default, y se recuerda al despachar', async () => {
    const user = userEvent.setup()
    mock.reasons = REASONS.map((r) => ({ ...r, isDefault: r.code === 'SALE' }))
    window.localStorage.setItem('teikem.manualIssue.lastReason.0.0', 'SAMPLE')
    wrap(['inventory.view', 'warehouse.issue'])
    await openManualMode(user)
    await waitFor(() => expect(screen.getByLabelText(/^Motivo/)).toHaveValue('SAMPLE'))
    await user.selectOptions(screen.getByLabelText(/^Motivo/), 'SALE')
    await user.click(screen.getByRole('button', { name: 'Despachar (bajar de inventario)' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(window.localStorage.getItem('teikem.manualIssue.lastReason.0.0')).toBe('SALE')
  })

  it('un último motivo que ya no existe se ignora: llega el default; sin default, el motivo queda vacío', async () => {
    const user = userEvent.setup()
    mock.reasons = REASONS.map((r) => ({ ...r, isDefault: r.code === 'SALE' }))
    window.localStorage.setItem('teikem.manualIssue.lastReason.0.0', 'GONE')
    const first = wrap(['inventory.view', 'warehouse.issue'])
    await openManualMode(user)
    await waitFor(() => expect(screen.getByLabelText(/^Motivo/)).toHaveValue('SALE'))
    first.unmount()
    mock.reasons = REASONS
    wrap(['inventory.view', 'warehouse.issue'])
    await openManualMode(user)
    expect(screen.getByLabelText(/^Motivo/)).toHaveValue('')
  })

  it('el almacenamiento roto no impide despachar (try/catch)', async () => {
    const user = userEvent.setup()
    mock.reasons = REASONS.map((r) => ({ ...r, isDefault: r.code === 'SALE' }))
    const spy = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denegado')
    })
    try {
      wrap(['inventory.view', 'warehouse.issue'])
      await openManualMode(user)
      await waitFor(() => expect(screen.getByLabelText(/^Motivo/)).toHaveValue('SALE'))
    } finally {
      spy.mockRestore()
    }
  })

  it('el 409 de inventario insuficiente vuelve a la cantidad de su fila con el mensaje exacto del servidor', async () => {
    const user = userEvent.setup()
    mock.postResponse = { status: 409, body: { title: 'No hay existencia suficiente.', code: 'insufficient_stock', errors: { 'lines[0]': ['No hay existencia suficiente de A-1 en ALM-01.'] } } }
    wrap(['inventory.view', 'warehouse.issue'])
    await openManualMode(user)
    await user.selectOptions(screen.getByLabelText(/^Motivo/), 'SALE')
    await user.click(screen.getByRole('button', { name: 'Despachar (bajar de inventario)' }))
    expect(await screen.findByText('No hay existencia suficiente de A-1 en ALM-01.')).toBeInTheDocument()
    expect(screen.getByLabelText('Cantidad de la línea 1')).toHaveAttribute('aria-invalid', 'true')
  })
})

describe('Ficha de un despacho manual', () => {
  it('clic en la fila: motivo, nota, líneas con costo y enlace al Kárdex; sin Empacar', async () => {
    const user = userEvent.setup()
    wrap(['inventory.view', 'warehouse.issue', 'orders.create', 'warehouse.pick'])
    await user.click(await screen.findByText('DMA-00001'))
    const dialog = await screen.findByRole('dialog', { name: /DMA-00001/ })
    expect(await within(dialog).findByTestId('mi-reason')).toHaveTextContent('Muestra')
    expect(within(dialog).getByTestId('mi-note')).toHaveTextContent('Feria de salud')
    expect(within(dialog).getByRole('link', { name: 'Ver los movimientos' })).toHaveAttribute('href', '/warehouse/kardex?refEntity=PICK_BATCH&refId=1002')
    expect(within(dialog).queryByRole('button', { name: 'Empacar' })).toBeNull()
    expect(within(dialog).getByText('Despachado')).toBeInTheDocument()
  })

  it('Eliminar confirma y llama a DELETE /manual-issues/{id} (solo con warehouse.issue)', async () => {
    const user = userEvent.setup()
    wrap(['inventory.view', 'warehouse.issue'], `/warehouse/pick-batches/${D1}`)
    expect(await screen.findByRole('heading', { name: /DMA-00001/ })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Eliminar' }))
    const confirm = await screen.findByRole('dialog', { name: 'Eliminar despacho manual' })
    expect(confirm).toHaveTextContent('El inventario vuelve a su posición original')
    await user.click(within(confirm).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'DELETE' && c.url.pathname === `/api/v1/manual-issues/${D1}`)).toBe(true))
    expect(mock.calls.find((c) => c.method === 'DELETE')?.body).toEqual({ rowVersion: 'AA==' })
  })

  it('con warehouse.pick pero sin warehouse.issue no se puede eliminar un manual', async () => {
    wrap(['inventory.view', 'warehouse.pick'], `/warehouse/pick-batches/${D1}`)
    expect(await screen.findByRole('heading', { name: /DMA-00001/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Eliminar' })).toBeNull()
  })

  it('un error del servidor al eliminar se muestra con su mensaje exacto', async () => {
    const user = userEvent.setup()
    mock.deleteResponse = { status: 422, body: { title: 'El producto A-1 está dado de baja: no se puede eliminar el despacho.', code: 'status_rule' } }
    wrap(['inventory.view', 'warehouse.issue'], `/warehouse/pick-batches/${D1}`)
    await user.click(await screen.findByRole('button', { name: 'Eliminar' }))
    const confirm = await screen.findByRole('dialog', { name: 'Eliminar despacho manual' })
    await user.click(within(confirm).getByRole('button', { name: 'Eliminar' }))
    expect(await screen.findByText('El producto A-1 está dado de baja: no se puede eliminar el despacho.')).toBeInTheDocument()
  })
})
