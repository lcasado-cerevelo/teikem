// Lote 229 — pedido guiado con las ofertas nuevas: la tarjeta dice de qué oferta sale y hasta cuándo vale («sin fecha de fin»), el precio de oferta lleva el chip
// «Oferta», los avisos del servidor (dos ofertas a la vez, oferta más cara que la lista general) salen junto al precio, en el carrito y en el resumen de entrega
// (`linePrices` del borrador), y un kit de oferta solo sale como tarjeta.
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import type { SalesOrderDto } from '../salesApi'
import { DeliveryPriceNotices } from './PriceNotices'
import { CartBar, ProductsStep } from './ProductsStep'
import { cartPriceNotices, priceSourceOf } from './priceSource'
import type { FakeApi } from './wizardTestApi'
import { StepHarness, WizardProviders } from './wizardTestKit'
import type { WizardState } from './wizardRules'

const mock = vi.hoisted(() => ({ requests: [], route: null }) as FakeApi)

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fakeFetch } = await import('./wizardTestApi')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fakeFetch(mock) }) }
})

const item = (id: string, sku: string, name: string, price: number | null, extra: Record<string, unknown> = {}) => ({
  productPublicId: id, sku, name, price, priceSource: 'GENERAL_LIST', isKit: false, kitAdjustable: false, isPromotion: false, imagePublicId: null, isOfferKit: false, notices: [], ...extra,
})
const TWO = 'El producto 52800 está en dos ofertas a la vez: «Navidad» a $5.50 y «Verano» a $6.00. Se aplica la más barata («Navidad»).'
const ABOVE = 'La oferta «Verano» pone 070120 a $4.00, más caro que la lista general ($3.50): se aplica el precio más bajo.'

const OFFER_KIT = item('k-1', 'KIT-NAV', 'Kit Navidad', 41.94, { isKit: true, isOfferKit: true, isPromotion: true, priceSource: 'OFFER', offerPublicId: 'o-1', offerName: 'Navidad', offerValidFrom: '2026-11-01', offerValidTo: '2026-12-31', offerPrice: 41.94 })
const GLOVES = item('p-1', '52800', 'Guantes de nitrilo', 5.5, {
  isPromotion: true, priceSource: 'OFFER', offerPublicId: 'o-1', offerName: 'Navidad', offerValidFrom: '2026-11-01', offerValidTo: null, offerPrice: 5.5,
  notices: [{ code: 'TWO_OFFERS', message: TWO, productPublicId: 'p-1', sku: '52800' }],
})
const CATALOG = {
  clientPublicId: 'c-1',
  date: '2026-11-09',
  brands: [
    {
      brand: 'Ammex',
      // tarjetas: el kit de oferta (con «válida hasta») y un producto normal en oferta sin fecha de fin; el producto en oferta sale además en la lista
      offers: [OFFER_KIT, GLOVES],
      products: [GLOVES, OFFER_KIT, item('p-2', '52810', 'Guantes de látex', 4, { priceSource: 'CLIENT_LIST', offerPublicId: 'o-2', offerName: 'Verano', offerPrice: 4.5, isPromotion: true })],
    },
    {
      brand: 'Uline',
      offers: [],
      products: [item('p-3', '070120', 'Caja de cartón', 3.5, { notices: [{ code: 'OFFER_ABOVE_GENERAL', message: ABOVE, productPublicId: 'p-3', sku: '070120' }] })],
    },
  ],
}

function Step({ onState }: { onState?: (s: WizardState) => void }) {
  return (
    <StepHarness initial={{ clientPublicId: 'c-1' }} onState={onState}>
      {(state, change) => (
        <>
          <ProductsStep state={state} onChange={change} />
          <CartBar state={state} onChange={change} saved={null} />
        </>
      )}
    </StepHarness>
  )
}

