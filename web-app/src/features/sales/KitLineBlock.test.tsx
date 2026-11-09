// Lote 69 (kits en órdenes de venta) — la sección de componentes de la orden guardada y el bloque de un kit en el borrador (piezas del catálogo, ajustables al pedir).
// Lote 230: sustitución de piezas («Cambiar» con los sustitutos válidos, «en lugar de …», «Deshacer») y la pieza real en la orden guardada.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { KitComponentsSection, KitLineBlock } from './KitLineBlock'
import { useState } from 'react'
import type { KitPiece, LineDraft } from './salesRules'
import type { SalesOrderLineDto } from './salesApi'

const mock = vi.hoisted(() => ({ adjustable: false, subsCalls: 0 }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    if (url.pathname === '/api/v1/sales-orders/kits/k1/substitutes') {
      mock.subsCalls++
      return new Response(
        JSON.stringify({
          kitPublicId: 'k1', sku: 'KIT-1', name: 'Kit', adjustable: false,
          components: [
            {
              productPublicId: 'a1', sku: 'A', name: 'Tornillo', quantity: 2, notSubstitutable: false, categoryId: 4, categoryPath: 'Ferretería / Tornillos', salePrice: 1,
              substitutes: [{ productPublicId: 'a2', sku: 'A2', name: 'Tornillo inox', brand: 'Acme', salePrice: 1 }],
            },
            { productPublicId: 'b1', sku: 'B', name: 'Tuerca', quantity: 1, notSubstitutable: true, salePrice: 1, substitutes: [] },
          ],
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      )
    }
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
  mock.subsCalls = 0
})

const draftLine = (patch: Partial<LineDraft> = {}): LineDraft => ({ key: 1, productPublicId: 'k1', sku: 'KIT-1', name: 'Kit', listPrice: 50, quantity: '3', price: '', isKit: true, components: null, ...patch })

function wrap(ui: React.ReactNode, permissions = ['inventory.view', 'sales.view']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL', 'SALES_ORDERS']}>
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

  it('Lote 230: la pieza real con «en lugar de» la del kit', () => {
    const line = { ...kitLine, components: [{ ...kitLine.components![0], productPublicId: 'a2', sku: 'A2', name: 'Tornillo inox', substitutesProductPublicId: 'a1', substitutesSku: 'A', substitutesName: 'Tornillo' }] } as SalesOrderLineDto
    wrap(<KitComponentsSection lines={[line]} />)
    const table = screen.getByRole('table', { name: 'Componentes KIT-1' })
    expect(table).toHaveTextContent('A2')
    expect(within(table).getByText('en lugar de A')).toBeInTheDocument()
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

  it('Lote 230: con sales.create, «Cambiar» lista solo los sustitutos válidos; escoger muestra «en lugar de» y manda la original; Deshacer la vuelve', async () => {
    const user = userEvent.setup()
    const pieces: KitPiece[][] = []
    function Harness() {
      const [components, setComponents] = useState<KitPiece[] | null>(null)
      return (
        <KitLineBlock
          line={draftLine({ components })}
          saved={undefined}
          onPieces={(p) => {
            if (p) pieces.push(p)
            setComponents(p)
          }}
        />
      )
    }
    // vendedor sin inventory.view: las piezas salen igual (del endpoint de sustitutos)
    wrap(<Harness />, ['sales.view', 'sales.create'])
    expect(await screen.findByText('Tornillo')).toBeInTheDocument()
    expect(screen.getByText('Tuerca')).toBeInTheDocument()
    // la pieza «no sustituible» no lleva el botón
    expect(screen.queryByRole('button', { name: 'Cambiar B' })).toBeNull()
    const change = screen.getByRole('button', { name: 'Cambiar A' })
    await user.click(change)
    const list = screen.getByRole('list', { name: 'Productos que pueden sustituir a A' })
    expect(within(list).getAllByRole('button').map((b) => b.textContent)).toEqual(['A2 · Tornillo inox · Acme'])
    await user.click(within(list).getByRole('button', { name: 'Usar A2 en lugar de A' }))
    expect(pieces.at(-1)).toEqual([
      { productPublicId: 'a2', sku: 'A2', name: 'Tornillo inox', quantity: '2', substitutesProductPublicId: 'a1', substitutesSku: 'A', substitutesName: 'Tornillo' },
      { productPublicId: 'b1', sku: 'B', name: 'Tuerca', quantity: '1' },
    ])
    expect(await screen.findByText('Tornillo inox')).toBeInTheDocument()
    expect(screen.getByText('en lugar de A')).toBeInTheDocument()
    // el único sustituto ya está puesto: no queda nada que cambiar, pero sí se puede deshacer
    expect(screen.queryByRole('button', { name: 'Cambiar A2' })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Deshacer el cambio de A' }))
    expect(pieces.at(-1)?.[0]).toEqual({ productPublicId: 'a1', sku: 'A', name: 'Tornillo', quantity: '2' })
    expect(screen.queryByText('en lugar de A')).toBeNull()
  })

  it('Lote 230: un borrador guardado con una pieza sustituida la muestra con «en lugar de» y deja deshacerla', async () => {
    const user = userEvent.setup()
    const onPieces = vi.fn()
    const saved = {
      id: 1, productPublicId: 'k1', sku: 'KIT-1', isKit: true,
      components: [
        { productPublicId: 'a2', sku: 'A2', name: 'Tornillo inox', qtyPerKit: 2, substitutesProductPublicId: 'a1', substitutesSku: 'A', substitutesName: 'Tornillo' },
        { productPublicId: 'b1', sku: 'B', name: 'Tuerca', qtyPerKit: 1 },
      ],
    } as unknown as SalesOrderLineDto
    wrap(<KitLineBlock line={draftLine()} saved={saved} onPieces={onPieces} />, ['sales.view', 'sales.create'])
    expect(await screen.findByText('Tornillo inox')).toBeInTheDocument()
    expect(screen.getByText('en lugar de A')).toBeInTheDocument()
    await waitFor(() => expect(mock.subsCalls).toBe(1))
    await user.click(screen.getByRole('button', { name: 'Deshacer el cambio de A' }))
    expect(onPieces.mock.calls.at(-1)?.[0]).toEqual([
      { productPublicId: 'a1', sku: 'A', name: 'Tornillo', quantity: '2' },
      { productPublicId: 'b1', sku: 'B', name: 'Tuerca', quantity: '1' },
    ])
  })
})
