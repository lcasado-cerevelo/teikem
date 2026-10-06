// Lote F17 (Rentas F-R1) — "Convertir a serie" en la ficha del producto sobre un fetch simulado: el botón solo con
// inventory.manage + inventory.adjust, WMS_LOTSERIAL y un producto sin seguimiento con existencia; captura por posición con
// pegar varias líneas, repetidas y conteo con el mensaje exacto; confirmación de neto cero; cuerpo enviado y los 409/422 del
// servidor tal cual.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ProductDetailScreen from './ProductDetailScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], product: null as unknown, balances: null as unknown, post: null as null | (() => Response | unknown) }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const P = 'aaaaaaaa-0000-0000-0000-000000000001'
const product = (trackingTypeCode = 'NONE', qtyOnHand = 3) => ({
  product: { id: 1, publicId: P, sku: 'CAMA-1', name: 'Cama de hospital', trackingTypeCode, qtyOnHand, qtyAvailable: qtyOnHand, isOwn: true, isActive: true },
  rowVersion: 'RV==',
})
const BALANCES = {
  total: 2,
  items: [
    { binId: 10, binCode: 'A-01', warehouseCode: 'ALM-01', zoneCode: 'RSV', qtyOnHand: 2, qtyReserved: 0 },
    { binId: 11, binCode: 'B-02', warehouseCode: 'ALM-01', zoneCode: 'RSV', qtyOnHand: 1, qtyReserved: 0 },
  ],
}

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method !== 'GET' && mock.post) return mock.post()
  if (method === 'GET' && p === `/api/v1/products/${P}`) return mock.product ?? product()
  if (method === 'GET' && p === '/api/v1/inventory/balances') return mock.balances ?? BALANCES
  if (method === 'GET') return []
  return product('SERIAL')
}

function wrap(permissions: string[], modules = ['WMS_LOTSERIAL']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[`/warehouse/products/${P}?tab=serials`]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={modules}>
          <Routes>
            <Route path="/warehouse/products/:publicId" element={<ProductDetailScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const BOTH = ['inventory.view', 'inventory.manage', 'inventory.adjust']
const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ title, status, code }), { status, headers: { 'Content-Type': 'application/problem+json' } })

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.product = null
  mock.balances = null
  mock.post = null
})

describe('Convertir a serie: botón', () => {
  it('con inventory.manage + inventory.adjust y un producto sin seguimiento con existencia', async () => {
    wrap(BOTH)
    expect(await screen.findByRole('button', { name: 'Convertir a serie' })).toBeInTheDocument()
  })

  it('no aparece sin inventory.adjust, sin el módulo, con seguimiento por serie o sin existencia', async () => {
    const cases: [string[], string[], unknown][] = [
      [['inventory.view', 'inventory.manage'], ['WMS_LOTSERIAL'], product()],
      [['inventory.view', 'inventory.adjust'], ['WMS_LOTSERIAL'], product()],
      [BOTH, [], product()],
      [BOTH, ['WMS_LOTSERIAL'], product('SERIAL')],
      [BOTH, ['WMS_LOTSERIAL'], product('NONE', 0)],
    ]
    for (const [perms, modules, dto] of cases) {
      mock.product = dto
      const view = wrap(perms, modules)
      await screen.findByRole('heading', { level: 1 })
      expect(screen.queryByRole('button', { name: 'Convertir a serie' })).toBeNull()
      view.unmount()
    }
  })
})

describe('Convertir a serie: captura y confirmación', () => {
  it('una caja por posición; repetidas y conteo con el mensaje exacto; confirmación de neto cero; cuerpo enviado', async () => {
    const user = userEvent.setup()
    mock.post = () => ({ product: product('SERIAL'), serialCount: 3 })
    wrap(BOTH)
    await user.click(await screen.findByRole('button', { name: 'Convertir a serie' }))
    const dialog = screen.getByRole('dialog', { name: 'Convertir CAMA-1 a serie' })
    const a = await within(dialog).findByLabelText('Series para A-01 · ALM-01 (2 en mano)')
    const b = within(dialog).getByLabelText('Series para B-02 · ALM-01 (1 en mano)')
    // pegar varias líneas (con una repetida en otra posición, sin distinguir mayúsculas)
    await user.click(a)
    await user.paste('SN-1\nSN-2')
    await user.click(b)
    await user.paste('sn-2')
    expect(within(dialog).getByText('2 de 2 series')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Revisar y convertir' }))
    expect(within(dialog).getAllByText('El número de serie sn-2 está repetido.').length).toBeGreaterThan(0)
    await user.clear(b)
    await user.clear(a)
    await user.type(a, 'SN-1')
    expect(within(dialog).getAllByText('Capture 2 número(s) de serie para A-01 (hay 1).').length).toBeGreaterThan(0)
    expect(mock.calls.filter((c) => c.method === 'POST')).toHaveLength(0)

    await user.type(a, '{Enter}SN-2')
    await user.type(b, 'SN-3')
    await user.click(within(dialog).getByRole('button', { name: 'Revisar y convertir' }))
    expect(within(dialog).getByText('Se darán de alta 3 series de CAMA-1 en 2 posición(es).')).toBeInTheDocument()
    expect(within(dialog).getByText(/Es un movimiento neto cero/)).toBeInTheDocument()
    expect(within(dialog).getByText(/motivo «Conversión a serie»/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Convertir a serie' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const post = mock.calls.find((c) => c.method === 'POST')!
    expect(post.url.pathname).toBe(`/api/v1/products/${P}/convert-to-serial`)
    expect(post.body).toEqual({
      positions: [
        { binId: 10, serialNumbers: ['SN-1', 'SN-2'] },
        { binId: 11, serialNumbers: ['SN-3'] },
      ],
      notes: null,
      rowVersion: 'RV==',
    })
    expect(await screen.findByText('CAMA-1 ahora se controla por serie: 3 series dadas de alta.')).toBeInTheDocument()
  })

  it('el 409 del servidor (series ya registradas) vuelve a la captura con el mensaje tal cual', async () => {
    const user = userEvent.setup()
    mock.post = () => problem(409, 'La serie SN-1 ya está en inventario.', 'conflict')
    wrap(BOTH)
    await user.click(await screen.findByRole('button', { name: 'Convertir a serie' }))
    const dialog = screen.getByRole('dialog', { name: 'Convertir CAMA-1 a serie' })
    await user.type(await within(dialog).findByLabelText(/Series para A-01/), 'SN-1{Enter}SN-2')
    await user.type(within(dialog).getByLabelText(/Series para B-02/), 'SN-3')
    await user.click(within(dialog).getByRole('button', { name: 'Revisar y convertir' }))
    await user.click(within(dialog).getByRole('button', { name: 'Convertir a serie' }))
    expect(await within(dialog).findByText('La serie SN-1 ya está en inventario.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Revisar y convertir' })).toBeInTheDocument()
  })

  it('unidades reservadas: el aviso del 409 del servidor y no deja convertir', async () => {
    const user = userEvent.setup()
    mock.balances = { total: 1, items: [{ ...BALANCES.items[0], qtyReserved: 1 }] }
    wrap(BOTH)
    await user.click(await screen.findByRole('button', { name: 'Convertir a serie' }))
    const dialog = screen.getByRole('dialog', { name: 'Convertir CAMA-1 a serie' })
    expect(await within(dialog).findByText('El producto CAMA-1 tiene unidades reservadas; libérelas antes de convertirlo.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Revisar y convertir' })).toBeDisabled()
  })
})
