import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient, setAccessDeniedHandler } from '../../app/queryClient'
import { SessionContext, type MeDto, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import {
  NO_WAREHOUSE_FILTER,
  readWarehouseFilter,
  sanitizeWarehouseFilter,
  warehouseFilterStorageKey,
  writeWarehouseFilter,
} from './api'
import Pulse from './Pulse'

// Cliente de la app sobre un fetch simulado (misma política que el real).
type Handler = (path: string, url: URL) => Response | unknown
const mock = vi.hoisted(() => ({ urls: [] as URL[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.urls.push(url)
    const result = (mock.handler as Handler)(url.pathname, url)
    if (result instanceof Response) return result
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const ME: MeDto = { userId: 7, fullName: 'María Rivera', email: 'almacen@teikem.local', tenantId: 3, tenantName: 'Demo', lang: 'es' }
const KEY = 'teikem.pulse.warehouseFilter.3.7'

const WH1 = { id: 1, publicId: 'wh-0000-0001', code: 'ALM-01', name: 'Almacén principal', isActive: true }
const WH2 = { id: 2, publicId: 'wh-0000-0002', code: 'ALM-02', name: 'Almacén norte', isActive: true }
const CATEGORIES = [
  // Farmacia solo tiene productos en su subcategoría (productCount del DTO = directos)
  { id: 1, name: 'Farmacia', parentId: null, path: 'Farmacia', isActive: true, productCount: 0 },
  { id: 2, name: 'Analgésicos', parentId: 1, path: 'Farmacia › Analgésicos', isActive: true, productCount: 14 },
]
const PRODUCT = {
  id: 10,
  publicId: 'prod-0000-0010',
  sku: 'AN-0100',
  name: 'Analgésico 500 mg x 20',
  minQty: 400,
  isActive: true,
  isBelowMin: true,
}
const OTHER = { ...PRODUCT, id: 11, publicId: 'prod-0000-0011', sku: 'AN-01000', name: 'Otro' }

function session(): Session {
  return {
    me: ME,
    isAuthenticated: true,
    isLoading: false,
    error: null,
    tenantId: 3,
    lang: 'es',
    setLang: vi.fn(),
    logout: vi.fn(async () => {}),
    switchTenant: vi.fn(async () => {}),
    permissions: new Set(),
    modules: new Set(),
    reloadMe: vi.fn(async () => {}),
  }
}

function renderPulse() {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <SessionContext.Provider value={session()}>
          <AccessProvider permissions={['inventory.view']} modules={['WMS_LOTSERIAL']}>
            <Pulse />
          </AccessProvider>
        </SessionContext.Provider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Saldos según el filtro (lo que devolvería el API), para comprobar que las tarjetas pintan el DTO. */
function handler(path: string, url: URL): unknown {
  const p = url.searchParams
  if (path === '/api/v1/warehouses') return [WH1, WH2]
  if (path === '/api/v1/product-categories') return CATEGORIES
  if (path === `/api/v1/products/${PRODUCT.publicId}`) return { product: PRODUCT }
  if (path.startsWith('/api/v1/products/'))
    return new Response(JSON.stringify({ status: 404, code: 'not_found', title: 'No existe el producto.' }), {
      status: 404,
      headers: { 'Content-Type': 'application/problem+json' },
    })
  if (path === '/api/v1/inventory/balances') {
    if (p.get('productPublicIds')) return { total: 1, skip: 0, take: 1, totalOnHand: 312, totalAvailable: 290, items: [] }
    if (p.get('categoryIds')) return { total: 4, skip: 0, take: 1, totalOnHand: 1240, totalAvailable: 1180, items: [] }
    return { total: 50, skip: 0, take: 1, totalOnHand: 9000, totalAvailable: 8500.5, items: [] }
  }
  if (path === '/api/v1/products') {
    if (p.get('belowMin') !== 'true') return { total: 0, skip: 0, take: 20, items: [] }
    if (p.get('search')) return { total: 2, skip: 0, take: 100, items: [OTHER, PRODUCT] }
    return { total: p.get('categoryIds') ? 2 : 7, skip: 0, take: 1, items: [] }
  }
  if (path === '/api/v1/receipts') return { total: p.get('warehousePublicId') ? 2 : 5, skip: 0, take: 1, items: [] }
  if (path === '/api/v1/warehouse-tasks') {
    if (p.get('types')) return { total: p.get('types') === 'PUTAWAY' ? 20 : 1, skip: 0, take: 1, items: [] }
    return { total: p.get('warehousePublicId') ? 23 : 40, skip: 0, take: 1, items: [] }
  }
  if (path === '/api/v1/cycle-counts') return p.get('warehousePublicIds') ? [{ id: 1 }] : [{ id: 1 }, { id: 2 }]
  return []
}

const tile = (name: string) => screen.findByRole('group', { name })
const lastUrl = (path: string) => mock.urls.filter((u) => u.pathname === path).at(-1)
const onAccessDenied = vi.fn()

beforeAll(() => {
  setLang('es')
  setAccessDeniedHandler(onAccessDenied)
})
afterAll(() => setAccessDeniedHandler(null))
beforeEach(() => {
  mock.urls = []
  mock.handler = handler
  window.localStorage.clear()
  onAccessDenied.mockReset()
})

describe('selección guardada del filtro (lógica pura)', () => {
  it('clave por compañía y usuario; lectura tolerante; sin filtro borra la entrada', () => {
    expect(warehouseFilterStorageKey(3, 7)).toBe(KEY)
    expect(warehouseFilterStorageKey(null, 7)).toBeNull()
    expect(readWarehouseFilter(null)).toEqual(NO_WAREHOUSE_FILTER)
    window.localStorage.setItem(KEY, '{no es json')
    expect(readWarehouseFilter(KEY)).toEqual(NO_WAREHOUSE_FILTER)
    window.localStorage.setItem(KEY, JSON.stringify({ warehousePublicId: 5, item: { kind: 'category', id: 'x' } }))
    expect(readWarehouseFilter(KEY)).toEqual(NO_WAREHOUSE_FILTER)
    writeWarehouseFilter(KEY, { warehousePublicId: WH1.publicId, item: { kind: 'category', id: 2 } })
    expect(readWarehouseFilter(KEY)).toEqual({ warehousePublicId: WH1.publicId, item: { kind: 'category', id: 2 } })
    writeWarehouseFilter(KEY, NO_WAREHOUSE_FILTER)
    expect(window.localStorage.getItem(KEY)).toBeNull()
  })

  it('descarta lo que ya no existe solo cuando llegan los catálogos', () => {
    const saved = { warehousePublicId: 'gone', item: { kind: 'category' as const, id: 99 } }
    expect(sanitizeWarehouseFilter(saved, {})).toBe(saved)
    expect(sanitizeWarehouseFilter(saved, { warehouses: [WH1], categories: CATEGORIES })).toEqual(NO_WAREHOUSE_FILTER)
    const product = { warehousePublicId: WH1.publicId, item: { kind: 'product' as const, publicId: 'p' } }
    expect(sanitizeWarehouseFilter(product, { warehouses: [WH1], productGone: false })).toBe(product)
    expect(sanitizeWarehouseFilter(product, { warehouses: [WH1], productGone: true }).item).toBeNull()
  })
})

describe('Pulse — panel Almacén con filtro (Lote F7A)', () => {
  it('sin filtro: totales generales, reservado = en mano − disponible, bajo mínimo y la marca "almacén" en los documentos', async () => {
    renderPulse()
    await waitFor(async () => expect(within(await tile('En mano')).getByText('9,000')).toBeInTheDocument())
    const available = await tile('Disponible')
    await waitFor(() => expect(within(available).getByText('8,500.50')).toBeInTheDocument())
    expect(within(available).getByText('499.50')).toBeInTheDocument()
    expect(within(available).getByText(/Reservado:/)).toBeInTheDocument()
    await waitFor(async () => expect(within(await tile('Bajo mínimo')).getByText('7')).toBeInTheDocument())
    expect(lastUrl('/api/v1/products')!.search).toBe('?belowMin=true&take=1')
    for (const name of ['Recibos abiertos', 'Tareas pendientes', 'Conteos abiertos'])
      expect(within(await tile(name)).getByText('almacén')).toBeInTheDocument()
    expect(within(await tile('En mano')).queryByText('almacén')).toBeNull()
  })

  it('categoría padre: el subtítulo cuenta los productos de sus subcategorías, como las cifras del API', async () => {
    window.localStorage.setItem(KEY, JSON.stringify({ warehousePublicId: null, item: { kind: 'category', id: 1 } }))
    renderPulse()
    const onHand = await tile('En mano')
    await waitFor(() => expect(within(onHand).getByText('1,240')).toBeInTheDocument())
    await waitFor(() => expect(within(onHand).getByText('Farmacia · 14 productos')).toBeInTheDocument())
  })

  it('restaura almacén + categoría de localStorage: saldo de la categoría, documentos solo por almacén y enlace a Inventario', async () => {
    window.localStorage.setItem(KEY, JSON.stringify({ warehousePublicId: WH1.publicId, item: { kind: 'category', id: 2 } }))
    renderPulse()
    const onHand = await tile('En mano')
    await waitFor(() => expect(within(onHand).getByText('1,240')).toBeInTheDocument())
    await waitFor(() => expect(within(onHand).getByText('Analgésicos · 14 productos')).toBeInTheDocument())
    expect(within(await tile('Disponible')).getByText('60')).toBeInTheDocument()
    const below = await tile('Bajo mínimo')
    await waitFor(() => expect(within(below).getByText('2')).toBeInTheDocument())
    expect(within(below).getByRole('link', { name: 'Ver en Inventario ›' })).toHaveAttribute(
      'href',
      `/warehouse/inventory?categoryIds=2&warehousePublicIds=${encodeURIComponent(WH1.publicId!)}`,
    )
    await waitFor(async () => expect(within(await tile('Recibos abiertos')).getByText('2')).toBeInTheDocument())
    await waitFor(async () => expect(within(await tile('Tareas pendientes')).getByText('23')).toBeInTheDocument())
    await waitFor(async () => expect(within(await tile('Conteos abiertos')).getByText('1')).toBeInTheDocument())

    const balances = lastUrl('/api/v1/inventory/balances')!.searchParams
    expect(balances.getAll('warehousePublicIds')).toEqual([WH1.publicId])
    expect(balances.getAll('categoryIds')).toEqual(['2'])
    const belowMin = lastUrl('/api/v1/products')!.searchParams
    expect(belowMin.get('warehousePublicId')).toBe(WH1.publicId)
    expect(belowMin.getAll('categoryIds')).toEqual(['2'])
    // recibos, tareas y conteos: solo el almacén (nunca la categoría)
    for (const path of ['/api/v1/receipts', '/api/v1/warehouse-tasks', '/api/v1/cycle-counts']) {
      const u = lastUrl(path)!
      expect(u.search).toContain(WH1.publicId)
      expect(u.searchParams.has('categoryIds')).toBe(false)
    }
    expect(screen.getByRole('combobox', { name: 'Almacén' })).toHaveValue(WH1.publicId)
    expect(screen.getByRole('button', { name: /Categoría o producto: Categoría Farmacia › Analgésicos/ })).toBeInTheDocument()
  })

  it('elegir un producto: saldo del producto, subtítulo con SKU y mínimo, "Sí" bajo mínimo y enlace al Kárdex; se recuerda', async () => {
    const user = userEvent.setup()
    mock.handler = (path: string, url: URL) =>
      path === '/api/v1/products' && url.searchParams.get('belowMin') !== 'true'
        ? { total: 1, skip: 0, take: 20, items: [PRODUCT] }
        : handler(path, url)
    renderPulse()
    await user.click(await screen.findByRole('button', { name: 'Categoría o producto' }))
    await user.type(screen.getByRole('combobox', { name: 'Buscar categoría o producto…' }), 'AN-01')
    await user.click(await screen.findByRole('option', { name: /AN-0100 · Analgésico 500 mg x 20/ }))

    const onHand = await tile('En mano')
    await waitFor(() => expect(within(onHand).getByText('312')).toBeInTheDocument())
    expect(within(onHand).getByText('AN-0100 · mínimo 400')).toBeInTheDocument()
    expect(within(await tile('Disponible')).getByText('22')).toBeInTheDocument()
    const below = await tile('Bajo mínimo')
    await waitFor(() => expect(within(below).getByText('Sí')).toBeInTheDocument())
    expect(within(below).getByRole('link', { name: 'Ver Kárdex de AN-0100 ›' })).toHaveAttribute(
      'href',
      `/warehouse/inventory?tab=kardex&product=${PRODUCT.publicId}`,
    )
    expect(lastUrl('/api/v1/inventory/balances')!.searchParams.getAll('productPublicIds')).toEqual([PRODUCT.publicId])
    expect(JSON.parse(window.localStorage.getItem(KEY)!)).toEqual({
      warehousePublicId: null,
      item: { kind: 'product', publicId: PRODUCT.publicId },
    })

    // ✕ vuelve a los totales generales y deja de recordarse
    await user.click(screen.getByRole('button', { name: 'Quitar categoría o producto' }))
    await waitFor(async () => expect(within(await tile('En mano')).getByText('9,000')).toBeInTheDocument())
    expect(window.localStorage.getItem(KEY)).toBeNull()
  })

  it('cambiar el almacén aplica a las seis tarjetas y se guarda', async () => {
    const user = userEvent.setup()
    renderPulse()
    const select = await screen.findByRole('combobox', { name: 'Almacén' })
    await waitFor(() => expect(within(select).getByRole('option', { name: 'ALM-02 · Almacén norte' })).toBeInTheDocument())
    expect(within(select).getAllByRole('option')[0]).toHaveTextContent('Todos los almacenes')
    await user.selectOptions(select, WH2.publicId)
    await waitFor(async () => expect(within(await tile('Recibos abiertos')).getByText('2')).toBeInTheDocument())
    expect(lastUrl('/api/v1/inventory/balances')!.searchParams.getAll('warehousePublicIds')).toEqual([WH2.publicId])
    expect(lastUrl('/api/v1/products')!.searchParams.get('warehousePublicId')).toBe(WH2.publicId)
    expect(JSON.parse(window.localStorage.getItem(KEY)!)).toEqual({ warehousePublicId: WH2.publicId, item: null })
  })

  it('lo guardado que ya no existe (almacén dado de baja, producto 404) se limpia sin error', async () => {
    window.localStorage.setItem(KEY, JSON.stringify({ warehousePublicId: 'wh-borrado', item: { kind: 'product', publicId: 'prod-borrado' } }))
    renderPulse()
    await waitFor(() => expect(window.localStorage.getItem(KEY)).toBeNull())
    await waitFor(async () => expect(within(await tile('En mano')).getByText('9,000')).toBeInTheDocument())
    expect(screen.getByRole('combobox', { name: 'Almacén' })).toHaveValue('')
    expect(screen.getByRole('button', { name: 'Categoría o producto' })).toBeInTheDocument()
    expect(screen.queryByText('No existe el producto.')).toBeNull()
    expect(onAccessDenied).not.toHaveBeenCalled()
  })
})
