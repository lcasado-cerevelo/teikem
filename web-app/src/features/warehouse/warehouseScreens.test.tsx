// Pruebas de pantalla del almacén (Lote F6) sobre un fetch simulado: guardas de permiso por fila y consultas de otros
// módulos que no deben dispararse (ni sacar al usuario de la pantalla).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import CycleCountDetailScreen from './CycleCountDetailScreen'
import CrossDockPlanListScreen from './CrossDockPlanListScreen'
import CycleCountListScreen from './CycleCountListScreen'
import { DockAppointmentsTab } from './DockAppointmentsTab'
import LocationsScreen from './LocationsScreen'
import PickBatchListScreen from './PickBatchListScreen'
import PurchaseOrderDetailScreen from './PurchaseOrderDetailScreen'
import ReceiptListScreen from './ReceiptListScreen'
import { TaskQueue } from './taskQueue'

type Handler = (url: URL) => unknown
const mock = vi.hoisted(() => ({ requests: [] as URL[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    const body = (mock.handler as Handler)(url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const PO = '99999999-0000-0000-0000-000000000001'

const TASKS = [
  { id: 1, typeCode: 'PUTAWAY', type: 'Acomodo', statusCode: 'PENDING', status: 'Pendiente', warehousePublicId: WH, warehouseCode: 'ALM-01', completableFromQueue: true },
  { id: 2, typeCode: 'REPLENISH', type: 'Reabasto', statusCode: 'PENDING', status: 'Pendiente', warehousePublicId: WH, warehouseCode: 'ALM-01', completableFromQueue: true },
]

// Conteo a ciegas (Lote 8A, decisión 15): sin warehouse.count el API manda netVariance/varianceLines = null.
const BLIND_COUNT = {
  id: 1,
  number: 'CC-00001',
  warehouseCode: 'ALM-01',
  statusCode: 'OPEN',
  status: 'Abierto',
  lineCount: 3,
  countedLines: 1,
  netVariance: null,
  varianceLines: null,
  createdAtUtc: '2026-01-01T00:00:00Z',
}

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/cycle-counts') return [BLIND_COUNT]
  if (p === '/api/v1/cycle-counts/1') return { count: BLIND_COUNT, isBlind: true, lines: [] }
  if (p === '/api/v1/warehouse-tasks') return { total: TASKS.length, skip: 0, take: 25, items: TASKS }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/zones`)
    return [{ id: 1, code: 'A', name: 'Zona A', zoneTypeCode: 'STORAGE', zoneType: 'Almacenaje', isActive: true, binCount: 2 }]
  if (p === `/api/v1/warehouses/${WH}/bins`)
    return [
      { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', qtyOnHand: 5, productCount: 1, isActive: true },
      { id: 11, code: 'A-02', zoneId: 1, zoneCode: 'A', qtyOnHand: 0, productCount: 0, isActive: true },
    ]
  if (p === '/api/v1/inventory/balances') return { total: 0, skip: 0, take: 200, items: [] }
  if (p === '/api/v1/dock-appointments') return []
  if (p === '/api/v1/cross-dock-plans') return []
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  if (p === `/api/v1/purchase-orders/${PO}`)
    return { id: 7, publicId: PO, number: 'OC-00001', supplierName: 'Proveedor', warehousePublicId: WH, warehouseCode: 'ALM-01', statusCode: 'PARTIAL', status: 'Parcial', lines: [], rowVersion: 'AA==' }
  if (p === `/api/v1/purchase-orders/${PO}/shortage-lines`)
    return [{ purchaseOrderLineId: 5, productPublicId: 'aaaaaaaa-0000-0000-0000-000000000001', sku: 'TORN-01', productName: 'Tornillo', qtyOrdered: 10, qtyReceived: 6, qtyResolved: 0, qtyPending: 4 }]
  return new Response(JSON.stringify({ title: 'Sin acceso', code: 'module_disabled' }), { status: 403 })
}

function wrap(ui: ReactNode, permissions: string[], modules: string[], path = '/', pattern = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={modules}>
          <Routes>
            <Route path={pattern} element={ui} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.handler = route
})

describe('Ubicaciones (maqueta ubicaciones())', () => {
  const path = `/warehouse/locations?warehouse=${WH}`

  it('cada nodo de zona lleva la barra .spark: 7 segmentos a la altura del % ocupado', async () => {
    wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    const node = await screen.findByRole('group', { name: /Zona A/ })
    const segments = node.querySelectorAll('.spark i')
    expect(segments).toHaveLength(7)
    // 1 de 2 posiciones con existencias = 50 %
    segments.forEach((s) => expect((s as HTMLElement).style.height).toBe('50%'))
  })

  it("'Nueva posición' solo con warehouse.manage, y abre el modal de alta con las zonas del almacén elegido", async () => {
    const user = userEvent.setup()
    const readOnly = wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    await screen.findByRole('group', { name: /Zona A/ })
    expect(screen.queryByRole('button', { name: 'Nueva posición' })).toBeNull()
    readOnly.unmount()

    wrap(<LocationsScreen />, ['inventory.view', 'warehouse.manage'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    await screen.findByRole('group', { name: /Zona A/ })
    await user.click(screen.getByRole('button', { name: 'Nueva posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva posición' })
    expect(within(dialog).getByRole('option', { name: 'A' })).toBeInTheDocument()
  })
})

describe('TaskQueue (tareas de almacén dentro de la pantalla de su tipo)', () => {
  it("'Completar' exige el permiso del tipo de tarea (como Iniciar): solo lectura no lo ve", async () => {
    wrap(<TaskQueue types={['PUTAWAY', 'REPLENISH']} title="Tareas" />, ['inventory.view'], ['WMS_LOTSERIAL'])
    await screen.findAllByText('Acomodo')
    expect(screen.queryByRole('button', { name: 'Completar' })).toBeNull()
  })

  it("con warehouse.receive solo se ofrece 'Completar' en PUTAWAY", async () => {
    wrap(<TaskQueue types={['PUTAWAY', 'REPLENISH']} title="Tareas" />, ['inventory.view', 'warehouse.receive'], ['WMS_LOTSERIAL'])
    await screen.findAllByText('Acomodo')
    expect(screen.getAllByRole('button', { name: 'Completar' })).toHaveLength(1)
    const row = screen.getAllByRole('button', { name: 'Completar' })[0].closest('tr, .card, article, li') as HTMLElement | null
    if (row) expect(within(row).getByText('Acomodo')).toBeInTheDocument()
  })
})

describe('Tareas y citas dentro de las pantallas de la maqueta (Fase 3)', () => {
  const taskRequests = () => mock.requests.filter((u) => u.pathname === '/api/v1/warehouse-tasks')

  it("Recibo → 'Acomodo pendiente' (?tab=putaway) pide solo tareas PUTAWAY", async () => {
    wrap(<ReceiptListScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], '/warehouse/receipts?tab=putaway', '/warehouse/receipts')
    expect(await screen.findByRole('tab', { name: 'Acomodo pendiente' })).toHaveAttribute('aria-selected', 'true')
    await waitFor(() => expect(taskRequests().length).toBeGreaterThan(0))
    expect(taskRequests().every((u) => u.searchParams.getAll('types').join() === 'PUTAWAY')).toBe(true)
  })

  it("Recolección → 'Reabasto' pide solo tareas REPLENISH y ofrece 'Correr reabasto' con warehouse.pick", async () => {
    const user = userEvent.setup()
    wrap(<PickBatchListScreen />, ['inventory.view', 'warehouse.pick'], ['WMS_LOTSERIAL'], '/warehouse/pick-batches', '/warehouse/pick-batches')
    await user.click(await screen.findByRole('tab', { name: 'Reabasto' }))
    await waitFor(() => expect(taskRequests().length).toBeGreaterThan(0))
    expect(taskRequests().every((u) => u.searchParams.getAll('types').join() === 'REPLENISH')).toBe(true)
    expect(screen.getByRole('button', { name: 'Correr reabasto' })).toBeInTheDocument()
  })

  it("Cruce de muelle tiene la pestaña 'Citas de muelle'; 'Tareas de cruce' solo con WMS_LOTSERIAL", async () => {
    const user = userEvent.setup()
    wrap(<CrossDockPlanListScreen />, ['inventory.view', 'warehouse.crossdock'], ['CROSSDOCK'], '/warehouse/cross-dock-plans', '/warehouse/cross-dock-plans')
    await user.click(await screen.findByRole('tab', { name: 'Citas de muelle' }))
    await waitFor(() => expect(mock.requests.some((u) => u.pathname === '/api/v1/dock-appointments')).toBe(true))
    expect(screen.queryByRole('tab', { name: 'Tareas de cruce' })).toBeNull()
  })

  it("con WMS_LOTSERIAL, 'Tareas de cruce' pide solo tareas CROSSDOCK", async () => {
    wrap(<CrossDockPlanListScreen />, ['inventory.view'], ['CROSSDOCK', 'WMS_LOTSERIAL'], '/warehouse/cross-dock-plans?tab=tasks', '/warehouse/cross-dock-plans')
    expect(await screen.findByRole('tab', { name: 'Tareas de cruce' })).toHaveAttribute('aria-selected', 'true')
    await waitFor(() => expect(taskRequests().length).toBeGreaterThan(0))
    expect(taskRequests().every((u) => u.searchParams.getAll('types').join() === 'CROSSDOCK')).toBe(true)
  })
})

describe('DockAppointmentsTab', () => {
  it('al cargar no pide avisos de llegada (WMS): la agenda de CROSSDOCK no depende de ese módulo', async () => {
    wrap(<DockAppointmentsTab />, ['inventory.view', 'warehouse.crossdock'], ['CROSSDOCK'])
    await waitFor(() => expect(mock.requests.some((u) => u.pathname === '/api/v1/dock-appointments')).toBe(true))
    expect(mock.requests.some((u) => u.pathname === '/api/v1/asns')).toBe(false)
  })
})

describe('PurchaseOrderDetailScreen', () => {
  it('Resolver faltante con CLOSE no consulta posiciones de WMS', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderDetailScreen />, ['purchasing.view', 'inventory.adjust'], ['PURCHASING'], `/po/${PO}`, '/po/:publicId')
    await user.click(await screen.findByRole('tab', { name: 'Faltantes' }))
    await user.click(await screen.findByRole('button', { name: 'Resolver' }))
    expect(await screen.findByLabelText(/Acción/)).toHaveValue('CLOSE')
    expect(mock.requests.some((u) => u.pathname.endsWith('/bins'))).toBe(false)
    expect(mock.requests.some((u) => u.pathname.startsWith('/api/v1/products/'))).toBe(false)
  })
})

describe('CycleCountListScreen', () => {
  it("sin warehouse.count no hay columna 'Diferencia neta'", async () => {
    wrap(<CycleCountListScreen />, ['inventory.view'], ['WMS_LOTSERIAL'])
    await screen.findAllByText('CC-00001')
    expect(screen.queryByText('Diferencia neta')).toBeNull()
  })

  it("con warehouse.count y netVariance null la celda es '—', nunca '0'", async () => {
    wrap(<CycleCountListScreen />, ['inventory.view', 'warehouse.count'], ['WMS_LOTSERIAL'])
    await screen.findAllByText('CC-00001')
    expect(screen.getAllByText('Diferencia neta').length).toBeGreaterThan(0)
    expect(screen.getAllByText('—').length).toBeGreaterThan(0)
    expect(screen.queryByText('0')).toBeNull()
  })
})

describe('CycleCountDetailScreen', () => {
  it("a ciegas el resumen pinta '—' en líneas con diferencia y diferencia neta (no '0')", async () => {
    wrap(
      <CycleCountDetailScreen />,
      ['inventory.view', 'warehouse.count.capture'],
      ['WMS_LOTSERIAL'],
      '/warehouse/cycle-counts/1',
      '/warehouse/cycle-counts/:id',
    )
    for (const label of ['Líneas con diferencia', 'Diferencia neta']) {
      const field = (await screen.findByText(label, { selector: 'label' })).closest('.f') as HTMLElement
      expect(within(field).getByText('—')).toBeInTheDocument()
      expect(within(field).queryByText('0')).toBeNull()
    }
  })
})
