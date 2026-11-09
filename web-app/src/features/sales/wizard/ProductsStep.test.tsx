// Lote 223 (P3) — paso 2 del pedido guiado: pestañas por marca, ofertas como tarjetas con + / −, la lista completa con su casilla de cantidad (teclear agrega la
// línea) y el carrito fijo (conteo, subtotal, corregir o quitar), sobre el catálogo simulado de GET /sales-orders/catalog.
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { CartBar, ProductsStep } from './ProductsStep'
import type { FakeApi } from './wizardTestApi'
import { StepHarness, WizardProviders } from './wizardTestKit'
import { cartToLines, type WizardState } from './wizardRules'

const mock = vi.hoisted(() => ({ requests: [], route: null }) as FakeApi)

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fakeFetch } = await import('./wizardTestApi')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fakeFetch(mock) }) }
})

const item = (id: string, sku: string, name: string, price: number | null, extra: Record<string, unknown> = {}) => ({ productPublicId: id, sku, name, price, priceSource: 'GENERAL_LIST', isKit: false, kitAdjustable: false, isPromotion: false, imagePublicId: null, ...extra })

const CATALOG = {
  clientPublicId: 'c-1',
  date: '2026-11-07',
  brands: [
    {
      brand: 'Ammex',
      offers: [item('k-1', 'OFERTA1', 'Oferta guantes y cajas', 41.94, { isKit: true, isPromotion: true, imagePublicId: 'f-1' })],
      products: [item('p-1', '52800', 'Guantes de nitrilo', 6.25, { priceSource: 'CLIENT_LIST' }), item('p-2', '52810', 'Guantes de látex', null)],
    },
    { brand: 'Uline', offers: [], products: [item('p-3', '070120', 'Caja de cartón', 3.5)] },
    { brand: null, offers: [], products: [item('p-4', 'SUELTO', 'Producto sin marca', 1)] },
  ],
}

function Step({ initial, onState }: { initial?: Partial<WizardState>; onState?: (s: WizardState) => void }) {
  return (
    <StepHarness initial={{ clientPublicId: 'c-1', ...initial }} onState={onState}>
      {(state, change) => (
        <>
          <ProductsStep state={state} onChange={change} />
          <CartBar state={state} onChange={change} saved={null} />
        </>
      )}
    </StepHarness>
  )
}

