// Lote 232 — ficha de un kit ARMADO, pestaña «Armados» (`?tab=assemblies`). Sobre un fetch simulado:
// - distintivo «Kit armado», historial ARM-##### (fecha, quién, kits, quedan, lote del kit, piezas con su lote) y «se pueden armar»;
// - «Armar kits»: cantidad, posición destino y de dónde sale cada pieza (sugerida por la rotación o escogida), resumen antes de confirmar
//   (piezas que salen, kits que entran, nota del costo) y lo que viaja; el 409 del servidor («con ese lote alcanzan N») sale tal cual;
// - «Desarmar» sobre un armado del historial: parcial, resumen de lo que vuelve a cada lote y lo que viaja; más de lo que queda se avisa sin llamar;
// - un producto normal no tiene la pestaña; sin inventory.adjust no hay Armar ni Desarmar.
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
const mock = vi.hoisted(() => ({ calls: [] as Call[], assembled: true, assembleFails: false }))
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
const ARM = 'bbbbbbbb-0000-0000-0000-000000000001'

const ASSEMBLY = {
  publicId: ARM,
  number: 'ARM-00001',
  kitPublicId: KIT,
  kitSku: 'KIT-1',
  quantity: 3,
  disassembledQty: 1,
  remaining: 2,
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  toBinId: 10,
  toBinCode: 'K-01',
  kitLotId: 50,
  kitLotNumber: 'ARM-00001',
  kitLotExpiry: '2027-01-31',
  unitCost: 12.5,
  totalCost: 37.5,
  createdAtUtc: '2026-11-10T14:00:00Z',
  createdByName: 'Ana Pérez',
  lines: [
    { productPublicId: A, sku: 'GUA-1', name: 'Guantes', lotId: 1, lotNumber: 'L1', expiryDate: '2027-01-31', fromBinId: 1, fromBinCode: 'A-01', qtyPerKit: 2, quantity: 6, returnedQty: 2 },
    { productPublicId: B, sku: 'TIR-1', name: 'Tirillas', fromBinId: 3, fromBinCode: 'B-01', qtyPerKit: 1, quantity: 3, returnedQty: 1 },
  ],
  disassemblies: [{ publicId: 'cccccccc-0000-0000-0000-000000000001', number: 'DES-00001', quantity: 1, warehousePublicId: WH, warehouseCode: 'ALM-01', createdAtUtc: '2026-11-10T15:00:00Z', lines: [] }],
}

