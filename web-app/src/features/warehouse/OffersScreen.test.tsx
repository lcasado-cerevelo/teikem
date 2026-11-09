// Lote 229 — sección «Ofertas» de Precios y ofertas sobre un fetch simulado de `/api/v1/offers`: tarjetas con estado y vigencia («Sin fecha de fin» bien visible),
// crear una oferta (validación, aviso sin «hasta», cuerpo), agregar un renglón con un producto existente, crear un kit para la oferta (piezas con «No
// sustituible», suma a precio normal y ahorro), los avisos del servidor como avisos (no errores), la foto del renglón y los permisos.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { PriceListsTab } from './PriceListsTab'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    let body: unknown
    try {
      body = text ? JSON.parse(text) : undefined
    } catch {
      body = text
    }
    const call = { method: req.method, url: new URL(req.url), body }
    mock.calls.push(call)
    const out = route(call)
    return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const O1 = 'dddddddd-0000-0000-0000-000000000001'
const O2 = 'dddddddd-0000-0000-0000-000000000002'
const NEW = 'dddddddd-0000-0000-0000-000000000003'
const GUA = 'eeeeeeee-0000-0000-0000-000000000001'
const MON = 'eeeeeeee-0000-0000-0000-000000000002'
const TIR = 'eeeeeeee-0000-0000-0000-000000000003'
const KIT = 'eeeeeeee-0000-0000-0000-000000000004'

const LIST_ITEM = (publicId: string, name: string, state: string, extra: object = {}) => ({
  publicId, name, validFrom: '2026-11-01', validTo: null, hasEndDate: false, validityText: 'desde el 01/11/2026, sin fecha de fin', isPublished: state !== 'DRAFT', state,
  inEffect: state === 'ACTIVE', appliesToAll: true, itemCount: 2, clientCount: 0, imagePublicId: null, isActive: true, createdAtUtc: '2026-11-01T00:00:00', ...extra,
})
const WARNING = 'El producto GUA-1 también está en la oferta «Verano» (del 01/11/2026 al 31/12/2026) a $8.00, igual o más barata que aquí ($9.00): mientras las dos estén vigentes se aplica el precio más bajo.'
const DETAIL = (publicId: string, name: string, state: string, extra: object = {}) => ({
  publicId, name, validFrom: '2026-11-01', validTo: null, hasEndDate: false, validityText: 'desde el 01/11/2026, sin fecha de fin', isPublished: state !== 'DRAFT', state,
  inEffect: state === 'ACTIVE', appliesToAll: true, notes: null, imagePublicId: null, isActive: true, clients: [], warnings: [], createdAtUtc: '2026-11-01T00:00:00',
  items: [],
  ...extra,
})
const ITEMS = [
  { id: 11, productPublicId: GUA, sku: 'GUA-1', productName: 'Guantes', isKit: false, isOfferKit: false, price: 9, productSalePrice: 12, generalListPrice: 10, imagePublicId: null, effectiveImagePublicId: 'f-prod', productIsActive: true },
  {
    id: 12, productPublicId: KIT, sku: 'KIT-OF', productName: 'Monitor y tirillas', isKit: true, isOfferKit: true, price: 30, productSalePrice: 30, generalListPrice: null, imagePublicId: 'f-own', effectiveImagePublicId: 'f-own', productIsActive: true,
    kit: { productPublicId: KIT, sku: 'KIT-OF', name: 'Monitor y tirillas', isKit: true, adjustable: false, components: [{ productPublicId: MON, sku: 'MON-1', name: 'Monitor', quantity: 1, sortOrder: 1, isActive: true, notSubstitutable: false }] },
  },
]

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/price-lists') return []
  if (method === 'GET' && p === '/api/v1/offers')
    return [LIST_ITEM(O1, 'Navidad', 'ACTIVE'), LIST_ITEM(O2, 'Verano', 'SCHEDULED', { validTo: '2026-12-31', hasEndDate: true, validityText: 'del 01/11/2026 al 31/12/2026', itemCount: 1 })]
  if (method === 'GET' && p === `/api/v1/offers/${O1}`) return DETAIL(O1, 'Navidad', 'ACTIVE', { items: ITEMS, warnings: [{ code: 'CHEAPER_OFFER', message: WARNING, productPublicId: GUA, sku: 'GUA-1', otherOfferName: 'Verano', otherPrice: 8 }] })
  if (method === 'POST' && p === '/api/v1/offers') return DETAIL(NEW, 'Black Friday', 'DRAFT')
  if (method === 'GET' && p === `/api/v1/offers/${NEW}`) return DETAIL(NEW, 'Black Friday', 'DRAFT')
  if (method === 'POST' && p === `/api/v1/offers/${O1}/items`) return DETAIL(O1, 'Navidad', 'ACTIVE', { items: ITEMS })
  if (method === 'POST' && p === `/api/v1/offers/${O1}/kits`) return { productPublicId: KIT, sku: 'KIT-NEW', offer: DETAIL(O1, 'Navidad', 'ACTIVE', { items: ITEMS }) }
  if (method === 'DELETE' && p === `/api/v1/offers/${O1}/items/12/image`) return DETAIL(O1, 'Navidad', 'ACTIVE', { items: ITEMS })
  if (method === 'GET' && p === '/api/v1/products')
    return {
      total: 3, skip: 0, take: 50,
      items: [
        { id: 1, publicId: GUA, sku: 'GUA-1', name: 'Guantes', isOwn: true, isActive: true, salePrice: 12 },
        { id: 2, publicId: MON, sku: 'MON-1', name: 'Monitor', isOwn: true, isActive: true, salePrice: 30 },
        { id: 3, publicId: TIR, sku: 'TIR-1', name: 'Tirillas', isOwn: true, isActive: true, salePrice: 10 },
      ],
    }
  if (method === 'GET' && p.startsWith('/api/v1/files/')) return new Response('', { status: 404 })
  return []
}

