// Lote 2 — Compras: lista sin buscador en la tabla, filtros Proveedor/Almacén de multiselección que viajan como
// `supplierIds`/`warehousePublicIds` (también a la exportación), alta que exige proveedor, almacén y una línea con cantidad > 0,
// y la misma exigencia al editar en la ficha. Ficha (decisión del 2026-09-30): proveedor y almacén editables solo en
// Borrador, el PATCH lleva solo lo que cambió y los errores del servidor quedan en su campo o en el aviso. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import PurchaseOrderDetailScreen from './PurchaseOrderDetailScreen'
import PurchaseOrderListScreen from './PurchaseOrderListScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], order: null as unknown, patch: null as null | (() => Response) }))
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

const WH = '11111111-1111-1111-1111-111111111111'
const PO = '99999999-0000-0000-0000-000000000001'
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'
const ORDER = {
  id: 7,
  publicId: PO,
  number: 'PO-00001',
  supplierId: 1,
  supplierName: 'Acme Corp',
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  statusCode: 'DRAFT',
  status: 'Borrador',
  canEdit: true,
  rowVersion: 'AA==',
  lines: [{ id: 5, productPublicId: P1, sku: 'TORN-01', productName: 'Tornillo', qtyOrdered: 10, qtyReceived: 0, unitCost: 2 }],
}

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/purchase-orders') return { total: 1, skip: 0, take: 25, items: [ORDER] }
  if (method === 'GET' && p === `/api/v1/purchase-orders/${PO}`) return mock.order ?? ORDER
  if (method === 'PATCH' && mock.patch) return mock.patch()
  if (method === 'GET' && p === '/api/v1/suppliers')
    return [
      { id: 1, name: 'Acme Corp', isActive: true },
      { id: 2, name: 'Bolt SA', isActive: true },
    ]
  if (method === 'GET' && p === '/api/v1/warehouses')
    return [
      { id: 1, publicId: WH, code: 'ALM-01', name: 'Principal', isActive: true },
      { id: 2, publicId: '22222222-2222-2222-2222-222222222222', code: 'ALM-02', name: 'Norte', isActive: true },
    ]
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  if (method !== 'GET') return ORDER
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function wrap(ui: React.ReactNode, permissions: string[], path = '/', pattern = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['PURCHASING']}>
          <Routes>
            <Route path={pattern} element={ui} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const MANAGE = ['purchasing.view', 'purchasing.manage']
const listGets = () => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === '/api/v1/purchase-orders')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.order = null
  mock.patch = null
})

const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ title, status, code }), { status, headers: { 'Content-Type': 'application/problem+json' } })
const patchBody = () => mock.calls.find((c) => c.method === 'PATCH')?.body as Record<string, unknown> | undefined

describe('Compras: lista', () => {
  it('sin buscador en la tabla; Proveedor y Almacén son multiselección con buscador y viajan como arreglos', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderListScreen />, MANAGE)
    await screen.findAllByText('PO-00001')
    expect(screen.queryByRole('searchbox', { name: /Buscar/ })).toBeNull()

    await user.click(screen.getByLabelText('Proveedor'))
    await user.type(screen.getByRole('searchbox'), 'bolt')
    expect(screen.queryByLabelText('Acme Corp')).toBeNull()
    await user.click(screen.getByLabelText('Bolt SA'))
    await user.keyboard('{Escape}')
    await waitFor(() => expect(listGets().at(-1)!.url.searchParams.getAll('supplierIds')).toEqual(['2']))

    await user.click(screen.getByLabelText('Almacén'))
    await user.click(screen.getByLabelText(/ALM-02/))
    await user.keyboard('{Escape}')
    await waitFor(() => expect(listGets().at(-1)!.url.searchParams.getAll('warehousePublicIds')).toEqual(['22222222-2222-2222-2222-222222222222']))
    const last = listGets().at(-1)!.url.searchParams
    expect(last.get('supplierId')).toBeNull()
    expect(last.get('warehousePublicId')).toBeNull()
    expect(last.get('skip')).toBe('0')
  })

  it('alta: exige proveedor, almacén y una línea con producto y cantidad mayor que cero', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderListScreen />, MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Nueva orden de compra' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Indique el proveedor.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el almacén.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el producto.')).toBeInTheDocument()
    expect(within(dialog).getByText('La cantidad ordenada debe ser mayor que cero.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)
  })
})

