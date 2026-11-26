// Lote 247 — «Precios especiales» sobre un fetch simulado: la tabla (precio, estado, lista general, se cobra hoy con su origen), el aviso de «más caro que la lista
// general», los de la cadena y las listas en la ficha del cliente, alta con validación y cuerpo, quitar con confirmación, los permisos y el módulo apagado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { SpecialPricesPanel } from './SpecialPricesPanel'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], fail: null as null | Response }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    if (mock.fail && call.method !== 'GET') return mock.fail
    const out = route(call)
    return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const CLIENT = 'aaaaaaaa-0000-0000-0000-0000000000c1'
const CHAIN = 'aaaaaaaa-0000-0000-0000-0000000000c2'
const PROD = 'aaaaaaaa-0000-0000-0000-0000000000a1'
const ROW = (itemId: number, sku: string, extra: object = {}) => ({
  itemId, priceListPublicId: 'l1', productPublicId: PROD, sku, productName: `Producto ${sku}`, brand: null, isKit: false, price: 8.5, validFrom: '2026-11-01', validTo: null,
  state: 'ACTIVE', daysToExpire: null, generalListPrice: 10, aboveGeneral: false, productSalePrice: 12, productIsActive: true, chargedPrice: 8.5, chargedSource: 'CLIENT_LIST', chargedSourceName: null, ...extra,
})
const DTO = (kind: string, extra: object = {}) => ({
  ownerKind: kind, ownerPublicId: kind === 'CLIENT' ? CLIENT : CHAIN, ownerName: 'Farmacia Central', ownerIsActive: true, chainPublicId: null, chainName: null, chainRowCount: 0,
  rows: [ROW(1, 'MED-1'), ROW(2, 'MED-2', { price: 11, aboveGeneral: true, chargedPrice: 10, chargedSource: 'GENERAL_LIST' })], chainRows: [], clientLists: [], warnings: [], ...extra,
})
let dto: unknown

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p.startsWith('/api/v1/special-prices/')) return dto
  if (method === 'GET' && p === '/api/v1/products')
    return { total: 1, skip: 0, take: 50, items: [{ id: 1, publicId: PROD, sku: 'MED-3', name: 'Medidor', isOwn: true, isActive: true, salePrice: 12 }] }
  return []
}

