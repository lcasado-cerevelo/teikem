import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ProductMultiFilter, ProductPicker, WarehousePicker, type ProductFilterItem } from './pickers'

// El cliente de la app se sustituye por uno con la misma política sobre un fetch simulado.
const mock = vi.hoisted(() => ({ requests: [] as URL[], handler: (_url: URL): unknown => [] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    const body = mock.handler(url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WAREHOUSES = [
  { id: 1, publicId: '11111111-1111-1111-1111-111111111111', code: 'ALM-01', name: 'Almacén principal', isActive: true },
  { id: 2, publicId: '22222222-2222-2222-2222-222222222222', code: 'ALM-02', name: 'Almacén norte', isActive: true },
]
const OLD_WAREHOUSE = { id: 3, publicId: '33333333-3333-3333-3333-333333333333', code: 'ALM-99', name: 'Almacén cerrado', isActive: false }

const PRODUCTS = [
  { id: 1, publicId: 'aaaaaaaa-0000-0000-0000-000000000001', sku: 'TORN-01', name: 'Tornillo', isOwn: true, isActive: true, trackingTypeCode: 'NONE' },
  { id: 2, publicId: 'aaaaaaaa-0000-0000-0000-000000000002', sku: 'TUER-01', name: 'Tuerca', isOwn: false, ownerName: 'Acme', isActive: true, trackingTypeCode: 'LOT' },
  { id: 3, publicId: 'aaaaaaaa-0000-0000-0000-000000000003', sku: 'CLAV-01', name: 'Clavo', isOwn: true, isActive: true, trackingTypeCode: 'SERIAL' },
]

function route(url: URL): unknown {
  if (url.pathname === '/api/v1/warehouses') return WAREHOUSES
  if (url.pathname === `/api/v1/warehouses/${OLD_WAREHOUSE.publicId}`) return { warehouse: OLD_WAREHOUSE, zones: [], docks: [] }
  if (url.pathname === '/api/v1/products') {
    const search = (url.searchParams.get('search') ?? '').toLowerCase()
    const items = PRODUCTS.filter((p) => `${p.sku} ${p.name}`.toLowerCase().includes(search))
    return { total: items.length, skip: 0, take: 50, items }
  }
  const detail = PRODUCTS.find((p) => url.pathname === `/api/v1/products/${p.publicId}`)
  if (detail) return { product: detail, hasMovements: false }
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.handler = route
})

describe('WarehousePicker', () => {
  it('lista almacenes activos (includeInactive=false) como "Code · Name" y devuelve el publicId', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<WarehousePicker value={null} onChange={onChange} aria-label="Almacén" />)
    const select = screen.getByRole('combobox', { name: 'Almacén' })
    await screen.findByRole('option', { name: 'ALM-02 · Almacén norte' })
    expect(mock.requests.find((u) => u.pathname === '/api/v1/warehouses')?.searchParams.get('includeInactive')).toBe('false')
    expect(screen.getByRole('option', { name: 'Seleccione un almacén…' })).toBeInTheDocument()
    await user.selectOptions(select, WAREHOUSES[1].publicId)
    expect(onChange).toHaveBeenCalledWith(WAREHOUSES[1].publicId, expect.objectContaining({ code: 'ALM-02' }))
    await user.selectOptions(select, '')
    expect(onChange).toHaveBeenLastCalledWith(null, null)
  })

  it('un valor que ya no está activo se conserva con su etiqueta (ficha del almacén)', async () => {
    wrap(<WarehousePicker value={OLD_WAREHOUSE.publicId} onChange={vi.fn()} aria-label="Almacén" />)
    expect(await screen.findByRole('option', { name: 'ALM-99 · Almacén cerrado' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Almacén' })).toHaveValue(OLD_WAREHOUSE.publicId)
  })

  it('sin acceso al módulo: aviso en la lista, sin sacar de la pantalla', async () => {
    mock.handler = () =>
      new Response(JSON.stringify({ status: 403, code: 'module_disabled', title: 'Módulo apagado' }), {
        status: 403,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    wrap(<WarehousePicker value={null} onChange={vi.fn()} aria-label="Almacén" />)
    expect(await screen.findByRole('option', { name: 'Su usuario no puede consultar almacenes.' })).toBeInTheDocument()
  })
})

describe('ProductPicker', () => {
  it('busca con search= y activeOnly=true, muestra "SKU · Nombre" y devuelve el publicId con la fila', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<ProductPicker value={null} onChange={onChange} aria-label="Producto" />)
    await user.type(screen.getByRole('combobox', { name: 'Producto' }), 'tu')
    await waitFor(() => expect(mock.requests.some((u) => u.searchParams.get('search') === 'tu')).toBe(true))
    await waitFor(() => expect(screen.queryByText('Tornillo')).toBeNull())
    const last = mock.requests.filter((u) => u.pathname === '/api/v1/products').at(-1)
    expect(last?.searchParams.get('activeOnly')).toBe('true')
    const option = screen.getByRole('option', { name: /TUER-01 · Tuerca/ })
    expect(option).toHaveTextContent('Acme') // dueño cliente visible
    await user.click(option)
    expect(onChange).toHaveBeenCalledWith(PRODUCTS[1].publicId, expect.objectContaining({ trackingTypeCode: 'LOT' }))
  })

  it('ownOnly se envía al API', async () => {
    const user = userEvent.setup()
    wrap(<ProductPicker value={null} onChange={vi.fn()} ownOnly aria-label="Producto" />)
    await user.click(screen.getByRole('combobox', { name: 'Producto' }))
    await waitFor(() => expect(mock.requests.some((u) => u.pathname === '/api/v1/products' && u.searchParams.get('ownOnly') === 'true')).toBe(true))
  })

  it('con un valor inicial pide la ficha para mostrar "SKU · Nombre"', async () => {
    wrap(<ProductPicker value={PRODUCTS[2].publicId} onChange={vi.fn()} aria-label="Producto" />)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Producto' })).toHaveValue('CLAV-01 · Clavo'))
  })
})

describe('ProductMultiFilter', () => {
  function Harness({ onChange }: { onChange: (v: ProductFilterItem[]) => void }) {
    const [value, setValue] = useState<ProductFilterItem[]>([])
    return (
      <ProductMultiFilter
        label="Producto"
        value={value}
        onChange={(v) => {
          setValue(v)
          onChange(v)
        }}
      />
    )
  }

  it('busca en el API por SKU o nombre (no en una página fija), agrega una píldora por producto y la quita', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<Harness onChange={onChange} />)
    await user.type(screen.getByRole('combobox', { name: 'Producto' }), 'clavo')
    await waitFor(() => expect(mock.requests.some((u) => u.pathname === '/api/v1/products' && u.searchParams.get('search') === 'clavo')).toBe(true))
    await user.click(await screen.findByRole('option', { name: /CLAV-01 · Clavo/ }))
    expect(onChange).toHaveBeenLastCalledWith([{ publicId: PRODUCTS[2].publicId, sku: 'CLAV-01', label: 'CLAV-01 · Clavo' }])
    // el buscador queda vacío para agregar otro
    expect(screen.getByRole('combobox', { name: 'Producto' })).toHaveValue('')
    await user.click(screen.getByRole('button', { name: 'Quitar CLAV-01' }))
    expect(onChange).toHaveBeenLastCalledWith([])
  })

  it('por defecto busca solo activos; con includeInactive (Kárdex) omite activeOnly y marca los dados de baja', async () => {
    const user = userEvent.setup()
    const OLD = { id: 9, publicId: 'aaaaaaaa-0000-0000-0000-000000000009', sku: 'VIEJ-01', name: 'Viejo', isOwn: true, isActive: false, trackingTypeCode: 'NONE' }
    mock.handler = (url) => {
      if (url.pathname !== '/api/v1/products') return route(url)
      const items = url.searchParams.get('activeOnly') === 'true' ? PRODUCTS : [...PRODUCTS, OLD]
      return { total: items.length, skip: 0, take: 50, items }
    }
    const { unmount } = wrap(<ProductMultiFilter label="Producto" value={[]} onChange={() => {}} />)
    await user.click(screen.getByRole('combobox', { name: 'Producto' }))
    await screen.findByRole('option', { name: /TORN-01/ })
    expect(mock.requests.filter((u) => u.pathname === '/api/v1/products').every((u) => u.searchParams.get('activeOnly') === 'true')).toBe(true)
    expect(screen.queryByRole('option', { name: /VIEJ-01/ })).toBeNull()
    unmount()

    mock.requests = []
    wrap(<ProductMultiFilter label="Producto" value={[]} onChange={() => {}} includeInactive />)
    await user.click(screen.getByRole('combobox', { name: 'Producto' }))
    const option = await screen.findByRole('option', { name: /VIEJ-01/ })
    expect(option).toHaveTextContent('Inactivo')
    expect(mock.requests.filter((u) => u.pathname === '/api/v1/products').every((u) => u.searchParams.get('activeOnly') !== 'true')).toBe(true)
  })

  it('la píldora recorta un SKU largo con elipsis (no desborda a 360 px)', () => {
    const sku = 'X'.repeat(60)
    wrap(<ProductMultiFilter label="Producto" value={[{ publicId: 'p1', sku, label: `${sku} · Largo` }]} onChange={() => {}} />)
    const text = screen.getByText(sku)
    expect(text).toHaveClass('pfilter-text')
    expect(text.closest('.chip')?.parentElement).toHaveClass('pfilter-chips')
  })
})
