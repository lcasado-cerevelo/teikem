// Lote 177 — recolección MAESTRA sobre un fetch simulado: «Recolectar juntos» elige documentos (los de otro cliente se bloquean) y manda la solicitud; la vista «Maestras» lista las maestras y
// su ficha muestra los documentos y todos los movimientos agrupados por documento.
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
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.text()
    mock.calls.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    const [status, out] = route(req.method, url)
    return new Response(JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const G = 'cccccccc-0000-0000-0000-000000000001'
const doc = (n: number, client: string, kind = 'EXIT_NOTICE') => ({
  kind, publicId: `aaaaaaaa-0000-0000-0000-00000000000${n}`, number: kind === 'EXIT_NOTICE' ? `SAL-0000${n}` : `OV-0000${n}`, clientName: client, statusCode: 'REQUESTED', status: 'Solicitado',
  ordered: 10, reserved: 4, shipped: 0, batchCount: 0, batches: [],
})
const GROUP = {
  id: 1, publicId: G, number: 'EMP-00009', warehouseCode: 'ALM-01', clientName: 'ASICS', statusCode: 'COLLECTED', collectedAtUtc: '2026-10-21T10:00:00Z', collectedBy: 'Ana Ruiz', totalQty: 9,
  orderNumber: null, note: null, canPack: true, canDelete: true, isActive: true, rowVersion: 'AA==',
  documents: [
    { kind: 'EXIT_NOTICE', publicId: 'a1', number: 'SAL-00001', batchPublicId: 'b1', batchNumber: 'EMP-00007', quantity: 4 },
    { kind: 'SALES_ORDER', publicId: 'a2', number: 'OV-00002', batchPublicId: 'b2', batchNumber: 'EMP-00008', quantity: 5 },
  ],
  movements: [
    { documentKind: 'EXIT_NOTICE', documentNumber: 'SAL-00001', batchNumber: 'EMP-00007', sku: 'MED', productName: 'Medias', quantity: 4, binCode: 'P-01', lotNumber: null },
    { documentKind: 'SALES_ORDER', documentNumber: 'OV-00002', batchNumber: 'EMP-00008', sku: 'MED', productName: 'Medias', quantity: 2, binCode: 'P-01', lotNumber: 'L1' },
    { documentKind: 'SALES_ORDER', documentNumber: 'OV-00002', batchNumber: 'EMP-00008', sku: 'MED', productName: 'Medias', quantity: 3, binCode: 'P-02', lotNumber: 'L1' },
  ],
}

function route(method: string, url: URL): [number, unknown] {
  const p = url.pathname
  if (p === '/api/v1/pick-batches/documents') {
    const items = [doc(1, 'ASICS'), doc(2, 'ASICS', 'SALES_ORDER'), doc(3, 'NIKE')]
    return [200, { total: items.length, skip: 0, take: 200, items }]
  }
  if (method === 'POST' && p === '/api/v1/pick-batches/groups') return [200, GROUP]
  if (p === '/api/v1/pick-batches/groups') return [200, { total: 1, skip: 0, take: 25, items: [{ ...GROUP, movements: [] }] }]
  if (p === `/api/v1/pick-batches/groups/${G}`) return [200, GROUP]
  if (p === '/api/v1/pick-batches') return [200, { total: 0, skip: 0, take: 25, items: [] }]
  return [200, []]
}

function wrap(path = '/warehouse/pick-batches') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.view', 'warehouse.pick', 'orders.create']} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/pick-batches" element={<PickBatchListScreen />} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  window.localStorage.clear()
})

describe('Recolección maestra', () => {
  it('«Recolectar juntos» bloquea los documentos de otro cliente y manda los elegidos', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Recolectar juntos' }))
    const dialog = await screen.findByRole('dialog')
    const boxes = await within(dialog).findAllByRole('checkbox')
    expect(boxes).toHaveLength(3)
    const submit = within(dialog).getByRole('button', { name: /Recolectar juntos \(0\)/ })
    expect(submit).toBeDisabled()
    await user.click(boxes[0])
    expect(boxes[2]).toBeDisabled()   // NIKE: otro cliente
    expect(within(dialog).getByRole('button', { name: /Recolectar juntos \(1\)/ })).toBeDisabled()   // hacen falta dos
    await user.click(boxes[1])
    const go = within(dialog).getByRole('button', { name: /Recolectar juntos \(2\)/ })
    expect(go).toBeEnabled()
    await user.click(go)
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname === '/api/v1/pick-batches/groups')).toBe(true))
    const post = mock.calls.find((c) => c.method === 'POST')!
    expect(post.body).toMatchObject({ documents: [{ kind: 'EXIT_NOTICE', publicId: doc(1, 'x').publicId }, { kind: 'SALES_ORDER', publicId: doc(2, 'x', 'SALES_ORDER').publicId }] })
    // la maestra recién creada se abre en la vista «Maestras»
    expect(await screen.findByText('Recolección maestra')).toBeInTheDocument()
  })

  it('la vista «Maestras» lista las maestras y su ficha agrupa los movimientos por documento', async () => {
    const user = userEvent.setup()
    wrap('/warehouse/pick-batches?view=groups')
    await user.click(await screen.findByText('EMP-00009'))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Documentos (2)')).toBeInTheDocument()
    expect(within(dialog).getAllByText('SAL-00001').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('P-02').length).toBe(1)   // la posición de donde salió también la segunda parte
    expect(within(dialog).getByRole('button', { name: 'Empacar' })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Eliminar' })).toBeInTheDocument()
  })
})
