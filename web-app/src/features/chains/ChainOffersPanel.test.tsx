// Lote 247 — «Ofertas» en la ficha de la cadena: pide `GET /offers?chainPublicId=`, muestra nombre, vigencia y cómo aplica (Todos / Incluida / Todos menos los
// excluidos), solo lectura, y no se pinta sin el módulo o sin permiso.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ChainOffersPanel } from './ChainOffersPanel'

const mock = vi.hoisted(() => ({ calls: [] as URL[], offers: [] as unknown[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    mock.calls.push(new URL(req.url))
    return new Response(JSON.stringify(mock.offers), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const CHAIN = 'bbbbbbbb-0000-0000-0000-000000000001'
const OFFER = (n: string, mode: string, extra: object = {}) => ({ publicId: `o-${n}`, name: n, validFrom: '2026-11-01', validTo: null, hasEndDate: false, audienceMode: mode, appliesToAll: mode === 'ALL', itemCount: 3, ...extra })

function wrap(modules = ['SALES_ORDERS'], permissions = ['sales.view']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={modules}>
          <ChainOffersPanel chainPublicId={CHAIN} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.offers = [OFFER('Navidad', 'ALL'), OFFER('Solo Walgreens', 'INCLUDE', { validTo: '2026-12-31', hasEndDate: true }), OFFER('Menos otros', 'EXCLUDE')]
})

describe('ChainOffersPanel', () => {
  it('pide las ofertas de la cadena y dice cómo aplica cada una', async () => {
    wrap()
    const table = await screen.findByRole('table', { name: 'Ofertas' })
    expect(mock.calls[0].pathname).toBe('/api/v1/offers')
    expect(mock.calls[0].searchParams.get('chainPublicId')).toBe(CHAIN)
    expect(within(table).getByText('Todos')).toBeInTheDocument()
    expect(within(table).getByText('Incluida')).toBeInTheDocument()
    expect(within(table).getByText('Todos menos los excluidos')).toBeInTheDocument()
    expect(within(table).getAllByText(/Desde el .*sin fecha final/)).toHaveLength(2)
    expect(screen.getByRole('link', { name: 'Ver todas las ofertas en Precios y ofertas' })).toHaveAttribute('href', '/money/prices')
  })

  it('sin ofertas lo dice; sin módulo o sin permiso no se pinta', async () => {
    mock.offers = []
    const a = wrap()
    expect(await screen.findByText('Sin ofertas vigentes')).toBeInTheDocument()
    a.unmount()
    expect(wrap([]).container).toBeEmptyDOMElement()
  })
})
