// Lote 235 — el almacén de la orden y lo disponible en el pedido guiado, sobre un API simulado: con varios almacenes el campo sale ya escogido con el principal
// (y viaja en el borrador); cada producto dice «Disponible: N»; pedir de más NO bloquea: queda marcado «Backorder: N» en la lista, el carrito y el resumen de la
// entrega, y el envío manda allowBackorder sin volver a preguntar; la nota de otros almacenes solo sale si otro tiene.
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import type { SalesOrderDto } from '../salesApi'
import type { StockInput } from '../stockRules'
import { ClientStep } from './ClientStep'
import { DeliveryStep } from './DeliveryStep'
import NewOrderScreen from './NewOrderScreen'
import { CartBar, ProductsStep } from './ProductsStep'
import { recordingFormData, type FakeApi, type FakeRequest } from './wizardTestApi'
import { StepHarness, WizardProviders } from './wizardTestKit'
import type { WizardState } from './wizardRules'

const mock = vi.hoisted(() => ({ requests: [], route: null, forms: [] }) as FakeApi)

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fakeFetch } = await import('./wizardTestApi')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fakeFetch(mock) }) }
})

const WAREHOUSES = [
  { publicId: 'w-1', code: 'ALM-01', name: 'Bayamón', isMain: false },
  { publicId: 'w-2', code: 'ALM-02', name: 'Ponce', isMain: true },
  { publicId: 'w-3', code: 'ALM-03', name: 'Mayagüez', isMain: false },
]
const STOCK: StockInput = {
  warehouses: WAREHOUSES,
  products: [
    { productPublicId: 'p-1', stock: [{ warehousePublicId: 'w-1', available: 12 }, { warehousePublicId: 'w-2', available: 5 }, { warehousePublicId: 'w-3', available: 3 }] },
    { productPublicId: 'p-3', stock: [{ warehousePublicId: 'w-3', available: 8 }] },
    { productPublicId: 'k-1', stock: [{ warehousePublicId: 'w-2', available: 2 }] },
  ],
}
const ONE_WAREHOUSE: StockInput = { warehouses: [WAREHOUSES[1]], products: [{ productPublicId: 'p-1', stock: [{ warehousePublicId: 'w-2', available: 5 }] }] }

const item = (id: string, sku: string, name: string, price: number | null, extra: Record<string, unknown> = {}) => ({ productPublicId: id, sku, name, price, priceSource: 'GENERAL_LIST', isKit: false, kitAdjustable: false, isPromotion: false, imagePublicId: null, ...extra })
const CATALOG = {
  clientPublicId: 'c-1',
  date: '2026-11-10',
  brands: [
    {
      brand: 'Ammex',
      offers: [item('k-1', 'OFERTA1', 'Oferta guantes y cajas', 41.94, { isKit: true, isOfferKit: true, isPromotion: true })],
      products: [item('p-1', '52800', 'Guantes de nitrilo', 6.25), item('p-2', '52810', 'Guantes de látex', 5), item('p-3', '070120', 'Caja de cartón', 3.5)],
    },
  ],
}

function Products({ stock = STOCK, warehouseId = 'w-2', onState }: { stock?: StockInput; warehouseId?: string; onState?: (s: WizardState) => void }) {
  return (
    <StepHarness initial={{ clientPublicId: 'c-1' }} onState={onState}>
      {(state, change) => (
        <>
          <ProductsStep state={state} onChange={change} stock={stock} warehouseId={warehouseId} />
          <CartBar state={state} onChange={change} saved={null} stock={stock} warehouseId={warehouseId} />
        </>
      )}
    </StepHarness>
  )
}

