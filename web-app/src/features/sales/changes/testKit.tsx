// Lote 249 — ayudante de las pruebas de componente de «Modificar la orden»: API simulado y envoltorio con sesión, acceso y consultas.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { AccessProvider } from '../../../kernel/access'

export interface Sent { method: string; path: string; body: unknown }
export const mock: { requests: Sent[]; routes: Record<string, (req: { method: string; body: unknown; search: URLSearchParams }) => { status?: number; json: unknown }> } = { requests: [], routes: {} }

export function fetchFor() {
  return async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text && !text.startsWith('--') ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    const route = mock.routes[`${req.method} ${url.pathname}`]
    if (!route) return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    const r = route({ method: req.method, body, search: url.searchParams })
    return new Response(JSON.stringify(r.json), { status: r.status ?? 200, headers: { 'Content-Type': r.status && r.status >= 400 ? 'application/problem+json' : 'application/json' } })
  }
}

export const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

export function wrap(ui: ReactElement, permissions: string[] = ['sales.view', 'sales.modify', 'sales.price', 'sales.tax', 'sales.modify.approve', 'warehouse.reopen-pack']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['SALES_ORDERS', 'WMS_LOTSERIAL']}>{ui}</AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
