// Lote 235 — la ventana «Nueva orden» con el almacén de la orden y lo disponible, sobre un fetch simulado: con un solo almacén no se muestra el campo; con varios
// viene ya escogido con el principal (y viaja al guardar); cada línea dice «Disponible: N», marca el backorder si se pide de más (no bloquea el guardado) y
// muestra la nota de otros almacenes.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { OrderModal } from './OrderModal'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; body: unknown }[],
  stock: null as unknown,
}))

const CLIENT = '33333333-0000-0000-0000-000000000003'
const PROD = '44444444-0000-0000-0000-000000000004'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, body: text ? JSON.parse(text) : undefined })
    const p = url.pathname
    const m = req.method
    if (m === 'GET' && p === '/api/v1/sales-orders/stock') return json(mock.stock)
    if (m === 'POST' && p === '/api/v1/sales-orders') return json({ publicId: 'o-1', number: 'OV-00001', status: 'DRAFT', lines: [] })
    if (m === 'GET' && p === '/api/v1/clients') return json([{ id: 1, publicId: '33333333-0000-0000-0000-000000000003', code: 'FARM', name: 'Farmacia Central', isActive: true }])
    if (m === 'GET' && p === '/api/v1/products') return json({ items: [{ id: 1, publicId: '44444444-0000-0000-0000-000000000004', sku: 'MED', name: 'Medidor', isActive: true, isOwn: true, salePrice: 12.5 }], total: 1, skip: 0, take: 50 })
    if (m === 'GET' && p === '/api/v1/sales-orders/catalog') return json({ brands: [] })
    if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return json([])
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const W1 = { publicId: 'w-1', code: 'ALM-01', name: 'Bayamón', isMain: false }
const W2 = { publicId: 'w-2', code: 'ALM-02', name: 'Ponce', isMain: true }
const stockOf = (...warehouses: { publicId: string }[]) => ({
  warehouses,
  products: [{ productPublicId: PROD, stock: [{ warehousePublicId: 'w-1', available: 12 }, { warehousePublicId: 'w-2', available: 5 }].filter((s) => warehouses.some((w) => w.publicId === s.warehousePublicId)) }],
})

const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(['sales.view', 'sales.create', 'clients.read']),
  modules: new Set(['SALES_ORDERS', 'CATALOG']),
  reloadMe: async () => {},
} as unknown as Session

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={SESSION.permissions} modules={SESSION.modules}>
          <MemoryRouter>
            <OrderModal publicId={null} onClose={() => {}} />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

async function pickProduct(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
  await user.click(within(dialog).getByRole('combobox', { name: 'Producto de la línea 1' }))
  await user.click(await within(dialog).findByRole('option', { name: /MED/ }))
}

describe('OrderModal — Lote 235: almacén de la orden y disponible', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.stock = stockOf(W1, W2)
  })

  it('con un solo almacén activo no se muestra el campo Almacén', async () => {
    mock.stock = stockOf(W2)
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden' })
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/sales-orders/stock')).toBe(true))
    await waitFor(() => expect(within(dialog).queryByLabelText('Almacén')).toBeNull())
  })

  it('con varios, el campo viene ya escogido con el principal y cada línea dice lo disponible y la nota del otro almacén', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden' })
    const field = await within(dialog).findByRole('combobox', { name: 'Almacén' })
    expect(field).toHaveValue('w-2')
    await pickProduct(user, dialog)
    expect(await within(dialog).findByText('Disponible: 5')).toBeInTheDocument()
    expect(within(dialog).getByText('Hay disponible en Bayamón: 12')).toBeInTheDocument()
    // al cambiar de almacén cambia el disponible (y la nota apunta al otro)
    await user.selectOptions(field, 'w-1')
    expect(within(dialog).getByText('Disponible: 12')).toBeInTheDocument()
    expect(within(dialog).getByText('Hay disponible en Ponce: 5')).toBeInTheDocument()
  })

  it('pedir más de lo disponible se marca «Backorder: N» y NO impide guardar; la orden viaja con el almacén principal', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden' })
    await within(dialog).findByRole('combobox', { name: 'Almacén' })
    await user.click(within(dialog).getByLabelText('Cliente que compra'))
    await user.click(await screen.findByText(/Farmacia Central/))
    await pickProduct(user, dialog)
    await user.type(within(dialog).getByLabelText('Cantidad de la línea 1'), '8')
    expect(within(dialog).getByText('Backorder: 3')).toBeInTheDocument()
    expect(within(dialog).getByText(/Solo hay 5 disponibles en Ponce; 3 quedan en backorder\./)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar borrador' }))
    await waitFor(() => expect(mock.requests.filter((r) => r.method === 'POST' && r.path === '/api/v1/sales-orders')).toHaveLength(1))
    const body = mock.requests.find((r) => r.method === 'POST' && r.path === '/api/v1/sales-orders')!.body as { warehousePublicId?: string; clientPublicId?: string; lines: { quantity: number }[] }
    expect(body).toMatchObject({ warehousePublicId: 'w-2', clientPublicId: CLIENT })
    expect(body.lines[0].quantity).toBe(8)
  })
})
