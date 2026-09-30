// Ajuste de inventario del Kárdex ("Ajustar"): la nota es obligatoria (decisión del 2026-09-30) con el mismo mensaje que el
// bloque de ajuste del modal de producto, viaja recortada en `notes` y el 400 del API en `errors.notes` queda bajo el campo.
// Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { InventoryAdjustModal } from './InventoryAdjustModal'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], adjust: null as null | (() => Response) }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.text()
    mock.calls.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    const body = route(req.method, url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const PID = 'aaaaaaaa-0000-0000-0000-000000000001'
const PRODUCT = { id: 1, publicId: PID, sku: 'TORN-01', name: 'Tornillo', isOwn: true, isActive: true, trackingTypeCode: 'NONE' }

function route(method: string, url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/catalogs/AdjustmentReason') return [{ code: 'FOUND', label: 'Encontrado', sortOrder: 1 }]
  if (p === '/api/v1/products') return { total: 1, skip: 0, take: 20, items: [PRODUCT] }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`)
    return { total: 1, skip: 0, take: 50, items: [{ id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true }] }
  if (method === 'POST' && p === '/api/v1/inventory/adjustments') return mock.adjust ? mock.adjust() : { transactions: [], balances: [] }
  return []
}

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['inventory.view', 'inventory.adjust']} modules={['WMS_LOTSERIAL']}>
          <InventoryAdjustModal open onClose={() => {}} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Llena producto, almacén, posición, cantidad y motivo (todo menos la nota). */
async function fillAllButNotes(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
  await user.type(within(dialog).getByRole('combobox', { name: /^Producto/ }), 'torn')
  await user.click(await screen.findByRole('option', { name: /TORN-01 · Tornillo/ }))
  await user.click(within(dialog).getByRole('combobox', { name: /^Almacén/ }))
  await user.click(await screen.findByRole('option', { name: 'ALM-01 · Almacén principal' }))
  await user.click(within(dialog).getByRole('combobox', { name: /^Posición/ }))
  await user.click(await screen.findByRole('option', { name: /A-01/ }))
  await user.type(within(dialog).getByLabelText(/^Cantidad/), '2')
  await waitFor(() => expect(within(dialog).getByRole('option', { name: 'Encontrado' })).toBeInTheDocument())
  await user.selectOptions(within(dialog).getByLabelText(/^Motivo/), 'FOUND')
}

const posts = () => mock.calls.filter((c) => c.method === 'POST')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.adjust = null
})

describe('InventoryAdjustModal', () => {
  it('la nota es obligatoria (también si solo trae espacios) y sin ella no llama al API', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    expect(within(dialog).getByText('Notas').closest('label')).toHaveTextContent('*')
    await fillAllButNotes(user, dialog)
    await user.type(within(dialog).getByLabelText(/^Notas/), '   ')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Escriba una nota que explique el ajuste.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })

  it('con nota: POST con la nota recortada', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await fillAllButNotes(user, dialog)
    await user.type(within(dialog).getByLabelText(/^Notas/), '  Caja encontrada en muelle ')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toEqual({ productPublicId: PID, warehousePublicId: WH, binId: 10, quantity: 2, reason: 'FOUND', notes: 'Caja encontrada en muelle' })
  })

  it('el 400 del API en errors.notes queda bajo el campo Notas', async () => {
    const user = userEvent.setup()
    mock.adjust = () =>
      new Response(
        JSON.stringify({ title: 'Uno o más campos no son válidos.', status: 400, code: 'validation', errors: { notes: ['Escriba una nota que explique el ajuste.'] } }),
        { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
      )
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await fillAllButNotes(user, dialog)
    await user.type(within(dialog).getByLabelText(/^Notas/), 'x')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    const notes = within(dialog).getByLabelText(/^Notas/)
    await waitFor(() => expect(notes).toHaveAttribute('aria-invalid', 'true'))
    expect(within(dialog).getByText('Escriba una nota que explique el ajuste.')).toHaveClass('ferr')
  })
})
