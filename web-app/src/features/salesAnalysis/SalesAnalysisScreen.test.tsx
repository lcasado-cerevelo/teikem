// Lote 241 — pantalla «Análisis de ventas» sobre un API simulado: cada pestaña (Por representante con su exportación a Excel,
// Movimiento de productos con grupos de columnas y matrices, Gráficos con «Imprimir presentación», Configuración), el aviso de
// estado con sus enlaces, las cuotas en bloque («copiar del mes anterior» + guardar solo lo que cambió), la importación con
// simulación primero y confirmación, la asignación en bloque de clientes y la vista de solo lectura sin `sales.analysis.manage`.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import type { WorkBook } from 'xlsx'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { todayIso } from '../../kernel/format'
import { setLang } from '../../kernel/i18n/i18n'
import SalesAnalysisScreen from './SalesAnalysisScreen'

const BY_REP = {
  month: '2026-10',
  compareMonth: '2026-09',
  cutoffDate: '2026-11-06',
  warnings: [],
  reps: [
    { routeId: 1, routeName: 'Cadenas', repId: 1, repName: 'ADVANCE SOLUTIONS GROUP LLC', compareSales: 60773, sales: 58866.41, change: -1906.59, changePct: -0.0314, quota: 57700, compliance: 1.0202 },
    { routeId: 3, routeName: 'Metro', repId: 3, repName: 'INDHYRA S ORTIZ ORTIZ', compareSales: 10432.94, sales: 7175.18, change: -3257.76, changePct: -0.3123, quota: 12500, compliance: 0.574 },
  ],
  totals: { compareSales: 71205.94, sales: 66041.59, change: -5164.35, changePct: -0.0725, quota: 70200, compliance: 0.9408 },
  reconciliation: { routesTotal: 66041.59, unassigned: -23.16, shippingAndOther: 12, monthTotal: 66030.43, difference: 0, productSales: 70000, discounts: -3958.41, shipping: 12, other: 0, unassignedClients: 0, unassignedCompare: 0 },
  lines: [
    { line: 'Prodigy', isDiscounts: false, sales: 60000, shareOfNet: 0.9085, shareOfGross: 0.857, leaderRepId: 1, leaderName: 'ADVANCE SOLUTIONS GROUP LLC', leaderRoute: 'Cadenas', leaderSales: 55000 },
    { line: 'Padimont', isDiscounts: false, sales: 10000, shareOfNet: 0.15, shareOfGross: 0.143, leaderRepId: 3, leaderName: 'INDHYRA S ORTIZ ORTIZ', leaderRoute: 'Metro', leaderSales: 4000 },
    { line: 'Descuentos sin línea', isDiscounts: true, sales: -3958.41, shareOfNet: -0.06, shareOfGross: -0.056 },
  ],
  repLines: [
    { routeName: 'Cadenas', repId: 1, repName: 'ADVANCE SOLUTIONS GROUP LLC', line: 'Prodigy', sales: 58866.41, shareOfRep: 1 },
    { routeName: 'Metro', repId: 3, repName: 'INDHYRA S ORTIZ ORTIZ', line: 'Padimont', sales: 4000, shareOfRep: 0.557 },
  ],
  repProducts: [
    { routeName: 'Cadenas', repId: 1, repName: 'ADVANCE SOLUTIONS GROUP LLC', line: 'Prodigy', productId: 5, sku: '52800', product: 'PRODIGY STRIPS 1/50CT.', lineClass: 'PRODUCT', units: 11648, sales: 48808.9 },
    { routeName: 'Metro', repId: 3, repName: 'INDHYRA S ORTIZ ORTIZ', line: 'Padimont', productId: 39, sku: 'NECH1003', product: 'Padimont Gloves Large', lineClass: 'PRODUCT', units: 100, sales: 499.4 },
  ],
  routeProducts: [
    { routeId: 1, routeName: 'Cadenas', repNames: 'ADVANCE SOLUTIONS GROUP LLC', line: 'Padimont', productId: 39, sku: 'NECH1003', product: 'Padimont Gloves Large', units: 0, sales: 0 },
  ],
}
const TX = { date: '2026-10-01', source: 'HISTORY', type: 'Invoice', number: '2025-5114', clientName: 'FARMACIA CARIDAD #13', repId: 1, repName: 'ADVANCE SOLUTIONS GROUP LLC', routeName: 'Cadenas', productId: 5, sku: '52800', product: 'PRODIGY STRIPS 1/50CT.', line: 'Prodigy', lineClass: 'PRODUCT', quantity: 160, unitPrice: 4.18, amount: 668.8 }
const MOVEMENT = {
  fromMonth: '2026-09',
  toMonth: '2026-11',
  asOf: '2026-11-05',
  lastCompleteMonth: '2026-10',
  weeksFrom: '2026-09-07',
  weeksTo: '2026-11-01',
  leadTimeDays: 5,
  serviceLevelZ: 1.645,
  months: ['2026-09', '2026-10', '2026-11'],
  warnings: [],
  totalUnits: 3015,
  totalSales: 14999.95,
  products: [
    { productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large', category: 'GLOVES', brand: 'PADIMONT', line: 'Padimont', matched: true, abc: 'B', status: 'STABLE', price: 5, cost: 3.5, marginPct: 0.3, onHand: 835, reserved: 0, available: 835, units: 1015, sales: 5013.95, shareOfSales: 0.334, monthlyAverage: 450, bestMonth: '2026-09', weeklyAverage: 106.25, weeklyConfidence: 'HIGH', coverageWeeks: 7.9, coverageMonths: 1.8, projected1: 373, projected2: 0, projected3: 0, reorderPoint: 140, suggestedReorderPoint: 132, trend: -0.2348, clients: 152, invoices: 215, topClient: 'Farmacia Deborah #2', topClientUnits: 240, lastSale: '2026-11-05', pendingPurchase: 0 },
    { productId: 5, sku: '52800', name: 'PRODIGY STRIPS 1/50CT.', line: 'Prodigy', matched: true, abc: 'A', status: 'RISING', units: 2000, sales: 9986, onHand: 900, reserved: 0, available: 900, weeklyAverage: 600, coverageWeeks: 1.5, suggestedReorderPoint: 1000, lastSale: '2026-11-04' },
  ],
  unitsByMonth: [
    { productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large', values: [460, 440, 115], total: 1015, min: 440, max: 460, average: 450 },
    { productId: 5, sku: '52800', name: 'PRODIGY STRIPS 1/50CT.', values: [0, 1500, 500], total: 2000, min: 1500, max: 1500, average: 1500 },
  ],
  salesByMonth: [{ productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large', values: [2353.85, 2219.1, 441], total: 5013.95 }],
  purchasesByMonth: [{ productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large', values: [900, 0, 0], total: 900 }],
  topClients: [{ productId: 39, sku: 'NECH1003', name: 'Padimont Gloves Large', rank: 1, client: 'Farmacia Deborah #2', units: 240, shareOfProduct: 0.236 }],
}
const CHARTS = {
  months: ['2026-09', '2026-10'],
  totalByMonth: { key: 'TOTAL', label: 'Total', values: [142792.04, 141902.65] },
  byLineByMonth: [{ key: 'Prodigy', label: 'Prodigy', values: [130000, 134341.2] }],
  month: '2026-10',
  repVsQuota: [{ repId: 1, rep: 'ADVANCE SOLUTIONS GROUP LLC', route: 'Cadenas', sales: 58866.41, quota: 57700, compliance: 1.0202 }],
  lineShare: [{ line: 'Prodigy', sales: 134341.2, share: 0.9117 }],
  topProductUnits: [{ key: '52800', label: '52800', values: [23554, 23818] }],
  coverage: [{ productId: 5, sku: '52800', name: 'PRODIGY STRIPS', abc: 'A', available: 900, weeklyAverage: 600, coverageWeeks: 1.5 }],
}
const STATUS = {
  cutoffDate: null,
  leadTimeDays: 5,
  serviceLevelZ: 1.645,
  batches: [{ publicId: 'b1', kind: 'SALES', fileName: 'SalesByItemDetail.CSV', dateFrom: '2025-12-19', dateTo: '2026-11-05', lineCount: 13350, totalAmount: 1285918.96, importedAtUtc: '2026-11-12T10:00:00', isActive: true }],
  activeClients: 700,
  clientsWithoutRep: 3,
  brandsWithoutLine: ['YARDLEY'],
  unmatchedClients: [{ raw: 'FARMACIA FANTASMA', lines: 4, amount: 120, firstDate: '2026-01-02', lastDate: '2026-03-04' }],
  unmatchedItems: [],
  unmatchedPurchaseItems: [{ raw: '53310/53350', lines: 2, amount: 50, firstDate: '2026-04-01', lastDate: '2026-04-02' }],
  overlap: false,
  warnings: [],
}
const REPS = [
  { id: 1, name: 'ADVANCE SOLUTIONS GROUP LLC', routeId: 1, routeName: 'Cadenas', isActive: true, clientCount: 100, currentQuota: 57700 },
  { id: 2, name: 'CINDY OLIVAS DAVILA', routeId: 2, routeName: 'Norte', isActive: true, clientCount: 80, currentQuota: null },
]
const SIM_REPORT = (dryRun: boolean) => ({
  dryRun,
  company: 'Advance Solutions',
  files: [
    {
      kind: 'SALES',
      fileName: 'SalesByItemDetail.CSV',
      linesRead: 13350,
      linesSaved: 13350,
      zeroQtySkipped: 0,
      duplicatesSkipped: 0,
      linesByClass: { PRODUCT: 11542, DISCOUNT: 1795, SHIPPING: 11, OTHER: 2 },
      amountByClass: { PRODUCT: 1354427.03, DISCOUNT: -68634.37, SHIPPING: 132, OTHER: -5.7 },
      dateFrom: '2025-12-19',
      dateTo: '2026-11-05',
      totalAmount: 1285918.96,
      reportTotal: 1285918.96,
      matchedClients: 700,
      matchedItems: 41,
      unmatchedClients: [{ raw: 'FARMACIA FANTASMA', lines: 4, amount: 120, firstDate: '2026-01-02', lastDate: '2026-03-04' }],
      unmatchedItems: [],
      byMonth: [{ month: '2026-10', lines: 1600, quantity: 20000, amount: 141902.65, productAmount: 147359.82 }],
      action: dryRun ? 'NEW' : 'NEW',
      replacedLines: 0,
      warnings: [],
    },
  ],
  warnings: [],
})

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  uploads: [] as { field: string; value: unknown }[],
  workbooks: [] as { wb: WorkBook; fileName: string }[],
}))

vi.mock('./salesAnalysisExcel', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./salesAnalysisExcel')>()
  const XLSX = await import('xlsx')
  return {
    ...actual,
    downloadWorkbook: vi.fn(async (make: (x: typeof XLSX) => WorkBook, fileName: string) => {
      mock.workbooks.push({ wb: make(XLSX), fileName })
    }),
  }
})

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    let body: unknown
    try {
      body = text ? JSON.parse(text) : undefined
    } catch {
      body = text
    }
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname.replace('/api/v1/sales-analysis', '§')
    const m = req.method
    if (m === 'GET' && p === '§/status') return json(STATUS)
    if (m === 'GET' && p === '§/by-rep') return json(BY_REP)
    if (m === 'GET' && p === '§/transactions') return json({ items: [TX], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === '§/product-movement') return json(MOVEMENT)
    if (m === 'GET' && p === '§/charts') return json(CHARTS)
    if (m === 'GET' && p === '§/routes') return json([{ id: 1, name: 'Cadenas', sortOrder: 10, isActive: true, repCount: 1 }, { id: 2, name: 'Norte', sortOrder: 20, isActive: true, repCount: 1 }])
    if (m === 'GET' && p === '§/reps') return json(REPS)
    if (m === 'GET' && p === '§/quotas') {
      const month = url.searchParams.get('month') ?? ''
      const first = month.endsWith('-01')
      return json({ month, rows: [{ repId: 1, repName: REPS[0].name, routeName: 'Cadenas', repActive: true, amount: first ? 57700 : null, effective: 57700, effectiveFrom: month.slice(0, 4) + '-01' }, { repId: 2, repName: REPS[1].name, routeName: 'Norte', repActive: true, amount: null, effective: null }], totalEffective: 57700 })
    }
    if (m === 'PUT' && p.startsWith('§/quotas/')) return json({ month: p.slice('§/quotas/'.length), rows: [], totalEffective: 0 })
    if (m === 'GET' && p === '§/clients') return json({ items: [{ clientId: 11, publicId: 'c11', code: 'F11', name: 'Farmacia Once', isActive: true }, { clientId: 12, publicId: 'c12', code: 'F12', name: 'Farmacia Doce', isActive: true }], total: 2, skip: 0, take: 25 })
    if (m === 'POST' && p === '§/clients/assign-rep') return json({ updated: 2, repId: 1, repName: REPS[0].name })
    if (m === 'POST' && p === '§/import') {
      const dry = [...mock.uploads].reverse().find((u) => u.field === 'dryRun')?.value
      return json(SIM_REPORT(dry !== 'false'))
    }
    if (m === 'GET' && url.pathname === '/api/v1/tenant/settings') return json({ name: 'Advance Solutions' })
    if (m === 'GET' && url.pathname === '/api/v1/users') return json([])
    if (url.pathname.startsWith('/api/v1/catalogs/') || url.pathname.startsWith('/api/v1/status/')) return json([])
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

class RecordingFormData {
  append(field: string, value: unknown) {
    mock.uploads.push({ field, value })
  }
}

const VIEW = ['sales.view', 'sales.analysis.view']
const MANAGE = [...VIEW, 'sales.analysis.manage']

function wrap(perms: string[], initial = '/analytics/sales') {
  const session = { me: { userId: 9, tenantName: 'Advance Solutions' }, isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', permissions: new Set(perms), modules: new Set(['SALES_ORDERS']) } as unknown as Session
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={session}>
        <AccessProvider permissions={perms} modules={['SALES_ORDERS']}>
          <MemoryRouter initialEntries={[initial]}>
            <SalesAnalysisScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const req = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

describe('SalesAnalysisScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.uploads.length = 0
    mock.workbooks.length = 0
  })
  afterEach(() => vi.unstubAllGlobals())

  it('«Por representante»: tarjetas, Comparativo con cumplimiento y conciliación, Líneas, transacciones del mes y el aviso de estado con enlaces', async () => {
    wrap(VIEW)
    expect(await screen.findByRole('heading', { name: 'Análisis de ventas', level: 1 })).toBeInTheDocument()
    const comp = await screen.findByRole('table', { name: 'Comparativo por ruta y representante' })
    expect(within(comp).getAllByText('ADVANCE SOLUTIONS GROUP LLC').length).toBeGreaterThan(0)
    expect(within(comp).getByText('$58,866.41')).toBeInTheDocument()
    expect(within(comp).getByText('57.4%')).toBeInTheDocument()
    const rec = screen.getByRole('table', { name: 'Totales y conciliación' })
    expect(within(rec).getByText('Total de las rutas')).toBeInTheDocument()
    expect(within(rec).getByText('-$23.16')).toBeInTheDocument()
    expect(within(rec).getByText('$66,030.43')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Cumplimiento de la cuota total' })).toHaveTextContent('94.1%')
    const lines = screen.getByRole('table', { name: 'Líneas' })
    expect(within(lines).getByText('No aplica')).toBeInTheDocument()
    // transacciones: paginadas por el servidor con el mes
    await waitFor(() => expect(req('GET', '/api/v1/sales-analysis/transactions').some((r) => r.search.includes('month=2026-10') && r.search.includes('take=25'))).toBe(true))
    expect(await screen.findByText('FARMACIA CARIDAD #13')).toBeInTheDocument()
    // aviso de estado
    const banner = await screen.findByRole('complementary', { name: 'Para que el análisis cuadre falta:' })
    expect(within(banner).getByText(/3 cliente\(s\) activo\(s\) sin representante/)).toBeInTheDocument()
    expect(within(banner).getByRole('link', { name: 'Asignar representantes' })).toHaveAttribute('href', '/analytics/sales?tab=setup&sec=clients')
    expect(within(banner).getByRole('link', { name: 'Emparejar a mano' })).toHaveAttribute('href', '/analytics/sales?tab=setup&sec=link')
    expect(within(banner).getByRole('link', { name: 'Poner la fecha de corte' })).toBeInTheDocument()
  })

  it('«Exportar a Excel» arma UN libro con las 7 hojas del dueño (transacciones de todo el mes e inventario del mismo mes)', async () => {
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByRole('table', { name: 'Comparativo por ruta y representante' })
    await user.click(screen.getByRole('button', { name: 'Exportar a Excel' }))
    await waitFor(() => expect(mock.workbooks).toHaveLength(1))
    const { wb, fileName } = mock.workbooks[0]
    expect(wb.SheetNames).toEqual(['Comparativo', 'Marcas', 'Representante producto', 'Ventas marca por representante', 'Variedades por ruta', 'Inventario', 'Transacciones'])
    expect(fileName).toMatch(/^analisis-de-ventas-2026-10.*\.xlsx$/)
    expect(req('GET', '/api/v1/sales-analysis/transactions').some((r) => r.search.includes('all=true'))).toBe(true)
    expect(req('GET', '/api/v1/sales-analysis/product-movement').some((r) => r.search.includes('fromMonth=2026-10') && r.search.includes('toMonth=2026-10'))).toBe(true)
    expect(wb.Sheets.Transacciones.F4?.v).toBe('FARMACIA CARIDAD #13')
  })

  it('«Movimiento de productos»: Resumen con chips de ABC, estado y cobertura; grupos de columnas; matrices por mes; definiciones', async () => {
    const user = userEvent.setup()
    wrap(VIEW, '/analytics/sales?tab=movement')
    const table = await screen.findByRole('table', { name: 'Resumen por producto' })
    expect(within(table).getByText('Estable')).toBeInTheDocument()
    expect(within(table).getByText('Subiendo')).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: /Venta \$/ })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Inventario y cobertura' }))
    const t2 = screen.getByRole('table', { name: 'Resumen por producto' })
    expect(within(t2).getByRole('columnheader', { name: /Semanas de cobertura/ })).toBeInTheDocument()
    expect(within(t2).getByText('1.5 sem.')).toBeInTheDocument()
    expect(within(t2).getByText('7.9 sem.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Reorden' }))
    expect(within(screen.getByRole('table', { name: 'Resumen por producto' })).getByRole('columnheader', { name: /Punto de reorden sugerido/ })).toBeInTheDocument()
    // matriz: unidades por mes con el mes parcial, y compras
    const matrix = screen.getByRole('region', { name: 'Unidades por mes' })
    expect(within(matrix).getByRole('columnheader', { name: 'nov-26 (al 5)' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Compras por mes' }))
    expect(within(screen.getByRole('region', { name: 'Compras por mes' })).getByRole('columnheader', { name: 'Total comprado' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Qué significa cada columna' })).toBeInTheDocument()
    // exportar el segundo libro
    await user.click(screen.getByRole('button', { name: 'Exportar a Excel' }))
    await waitFor(() => expect(mock.workbooks).toHaveLength(1))
    expect(mock.workbooks[0].wb.SheetNames).toEqual(['Resumen', 'Unidades por mes', 'Venta $ por mes', 'Compras por mes', 'Clientes por producto', 'Gráfico', 'Notas'])
    expect(mock.workbooks[0].wb.Sheets.Resumen.A1?.v).toBe('Movimiento de productos - Advance Solutions')
  })

  it('«Gráficos»: los cinco gráficos con título, resumen accesible y datos; «Imprimir presentación» abre la impresión', async () => {
    const print = vi.fn()
    vi.stubGlobal('print', print)
    const user = userEvent.setup()
    wrap(VIEW, '/analytics/sales?tab=charts')
    const present = await screen.findByRole('region', { name: 'Presentación de ventas' })
    for (const title of ['Venta por mes', 'Venta contra cuota por representante', 'Participación por línea', 'Unidades por mes de los 1 productos principales', 'Cobertura por producto'])
      expect(within(present).getByRole('heading', { name: title })).toBeInTheDocument()
    expect(within(present).getByText('Advance Solutions')).toBeInTheDocument()
    expect(within(present).getByRole('img', { name: /Venta contra cuota por representante\. ADVANCE SOLUTIONS GROUP LLC: \$58,866\.41 de \$57,700\.00/ })).toBeInTheDocument()
    expect(within(present).getAllByText('Ver los datos')).toHaveLength(5)
    await user.click(screen.getByRole('button', { name: 'Imprimir presentación o guardar PDF' }))
    expect(print).toHaveBeenCalledTimes(1)
  })

  it('Cuotas: «copiar del mes anterior» y una celda editada se guardan en bloque, solo lo que cambió y un PUT por mes', async () => {
    const user = userEvent.setup()
    const year = todayIso().slice(0, 4)
    wrap(MANAGE, '/analytics/sales?tab=setup&sec=quotas')
    const feb = `${year}-02`
    await screen.findByRole('region', { name: 'Cuotas' })
    expect(screen.getByLabelText(`Cuota de ADVANCE SOLUTIONS GROUP LLC en ene-${year.slice(2)}`)).toHaveValue('57700')
    // vacía = heredada (sugerencia tenue)
    expect(screen.getByLabelText(`Cuota de ADVANCE SOLUTIONS GROUP LLC en feb-${year.slice(2)}`)).toHaveAttribute('placeholder', '$57,700')
    await user.click(screen.getByRole('button', { name: `Copiar en feb-${year.slice(2)} las cuotas del mes anterior` }))
    expect(screen.getByLabelText(`Cuota de ADVANCE SOLUTIONS GROUP LLC en feb-${year.slice(2)}`)).toHaveValue('57700')
    await user.type(screen.getByLabelText(`Cuota de CINDY OLIVAS DAVILA en mar-${year.slice(2)}`), '16500')
    await user.click(screen.getByRole('button', { name: 'Guardar (2)' }))
    await waitFor(() => expect(mock.requests.filter((r) => r.method === 'PUT')).toHaveLength(2))
    const puts = mock.requests.filter((r) => r.method === 'PUT')
    expect(puts.map((r) => [r.path, r.body])).toEqual([
      [`/api/v1/sales-analysis/quotas/${feb}`, { items: [{ repId: 1, amount: 57700 }] }],
      [`/api/v1/sales-analysis/quotas/${year}-03`, { items: [{ repId: 2, amount: 16500 }] }],
    ])
  })

  it('una cuota inválida no deja guardar y lo dice', async () => {
    const user = userEvent.setup()
    const yy = todayIso().slice(2, 4)
    wrap(MANAGE, '/analytics/sales?tab=setup&sec=quotas')
    await user.type(await screen.findByLabelText(`Cuota de CINDY OLIVAS DAVILA en abr-${yy}`), '12.345')
    expect(screen.getByRole('alert')).toHaveTextContent('la cuota debe ser cero o mayor, con hasta 2 decimales')
    expect(screen.getByRole('button', { name: 'Guardar (0)' })).toBeDisabled()
  })

  it('Importar: simulación primero (nada guardado, total contra el reporte, sin emparejar) y luego «Importar de verdad» con confirmación', async () => {
    vi.stubGlobal('FormData', RecordingFormData)
    const user = userEvent.setup()
    wrap(MANAGE, '/analytics/sales?tab=setup&sec=import')
    const real = await screen.findByRole('button', { name: 'Importar de verdad' })
    expect(real).toBeDisabled()
    await user.upload(screen.getByLabelText('Reporte de ventas (Sales by Item Detail, CSV)'), new File(['Type,Date'], 'SalesByItemDetail.CSV', { type: 'text/csv' }))
    await user.click(screen.getByRole('button', { name: 'Simular' }))
    const result = await screen.findByRole('region', { name: 'Resultado de la importación' })
    expect(within(result).getByText('no se guardó nada')).toBeInTheDocument()
    expect(within(result).getByText('cuadra')).toBeInTheDocument()
    expect(within(result).getByRole('table', { name: 'Clientes sin emparejar (1)' })).toHaveTextContent('FARMACIA FANTASMA')
    expect(within(result).getByRole('table', { name: 'Por clase' })).toHaveTextContent('Descuento')
    expect(mock.uploads).toEqual(expect.arrayContaining([{ field: 'dryRun', value: 'true' }, expect.objectContaining({ field: 'salesFile' })]))
    await user.click(screen.getByRole('button', { name: 'Importar de verdad' }))
    const dialog = await screen.findByRole('dialog', { name: '¿Importar de verdad?' })
    expect(dialog).toHaveTextContent('SalesByItemDetail.CSV')
    await user.click(within(dialog).getByRole('button', { name: 'Importar de verdad' }))
    await waitFor(() => expect(screen.getByRole('region', { name: 'Resultado de la importación' })).toHaveTextContent('guardado'))
    expect(mock.uploads.filter((u) => u.field === 'dryRun').map((u) => u.value)).toEqual(['true', 'false'])
    expect(req('POST', '/api/v1/sales-analysis/import')).toHaveLength(2)
  })

  it('Clientes sin representante: elegir varios y asignarlos en bloque', async () => {
    const user = userEvent.setup()
    wrap(MANAGE, '/analytics/sales?tab=setup&sec=clients')
    await user.click(await screen.findByRole('checkbox', { name: 'Elegir a Farmacia Once' }))
    await user.click(screen.getByRole('checkbox', { name: 'Elegir a Farmacia Doce' }))
    expect(screen.getByText('2 elegido(s)')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Asignar a…'), '1')
    await user.click(screen.getByRole('button', { name: 'Asignar' }))
    await waitFor(() => expect(req('POST', '/api/v1/sales-analysis/clients/assign-rep')).toHaveLength(1))
    expect(req('POST', '/api/v1/sales-analysis/clients/assign-rep')[0].body).toEqual({ clientIds: [11, 12], repId: 1 })
    expect(req('GET', '/api/v1/sales-analysis/clients').some((r) => r.search.includes('unassigned=true'))).toBe(true)
  })

  it('sin «administrar» la configuración es de solo lectura: sin Importar ni Emparejar, cuotas sin campos', async () => {
    wrap(VIEW, '/analytics/sales?tab=setup&sec=quotas')
    expect(await screen.findByText(/Solo lectura/)).toBeInTheDocument()
    const tabs = screen.getByRole('tablist', { name: 'Secciones de la configuración' })
    expect(within(tabs).queryByRole('tab', { name: 'Importar historial' })).toBeNull()
    expect(within(tabs).queryByRole('tab', { name: 'Emparejar a mano' })).toBeNull()
    await screen.findByRole('region', { name: 'Cuotas' })
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(screen.queryByRole('button', { name: /Guardar/ })).toBeNull()
  })
})
