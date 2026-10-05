// Productos e inventario (Fase 8, maqueta `inventario()`; Lote 12): columnas (con Marca y el modelo debajo), río de KPIs
// clicables que filtran la tabla (estado en `?kpi=`; naranja solo con algo que atender), filtros Almacén/SKU/Nombre/
// Categoría/Marca que van al API y vuelven a la página 1, y los dos reportes PDF con los filtros de la tabla. Sobre un
// fetch simulado; la descarga del PDF se sustituye por un espía (se revisa la especificación del reporte).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { downloadReportPdf, type ReportSpec } from '../../kernel/ui/reportPdf'
import ProductListScreen from './ProductListScreen'

const mock = vi.hoisted(() => ({ requests: [] as URL[], belowMin: 2, serialMissing: 1 }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    const body = route(url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
vi.mock('../../kernel/ui/reportPdf', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/ui/reportPdf')>()
  return { ...actual, downloadReportPdf: vi.fn(async () => {}) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const WH2 = '11111111-1111-1111-1111-111111111112'
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'

const ROWS = [
  {
    id: 1,
    publicId: P1,
    sku: 'GLU-100',
    name: 'Medidor de glucosa',
    categoryName: 'Médico',
    brand: 'Abbott',
    model: 'FreeStyle Lite',
    isOwn: true,
    trackingTypeCode: 'SERIAL',
    purchaseCost: 12.5,
    qtyOnHand: 40,
    qtyReserved: 0,
    qtyAvailable: 40,
    isBelowMin: false,
    isActive: true,
  },
  {
    id: 2,
    publicId: 'aaaaaaaa-0000-0000-0000-000000000002',
    sku: 'GLU-STR',
    name: 'Tiras reactivas',
    categoryName: 'Médico',
    isOwn: false,
    ownerName: 'Farmacia Central',
    trackingTypeCode: 'LOT',
    qtyOnHand: 180,
    qtyReserved: 30,
    qtyAvailable: 150,
    isBelowMin: true,
    isActive: true,
  },
  {
    id: 3,
    publicId: 'aaaaaaaa-0000-0000-0000-000000000003',
    sku: 'OLD-01',
    name: 'Producto viejo',
    isOwn: true,
    trackingTypeCode: 'NONE',
    qtyOnHand: 0,
    qtyReserved: 0,
    qtyAvailable: 0,
    isBelowMin: false,
    isActive: false,
  },
]

const KARDEX = [
  {
    id: 90,
    createdAtUtc: '2026-09-28T14:00:00',
    typeCode: 'ADJUSTMENT',
    sku: 'GLU-100',
    productName: 'Medidor de glucosa',
    signedQuantity: -2,
    quantity: 2,
    fromWarehouseCode: 'ALM-01',
    fromBinCode: 'A-01',
    reasonCode: 'DAMAGED',
    reason: 'Dañado',
    notes: 'Caja mojada',
    userName: 'Ana Pérez',
  },
]

function route(url: URL): unknown {
  const p = url.pathname
  const q = url.searchParams
  if (p === '/api/v1/catalogs/TrackingType')
    return [
      { code: 'NONE', label: 'Ninguno', sortOrder: 1 },
      { code: 'LOT', label: 'Lote', sortOrder: 2 },
      { code: 'SERIAL', label: 'Serie', sortOrder: 3 },
    ]
  if (p === '/api/v1/products/brands') return ['Abbott', 'Roche']
  if (p === '/api/v1/products') {
    if (q.get('take') === '1') {
      if (q.get('belowMin') === 'true') return { total: mock.belowMin, skip: 0, take: 1, items: [] }
      if (q.get('serialMissing') === 'true') return { total: mock.serialMissing, skip: 0, take: 1, items: [] }
      if (q.get('serialOnly') === 'true') return { total: 7, skip: 0, take: 1, items: [] }
      if (q.get('activeOnly') === 'true') return { total: 250, skip: 0, take: 1, items: [] }
    }
    // ProductPicker (filtro SKU) y la lectura completa del reporte (de a 200)
    if (q.has('search')) return { total: 1, skip: 0, take: 20, items: [ROWS[0]] }
    if (q.get('take') === '200') return { total: 3, skip: 0, take: 200, items: ROWS }
    return { total: 60, skip: Number(q.get('skip') ?? 0), take: 25, items: ROWS }
  }
  if (p === '/api/v1/inventory/transactions') return { total: 1, skip: 0, take: 200, items: KARDEX }
  if (p === '/api/v1/inventory/balances') return { total: 12, skip: 0, take: 1, totalOnHand: 1250, totalAvailable: 1100, items: [] }
  if (p === '/api/v1/warehouses')
    return [
      { id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true },
      { id: 2, publicId: WH2, code: 'ALM-02', name: 'Almacén norte', isActive: true },
    ]
  if (p === '/api/v1/product-categories') return [{ id: 3, name: 'Médico', path: 'Médico', isActive: true, productCount: 2 }]
  if (p === `/api/v1/products/${P1}`) return { product: ROWS[0], hasMovements: true, rowVersion: 'AAAAAAAAB9E=' }
  return []
}

function wrap(url = '/warehouse/products', permissions = ['inventory.view']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[url]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <ProductListScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Última consulta de la lista paginada de la tabla (take=25). */
function lastListQuery(): URLSearchParams {
  const hits = mock.requests.filter((u) => u.pathname === '/api/v1/products' && u.searchParams.get('take') === '25')
  return hits[hits.length - 1].searchParams
}

/** Elige opciones de un SearchSelect (botón con la etiqueta del filtro; cada opción es una casilla con su etiqueta). */
async function pickOptions(user: ReturnType<typeof userEvent.setup>, label: string, options: string[]) {
  await user.click(screen.getByLabelText(label))
  for (const o of options) await user.click(await screen.findByLabelText(o))
  await user.keyboard('{Escape}')
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.belowMin = 2
  mock.serialMissing = 1
  vi.mocked(downloadReportPdf).mockClear()
})

describe('ProductListScreen · Productos e inventario', () => {
  it('título de la maqueta y columnas exactas en su orden (con Marca)', async () => {
    wrap()
    expect(await screen.findByRole('heading', { level: 1, name: 'Productos e inventario' })).toBeInTheDocument()
    const table = await screen.findByRole('table', { name: 'Productos e inventario' })
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((th) => th.textContent?.replace(/[▲▼]/g, '').trim())
    expect(headers).toEqual(['SKU', 'Producto', 'Categoría', 'Marca', 'Dueño', 'Disponible', 'Reservado', 'Total', 'Rastreo', 'Estatus'])
    // sin buscador dentro de la tabla
    expect(screen.queryByPlaceholderText('Buscar…')).toBeNull()
  })

  it('cada fila: marca con el modelo debajo, dueño, disponible/reservado/total del API, rastreo del catálogo y estado', async () => {
    wrap()
    const table = await screen.findByRole('table', { name: 'Productos e inventario' })
    const cells = (sku: string) =>
      within(within(table).getByText(sku).closest('tr')!)
        .getAllByRole('cell')
        .map((td) => td.textContent)
    await waitFor(() => expect(cells('GLU-100')[8]).toBe('Serie'))
    expect(cells('GLU-100')).toEqual(['GLU-100', 'Medidor de glucosa', 'Médico', 'AbbottFreeStyle Lite', 'Propio', '40', '—', '40', 'Serie', 'OK'])
    expect(within(table).getByTitle('Abbott · FreeStyle Lite')).toBeInTheDocument()
    expect(cells('GLU-STR')).toEqual(['GLU-STR', 'Tiras reactivas', 'Médico', '', 'Farmacia Central', '150', '30', '180', 'Lote', 'Bajo mínimo'])
    expect(cells('OLD-01')[9]).toBe('Inactivo')
    expect(within(table).getByText('OLD-01').closest('tr')).toHaveClass('dim')
  })

  it('KPIs de todo el catálogo con take=1; Bajo mínimo y Con número de serie en naranja solo con algo que atender', async () => {
    wrap()
    const river = await screen.findByRole('group', { name: 'Resumen del catálogo' })
    const serial = await within(river).findByRole('button', { name: 'Con número de serie: 7. 1 sin series completas' })
    expect(serial).toHaveClass('money')
    expect(within(serial).getByText('1 sin series completas')).toBeInTheDocument()
    expect(within(river).getByRole('button', { name: 'SKUs activos: 250' })).toHaveClass('flow')
    expect(within(river).getByRole('button', { name: 'Unidades totales: 1,250' })).toHaveClass('flow')
    // la cifra suma solo productos activos (coincide con el filtro del KPI)
    expect(mock.requests.some((u) => u.pathname === '/api/v1/inventory/balances' && u.searchParams.get('activeProductsOnly') === 'true')).toBe(true)
    expect(within(river).getByRole('button', { name: 'Bajo mínimo: 2' })).toHaveClass('money')
    const products = mock.requests.filter((u) => u.pathname === '/api/v1/products').map((u) => u.search)
    expect(products).toContain('?activeOnly=true&take=1')
    expect(products).toContain('?belowMin=true&take=1')
    expect(products).toContain('?unavailable=true&take=1')   // tableta "No disponibles" (2026-10-05)
    expect(products).toContain('?activeOnly=true&serialOnly=true&take=1')
    expect(products).toContain('?serialMissing=true&take=1')
    // ya no se recorre el catálogo para contar las series
    expect(products.some((s) => s.includes('take=200'))).toBe(false)
  })

  it('sin bajo mínimo ni series incompletas, los KPIs van en azul y sin aviso', async () => {
    mock.belowMin = 0
    mock.serialMissing = 0
    wrap()
    const river = await screen.findByRole('group', { name: 'Resumen del catálogo' })
    expect(await within(river).findByRole('button', { name: 'Bajo mínimo: 0' })).toHaveClass('flow')
    expect(await within(river).findByRole('button', { name: 'Con número de serie: 7' })).toHaveClass('flow')
    expect(within(river).queryByText(/sin series completas/)).toBeNull()
  })

  it('clic en un KPI filtra la tabla (resaltado, página 1); otro clic lo quita; ?kpi= abre filtrado', async () => {
    const user = userEvent.setup()
    wrap()
    const river = await screen.findByRole('group', { name: 'Resumen del catálogo' })
    await user.click(await screen.findByRole('button', { name: 'Página siguiente' }))
    await waitFor(() => expect(lastListQuery().get('skip')).toBe('25'))

    const units = within(river).getByRole('button', { name: /^Unidades totales/ })
    // Unidades totales: activos con existencia en mano (onlyOnHand), no "con disponible"; el aviso dice que la cifra suma inactivos
    expect(units).toHaveAttribute('title', expect.stringContaining('Activos con existencia'))
    expect(units).toHaveAttribute('title', expect.stringContaining('existencia en mano de los productos activos'))
    await user.click(units)
    await waitFor(() => expect(lastListQuery().get('onlyOnHand')).toBe('true'))
    expect(lastListQuery().has('onlyAvailable')).toBe(false)
    expect(lastListQuery().get('activeOnly')).toBe('true')
    expect(lastListQuery().get('skip')).toBe('0')
    expect(units).toHaveAttribute('aria-pressed', 'true')
    expect(units).toHaveClass('on')

    const active = within(river).getByRole('button', { name: /^SKUs activos/ })
    await user.click(active)
    await waitFor(() => expect(lastListQuery().has('onlyOnHand')).toBe(false))
    expect(lastListQuery().get('activeOnly')).toBe('true')
    expect(units).toHaveAttribute('aria-pressed', 'false')

    await user.click(within(river).getByRole('button', { name: /^Con número de serie/ }))
    await waitFor(() => expect(lastListQuery().get('serialOnly')).toBe('true'))
    await user.click(within(river).getByRole('button', { name: /^Con número de serie/ }))
    await waitFor(() => expect(lastListQuery().has('serialOnly')).toBe(false))
    expect(lastListQuery().has('activeOnly')).toBe(false)
  })

  it('tableta "No disponibles": entre Bajo mínimo y Con número de serie; al tocarla filtra la tabla con unavailable', async () => {
    const user = userEvent.setup()
    wrap()
    const river = await screen.findByRole('group', { name: 'Resumen del catálogo' })
    const names = (await within(river).findAllByRole('button')).map((b) => (b.textContent ?? '').replace(/[\d,.…—]+.*$/, '').trim())
    expect(names.indexOf('No disponibles')).toBe(names.indexOf('Bajo mínimo') + 1)
    expect(names.indexOf('Con número de serie')).toBe(names.indexOf('No disponibles') + 1)
    const tile = within(river).getByRole('button', { name: /^No disponibles/ })
    expect(tile).toHaveAttribute('title', expect.stringContaining('sin existencia o con todo reservado'))
    await user.click(tile)
    await waitFor(() => expect(lastListQuery().get('unavailable')).toBe('true'))
    expect(lastListQuery().get('activeOnly')).toBe('true')
    expect(tile).toHaveAttribute('aria-pressed', 'true')
    await user.click(tile)
    await waitFor(() => expect(lastListQuery().has('unavailable')).toBe(false))
  })

  it('?kpi=low abre con Bajo mínimo elegido', async () => {
    wrap('/warehouse/products?kpi=low')
    const river = await screen.findByRole('group', { name: 'Resumen del catálogo' })
    expect(await within(river).findByRole('button', { name: /^Bajo mínimo/ })).toHaveAttribute('aria-pressed', 'true')
    await waitFor(() => expect(lastListQuery().get('belowMin')).toBe('true'))
  })

  it('Lote 15: ?kpi=low&warehousePublicIds= (franja "Almacén hoy" del Pulso) abre con Bajo mínimo y el almacén elegidos', async () => {
    wrap(`/warehouse/products?kpi=low&warehousePublicIds=${WH}`)
    await waitFor(() => expect(lastListQuery().get('belowMin')).toBe('true'))
    expect(lastListQuery().getAll('warehousePublicIds')).toEqual([WH])
  })

  it('filtros al API y a la página 1: Almacén (varios), SKU, Nombre, Categoría y Marca', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Página siguiente' }))
    await waitFor(() => expect(lastListQuery().get('skip')).toBe('25'))

    await pickOptions(user, 'Almacén', ['ALM-01 · Almacén principal', 'ALM-02 · Almacén norte'])
    await waitFor(() => expect(lastListQuery().getAll('warehousePublicIds')).toEqual([WH, WH2]))
    expect(lastListQuery().get('skip')).toBe('0')
    expect(lastListQuery().has('warehousePublicId')).toBe(false)

    await user.type(screen.getByRole('combobox', { name: 'SKU' }), 'glu')
    await user.click(await screen.findByRole('option', { name: /GLU-100 · Medidor de glucosa/ }))
    await waitFor(() => expect(lastListQuery().getAll('productPublicIds')).toEqual([P1]))

    await user.type(screen.getByRole('searchbox', { name: 'Nombre' }), 'medidor')
    await waitFor(() => expect(lastListQuery().get('name')).toBe('medidor'))
    expect(lastListQuery().has('search')).toBe(false)

    await pickOptions(user, 'Categoría', ['Médico'])
    await waitFor(() => expect(lastListQuery().getAll('categoryIds')).toEqual(['3']))

    await pickOptions(user, 'Marca', ['Roche'])
    await waitFor(() => expect(lastListQuery().getAll('brands')).toEqual(['Roche']))

    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(lastListQuery().has('brands')).toBe(false))
    expect(lastListQuery().has('warehousePublicIds')).toBe(false)
    // el nombre se aplica con la misma pausa que al escribir
    await waitFor(() => expect(lastListQuery().has('name')).toBe(false))
  })

  it('Reporte de inventario: lee todo lo filtrado (de a 200) y arma el PDF con filtros por nombre, grupos y valor', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('table', { name: 'Productos e inventario' })
    await pickOptions(user, 'Almacén', ['ALM-01 · Almacén principal'])
    await user.click(screen.getByRole('button', { name: /^Unidades totales/ }))
    await user.click(screen.getByRole('button', { name: 'Reporte de inventario' }))
    await waitFor(() => expect(downloadReportPdf).toHaveBeenCalledTimes(1))
    const read = mock.requests.find((u) => u.pathname === '/api/v1/products' && u.searchParams.get('take') === '200')!
    expect(read.searchParams.getAll('warehousePublicIds')).toEqual([WH])
    expect(read.searchParams.get('onlyOnHand')).toBe('true')
    expect(read.searchParams.has('onlyAvailable')).toBe(false)
    const spec = vi.mocked(downloadReportPdf).mock.calls[0][0] as ReportSpec
    expect(spec.title).toBe('Reporte de inventario')
    expect(spec.filters.map((f) => f.label)).toEqual(['Almacén', 'Vista'])
    expect(spec.filters[0].value).toContain('ALM-01 · Almacén principal')
    expect(spec.filters[1].value).toBe('Activos con existencia')
    // el producto sin categoría no tiene existencia: no se lista, solo se cuenta en un aviso
    expect(spec.sections.map((s) => s.title)).toEqual(['Médico (2)'])
    expect(spec.notices).toContain('Productos sin existencia (en mano 0) no incluidos: 1.')
    // valor = total × costo; sin costo = '—'
    expect(spec.sections[0].rows[0]).toEqual(['GLU-100', 'Medidor de glucosa', 'Médico', 'Abbott', 40, 0, 40, 12.5, 500])
  })

  it('Reporte de ajustes: traslada los filtros al Kárdex con el tipo ADJUSTMENT y avisa que el KPI no aplica', async () => {
    const user = userEvent.setup()
    wrap('/warehouse/products?kpi=low')
    await screen.findByRole('table', { name: 'Productos e inventario' })
    await pickOptions(user, 'Marca', ['Abbott'])
    await user.click(screen.getByRole('button', { name: 'Reporte de ajustes' }))
    await waitFor(() => expect(downloadReportPdf).toHaveBeenCalledTimes(1))
    const read = mock.requests.find((u) => u.pathname === '/api/v1/inventory/transactions')!
    expect(read.searchParams.getAll('types')).toEqual(['ADJUSTMENT'])
    expect(read.searchParams.getAll('brands')).toEqual(['Abbott'])
    expect(read.searchParams.has('belowMin')).toBe(false)
    const spec = vi.mocked(downloadReportPdf).mock.calls[0][0] as ReportSpec
    expect(spec.title).toBe('Reporte de ajustes')
    expect(spec.sections[0].rows[0]).toEqual([expect.any(String), 'GLU-100', 'Medidor de glucosa', 'ALM-01 / A-01', -2, 'Dañado', 'Caja mojada', 'Ana Pérez'])
    expect(spec.notices?.some((n) => n.includes('Bajo mínimo'))).toBe(true)
  })

  it('"Nuevo producto" solo con inventory.manage; clic en la fila pide la ficha para "Editar producto"', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('table', { name: 'Productos e inventario' })
    expect(screen.queryByRole('button', { name: 'Nuevo producto' })).toBeNull()
    await user.click(screen.getByText('GLU-100'))
    await waitFor(() => expect(mock.requests.some((u) => u.pathname === `/api/v1/products/${P1}`)).toBe(true))
  })

  it('con inventory.manage aparece "Nuevo producto"; la pestaña Categorías va en ?tab=categories', async () => {
    wrap('/warehouse/products?tab=categories', ['inventory.view', 'inventory.manage'])
    expect(await screen.findByRole('tab', { name: 'Categorías' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('group', { name: 'Resumen del catálogo' })).toBeNull()
    await userEvent.setup().click(screen.getByRole('tab', { name: 'Productos' }))
    expect(await screen.findByRole('button', { name: 'Nuevo producto' })).toBeInTheDocument()
  })
})
