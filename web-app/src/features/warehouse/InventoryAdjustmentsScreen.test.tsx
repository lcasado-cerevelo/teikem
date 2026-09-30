// Ajustes de inventario (Fase 7, maqueta `ajustesAlmacen()`): maestro-detalle sobre un fetch simulado. Lista de compras
// con recibo parcial (chip "N corto", "N línea(s)"), la elegida en `?po=` (sin él, la primera), UNA sola consulta de
// líneas por la orden elegida (sin N+1), líneas sin pendiente fuera, guardas por permiso/módulo de cada acción y el modal
// de resolución abierto con su acción (Cerrar no consulta posiciones de WMS y manda el pendiente que se ve).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import InventoryAdjustmentsScreen from './InventoryAdjustmentsScreen'

interface Seen {
  method: string
  url: URL
  body: unknown
}
type Handler = (url: URL, method: string) => unknown
const mock = vi.hoisted(() => ({ requests: [] as Seen[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.text()
    mock.requests.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    const body = (mock.handler as Handler)(url, req.method)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const PO_A = '99999999-0000-0000-0000-00000000000a'
const PO_B = '99999999-0000-0000-0000-00000000000b'
const PATH = '/warehouse/inventory-adjustments'

const SUMMARIES = [
  { publicId: PO_A, number: 'OC-00001', supplierId: 1, supplierName: 'Proveedor Uno', linesWithShortage: 1, qtyPending: 2, pendingCost: 5, warehousePublicId: WH, warehouseCode: 'ALM-01' },
  { publicId: PO_B, number: 'OC-00002', supplierId: 2, supplierName: 'Proveedor Dos', linesWithShortage: 2, qtyPending: 3.5, pendingCost: 7, warehousePublicId: WH, warehouseCode: 'ALM-01' },
]
const line = (id: number, sku: string, name: string, ordered: number, received: number, pending: number) => ({
  purchaseOrderLineId: id,
  productPublicId: `aaaaaaaa-0000-0000-0000-00000000000${id}`,
  sku,
  productName: name,
  qtyOrdered: ordered,
  qtyReceived: received,
  qtyResolved: ordered - received - pending,
  qtyPending: pending,
  unitCost: 1,
  pendingCost: pending,
  resolutions: [],
})
const LINES: Record<string, unknown[]> = {
  // ARAN-01 ya resuelta (pendiente 0): el endpoint la trae por su bitácora, la pantalla no la muestra
  [PO_A]: [line(5, 'TORN-01', 'Tornillo', 10, 8, 2), line(6, 'ARAN-01', 'Arandela', 4, 2, 0)],
  [PO_B]: [line(7, 'CLAV-01', 'Clavo', 5, 3.5, 1.5), line(8, 'PERN-01', 'Perno', 2, 0, 2)],
}

function routes(summaries: unknown[] = SUMMARIES): Handler {
  return (url, method) => {
    const p = url.pathname
    if (p === '/api/v1/purchase-orders/shortages') return summaries
    for (const po of [PO_A, PO_B]) if (p === `/api/v1/purchase-orders/${po}/shortage-lines`) return LINES[po]
    if (method === 'POST' && p === `/api/v1/purchase-orders/${PO_A}/lines/5/resolve`)
      return {
        line: { ...line(5, 'TORN-01', 'Tornillo', 10, 8, 0), resolutions: [{ id: 1, actionCode: 'CLOSE', action: 'Cerrar', quantity: 2 }] },
        purchaseOrder: { publicId: PO_A, number: 'OC-00001' },
        reorder: null,
      }
    return new Response(JSON.stringify({ title: 'Sin acceso', code: 'forbidden' }), { status: 403 })
  }
}

function Probe() {
  const location = useLocation()
  return <output data-testid="location">{location.search}</output>
}

function wrap(permissions: string[], modules: string[], path = PATH) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={modules}>
          <Routes>
            <Route
              path={PATH}
              element={
                <>
                  <InventoryAdjustmentsScreen />
                  <Probe />
                </>
              }
            />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const linesRequests = () => mock.requests.filter((r) => r.url.pathname.endsWith('/shortage-lines'))
const ALL = ['purchasing.view', 'purchasing.manage', 'inventory.adjust']
const ALL_MODULES = ['PURCHASING', 'WMS_LOTSERIAL']

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.handler = routes()
})

describe('Ajustes de inventario (maqueta ajustesAlmacen())', () => {
  it('lista las compras con su chip "N corto" y "N línea(s)"; elige la primera con UNA sola consulta de líneas', async () => {
    wrap(ALL, ALL_MODULES)
    const list = await screen.findByRole('group', { name: 'Compras con recibo parcial' })
    const first = await within(list).findByRole('button', { name: /OC-00001/ })
    expect(within(first).getByText('2 corto')).toBeInTheDocument()
    expect(within(first).getByText('1 línea(s)')).toBeInTheDocument()
    expect(within(first).getByText('Proveedor Uno')).toBeInTheDocument()
    expect(within(list).getByRole('button', { name: /OC-00002/ })).toHaveTextContent('3,5 corto')
    expect(first).toHaveAttribute('aria-current', 'true')

    // detalle de la primera: solo líneas con pendiente > 0
    expect(await screen.findByText('TORN-01')).toBeInTheDocument()
    expect(screen.queryByText('ARAN-01')).toBeNull()
    expect(screen.getByText(/Reordenar crea un borrador de PO nuevo/)).toBeInTheDocument()
    expect(linesRequests().map((r) => r.url.pathname)).toEqual([`/api/v1/purchase-orders/${PO_A}/shortage-lines`])
  })

  it('clic en otra compra la pone en ?po= y trae sus líneas', async () => {
    const user = userEvent.setup()
    wrap(ALL, ALL_MODULES)
    await screen.findByText('TORN-01')
    await user.click(screen.getByRole('button', { name: /OC-00002/ }))
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(`?po=${PO_B}`))
    expect(await screen.findByText('CLAV-01')).toBeInTheDocument()
    expect(screen.getByText('PERN-01')).toBeInTheDocument()
    expect(screen.queryByText('TORN-01')).toBeNull()
    expect(screen.getByRole('button', { name: /OC-00002/ })).toHaveAttribute('aria-current', 'true')
  })

  it('?po= de la URL elige esa compra al entrar; uno que ya no está cae en la primera', async () => {
    const one = wrap(ALL, ALL_MODULES, `${PATH}?po=${PO_B}`)
    expect(await screen.findByText('CLAV-01')).toBeInTheDocument()
    one.unmount()
    wrap(ALL, ALL_MODULES, `${PATH}?po=00000000-0000-0000-0000-000000000000`)
    expect(await screen.findByText('TORN-01')).toBeInTheDocument()
  })

  it('sin inventory.adjust no hay columna Resolver ni botones', async () => {
    wrap(['purchasing.view'], ALL_MODULES)
    await screen.findByText('TORN-01')
    expect(screen.queryByRole('columnheader', { name: /Resolver/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Cerrar' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Reordenar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Ajuste manual' })).toBeNull()
  })

  it('Reordenar exige purchasing.manage y Ajuste manual el módulo WMS_LOTSERIAL', async () => {
    const onlyAdjust = wrap(['purchasing.view', 'inventory.adjust'], ['PURCHASING'])
    await screen.findByText('TORN-01')
    expect(screen.getByRole('button', { name: 'Cerrar' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Reordenar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Ajuste manual' })).toBeNull()
    expect(screen.queryByLabelText(/Cantidad del ajuste/)).toBeNull()
    onlyAdjust.unmount()

    wrap(ALL, ALL_MODULES)
    await screen.findByText('TORN-01')
    expect(screen.getByRole('button', { name: /Reordenar/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ajuste manual' })).toBeInTheDocument()
    expect(screen.getByLabelText('Cantidad del ajuste de TORN-01')).toBeInTheDocument()
    expect(screen.getByLabelText('Motivo del ajuste de TORN-01')).toBeInTheDocument()
  })

  it('Cerrar abre el modal en CLOSE sin consultar posiciones, manda el pendiente y avisa "Faltante cerrado"', async () => {
    const user = userEvent.setup()
    wrap(['purchasing.view', 'inventory.adjust'], ['PURCHASING'])
    const table = await screen.findByRole('table')
    await user.click(within(table).getByRole('button', { name: 'Cerrar' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByLabelText(/Acción/)).toHaveValue('CLOSE')
    expect(within(dialog).getByText('TORN-01 · Tornillo — Faltante: 2')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.url.pathname.endsWith('/bins'))).toBe(false)
    expect(mock.requests.some((r) => r.url.pathname.startsWith('/api/v1/products/'))).toBe(false)

    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Faltante cerrado: TORN-01')).toBeInTheDocument()
    const post = mock.requests.find((r) => r.method === 'POST')
    expect(post?.url.pathname).toBe(`/api/v1/purchase-orders/${PO_A}/lines/5/resolve`)
    expect(post?.body).toMatchObject({ action: 'CLOSE', quantity: 2, binId: null, rowVersion: null })
  })

  it('si el pendiente cambió en el servidor, el 400 de Cerrar se ve arriba del formulario (sin campo Cantidad) y el modal sigue abierto', async () => {
    const user = userEvent.setup()
    const message = 'Cerrar y Reordenar resuelven el faltante completo (1); para una parte use el ajuste manual.'
    const base = routes()
    mock.handler = (url: URL, method: string) =>
      method === 'POST'
        ? new Response(JSON.stringify({ title: 'Uno o más campos no son válidos.', status: 400, code: 'validation', errors: { quantity: [message] } }), {
            status: 400,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        : base(url, method)
    wrap(['purchasing.view', 'inventory.adjust'], ['PURCHASING'])
    const table = await screen.findByRole('table')
    await user.click(within(table).getByRole('button', { name: 'Cerrar' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).queryByLabelText(/^Cantidad/)).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText(message)).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('Ajuste manual precarga en el modal la cantidad y el motivo capturados en la fila', async () => {
    const user = userEvent.setup()
    wrap(ALL, ALL_MODULES)
    await screen.findByText('TORN-01')
    await user.type(screen.getByLabelText('Cantidad del ajuste de TORN-01'), '1')
    await user.type(screen.getByLabelText('Motivo del ajuste de TORN-01'), 'Apareció en muelle')
    await user.click(screen.getByRole('button', { name: 'Ajuste manual' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByLabelText(/Acción/)).toHaveValue('MANUAL_ADJUSTMENT')
    expect(within(dialog).getByLabelText(/^Cantidad/)).toHaveValue(1)
    expect(within(dialog).getByLabelText(/Notas/)).toHaveValue('Apareció en muelle')
  })

  it('Ajuste manual exige la nota (mismo mensaje que todo ajuste); Cerrar la deja opcional', async () => {
    const user = userEvent.setup()
    wrap(ALL, ALL_MODULES)
    await screen.findByText('TORN-01')
    await user.type(screen.getByLabelText('Cantidad del ajuste de TORN-01'), '1')
    await user.click(screen.getByRole('button', { name: 'Ajuste manual' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Notas').closest('label')).toHaveTextContent('*')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Escriba una nota que explique el ajuste.')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.method === 'POST')).toBe(false)

    // al pasar a Cerrar la nota deja de ser obligatoria
    await user.selectOptions(within(dialog).getByLabelText(/Acción/), 'CLOSE')
    expect(within(dialog).getByText('Notas').closest('label')).not.toHaveTextContent('*')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'POST')?.body).toMatchObject({ action: 'CLOSE', notes: null })
  })

  it('el 400 del API en errors.notes queda bajo el campo Notas del modal', async () => {
    const user = userEvent.setup()
    const base = routes()
    mock.handler = (url: URL, method: string) =>
      method === 'POST'
        ? new Response(
            JSON.stringify({ title: 'Uno o más campos no son válidos.', status: 400, code: 'validation', errors: { notes: ['Escriba una nota que explique el ajuste.'] } }),
            { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
          )
        : base(url, method)
    wrap(['purchasing.view', 'inventory.adjust'], ['PURCHASING'])
    const table = await screen.findByRole('table')
    await user.click(within(table).getByRole('button', { name: 'Cerrar' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(within(dialog).getByLabelText(/^Notas/)).toHaveAttribute('aria-invalid', 'true'))
    expect(within(dialog).getByText('Escriba una nota que explique el ajuste.')).toHaveClass('ferr')
  })

  it('sin compras con faltante: "Sin recibos parciales pendientes" y "Selecciona una compra", sin consultar líneas', async () => {
    mock.handler = routes([])
    wrap(ALL, ALL_MODULES)
    expect(await screen.findByText('Sin recibos parciales pendientes')).toBeInTheDocument()
    expect(screen.getByText('Selecciona una compra')).toBeInTheDocument()
    expect(linesRequests()).toHaveLength(0)
  })
})
