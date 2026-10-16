// Lote 68 (kits) — pestaña «Kit» de la ficha del producto sobre un fetch simulado: producto normal (convertir, solo con inventory.manage y sin rastreo), kit con sus
// componentes y «se pueden armar N» por almacén, el cuerpo que se envía y el mensaje del servidor tal cual.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ProductDetailScreen from './ProductDetailScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], kit: null as unknown, tracking: 'NONE', put: null as null | (() => Response | unknown) }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const P = 'aaaaaaaa-0000-0000-0000-000000000001'
const A = 'bbbbbbbb-0000-0000-0000-000000000002'
const NOT_KIT = { productPublicId: P, sku: 'KIT-1', name: 'Kit uno', isKit: false, adjustable: false, components: [], availability: [] }
const IS_KIT = {
  productPublicId: P, sku: 'KIT-1', name: 'Kit uno', isKit: true, adjustable: true,
  components: [{ productPublicId: A, sku: 'TORN-1', name: 'Tornillo', quantity: 4, sortOrder: 1, isActive: true }],
  availability: [{ warehousePublicId: 'w1', warehouseCode: 'ALM-01', canBuild: 5 }, { warehousePublicId: 'w2', warehouseCode: 'ALM-02', canBuild: 0 }],
}

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'PUT' && mock.put) return mock.put()
  if (method === 'GET' && p === `/api/v1/products/${P}/kit`) return mock.kit ?? NOT_KIT
  if (method === 'GET' && p === `/api/v1/products/${P}`) {
    return { product: { id: 1, publicId: P, sku: 'KIT-1', name: 'Kit uno', trackingTypeCode: mock.tracking, qtyOnHand: 0, qtyAvailable: 0, isOwn: true, isActive: true, isKit: (mock.kit as { isKit?: boolean } | null)?.isKit === true }, rowVersion: 'RV==' }
  }
  if (method === 'GET' && p === '/api/v1/products') return { total: 1, skip: 0, take: 50, items: [{ id: 2, publicId: A, sku: 'TORN-1', name: 'Tornillo', isOwn: true, isActive: true }] }
  if (method === 'GET') return []
  return mock.kit ?? NOT_KIT
}

function wrap(permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[`/warehouse/products/${P}?tab=kit`]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/products/:publicId" element={<ProductDetailScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const MANAGE = ['inventory.view', 'inventory.manage']
const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ title, status, code }), { status, headers: { 'Content-Type': 'application/problem+json' } })

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.kit = null
  mock.tracking = 'NONE'
  mock.put = null
})

describe('Pestaña Kit: producto normal', () => {
  it('con inventory.manage ofrece convertirlo; sin él solo lo explica', async () => {
    wrap(MANAGE)
    expect(await screen.findByText('Este producto no es un kit')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Convertir en kit' })).toBeInTheDocument()
  })

  it('sin inventory.manage no hay botón', async () => {
    wrap(['inventory.view'])
    await screen.findByText('Este producto no es un kit')
    expect(screen.queryByRole('button', { name: 'Convertir en kit' })).toBeNull()
  })

  it('un producto con rastreo por lote o serie explica por qué no puede ser kit y no ofrece convertir', async () => {
    mock.tracking = 'LOT'
    wrap(MANAGE)
    expect(await screen.findByText('Un producto con rastreo por lote o serie no puede ser un kit.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Convertir en kit' })).toBeNull()
  })

  it('al convertir, valida en pantalla y manda isKit con los componentes', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Convertir en kit' }))
    await user.click(await screen.findByRole('button', { name: 'Guardar kit' }))
    expect(await screen.findByText('Escoja el producto de cada componente.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'PUT')).toBe(false)

    await user.click(screen.getByRole('combobox'))
    await user.click(await screen.findByText(/TORN-1/))
    await user.clear(screen.getByLabelText('Cantidad por kit'))
    await user.type(screen.getByLabelText('Cantidad por kit'), '4')
    mock.kit = IS_KIT
    await user.click(screen.getByRole('button', { name: 'Guardar kit' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(mock.calls.find((c) => c.method === 'PUT')?.body).toEqual({ isKit: true, adjustable: false, components: [{ productPublicId: A, quantity: 4 }] })
  })

  it('el mensaje del servidor se muestra tal cual', async () => {
    const user = userEvent.setup()
    mock.put = () => problem(409, 'KIT-1 tiene existencia: no se puede convertir en kit mientras tenga existencia o reservas.', 'conflict')
    wrap(MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Convertir en kit' }))
    await user.click(screen.getByRole('combobox'))
    await user.click(await screen.findByText(/TORN-1/))
    await user.click(screen.getByRole('button', { name: 'Guardar kit' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('KIT-1 tiene existencia: no se puede convertir en kit mientras tenga existencia o reservas.')
  })
})

describe('Pestaña Kit: un kit', () => {
  it('muestra los componentes, cuántos se pueden armar por almacén y las acciones', async () => {
    mock.kit = IS_KIT
    wrap(MANAGE)
    expect(await screen.findByText('TORN-1')).toBeInTheDocument()
    expect(screen.getByText('Ajustable al pedir')).toBeInTheDocument()
    expect(screen.getByText('Se pueden armar 5')).toBeInTheDocument()
    expect(screen.getByText('ALM-01: se pueden armar 5')).toBeInTheDocument()
    expect(screen.getByText('ALM-02: se pueden armar 0')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Editar componentes' })).toBeInTheDocument()
  })

  it('volver a producto normal pide confirmación y manda isKit=false', async () => {
    const user = userEvent.setup()
    mock.kit = IS_KIT
    wrap(MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Volver a producto normal' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Se quitan sus componentes')
    mock.kit = NOT_KIT
    await user.click(screen.getAllByRole('button', { name: 'Volver a producto normal' }).at(-1)!)
    await waitFor(() => expect(mock.calls.find((c) => c.method === 'PUT')?.body).toEqual({ isKit: false }))
  })
})
