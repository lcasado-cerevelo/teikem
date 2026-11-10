// Lote 231 — la ficha del producto ya no tiene la pestaña «Kit» ni «Convertir en kit» (un kit que se arma al pedir es un kit de oferta y se crea en
// Contabilidad → Precios y ofertas): solo Lotes y Series; un enlace viejo con `?tab=kit` abre Lotes y nadie pide `/products/{id}/kit` ni lo cambia.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ProductDetailScreen from './ProductDetailScreen'

interface Call {
  method: string
  url: URL
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], isKit: false }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const call = { method: req.method, url: new URL(req.url) }
    mock.calls.push(call)
    return new Response(JSON.stringify(route(call)), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const P = 'aaaaaaaa-0000-0000-0000-000000000001'

function route({ method, url }: Call): unknown {
  if (method === 'GET' && url.pathname === `/api/v1/products/${P}`) {
    return { product: { id: 1, publicId: P, sku: 'MON-1', name: 'Monitor', trackingTypeCode: 'NONE', qtyOnHand: 0, qtyAvailable: 0, isOwn: true, isActive: true, isKit: mock.isKit }, rowVersion: 'RV==' }
  }
  return []
}

function wrap(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.view', 'inventory.manage', 'inventory.adjust']} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/products/:publicId" element={<ProductDetailScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.isKit = false
})

describe('Ficha del producto sin pestaña Kit (lote 231)', () => {
  it('solo Lotes y Series; ni «Kit» ni «Convertir en kit», aun con inventory.manage', async () => {
    wrap(`/warehouse/products/${P}?tab=lots`)
    const tabs = await screen.findByRole('tablist', { name: 'Productos e inventario' })
    expect(Array.from(tabs.querySelectorAll('[role="tab"]')).map((b) => b.textContent)).toEqual(['Lotes', 'Series'])
    expect(screen.queryByRole('tab', { name: 'Kit' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Convertir en kit' })).toBeNull()
  })

  it('un enlace viejo con ?tab=kit abre Lotes y no consulta ni cambia el kit', async () => {
    mock.isKit = true
    wrap(`/warehouse/products/${P}?tab=kit`)
    expect(await screen.findByRole('tab', { name: 'Lotes' })).toHaveAttribute('aria-selected', 'true')
    // el distintivo «Kit» de un kit viejo se sigue viendo en el encabezado
    expect(screen.getByText('Kit')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.url.pathname.endsWith('/kit'))).toBe(false)
    expect(mock.calls.some((c) => c.method === 'PUT')).toBe(false)
  })
})
