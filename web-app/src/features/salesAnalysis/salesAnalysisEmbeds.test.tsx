// Lote 241 — lo del análisis que vive en otras pantallas: «Representante de ventas» en la ficha del cliente (ver y cambiar) y la
// cifra «Venta del mes vs cuota» del panel Ventas del Pulso (enlaza a Análisis de ventas).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { SalesPulsePanel } from '../analytics/SalesPulsePanel'
import { ClientSalesRepPanel } from './ClientSalesRepPanel'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[], pulse: {} as Record<string, unknown> }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, body: text ? JSON.parse(text) : undefined })
    if (url.pathname === '/api/v1/sales-analysis/reps')
      return json([
        { id: 1, name: 'ADVANCE SOLUTIONS GROUP LLC', routeName: 'Cadenas', isActive: true },
        { id: 2, name: 'CINDY OLIVAS DAVILA', routeName: 'Norte', isActive: true },
      ])
    if (req.method === 'PUT' && url.pathname === '/api/v1/sales-analysis/clients/11/rep') return json({ updated: 1, repId: 2, repName: 'CINDY OLIVAS DAVILA' })
    if (url.pathname === '/api/v1/sales-orders/pulse') return json(mock.pulse)
    return new Response('{}', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function wrap(node: ReactNode, perms: string[], modules = ['SALES_ORDERS']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={perms} modules={modules}>
        <MemoryRouter>{node}</MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

const CLIENT = { id: 11, publicId: 'c11', name: 'Farmacia Once', salesRepId: 1, salesRepName: 'ADVANCE SOLUTIONS GROUP LLC', salesRouteName: 'Cadenas' }

describe('ficha del cliente: Representante de ventas', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => (mock.requests.length = 0))

  it('no se pinta sin el permiso del análisis o con el módulo apagado', () => {
    const { container } = wrap(<ClientSalesRepPanel client={CLIENT} />, ['clients.read'])
    expect(container).toBeEmptyDOMElement()
    const off = wrap(<ClientSalesRepPanel client={CLIENT} />, ['sales.analysis.view'], [])
    expect(off.container).toBeEmptyDOMElement()
  })

  it('con «ver» muestra el representante y la ruta, sin poder cambiarlo', async () => {
    const user = userEvent.setup()
    wrap(<ClientSalesRepPanel client={CLIENT} />, ['sales.analysis.view'])
    expect(screen.getByText('ADVANCE SOLUTIONS GROUP LLC · Cadenas')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Expandir|Mostrar|Abrir/ }))
    expect(screen.queryByLabelText('Cambiar el representante')).toBeNull()
  })

  it('con «administrar» lo cambia (PUT clients/{id}/rep)', async () => {
    const user = userEvent.setup()
    wrap(<ClientSalesRepPanel client={CLIENT} />, ['sales.analysis.manage'])
    await user.click(screen.getByRole('button', { name: /Expandir|Mostrar|Abrir/ }))
    const select = await screen.findByLabelText('Cambiar el representante')
    await waitFor(() => expect(screen.getByRole('option', { name: 'CINDY OLIVAS DAVILA · Norte' })).toBeInTheDocument())
    await user.selectOptions(select, '2')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PUT')).toMatchObject({ path: '/api/v1/sales-analysis/clients/11/rep', body: { repId: 2 } })
  })
})

describe('Pulso: Venta del mes vs cuota', () => {
  beforeAll(() => setLang('es'))

  it('con monthSales y monthQuota pinta la cifra con el porcentaje y enlaza al análisis', async () => {
    mock.pulse = { ordersToday: 2, ordersTodayAmount: 100, urgentOpen: 0, clientsToApprove: 0, awaitingApproval: 0, routes: {}, monthSales: 70350, monthQuota: 140700 }
    wrap(<SalesPulsePanel />, ['pulse.view'])
    const tile = await screen.findByRole('group', { name: 'Venta del mes vs cuota' })
    expect(tile).toHaveTextContent('$70,350')
    expect(tile).toHaveTextContent('de $140,700 · 50 %')
    expect(tile).toHaveAttribute('href', '/analytics/sales')
  })

  it('sin monthSales (sin permiso del análisis) no aparece', async () => {
    mock.pulse = { ordersToday: 2, ordersTodayAmount: 100, urgentOpen: 0, clientsToApprove: 0, awaitingApproval: 0, routes: {} }
    wrap(<SalesPulsePanel />, ['pulse.view'])
    await screen.findByRole('group', { name: 'Pedidos de hoy' }).catch(() => null)
    await waitFor(() => expect(screen.queryByText('…')).toBeNull())
    expect(screen.queryByRole('group', { name: 'Venta del mes vs cuota' })).toBeNull()
  })
})