describe('Compras: ficha', () => {
  it('editar: una línea con cantidad 0 no se guarda y muestra el mensaje', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderDetailScreen />, MANAGE, `/po/${PO}`, '/po/:publicId')
    const qty = await screen.findByLabelText(/Cantidad ordenada/)
    expect(qty).toHaveValue(10)
    await user.clear(qty)
    await user.type(qty, '0')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('La cantidad ordenada debe ser mayor que cero.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'PATCH')).toBe(false)
  })

  it('editar: una línea nueva pide producto (buscador) y cantidad', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderDetailScreen />, MANAGE, `/po/${PO}`, '/po/:publicId')
    await user.click(await screen.findByRole('button', { name: 'Agregar línea' }))
    expect(screen.getByRole('combobox', { name: /Producto/ })).toBeInTheDocument()
    await user.type(screen.getAllByLabelText(/Cantidad ordenada/)[0], '1')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Indique el producto.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'PATCH')).toBe(false)
  })
  it('Borrador: Proveedor (buscador) y Almacén editables; el PATCH lleva solo el proveedor que cambió', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderDetailScreen />, MANAGE, `/po/${PO}`, '/po/:publicId')
    const supplier = await screen.findByRole('combobox', { name: /Proveedor/ })
    await waitFor(() => expect(supplier).toHaveValue('Acme Corp'))
    await waitFor(() => expect(screen.getByRole('combobox', { name: /Almacén/ })).toHaveValue('ALM-01 · Principal'))
    await user.clear(supplier)
    await user.type(supplier, 'bolt')
    await user.click(await screen.findByRole('option', { name: /Bolt SA/ }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(patchBody()).toBeDefined())
    expect(patchBody()).toMatchObject({ supplierId: 2, rowVersion: 'AA==' })
    expect(patchBody()).not.toHaveProperty('warehousePublicId')
  })

  it('Borrador: cambiar solo el almacén manda warehousePublicId; un 422 queda bajo el campo Almacén', async () => {
    const user = userEvent.setup()
    mock.patch = () => problem(422, 'El almacén está dado de baja; no admite órdenes de compra nuevas.', 'status_rule')
    wrap(<PurchaseOrderDetailScreen />, MANAGE, `/po/${PO}`, '/po/:publicId')
    const warehouse = await screen.findByRole('combobox', { name: /Almacén/ })
    await user.click(warehouse)
    await user.click(await screen.findByRole('option', { name: /ALM-02/ }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(patchBody()).toBeDefined())
    expect(patchBody()).toMatchObject({ warehousePublicId: '22222222-2222-2222-2222-222222222222' })
    expect(patchBody()).not.toHaveProperty('supplierId')
    const message = await screen.findByText('El almacén está dado de baja; no admite órdenes de compra nuevas.')
    expect(message).toHaveClass('ferr')
    expect(screen.getByRole('combobox', { name: /Almacén/ })).toHaveAttribute('aria-invalid', 'true')
  })

  it('409 (la orden ya salió de Borrador) sale en el aviso del formulario', async () => {
    const user = userEvent.setup()
    const text = 'El proveedor y el almacén solo se cambian mientras la orden de compra está en borrador.'
    mock.patch = () => problem(409, text, 'conflict')
    wrap(<PurchaseOrderDetailScreen />, MANAGE, `/po/${PO}`, '/po/:publicId')
    const supplier = await screen.findByRole('combobox', { name: /Proveedor/ })
    await waitFor(() => expect(supplier).toHaveValue('Acme Corp'))
    await user.clear(supplier)
    await user.type(supplier, 'bolt')
    await user.click(await screen.findByRole('option', { name: /Bolt SA/ }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(text)
    expect(screen.getByRole('combobox', { name: /Proveedor/ })).not.toHaveAttribute('aria-invalid', 'true')
  })

  it('Borrador: proveedor y almacén son obligatorios', async () => {
    const user = userEvent.setup()
    wrap(<PurchaseOrderDetailScreen />, MANAGE, `/po/${PO}`, '/po/:publicId')
    const supplier = await screen.findByRole('combobox', { name: /Proveedor/ })
    await waitFor(() => expect(supplier).toHaveValue('Acme Corp'))
    await user.click(screen.getByRole('button', { name: 'Quitar' }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Indique el proveedor.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'PATCH')).toBe(false)
  })

  it('fuera de Borrador: proveedor y almacén de solo lectura y sin consultar proveedores', async () => {
    mock.order = { ...ORDER, statusCode: 'SENT', status: 'Enviada' }
    wrap(<PurchaseOrderDetailScreen />, MANAGE, `/po/${PO}`, '/po/:publicId')
    await screen.findByLabelText(/Cantidad ordenada/)
    expect(screen.queryByRole('combobox', { name: /Proveedor/ })).toBeNull()
    expect(screen.queryByRole('combobox', { name: /Almacén/ })).toBeNull()
    expect(screen.getByText('ALM-01')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.url.pathname === '/api/v1/suppliers')).toBe(false)
  })
})
