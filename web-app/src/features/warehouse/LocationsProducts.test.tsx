// Informe "Productos por posición" en Ubicaciones, sobre un API simulado: casillas (marcar, toda la página, contador,
// quitar), filtro "Pasillo" (`aisle` al servidor), el botón abre el modal con el alcance correcto (filtro o marcadas) e
// impresión: lee `bin-products` y descarga el PDF con cada posición y sus productos; sin productos avisa sin generar; si el
// PDF falla el modal lo dice. No hay columna "Hoja" ni filtro "Hoja" (se quitaron).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { downloadBinSheetsPdf } from '../../kernel/ui/binSheetPdf'
import LocationsScreen from './LocationsScreen'

interface Call {
  method: string
  url: URL
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], bins: [] as Record<string, unknown>[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.calls.push({ method: req.method, url })
    const res = route(url)
    return res instanceof Response ? res : new Response(JSON.stringify(res), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

vi.mock('../../kernel/ui/binSheetPdf', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/ui/binSheetPdf')>()
  return { ...actual, downloadBinSheetsPdf: vi.fn(async (spec: Parameters<typeof actual.downloadBinSheetsPdf>[0]) => actual.planBinSheets(spec.bins, spec)) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const GENERATED = '2026-10-03T14:05:00.25'
const ZONES = [{ id: 1, code: 'A', name: 'Zona A', zoneTypeCode: 'STORAGE', isActive: true, binCount: 4, capacityQty: 0, qtyOnHand: 9, binsWithoutCapacity: 4 }]
const initialBins = () => [
  { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', aisle: 'A', qtyOnHand: 5, productCount: 1, singleProductSku: 'TORN-01', singleProductName: 'Tornillo', occupancy: 'NO_CAPACITY', isActive: true },
  { id: 11, code: 'A-02', zoneId: 1, zoneCode: 'A', aisle: 'A', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
  { id: 12, code: 'B-03', zoneId: 1, zoneCode: 'A', aisle: 'B', qtyOnHand: 2, productCount: 1, singleProductSku: 'TUER-01', singleProductName: 'Tuerca', occupancy: 'NO_CAPACITY', isActive: true },
  { id: 13, code: 'B-04', zoneId: 1, zoneCode: 'A', aisle: 'B', qtyOnHand: 2, productCount: 1, singleProductSku: 'ARAN-01', singleProductName: 'Arandela', occupancy: 'NO_CAPACITY', isActive: true },
]
const PRODUCTS: Record<number, { sku: string; name: string; barcode: string }[]> = {
  10: [{ sku: 'TORN-01', name: 'Tornillo', barcode: '7501234567890' }],
  12: [{ sku: 'TUER-01', name: 'Tuerca', barcode: '' }],
  13: [{ sku: 'ARAN-01', name: 'Arandela', barcode: '' }],
}

function matching(url: URL) {
  const ids = url.searchParams.getAll('binIds').map(Number)
  const aisle = (url.searchParams.get('aisle') ?? '').toLowerCase()
  return mock.bins.filter(
    (b) => (ids.length === 0 || ids.includes(b.id as number)) && (aisle === '' || String(b.aisle ?? '').toLowerCase().includes(aisle)),
  )
}

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/zones`) return ZONES
  if (p === `/api/v1/warehouses/${WH}/bins`) {
    const items = matching(url)
    return { total: items.length, skip: 0, take: Number(url.searchParams.get('take') ?? 100), items }
  }
  if (p === `/api/v1/warehouses/${WH}/bin-products`) {
    const items = matching(url).map((b) => ({ binId: b.id, code: b.code, zoneId: 1, zoneCode: 'A', aisle: b.aisle, isActive: true, products: PRODUCTS[b.id as number] ?? [] }))
    return { total: items.length, skip: 0, take: 200, generatedAtUtc: GENERATED, items }
  }
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  return new Response(JSON.stringify({ title: 'Sin acceso', code: 'forbidden' }), { status: 403 })
}

function wrap(permissions = ['inventory.view']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[`/warehouse/locations?warehouse=${WH}`]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/locations" element={<LocationsScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const binCalls = () => mock.calls.filter((c) => c.url.pathname === `/api/v1/warehouses/${WH}/bins`)
const readCalls = () => mock.calls.filter((c) => c.url.pathname.endsWith('/bin-products'))

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.bins = initialBins()
  vi.mocked(downloadBinSheetsPdf).mockClear()
})

describe('Ubicaciones · productos por posición', () => {
  it('ya no hay columna ni filtro "Hoja" ni aviso de hojas desactualizadas', async () => {
    wrap()
    await screen.findByText('A-01')
    expect(screen.queryByRole('columnheader', { name: /Hoja/ })).toBeNull()
    expect(screen.queryByLabelText('Hoja')).toBeNull()
    expect(screen.queryByText(/desactualizada/i)).toBeNull()
    expect(screen.queryByRole('button', { name: 'Hojas de posición' })).toBeNull()
  })

  it('filtro "Pasillo": va al servidor como `aisle` y filtra la tabla; Limpiar lo quita', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-01')
    await user.type(screen.getByLabelText('Pasillo'), 'B')
    await waitFor(() => expect(screen.queryByText('A-01')).toBeNull())
    expect(screen.getByText('B-03')).toBeInTheDocument()
    expect(binCalls().at(-1)?.url.searchParams.get('aisle')).toBe('B')
    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    expect(await screen.findByText('A-01')).toBeInTheDocument()
    expect(binCalls().at(-1)?.url.searchParams.get('aisle')).toBeNull()
  })

  it('casillas: marcar una, toda la página, contador y quitar; con marcas el modal abre en "marcadas"', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-01')
    expect(screen.getByText('0 marcadas')).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición B-03' }))
    expect(screen.getByText('1 marcada')).toBeInTheDocument()
    const page = screen.getByRole('checkbox', { name: 'Seleccionar todas las de la página' }) as HTMLInputElement
    expect(page.indeterminate).toBe(true)
    await user.click(page)
    expect(screen.getByText('4 marcadas')).toBeInTheDocument()
    await user.click(page)
    expect(screen.getByText('0 marcadas')).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-01' }))
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición B-04' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Productos por posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Productos por posición' })
    expect(within(dialog).getByLabelText('Las posiciones marcadas (2)')).toBeChecked()
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    await user.click(screen.getByRole('button', { name: 'Quitar marcas' }))
    expect(screen.getByText('0 marcadas')).toBeInTheDocument()
  })

  it('imprimir las marcadas: lee bin-products por ids y descarga el PDF con cada posición y sus productos', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-01')
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-01' }))
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición B-03' }))
    await user.click(screen.getByRole('button', { name: 'Productos por posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Productos por posición' })
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    await waitFor(() => expect(downloadBinSheetsPdf).toHaveBeenCalledTimes(1))
    const read = readCalls()[0]
    expect(read.url.searchParams.getAll('binIds')).toEqual(['10', '12'])
    expect(read.url.searchParams.get('take')).toBe('200')
    const spec = vi.mocked(downloadBinSheetsPdf).mock.calls[0][0]
    expect(spec.bins.map((b) => [b.code, b.products.map((p) => p.sku)])).toEqual([
      ['A-01', ['TORN-01']],
      ['B-03', ['TUER-01']],
    ])
    expect(spec.warehouse).toBe('ALM-01 · Almacén principal')
    expect(await screen.findByText(/Se generaron 2 página\(s\) de 2 posición\(es\)/)).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Productos por posición' })).toBeNull()
  })

  it('con el filtro Pasillo, el alcance "filtro actual" lleva el pasillo a bin-products', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-01')
    await user.type(screen.getByLabelText('Pasillo'), 'B')
    await waitFor(() => expect(screen.queryByText('A-01')).toBeNull())
    await user.click(screen.getByRole('button', { name: 'Productos por posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Productos por posición' })
    expect(within(dialog).getByLabelText('Las posiciones del filtro actual (2)')).toBeChecked()
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    await waitFor(() => expect(downloadBinSheetsPdf).toHaveBeenCalledTimes(1))
    expect(readCalls()[0].url.searchParams.get('aisle')).toBe('B')
    expect(vi.mocked(downloadBinSheetsPdf).mock.calls[0][0].bins.map((b) => b.code)).toEqual(['B-03', 'B-04'])
  })

  it('si el PDF falla el modal lo dice; sin productos (vacías omitidas) avisa sin generar', async () => {
    const user = userEvent.setup()
    vi.mocked(downloadBinSheetsPdf).mockRejectedValueOnce(new Error('jsPDF'))
    wrap()
    await screen.findByText('A-01')
    await user.click(screen.getByRole('button', { name: 'Productos por posición' }))
    let dialog = await screen.findByRole('dialog', { name: 'Productos por posición' })
    expect(within(dialog).getByLabelText('Las posiciones del filtro actual (4)')).toBeChecked()
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('No se pudo generar el PDF de productos por posición.')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))

    // solo la vacía marcada: sin "Incluir posiciones vacías" no hay nada que imprimir
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-02' }))
    await user.click(screen.getByRole('button', { name: 'Productos por posición' }))
    dialog = await screen.findByRole('dialog', { name: 'Productos por posición' })
    vi.mocked(downloadBinSheetsPdf).mockClear()
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('No hay nada para imprimir')
    expect(downloadBinSheetsPdf).not.toHaveBeenCalled()
    // con el interruptor sí: página "Sin productos"
    await user.click(within(dialog).getByRole('switch', { name: 'Incluir posiciones vacías' }))
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    await waitFor(() => expect(downloadBinSheetsPdf).toHaveBeenCalledTimes(1))
    expect(vi.mocked(downloadBinSheetsPdf).mock.calls[0][0].includeEmpty).toBe(true)
  })

  it('sin inventory.view no se pinta "Productos por posición"', async () => {
    wrap([])
    await screen.findByText('A-01')
    expect(screen.queryByRole('button', { name: 'Productos por posición' })).toBeNull()
  })
})
