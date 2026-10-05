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
import CrossDockPlanListScreen from './CrossDockPlanListScreen'
import { DockAppointmentsTab } from './DockAppointmentsTab'
import LocationsScreen from './LocationsScreen'
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

// Zona A sin cupo configurado en ninguna posición (como Advance Depot hoy); zona B con cupo.
const ZONES = [
  { id: 1, code: 'A', name: 'Zona A', zoneTypeCode: 'STORAGE', zoneType: 'Almacenaje', isActive: true, binCount: 2, occupiedBinCount: 1, capacityQty: 0, qtyOnHandInCapacityBins: 0, qtyOnHand: 5, binsWithoutCapacity: 2 },
  { id: 2, code: 'B', name: 'Zona B', zoneTypeCode: 'PICKING', zoneType: 'Picking', isActive: true, binCount: 1, occupiedBinCount: 1, capacityQty: 100, qtyOnHandInCapacityBins: 40, qtyOnHand: 40, binsWithoutCapacity: 0 },
]
const BINS = [
  { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', qtyOnHand: 5, productCount: 1, singleProductSku: 'TORN-01', singleProductName: 'Tornillo', occupancy: 'NO_CAPACITY', isActive: true },
  { id: 11, code: 'A-02', zoneId: 1, zoneCode: 'A', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
  { id: 20, code: 'B-01', zoneId: 2, zoneCode: 'B', qtyOnHand: 40, productCount: 2, maxCapacityQty: 100, occupancy: 'PARTIAL', isActive: true },
]

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/warehouse-tasks') return { total: TASKS.length, skip: 0, take: 25, items: TASKS }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/zones`) return ZONES
  if (p === `/api/v1/warehouses/${WH}/bins`) {
    // listado paginado (Lote 1) con el filtro zoneIds del servidor
    const zoneIds = url.searchParams.getAll('zoneIds').map(Number)
    const items = zoneIds.length > 0 ? BINS.filter((b) => zoneIds.includes(b.zoneId)) : BINS
    return { total: items.length, skip: Number(url.searchParams.get('skip') ?? 0), take: Number(url.searchParams.get('take') ?? 100), items }
  }
  if (p === '/api/v1/inventory/balances') return { total: 0, skip: 0, take: 200, items: [] }
  if (p === '/api/v1/receipts') return { total: 0, skip: 0, take: 25, items: [] }
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

describe('Posiciones (antes Ubicaciones; maqueta ubicaciones())', () => {
  const path = `/warehouse/locations?warehouse=${WH}`

  const binRequests = () => mock.requests.filter((u) => u.pathname === `/api/v1/warehouses/${WH}/bins`)

  it('recuadro de zona: ocupado/capacidad con % real; sin cupo, la existencia sin porcentaje; sin barras de 7 días', async () => {
    wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    const withCapacity = await screen.findByRole('button', { name: /^Zona Zona B: 40 de 100 unidades de cupo ocupadas \(40%\)/ })
    expect(withCapacity).toHaveTextContent('40/100')
    expect(withCapacity).toHaveTextContent('ocupado · 40%')
    const noCapacity = screen.getByRole('button', { name: /^Zona Zona A: 5 unidades, sin cupo configurado/ })
    expect(noCapacity).toHaveTextContent('unidades · sin cupo configurado')
    expect(noCapacity).not.toHaveTextContent('%')
    expect(document.querySelector('.locations .spark')).toBeNull()
  })

  it('clic en un recuadro filtra la tabla por esa zona (zoneIds al servidor); otro clic quita el filtro', async () => {
    const user = userEvent.setup()
    wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    await screen.findByText('A-01')
    const zoneB = screen.getByRole('button', { name: /^Zona Zona B/ })
    expect(zoneB).toHaveAttribute('aria-pressed', 'false')
    await user.click(zoneB)
    await waitFor(() => expect(screen.queryByText('A-01')).toBeNull())
    expect(screen.getByText('B-01')).toBeInTheDocument()
    expect(zoneB).toHaveAttribute('aria-pressed', 'true')
    expect(binRequests().at(-1)?.searchParams.getAll('zoneIds')).toEqual(['2'])
    await user.click(zoneB)
    await screen.findByText('A-01')
    expect(zoneB).toHaveAttribute('aria-pressed', 'false')
    expect(binRequests().at(-1)?.searchParams.getAll('zoneIds')).toEqual([])
  })

  it('tabla paginada en el servidor con Cupo y Estatus (Vacía / Parcial / Ocupada sin cupo), producto o "N productos", sin buscador', async () => {
    wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], `${path}&zone=1`, '/warehouse/locations')
    await screen.findByText('A-01')
    // ?zone=1 llega al servidor y resalta su recuadro
    expect(binRequests()[0].searchParams.getAll('zoneIds')).toEqual(['1'])
    expect(binRequests()[0].searchParams.get('skip')).toBe('0')
    expect(binRequests()[0].searchParams.get('take')).toBe('25')
    expect(screen.getByRole('button', { name: /^Zona Zona A/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('columnheader', { name: /Estatus/ })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /Cupo/ })).toBeInTheDocument()
    expect(screen.getByText('Tornillo')).toBeInTheDocument()
    expect(screen.getByText('Ocupada sin cupo')).toBeInTheDocument()
    expect(screen.getByText('Vacía')).toBeInTheDocument()
    // sin buscador dentro de la tabla: los únicos campos de texto son los filtros "Posición" y "Pasillo" de arriba
    expect(screen.getAllByRole('searchbox')).toEqual([screen.getByRole('searchbox', { name: 'Posición' }), screen.getByRole('searchbox', { name: 'Pasillo' })])
  })

  it('el filtro "Posición" manda el texto al servidor (search) y vuelve a la página 1', async () => {
    const user = userEvent.setup()
    wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    await screen.findByText('A-01')
    await user.type(screen.getByRole('searchbox', { name: 'Posición' }), 'B-0')
    await waitFor(() => expect(binRequests().at(-1)?.searchParams.get('search')).toBe('B-0'))
    expect(binRequests().at(-1)?.searchParams.get('skip')).toBe('0')
  })

  it('posición con cupo: barra con el % real y estatus Parcial; varios productos = "N productos"', async () => {
    wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], `${path}&zone=2`, '/warehouse/locations')
    await screen.findByText('B-01')
    expect(screen.getByRole('img', { name: '40% del cupo' })).toBeInTheDocument()
    expect(screen.getByText('Parcial')).toBeInTheDocument()
    expect(screen.getByText('2 productos')).toBeInTheDocument()
  })

  it("'Nueva posición' solo con warehouse.manage, y abre el modal de alta con las zonas del almacén elegido", async () => {
    const user = userEvent.setup()
    const readOnly = wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    await screen.findByRole('button', { name: /^Zona Zona A/ })
    expect(screen.queryByRole('button', { name: 'Nueva posición' })).toBeNull()
    readOnly.unmount()

    wrap(<LocationsScreen />, ['inventory.view', 'warehouse.manage'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    await screen.findByRole('button', { name: /^Zona Zona A/ })
    await user.click(screen.getByRole('button', { name: 'Nueva posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva posición' })
    // la zona es un combobox con buscador (BinModal, Lote 1): sus opciones son las zonas del almacén elegido
    await user.click(within(dialog).getByRole('combobox', { name: /Zona/ }))
    expect(await screen.findByRole('option', { name: /A · Zona A/ })).toBeInTheDocument()
  })

  it('clic en la fila abre la edición de esa posición (solo con warehouse.manage)', async () => {
    const user = userEvent.setup()
    const readOnly = wrap(<LocationsScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    await user.click(await screen.findByText('A-01'))
    expect(screen.queryByRole('dialog')).toBeNull()
    readOnly.unmount()

    wrap(<LocationsScreen />, ['inventory.view', 'warehouse.manage'], ['WMS_LOTSERIAL'], path, '/warehouse/locations')
    await user.click(await screen.findByText('A-01'))
    const dialog = await screen.findByRole('dialog')
    // el código se muestra de solo lectura (campo con su valor) en la edición
    expect(within(dialog).queryByDisplayValue('A-01') ?? within(dialog).queryByText('A-01')).not.toBeNull()
    expect(within(dialog).getByLabelText(/Cupo máximo/)).toBeInTheDocument()
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

  // Lote 13 (decisión 5): 'Acomodo pendiente' es la lista de RECIBOS con acomodo por cerrar (phase=PENDING_PUTAWAY); las
  // tareas de cada uno van a la derecha (más casos en ReceiptScreen.test.tsx).
  it("Recibo → 'Acomodo pendiente' (?tab=putaway) pide recibos con phase=PENDING_PUTAWAY, no la cola de tareas", async () => {
    wrap(<ReceiptListScreen />, ['inventory.view'], ['WMS_LOTSERIAL'], '/warehouse/receipts?tab=putaway', '/warehouse/receipts')
    expect(await screen.findByRole('tab', { name: 'Acomodo pendiente' })).toHaveAttribute('aria-selected', 'true')
    const receiptRequests = () => mock.requests.filter((u) => u.pathname === '/api/v1/receipts')
    await waitFor(() => expect(receiptRequests().length).toBeGreaterThan(0))
    expect(receiptRequests().every((u) => u.searchParams.get('phase') === 'PENDING_PUTAWAY')).toBe(true)
    expect(taskRequests()).toHaveLength(0)
  })

  // Recolección → 'Reabasto': en PickBatchScreen.test.tsx (Lote 13).

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

// Conteo cíclico (Lote 14, dos paneles): CycleCountScreen.test.tsx.
