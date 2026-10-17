// Pruebas del Radar de órdenes abiertas (Lote 89) sobre un fetch simulado: los grupos por pueblo y por cliente, sus enlaces, la búsqueda entre lo abierto y el error.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { RadarPanel } from './RadarPanel'

const mock = vi.hoisted(() => ({ requests: [] as { path: string; search: string }[], fail: false }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push({ path: url.pathname, search: url.search })
    if (mock.fail) return new Response(JSON.stringify({ title: 'Error interno.', status: 500 }), { status: 500, headers: { 'Content-Type': 'application/problem+json' } })
    if (url.pathname === '/api/v1/pulse/radar') {
      const client = url.searchParams.get('groupBy') === 'CLIENT'
      return json({
        groupBy: client ? 'CLIENT' : 'TOWN', openCount: 214, codAmount: 5320, openStatuses: ['CONFIRMED', 'IN_TRANSIT'],
        groups: client
          ? [{ key: 'cli-1', label: 'Farmacia Central', count: 80, codAmount: 0 }]
          : [{ key: 'Ponce', label: 'Ponce', count: 52, codAmount: 1200 }, { key: 'San Germán', label: 'San Germán', count: 31, codAmount: 0 }],
      })
    }
    if (url.pathname === '/api/v1/orders') {
      const q = url.searchParams.get('search')
      return json({ items: q === 'zzz' ? [] : [{ publicId: 'o-1', orderNumber: 'ORD-00001', clientInvoiceNumber: 'FAC-00012', consigneeName: 'Hospital Norte', consigneeCity: 'Ponce', status: 'CONFIRMED', statusLabel: 'Confirmada', codAmount: 100 }], total: 1 })
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <RadarPanel />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Radar de órdenes abiertas', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.fail = false
  })

  it('muestra cuántas hay abiertas y los grupos por pueblo, cada uno con su enlace a Órdenes filtrado', async () => {
    wrap()
    expect(await screen.findByText('214 abiertas')).toBeInTheDocument()
    const ponce = await screen.findByRole('link', { name: /Ponce/ })
    expect(ponce).toHaveAttribute('href', '/orders?city=Ponce&statuses=CONFIRMED%2CIN_TRANSIT')
    expect(within(ponce).getByText('52')).toBeInTheDocument()
    expect(within(ponce).getByText('$1,200.00')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /San Germán/ })).toHaveAttribute('href', '/orders?city=San+Germ%C3%A1n&statuses=CONFIRMED%2CIN_TRANSIT')
    expect(screen.getByRole('link', { name: 'Ver todas en Órdenes' })).toHaveAttribute('href', '/orders?statuses=CONFIRMED%2CIN_TRANSIT')
    expect(screen.getByText('Solo lo abierto — lo entregado no estorba aquí')).toBeInTheDocument()
  })

  it('agrupar por cliente cambia los grupos y pide groupBy=CLIENT', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('link', { name: /Ponce/ })
    await user.click(screen.getByRole('button', { name: 'Cliente' }))
    const link = await screen.findByRole('link', { name: /Farmacia Central/ })
    expect(link).toHaveAttribute('href', '/orders?client=cli-1&statuses=CONFIRMED%2CIN_TRANSIT')
    expect(mock.requests.some((r) => r.path === '/api/v1/pulse/radar' && r.search.includes('groupBy=CLIENT'))).toBe(true)
  })

  it('buscar muestra las órdenes abiertas que coinciden, con su enlace, y sin resultados lo dice', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('214 abiertas')
    await user.type(screen.getByLabelText('Busca por factura, orden o consignatario…'), 'FAC-12')
    const hit = await screen.findByRole('link', { name: /FAC-00012/ })
    expect(hit).toHaveAttribute('href', '/orders/o-1')
    expect(within(hit).getByText('Hospital Norte')).toBeInTheDocument()
    const call = mock.requests.find((r) => r.path === '/api/v1/orders')!
    expect(call.search).toContain('search=FAC-12')
    expect(call.search).toContain('statuses=CONFIRMED')   // solo lo abierto
    await user.clear(screen.getByLabelText('Busca por factura, orden o consignatario…'))
    await user.type(screen.getByLabelText('Busca por factura, orden o consignatario…'), 'zzz')
    expect(await screen.findByText('Sin resultados en abiertas')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('link', { name: /FAC-00012/ })).toBeNull())
  })

  it('con menos de 2 letras no busca y sigue mostrando los grupos', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('link', { name: /Ponce/ })
    await user.type(screen.getByLabelText('Busca por factura, orden o consignatario…'), 'F')
    expect(screen.getByRole('link', { name: /Ponce/ })).toBeInTheDocument()
    expect(mock.requests.some((r) => r.path === '/api/v1/orders')).toBe(false)
  })

  it('si el servidor falla dice el error', async () => {
    mock.fail = true
    wrap()
    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})
