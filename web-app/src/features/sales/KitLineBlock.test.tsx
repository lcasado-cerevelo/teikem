// Lote 69 (kits en órdenes de venta) — la sección de componentes de la orden guardada y el bloque de un kit en el borrador (piezas del catálogo, ajustables al pedir).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { KitComponentsSection, KitLineBlock } from './KitLineBlock'
import type { LineDraft } from './salesRules'
import type { SalesOrderLineDto } from './salesApi'

const mock = vi.hoisted(() => ({ adjustable: false }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    if (url.pathname.endsWith('/kit')) {
      return new Response(
        JSON.stringify({
          productPublicId: 'k1', sku: 'KIT-1', name: 'Kit', isKit: true, adjustable: mock.adjustable,
          components: [{ productPublicId: 'a1', sku: 'A', name: 'Tornillo', quantity: 2, sortOrder: 1, isActive: true }, { productPublicId: 'b1', sku: 'B', name: 'Tuerca', quantity: 1, sortOrder: 2, isActive: true }],
          availability: [],
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      )
    }
    return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.adjustable = false
})

const draftLine = (patch: Partial<LineDraft> = {}): LineDraft => ({ key: 1, productPublicId: 'k1', sku: 'KIT-1', name: 'Kit', listPrice: 50, quantity: '3', price: '', isKit: true, components: null, ...patch })

function wrap(ui: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={['inventory.view', 'sales.view']} modules={['WMS_LOTSERIAL', 'SALES_ORDERS']}>
        {ui}
      </AccessProvider>
    </QueryClientProvider>,
  )
}

describe('KitComponentsSection', () => {
  const kitLine = {
    id: 5, lineNo: 1, productPublicId: 'k1', sku: 'KIT-1', productName: 'Kit', qtyOrdered: 3, qtyReserved: 2, qtyBackordered: 1, qtyShipped: 0, isClosed: false, unitPrice: 50, lineSubtotal: 150,
    taxRatePct: 0, taxAmount: 0, isTaxExempt: false, isKit: true,
    components: [
      { productPublicId: 'a1', sku: 'A', name: 'Tornillo', qtyPerKit: 2, needed: 6, reserved: 4, backordered: 2, shipped: 0 },
      { productPublicId: 'b1', sku: 'B', name: 'Tuerca', qtyPerKit: 1, needed: 3, reserved: 2, backordered: 1, shipped: 0 },
    ],
  } as unknown as SalesOrderLineDto

  it('una tabla por kit con pedido, reservado, en backorder y despachado de cada componente', () => {
    wrap(<KitComponentsSection lines={[kitLine]} />)
    const table = screen.getByRole('table', { name: 'Componentes KIT-1' })
    expect(table).toHaveTextContent('Tornillo')
    const rows = screen.getAllByRole('row')
    expect(rows[1]).toHaveTextContent(/A\s*Tornillo.*2.*6.*4.*2/)
  })

  it('no pinta nada si no hay kits', () => {
    const { container } = wrap(<KitComponentsSection lines={[{ ...kitLine, isKit: false, components: [] } as SalesOrderLineDto]} />)
    expect(container).toBeEmptyDOMElement()
  })
})

describe('KitLineBlock', () => {
  it('muestra las piezas del catálogo sin permitir cambiarlas si el kit no es ajustable', async () => {
    wrap(<KitLineBlock line={draftLine()} saved={undefined} onPieces={() => undefined} />)
    expect(await screen.findByText('Tornillo')).toBeInTheDocument()
    expect(screen.getByText('× 2')).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).toBeNull()
  })

  it('con un kit ajustable las cantidades se editan y avisan las piezas nuevas', async () => {
    const user = userEvent.setup()
    mock.adjustable = true
    const onPieces = vi.fn()
    wrap(<KitLineBlock line={draftLine()} saved={undefined} onPieces={onPieces} />)
    const input = await screen.findByLabelText('Por kit A')
    await user.type(input, '5')
    await waitFor(() => expect(onPieces).toHaveBeenCalled())
    expect(onPieces.mock.calls.at(-1)?.[0]).toEqual([
      { productPublicId: 'a1', sku: 'A', name: 'Tornillo', quantity: '25' },
      { productPublicId: 'b1', sku: 'B', name: 'Tuerca', quantity: '1' },
    ])
  })
})