function wrap(permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['SALES_ORDERS']}>
          <PriceListsTab />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const EDIT = ['sales.view', 'sales.pricelist']
const posts = (path: string) => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

async function openOffer(user: ReturnType<typeof userEvent.setup>, name: string) {
  const card = await screen.findByRole('article', { name })
  await user.click(within(card).getByRole('button', { name: /Abrir oferta|Ver oferta/ }))
  return screen.findByRole('dialog', { name })
}

describe('Precios y ofertas — Ofertas (lote 229)', () => {
  it('tarjetas con estado, vigencia, a quién aplica y renglones; sin «hasta» lo dice bien visible', async () => {
    wrap(['sales.view'])
    const navidad = await screen.findByRole('article', { name: 'Navidad' })
    expect(within(navidad).getByText('Vigente')).toBeInTheDocument()
    expect(within(navidad).getByText('Se aplica hoy')).toBeInTheDocument()
    expect(within(navidad).getByText('Sin fecha de fin: vale hasta que la desactive')).toHaveClass('of-open')
    expect(within(navidad).getByText(/Todos los clientes · 2 renglón\(es\)/)).toBeInTheDocument()
    const verano = screen.getByRole('article', { name: 'Verano' })
    expect(within(verano).getByText('Publicada (todavía no empieza)')).toBeInTheDocument()
    expect(within(verano).getByText('Del 11/01/2026 al 12/31/2026')).toBeInTheDocument()
    expect(within(verano).queryByText(/Sin fecha de fin/)).toBeNull()
    // solo sales.view: se ve, no se edita
    expect(within(navidad).getByRole('button', { name: 'Ver oferta' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Nueva oferta' })).toBeNull()
    // el flujo viejo (kit marcado «Es promoción») ya no se consulta
    expect(mock.calls.some((c) => c.url.pathname === '/api/v1/price-lists/offers')).toBe(false)
  })

  it('Nueva oferta: valida el nombre, avisa que sin «hasta» no vence y manda el cuerpo de crear', async () => {
    const user = userEvent.setup()
    wrap(EDIT)
    await screen.findByRole('article', { name: 'Navidad' })
    await user.click(screen.getByRole('button', { name: 'Nueva oferta' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva oferta' })
    expect(within(dialog).getByRole('note')).toHaveTextContent('«Hasta» está en blanco: la oferta no tiene fecha de fin y vale hasta que la desactive.')
    await user.click(within(dialog).getByRole('button', { name: 'Crear oferta' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Indique el nombre de la oferta.')
    expect(posts('/api/v1/offers')).toHaveLength(0)

    await user.type(within(dialog).getByLabelText('Nombre de la oferta'), '  Black Friday ')
    await user.click(within(dialog).getByRole('button', { name: 'Crear oferta' }))
    await waitFor(() => expect(posts('/api/v1/offers')).toHaveLength(1))
    expect(posts('/api/v1/offers')[0].body).toMatchObject({ name: 'Black Friday', validFrom: expect.any(String), validTo: null, clearValidTo: true, appliesToAll: true, notes: null })
    // creada: ya se agregan renglones de las dos maneras
    const created = await screen.findByRole('dialog', { name: 'Black Friday' })
    expect(await within(created).findByRole('combobox', { name: 'Escoger un producto o kit' })).toBeInTheDocument()
    expect(within(created).getByRole('button', { name: 'Crear kit para esta oferta' })).toBeInTheDocument()
    expect(within(created).getByText('Borrador')).toBeInTheDocument()
  })

  it('renglón con un producto existente y su precio de oferta', async () => {
    const user = userEvent.setup()
    wrap(EDIT)
    const dialog = await openOffer(user, 'Navidad')
    await user.click(await within(dialog).findByRole('button', { name: 'Agregar' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Escoja el producto o kit de cada renglón.')
    await user.click(within(dialog).getByRole('combobox', { name: 'Escoger un producto o kit' }))
    await user.click(await within(dialog).findByRole('option', { name: /GUA-1/ }))
    await user.type(within(dialog).getByLabelText('Precio de oferta'), '8.75')
    await user.click(within(dialog).getByRole('button', { name: 'Agregar' }))
    await waitFor(() => expect(posts(`/api/v1/offers/${O1}/items`)).toHaveLength(1))
    expect(posts(`/api/v1/offers/${O1}/items`)[0].body).toEqual({ items: [{ productPublicId: GUA, price: 8.75 }] })
  })

  it('crear kit para la oferta: piezas con «No sustituible», suma a precio normal, ahorro y el cuerpo', async () => {
    const user = userEvent.setup()
    wrap(EDIT)
    const offer = await openOffer(user, 'Navidad')
    await user.click(await within(offer).findByRole('button', { name: 'Crear kit para esta oferta' }))
    const dialog = await screen.findByRole('dialog', { name: 'Crear kit para la oferta «Navidad»' })
    await user.click(within(dialog).getByRole('button', { name: 'Crear kit' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Indique el SKU del kit.')

    await user.type(within(dialog).getByLabelText('SKU del kit'), 'KIT-NEW')
    await user.type(within(dialog).getByLabelText('Nombre del kit'), 'Monitor con tirillas')
    await user.type(within(dialog).getByLabelText('Precio de oferta del kit'), '30')
    await user.click(within(dialog).getByRole('combobox', { name: 'Pieza 1' }))
    await user.click(await within(dialog).findByRole('option', { name: /MON-1/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Agregar pieza' }))
    await user.click(within(dialog).getByRole('combobox', { name: 'Pieza 2' }))
    await user.click(await within(dialog).findByRole('option', { name: /TIR-1/ }))
    await user.click(within(dialog).getByRole('checkbox', { name: 'Pieza 2: no sustituible' }))
    expect(within(dialog).getByText('Suma de las piezas a su precio de venta: $40.00')).toBeInTheDocument()
    expect(within(dialog).getByText('Ahorro para el cliente: $10.00')).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Crear kit' }))
    await waitFor(() => expect(posts(`/api/v1/offers/${O1}/kits`)).toHaveLength(1))
    expect(posts(`/api/v1/offers/${O1}/kits`)[0].body).toEqual({
      sku: 'KIT-NEW',
      name: 'Monitor con tirillas',
      price: 30,
      components: [
        { productPublicId: MON, quantity: 1, notSubstitutable: false },
        { productPublicId: TIR, quantity: 1, notSubstitutable: true },
      ],
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Crear kit para la oferta «Navidad»' })).toBeNull())
  })

  it('los avisos del servidor salen como avisos (no como error) y el renglón muestra de dónde sale su foto', async () => {
    const user = userEvent.setup()
    wrap(EDIT)
    const dialog = await openOffer(user, 'Navidad')
    const warns = await within(dialog).findByRole('status', { name: 'Avisos (no impiden guardar ni publicar)' })
    expect(warns).toHaveTextContent(WARNING)
    expect(within(dialog).queryByRole('alert')).toBeNull()
    // sin «hasta»: el aviso también en el editor de una oferta ya creada
    expect(within(dialog).getByRole('note')).toHaveTextContent(/no tiene fecha de fin/)
    // GUA-1 sin foto propia: la del producto; KIT-OF con foto propia: se puede volver a la del producto
    const table = within(dialog).getByRole('table', { name: 'Renglones (producto o kit y precio de oferta)' })
    expect(within(table).getByText('Foto del producto')).toBeInTheDocument()
    expect(within(table).getByRole('button', { name: 'Poner otra foto' })).toBeInTheDocument()
    expect(within(table).getByText('Kit de la oferta')).toBeInTheDocument()
    await user.click(within(table).getByRole('button', { name: 'Volver a la del producto' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'DELETE' && c.url.pathname === `/api/v1/offers/${O1}/items/12/image`)).toBe(true))
  })

  it('con solo sales.view el editor es de lectura', async () => {
    const user = userEvent.setup()
    wrap(['sales.view'])
    const dialog = await openOffer(user, 'Navidad')
    expect(await within(dialog).findByText('GUA-1')).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Crear kit para esta oferta' })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Guardar datos' })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Desactivar' })).toBeNull()
    expect(within(dialog).getByLabelText('Nombre de la oferta')).toBeDisabled()
  })
})
