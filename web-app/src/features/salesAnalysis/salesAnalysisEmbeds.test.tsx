// Lote 241/244 — lo del análisis que vive en otras pantallas: «Representante de ventas» en la ficha del cliente (ver y cambiar) y la
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
import { ClientSalesPanel } from './ClientSalesPanel'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[], pulse: {} as Record<string, unknown>, sales: {} as Record<string, unknown> }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, body: text ? JSON.parse(text) : undefined })
    if (url.pathname === '/api/v1/sales-analysis/routes') return json([{ id: 5, name: 'Norte', isActive: true }, { id: 6, name: 'Cadenas', isActive: true }])
    if (url.pathname === '/api/v1/sales-analysis/routes/5/people')
      return json({
        routeId: 5,
        primary: { id: 1, userId: 7, userName: 'Ana Titular', role: 'PRIMARY', isCurrent: true, isVoided: false, userIsActiveMember: true },
        supports: [{ id: 2, userId: 8, userName: 'Beto Apoyo', role: 'SUPPORT', isCurrent: true, isVoided: false, userIsActiveMember: true }],
      })
    if (req.method === 'PUT' && url.pathname === '/api/v1/sales-analysis/clients/11/sales') return json(mock.sales)
    if (url.pathname === '/api/v1/sales-analysis/clients/11/sales') return json(mock.sales)
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

const CLIENT = { id: 11, publicId: 'c11', name: 'Farmacia Once' }

const SALES = {
  clientId: 11, routeId: 5, routeName: 'Norte', routeFromRep: false, directRouteId: 5,
  primary: { userId: 7, name: 'Ana Titular', isActive: true },
  supports: [{ userId: 8, name: 'Beto Apoyo', isActive: true }],
  preferred: { userId: 8, name: 'Beto Apoyo', isActive: true }, preferredOutOfRoute: false,
  proposedUserId: 8, proposedName: 'Beto Apoyo', proposedKind: 'PREFERRED', repId: 1, repName: 'REP UNO', repRouteName: 'Cadenas', rowVersion: 'rv1',
}

describe('ficha del cliente: Venta', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.sales = { ...SALES }
  })

  it('no se pinta sin el permiso del análisis o con el módulo apagado', () => {
    const { container } = wrap(<ClientSalesPanel client={CLIENT} />, ['clients.read'])
    expect(container).toBeEmptyDOMElement()
    const off = wrap(<ClientSalesPanel client={CLIENT} />, ['sales.analysis.view'], [])
    expect(off.container).toBeEmptyDOMElement()
  })

  it('con «ver» muestra ruta, equipo, preferido y propuesto, sin poder cambiarlos', async () => {
    const user = userEvent.setup()
    wrap(<ClientSalesPanel client={CLIENT} />, ['sales.analysis.view'])
    expect(await screen.findByText('Norte · Beto Apoyo')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Expandir|Mostrar|Abrir/ }))
    expect(screen.getByText('Ana Titular')).toBeInTheDocument()
    expect(screen.getByText('REP UNO')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Guardar' })).toBeNull()
  })

  it('si el preferido salió de la ruta, avisa sin bloquear', async () => {
    mock.sales = { ...SALES, preferredOutOfRoute: true }
    const user = userEvent.setup()
    wrap(<ClientSalesPanel client={CLIENT} />, ['sales.analysis.view'])
    await user.click(await screen.findByRole('button', { name: /Expandir|Mostrar|Abrir/ }))
    expect(screen.getByText('El vendedor preferido ya no pertenece a la ruta del cliente.')).toBeInTheDocument()
  })

  it('con «administrar» cambia el preferido (PUT clients/{id}/sales con rowVersion)', async () => {
    const user = userEvent.setup()
    wrap(<ClientSalesPanel client={CLIENT} />, ['sales.analysis.manage'])
    await user.click(await screen.findByRole('button', { name: /Expandir|Mostrar|Abrir/ }))
    const select = await screen.findByLabelText('Vendedor preferido')
    await waitFor(() => expect(screen.getByRole('option', { name: 'Ana Titular' })).toBeInTheDocument())
    await user.selectOptions(select, '7')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PUT')).toMatchObject({ path: '/api/v1/sales-analysis/clients/11/sales', body: { preferredUserId: 7, rowVersion: 'rv1' } })
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
