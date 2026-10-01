// Pruebas de Inventario (Lote F6) sobre un fetch simulado: en Saldos y Kárdex la paginación es del servidor, así que todo
// cambio de filtro vuelve a la página 1 (skip=0); el filtro Producto viaja como productPublicIds; el Kárdex
// muestra la columna Motivo. Sus endpoints no aceptan orden: el orden por encabezado es en el cliente (solo la página visible).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import InventoryScreen from './InventoryScreen'

type Handler = (url: URL, method?: string) => unknown
const mock = vi.hoisted(() => ({ requests: [] as URL[], posts: [] as { url: URL; body: unknown }[], handler: null as unknown, discStatus: 'OPEN' }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    if (req.method !== 'GET') {
      const text = await req.text()
      mock.posts.push({ url, body: text ? JSON.parse(text) : null })
    }
    const body = (mock.handler as Handler)(url, req.method)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const PRODUCT = { id: 1, publicId: 'aaaaaaaa-0000-0000-0000-000000000001', sku: 'TORN-01', name: 'Tornillo', isOwn: true, isActive: true, trackingTypeCode: 'NONE' }
const TOTAL = 60

function balance(i: number) {
  return { id: i, warehousePublicId: WH, warehouseCode: 'ALM-01', binCode: `A-${i}`, productPublicId: PRODUCT.publicId, sku: 'TORN-01', productName: 'Tornillo', qtyOnHand: i, qtyReserved: 0, qtyAvailable: i }
}

function movement(i: number) {
  return {
    id: i,
    createdAtUtc: '2026-09-01T10:00:00',
    typeCode: 'ADJUST',
    type: 'Ajuste',
    productPublicId: PRODUCT.publicId,
    sku: 'TORN-01',
    productName: 'Tornillo',
    toWarehouseCode: 'ALM-01',
    toBinCode: 'A-01',
    quantity: 10,
    signedQuantity: 10,
    reasonCode: 'COUNT',
    reason: `Conteo físico ${i}`,
  }
}

function page<T>(url: URL, make: (i: number) => T) {
  const skip = Number(url.searchParams.get('skip') ?? 0)
  const take = Number(url.searchParams.get('take') ?? 25)
  const n = Math.max(0, Math.min(take, TOTAL - skip))
  return { total: TOTAL, skip, take, items: Array.from({ length: n }, (_, k) => make(skip + k + 1)) }
}

const DISC = 'dddddddd-0000-0000-0000-000000000001'
function discrepancy(status = mock.discStatus) {
  return {
    publicId: DISC,
    kindCode: 'BALANCE',
    kind: 'Saldo por posición',
    productPublicId: PRODUCT.publicId,
    sku: 'TORN-01',
    productName: 'Tornillo',
    warehousePublicId: WH,
    warehouseCode: 'ALM-01',
    binId: 10,
    binCode: 'A-01',
    ledgerQty: 4,
    balanceQty: 5,
    difference: 1,
    statusCode: status,
    status: status === 'OPEN' ? 'Pendiente' : 'Resuelto',
    triggerCode: 'EVENT',
    trigger: 'Automática (movimiento)',
    detectedAtUtc: '2026-09-30T10:00:00',
    lastCheckedAtUtc: '2026-09-30T10:00:05',
    checkCount: 2,
    rowVersion: 'AAAA',
    correctedFromQty: status === 'RESOLVED' ? 5 : null,
    correctedToQty: status === 'RESOLVED' ? 4 : null,
  }
}

function route(url: URL, method = 'GET'): unknown {
  const p = url.pathname
  if (p === '/api/v1/inventory/transactions/summary') return { movements: 7, inCount: 5, inQty: 12, outCount: 2, outQty: 3, internalCount: 1 }
  if (p === '/api/v1/inventory/transactions/1')
    return {
      transaction: { ...movement(1), refEntityCode: 'RECEIPT', refId: 318, refLabel: 'Recibo REC-000318', userName: 'María R.' },
      ownerName: 'Propio',
      categoryName: 'Ferretería',
      document: { entityCode: 'RECEIPT', entityLabel: 'Recibo', id: 318, publicId: 'eeeeeeee-0000-0000-0000-000000000001', number: 'REC-000318', status: 'Completado', partyName: 'Proveedor X' },
      related: [movement(1), { ...movement(2), id: 2 }],
      relatedTruncated: false,
    }
  if (p === '/api/v1/inventory/owners') return [{ name: 'Propio', isOwn: true }, { clientPublicId: 'cccccccc-0000-0000-0000-000000000001', name: 'Cliente A', isOwn: false }]
  if (p === '/api/v1/inventory/discrepancies') return { total: 1, skip: 0, take: 25, openCount: mock.discStatus === 'OPEN' ? 1 : 0, items: [discrepancy()] }
  if (p === `/api/v1/inventory/discrepancies/${DISC}`) return { discrepancy: discrepancy(), currentReserved: 0, recentMovements: [movement(1)], history: [] }
  if (p === `/api/v1/inventory/discrepancies/${DISC}/resolve` && method === 'POST') {
    mock.discStatus = 'RESOLVED'
    return { discrepancy: discrepancy('RESOLVED'), currentReserved: 0, recentMovements: [], history: [] }
  }
  if (p === '/api/v1/inventory/reconciliation/status') return { enabled: true, consuming: true, pending: 0, processed: 3, dropped: 0 }
  if (p === '/api/v1/status/InventoryDiscrepancyStatus')
    return [
      { code: 'OPEN', label: 'Pendiente', color: '#EF4444', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 },
      { code: 'RESOLVED', label: 'Resuelto', color: '#059669', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 2 },
    ]
  if (p === '/api/v1/warehouses/bins/search') return [{ id: 10, code: 'A-01', zoneCode: 'PCK', warehousePublicId: WH, warehouseCode: 'ALM-01', isActive: true }]
  if (p === '/api/v1/inventory/balances') return page(url, balance)
  if (p === '/api/v1/inventory/transactions') return page(url, movement)
  if (p === '/api/v1/products') return { total: 1, skip: 0, take: 50, items: [PRODUCT] }
  if (p === `/api/v1/products/${PRODUCT.publicId}`) return { product: PRODUCT }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  return []
}

/** Pestaña Saldos (desde la Fase 8 el Kárdex es la primera pestaña y va sin parámetro). */
const BALANCES_URL = '/warehouse/kardex?tab=balances'

/** Muestra la URL actual (ruta + consulta) para comprobar lo que escribe la pantalla. */
function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname + location.search}</output>
}