describe('ProductsStep — paso 2 del pedido guiado', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.route = (r) => (r.method === 'GET' && r.path === '/api/v1/sales-orders/catalog' ? CATALOG : undefined)
  })

  it('pide el catálogo con los precios del cliente y pinta una pestaña por marca (sin marca al final)', async () => {
    render(<WizardProviders><Step /></WizardProviders>)
    const tabs = await screen.findByRole('tablist', { name: 'Marcas' })
    expect(within(tabs).getAllByRole('tab').map((x) => x.textContent)).toEqual(['Ammex', 'Uline', 'Sin marca'])
    expect(mock.requests.find((r) => r.path === '/api/v1/sales-orders/catalog')?.url.searchParams.get('clientPublicId')).toBe('c-1')
    const offers = screen.getByRole('list', { name: 'Ofertas de Ammex' })
    expect(within(offers).getByText('Oferta guantes y cajas')).toBeInTheDocument()
    const list = screen.getByRole('list', { name: 'Productos de Ammex' })
    expect(within(list).getByText('Guantes de nitrilo')).toBeInTheDocument()
    // sin precio para este cliente: la casilla queda apagada
    expect(within(list).getByRole('textbox', { name: 'Cantidad de 52810' })).toBeDisabled()
    expect(within(list).getByText('Sin precio')).toBeInTheDocument()
  })

  it('cada renglón de la lista lleva su cuadrito de foto; sin foto queda vacío y sin botón de ampliar', async () => {
    render(<WizardProviders><Step /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    const rows = within(list).getAllByRole('listitem')
    expect(rows).toHaveLength(2)
    for (const row of rows) {
      expect(row.querySelector('.wz-thumb')).not.toBeNull()
      expect(within(row).queryByRole('button', { name: /Ver la foto de/ })).toBeNull()
    }
    expect(document.querySelector('.wz-thumb-big')).toBeNull()
  })

  it('teclear una cantidad agrega la línea y el carrito muestra el conteo y el subtotal', async () => {
    const user = userEvent.setup()
    let last: WizardState | null = null
    render(<WizardProviders><Step onState={(s) => (last = s)} /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    await user.type(within(list).getByRole('textbox', { name: 'Cantidad de 52800' }), '4')
    expect(last!.cart).toMatchObject([{ productPublicId: 'p-1', sku: '52800', quantity: '4', price: 6.25 }])
    const cart = screen.getByRole('button', { name: /Carrito: 1 producto\(s\), 4 unidad\(es\)/ })
    expect(cart).toHaveTextContent('$25.00')
    expect(cart).toHaveAttribute('aria-expanded', 'false')
  })

  it('ofertas con + / − y otra marca en su pestaña; el carrito abierto corrige y quita', async () => {
    const user = userEvent.setup()
    let last: WizardState | null = null
    render(<WizardProviders><Step onState={(s) => (last = s)} /></WizardProviders>)
    const offers = await screen.findByRole('list', { name: 'Ofertas de Ammex' })
    const more = within(offers).getByRole('button', { name: 'Agregar uno de Oferta guantes y cajas' })
    await user.click(more)
    await user.click(more)
    expect(within(offers).getByRole('textbox', { name: 'Cantidad de OFERTA1' })).toHaveValue('2')
    await user.click(within(offers).getByRole('button', { name: 'Quitar uno de Oferta guantes y cajas' }))
    expect(within(offers).getByRole('textbox', { name: 'Cantidad de OFERTA1' })).toHaveValue('1')

    await user.click(screen.getByRole('tab', { name: 'Uline' }))
    expect(screen.queryByRole('list', { name: 'Ofertas de Uline' })).not.toBeInTheDocument()
    await user.type(within(screen.getByRole('list', { name: 'Productos de Uline' })).getByRole('textbox', { name: 'Cantidad de 070120' }), '3')
    expect(last!.cart.map((l) => l.sku)).toEqual(['OFERTA1', '070120'])

    await user.click(screen.getByRole('button', { name: /Carrito: 2 producto/ }))
    const region = screen.getByRole('region', { name: 'Carrito' })
    const qty = within(region).getByRole('textbox', { name: 'Cantidad de 070120' })
    await user.clear(qty)
    await user.type(qty, '5')
    expect(last!.cart.find((l) => l.sku === '070120')?.quantity).toBe('5')
    await user.click(within(region).getByRole('button', { name: 'Quitar OFERTA1 del carrito' }))
    await waitFor(() => expect(last!.cart.map((l) => l.sku)).toEqual(['070120']))
  })

  it('el buscador filtra la lista de la marca por SKU o nombre', async () => {
    const user = userEvent.setup()
    render(<WizardProviders><Step /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    await user.type(screen.getByRole('searchbox', { name: /Buscar por SKU o nombre/ }), 'nitrilo')
    expect(within(list).getAllByRole('listitem')).toHaveLength(1)
    await user.clear(screen.getByRole('searchbox', { name: /Buscar por SKU o nombre/ }))
    await user.type(screen.getByRole('searchbox', { name: /Buscar por SKU o nombre/ }), 'zzz')
    expect(screen.getByText('Ningún producto de esta marca coincide con la búsqueda.')).toBeInTheDocument()
  })

  it('Lote 230: en el carrito, una pieza del kit con sustitutos lleva «Cambiar»; escoger deja «en lugar de» y la línea manda todas las piezas', async () => {
    const user = userEvent.setup()
    let last: WizardState | null = null
    mock.route = (r) => {
      if (r.method === 'GET' && r.path === '/api/v1/sales-orders/catalog') return CATALOG
      if (r.method === 'GET' && r.path === '/api/v1/sales-orders/kits/k-1/substitutes')
        return {
          kitPublicId: 'k-1', sku: 'OFERTA1', name: 'Oferta guantes y cajas', adjustable: false,
          components: [
            {
              productPublicId: 'p-1', sku: '52800', name: 'Guantes de nitrilo M', quantity: 2, notSubstitutable: false, categoryPath: 'Guantes / Nitrilo', salePrice: 6.25,
              substitutes: [{ productPublicId: 'p-9', sku: '52801', name: 'Guantes de nitrilo L', brand: 'Ammex', salePrice: 6.25 }],
            },
            { productPublicId: 'p-3', sku: '070120', name: 'Caja de cartón', quantity: 1, notSubstitutable: true, substitutes: [] },
          ],
        }
      return undefined
    }
    render(<WizardProviders><Step onState={(s) => (last = s)} /></WizardProviders>)
    const offers = await screen.findByRole('list', { name: 'Ofertas de Ammex' })
    await user.click(within(offers).getByRole('button', { name: 'Agregar uno de Oferta guantes y cajas' }))
    await user.click(screen.getByRole('button', { name: /Carrito: 1 producto/ }))
    const region = screen.getByRole('region', { name: 'Carrito' })
    // la pieza «no sustituible» no lleva el botón
    expect(await within(region).findByRole('button', { name: 'Cambiar 52800' })).toBeInTheDocument()
    expect(within(region).queryByRole('button', { name: 'Cambiar 070120' })).toBeNull()
    await user.click(within(region).getByRole('button', { name: 'Cambiar 52800' }))
    await user.click(within(region).getByRole('button', { name: 'Usar 52801 en lugar de 52800' }))
    expect(within(region).getByText('Guantes de nitrilo L')).toBeInTheDocument()
    expect(within(region).getByText('en lugar de 52800')).toBeInTheDocument()
    // el precio de la línea no cambia
    expect(within(region).getByText('$41.94')).toBeInTheDocument()
    expect(cartToLines(last!.cart)).toEqual([
      {
        productPublicId: 'k-1',
        quantity: 1,
        components: [
          { productPublicId: 'p-9', quantity: 2, substitutesProductPublicId: 'p-1' },
          { productPublicId: 'p-3', quantity: 1 },
        ],
      },
    ])
    // Deshacer vuelve la pieza del kit
    await user.click(within(region).getByRole('button', { name: 'Deshacer el cambio de 52800' }))
    expect(within(region).queryByText('en lugar de 52800')).toBeNull()
    expect(cartToLines(last!.cart)[0].components).toEqual([
      { productPublicId: 'p-1', quantity: 2 },
      { productPublicId: 'p-3', quantity: 1 },
    ])
  })
})