describe('Lote 235 — almacén de la orden y disponible en el pedido guiado', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    vi.stubGlobal('FormData', recordingFormData(mock))
    mock.requests.length = 0
    mock.forms = []
    mock.route = (r) => (r.method === 'GET' && r.path === '/api/v1/sales-orders/catalog' ? CATALOG : r.method === 'GET' && r.path === '/api/v1/clients' ? [] : undefined)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('paso Cliente: con varios almacenes el campo viene ya escogido con el principal y se puede cambiar', async () => {
    const user = userEvent.setup()
    let last: WizardState | null = null
    render(
      <WizardProviders>
        <StepHarness initial={{ clientPublicId: 'c-1', clientLabel: 'C0001 · Farmacia Central' }} onState={(s) => (last = s)}>
          {(state, change) => (
            <ClientStep state={state} onChange={change} onPickClient={() => {}} client={undefined} clientLoading={false} paymentChanged={false} needsWarehouse warehouses={WAREHOUSES} warehouseId={state.warehousePublicId ?? 'w-2'} onClientCreated={() => {}} />
          )}
        </StepHarness>
      </WizardProviders>,
    )
    const field = screen.getByRole('combobox', { name: 'Almacén' })
    expect(field).toHaveValue('w-2')
    expect(within(field).getAllByRole('option').map((o) => o.textContent)).toEqual(['ALM-01 · Bayamón', 'ALM-02 · Ponce (principal)', 'ALM-03 · Mayagüez'])
    await user.selectOptions(field, 'w-3')
    expect(last).toMatchObject({ warehousePublicId: 'w-3' })
    expect(field).toHaveValue('w-3')
  })

  it('paso Productos: cada renglón y cada tarjeta de oferta dicen lo disponible en el almacén de la orden', async () => {
    render(<WizardProviders><Products /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    const rows = within(list).getAllByRole('listitem')
    expect(within(rows[0]).getByText('Disponible: 5')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Disponible: 0')).toBeInTheDocument()
    expect(within(rows[2]).getByText('Disponible: 0')).toBeInTheDocument()
    // el kit de oferta: kits completos que se pueden armar ahí
    expect(within(screen.getByRole('list', { name: 'Ofertas de Ammex' })).getByText('Disponible: 2')).toBeInTheDocument()
  })

  it('la nota de otros almacenes: uno → «Hay disponible en X: N»; varios → la lista; ninguno → nada', async () => {
    render(<WizardProviders><Products /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    const rows = within(list).getAllByRole('listitem')
    expect(within(rows[0]).getByText('Hay disponible en: Bayamón 12 · Mayagüez 3')).toBeInTheDocument()
    expect(within(rows[1]).queryByText(/Hay disponible/)).toBeNull()
    expect(within(rows[2]).getByText('Hay disponible en Mayagüez: 8')).toBeInTheDocument()
  })

  it('con un solo almacén no sale ninguna nota de otros almacenes', async () => {
    render(<WizardProviders><Products stock={ONE_WAREHOUSE} /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    expect(within(list).getAllByText('Disponible: 5')).toHaveLength(1)
    expect(screen.queryByText(/Hay disponible/)).toBeNull()
  })

  it('pedir más de lo disponible NO bloquea: el renglón y el carrito lo marcan «Backorder: N»', async () => {
    const user = userEvent.setup()
    let last: WizardState | null = null
    render(<WizardProviders><Products onState={(s) => (last = s)} /></WizardProviders>)
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    const row = within(list).getAllByRole('listitem')[0]
    await user.type(within(row).getByRole('textbox', { name: 'Cantidad de 52800' }), '5')
    expect(within(row).queryByText(/Backorder/)).toBeNull()   // justo lo disponible: sin aviso
    await user.clear(within(row).getByRole('textbox', { name: 'Cantidad de 52800' }))
    await user.type(within(row).getByRole('textbox', { name: 'Cantidad de 52800' }), '8')
    expect(within(row).getByText('Backorder: 3')).toBeInTheDocument()
    expect(within(row).getByRole('status')).toHaveTextContent('Solo hay 5 disponibles en Ponce; 3 quedan en backorder.')
    // la línea queda en el carrito con su cantidad completa
    expect(last!.cart).toMatchObject([{ productPublicId: 'p-1', quantity: '8' }])
    await user.click(screen.getByRole('button', { name: /^Carrito: 1 producto/ }))
    const cart = await screen.findByRole('region', { name: 'Carrito' })
    expect(within(cart).getByText('Backorder: 3')).toBeInTheDocument()
    // sin nada disponible
    const empty = within(list).getAllByRole('listitem')[1]
    await user.type(within(empty).getByRole('textbox', { name: 'Cantidad de 52810' }), '1')
    expect(within(empty).getByRole('status')).toHaveTextContent('No hay disponible en Ponce; 1 queda en backorder.')
  })

  it('paso Entrega: el resumen lista las líneas que quedan en backorder', () => {
    const saved = { publicId: 'o-1', number: 'OV-00042', subtotal: 50, taxAmount: 5.75, total: 55.75, terms: {} } as unknown as SalesOrderDto
    const cart = [
      { productPublicId: 'p-1', sku: '52800', name: 'Guantes de nitrilo', price: 6.25, priceSource: 'GENERAL_LIST', isKit: false, kitAdjustable: false, isPromotion: false, imagePublicId: null, quantity: '8', components: null },
      { productPublicId: 'k-1', sku: 'OFERTA1', name: 'Oferta guantes y cajas', price: 41.94, priceSource: 'OFFER', isKit: true, kitAdjustable: false, isPromotion: true, imagePublicId: null, quantity: '2', components: null },
    ]
    render(
      <WizardProviders permissions={['sales.view', 'sales.create']}>
        <StepHarness initial={{ clientPublicId: 'c-1', orderPublicId: 'o-1', deliveryMode: 'PICKUP', cart }}>
          {(state, change) => <DeliveryStep state={state} onChange={change} onRecalc={() => {}} client={undefined} saved={saved} recalculating={false} stock={STOCK} warehouseId="w-2" />}
        </StepHarness>
      </WizardProviders>,
    )
    const short = screen.getByRole('list', { name: 'Productos que quedan en backorder' })
    expect(within(short).getAllByRole('listitem')).toHaveLength(1)   // el kit alcanza (2 de 2)
    expect(within(short).getByText('Backorder: 3')).toBeInTheDocument()
    expect(within(short).getByRole('status')).toHaveTextContent('Solo hay 5 disponibles en Ponce; 3 quedan en backorder.')
    expect(within(short).queryByText(/Disponible:/)).toBeNull()
  })

  it('pantalla completa: el borrador viaja con el almacén principal, pedir de más deja avanzar y el envío manda allowBackorder sin preguntar otra vez', async () => {
    const CLIENT = { id: 1, publicId: 'c-1', code: 'C0001', name: 'Farmacia Central', status: 'ACTIVE', isActive: true, paymentTerm: 'CASH', requireOrderSignature: false, physicalAddress: { publicId: 'l-phys', line1: 'Calle Sol 12', city: 'Bayamón', postalCode: '00961', isActive: true } }
    let order: Record<string, unknown> | null = null
    const apply = (body: Record<string, unknown>) => {
      order = { publicId: 'o-1', number: 'OV-00042', status: 'DRAFT', clientPublicId: 'c-1', clientName: 'Farmacia Central', lines: [], subtotal: 0, taxAmount: 0, total: 0, hasBackorder: false, terms: {}, ...(order ?? {}) }
      if (body.warehousePublicId) order.warehousePublicId = body.warehousePublicId
      if (Array.isArray(body.lines)) order.lines = (body.lines as { productPublicId: string; quantity: number }[]).map((l, i) => ({ id: i + 1, lineNo: i + 1, productPublicId: l.productPublicId, sku: '52800', productName: 'Guantes de nitrilo', qtyOrdered: l.quantity, unitPrice: 6.25 }))
      return order
    }
    mock.route = (r: FakeRequest) => {
      const { method: m, path: p } = r
      if (m === 'GET' && p === '/api/v1/sales-orders/stock') return STOCK
      if (m === 'GET' && p === '/api/v1/clients') return [CLIENT, { ...CLIENT, publicId: 'c-2', code: 'C0002', name: 'Otra farmacia' }]
      if (m === 'GET' && p === '/api/v1/clients/c-1') return CLIENT
      if (m === 'GET' && p === '/api/v1/sales-orders/catalog') return CATALOG
      if (m === 'POST' && p === '/api/v1/sales-orders') return apply(r.body as Record<string, unknown>)
      if (m === 'PATCH' && p === '/api/v1/sales-orders/o-1') return apply(r.body as Record<string, unknown>)
      if (m === 'POST' && p === '/api/v1/sales-orders/o-1/submit') return { ...order, status: 'REQUESTED', hasBackorder: true }
      return undefined
    }
    const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)
    const user = userEvent.setup()
    render(<WizardProviders><NewOrderScreen /></WizardProviders>)

    await user.type(screen.getByRole('searchbox', { name: 'Buscar cliente' }), 'farm')
    await user.click(await screen.findByRole('button', { name: /Farmacia Central/ }))
    // con varios almacenes el campo sale ya escogido con el principal (no es un campo vacío obligatorio)
    const field = await screen.findByRole('combobox', { name: 'Almacén' })
    expect(field).toHaveValue('w-2')
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Forma de pago' })).not.toHaveValue(''))
    await user.click(screen.getByRole('button', { name: 'Siguiente' }))
    await screen.findByRole('heading', { level: 2, name: 'Productos' })
    expect((sent('POST', '/api/v1/sales-orders')[0].body as { warehousePublicId?: string }).warehousePublicId).toBe('w-2')

    // 8 con 5 disponibles: marcado, pero el paso avanza y el borrador guarda la cantidad completa
    const list = await screen.findByRole('list', { name: 'Productos de Ammex' })
    await user.type(within(list).getByRole('textbox', { name: 'Cantidad de 52800' }), '8')
    expect(within(list).getByText('Backorder: 3')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Siguiente' }))
    await screen.findByRole('heading', { level: 2, name: 'Entrega' })
    expect((sent('PATCH', '/api/v1/sales-orders/o-1')[0].body as { lines: { quantity: number }[] }).lines[0].quantity).toBe(8)
    expect(screen.getByRole('list', { name: 'Productos que quedan en backorder' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Siguiente' }))
    await screen.findByRole('heading', { level: 2, name: 'Autorización y envío' })

    await user.click(screen.getByRole('button', { name: 'Enviar pedido' }))
    const dialog = await screen.findByRole('dialog', { name: 'Confirme con su contraseña' })
    await user.type(within(dialog).getByLabelText('Contraseña'), 'Secreta1!{Enter}')
    await waitFor(() => expect(sent('POST', '/api/v1/sales-orders/o-1/submit')).toHaveLength(1))
    expect(sent('POST', '/api/v1/sales-orders/o-1/submit')[0].body).toMatchObject({ authMethod: 'PASSWORD', allowBackorder: true })
    // no hizo falta el diálogo de faltante
    expect(sent('GET', '/api/v1/sales-orders/o-1/availability')).toHaveLength(0)
    expect(await screen.findByText('OV-00042')).toBeInTheDocument()
  })

  it('con un solo almacén el paso Cliente no muestra el campo y el borrador no manda almacén (lo pone el servidor)', async () => {
    mock.route = (r: FakeRequest) => {
      if (r.method === 'GET' && r.path === '/api/v1/sales-orders/stock') return ONE_WAREHOUSE
      if (r.method === 'GET' && r.path === '/api/v1/clients') return []
      return undefined
    }
    render(<WizardProviders><NewOrderScreen /></WizardProviders>)
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/sales-orders/stock')).toBe(true))
    expect(screen.queryByRole('combobox', { name: 'Almacén' })).toBeNull()
  })
})
