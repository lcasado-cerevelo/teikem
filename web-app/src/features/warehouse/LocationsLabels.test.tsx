// Lote F16 — etiquetas de posición en Ubicaciones, sobre un API simulado: el botón (con inventory.view, junto a "Productos
// por posición"), el modal (qué imprimir, tamaño con su equivalente en cm, orientación), la impresión del filtro actual (con el
// buscador de la tabla, lectura de a 200) y de las marcadas (binIds), el tamaño recordado, el tope de 500, los avisos de
// las que salen sin código de barras y que NUNCA se llama a nada que no sea una lectura (GET).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { downloadBinLabelsPdf } from '../../kernel/ui/binLabelPdf'
import LocationsScreen from './LocationsScreen'

interface Call {
  method: string
  url: URL
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], bins: [] as Record<string, unknown>[], total: null as number | null }))

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

vi.mock('../../kernel/ui/binLabelPdf', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/ui/binLabelPdf')>()
  return { ...actual, downloadBinLabelsPdf: vi.fn(async (spec: Parameters<typeof actual.downloadBinLabelsPdf>[0]) => actual.planBinLabels(spec.bins, spec.size, spec.orientation)) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const ZONES = [{ id: 1, code: 'A', name: 'Zona A', zoneTypeCode: 'STORAGE', isActive: true, binCount: 4, capacityQty: 0, qtyOnHand: 0, binsWithoutCapacity: 4 }]
const initialBins = () => [
  { id: 10, code: 'A-10', zoneId: 1, zoneCode: 'A', aisle: '01', rack: '02', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
  { id: 11, code: 'A-2', zoneId: 1, zoneCode: 'A', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
  { id: 12, code: 'B-01', zoneId: 1, zoneCode: 'A', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
  { id: 13, code: 'A-1', zoneId: 1, zoneCode: 'A', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
]

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/zones`) return ZONES
  if (p === `/api/v1/warehouses/${WH}/bins`) {
    const ids = url.searchParams.getAll('binIds').map(Number)
    const search = (url.searchParams.get('search') ?? '').toLowerCase()
    const items = mock.bins.filter((b) => (ids.length === 0 || ids.includes(b.id as number)) && String(b.code).toLowerCase().includes(search))
    const skip = Number(url.searchParams.get('skip') ?? 0)
    const take = Number(url.searchParams.get('take') ?? 100)
    return { total: mock.total ?? items.length, skip, take, items: items.slice(skip, skip + take) }
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

/** Lecturas del flujo de etiquetas (take=200; la tabla pide 25). */
const labelReads = () => mock.calls.filter((c) => c.url.pathname === `/api/v1/warehouses/${WH}/bins` && c.url.searchParams.get('take') === '200')
const sheetCalls = () => mock.calls.filter((c) => c.url.pathname.includes('bin-products') || c.method !== 'GET')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.bins = initialBins()
  mock.total = null
  localStorage.clear()
  vi.mocked(downloadBinLabelsPdf).mockClear()
})

describe('Ubicaciones · etiquetas de posición (Lote F16)', () => {
  it('el botón abre el modal: filtro actual, marcadas, los tres tamaños con cm y la orientación (4×2 y automática por defecto)', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-10')
    const actions = screen.getByRole('button', { name: 'Productos por posición' }).parentElement as HTMLElement
    await user.click(within(actions).getByRole('button', { name: 'Etiquetas de posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Etiquetas de posición' })
    expect(within(dialog).getByLabelText('Las posiciones del filtro actual (4)')).toBeChecked()
    expect(within(dialog).getByLabelText('Las posiciones marcadas (0)')).toBeDisabled()
    expect(within(dialog).getByRole('radio', { name: /4 × 2 pulgadas \(10 × 5 cm\)/ })).toBeChecked()
    expect(within(dialog).getByRole('radio', { name: /4 × 4 pulgadas \(10 × 10 cm\)/ })).not.toBeChecked()
    expect(within(dialog).getByRole('radio', { name: /4 × 6 pulgadas \(10 × 15 cm\)/ })).not.toBeChecked()
    expect(within(dialog).getByText('Apaisada: 4 de ancho × 2 de alto.')).toBeInTheDocument()
    expect(within(dialog).getByRole('radio', { name: 'Automática (recomendada)' })).toBeChecked()
    expect(within(dialog).getByRole('radio', { name: 'Girar 90°' })).not.toBeChecked()
    expect(within(dialog).getByRole('button', { name: 'Generar PDF' })).toBeEnabled()
  })

  it('filtro actual (con el buscador): lee de a 200 con los filtros, ordena natural, descarga 4×6 girada; no marca nada', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-10')
    await user.type(screen.getByLabelText('Posición'), 'a-')
    await waitFor(() => expect(screen.queryByText('B-01')).toBeNull())
    await user.click(screen.getByRole('button', { name: 'Etiquetas de posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Etiquetas de posición' })
    expect(within(dialog).getByLabelText('Las posiciones del filtro actual (3)')).toBeChecked()
    await user.click(within(dialog).getByRole('radio', { name: /4 × 6 pulgadas/ }))
    await user.click(within(dialog).getByRole('radio', { name: 'Girar 90°' }))
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Etiquetas de posición' })).toBeNull())
    const reads = labelReads()
    expect(reads).toHaveLength(1)
    expect(reads[0].url.searchParams.get('search')).toBe('a-')
    expect(reads[0].url.searchParams.get('skip')).toBe('0')
    expect(reads[0].url.searchParams.get('includeInactive')).toBe('false')
    const spec = vi.mocked(downloadBinLabelsPdf).mock.calls[0][0]
    expect(spec).toMatchObject({ size: '4x6', orientation: 'rotate', title: 'Etiquetas de posición' })
    expect(spec.bins.map((b) => b.code)).toEqual(['A-1', 'A-2', 'A-10'])
    expect(spec.bins[2].details).toEqual([
      { label: 'Almacén', value: 'ALM-01' },
      { label: 'Zona', value: 'A' },
      { label: 'Pasillo', value: '01' },
      { label: 'Rack', value: '02' },
    ])
    // ni escrituras ni el informe de productos: las etiquetas no tienen estado
    expect(sheetCalls()).toEqual([])
    // el tamaño y la orientación quedan recordados para reimprimir
    await user.click(screen.getByRole('button', { name: 'Etiquetas de posición' }))
    const again = await screen.findByRole('dialog', { name: 'Etiquetas de posición' })
    expect(within(again).getByRole('radio', { name: /4 × 6 pulgadas/ })).toBeChecked()
    expect(within(again).getByRole('radio', { name: 'Girar 90°' })).toBeChecked()
  })

  it('marcadas: el modal abre en "marcadas", lee por binIds y las marcas se quedan (se puede reimprimir)', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('A-10')
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición B-01' }))
    await user.click(screen.getByRole('checkbox', { name: 'Marcar la posición A-10' }))
    await user.click(screen.getByRole('button', { name: 'Etiquetas de posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Etiquetas de posición' })
    expect(within(dialog).getByLabelText('Las posiciones marcadas (2)')).toBeChecked()
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    await waitFor(() => expect(downloadBinLabelsPdf).toHaveBeenCalledTimes(1))
    expect(labelReads()[0].url.searchParams.getAll('binIds')).toEqual(['10', '12'])
    expect(vi.mocked(downloadBinLabelsPdf).mock.calls[0][0].bins.map((b) => b.code)).toEqual(['A-10', 'B-01'])
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(screen.getByText('2 marcadas')).toBeInTheDocument()
    expect(sheetCalls()).toEqual([])
  })

  it('más de 500 en el filtro: avisa que el PDF es grande pero NO impide generar', async () => {
    const user = userEvent.setup()
    mock.total = 501
    wrap()
    await screen.findByText('A-10')
    await user.click(screen.getByRole('button', { name: 'Etiquetas de posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Etiquetas de posición' })
    expect(within(dialog).getByText(/Son 501: el PDF será grande/)).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Generar PDF' })).toBeEnabled()
  })

  it('una posición sin código de barras posible: el PDF sale y el modal se queda con la lista; "Listo"', async () => {
    const user = userEvent.setup()
    mock.bins = [{ ...initialBins()[0], id: 20, code: 'AÑO-1' }, ...initialBins()]
    wrap()
    await screen.findByText('AÑO-1')
    await user.click(screen.getByRole('button', { name: 'Etiquetas de posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Etiquetas de posición' })
    await user.click(within(dialog).getByRole('button', { name: 'Generar PDF' }))
    const status = await within(dialog).findByText(/Se generaron 5 etiquetas de 4 × 2 pulgadas\. 1 salió sin código de barras/)
    expect(status.parentElement).toHaveTextContent('Omitidos porque tienen caracteres que el código de barras (Code 128) no admite (1): AÑO-1')
    await user.click(within(dialog).getByRole('button', { name: 'Listo' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('sin inventory.view no se pinta "Etiquetas de posición"', async () => {
    wrap([])
    await screen.findByText('A-10')
    expect(screen.queryByRole('button', { name: 'Etiquetas de posición' })).toBeNull()
  })
})