function wrap(ui: ReactNode, url = '/warehouse/kardex', permissions = ['inventory.view']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[url]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          {ui}
          <LocationProbe />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Última consulta a la ruta indicada. */
function last(path: string): URL {
  const hits = mock.requests.filter((u) => u.pathname === path)
  return hits[hits.length - 1]
}

/** Espera a que la última consulta a `path` cumpla la condición. */
async function waitLast(path: string, check: (u: URL) => boolean) {
  await waitFor(() => expect(check(last(path))).toBe(true), { timeout: 4000 })
}

async function goToPage2(user: ReturnType<typeof userEvent.setup>, path: string) {
  await user.click(await screen.findByRole('button', { name: 'Página siguiente' }, { timeout: 4000 }))
  await waitLast(path, (u) => u.searchParams.get('skip') === '25')
}

async function pickProduct(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole('combobox', { name: 'Producto' }), 'torn')
  await user.click(await screen.findByRole('option', { name: /TORN-01 · Tornillo/ }, { timeout: 4000 }))
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.posts = []
  mock.discStatus = 'OPEN'
  mock.handler = route
})

describe('InventoryScreen · Saldos', () => {
  const PATH = '/api/v1/inventory/balances'

  it('elegir un producto en el filtro vuelve a la página 1 y lo manda como productPublicIds', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, BALANCES_URL)
    await waitLast(PATH, (u) => u.searchParams.get('skip') === '0')
    await goToPage2(user, PATH)
    await pickProduct(user)
    await waitLast(PATH, (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    expect(last(PATH).searchParams.get('skip')).toBe('0')
    // Lote 14: el filtro Producto es compartido por las tres pestañas y admite dados de baja (historial del Kárdex)
    expect(last('/api/v1/products').searchParams.get('activeOnly')).not.toBe('true')
  })

  it('las columnas se ordenan por encabezado (en el cliente, sobre la página visible) sin volver a pedir al API', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, BALANCES_URL)
    const header = await screen.findByRole('columnheader', { name: 'Disponible' })
    expect(header).toHaveAttribute('aria-sort', 'none')
    const calls = mock.requests.filter((u) => u.pathname === PATH).length
    await user.click(within(header).getByRole('button'))
    await user.click(within(screen.getByRole('columnheader', { name: /Disponible/ })).getByRole('button'))
    expect(screen.getByRole('columnheader', { name: /Disponible/ })).toHaveAttribute('aria-sort', 'descending')
    // la página 1 trae A-1…A-25: en descendente la primera fila es la de mayor disponible
    const firstRow = screen.getAllByRole('row')[1]
    expect(within(firstRow).getByText('A-25')).toBeInTheDocument()
    expect(mock.requests.filter((u) => u.pathname === PATH).length).toBe(calls)
  })
})

