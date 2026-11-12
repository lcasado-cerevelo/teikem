// Lote 242 — «Despachar» en la ficha de un documento de «Recolección y empaque»: una orden de venta (o un aviso de salida) con algo reservado por sacar se despacha desde ahí, con el MISMO
// despacho de Ventas → la orden → «Despachar» (consume la reserva, baja el inventario y crea la recolección enlazada) y la misma pregunta de después. Un envío empacado o un documento sin
// nada reservado no lo ofrecen, y sin warehouse.pick tampoco. «Recolectar» (formulario manual) sigue aparte y una nota dice la diferencia.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import type { PickDocumentDto } from './api'
import { PickDocumentModal } from './PickDocumentModal'

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
    let body: unknown = null
    try {
      body = await req.clone().json()
    } catch {
      body = null
    }
    mock.calls.push({ method: req.method, url, body })
    const [status, out] = route(req.method, url)
    return new Response(JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const SO = 'cccccccc-0000-0000-0000-000000000001'
const ORDER = {
  publicId: SO,
  number: 'OV-00007',
  status: 'REQUESTED',
  statusLabel: 'Solicitada',
  clientPublicId: 'dddddddd-0000-0000-0000-000000000001',
  clientName: 'Farmacia Demo',
  lines: [
    { id: 11, lineNo: 1, productPublicId: 'eeeeeeee-0000-0000-0000-000000000001', sku: '52800', productName: 'PRODIGY STRIPS 1/50CT.', qtyOrdered: 6, qtyReserved: 6, qtyShipped: 0, qtyBackordered: 0 },
    { id: 12, lineNo: 2, productPublicId: 'eeeeeeee-0000-0000-0000-000000000002', sku: '070120', productName: 'PRODIGY MONITOR 1EA.', qtyOrdered: 1, qtyReserved: 1, qtyShipped: 0, qtyBackordered: 0 },
  ],
}

function doc(patch: Partial<PickDocumentDto> = {}): PickDocumentDto {
  return {
    kind: 'SALES_ORDER',
    publicId: SO,
    number: 'OV-00007',
    clientName: 'Farmacia Demo',
    statusCode: 'REQUESTED',
    status: 'Solicitada',
    ordered: 7,
    reserved: 7,
    shipped: 0,
    batchCount: 0,
    batches: [],
    warehousePublicId: 'ffffffff-0000-0000-0000-000000000001',
    lines: [
      { productPublicId: 'eeeeeeee-0000-0000-0000-000000000001', sku: '52800', productName: 'PRODIGY STRIPS 1/50CT.', ordered: 6, reserved: 6, shipped: 0, trackingTypeCode: 'NONE', locations: [{ binId: 1, binCode: 'PISO', quantity: 6 }] },
    ],
    ...patch,
  } as PickDocumentDto
}

function route(method: string, url: URL): [number, unknown] {
  const p = url.pathname
  if (method === 'GET' && p === `/api/v1/sales-orders/${SO}`) return [200, ORDER]
  if (method === 'POST' && p === `/api/v1/sales-orders/${SO}/dispatch`) return [200, { pickBatchPublicId: 'bbbbbbbb-0000-0000-0000-000000000009', pickBatchNumber: 'EMP-00009', order: { ...ORDER, status: 'SHIPPED' } }]
  return [200, []]
}

function show(d: PickDocumentDto, permissions = ['inventory.view', 'warehouse.pick', 'sales.view'], onClose = vi.fn(), onCollect?: () => void) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL', 'SALES_ORDERS']}>
          <PickDocumentModal document={d} onClose={onClose} onCollect={onCollect} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
  return onClose
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

describe('«Despachar» en la ficha del documento', () => {
  it('una orden de venta con algo reservado ofrece «Despachar» y la nota que lo distingue de «Recolectar»', async () => {
    show(doc(), undefined, undefined, () => {})
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Despachar' })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Recolectar' })).toBeInTheDocument()
    expect(within(dialog).getByText(/«Despachar» saca esto contra la orden/)).toBeInTheDocument()
  })

  it('no lo ofrece sin warehouse.pick, ni en un envío empacado, ni sin nada reservado', async () => {
    show(doc(), ['inventory.view'])
    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Despachar' })).toBeNull()
  })

  it('un envío empacado o un documento sin nada reservado no se despacha desde aquí', async () => {
    show(doc({ kind: 'ORDER', ordered: null, reserved: null, lines: [] }))
    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Despachar' })).toBeNull()
  })

  it('despacha la orden: carga la orden, pide cuánto sale, la POST y pregunta «¿Qué sigue?»; el retiro termina y cierra la ficha', async () => {
    const user = userEvent.setup()
    const onClose = show(doc())
    await user.click(await screen.findByRole('button', { name: 'Despachar' }))
    // la ventana de despacho de Ventas, con lo reservado por línea
    const dispatch = await screen.findByRole('dialog', { name: /Despachar la orden OV-00007/ })
    expect(within(dispatch).getByText('PRODIGY STRIPS 1/50CT.')).toBeInTheDocument()
    expect(within(dispatch).getByLabelText('Cantidad a despachar de 52800')).toHaveValue('6')
    await user.click(within(dispatch).getByRole('button', { name: 'Despachar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname === `/api/v1/sales-orders/${SO}/dispatch`)).toBe(true))
    const post = mock.calls.find((c) => c.method === 'POST')!
    expect(post.body).toMatchObject({ lines: [{ lineId: 11, quantity: 6 }, { lineId: 12, quantity: 1 }] })
    // la misma pregunta de después de Ventas
    const next = await screen.findByRole('dialog', { name: /Despacho EMP-00009/ })
    expect(within(next).getByText('¿Qué sigue con lo despachado?')).toBeInTheDocument()
    await user.click(within(next).getByRole('button', { name: /Retiro: el cliente lo recoge/ }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('cancelar la ventana de despacho no cierra la ficha ni despacha nada', async () => {
    const user = userEvent.setup()
    const onClose = show(doc())
    await user.click(await screen.findByRole('button', { name: 'Despachar' }))
    const dispatch = await screen.findByRole('dialog', { name: /Despachar la orden OV-00007/ })
    await user.click(within(dispatch).getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: /Despachar la orden/ })).toBeNull())
    expect(onClose).not.toHaveBeenCalled()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)
  })
})
