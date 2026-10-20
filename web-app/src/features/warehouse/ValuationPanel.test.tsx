// Lote 148 — «Valor del inventario»: la pestaña solo con inventory.value, muestra totales y filas, y el buscador y el almacén viajan al API.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ValuationPanel } from './ValuationPanel'

interface Call {
  method: string
  url: URL
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const call = { method: req.method, url: new URL(req.url) }
    mock.calls.push(call)
    const body = route(call)
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
function route({ url }: Call): unknown {
  if (url.pathname === '/api/v1/inventory/valuation')
    return {
      total: 1,
      skip: 0,
      take: 25,
      items: [{ productPublicId: 'aaaaaaaa-0000-0000-0000-000000000001', sku: 'TORN-01', productName: 'Tornillo', clientName: null, onHand: 8, unitCost: 6.5, value: 52, unvaluedQty: 0, productCost: 12.5 }],
      totalValue: 52,
      totalQty: 8,
      unvaluedQty: 3,
      method: 'LAYERS',
    }
  if (url.pathname === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Principal', isActive: true }]
  return []
}

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.value', 'inventory.view']} modules={['WMS_LOTSERIAL']}>
          <ValuationPanel />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
const gets = () => mock.calls.filter((c) => c.url.pathname === '/api/v1/inventory/valuation')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

describe('Valor del inventario', () => {
  it('muestra los totales de todo el filtro, el método y la fila con su valor', async () => {
    wrap()
    expect(await screen.findByText('TORN-01')).toBeInTheDocument()
    expect(screen.getByText('Por capas (lo primero que entró)')).toBeInTheDocument()
    expect(screen.getByText('Valor total')).toBeInTheDocument()
    expect(screen.getByText('Unidades sin costo')).toBeInTheDocument()
  })

  it('el almacén elegido viaja como warehousePublicId y vuelve a la primera página', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('TORN-01')
    await user.selectOptions(screen.getByLabelText('Almacén'), WH)
    await waitFor(() => expect(gets().at(-1)!.url.searchParams.get('warehousePublicId')).toBe(WH))
    expect(gets().at(-1)!.url.searchParams.get('skip')).toBe('0')
  })
})