describe('InventoryScreen · Kárdex', () => {
  const PATH = '/api/v1/inventory/transactions'

  it('muestra la columna Motivo con el motivo del movimiento y la cantidad con signo', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    await user.click(await screen.findByRole('tab', { name: 'Kárdex' }))
    expect(await screen.findByRole('columnheader', { name: 'Motivo' })).toBeInTheDocument()
    expect(await screen.findByText('Conteo físico 1')).toBeInTheDocument()
    expect(screen.getAllByText('+10').length).toBeGreaterThan(0)
    // ordenable por encabezado (en el cliente, sobre la página visible)
    const dateHeader = screen.getByRole('columnheader', { name: 'Fecha' })
    expect(within(dateHeader).getByRole('button')).toBeInTheDocument()
  })

  it('el filtro Producto (con dados de baja) vuelve a la página 1 y viaja como productPublicIds', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    await user.click(await screen.findByRole('tab', { name: 'Kárdex' }))
    await waitLast(PATH, (u) => u.searchParams.get('skip') === '0')
    await goToPage2(user, PATH)
    await pickProduct(user)
    await waitLast(PATH, (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    expect(last(PATH).searchParams.get('skip')).toBe('0')
    expect(last('/api/v1/products').searchParams.get('activeOnly')).not.toBe('true')
  })

  it('2026-10-01: las tablas ya no traen buscador propio (para eso están los filtros de arriba)', async () => {
    wrap(<InventoryScreen />)
    await screen.findByRole('tab', { name: 'Kárdex' })
    await waitLast(PATH, (u) => u.searchParams.get('skip') === '0')
    expect(screen.queryByRole('searchbox')).toBeNull()
    expect(last(PATH).searchParams.has('search')).toBe(false)
  })
})

describe('InventoryScreen · Kárdex de movimientos (Fase 8: ítem propio del menú)', () => {
  it('sin parámetros abre el Kárdex (primera pestaña) con el título de la maqueta; Saldos no se consulta', async () => {
    wrap(<InventoryScreen />)
    expect(await screen.findByRole('heading', { level: 1, name: 'Kárdex de movimientos' })).toBeInTheDocument()
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Kárdex', 'Saldos', 'Conciliación'])
    expect(screen.getByRole('tab', { name: 'Kárdex' })).toHaveAttribute('aria-selected', 'true')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('skip') === '0')
    expect(mock.requests.some((u) => u.pathname === '/api/v1/inventory/balances')).toBe(false)
  })

  it('la pestaña va en la URL: Saldos → ?tab=balances, Conciliación → ?tab=reconciliation, Kárdex sin parámetro', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, `/warehouse/kardex?product=${PRODUCT.publicId}`)
    await user.click(await screen.findByRole('tab', { name: 'Saldos' }))
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/warehouse\/kardex\?tab=balances$/)
    await user.click(screen.getByRole('tab', { name: 'Conciliación' }))
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/warehouse\/kardex\?tab=reconciliation$/)
    await user.click(screen.getByRole('tab', { name: 'Kárdex' }))
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/warehouse\/kardex$/)
  })

  it('?types=ADJUSTMENT (Reporte de ajustes de Productos e inventario) filtra el Kárdex por tipo', async () => {
    wrap(<InventoryScreen />, `/warehouse/kardex?types=ADJUSTMENT&warehousePublicIds=${WH}`)
    await waitLast(
      '/api/v1/inventory/transactions',
      (u) => u.searchParams.getAll('types').includes('ADJUSTMENT') && u.searchParams.getAll('warehousePublicIds').includes(WH),
    )
  })
})

