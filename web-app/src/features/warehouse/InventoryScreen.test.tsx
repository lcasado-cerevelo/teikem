// Pruebas de Inventario (Lote F6) sobre un fetch simulado: en Saldos y Kárdex la paginación es del servidor, así que todo
// cambio de filtro o del buscador vuelve a la página 1 (skip=0); el filtro Producto viaja como productPublicIds; el Kárdex
// muestra la columna Motivo. Sus endpoints no aceptan orden: el orden por encabezado es en el cliente (solo la página visible).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, useLocation } from 'react-router-dom'
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
  if (p === `/api/v1/products/${PRODUCT.publicId}`) return { product: PRODUCT }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  return []
}

/** Pestaña Saldos (desde la Fase 8 el Kárdex es la primera pestaña y va sin parámetro). */
const BALANCES_URL = '/warehouse/kardex?tab=balances'

/** Muestra la URL actual (ruta + consulta) para comprobar lo que escribe la pantalla. */
function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname + location.search}</output>
}

function wrap(ui: ReactNode, url = '/warehouse/kardex') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[url]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.view']} modules={['WMS_LOTSERIAL']}>
          {ui}
          <LocationProbe />
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
    wrap(<InventoryScreen />, BALANCES_URL)
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
    wrap(<InventoryScreen />, BALANCES_URL)
    await waitLast(PATH, (u) => u.searchParams.get('skip') === '0')
    await goToPage2(user, PATH)
    await user.type(screen.getByRole('searchbox'), 'tornillo')
    await waitLast(PATH, (u) => u.searchParams.get('search') === 'tornillo')
    expect(last(PATH).searchParams.get('skip')).toBe('0')
  })

  it('las columnas se ordenan por encabezado (en el cliente, sobre la página visible) sin volver a pedir al API', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, BALANCES_URL)
    const header = await screen.findByRole('columnheader', { name: 'Disponible' })
    expect(header).toHaveAttribute('aria-sort', 'none')
    const calls = mock.requests.filter((u) => u.pathname === PATH).length
    await user.click(within(header).getByRole('button'))
    await user.click(within(screen.getByRole('columnheader', { name: /Disponible/ })).getByRole('button'))
    expect(screen.getByRole('columnheader', { name: /Disponible/ })).toHaveAttribute('aria-sort', 'descending')
    // la página 1 trae A-1…A-25: en descendente la primera fila es la de mayor disponible
    const firstRow = screen.getAllByRole('row')[1]
    expect(within(firstRow).getByText('A-25')).toBeInTheDocument()
    expect(mock.requests.filter((u) => u.pathname === PATH).length).toBe(calls)
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
    // ordenable por encabezado (en el cliente, sobre la página visible)
    const dateHeader = screen.getByRole('columnheader', { name: 'Fecha' })
    expect(within(dateHeader).getByRole('button')).toBeInTheDocument()
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

describe('InventoryScreen · Kárdex de movimientos (Fase 8: ítem propio del menú)', () => {
  it('sin parámetros abre el Kárdex (primera pestaña) con el título de la maqueta; Saldos no se consulta', async () => {
    wrap(<InventoryScreen />)
    expect(await screen.findByRole('heading', { level: 1, name: 'Kárdex de movimientos' })).toBeInTheDocument()
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Kárdex', 'Saldos', 'Conciliación'])
    expect(screen.getByRole('tab', { name: 'Kárdex' })).toHaveAttribute('aria-selected', 'true')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('skip') === '0')
    expect(mock.requests.some((u) => u.pathname === '/api/v1/inventory/balances')).toBe(false)
  })

  it('la pestaña va en la URL: Saldos → ?tab=balances, Conciliación → ?tab=reconciliation, Kárdex sin parámetro', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, `/warehouse/kardex?product=${PRODUCT.publicId}`)
    await user.click(await screen.findByRole('tab', { name: 'Saldos' }))
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/warehouse\/kardex\?tab=balances$/)
    await user.click(screen.getByRole('tab', { name: 'Conciliación' }))
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/warehouse\/kardex\?tab=reconciliation$/)
    await user.click(screen.getByRole('tab', { name: 'Kárdex' }))
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/warehouse\/kardex$/)
  })

  it('?types=ADJUSTMENT (Reporte de ajustes de Productos e inventario) filtra el Kárdex por tipo', async () => {
    wrap(<InventoryScreen />, `/warehouse/kardex?types=ADJUSTMENT&warehousePublicIds=${WH}`)
    await waitLast(
      '/api/v1/inventory/transactions',
      (u) => u.searchParams.getAll('types').includes('ADJUSTMENT') && u.searchParams.getAll('warehousePublicIds').includes(WH),
    )
  })
})

