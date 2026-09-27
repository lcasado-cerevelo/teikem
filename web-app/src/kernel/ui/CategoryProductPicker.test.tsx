import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { CategoryProductPicker, type CategoryOption } from './CategoryProductPicker'
import { categoryTree, filterCategoryTree, isCategoryProductValue, sameCategoryProduct, type CategoryProductValue } from './categoryTree'

// El cliente de la app se sustituye por uno con la misma política sobre un fetch simulado.
const mock = vi.hoisted(() => ({ requests: [] as URL[], handler: (_url: URL): unknown => [] }))
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    const body = mock.handler(url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const CATEGORIES: CategoryOption[] = [
  { id: 1, name: 'Farmacia', parentId: null, path: 'Farmacia', isActive: true, productCount: 23 },
  { id: 2, name: 'Analgésicos', parentId: 1, path: 'Farmacia › Analgésicos', isActive: true, productCount: 14 },
  { id: 3, name: 'Antibióticos', parentId: 1, path: 'Farmacia › Antibióticos', isActive: true, productCount: 9 },
  { id: 4, name: 'Ferretería', parentId: null, path: 'Ferretería', isActive: true, productCount: 1 },
]

const PRODUCTS = [
  { id: 10, publicId: 'aaaaaaaa-0000-0000-0000-000000000010', sku: 'AN-0100', name: 'Analgésico 500 mg x 20', isActive: true },
  { id: 11, publicId: 'aaaaaaaa-0000-0000-0000-000000000011', sku: 'AN-0140', name: 'Analgésico infantil jarabe', isActive: true },
  { id: 12, publicId: 'aaaaaaaa-0000-0000-0000-000000000012', sku: 'TR-0001', name: 'Tornillo', isActive: true },
]

function route(url: URL): unknown {
  if (url.pathname === '/api/v1/products') {
    const search = (url.searchParams.get('search') ?? '').toLowerCase()
    const items = PRODUCTS.filter((p) => `${p.sku} ${p.name}`.toLowerCase().includes(search))
    return { total: items.length, skip: 0, take: 20, items }
  }
  const detail = PRODUCTS.find((p) => url.pathname === `/api/v1/products/${p.publicId}`)
  if (detail) return { product: detail }
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function Harness({ initial = null, onChange }: { initial?: CategoryProductValue; onChange?: (v: CategoryProductValue) => void }) {
  const [value, setValue] = useState<CategoryProductValue>(initial)
  return (
    <CategoryProductPicker
      value={value}
      onChange={(v) => {
        setValue(v)
        onChange?.(v)
      }}
      categories={CATEGORIES}
    />
  )
}

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

const productRequests = () => mock.requests.filter((u) => u.pathname === '/api/v1/products')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.handler = route
})

describe('categoryTree', () => {
  it('aplana el árbol con su nivel (padre y luego hijos); un padre ausente o un ciclo no rompen el recorrido', () => {
    const rows = categoryTree(CATEGORIES)
    expect(rows.map((r) => [r.category.name, r.level])).toEqual([
      ['Farmacia', 0],
      ['Analgésicos', 1],
      ['Antibióticos', 1],
      ['Ferretería', 0],
    ])
    // padre inactivo (no está en la lista) → raíz
    expect(categoryTree([{ id: 5, name: 'Huérfana', parentId: 99 }]).map((r) => r.level)).toEqual([0])
    // ciclo 6 ↔ 7: ambos aparecen una sola vez
    expect(categoryTree([{ id: 6, name: 'A', parentId: 7 }, { id: 7, name: 'B', parentId: 6 }])).toHaveLength(2)
  })

  it('filtra por nombre o ruta sin acentos; valida la forma del valor guardado', () => {
    expect(filterCategoryTree(categoryTree(CATEGORIES), 'analgesicos').map((r) => r.category.id)).toEqual([2])
    expect(filterCategoryTree(categoryTree(CATEGORIES), 'farmacia').map((r) => r.category.id)).toEqual([1, 2, 3])
    expect(isCategoryProductValue(null)).toBe(true)
    expect(isCategoryProductValue({ kind: 'category', id: 3 })).toBe(true)
    expect(isCategoryProductValue({ kind: 'product', publicId: 'x' })).toBe(true)
    expect(isCategoryProductValue({ kind: 'category', id: '3' })).toBe(false)
    expect(isCategoryProductValue({ kind: 'product' })).toBe(false)
    expect(isCategoryProductValue('Farmacia')).toBe(false)
    expect(sameCategoryProduct({ kind: 'category', id: 1 }, { kind: 'category', id: 1 })).toBe(true)
    expect(sameCategoryProduct({ kind: 'category', id: 1 }, null)).toBe(false)
  })
})

