// Lote 232 — «Nuevo producto» pregunta si es un kit (kit ARMADO). Sobre un fetch simulado:
// - «No» por omisión: un producto normal no ve composición ni «cuántos armar», conserva Rastreo y Costo, y al guardar no viaja nada de kit;
// - «Sí»: aparecen la composición y «¿Cuántos kits arma ahora?» (almacén, posición, «se pueden armar N»), el rastreo y el costo pasan a
//   informativos y viaja `isAssembledKit`, `kitPieces`, rastreo/costo null y lo de armar;
// - una pieza que es kit se rechaza antes de llamar al API con el mensaje del servidor;
// - en edición, un kit con composición bloqueada se ve en solo lectura con «La composición ya no se cambia: cree un kit nuevo» y el acceso a
//   «Armar y desarmar»; una composición libre se cambia con PUT …/assembled-kit al guardar.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import type { ProductDetailDto } from './api'
import { ProductEditorModal } from './ProductEditorModal'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], locked: true }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.text()
    mock.calls.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    const body = route(req.method, url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const KIT = 'aaaaaaaa-0000-0000-0000-0000000000a1'
const A = 'aaaaaaaa-0000-0000-0000-00000000000a'
const B = 'aaaaaaaa-0000-0000-0000-00000000000b'
const K = 'aaaaaaaa-0000-0000-0000-00000000000c'

const LOOKUPS: Record<string, { code: string; label: string; sortOrder: number }[]> = {
  TrackingType: [
    { code: 'NONE', label: 'Ninguno', sortOrder: 1 },
    { code: 'LOT', label: 'Lote', sortOrder: 2 },
    { code: 'SERIAL', label: 'Serie', sortOrder: 3 },
  ],
  UnitOfMeasure: [{ code: 'UN', label: 'Unidad', sortOrder: 1 }],
}

const PRODUCTS = [
  { id: 1, publicId: A, sku: 'GUA-1', name: 'Guantes', trackingTypeCode: 'LOT', isActive: true, isOwn: true, qtyAvailable: 9 },
  { id: 2, publicId: B, sku: 'TIR-1', name: 'Tirillas', trackingTypeCode: 'NONE', isActive: true, isOwn: true, qtyAvailable: 10 },
  { id: 3, publicId: K, sku: 'KIT-X', name: 'Otro kit', trackingTypeCode: 'NONE', isActive: true, isOwn: true, isAssembledKit: true },
]

function route(method: string, url: URL): unknown {
  const p = url.pathname
  if (p.startsWith('/api/v1/catalogs/')) return LOOKUPS[p.slice('/api/v1/catalogs/'.length)] ?? []
  if (p === '/api/v1/product-categories') return []
  if (p === '/api/v1/products/brands') return []
  if (p === '/api/v1/products/units-suggestion') return { regionCode: 'PR', dimensionUnit: 'IN', weightUnit: 'LB', dimensionUnits: ['IN', 'CM', 'FT', 'M'], weightUnits: ['LB', 'KG'] }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/zones`) return [{ id: 1, code: 'A', zoneTypeCode: 'STORAGE', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) return { total: 1, skip: 0, take: 50, items: [{ id: 10, code: 'K-01', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true }] }
  if (p === '/api/v1/products' && method === 'GET') return { total: PRODUCTS.length, skip: 0, take: 20, items: PRODUCTS }
  if (p === '/api/v1/products' && method === 'POST') return { product: { id: 99, publicId: KIT, isAssembledKit: true } }
  const one = PRODUCTS.find((x) => p === `/api/v1/products/${x.publicId}`)
  if (one && method === 'GET') return { product: one, rowVersion: 'RV==' }
  if (p === '/api/v1/inventory/exit-options')
    return {
      total: 3,
      items: [
        // GUA-1 (2 por kit): lote L1 con 5 y lote L2 con 4 → en un armado alcanza para 2; TIR-1 (1 por kit) 10
        { productPublicId: A, binId: 1, binCode: 'A-01', lotId: 1, lotNumber: 'L1', available: 5, rank: 1 },
        { productPublicId: A, binId: 2, binCode: 'A-02', lotId: 2, lotNumber: 'L2', available: 4, rank: 2 },
        { productPublicId: B, binId: 3, binCode: 'B-01', available: 10, rank: 1 },
      ],
    }
  if (p === `/api/v1/products/${KIT}/assembled-kit` && method === 'GET')
    return {
      productPublicId: KIT,
      sku: 'KIT-1',
      isAssembledKit: true,
      trackingTypeCode: 'LOT',
      compositionLocked: mock.locked,
      pieces: [
        { productPublicId: A, sku: 'GUA-1', name: 'Guantes', quantity: 2, sortOrder: 1, isActive: true, trackingTypeCode: 'LOT' },
        { productPublicId: B, sku: 'TIR-1', name: 'Tirillas', quantity: 1, sortOrder: 2, isActive: true, trackingTypeCode: 'NONE' },
      ],
      availability: [{ warehousePublicId: WH, warehouseCode: 'ALM-01', canAssemble: 2, canAssembleTotal: 4 }],
      qtyOnHand: 3,
      qtyAvailable: 3,
    }
  if (p === `/api/v1/products/${KIT}/assembled-kit` && method === 'PUT') return { productPublicId: KIT, isAssembledKit: true, compositionLocked: false, pieces: [], availability: [] }
  if (p === `/api/v1/products/${KIT}` && method === 'PATCH') return kitDetail()
  return new Response(JSON.stringify({ title: 'Sin acceso', code: 'forbidden' }), { status: 403 })
}

function kitDetail(): ProductDetailDto {
  return {
    product: { id: 99, publicId: KIT, sku: 'KIT-1', name: 'Kit de glucosa', trackingTypeCode: 'LOT', baseUomCode: 'UN', qtyOnHand: 3, qtyAvailable: 3, isActive: true, isOwn: true, isAssembledKit: true },
    hasMovements: true,
    rowVersion: 'RV==',
    kitPieces: [],
  }
}

function wrap(ui: ReactNode, permissions = ['inventory.view', 'inventory.manage', 'inventory.adjust']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          {ui}
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

function findDialog(name: string): Promise<HTMLElement> {
  return waitFor(() => {
    const dialog = screen.getByRole('dialog', { name })
    if (!dialog.querySelector('form')) throw new Error('cargando')
    return dialog
  })
}

async function pickPiece(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement, n: number, sku: RegExp) {
  await user.click(within(dialog).getByRole('combobox', { name: `Pieza ${n}` }))
  await user.click(await within(dialog).findByRole('option', { name: sku }))
}

const posted = () => mock.calls.find((c) => c.method === 'POST' && c.url.pathname === '/api/v1/products')?.body as Record<string, unknown> | undefined

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.locked = true
})

describe('Nuevo producto: ¿es un kit? (lote 232)', () => {
  it('«No» por omisión: sin composición ni «cuántos armar»; Rastreo y Costo normales; al guardar no viaja nada de kit', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={null} onClose={() => {}} />)
    const dialog = await findDialog('Nuevo producto')
    const question = within(dialog).getByRole('group', { name: '¿Lo que va a crear es un kit?' })
    expect(within(question).getByRole('radio', { name: 'No' })).toBeChecked()
    expect(within(question).getByRole('radio', { name: 'Sí, es un kit' })).not.toBeChecked()
    expect(within(dialog).queryByRole('heading', { name: 'Composición del kit' })).toBeNull()
    expect(within(dialog).queryByText('¿Cuántos kits arma ahora?')).toBeNull()
    expect(within(dialog).getByRole('combobox', { name: /^Rastreo/ })).toBeInTheDocument()
    expect(within(dialog).getByLabelText(/^Costo de compra/)).not.toBeDisabled()

    await user.type(within(dialog).getByLabelText(/^SKU/), 'TOR-1')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Tornillo')
    await user.click(within(dialog).getByRole('button', { name: /Guardar/ }))
    await waitFor(() => expect(posted()).toBeDefined())
    const body = posted()!
    expect(body).toMatchObject({ sku: 'TOR-1', trackingType: 'NONE' })
    for (const k of ['isAssembledKit', 'kitPieces', 'assembleQuantity', 'assembleToBinId', 'assembleWarehousePublicId']) expect(body).not.toHaveProperty(k)
  })

  it('«Sí»: composición y «cuántos armar» ahí mismo; rastreo y costo informativos; «se pueden armar 2»; lo que viaja al servidor', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={null} onClose={() => {}} />)
    const dialog = await findDialog('Nuevo producto')
    await user.click(within(dialog).getByRole('radio', { name: 'Sí, es un kit' }))
    expect(await within(dialog).findByRole('heading', { name: 'Composición del kit' })).toBeInTheDocument()
    expect(within(dialog).getByText('¿Cuántos kits arma ahora?')).toBeInTheDocument()
    // sin «No sustituible» (el kit armado no tiene sustitución); rastreo y costo no se escriben
    expect(within(dialog).queryByText('No sustituible')).toBeNull()
    expect(within(dialog).queryByRole('combobox', { name: /^Rastreo/ })).toBeNull()
    expect(within(dialog).getByLabelText('Rastreo')).toHaveValue('Lo deciden las piezas')
    expect(within(dialog).getByLabelText('Costo de compra')).toHaveValue('Sale de las piezas al armar')
    expect(within(dialog).getByText('En un kit armado el rastreo y el costo no se escriben: los deciden sus piezas.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText(/^SKU/), 'KIT-1')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Kit de glucosa')
    await pickPiece(user, dialog, 1, /GUA-1/)
    const qty1 = within(dialog).getAllByLabelText('Cantidad')[0]
    await user.clear(qty1)
    await user.type(qty1, '2')
    await user.click(within(dialog).getByRole('button', { name: 'Agregar pieza' }))
    await pickPiece(user, dialog, 2, /TIR-1/)
    await waitFor(() => expect(within(dialog).getByLabelText('Rastreo')).toHaveValue('Por lote (lo deciden las piezas: alguna va por lote)'))

    // un solo almacén: queda escogido; se pueden armar 2 en un solo armado (un lote por pieza) y 4 juntando lotes
    expect(await within(dialog).findByText('Con lo disponible en este almacén se pueden armar 2 (en un solo armado).')).toBeInTheDocument()
    expect(within(dialog).getByText(/Juntando lotes, en varios armados, alcanzan 4\./)).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Kits a armar ahora/), '2')
    await user.click(within(dialog).getByRole('button', { name: /Guardar/ }))
    // sin posición: el mensaje del servidor y no se llama al API
    expect(await within(dialog).findByText('Para armar kits al crear el producto indique la posición donde quedan.')).toBeInTheDocument()
    expect(posted()).toBeUndefined()

    await user.click(within(dialog).getByRole('combobox', { name: /^Posición donde quedan los kits/ }))
    await user.click(await within(dialog).findByRole('option', { name: /K-01/ }))
    await user.click(within(dialog).getByRole('button', { name: /Guardar/ }))
    await waitFor(() => expect(posted()).toBeDefined())
    expect(posted()).toMatchObject({
      sku: 'KIT-1',
      name: 'Kit de glucosa',
      isAssembledKit: true,
      kitPieces: [
        { productPublicId: A, quantity: 2 },
        { productPublicId: B, quantity: 1 },
      ],
      trackingType: null,
      purchaseCost: null,
      assembleQuantity: 2,
      assembleWarehousePublicId: WH,
      assembleToBinId: 10,
    })
  })

  it('una pieza que es kit: el mensaje del servidor bajo la composición y no se llama al API', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={null} onClose={() => {}} />)
    const dialog = await findDialog('Nuevo producto')
    await user.click(within(dialog).getByRole('radio', { name: 'Sí, es un kit' }))
    await user.type(within(dialog).getByLabelText(/^SKU/), 'KIT-2')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Kit doble')
    await pickPiece(user, dialog, 1, /KIT-X/)
    await user.click(within(dialog).getByRole('button', { name: /Guardar/ }))
    expect(await within(dialog).findByText('KIT-X es un kit: un kit armado no lleva otro kit como pieza.')).toBeInTheDocument()
    expect(posted()).toBeUndefined()
  })

  it('sin inventory.adjust: el kit se crea sin armar (se explica por qué) y no viaja cantidad', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={null} onClose={() => {}} />, ['inventory.view', 'inventory.manage'])
    const dialog = await findDialog('Nuevo producto')
    await user.click(within(dialog).getByRole('radio', { name: 'Sí, es un kit' }))
    expect(await within(dialog).findByText(/Armar kits pide el permiso de ajustar inventario/)).toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/^Kits a armar ahora/)).toBeNull()
    await user.type(within(dialog).getByLabelText(/^SKU/), 'KIT-3')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Kit sin armar')
    await pickPiece(user, dialog, 1, /TIR-1/)
    await user.click(within(dialog).getByRole('button', { name: /Guardar/ }))
    await waitFor(() => expect(posted()).toBeDefined())
    expect(posted()).toMatchObject({ isAssembledKit: true, kitPieces: [{ productPublicId: B, quantity: 1 }], assembleQuantity: null, assembleToBinId: null })
  })
})

describe('Editar un kit armado (lote 232)', () => {
  it('composición bloqueada: solo lectura con «ya no se cambia», existencia, «se pueden armar» y el acceso a Armar y desarmar', async () => {
    wrap(<ProductEditorModal open product={kitDetail()} onClose={() => {}} />)
    const dialog = await findDialog('Editar producto')
    expect(await within(dialog).findByText('La composición ya no se cambia: cree un kit nuevo.')).toBeInTheDocument()
    expect(within(dialog).queryByRole('combobox', { name: 'Pieza 1' })).toBeNull()
    expect(within(dialog).getByText('GUA-1')).toBeInTheDocument()
    expect(within(dialog).getByText('Kits en existencia: 3 · disponibles: 3')).toBeInTheDocument()
    expect(within(dialog).getByText('ALM-01: 2 en un armado · 4 juntando lotes')).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: 'Armar y desarmar' })).toHaveAttribute('href', `/warehouse/products/${KIT}?tab=assemblies`)
    // rastreo y dueño fijos
    expect(within(dialog).getByLabelText('Rastreo')).toHaveValue('Por lote (lo deciden las piezas: alguna va por lote)')
  })

  it('composición libre: se cambia y al guardar va con PUT …/assembled-kit (sin rastreo ni dueño en el PATCH)', async () => {
    mock.locked = false
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={kitDetail()} onClose={() => {}} />)
    const dialog = await findDialog('Editar producto')
    const qty = (await within(dialog).findAllByLabelText('Cantidad'))[0]
    await user.clear(qty)
    await user.type(qty, '3')
    await user.click(within(dialog).getByRole('button', { name: /Guardar/ }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'PUT')).toBe(true))
    const put = mock.calls.find((c) => c.method === 'PUT')!
    expect(put.url.pathname).toBe(`/api/v1/products/${KIT}/assembled-kit`)
    expect(put.body).toEqual({
      pieces: [
        { productPublicId: A, quantity: 3 },
        { productPublicId: B, quantity: 1 },
      ],
    })
  })
})