describe('InventoryScreen · parámetros de URL (enlaces de Pulso, Lote F7A)', () => {
  it('?tab=kardex&product=X (valor anterior de la pestaña) abre el Kárdex filtrado por el producto y muestra su SKU en la píldora', async () => {
    wrap(<InventoryScreen />, `/warehouse/kardex?tab=kardex&product=${PRODUCT.publicId}`)
    expect(await screen.findByRole('tab', { name: 'Kárdex' })).toHaveAttribute('aria-selected', 'true')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    expect(last('/api/v1/inventory/transactions').searchParams.get('skip')).toBe('0')
    // Saldos no se consulta: la pestaña inicial es el Kárdex
    expect(mock.requests.some((u) => u.pathname === '/api/v1/inventory/balances')).toBe(false)
    // el SKU sale de la ficha del producto
    expect(await screen.findByRole('button', { name: 'Quitar TORN-01' }, { timeout: 4000 })).toBeInTheDocument()
  })

  it('?tab=balances&categoryIds=7 abre Saldos filtrado por la categoría', async () => {
    wrap(<InventoryScreen />, '/warehouse/kardex?tab=balances&categoryIds=7')
    expect(await screen.findByRole('tab', { name: 'Saldos' })).toHaveAttribute('aria-selected', 'true')
    await waitLast('/api/v1/inventory/balances', (u) => u.searchParams.getAll('categoryIds').includes('7'))
  })

  it('?tab=balances&warehousePublicIds=W&categoryIds=7 abre Saldos con el almacén y la categoría (los mismos filtros que la cifra de Pulso)', async () => {
    wrap(<InventoryScreen />, `/warehouse/kardex?tab=balances&categoryIds=7&warehousePublicIds=${WH}`)
    await waitLast(
      '/api/v1/inventory/balances',
      (u) => u.searchParams.getAll('categoryIds').includes('7') && u.searchParams.getAll('warehousePublicIds').includes(WH),
    )
  })

  it('?product=X&warehousePublicIds=W manda el almacén al Kárdex', async () => {
    wrap(<InventoryScreen />, `/warehouse/kardex?product=${PRODUCT.publicId}&warehousePublicIds=${WH}`)
    await waitLast(
      '/api/v1/inventory/transactions',
      (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId) && u.searchParams.getAll('warehousePublicIds').includes(WH),
    )
  })

  it('varios productos en la URL: cada píldora toma su SKU; la ficha que no se puede leer muestra un texto fijo', async () => {
    const other = { ...PRODUCT, id: 2, publicId: 'aaaaaaaa-0000-0000-0000-000000000002', sku: 'TUER-02', name: 'Tuerca' }
    const missing = 'aaaaaaaa-0000-0000-0000-000000000009'
    mock.handler = (url: URL) => {
      if (url.pathname === `/api/v1/products/${other.publicId}`) return { product: other }
      if (url.pathname === `/api/v1/products/${missing}`)
        return new Response(JSON.stringify({ title: 'No encontrado', status: 404 }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } })
      return route(url)
    }
    wrap(<InventoryScreen />, `/warehouse/kardex?product=${PRODUCT.publicId},${other.publicId}&product=${missing}`)
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('productPublicIds').length === 3)
    expect(await screen.findByRole('button', { name: 'Quitar TORN-01' }, { timeout: 4000 })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Quitar TUER-02' }, { timeout: 4000 })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Quitar Producto no disponible' }, { timeout: 4000 })).toBeInTheDocument()
    expect(screen.queryByText('…')).toBeNull()
  })

  it('los filtros de la URL son de la pestaña abierta: al cambiar de pestaña no pasan a la otra ni reaparecen al volver', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, `/warehouse/kardex?product=${PRODUCT.publicId}&warehousePublicIds=${WH}`)
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    await user.click(screen.getByRole('tab', { name: 'Saldos' }))
    await waitLast('/api/v1/inventory/balances', (u) => u.searchParams.get('skip') === '0')
    const balances = last('/api/v1/inventory/balances').searchParams
    expect(balances.has('productPublicIds')).toBe(false)
    expect(balances.has('warehousePublicIds')).toBe(false)
    const before = mock.requests.length
    await user.click(screen.getByRole('tab', { name: 'Kárdex' }))
    await waitFor(() => expect(mock.requests.slice(before).some((u) => u.pathname === '/api/v1/inventory/transactions')).toBe(true), { timeout: 4000 })
    const kardex = last('/api/v1/inventory/transactions').searchParams
    expect(kardex.has('productPublicIds')).toBe(false)
    expect(kardex.has('warehousePublicIds')).toBe(false)
    expect(screen.queryByRole('button', { name: 'Quitar TORN-01' })).toBeNull()
  })

  it('?ref= ya no se usa: el Kárdex no manda búsqueda (el API no compara el documento de origen)', async () => {
    wrap(<InventoryScreen />, '/warehouse/kardex?ref=REC-000318')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('skip') === '0')
    expect(last('/api/v1/inventory/transactions').searchParams.has('search')).toBe(false)
    expect(screen.getByRole('searchbox')).toHaveValue('')
  })
})
