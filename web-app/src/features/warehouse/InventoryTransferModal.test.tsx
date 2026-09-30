// Lote 14 (hallazgo 3) — transferencia en el orden origen → ítem → destino: el ítem sale de lo disponible en la posición
// de origen (producto + lote); el lote viaja como lotId y, en productos SERIAL, las series elegidas (cantidad = número de
// series); el destino arranca en el almacén de origen; no se puede transferir a la misma posición ni más de lo disponible.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { InventoryTransferModal } from './InventoryTransferModal'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.text()
    mock.calls.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    return new Response(JSON.stringify(route(req.method, url)), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const LOTP = 'aaaaaaaa-0000-0000-0000-000000000001'
const SERP = 'aaaaaaaa-0000-0000-0000-000000000002'
const BINS = [
  { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true },
  { id: 11, code: 'A-02', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true },
]

function route(method: string, url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) {
    const ids = url.searchParams.getAll('binIds').map(Number)
    const items = BINS.filter((b) => ids.length === 0 || ids.includes(b.id))
    return { total: items.length, skip: 0, take: 50, items }
  }
  if (p === '/api/v1/inventory/balances')
    return {
      total: 2,
      skip: 0,
      take: 200,
      items: [
        { id: 1, binId: 10, productPublicId: LOTP, sku: 'GLU-STR', productName: 'Tiras', lotId: 7, lotNumber: 'L-2408', qtyOnHand: 5, qtyReserved: 0, qtyAvailable: 5 },
        { id: 2, binId: 10, productPublicId: SERP, sku: 'WCH-STD', productName: 'Silla', qtyOnHand: 2, qtyReserved: 0, qtyAvailable: 2 },
      ],
    }
  if (p === `/api/v1/products/${LOTP}`) return { product: { publicId: LOTP, sku: 'GLU-STR', name: 'Tiras', trackingTypeCode: 'LOT' } }
  if (p === `/api/v1/products/${SERP}`) return { product: { publicId: SERP, sku: 'WCH-STD', name: 'Silla', trackingTypeCode: 'SERIAL' } }
  if (p === `/api/v1/products/${SERP}/serials`)
    return [
      { id: 1, serialNumber: 'SN-1', binId: 10, statusCode: 'AVAILABLE' },
      { id: 2, serialNumber: 'SN-2', binId: 10, statusCode: 'AVAILABLE' },
      { id: 3, serialNumber: 'SN-9', binId: 99, statusCode: 'AVAILABLE' },
    ]
  if (method === 'POST' && p === '/api/v1/inventory/transfers') return { transactions: [], balances: [] }
  return []
}

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.view', 'inventory.adjust']} modules={['WMS_LOTSERIAL']}>
          <InventoryTransferModal open onClose={() => {}} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

type User = ReturnType<typeof userEvent.setup>
const posts = () => mock.calls.filter((c) => c.method === 'POST')

/** Origen: almacén y posición A-01; luego el ítem cuyo texto coincide. */
async function pickOriginAndItem(user: User, dialog: HTMLElement, item: RegExp) {
  await user.click(within(dialog).getByRole('combobox', { name: /^Almacén de origen/ }))
  await user.click(await screen.findByRole('option', { name: 'ALM-01 · Almacén principal' }))
  await user.click(within(dialog).getByRole('combobox', { name: /^Posición de origen/ }))
  await user.click(await screen.findByRole('option', { name: /A-01/ }))
  await user.click(within(dialog).getByRole('combobox', { name: /^Ítem/ }))
  await user.click(await screen.findByRole('option', { name: item }))
}

async function pickDestination(user: User, dialog: HTMLElement, bin: RegExp) {
  await user.click(within(dialog).getByRole('combobox', { name: /^Posición de destino/ }))
  await user.click(await screen.findByRole('option', { name: bin }))
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

describe('InventoryTransferModal', () => {
  it('orden de los campos: origen → ítem → destino → cantidad → notas', async () => {
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Transferencia de inventario' })
    const labels = Array.from(dialog.querySelectorAll('.f > label')).map((l) => (l.textContent ?? '').replace('*', '').trim())
    expect(labels).toEqual(['Almacén de origen', 'Posición de origen', 'Ítem', 'Almacén de destino', 'Posición de destino', 'Cantidad', 'Notas'])
    // sin posición de origen el ítem no se puede elegir
    expect(within(dialog).getByRole('combobox', { name: /^Ítem/ })).toBeDisabled()
  })

  it('producto LOT: el ítem trae el lote (lotId); el destino arranca en el almacén de origen', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Transferencia de inventario' })
    await pickOriginAndItem(user, dialog, /GLU-STR · Tiras · Lote L-2408/)
    expect(within(dialog).getByRole('combobox', { name: /^Almacén de destino/ })).toHaveValue('ALM-01 · Almacén principal')
    await pickDestination(user, dialog, /A-02/)
    expect(await within(dialog).findByText('Disponible en la posición: 5')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '3')
    await user.click(within(dialog).getByRole('button', { name: 'Transferir' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toEqual({ productPublicId: LOTP, fromWarehousePublicId: WH, fromBinId: 10, toWarehousePublicId: WH, toBinId: 11, quantity: 3, lotId: 7, notes: null })
  })

  it('producto SERIAL: se eligen las series de la posición y la cantidad es su número', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Transferencia de inventario' })
    await pickOriginAndItem(user, dialog, /WCH-STD · Silla/)
    await user.click(await within(dialog).findByRole('button', { name: /^Series/ }))
    // solo las series de A-01
    expect(screen.queryByRole('option', { name: /SN-9/ })).toBeNull()
    await user.click(await screen.findByRole('option', { name: /SN-2/ }))
    await user.keyboard('{Escape}')
    await pickDestination(user, dialog, /A-02/)
    expect(within(dialog).getByText('1 series')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Transferir' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ productPublicId: SERP, quantity: 1, serialNumbers: ['SN-2'] })
  })

  it('no deja transferir a la misma posición ni más de lo disponible', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Transferencia de inventario' })
    await pickOriginAndItem(user, dialog, /GLU-STR/)
    await pickDestination(user, dialog, /A-01/)
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '9')
    await user.click(within(dialog).getByRole('button', { name: 'Transferir' }))
    expect(await within(dialog).findByText('No puede transferir más de lo disponible en la posición (5).')).toBeInTheDocument()
    expect(within(dialog).getByText('El origen y el destino no pueden ser la misma posición.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })
})
