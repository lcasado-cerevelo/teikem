// Lote 221 (P1) — pestaña «Precios y ofertas» sobre un fetch simulado: listas con su estado calculado, ofertas como tarjetas, permisos (sales.view ve, sales.pricelist edita),
// validación antes de enviar, el cuerpo de crear y el mensaje del servidor tal cual al publicar.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { PriceListsTab } from './PriceListsTab'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], publish: null as null | (() => Response) }))
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

const L1 = 'cccccccc-0000-0000-0000-000000000001'
const L2 = 'cccccccc-0000-0000-0000-000000000002'
const LIST = (publicId: string, name: string, state: string, extra: object = {}) => ({
  publicId, name, validFrom: '2026-11-01', validTo: null, isPublished: state !== 'DRAFT', state, appliesToAll: true, itemCount: 2, clientCount: 0, isActive: true, createdAtUtc: '2026-11-01T00:00:00', ...extra,
})
const DETAIL = (publicId: string, name: string, state: string) => ({
  publicId, name, validFrom: '2026-11-01', validTo: null, isPublished: state !== 'DRAFT', state, appliesToAll: true, notes: null, isActive: true, clients: [], createdAtUtc: '2026-11-01T00:00:00',
  items: [{ id: 7, productPublicId: 'p1', sku: 'KIT-1', productName: 'Kit uno', isKit: true, isPromotion: true, price: 9.5, productSalePrice: 12, productIsActive: true }],
})
const OFFER = { productPublicId: 'p1', sku: 'KIT-1', name: 'Kit uno', brand: 'Acme', isKit: true, isActive: true, isPromotion: true, promoFrom: '2026-11-01', promoTo: '2026-11-30', inEffect: true, salePrice: 12 }

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/price-lists') return [LIST(L1, 'Base 2026', 'ACTIVE'), LIST(L2, 'Navidad', 'DRAFT')]
  if (method === 'GET' && p === '/api/v1/price-lists/offers') return [OFFER]
  if (method === 'GET' && p === `/api/v1/price-lists/${L2}`) return DETAIL(L2, 'Navidad', 'DRAFT')
  if (method === 'POST' && p === '/api/v1/price-lists') return DETAIL(L2, 'Navidad', 'DRAFT')
  if (method === 'POST' && p === `/api/v1/price-lists/${L2}/publish`) return mock.publish ? mock.publish() : DETAIL(L2, 'Navidad', 'ACTIVE')
  return []
}

function wrap(permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <PriceListsTab />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ title, status, code }), { status, headers: { 'Content-Type': 'application/problem+json' } })

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.publish = null
})

describe('Precios y ofertas', () => {
  it('muestra las listas con su estado y las ofertas como tarjetas', async () => {
    wrap(['sales.view'])
    const table = await screen.findByRole('table', { name: 'Listas de precios' })
    expect(await within(table).findByText('Base 2026')).toBeInTheDocument()
    expect(within(table).getByText('Vigente')).toBeInTheDocument()
    expect(within(table).getByText('Borrador')).toBeInTheDocument()
    const card = await screen.findByRole('article', { name: /KIT-1/ })
    expect(within(card).getByText('Kit uno')).toBeInTheDocument()
    expect(within(card).getByText('En vigor')).toBeInTheDocument()
  })

  it('sin sales.pricelist no hay acciones de edición', async () => {
    wrap(['sales.view'])
    await screen.findByText('Base 2026')
    expect(screen.queryByRole('button', { name: 'Nueva lista de precios' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Nueva oferta' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Editar oferta' })).toBeNull()
  })

  it('Nueva oferta: exige escoger un producto antes de continuar', async () => {
    const user = userEvent.setup()
    wrap(['sales.view', 'sales.pricelist'])
    await screen.findByText('Base 2026')
    await user.click(screen.getByRole('button', { name: 'Nueva oferta' }))
    await user.click(await screen.findByRole('button', { name: 'Continuar' }))
    expect(await screen.findByText('Escoge un producto.')).toBeInTheDocument()
  })

  it('con sales.pricelist: valida el nombre y manda el cuerpo de crear', async () => {
    const user = userEvent.setup()
    wrap(['sales.view', 'sales.pricelist'])
    await screen.findByText('Base 2026')
    await user.click(screen.getByRole('button', { name: 'Nueva lista de precios' }))
    await user.click(await screen.findByRole('button', { name: 'Crear lista' }))
    expect(await screen.findByText('Indique el nombre de la lista de precios.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)

    await user.type(screen.getByLabelText('Nombre'), '  Navidad ')
    await user.click(screen.getByRole('button', { name: 'Crear lista' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST')).toBe(true))
    expect(mock.calls.find((c) => c.method === 'POST')?.body).toMatchObject({ name: 'Navidad', clearValidTo: true, appliesToAll: true, validTo: null })
  })

  it('publicar: el mensaje del servidor se muestra tal cual', async () => {
    const user = userEvent.setup()
    mock.publish = () => problem(409, 'KIT-1 ya tiene precio en «Base 2026» en esas fechas.', 'conflict')
    wrap(['sales.view', 'sales.pricelist'])
    await user.click(await screen.findByText('Navidad'))
    await user.click(await screen.findByRole('button', { name: 'Publicar' }))
    expect(await screen.findByText(/KIT-1 ya tiene precio en «Base 2026» en esas fechas\./)).toBeInTheDocument()
  })
})
