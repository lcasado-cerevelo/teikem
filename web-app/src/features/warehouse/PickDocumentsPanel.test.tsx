// Lote 173 (C1) — «Recolección y empaque» por documento sobre un fetch simulado: por omisión se ve la lista de documentos con su avance; los filtros viajan al API; abrir un documento
// lista sus recolecciones como movimientos y tocar una abre la ficha de la recolección; «Por movimiento» lleva a la lista de recolecciones de siempre.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import PickBatchListScreen from './PickBatchListScreen'

interface Call {
  method: string
  url: URL
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.calls.push({ method: req.method, url })
    const [status, out] = route(url)
    return new Response(JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const B1 = 'bbbbbbbb-0000-0000-0000-000000000001'
const B2 = 'bbbbbbbb-0000-0000-0000-000000000002'
const NOTICE = {
  kind: 'EXIT_NOTICE',
  publicId: 'aaaaaaaa-0000-0000-0000-000000000001',
  number: 'SAL-00001',
  clientName: 'ASICS Care',
  statusCode: 'PARTIAL',
  status: 'Parcial',
  ordered: 10,
  reserved: 4,
  shipped: 5,
  batchCount: 2,
  lastCollectedAtUtc: '2026-10-20T15:00:00Z',
  batches: [
    { publicId: B2, number: 'EMP-00002', statusCode: 'COLLECTED', status: 'Recolectada', collectedAtUtc: '2026-10-20T15:00:00Z', collectedBy: 'Ana Ruiz', totalQty: 3, groupNumber: 'RMA-00001' },
    { publicId: B1, number: 'EMP-00001', statusCode: 'COLLECTED', status: 'Recolectada', collectedAtUtc: '2026-10-20T13:00:00Z', collectedBy: 'Luis Pérez', totalQty: 2 },
  ],
}
const SHIPMENT = { kind: 'ORDER', publicId: 'aaaaaaaa-0000-0000-0000-000000000002', number: 'ORD-9', clientName: 'Otro', statusCode: 'SUBMITTED', status: 'Sometido', ordered: null, reserved: null, shipped: 7, batchCount: 1, lastCollectedAtUtc: '2026-10-19T10:00:00Z', batches: [] }

function route(url: URL): [number, unknown] {
  const p = url.pathname
  if (p === '/api/v1/pick-batches/documents') {
    const kind = url.searchParams.get('kind')
    const items = kind === 'ORDER' ? [SHIPMENT] : [NOTICE, SHIPMENT]
    return [200, { total: items.length, skip: 0, take: 25, items }]
  }
  if (p === '/api/v1/pick-batches') return [200, { total: 0, skip: 0, take: 25, items: [] }]
  if (p === `/api/v1/pick-batches/${B2}`)
    return [200, { id: 2, publicId: B2, number: 'EMP-00002', warehouseCode: 'ALM-01', statusCode: 'COLLECTED', status: 'Recolectada', isActive: true, canPack: false, canDelete: false, totalQty: 3, lines: [], collectedAtUtc: '2026-10-20T15:00:00Z', rowVersion: 'AA==' }]
  if (p.startsWith('/api/v1/status/') || p.startsWith('/api/v1/catalogs/')) return [200, []]
  if (p === '/api/v1/warehouses') return [200, []]
  return [200, []]
}

function wrap(path = '/warehouse/pick-batches', permissions = ['inventory.view']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/pick-batches" element={<PickBatchListScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const docGets = () => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === '/api/v1/pick-batches/documents')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  window.localStorage.clear()
})

describe('Recolección y empaque por documento', () => {
  it('por omisión lista los documentos con su avance y sus movimientos', async () => {
    wrap()
    expect(await screen.findByText('SAL-00001')).toBeInTheDocument()
    expect(screen.getAllByText('Aviso de salida').length).toBeGreaterThan(1)   // la opción del filtro Tipo y la etiqueta de la fila
    expect(screen.getByText('Sacado 5 de 10')).toBeInTheDocument()
    expect(screen.getByText('Por sacar 4')).toBeInTheDocument()
    expect(screen.getByText('Sin reservar 1')).toBeInTheDocument()
    expect(screen.getByText('Sacado 7')).toBeInTheDocument()   // un envío no tiene pedido
    expect(screen.getByText('ORD-9')).toBeInTheDocument()
  })

  it('los filtros viajan al API: tipo, solo con pendiente y el buscador', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('SAL-00001')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Tipo' }), 'ORDER')
    await waitFor(() => expect(docGets().at(-1)!.url.searchParams.get('kind')).toBe('ORDER'))
    await user.click(screen.getByLabelText('Solo con pendiente por sacar'))
    await waitFor(() => expect(docGets().at(-1)!.url.searchParams.get('pendingOnly')).toBe('true'))
    await user.type(screen.getByRole('searchbox'), 'SAL-0')
    await waitFor(() => expect(docGets().at(-1)!.url.searchParams.get('search')).toBe('SAL-0'))
  })

  it('abrir un documento lista sus recolecciones y tocar una abre su ficha', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('SAL-00001'))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Recolecciones (2)')).toBeInTheDocument()
    const rows = within(dialog).getAllByRole('row').slice(1)
    expect(rows[0]).toHaveTextContent('EMP-00002')   // la más reciente primero
    expect(rows[0]).toHaveTextContent('Ana Ruiz')
    expect(rows[0]).toHaveTextContent('Maestra RMA-00001')
    expect(rows[1]).not.toHaveTextContent('Maestra')
    await user.click(rows[0])
    await waitFor(() => expect(mock.calls.some((c) => c.url.pathname === `/api/v1/pick-batches/${B2}`)).toBe(true))
  })

  it('«Por movimiento» lleva a la lista de recolecciones de siempre y la vista va en la URL', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('SAL-00001')
    await user.click(screen.getByRole('tab', { name: 'Por movimiento' }))
    await waitFor(() => expect(mock.calls.some((c) => c.url.pathname === '/api/v1/pick-batches')).toBe(true))
    expect(screen.queryByText('SAL-00001')).toBeNull()
    await user.click(screen.getByRole('tab', { name: 'Por documento' }))
    expect(await screen.findByText('SAL-00001')).toBeInTheDocument()
  })
})
