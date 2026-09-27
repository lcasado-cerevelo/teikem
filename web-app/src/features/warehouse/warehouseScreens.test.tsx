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
import CycleCountListScreen from './CycleCountListScreen'
import DockAppointmentListScreen from './DockAppointmentListScreen'
import PurchaseOrderDetailScreen from './PurchaseOrderDetailScreen'
import WarehouseTaskListScreen from './WarehouseTaskListScreen'

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
  if (p === '/api/v1/dock-appointments') return []
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

describe('WarehouseTaskListScreen', () => {
  it("'Completar' exige el permiso del tipo de tarea (como Iniciar): solo lectura no lo ve", async () => {
    wrap(<WarehouseTaskListScreen />, ['inventory.view'], ['WMS_LOTSERIAL'])
    await screen.findAllByText('Acomodo')
    expect(screen.queryByRole('button', { name: 'Completar' })).toBeNull()
  })

  it("con warehouse.receive solo se ofrece 'Completar' en PUTAWAY", async () => {
    wrap(<WarehouseTaskListScreen />, ['inventory.view', 'warehouse.receive'], ['WMS_LOTSERIAL'])
    await screen.findAllByText('Acomodo')
    expect(screen.getAllByRole('button', { name: 'Completar' })).toHaveLength(1)
    const row = screen.getAllByRole('button', { name: 'Completar' })[0].closest('tr, .card, article, li') as HTMLElement | null
    if (row) expect(within(row).getByText('Acomodo')).toBeInTheDocument()
  })
})

describe('DockAppointmentListScreen', () => {
  it('al cargar no pide avisos de llegada (WMS): la agenda de CROSSDOCK no depende de ese módulo', async () => {
    wrap(<DockAppointmentListScreen />, ['inventory.view', 'warehouse.crossdock'], ['CROSSDOCK'])
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