function wrap(kind: 'client' | 'chain', permissions: string[], modules = ['SALES_ORDERS'], ownerActive = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={modules}>
          <SpecialPricesPanel ownerKind={kind} ownerPublicId={kind === 'client' ? CLIENT : CHAIN} ownerActive={ownerActive} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
const EDIT = ['sales.view', 'sales.pricelist']
const sent = (method: string) => mock.calls.filter((c) => c.method === method && c.url.pathname.startsWith('/api/v1/special-prices/'))

beforeAll(() => setLang('es'))
beforeEach(() => {
  window.localStorage.clear()
  mock.calls = []
  mock.fail = null
  dto = DTO('CLIENT')
})

describe('SpecialPricesPanel', () => {
  it('la tabla muestra precio, lista general, se cobra hoy con su origen y el aviso de «más caro que la lista general»', async () => {
    dto = DTO('CLIENT', { warnings: [{ code: 'SPECIAL_ABOVE_GENERAL', message: 'Precio especial más caro que la lista general: MED-2 a $11.00.', productPublicId: PROD }] })
    wrap('client', EDIT)
    const table = await screen.findByRole('table', { name: 'Precios especiales' })
    for (const h of ['Producto', 'Precio', 'Desde', 'Hasta', 'Estado', 'Lista general', 'Se cobra hoy']) expect(within(table).getByRole('button', { name: h })).toBeInTheDocument()
    expect(within(table).getByText('MED-1')).toBeInTheDocument()
    expect(within(table).getAllByText('Sin fecha final').length).toBe(2)
    expect(within(table).getByText('Precio especial')).toBeInTheDocument() // origen CLIENT_LIST
    expect(within(table).getAllByText('Lista general').length).toBeGreaterThan(1) // el encabezado y el origen GENERAL_LIST
    expect(within(table).getByText('Más caro que la lista general')).toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Avisos de precios especiales' })).toHaveTextContent('MED-2 a $11.00')
  })

  it('en la ficha del cliente salen los de su cadena (solo lectura, con enlace) y las listas que lo incluyen', async () => {
    dto = DTO('CLIENT', {
      chainPublicId: CHAIN, chainName: 'Walgreens', chainRowCount: 1, chainRows: [ROW(9, 'CAD-1', { chargedSource: 'CHAIN_LIST' })],
      clientLists: [{ priceListPublicId: 'l9', name: 'Lista de farmacias', state: 'ACTIVE', itemCount: 4, matchedBy: 'CHAIN' }],
    })
    wrap('client', EDIT)
    const section = await screen.findByRole('region', { name: 'De su cadena «Walgreens»' })
    expect(within(section).getByText('CAD-1')).toBeInTheDocument()
    expect(within(section).getByRole('link', { name: 'Abrir la cadena' })).toHaveAttribute('href', `/catalog/chains?id=${CHAIN}`)
    // solo lectura: sin acciones en las filas de la cadena
    expect(within(section).queryByRole('button', { name: 'Quitar' })).toBeNull()
    expect(screen.getByText('Lista de farmacias · por su cadena')).toBeInTheDocument()
  })

  it('alta: valida antes de enviar y manda el cuerpo; el error del servidor sale tal cual', async () => {
    const user = userEvent.setup()
    wrap('chain', EDIT)
    dto = DTO('CHAIN')
    await screen.findByRole('table', { name: 'Precios especiales' })
    await user.click(screen.getByRole('button', { name: 'Agregar precio especial' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Escoja el producto o kit.')
    expect(sent('POST')).toHaveLength(0)
    await user.click(screen.getByRole('combobox', { name: 'Producto o kit' }))
    await user.click(await screen.findByRole('option', { name: /MED-3/ }))
    await user.type(screen.getByLabelText('Precio'), '9.25')
    mock.fail = new Response(JSON.stringify({ title: 'La cadena está inactiva.', status: 422 }), { status: 422, headers: { 'Content-Type': 'application/problem+json' } })
    await user.click(screen.getByRole('button', { name: 'Agregar precio especial' }))
    await waitFor(() => expect(sent('POST')).toHaveLength(1))
    expect(sent('POST')[0].body).toMatchObject({ productPublicId: PROD, price: 9.25, validTo: null })
    expect(await screen.findByText('La cadena está inactiva.')).toBeInTheDocument()
  })

  it('quitar pide confirmación y borra el renglón', async () => {
    const user = userEvent.setup()
    wrap('client', EDIT)
    const table = await screen.findByRole('table', { name: 'Precios especiales' })
    const row = within(table).getByText('MED-1').closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Quitar' }))
    const dialog = await screen.findByRole('dialog', { name: '¿Quitar el precio especial?' })
    expect(sent('DELETE')).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(sent('DELETE')).toHaveLength(1))
    expect(sent('DELETE')[0].url.pathname).toBe(`/api/v1/special-prices/clients/${CLIENT}/1`)
  })

  it('con solo sales.view no hay alta ni acciones; sin el módulo o sin permiso no se pinta; con dueño inactivo no admite precios', async () => {
    const view = wrap('client', ['sales.view'])
    await screen.findByRole('table', { name: 'Precios especiales' })
    expect(screen.queryByRole('button', { name: 'Agregar precio especial' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Cambiar precio' })).toBeNull()
    view.unmount()
    const off = wrap('client', EDIT, [])
    expect(off.container).toBeEmptyDOMElement()
    off.unmount()
    const noPerm = wrap('client', ['clients.read'])
    expect(noPerm.container).toBeEmptyDOMElement()
    noPerm.unmount()
    wrap('client', EDIT, ['SALES_ORDERS'], false)
    expect(await screen.findByText('Está inactivo: no admite precios nuevos.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Agregar precio especial' })).toBeNull()
  })
})