describe('ProductsStep — ofertas nuevas y avisos de precio (lote 229)', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.route = (r) => (r.method === 'GET' && r.path === '/api/v1/sales-orders/catalog' ? CATALOG : undefined)
  })

  it('la tarjeta dice la oferta y su vigencia; sin fecha de fin lo dice; el kit de oferta solo sale como tarjeta', async () => {
    render(<WizardProviders><Step /></WizardProviders>)
    const offers = await screen.findByRole('list', { name: 'Ofertas de Ammex' })
    const cards = within(offers).getAllByRole('listitem')
    expect(within(cards[0]).getByText('Kit Navidad')).toBeInTheDocument()
    expect(within(cards[0]).getByText('Navidad')).toBeInTheDocument()
    expect(within(cards[0]).getByText('Válida hasta el 12/31/2026')).toBeInTheDocument()
    expect(within(cards[1]).getByText('Sin fecha de fin')).toBeInTheDocument()
    expect(within(cards[1]).getByRole('list', { name: 'Avisos de precio de 52800' })).toHaveTextContent(TWO)
    const list = screen.getByRole('list', { name: 'Productos de Ammex' })
    // el kit de oferta no sale en la lista de la marca; el producto en oferta sí
    expect(within(list).queryByText('Kit Navidad')).toBeNull()
    expect(within(list).getByText('Guantes de nitrilo')).toBeInTheDocument()
  })

  it('el precio de oferta lleva el chip «Oferta»; el negociado más bajo no; los avisos van junto al producto', async () => {
    const user = userEvent.setup()
    render(<WizardProviders><Step /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    const [gloves, latex] = within(list).getAllByRole('listitem')
    expect(within(gloves).getByText('Oferta')).toHaveAttribute('title', 'Precio de la oferta «Navidad»')
    expect(within(gloves).getByText(TWO)).toBeInTheDocument()
    expect(within(latex).queryByText('Oferta')).toBeNull()
    // busca en todas las marcas: el aviso de la otra marca también sale
    await user.type(screen.getByRole('searchbox', { name: /Buscar por SKU o nombre/ }), 'caja')
    const all = screen.getByRole('list', { name: 'Productos de Todas las marcas' })
    expect(within(all).getByText(ABOVE)).toBeInTheDocument()
  })

  it('en el carrito la línea dice la oferta de donde sale el precio y sus avisos', async () => {
    const user = userEvent.setup()
    render(<WizardProviders><Step /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    await user.type(within(list).getByRole('textbox', { name: 'Cantidad de 52800' }), '3')
    await user.click(screen.getByRole('button', { name: /Carrito: 1 producto/ }))
    const region = screen.getByRole('region', { name: 'Carrito' })
    expect(within(region).getByText('Oferta «Navidad»')).toBeInTheDocument()
    expect(within(region).getByRole('list', { name: 'Avisos de precio de 52800' })).toHaveTextContent(TWO)
  })

  it('resumen de entrega: los avisos de lo guardado (linePrices) y del catálogo, sin repetir', async () => {
    const saved = {
      linePrices: [
        { productPublicId: 'p-1', sku: '52800', resolvedPrice: 5.5, priceSource: 'OFFER', offerName: 'Navidad', notices: [{ code: 'TWO_OFFERS', message: TWO }] },
        { productPublicId: 'p-3', sku: '070120', resolvedPrice: 3.5, priceSource: 'GENERAL_LIST', notices: [{ code: 'OFFER_ABOVE_GENERAL', message: ABOVE }] },
      ],
    } as unknown as SalesOrderDto
    const cart = [
      { productPublicId: 'p-1', sku: '52800' },
      { productPublicId: 'p-2', sku: '52810' },
      { productPublicId: 'p-3', sku: '070120' },
    ]
    render(
      <WizardProviders>
        <DeliveryPriceNotices cart={cart} clientPublicId="c-1" saved={saved} />
      </WizardProviders>,
    )
    const box = await screen.findByRole('region', { name: 'Avisos de precio' })
    const items = within(box).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent(`52800 ${TWO}`)
    expect(items[1]).toHaveTextContent(`070120 ${ABOVE}`)
  })

  it('priceSourceOf / cartPriceNotices: lo guardado manda sobre el catálogo', () => {
    const saved = { linePrices: [{ productPublicId: 'p-2', priceSource: 'OFFER', offerName: 'Verano', notices: [] }] } as unknown as SalesOrderDto
    expect(priceSourceOf('p-2', CATALOG, saved)).toEqual({ fromOffer: true, offerName: 'Verano', notices: [] })
    expect(priceSourceOf('p-2', CATALOG, null)).toEqual({ fromOffer: false, offerName: null, notices: [] })
    expect(priceSourceOf('p-1', CATALOG, null)).toEqual({ fromOffer: true, offerName: 'Navidad', notices: [TWO] })
    expect(cartPriceNotices([{ productPublicId: 'p-2', sku: '52810' }, { productPublicId: 'p-3', sku: '070120' }], CATALOG, null)).toEqual([{ productPublicId: 'p-3', sku: '070120', notices: [ABOVE] }])
  })
})