describe('CategoryProductPicker', () => {
  it('muestra el árbol con sangría y conteo; sin texto no busca productos', async () => {
    const user = userEvent.setup()
    wrap(<Harness />)
    await user.click(screen.getByRole('button', { name: 'Categoría o producto' }))
    const listbox = screen.getByRole('listbox')
    const cats = within(within(listbox).getByRole('group', { name: 'Categorías' })).getAllByRole('option')
    expect(cats.map((o) => o.textContent)).toEqual(['▣Farmacia23 productos', '▣Analgésicos14 productos', '▣Antibióticos9 productos', '▣Ferretería1 producto'])
    expect(cats[1].style.paddingLeft).toBe('26px')
    expect(cats[0].style.paddingLeft).toBe('8px')
    expect(within(listbox).getByText('Escriba para buscar productos por SKU o nombre.')).toBeInTheDocument()
    expect(productRequests()).toEqual([])
  })

  it('busca productos (250 ms, activeOnly, take=20) y elige un producto: "SKU · Nombre" en la píldora', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<Harness onChange={onChange} />)
    await user.click(screen.getByRole('button', { name: 'Categoría o producto' }))
    await user.type(screen.getByRole('combobox', { name: 'Buscar categoría o producto…' }), 'anal')
    const option = await screen.findByRole('option', { name: /AN-0100 · Analgésico 500 mg x 20/ })
    const last = productRequests().at(-1)!
    expect(last.searchParams.get('search')).toBe('anal')
    expect(last.searchParams.get('activeOnly')).toBe('true')
    expect(last.searchParams.get('take')).toBe('20')
    // la búsqueda no se dispara por cada tecla
    expect(productRequests().every((u) => u.searchParams.get('search') === 'anal')).toBe(true)
    // la sección Categorías también filtra con el texto
    expect(within(screen.getByRole('group', { name: 'Categorías' })).getAllByRole('option')).toHaveLength(1)
    expect(screen.queryByRole('option', { name: /Tornillo/ })).toBeNull()

    await user.click(option)
    expect(onChange).toHaveBeenLastCalledWith({ kind: 'product', publicId: PRODUCTS[0].publicId })
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(screen.getByRole('button', { name: 'Categoría o producto: Producto AN-0100 · Analgésico 500 mg x 20' })).toBeInTheDocument()
  })

  it('teclado: ↓ y Enter eligen una categoría; Escape cierra y devuelve el foco', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<Harness onChange={onChange} />)
    const trigger = screen.getByRole('button', { name: 'Categoría o producto' })
    await user.click(trigger)
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(trigger).toHaveFocus()

    await user.click(trigger)
    const input = screen.getByRole('combobox', { name: 'Buscar categoría o producto…' })
    expect(input).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(input.getAttribute('aria-activedescendant')).toBe(screen.getAllByRole('option')[1].id)
    await user.keyboard('{Enter}')
    expect(onChange).toHaveBeenLastCalledWith({ kind: 'category', id: 2 })
    expect(screen.getByRole('button', { name: 'Categoría o producto: Categoría Farmacia › Analgésicos' })).toBeInTheDocument()
  })

  it('✕ limpia el valor (vuelve a "Todos"); un producto guardado pide su ficha para la etiqueta', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<Harness initial={{ kind: 'product', publicId: PRODUCTS[1].publicId }} onChange={onChange} />)
    expect(await screen.findByText('AN-0140 · Analgésico infantil jarabe')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Quitar categoría o producto' }))
    expect(onChange).toHaveBeenLastCalledWith(null)
    expect(screen.getByText('Todos')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Quitar categoría o producto' })).toBeNull()
  })

  it('sin permiso para productos: aviso en la sección, sin sacar de la pantalla', async () => {
    mock.handler = (url) =>
      url.pathname === '/api/v1/products'
        ? new Response(JSON.stringify({ status: 403, code: 'forbidden', title: 'Sin permiso.' }), {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        : route(url)
    const user = userEvent.setup()
    wrap(<Harness />)
    await user.click(screen.getByRole('button', { name: 'Categoría o producto' }))
    await user.type(screen.getByRole('combobox'), 'tor')
    await waitFor(() => expect(screen.getByText('Su usuario no puede consultar productos.')).toBeInTheDocument())
    // las categorías siguen disponibles (el texto no coincide con ninguna)
    expect(screen.getByText('No hay categorías que coincidan.')).toBeInTheDocument()
  })
})