function route(method: string, url: URL): unknown {
  const p = url.pathname
  if (p === `/api/v1/products/${KIT}` && method === 'GET')
    return {
      product: { id: 99, publicId: KIT, sku: 'KIT-1', name: 'Kit de glucosa', trackingTypeCode: 'LOT', qtyOnHand: 2, qtyAvailable: 2, isOwn: true, isActive: true, isAssembledKit: mock.assembled },
      preferredWarehousePublicId: WH,
      preferredBinId: 10,
      rowVersion: 'RV==',
    }
  if (p === `/api/v1/products/${KIT}/assembled-kit`)
    return {
      productPublicId: KIT,
      sku: 'KIT-1',
      isAssembledKit: true,
      trackingTypeCode: 'LOT',
      compositionLocked: true,
      pieces: [
        { productPublicId: A, sku: 'GUA-1', name: 'Guantes', quantity: 2, sortOrder: 1, isActive: true, trackingTypeCode: 'LOT' },
        { productPublicId: B, sku: 'TIR-1', name: 'Tirillas', quantity: 1, sortOrder: 2, isActive: true, trackingTypeCode: 'NONE' },
      ],
      availability: [{ warehousePublicId: WH, warehouseCode: 'ALM-01', canAssemble: 2, canAssembleTotal: 4 }],
      qtyOnHand: 2,
      qtyAvailable: 2,
    }
  if (p === `/api/v1/products/${KIT}/assembled-kit/assemblies` && method === 'GET') return { total: 1, skip: 0, take: 25, items: [ASSEMBLY] }
  if (p === `/api/v1/products/${KIT}/assembled-kit/assemblies` && method === 'POST') {
    if (mock.assembleFails)
      return new Response(
        JSON.stringify({
          title: 'El lote L2 de GUA-1 solo alcanza para 2 kit(s): hay 4 disponible y cada kit lleva 2. Arme 2 con ese lote y el resto en otro armado con otro lote.',
          status: 409,
          code: 'insufficient_stock',
          errors: { 'pieces[0]': ['El lote L2 de GUA-1 solo alcanza para 2 kit(s): hay 4 disponible y cada kit lleva 2. Arme 2 con ese lote y el resto en otro armado con otro lote.'] },
        }),
        { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
      )
    return { assembly: { ...ASSEMBLY, number: 'ARM-00002', quantity: 3, totalCost: 37.5, toBinCode: 'K-01' }, movements: { transactions: [], balances: [] } }
  }
  if (p === `/api/v1/products/${KIT}/assembled-kit/assemblies/${ARM}/disassemble`)
    return { assembly: ASSEMBLY, disassembly: { number: 'DES-00002', quantity: 1 }, movements: { transactions: [], balances: [] } }
  if (p === '/api/v1/inventory/exit-options')
    return {
      total: 3,
      items: [
        { productPublicId: A, binId: 1, binCode: 'A-01', lotId: 1, lotNumber: 'L1', expiryDate: '2027-01-31', available: 5, rank: 1 },
        { productPublicId: A, binId: 2, binCode: 'A-02', lotId: 2, lotNumber: 'L2', available: 4, rank: 2 },
        { productPublicId: B, binId: 3, binCode: 'B-01', available: 10, rank: 1 },
      ],
    }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}`) return { id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }
  if (p === `/api/v1/warehouses/${WH}/zones`) return [{ id: 1, code: 'A', zoneTypeCode: 'STORAGE', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) return { total: 1, skip: 0, take: 50, items: [{ id: 10, code: 'K-01', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true }] }
  if (p.endsWith('/lots')) return []
  return []
}

function wrap(path: string, permissions = ['inventory.view', 'inventory.manage', 'inventory.adjust']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/products/:publicId" element={<ProductDetailScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const postTo = (suffix: string) => mock.calls.find((c) => c.method === 'POST' && c.url.pathname.endsWith(suffix))

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.assembled = true
  mock.assembleFails = false
})

describe('Ficha de un kit armado: Armados (lote 232)', () => {
  it('distintivo, pestaña «Armados» con el historial (número, quién, quedan, lote, piezas) y «se pueden armar»', async () => {
    wrap(`/warehouse/products/${KIT}?tab=assemblies`)
    const tabs = await screen.findByRole('tablist', { name: 'Productos e inventario' })
    expect(Array.from(tabs.querySelectorAll('[role="tab"]')).map((b) => b.textContent)).toEqual(['Lotes', 'Series', 'Armados'])
    expect(screen.getByText('Kit armado')).toBeInTheDocument()
    expect(await screen.findByText('ALM-01: 2 en un armado · 4 juntando lotes')).toBeInTheDocument()
    expect(screen.getByText(/no queda nada «reservado»/i)).toBeInTheDocument()
    expect(await screen.findAllByText('ARM-00001')).not.toHaveLength(0)
    expect(screen.getAllByText('Ana Pérez').length).toBeGreaterThan(0)
    expect(screen.getAllByText('GUA-1 · A-01 · lote L1 × 6').length).toBeGreaterThan(0)
    expect(screen.getAllByText('DES-00001').length).toBeGreaterThan(0)
  })

  it('Armar: de dónde sale cada pieza (sugerida o escogida), resumen antes de confirmar y lo que viaja', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/products/${KIT}?tab=assemblies`)
    await user.click(await screen.findByRole('button', { name: 'Armar kits' }))
    const dialog = await screen.findByRole('dialog', { name: 'Armar kits KIT-1' })
    expect(within(dialog).getByText(/Armar saca del inventario las piezas/)).toBeInTheDocument()
    expect(await within(dialog).findByText('Con lo disponible en este almacén se pueden armar 2 (en un solo armado).')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Cantidad de kits/), '2')
    // GUA-1: la sugerida es la de rank 1 (A-01 · lote L1); se escoge el lote L2
    const from = await within(dialog).findByLabelText(/GUA-1: de dónde sale/)
    await waitFor(() => expect(within(from).getAllByRole('option')).toHaveLength(3))
    expect(within(from).getAllByRole('option')[0]).toHaveTextContent('Sugerida por la rotación: A-01 · lote L1 · vence')
    await user.selectOptions(from, '2:2')
    await user.click(within(dialog).getByRole('button', { name: 'Revisar' }))

    expect(await within(dialog).findByRole('heading', { name: 'Resumen antes de confirmar' })).toBeInTheDocument()
    const rows = within(dialog).getAllByRole('row').slice(1)
    expect(rows[0]).toHaveTextContent('GUA-1')
    expect(rows[0]).toHaveTextContent('A-02 · lote L2')
    expect(rows[0]).toHaveTextContent('escogida')
    expect(rows[1]).toHaveTextContent('TIR-1')
    expect(rows[1]).toHaveTextContent('B-01')
    expect(rows[1]).toHaveTextContent('sugerida')
    expect(within(dialog).getByText('Kits que entran: 2 en K-01')).toBeInTheDocument()
    expect(within(dialog).getByText(/el valor total del inventario no cambia/)).toBeInTheDocument()
    expect(postTo('/assemblies')).toBeUndefined()

    await user.click(within(dialog).getByRole('button', { name: 'Armar 2 kit(s)' }))
    await waitFor(() => expect(postTo('/assemblies')).toBeDefined())
    expect(postTo('/assemblies')!.body).toEqual({ quantity: 2, toBinId: 10, warehousePublicId: WH, notes: null, pieces: [{ productPublicId: A, lotId: 2, binId: 2 }] })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Armar kits KIT-1' })).toBeNull())
  })

  it('Armar: el 409 del servidor («con ese lote alcanzan N») sale tal cual y el modal sigue abierto', async () => {
    mock.assembleFails = true
    const user = userEvent.setup()
    wrap(`/warehouse/products/${KIT}?tab=assemblies`)
    await user.click(await screen.findByRole('button', { name: 'Armar kits' }))
    const dialog = await screen.findByRole('dialog', { name: 'Armar kits KIT-1' })
    await user.type(await within(dialog).findByLabelText(/^Cantidad de kits/), '3')
    await user.click(within(dialog).getByRole('button', { name: 'Revisar' }))
    await user.click(await within(dialog).findByRole('button', { name: 'Armar 3 kit(s)' }))
    expect((await within(dialog).findAllByText(/El lote L2 de GUA-1 solo alcanza para 2 kit\(s\)/)).length).toBeGreaterThan(0)
    expect(screen.getByRole('dialog', { name: 'Armar kits KIT-1' })).toBeInTheDocument()
  })

  it('Armar sin cantidad: «Indique cuántos kits.» sin llamar al API', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/products/${KIT}?tab=assemblies`)
    await user.click(await screen.findByRole('button', { name: 'Armar kits' }))
    const dialog = await screen.findByRole('dialog', { name: 'Armar kits KIT-1' })
    await user.click(await within(dialog).findByRole('button', { name: 'Revisar' }))
    expect(await within(dialog).findByText('Indique cuántos kits.')).toBeInTheDocument()
    expect(postTo('/assemblies')).toBeUndefined()
  })

  it('Desarmar desde el historial: parcial, lo que vuelve a cada lote y lo que viaja; más de lo que queda se avisa sin llamar', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/products/${KIT}?tab=assemblies`)
    const [btn] = await screen.findAllByRole('button', { name: 'Desarmar' })
    await user.click(btn)
    const dialog = await screen.findByRole('dialog', { name: 'Desarmar del armado ARM-00001' })
    expect(within(dialog).getByText('De este armado quedan 2 kit(s) por desarmar.')).toBeInTheDocument()
    const qty = within(dialog).getByLabelText(/^Cantidad de kits/)
    expect(qty).toHaveValue(2)
    await user.clear(qty)
    await user.type(qty, '3')
    await user.click(within(dialog).getByRole('button', { name: 'Desarmar 3 kit(s)' }))
    expect(await within(dialog).findByText('Del armado ARM-00001 quedan 2 kit(s) por desarmar; pidió 3.')).toBeInTheDocument()
    expect(postTo('/disassemble')).toBeUndefined()

    await user.clear(qty)
    await user.type(qty, '1')
    const rows = within(dialog).getAllByRole('row').slice(1)
    expect(rows[0]).toHaveTextContent('GUA-1')
    expect(rows[0]).toHaveTextContent('L1')
    expect(rows[0]).toHaveTextContent('2')
    expect(rows[0]).toHaveTextContent('A-01')
    expect(rows[1]).toHaveTextContent('TIR-1')
    expect(rows[1]).toHaveTextContent('B-01')
    await user.click(within(dialog).getByRole('button', { name: 'Desarmar 1 kit(s)' }))
    await waitFor(() => expect(postTo('/disassemble')).toBeDefined())
    expect(postTo('/disassemble')!.url.pathname).toBe(`/api/v1/products/${KIT}/assembled-kit/assemblies/${ARM}/disassemble`)
    expect(postTo('/disassemble')!.body).toEqual({ quantity: 1, warehousePublicId: null, toBinId: null, notes: null })
  })

  it('sin inventory.adjust: se ve el historial pero no Armar ni Desarmar', async () => {
    wrap(`/warehouse/products/${KIT}?tab=assemblies`, ['inventory.view'])
    expect(await screen.findAllByText('ARM-00001')).not.toHaveLength(0)
    expect(screen.queryByRole('button', { name: 'Armar kits' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Desarmar' })).toBeNull()
  })

  it('un producto normal no tiene la pestaña «Armados» ni el distintivo (y ?tab=assemblies abre Lotes)', async () => {
    mock.assembled = false
    wrap(`/warehouse/products/${KIT}?tab=assemblies`)
    const tabs = await screen.findByRole('tablist', { name: 'Productos e inventario' })
    expect(Array.from(tabs.querySelectorAll('[role="tab"]')).map((b) => b.textContent)).toEqual(['Lotes', 'Series'])
    expect(screen.queryByText('Kit armado')).toBeNull()
    expect(mock.calls.some((c) => c.url.pathname.includes('assembled-kit'))).toBe(false)
  })
})
