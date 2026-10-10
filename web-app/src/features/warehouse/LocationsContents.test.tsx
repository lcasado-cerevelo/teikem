// 2026-10-10 — Ubicaciones: «N productos» (posición con varios) es un enlace que abre una ventana con lo que hay en la posición.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import LocationsScreen from './LocationsScreen'

interface Call {
  method: string
  url: URL
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.calls.push({ method: req.method, url })
    const res = route(url)
    return res instanceof Response ? res : new Response(JSON.stringify(res), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const ZONES = [{ id: 1, code: 'A', name: 'Zona A', zoneTypeCode: 'STORAGE', isActive: true, binCount: 2, capacityQty: 0, qtyOnHand: 0, binsWithoutCapacity: 2 }]
const BINS = [
  { id: 10, code: 'A-10', zoneId: 1, zoneCode: 'A', qtyOnHand: 12, productCount: 3, occupancy: 'NO_CAPACITY', isActive: true },
  { id: 11, code: 'A-11', zoneId: 1, zoneCode: 'A', qtyOnHand: 4, productCount: 1, singleProductSku: 'TORN-01', singleProductName: 'Tornillo', occupancy: 'NO_CAPACITY', isActive: true },
]
const BALANCES = [
  { id: 1, binId: 10, binCode: 'A-10', productPublicId: 'p1', sku: 'TORN-01', productName: 'Tornillo', lotNumber: null, qtyOnHand: 5, qtyReserved: 1, qtyAvailable: 4 },
  { id: 2, binId: 10, binCode: 'A-10', productPublicId: 'p2', sku: 'TUER-01', productName: 'Tuerca', lotNumber: 'L-7', qtyOnHand: 4, qtyReserved: 0, qtyAvailable: 4 },
  { id: 3, binId: 10, binCode: 'A-10', productPublicId: 'p3', sku: 'ARAN-01', productName: 'Arandela', lotNumber: null, qtyOnHand: 3, qtyReserved: 0, qtyAvailable: 3 },
]

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/zones`) return ZONES
  if (p === `/api/v1/warehouses/${WH}/bins`) return { total: BINS.length, skip: 0, take: 100, items: BINS }
  if (p === '/api/v1/inventory/balances') return { total: BALANCES.length, skip: 0, take: 200, items: BALANCES }
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  return new Response(JSON.stringify({ title: 'Sin acceso', code: 'forbidden' }), { status: 403 })
}

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[`/warehouse/locations?warehouse=${WH}`]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.view']} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/locations" element={<LocationsScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

describe('Ubicaciones · productos de una posición', () => {
  it('«3 productos» es un enlace que abre la ventana con los productos y su existencia; con un solo producto es solo el nombre', async () => {
    const user = userEvent.setup()
    wrap()
    const link = await screen.findByRole('button', { name: /3 productos/ })
    expect(screen.queryByRole('button', { name: /Tornillo/ })).toBeNull()
    await user.click(link)
    const dialog = await screen.findByRole('dialog', { name: 'Productos en A-10' })
    expect(await within(dialog).findByText('TUER-01')).toBeInTheDocument()
    expect(within(dialog).getByText('Arandela')).toBeInTheDocument()
    expect(within(dialog).getByText('L-7')).toBeInTheDocument()
    const read = mock.calls.find((c) => c.url.pathname === '/api/v1/inventory/balances')
    expect(read?.url.searchParams.getAll('binIds')).toEqual(['10'])
    expect(read?.url.searchParams.getAll('warehousePublicIds')).toEqual([WH])
    await user.click(within(dialog).getAllByRole('button', { name: 'Cerrar' }).at(-1)!)
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Productos en A-10' })).toBeNull())
  })
})