describe('InventoryScreen · parámetros de URL (enlaces de Pulso, Lote F7A)', () => {
  it('?tab=kardex&product=X (valor anterior de la pestaña) abre el Kárdex filtrado por el producto y muestra su SKU en la píldora', async () => {
    wrap(<InventoryScreen />, `/warehouse/kardex?tab=kardex&product=${PRODUCT.publicId}`)
    expect(await screen.findByRole('tab', { name: 'Kárdex' })).toHaveAttribute('aria-selected', 'true')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    expect(last('/api/v1/inventory/transactions').searchParams.get('skip')).toBe('0')
    // Saldos no se consulta: la pestaña inicial es el Kárdex
    expect(mock.requests.some((u) => u.pathname === '/api/v1/inventory/balances')).toBe(false)
    // el SKU sale de la ficha del producto
    expect(await screen.findByRole('button', { name: 'Quitar TORN-01' }, { timeout: 4000 })).toBeInTheDocument()
  })

  it('?tab=balances&categoryIds=7 abre Saldos filtrado por la categoría', async () => {
    wrap(<InventoryScreen />, '/warehouse/kardex?tab=balances&categoryIds=7')
    expect(await screen.findByRole('tab', { name: 'Saldos' })).toHaveAttribute('aria-selected', 'true')
    await waitLast('/api/v1/inventory/balances', (u) => u.searchParams.getAll('categoryIds').includes('7'))
  })

  it('?tab=balances&warehousePublicIds=W&categoryIds=7 abre Saldos con el almacén y la categoría (los mismos filtros que la cifra de Pulso)', async () => {
    wrap(<InventoryScreen />, `/warehouse/kardex?tab=balances&categoryIds=7&warehousePublicIds=${WH}`)
    await waitLast(
      '/api/v1/inventory/balances',
      (u) => u.searchParams.getAll('categoryIds').includes('7') && u.searchParams.getAll('warehousePublicIds').includes(WH),
    )
  })

  it('?product=X&warehousePublicIds=W manda el almacén al Kárdex', async () => {
    wrap(<InventoryScreen />, `/warehouse/kardex?product=${PRODUCT.publicId}&warehousePublicIds=${WH}`)
    await waitLast(
      '/api/v1/inventory/transactions',
      (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId) && u.searchParams.getAll('warehousePublicIds').includes(WH),
    )
  })

  it('varios productos en la URL: cada píldora toma su SKU; la ficha que no se puede leer muestra un texto fijo', async () => {
    const other = { ...PRODUCT, id: 2, publicId: 'aaaaaaaa-0000-0000-0000-000000000002', sku: 'TUER-02', name: 'Tuerca' }
    const missing = 'aaaaaaaa-0000-0000-0000-000000000009'
    mock.handler = (url: URL) => {
      if (url.pathname === `/api/v1/products/${other.publicId}`) return { product: other }
      if (url.pathname === `/api/v1/products/${missing}`)
        return new Response(JSON.stringify({ title: 'No encontrado', status: 404 }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } })
      return route(url)
    }
    wrap(<InventoryScreen />, `/warehouse/kardex?product=${PRODUCT.publicId},${other.publicId}&product=${missing}`)
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('productPublicIds').length === 3)
    expect(await screen.findByRole('button', { name: 'Quitar TORN-01' }, { timeout: 4000 })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Quitar TUER-02' }, { timeout: 4000 })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Quitar Producto no disponible' }, { timeout: 4000 })).toBeInTheDocument()
    expect(screen.queryByText('…')).toBeNull()
  })

  it('Lote 14 (hallazgo 6): los filtros son de la pantalla y los comparten las tres pestañas; sobreviven al cambio de pestaña', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, `/warehouse/kardex?product=${PRODUCT.publicId}&warehousePublicIds=${WH}`)
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    await user.click(screen.getByRole('tab', { name: 'Saldos' }))
    await waitLast(
      '/api/v1/inventory/balances',
      (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId) && u.searchParams.getAll('warehousePublicIds').includes(WH),
    )
    // la URL queda solo con la pestaña; los filtros siguen en pantalla
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/warehouse\/kardex\?tab=balances$/)
    expect(await screen.findByRole('button', { name: 'Quitar TORN-01' }, { timeout: 4000 })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Conciliación' }))
    await waitLast('/api/v1/inventory/discrepancies', (u) => u.searchParams.getAll('productPublicIds').includes(PRODUCT.publicId))
    expect(last('/api/v1/inventory/discrepancies').searchParams.getAll('status')).toEqual(['OPEN'])
    const before = mock.requests.length
    await user.click(screen.getByRole('tab', { name: 'Kárdex' }))
    await waitFor(() => expect(mock.requests.slice(before).some((u) => u.pathname === '/api/v1/inventory/transactions')).toBe(true), { timeout: 4000 })
    expect(last('/api/v1/inventory/transactions').searchParams.getAll('productPublicIds')).toEqual([PRODUCT.publicId])
  })

  it('un filtro que la pestaña no aplica se atenúa y se nombra en la ayuda (Saldos no filtra por tipo)', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, '/warehouse/kardex?types=ADJUSTMENT')
    await user.click(await screen.findByRole('tab', { name: 'Saldos' }))
    expect(await screen.findByText(/No aplican a Saldos.*Tipo/)).toBeInTheDocument()
    await waitLast('/api/v1/inventory/balances', (u) => !u.searchParams.has('types'))
    // el resumen de movimientos sí lleva el tipo (D13)
    await waitLast('/api/v1/inventory/transactions/summary', (u) => u.searchParams.getAll('types').includes('ADJUSTMENT'))
  })

  it('?ref= ya no se usa: el Kárdex no manda búsqueda (el API no compara el documento de origen)', async () => {
    wrap(<InventoryScreen />, '/warehouse/kardex?ref=REC-000318')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('skip') === '0')
    expect(last('/api/v1/inventory/transactions').searchParams.has('search')).toBe(false)
    expect(screen.queryByRole('searchbox')).toBeNull()
  })
})

