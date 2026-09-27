// Pruebas de Inventario (Lote F6) sobre un fetch simulado: en Saldos y Kárdex la paginación es del servidor, así que todo
// cambio de filtro o del buscador vuelve a la página 1 (skip=0); el filtro Producto viaja como productPublicIds; el Kárdex
// muestra la columna Motivo y no ofrece orden en el cliente (solo reordenaría la página visible).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import InventoryScreen from './InventoryScreen'

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
const PRODUCT = { id: 1, publicId: 'aaaaaaaa-0000-0000-0000-000000000001', sku: 'TORN-01', name: 'Tornillo', isOwn: true, isActive: true, trackingTypeCode: 'NONE' }
const TOTAL = 60

function balance(i: number) {
  return { id: i, warehousePublicId: WH, warehouseCode: 'ALM-01', binCode: `A-${i}`, productPublicId: PRODUCT.publicId, sku: 'TORN-01', productName: 'Tornillo', qtyOnHand: i, qtyReserved: 0, qtyAvailable: i }
}

function movement(i: number) {
  return {
    id: i,
    createdAtUtc: '2026-09-01T10:00:00',
    typeCode: 'ADJUST',
    type: 'Ajuste',
    productPublicId: PRODUCT.publicId,
    sku: 'TORN-01',
    productName: 'Tornillo',
    toWarehouseCode: 'ALM-01',
    toBinCode: 'A-01',
    quantity: 10,
    signedQuantity: 10,
    reasonCode: 'COUNT',
    reason: `Conteo físico ${i}`,
  }
}

function page<T>(url: URL, make: (i: number) => T) {
  const skip = Number(url.searchParams.get('skip') ?? 0)
  const take = Number(url.searchParams.get('take') ?? 25)
  const n = Math.max(0, Math.min(take, TOTAL - skip))
  return { total: TOTAL, skip, take, items: Array.from({ length: n }, (_, k) => make(skip + k + 1)) }
}

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/inventory/balances') return page(url, balance)
  if (p === '/api/v1/inventory/transactions') return page(url, movement)
  if (p === '/api/v1/products') return { total: 1, skip: 0, take: 50, items: [PRODUCT] }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  return []
}

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.view']} modules={['WMS_LOTSERIAL']}>
          {ui}
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Última consulta a la ruta indicada. */
function last(path: string): URL {
  const hits = mock.requests.filter((u) => u.pathname === path)
  return hits[hits.length - 1]
}

/** Espera a que la última consulta a `path` cumpla la condición. */
async function waitLast(path: string, check: (u: URL) => boolean) {
  await waitFor(() => expect(check(last(path))).toBe(true), { timeout: 4000 })
}

async function goToPage2(user: ReturnType<typeof userEvent.setup>, path: string) {
  await user.click(await screen.findByRole('button', { name: 'Página siguiente' }, { timeout: 4000 }))
  await waitLast(path, (u) => u.searchParams.get('skip') === '25')
}

async function pickProduct(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole('combobox', { name: 'Producto' }), 'torn')
  await user.click(await screen.findByRole('option', { name: /TORN-01 · Tornillo/ }, { timeout: 4000 }))
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.handler = route
})

describe('InventoryScreen · Saldos', () => {
  const PATH = '/api/v1/inventory/balances'

  it('elegir un producto en el filtro vuelve a la página 1 y lo manda como productPublicIds', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    await waitLast(PATH, (u) => u.searchParams.get('skip') === '0')
    await goToPage2(user, PATH)
    await pickProduct(user)
    await waitLast(PATH, (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    expect(last(PATH).searchParams.get('skip')).toBe('0')
    // Saldos solo busca productos activos
    expect(last('/api/v1/products').searchParams.get('activeOnly')).toBe('true')
  })

  it('escribir en el buscador vuelve a la página 1 y manda search', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    await waitLast(PATH, (u) => u.searchParams.get('skip') === '0')
    await goToPage2(user, PATH)
    await user.type(screen.getByRole('searchbox'), 'tornillo')
    await waitLast(PATH, (u) => u.searchParams.get('search') === 'tornillo')
    expect(last(PATH).searchParams.get('skip')).toBe('0')
  })

  it('las columnas no se ordenan en el cliente (la lista es paginada por el servidor)', async () => {
    wrap(<InventoryScreen />)
    const header = await screen.findByRole('columnheader', { name: 'Disponible' })
    expect(within(header).queryByRole('button')).toBeNull()
    expect(header).not.toHaveAttribute('aria-sort')
  })
})

describe('InventoryScreen · Kárdex', () => {
  const PATH = '/api/v1/inventory/transactions'

  it('muestra la columna Motivo con el motivo del movimiento y la cantidad con signo', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    await user.click(await screen.findByRole('tab', { name: 'Kárdex' }))
    expect(await screen.findByRole('columnheader', { name: 'Motivo' })).toBeInTheDocument()
    expect(await screen.findByText('Conteo físico 1')).toBeInTheDocument()
    expect(screen.getAllByText('+10').length).toBeGreaterThan(0)
    const dateHeader = screen.getByRole('columnheader', { name: 'Fecha' })
    expect(within(dateHeader).queryByRole('button')).toBeNull()
  })

  it('el filtro Producto (con dados de baja) vuelve a la página 1 y viaja como productPublicIds', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    await user.click(await screen.findByRole('tab', { name: 'Kárdex' }))
    await waitLast(PATH, (u) => u.searchParams.get('skip') === '0')
    await goToPage2(user, PATH)
    await pickProduct(user)
    await waitLast(PATH, (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    expect(last(PATH).searchParams.get('skip')).toBe('0')
    expect(last('/api/v1/products').searchParams.get('activeOnly')).not.toBe('true')
  })

  it('escribir en el buscador vuelve a la página 1', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    await user.click(await screen.findByRole('tab', { name: 'Kárdex' }))
    await waitLast(PATH, (u) => u.searchParams.get('skip') === '0')
    await goToPage2(user, PATH)
    await user.type(screen.getByRole('searchbox'), 'ajuste')
    await waitLast(PATH, (u) => u.searchParams.get('search') === 'ajuste')
    expect(last(PATH).searchParams.get('skip')).toBe('0')
  })
})
