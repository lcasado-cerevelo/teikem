// Productos e inventario (Fase 8, maqueta `inventario()`): un solo ítem con el catálogo y sus existencias. Columnas y
// estados de la maqueta, río de KPIs (SKUs activos, Unidades totales, Bajo mínimo, Con número de serie), filtros que van
// al API (y vuelven a la página 1), botones de reporte hacia el Kárdex de movimientos y clic en la fila = "Editar
// producto". Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ProductListScreen from './ProductListScreen'

const mock = vi.hoisted(() => ({ requests: [] as URL[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    const body = route(url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'

const ROWS = [
  {
    id: 1,
    publicId: P1,
    sku: 'GLU-100',
    name: 'Medidor de glucosa',
    categoryName: 'Médico',
    isOwn: true,
    trackingTypeCode: 'SERIAL',
    qtyOnHand: 40,
    qtyReserved: 0,
    qtyAvailable: 40,
    isBelowMin: false,
    isActive: true,
  },
  {
    id: 2,
    publicId: 'aaaaaaaa-0000-0000-0000-000000000002',
    sku: 'GLU-STR',
    name: 'Tiras reactivas',
    categoryName: 'Médico',
    isOwn: false,
    ownerName: 'Farmacia Central',
    trackingTypeCode: 'LOT',
    qtyOnHand: 180,
    qtyReserved: 30,
    qtyAvailable: 150,
    isBelowMin: true,
    isActive: true,
  },
  {
    id: 3,
    publicId: 'aaaaaaaa-0000-0000-0000-000000000003',
    sku: 'OLD-01',
    name: 'Producto viejo',
    isOwn: true,
    trackingTypeCode: 'NONE',
    qtyOnHand: 0,
    qtyReserved: 0,
    qtyAvailable: 0,
    isBelowMin: false,
    isActive: false,
  },
]

/** Catálogo "completo" que lee el conteo de productos con serie (3 SERIAL de 250 activos, en dos páginas de 200). */
function serialPage(skip: number) {
  const n = Math.max(0, Math.min(200, 250 - skip))
  return {
    total: 250,
    skip,
    take: 200,
    items: Array.from({ length: n }, (_, k) => ({ id: skip + k, trackingTypeCode: skip + k < 3 ? 'SERIAL' : 'NONE', isActive: true })),
  }
}

function route(url: URL): unknown {
  const p = url.pathname
  const q = url.searchParams
  if (p === '/api/v1/catalogs/TrackingType')
    return [
      { code: 'NONE', label: 'Ninguno', sortOrder: 1 },
      { code: 'LOT', label: 'Lote', sortOrder: 2 },
      { code: 'SERIAL', label: 'Serie', sortOrder: 3 },
    ]
  if (p === '/api/v1/products') {
    if (q.get('take') === '1' && q.get('belowMin') === 'true') return { total: 2, skip: 0, take: 1, items: [] }
    if (q.get('take') === '1' && q.get('activeOnly') === 'true') return { total: 250, skip: 0, take: 1, items: [] }
    if (q.get('take') === '200') return serialPage(Number(q.get('skip') ?? 0))
    return { total: 60, skip: Number(q.get('skip') ?? 0), take: 25, items: ROWS }
  }
  if (p === '/api/v1/inventory/balances') return { total: 12, skip: 0, take: 1, totalOnHand: 1250, totalAvailable: 1100, items: [] }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === '/api/v1/product-categories') return [{ id: 3, name: 'Médico', path: 'Médico', isActive: true, productCount: 2 }]
  if (p === `/api/v1/products/${P1}`) return { product: ROWS[0], hasMovements: true, rowVersion: 'AAAAAAAAB9E=' }
  return []
}

function wrap(url = '/warehouse/products', permissions = ['inventory.view']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[url]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <ProductListScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Última consulta de la lista paginada de la tabla (take=25). */
function lastListQuery(): URLSearchParams {
  const hits = mock.requests.filter((u) => u.pathname === '/api/v1/products' && u.searchParams.get('take') === '25')
  return hits[hits.length - 1].searchParams
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
})

describe('ProductListScreen · Productos e inventario', () => {
  it('título de la maqueta y columnas exactas en su orden', async () => {
    wrap()
    expect(await screen.findByRole('heading', { level: 1, name: 'Productos e inventario' })).toBeInTheDocument()
    const table = await screen.findByRole('table', { name: 'Productos e inventario' })
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((th) => th.textContent?.replace(/[▲▼]/g, '').trim())
    expect(headers).toEqual(['SKU', 'Producto', 'Categoría', 'Dueño', 'Disponible', 'Reservado', 'Total', 'Rastreo', 'Estado'])
  })

  it('cada fila: dueño "Propio" o el cliente, disponible/reservado/total del API, rastreo del catálogo y estado', async () => {
    wrap()
    const table = await screen.findByRole('table', { name: 'Productos e inventario' })
    const cells = (sku: string) =>
      within(within(table).getByText(sku).closest('tr')!)
        .getAllByRole('cell')
        .map((td) => td.textContent)
    await waitFor(() => expect(cells('GLU-100')[7]).toBe('Serie'))
    expect(cells('GLU-100')).toEqual(['GLU-100', 'Medidor de glucosa', 'Médico', 'Propio', '40', '—', '40', 'Serie', 'OK'])
    expect(cells('GLU-STR')).toEqual(['GLU-STR', 'Tiras reactivas', 'Médico', 'Farmacia Central', '150', '30', '180', 'Lote', 'Bajo mínimo'])
    expect(cells('OLD-01')[8]).toBe('Inactivo')
    // el inactivo se atenúa como en la maqueta
    expect(within(table).getByText('OLD-01').closest('tr')).toHaveClass('dim')
    expect(within(table).getByText('GLU-100').closest('tr')).not.toHaveClass('dim')
  })

  it('río de KPIs de todo el catálogo: SKUs activos, Unidades totales, Bajo mínimo y Con número de serie', async () => {
    wrap()
    const river = await screen.findByRole('group', { name: 'Resumen del catálogo' })
    await waitFor(() => expect(within(river).getByRole('group', { name: 'Con número de serie: 3' })).toBeInTheDocument())
    expect(within(river).getByRole('group', { name: 'SKUs activos: 250' })).toBeInTheDocument()
    expect(within(river).getByRole('group', { name: 'Unidades totales: 1,250' })).toBeInTheDocument()
    expect(within(river).getByRole('group', { name: 'Bajo mínimo: 2' })).toBeInTheDocument()
    // consultas acotadas (take=1) y el conteo de series recorre las páginas de 200
    const products = mock.requests.filter((u) => u.pathname === '/api/v1/products').map((u) => u.search)
    expect(products).toContain('?activeOnly=true&take=1')
    expect(products).toContain('?belowMin=true&take=1')
    expect(products).toContain('?activeOnly=true&skip=0&take=200')
    expect(products).toContain('?activeOnly=true&skip=200&take=200')
    expect(mock.requests.some((u) => u.pathname === '/api/v1/inventory/balances' && u.search === '?includeZero=false&take=1')).toBe(true)
  })

  it('los filtros van al API y vuelven a la página 1: Estado "Bajo mínimo" → belowMin, Almacén → warehousePublicId', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Página siguiente' }))
    await waitFor(() => expect(lastListQuery().get('skip')).toBe('25'))
    // por defecto muestra activos e inactivos (como la maqueta)
    expect(lastListQuery().has('activeOnly')).toBe(false)

    await user.selectOptions(screen.getByLabelText('Estado'), 'Bajo mínimo')
    await waitFor(() => expect(lastListQuery().get('belowMin')).toBe('true'))
    expect(lastListQuery().get('skip')).toBe('0')

    await user.selectOptions(screen.getByLabelText('Almacén'), 'ALM-01 · Almacén principal')
    await waitFor(() => expect(lastListQuery().get('warehousePublicId')).toBe(WH))

    await user.selectOptions(screen.getByLabelText('Estado'), 'Activos')
    await waitFor(() => expect(lastListQuery().get('activeOnly')).toBe('true'))
    expect(lastListQuery().has('belowMin')).toBe(false)
  })

  it('los reportes llevan al Kárdex de movimientos con los filtros de aquí (Saldos / tipo Ajuste)', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByRole('link', { name: 'Reporte de inventario' })).toHaveAttribute('href', '/warehouse/kardex?tab=balances')
    expect(screen.getByRole('link', { name: 'Reporte de ajustes' })).toHaveAttribute('href', '/warehouse/kardex?types=ADJUSTMENT')
    await user.selectOptions(await screen.findByLabelText('Almacén'), 'ALM-01 · Almacén principal')
    expect(screen.getByRole('link', { name: 'Reporte de inventario' })).toHaveAttribute('href', `/warehouse/kardex?tab=balances&warehousePublicIds=${WH}`)
    expect(screen.getByRole('link', { name: 'Reporte de ajustes' })).toHaveAttribute('href', `/warehouse/kardex?types=ADJUSTMENT&warehousePublicIds=${WH}`)
  })

  it('"Nuevo producto" solo con inventory.manage; clic en la fila pide la ficha para "Editar producto"', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('table', { name: 'Productos e inventario' })
    expect(screen.queryByRole('button', { name: 'Nuevo producto' })).toBeNull()
    await user.click(screen.getByText('GLU-100'))
    await waitFor(() => expect(mock.requests.some((u) => u.pathname === `/api/v1/products/${P1}`)).toBe(true))
  })

  it('con inventory.manage aparece "Nuevo producto"; la pestaña Categorías va en ?tab=categories', async () => {
    wrap('/warehouse/products?tab=categories', ['inventory.view', 'inventory.manage'])
    expect(await screen.findByRole('tab', { name: 'Categorías' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('group', { name: 'Resumen del catálogo' })).toBeNull()
    await userEvent.setup().click(screen.getByRole('tab', { name: 'Productos' }))
    expect(await screen.findByRole('button', { name: 'Nuevo producto' })).toBeInTheDocument()
  })
})