describe('InventoryScreen · Lote 14 (resumen, filtros nuevos, detalle y descuadres)', () => {
  it('resumen del Kárdex con los mismos filtros; en Saldos además En mano y Disponible (D13)', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    const summary = await screen.findByRole('group', { name: 'Resumen de movimientos' })
    expect(await within(summary).findByText('+12')).toBeInTheDocument()
    expect(within(summary).getByText('−3')).toBeInTheDocument()
    expect(within(summary).queryByText('En mano')).toBeNull()
    await user.click(screen.getByRole('tab', { name: 'Saldos' }))
    const balances = await screen.findByRole('group', { name: 'Resumen de movimientos' })
    expect(await within(balances).findByText('En mano')).toBeInTheDocument()
    expect(within(balances).getByText('Disponible')).toBeInTheDocument()
  })

  it('columnas Dueño y Categoría; clic en una fila abre el detalle (?txn=) con el documento y los relacionados', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    expect(await screen.findByRole('columnheader', { name: 'Dueño' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Categoría' })).toBeInTheDocument()
    await screen.findByText('Conteo físico 1', {}, { timeout: 4000 })
    const firstRow = screen.getAllByRole('row')[1]
    await user.click(within(firstRow).getAllByRole('cell')[3])
    expect(screen.getByTestId('location')).toHaveTextContent('txn=1')
    const dialog = await screen.findByRole('dialog', { name: 'Movimiento #1' })
    expect(await within(dialog).findByText('REC-000318')).toBeInTheDocument()
    expect(within(dialog).getByText('María R.')).toBeInTheDocument()
    expect(within(dialog).getByText('Ferretería')).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: 'Abrir' })).toHaveAttribute('href', '/warehouse/receipts?receipt=eeeeeeee-0000-0000-0000-000000000001')
    expect(within(dialog).getByText('Movimientos relacionados (2)')).toBeInTheDocument()
  })

  it('filtros nuevos al API: Posición (binIds), Dueño Propio (includeOwn), Dirección y Solo manuales', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />)
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('skip') === '0')
    await user.type(screen.getByRole('combobox', { name: 'Posición' }), 'A-0')
    await user.click(await screen.findByRole('option', { name: /A-01 · PCK · ALM-01/ }, { timeout: 4000 }))
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('binIds').includes('10'))
    await user.click(within(screen.getByRole('group', { name: 'Filtros del Kárdex' })).getByRole('button', { name: /^Dueño/ }))
    await user.click(await screen.findByRole('option', { name: /Propio/ }))
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('includeOwn') === 'true')
    await user.selectOptions(screen.getByLabelText('Dirección'), 'OUT')
    await user.click(screen.getByRole('switch', { name: 'Solo manuales' }))
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('direction') === 'OUT' && u.searchParams.get('manualOnly') === 'true')
    await waitLast('/api/v1/inventory/transactions/summary', (u) => u.searchParams.get('manualOnly') === 'true' && u.searchParams.getAll('binIds').includes('10'))
  })

  it('?refEntity=CYCLE_COUNT&refId=3 filtra el Kárdex por su documento (píldora que se puede quitar)', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, '/warehouse/kardex?refEntity=CYCLE_COUNT&refId=3')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('refEntity') === 'CYCLE_COUNT' && u.searchParams.get('refId') === '3')
    await user.click(await screen.findByRole('button', { name: 'Quitar el filtro de documento' }))
    await waitLast('/api/v1/inventory/transactions', (u) => !u.searchParams.has('refEntity'))
  })

  it('Conciliación: ?discrepancy= abre el detalle; Descartar exige nota; Corregir → Resuelto con de → a y oferta de conteo', async () => {
    const user = userEvent.setup()
    wrap(<InventoryScreen />, `/warehouse/kardex?tab=reconciliation&discrepancy=${DISC}`, ['inventory.view', 'inventory.adjust', 'warehouse.count.capture'])
    expect(await screen.findByText('Revisión automática: al día')).toBeInTheDocument()
    const dialog = await screen.findByRole('dialog', { name: 'Descuadre · TORN-01' })
    await user.click(await within(dialog).findByRole('button', { name: 'Descartar' }))
    expect(await within(dialog).findByText('Escriba una nota que explique por qué se descarta el descuadre.')).toBeInTheDocument()
    expect(mock.posts).toHaveLength(0)
    await user.type(within(dialog).getByLabelText(/^Nota/), 'Arreglo en base')
    await user.click(within(dialog).getByRole('button', { name: 'Corregir el saldo según el Kárdex' }))
    await waitFor(() => expect(mock.posts).toHaveLength(1))
    expect(mock.posts[0].url.pathname).toBe(`/api/v1/inventory/discrepancies/${DISC}/resolve`)
    expect(mock.posts[0].body).toEqual({ action: 'REBUILD_BALANCE', notes: 'Arreglo en base', rowVersion: 'AAAA' })
    expect(await within(dialog).findByRole('button', { name: 'Crear conteo de esa posición' })).toBeInTheDocument()
    expect(within(dialog).getByText('5 → 4')).toBeInTheDocument()
  })

  it('Conciliación: el error del API se muestra tal cual y "Ejecutar conciliación" manda los productos del filtro', async () => {
    const user = userEvent.setup()
    mock.handler = (url: URL, method?: string) => {
      if (url.pathname.endsWith('/resolve'))
        return new Response(JSON.stringify({ title: 'El descuadre ya está cerrado; solo se consulta.', status: 422, code: 'status_rule' }), {
          status: 422,
          headers: { 'Content-Type': 'application/problem+json' },
        })
      if (url.pathname === '/api/v1/inventory/reconciliation/run')
        return { checkedAtUtc: '2026-09-30T12:00:00', productsChecked: 1, balancesChecked: 3, opened: 0, stillOpen: 1, selfCorrected: 0, mismatches: [] }
      return route(url, method)
    }
    wrap(<InventoryScreen />, `/warehouse/kardex?tab=reconciliation&product=${PRODUCT.publicId}`, ['inventory.view', 'inventory.adjust'])
    await screen.findByText('Tornillo', {}, { timeout: 4000 })
    const row = screen.getAllByRole('row').find((r) => within(r).queryByText('TORN-01'))!
    await user.click(within(row).getByRole('button', { name: 'Descartar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Descuadre · TORN-01' })
    await user.type(await within(dialog).findByLabelText(/^Nota/), 'x')
    await user.click(within(dialog).getByRole('button', { name: 'Descartar' }))
    expect(await within(dialog).findByText('El descuadre ya está cerrado; solo se consulta.')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await user.click(screen.getByRole('button', { name: 'Ejecutar conciliación' }))
    await waitFor(() => expect(mock.posts.some((p) => p.url.pathname === '/api/v1/inventory/reconciliation/run')).toBe(true))
    expect(mock.posts.find((p) => p.url.pathname === '/api/v1/inventory/reconciliation/run')!.body).toEqual({ productPublicIds: [PRODUCT.publicId] })
    expect(await screen.findByText(/1 productos y 3 saldos revisados/)).toBeInTheDocument()
  })
})
