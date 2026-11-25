// Lote 245 — la cadena en las pantallas que ya existían: el panel «Cadena» de la ficha del cliente (cambiar y quitar), el filtro y el chip de Clientes, el filtro y la
// columna de Órdenes de venta (consulta con `chainPublicId` repetido) y la pestaña «Por cadena» del Análisis de ventas con un renglón expandido.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ClientListScreen from '../clients/ClientListScreen'
import type { ClientDetail } from '../clients/clientRules'
import { EMPTY_ORDERS_FILTERS, hasOrdersFilters, ordersApiFilters } from '../sales/ordersFilters'
import { salesOrderListQuery } from '../sales/salesApi'
import { ChainSalesTab } from './ChainSalesTab'
import { ClientChainPanel } from './ClientChainPanel'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
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

const notFound = () => new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found', status: 404 }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } })

const CHAINS = [
  { publicId: 'ch-1', name: 'Walgreens', notes: null, isActive: true, clientCount: 2 },
  { publicId: 'ch-2', name: 'CVS', notes: null, isActive: true, clientCount: 1 },
]
const SALES = {
  month: '2026-10',
  compareMonth: '2026-09',
  cutoffDate: null,
  rows: [
    {
      chainPublicId: 'ch-1',
      chainName: 'Walgreens',
      isActive: true,
      clientCount: 2,
      clientsWithSales: 2,
      compareSales: 100,
      sales: 150,
      change: 50,
      changePct: 0.5,
      shareOfNet: 0.75,
      clients: [
        { clientPublicId: 'c-1', clientName: 'Walgreens 101', compareSales: 60, sales: 100, change: 40, changePct: 0.66, shareOfChain: 0.66 },
        { clientPublicId: 'c-2', clientName: 'Walgreens 102', compareSales: 40, sales: 50, change: 10, changePct: 0.25, shareOfChain: 0.34 },
      ],
    },
  ],
  noChain: { chainPublicId: null, chainName: '', isActive: true, clientCount: 1, clientsWithSales: 1, compareSales: 40, sales: 50, change: 10, changePct: 0.25, shareOfNet: 0.25, clients: [] },
  totals: { compareSales: 140, sales: 200, change: 60, changePct: 0.43 },
  reconciliation: { chainsTotal: 150, noChain: 50, monthNet: 200, difference: 0 },
}

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/client-chains' && method === 'GET') return CHAINS
  if (p === '/api/v1/client-chains/by-client/c-1' && method === 'PUT') return { clientPublicId: 'c-1', chainPublicId: 'ch-2', chainName: 'CVS', chainIsActive: true }
  if (p === '/api/v1/sales-analysis/by-chain') return SALES
  if (p === '/api/v1/clients' && method === 'GET') return [{ id: 1, publicId: 'c-1', code: 'W1', name: 'Walgreens 101', status: 'ACTIVE', statusLabel: 'Activo', isActive: true, billingSummary: '', contractsCount: 0, chainPublicId: 'ch-1', chainName: 'Walgreens' }]
  return notFound()
}

const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

function wrap(ui: React.ReactNode, permissions: string[]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['SALES_ORDERS', 'CATALOG']}>
          {ui}
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  window.localStorage.clear()
})

const CLIENT = { id: 1, publicId: 'c-1', name: 'Walgreens 101', chainPublicId: 'ch-1', chainName: 'Walgreens', chainIsActive: true } as ClientDetail

describe('Cadena en la ficha del cliente', () => {
  it('con sales.chain cambia la cadena (PUT by-client)', async () => {
    const user = userEvent.setup()
    wrap(<ClientChainPanel client={CLIENT} />, ['sales.chain', 'clients.read'])
    const combo = await screen.findByRole('combobox', { name: 'Cadena' })
    await waitFor(() => expect(within(combo).getByRole('option', { name: 'CVS' })).toBeInTheDocument())
    await user.selectOptions(screen.getByRole('combobox', { name: 'Cadena' }), 'ch-2')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/client-chains/by-client/c-1')[0]?.body).toEqual({ chainPublicId: 'ch-2' }))
  })

  it('quitarla manda chainPublicId null', async () => {
    const user = userEvent.setup()
    wrap(<ClientChainPanel client={CLIENT} />, ['sales.chain'])
    await user.selectOptions(await screen.findByRole('combobox', { name: 'Cadena' }), '')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/client-chains/by-client/c-1')[0]?.body).toEqual({ chainPublicId: null }))
  })

  it('sin sales.chain solo la muestra', () => {
    wrap(<ClientChainPanel client={CLIENT} />, ['clients.read'])
    expect(screen.queryByRole('combobox', { name: 'Cadena' })).toBeNull()
    expect(screen.getAllByText('Walgreens').length).toBeGreaterThan(0)
  })
})

describe('Cadena en Clientes y en Órdenes', () => {
  it('la lista de clientes muestra el chip y filtra por cadena y por «Sin cadena»', async () => {
    const user = userEvent.setup()
    wrap(<ClientListScreen />, ['clients.read'])
    const list = await screen.findByRole('list', { name: 'Clientes' })
    expect(within(list).getByText('Walgreens')).toBeInTheDocument()
    const sel = await screen.findByRole('combobox', { name: 'Cadena' })
    await waitFor(() => expect(within(sel).getByRole('option', { name: 'CVS' })).toBeInTheDocument())
    await user.selectOptions(sel, 'ch-2')
    await waitFor(() => expect(callsTo('GET', '/api/v1/clients').some((c) => c.url.searchParams.get('chainPublicId') === 'ch-2')).toBe(true))
    await user.selectOptions(sel, 'NONE')
    await waitFor(() => expect(callsTo('GET', '/api/v1/clients').some((c) => c.url.searchParams.get('noChain') === 'true')).toBe(true))
  })

  it('el filtro de Órdenes repite chainPublicId', () => {
    const f = { ...EMPTY_ORDERS_FILTERS, chains: ['ch-1', 'ch-2'] }
    expect(hasOrdersFilters(f)).toBe(true)
    expect(salesOrderListQuery(ordersApiFilters(f, '')).chainPublicId).toEqual(['ch-1', 'ch-2'])
    expect(salesOrderListQuery(ordersApiFilters(EMPTY_ORDERS_FILTERS, '')).chainPublicId).toBeUndefined()
  })
})

describe('Análisis de ventas — pestaña «Por cadena»', () => {
  it('muestra las cadenas y «Sin cadena», expande los clientes y concilia', async () => {
    const user = userEvent.setup()
    wrap(<ChainSalesTab />, ['sales.analysis.view'])
    expect(await screen.findByText('Sin cadena', { selector: '.sa-rt-name' })).toBeInTheDocument()
    const toggle = screen.getByRole('button', { name: 'Ver los clientes de Walgreens' })
    expect(screen.queryByText('Walgreens 101')).toBeNull()
    await user.click(toggle)
    expect(screen.getByText('Walgreens 101')).toBeInTheDocument()
    expect(screen.getByRole('table', { name: 'Conciliación' })).toBeInTheDocument()
  })
})
