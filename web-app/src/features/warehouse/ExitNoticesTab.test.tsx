// Lote 144 — pestaña «Avisos de salida»: lista del servidor con filtros que viajan al API, botones solo con exitnotice.create,
// y la ficha con sus acciones. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ExitNoticesTab } from './ExitNoticesTab'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const NOTICE = '88888888-0000-0000-0000-000000000001'
const ITEM = {
  id: 1,
  publicId: NOTICE,
  number: 'SAL-00001',
  clientName: 'Cliente Uno',
  warehouseCode: 'ALM-01',
  reference: 'REF-9',
  statusCode: 'PARTIAL',
  status: 'Parcial',
  hasBackorder: true,
  requestedDate: '2026-10-20',
  lineCount: 1,
  qtyOrdered: 10,
  qtyShipped: 0,
}
const DETAIL = {
  ...ITEM,
  canModify: true,
  lines: [{ id: 5, sku: 'TORN-01', productName: 'Tornillo', qtyOrdered: 10, qtyReserved: 4, qtyBackordered: 6, qtyShipped: 0, isClosed: false }],
  dispatches: [],
}

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/exit-notices') return { total: 1, skip: 0, take: 25, items: [ITEM] }
  if (method === 'GET' && p === `/api/v1/exit-notices/${NOTICE}`) return DETAIL
  if (method === 'POST' && p === '/api/v1/exit-notices/backorders/fulfill') return { unitsReserved: 6, linesReserved: 1, lines: [{}] }
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  if (method === 'POST' && p.endsWith('/dispatch')) return { pickBatchNumber: 'EMP-00007', notice: DETAIL }
  if (method !== 'GET') return DETAIL
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function wrap(permissions: string[], initialOpen: string | null = null) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WAREHOUSE']}>
          <ExitNoticesTab initialOpen={initialOpen} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
const listGets = () => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === '/api/v1/exit-notices')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

describe('Avisos de salida: lista', () => {
  it('muestra el aviso y «Solo con pendiente» viaja como backorderOnly', async () => {
    const user = userEvent.setup()
    wrap(['exitnotice.create'])
    expect(await screen.findByText('SAL-00001')).toBeInTheDocument()
    await user.click(screen.getByLabelText('Solo con pendiente'))
    await waitFor(() => expect(listGets().at(-1)!.url.searchParams.get('backorderOnly')).toBe('true'))
  })

  it('Lote 171: muestra a quién está asignado y «Solo los asignados a mí» viaja como assignedToMe', async () => {
    const user = userEvent.setup()
    wrap(['exitnotice.create'])
    expect(await screen.findByText('SAL-00001')).toBeInTheDocument()
    await user.click(screen.getByLabelText('Solo los asignados a mí'))
    await waitFor(() => expect(listGets().at(-1)!.url.searchParams.get('assignedToMe')).toBe('true'))
  })

  it('los botones de crear y reservar exigen exitnotice.create', async () => {
    wrap([])
    await screen.findByText('SAL-00001')
    expect(screen.queryByRole('button', { name: 'Nuevo aviso de salida' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reservar el pendiente' })).toBeNull()
  })

  it('Lote 190: las acciones están en la tabla, como iconos con su nombre, y Tomar es directa', async () => {
    const user = userEvent.setup()
    wrap(['warehouse.pick', 'warehouse.manage', 'exitnotice.create'])
    await screen.findByText('SAL-00001')
    expect(screen.getAllByRole('button', { name: 'Despachar' }).length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: 'Asignar' }).length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: 'Reservar el pendiente' })).toBeNull()
    await user.click(screen.getAllByRole('button', { name: 'Tomar' })[0])
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname.endsWith('/take'))).toBe(true))
  })
})

// la ficha se abre sobre la lista, que también tiene las acciones por fila: se busca dentro de la ficha
const inDialog = () => within(screen.getAllByRole('dialog')[0])

describe('Avisos de salida: ficha', () => {
  it('Lote 171: Tomar (warehouse.pick) y Asignar (warehouse.manage) solo en un aviso abierto sin asignar', async () => {
    const user = userEvent.setup()
    const { unmount } = wrap(['exitnotice.create'], NOTICE)
    await screen.findByText('Tornillo')
    expect(inDialog().queryByRole('button', { name: 'Tomar' })).toBeNull()
    expect(inDialog().queryByRole('button', { name: 'Asignar' })).toBeNull()
    unmount()

    wrap(['warehouse.pick', 'warehouse.manage'], NOTICE)
    await screen.findByText('Tornillo')
    expect(inDialog().getByRole('button', { name: 'Asignar' })).toBeInTheDocument()
    await user.click(inDialog().getByRole('button', { name: 'Tomar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname === `/api/v1/exit-notices/${NOTICE}/take`)).toBe(true))
  })

  it('abre la ficha con las líneas y la acción de reservar el pendiente', async () => {
    wrap(['exitnotice.create'], NOTICE)
    expect(await screen.findByText('Tornillo')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cerrar con faltante' })).toBeInTheDocument()
  })

  it('Despachar exige warehouse.pick y manda lo reservado por línea', async () => {
    const user = userEvent.setup()
    const { unmount } = wrap(['exitnotice.create'], NOTICE)
    await screen.findByText('Tornillo')
    expect(inDialog().queryByRole('button', { name: 'Despachar' })).toBeNull()
    unmount()

    wrap(['warehouse.pick'], NOTICE)
    await screen.findByText('Tornillo')
    await user.click(inDialog().getByRole('button', { name: 'Despachar' }))
    await user.click(await screen.findByRole('button', { name: 'Ajustar cantidades' }))
    const qty = await screen.findByLabelText(/TORN-01/)
    expect(qty).toHaveValue(4)
    await user.clear(qty)
    await user.type(qty, '9')
    expect(await screen.findByText('No puede despachar más de lo reservado.')).toBeInTheDocument()
    await user.clear(qty)
    await user.type(qty, '3')
    await user.click(screen.getAllByRole('button', { name: 'Despachar' }).at(-1)!)
    await waitFor(() => {
      const call = mock.calls.find((c) => c.method === 'POST' && c.url.pathname.endsWith('/dispatch'))
      expect(call?.body).toMatchObject({ lines: [{ lineId: 5, quantity: 3 }] })
    })
  })

  it('Despachar todo lo reservado con un solo clic (sin tocar cantidades)', async () => {
    const user = userEvent.setup()
    wrap(['warehouse.pick'], NOTICE)
    await screen.findByText('Tornillo')
    await user.click(inDialog().getByRole('button', { name: 'Despachar' }))
    expect(await screen.findByText(/Se despachará todo lo reservado: 4 unidades en 1 líneas\./)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Despachar todo lo reservado' }))
    await waitFor(() => {
      const call = mock.calls.find((c) => c.method === 'POST' && c.url.pathname.endsWith('/dispatch'))
      expect(call?.body).toMatchObject({ lines: [{ lineId: 5, quantity: 4 }] })
    })
  })
})
