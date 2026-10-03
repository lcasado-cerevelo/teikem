// Lote F15 — hojas de posición en Ubicaciones, sobre un API simulado: insignia "Hoja" con texto y última impresión,
// filtro "Hoja" (sheetStatus al servidor), casillas (marcar, toda la página, contador, quitar), aviso acumulado con
// staleCount ("Imprimir las desactualizadas" abre el modal con ese alcance) e impresión: descarga el PDF y SOLO después
// marca impresas con el generatedAtUtc del servidor; la lista se refresca ("Al día"). Si el PDF falla no se marca nada.
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
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], bins: [] as Record<string, unknown>[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const body = req.method === 'POST' ? await req.json() : null
    mock.calls.push({ method: req.method, url, body })
    const res = route(req.method, url, body)
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
  { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', qtyOnHand: 5, productCount: 1, singleProductSku: 'TORN-01', singleProductName: 'Tornillo', occupancy: 'NO_CAPACITY', isActive: true, sheetStatus: 'NEVER_PRINTED', sheetContentChangedAtUtc: '2026-10-02T12:00:00' },
  { id: 11, code: 'A-02', zoneId: 1, zoneCode: 'A', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true, sheetStatus: 'EMPTY' },
  { id: 12, code: 'A-03', zoneId: 1, zoneCode: 'A', qtyOnHand: 2, productCount: 1, singleProductSku: 'TUER-01', singleProductName: 'Tuerca', occupancy: 'NO_CAPACITY', isActive: true, sheetStatus: 'STALE', sheetPrintedAtUtc: '2026-10-01T13:30:00', sheetContentChangedAtUtc: '2026-10-02T15:00:00' },
  { id: 13, code: 'A-04', zoneId: 1, zoneCode: 'A', qtyOnHand: 2, productCount: 1, singleProductSku: 'ARAN-01', singleProductName: 'Arandela', occupancy: 'NO_CAPACITY', isActive: true, sheetStatus: 'CURRENT', sheetPrintedAtUtc: '2026-10-02T18:00:00' },
]
const PRODUCTS: Record<number, { sku: string; name: string; barcode: string }[]> = {
  10: [{ sku: 'TORN-01', name: 'Tornillo', barcode: '7501234567890' }],
  12: [{ sku: 'TUER-01', name: 'Tuerca', barcode: '' }],
  13: [{ sku: 'ARAN-01', name: 'Arandela', barcode: '' }],
}

function matching(url: URL) {
  const statuses = url.searchParams.getAll('sheetStatus')
  const ids = url.searchParams.getAll('binIds').map(Number)
  return mock.bins.filter((b) => (statuses.length === 0 || statuses.includes(b.sheetStatus as string)) && (ids.length === 0 || ids.includes(b.id as number)))
}

function route(method: string, url: URL, body: unknown): unknown {
  const p = url.pathname
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/zones`) return ZONES
  if (p === `/api/v1/warehouses/${WH}/bins`) {
    const items = matching(url)
    const staleCount = items.filter((b) => b.sheetStatus === 'STALE' || b.sheetStatus === 'NEVER_PRINTED').length
    return { total: items.length, skip: 0, take: Number(url.searchParams.get('take') ?? 100), staleCount, items }
  }
  if (p === `/api/v1/warehouses/${WH}/bin-sheets`) {
    const items = matching(url).map((b) => ({ binId: b.id, code: b.code, zoneId: 1, zoneCode: 'A', isActive: true, sheetStatus: b.sheetStatus, products: PRODUCTS[b.id as number] ?? [] }))
    return { total: items.length, skip: 0, take: 200, staleCount: 0, generatedAtUtc: GENERATED, items }
  }
  if (p === `/api/v1/warehouses/${WH}/bin-sheets/mark-printed` && method === 'POST') {
    const ids = (body as { binIds: number[] }).binIds
    for (const b of mock.bins) if (ids.includes(b.id as number)) Object.assign(b, { sheetStatus: (b.productCount as number) > 0 ? 'CURRENT' : 'EMPTY', sheetPrintedAtUtc: GENERATED })
    return ids.map((binId) => ({ binId, sheetStatus: 'CURRENT' }))
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
const markCalls = () => mock.calls.filter((c) => c.url.pathname.endsWith('/mark-printed'))
const rowOf = (code: string) => screen.getByText(code).closest('tr') as HTMLElement

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.bins = initialBins()
  vi.mocked(downloadBinSheetsPdf).mockClear()
})

describe('Ubicaciones · hojas de posición (Lote F15)', () => {
  it('columna "Hoja": insignia con texto (no solo color), "—" sin productos y la última impresión debajo', async () => {
    wrap()
    await screen.findByText('A-01')
    expect(screen.getByRole('columnheader', { name: /Hoja/ })).toBeInTheDocument()
    expect(within(rowOf('A-01')).getByText('Sin hoja impresa')).toBeInTheDocument()
    expect(within(rowOf('A-03')).getByText('Desactualizada')).toBeInTheDocument()
    expect(within(rowOf('A-04')).getByText('Al día')).toBeInTheDocument()
    expect(within(rowOf('A-02')).getByLabelText('Sin productos: no necesita hoja')).toHaveTextContent('—')
    // 13:30 UTC = 9:30 a. m. en Puerto Rico (formatos de la compañía)
    expect(within(rowOf('A-03')).getByText(/^Impresa 10\/01\/2026 9:30\sa\.\sm\.$/)).toBeInTheDocument()
    expect(within(rowOf('A-03')).getByText('Desactualizada').closest('.loc-sheet')).toHaveAttribute('title', expect.stringMatching(/^Última impresión: .* · Último cambio de productos: /))
    expect(within(rowOf('A-01')).queryByText(/^Impresa/)).toBeNull()
  })

  it('aviso acumulado con staleCount y "Imprimir las desactualizadas" abre el modal con ese alcance', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByText('posiciones con la hoja desactualizada o sin imprimir')).toBeInTheDocument()
    expect(document.querySelector('.loc-stale')).toHaveTextContent(/^2 posiciones con la hoja desactualizada o sin imprimir/)
    await user.click(screen.getByRole('button', { name: 'Imprimir las desactualizadas' }))
    const dialog = await screen.findByRole('dialog', { name: 'Hojas de posición' })
    expect(within(dialog).getByLabelText('Solo las desactualizadas o sin hoja (2)')).toBeChecked()
    expect(within(dialog).getByLabelText('Las posiciones del filtro actual (4)')).not.toBeChecked()
    expect(within(dialog).getByLabelText('Las posiciones marcadas (0)')).toBeDisabled()
    expect(within(dialog).getByRole('switch', { name: 'Incluir posiciones vacías' })).not.toBeChecked()
  })

  it('filtro "Hoja": sheetStatus al servidor; el aviso se cuenta aparte sin ese filtro', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-01')
    await user.click(screen.getByLabelText('Hoja'))
    await user.click(await screen.findByLabelText('Desactualizada'))
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByText('A-01')).toBeNull())
    const withStatus = binCalls().filter((c) => c.url.searchParams.getAll('sheetStatus').length > 0)
    expect(withStatus.at(-1)?.url.searchParams.getAll('sheetStatus')).toEqual(['STALE'])
    // el contador sigue contando las 2 de toda la lista (consulta take=1 sin sheetStatus)
    const counter = binCalls().filter((c) => c.url.searchParams.get('take') === '1')
    expect(counter.length).toBeGreaterThan(0)
    expect(counter.at(-1)?.url.searchParams.getAll('sheetStatus')).toEqual([])
    // (el filtro "Hoja" no cuenta como "con los filtros actuales": el aviso es el de toda la lista)
    await waitFor(() => expect(document.querySelector('.loc-stale')).toHaveTextContent(/^2 posiciones con la hoja desactualizada o sin imprimir/))
    expect(document.querySelector('.loc-stale')).not.toHaveTextContent('con los filtros actuales')
  })

  it('casillas: marcar una, toda la página, contador y quitar; con marcas el modal abre en "marcadas"', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-01')
    expect(screen.getByText('0 marcadas')).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-03' }))
    expect(screen.getByText('1 marcada')).toBeInTheDocument()
    const page = screen.getByRole('checkbox', { name: 'Seleccionar todas las de la página' }) as HTMLInputElement
    expect(page.indeterminate).toBe(true)
    await user.click(page)
    expect(screen.getByText('4 marcadas')).toBeInTheDocument()
    expect(page).toBeChecked()
    await user.click(page)
    expect(screen.getByText('0 marcadas')).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-01' }))
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-04' }))
    // marcar no abre la posición (sin warehouse.manage no hay edición, pero la casilla tampoco propaga el clic)
    expect(screen.queryByRole('dialog')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Hojas de posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Hojas de posición' })
    expect(within(dialog).getByLabelText('Las posiciones marcadas (2)')).toBeChecked()
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    await user.click(screen.getByRole('button', { name: 'Quitar marcas' }))
    expect(screen.getByText('0 marcadas')).toBeInTheDocument()
  })

  it('imprimir las marcadas: descarga el PDF, DESPUÉS marca impresas con el generatedAtUtc y la lista queda "Al día"', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-01')
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-01' }))
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-03' }))
    await user.click(screen.getByRole('button', { name: 'Hojas de posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Hojas de posición' })
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    await waitFor(() => expect(markCalls()).toHaveLength(1))
    // lectura por ids (≤ 200) y take=200
    const read = mock.calls.find((c) => c.url.pathname.endsWith('/bin-sheets'))!
    expect(read.url.searchParams.getAll('binIds')).toEqual(['10', '12'])
    expect(read.url.searchParams.get('take')).toBe('200')
    // el PDF se pidió antes de marcar, con las dos posiciones y sus productos
    const spec = vi.mocked(downloadBinSheetsPdf).mock.calls[0][0]
    expect(spec.bins.map((b) => [b.code, b.products.map((p) => p.sku)])).toEqual([
      ['A-01', ['TORN-01']],
      ['A-03', ['TUER-01']],
    ])
    expect(spec.warehouse).toBe('ALM-01 · Almacén principal')
    const readIndex = mock.calls.indexOf(read)
    expect(mock.calls.indexOf(markCalls()[0])).toBeGreaterThan(readIndex)
    expect(markCalls()[0].body).toEqual({ binIds: [10, 12], generatedAtUtc: GENERATED })
    // la lista se vuelve a pedir y las insignias pasan a "Al día"; las marcas se quitan
    await waitFor(() => expect(within(rowOf('A-01')).getByText('Al día')).toBeInTheDocument())
    expect(within(rowOf('A-03')).getByText('Al día')).toBeInTheDocument()
    expect(await screen.findByText(/Se generaron 2 hoja\(s\) de 2 posición\(es\)/)).toBeInTheDocument()
    expect(screen.getByText('0 marcadas')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Hojas de posición' })).toBeNull()
  })

  it('si el PDF falla no se marca nada y el modal lo dice; sin productos (vacías omitidas) avisa sin generar', async () => {
    const user = userEvent.setup()
    vi.mocked(downloadBinSheetsPdf).mockRejectedValueOnce(new Error('jsPDF'))
    wrap()
    await screen.findByText('A-01')
    await user.click(screen.getByRole('button', { name: 'Hojas de posición' }))
    let dialog = await screen.findByRole('dialog', { name: 'Hojas de posición' })
    expect(within(dialog).getByLabelText('Las posiciones del filtro actual (4)')).toBeChecked()
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('No se pudieron generar las hojas de posición; no se marcó ninguna.')
    expect(markCalls()).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))

    // solo la vacía marcada: sin "Incluir posiciones vacías" no hay nada que imprimir
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-02' }))
    await user.click(screen.getByRole('button', { name: 'Hojas de posición' }))
    dialog = await screen.findByRole('dialog', { name: 'Hojas de posición' })
    vi.mocked(downloadBinSheetsPdf).mockClear()
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('No hay hojas para imprimir')
    expect(downloadBinSheetsPdf).not.toHaveBeenCalled()
    // con el interruptor sí: hoja "Sin productos" y se marca (STALE vacía → EMPTY)
    await user.click(within(dialog).getByRole('switch', { name: 'Incluir posiciones vacías' }))
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    await waitFor(() => expect(markCalls()).toHaveLength(1))
    expect(markCalls()[0].body).toEqual({ binIds: [11], generatedAtUtc: GENERATED })
    expect(vi.mocked(downloadBinSheetsPdf).mock.calls[0][0].includeEmpty).toBe(true)
  })

  it('sin inventory.view no se pinta "Hojas de posición" ni "Imprimir las desactualizadas"', async () => {
    wrap([])
    await screen.findByText('A-01')
    expect(screen.queryByRole('button', { name: 'Hojas de posición' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Imprimir las desactualizadas' })).toBeNull()
  })
})
