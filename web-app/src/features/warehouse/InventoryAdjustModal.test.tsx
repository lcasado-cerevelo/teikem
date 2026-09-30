// Ajuste de inventario (Lote 14, D11): "Subir"/"Bajar" con cantidad POSITIVA (la pantalla pone el signo que recibe el API),
// motivos según la dirección (Encontrado solo al subir; Daño, Pérdida y Vencido solo al bajar), pista de lo disponible en la
// posición y tope al bajar, lote al bajar un producto LOT (de los saldos de la posición), nota obligatoria (mismo mensaje
// que el API; su 400 queda bajo el campo). Sobre un fetch simulado.
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
const mock = vi.hoisted(() => ({ calls: [] as Call[], adjust: null as null | (() => Response), tracking: 'NONE' }))
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
const product = () => ({ id: 1, publicId: PID, sku: 'TORN-01', name: 'Tornillo', isOwn: true, isActive: true, trackingTypeCode: mock.tracking })

const REASONS = [
  { code: 'COUNT_VARIANCE', label: 'Diferencia de conteo', sortOrder: 2 },
  { code: 'DAMAGE', label: 'Daño', sortOrder: 3 },
  { code: 'LOSS', label: 'Pérdida', sortOrder: 4 },
  { code: 'FOUND', label: 'Encontrado', sortOrder: 5 },
  { code: 'EXPIRED', label: 'Vencido', sortOrder: 6 },
  { code: 'OTHER', label: 'Otro', sortOrder: 9 },
]

// B-09 no tiene existencias del producto: al bajar no se ofrece
const BINS = [
  { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true },
  { id: 11, code: 'B-09', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true },
  { id: 12, code: 'C-02', zoneId: 2, zoneCode: 'C', zoneTypeCode: 'RESERVE', isActive: true },
]
const BALANCES = [
  { id: 3, binId: 12, binCode: 'C-02', zoneCode: 'C', zoneTypeCode: 'RESERVE', productPublicId: PID, lotId: 7, lotNumber: 'L-7', qtyOnHand: 4, qtyReserved: 0, qtyAvailable: 4 },
  { id: 1, binId: 10, binCode: 'A-01', zoneCode: 'A', zoneTypeCode: 'STORAGE', productPublicId: PID, lotId: 7, lotNumber: 'L-7', qtyOnHand: 3, qtyReserved: 0, qtyAvailable: 3 },
  { id: 2, binId: 10, binCode: 'A-01', zoneCode: 'A', zoneTypeCode: 'STORAGE', productPublicId: PID, lotId: 8, lotNumber: 'L-8', qtyOnHand: 2, qtyReserved: 0, qtyAvailable: 2 },
]

function route(method: string, url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/catalogs/AdjustmentReason') return REASONS
  if (p === '/api/v1/products') return { total: 1, skip: 0, take: 20, items: [product()] }
  if (p === `/api/v1/products/${PID}`) return { product: product() }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) return { total: BINS.length, skip: 0, take: 50, items: BINS }
  if (p === '/api/v1/inventory/balances') {
    // saldos del producto: por posición (binIds) o de todo el almacén (posiciones donde se puede bajar)
    const ids = url.searchParams.getAll('binIds').map(Number)
    const items = BALANCES.filter((b) => ids.length === 0 || ids.includes(b.binId))
    return { total: items.length, skip: 0, take: 200, items }
  }
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

type User = ReturnType<typeof userEvent.setup>

async function pickReason(user: User, dialog: HTMLElement, text: string, label: string) {
  await user.clear(within(dialog).getByRole('combobox', { name: /^Motivo/ }))
  await user.type(within(dialog).getByRole('combobox', { name: /^Motivo/ }), text)
  await user.click(await screen.findByRole('option', { name: label }))
}

/** Producto, almacén y posición. */
async function fillWhere(user: User, dialog: HTMLElement) {
  await user.type(within(dialog).getByRole('combobox', { name: /^Producto/ }), 'torn')
  await user.click(await screen.findByRole('option', { name: /TORN-01 · Tornillo/ }))
  await user.click(within(dialog).getByRole('combobox', { name: /^Almacén/ }))
  await user.click(await screen.findByRole('option', { name: 'ALM-01 · Almacén principal' }))
  await user.click(within(dialog).getByRole('combobox', { name: /^Posición/ }))
  await user.click(await screen.findByRole('option', { name: /A-01/ }))
}

const posts = () => mock.calls.filter((c) => c.method === 'POST')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.adjust = null
  mock.tracking = 'NONE'
})

describe('InventoryAdjustModal · Subir/Bajar (D11)', () => {
  it('sin dirección no guarda: pide elegir si sube o baja; la cantidad debe ser mayor que cero', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    expect(within(dialog).getByRole('radiogroup', { name: 'Tipo de ajuste' })).toBeInTheDocument()
    await fillWhere(user, dialog)
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '0')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar ajuste' }))
    expect(await within(dialog).findByText('Elija si el ajuste sube o baja el inventario.')).toBeInTheDocument()
    expect(within(dialog).getByText('La cantidad debe ser mayor que cero.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })

  it('Subir: Encontrado sí, Daño no; POST con la cantidad positiva y la nota recortada', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await user.click(within(dialog).getByRole('radio', { name: /Subir/ }))
    expect(within(dialog).getByRole('radio', { name: /Subir/ })).toHaveAttribute('aria-checked', 'true')
    await fillWhere(user, dialog)
    await user.type(within(dialog).getByRole('combobox', { name: /^Motivo/ }), 'dañ')
    expect(await screen.findByText('Sin coincidencias')).toBeInTheDocument()
    await pickReason(user, dialog, 'encon', 'Encontrado')
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '2')
    await user.type(within(dialog).getByLabelText(/^Notas/), '  Caja encontrada en muelle ')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar ajuste' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toEqual({ productPublicId: PID, warehousePublicId: WH, binId: 10, quantity: 2, reason: 'FOUND', notes: 'Caja encontrada en muelle' })
  })

  it('Bajar: Daño sí, Encontrado no; POST con la cantidad en negativo', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await user.click(within(dialog).getByRole('radio', { name: /Bajar/ }))
    await fillWhere(user, dialog)
    await user.type(within(dialog).getByRole('combobox', { name: /^Motivo/ }), 'encon')
    expect(await screen.findByText('Sin coincidencias')).toBeInTheDocument()
    await pickReason(user, dialog, 'dañ', 'Daño')
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '2')
    await user.type(within(dialog).getByLabelText(/^Notas/), 'Caja rota')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar ajuste' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ quantity: -2, reason: 'DAMAGE' })
  })

  it('al cambiar a Bajar se quita un motivo que solo vale al subir', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await user.click(within(dialog).getByRole('radio', { name: /Subir/ }))
    await pickReason(user, dialog, 'encon', 'Encontrado')
    expect(within(dialog).getByRole('combobox', { name: /^Motivo/ })).toHaveValue('Encontrado')
    await user.click(within(dialog).getByRole('radio', { name: /Bajar/ }))
    expect(within(dialog).getByRole('combobox', { name: /^Motivo/ })).toHaveValue('')
  })

  it('Bajar muestra lo disponible en la posición y no deja bajar más', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await user.click(within(dialog).getByRole('radio', { name: /Bajar/ }))
    await fillWhere(user, dialog)
    expect(await within(dialog).findByText('Disponible en la posición: 5')).toBeInTheDocument()
    await pickReason(user, dialog, 'pérd', 'Pérdida')
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '9')
    await user.type(within(dialog).getByLabelText(/^Notas/), 'x')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar ajuste' }))
    expect(await within(dialog).findByText('No puede bajar más de lo disponible en la posición (5).')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })

  it('producto LOT al bajar: se elige el lote de los saldos de la posición y viaja como lotId', async () => {
    const user = userEvent.setup()
    mock.tracking = 'LOT'
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await user.click(within(dialog).getByRole('radio', { name: /Bajar/ }))
    await fillWhere(user, dialog)
    await user.click(await within(dialog).findByRole('combobox', { name: /^Lote/ }))
    await user.click(await screen.findByRole('option', { name: /L-8/ }))
    expect(await within(dialog).findByText('Disponible en la posición: 2')).toBeInTheDocument()
    await pickReason(user, dialog, 'venc', 'Vencido')
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '1')
    await user.type(within(dialog).getByLabelText(/^Notas/), 'Vencido en estante')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar ajuste' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toMatchObject({ quantity: -1, reason: 'EXPIRED', lotId: 8 })
  })

  it('Bajar: sin producto la posición está deshabilitada; con producto solo ofrece donde hay disponible, con su disponible y por código', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await user.click(within(dialog).getByRole('radio', { name: /Bajar/ }))
    await user.click(within(dialog).getByRole('combobox', { name: /^Almacén/ }))
    await user.click(await screen.findByRole('option', { name: 'ALM-01 · Almacén principal' }))
    const bin = within(dialog).getByRole('combobox', { name: /^Posición/ })
    expect(bin).toBeDisabled()
    expect(bin).toHaveAttribute('placeholder', 'Elija primero un producto')
    await user.type(within(dialog).getByRole('combobox', { name: /^Producto/ }), 'torn')
    await user.click(await screen.findByRole('option', { name: /TORN-01 · Tornillo/ }))
    await waitFor(() => expect(bin).toBeEnabled())
    await user.click(bin)
    await waitFor(() => expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['A-01 · A · 5 disp.', 'C-02 · C · 4 disp.']))
    expect(screen.queryByRole('option', { name: /B-09/ })).toBeNull()
  })

  it('Subir ofrece todas las posiciones; al pasar a Bajar se quita una posición sin disponible del producto', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    await user.click(within(dialog).getByRole('radio', { name: /Subir/ }))
    await user.type(within(dialog).getByRole('combobox', { name: /^Producto/ }), 'torn')
    await user.click(await screen.findByRole('option', { name: /TORN-01 · Tornillo/ }))
    await user.click(within(dialog).getByRole('combobox', { name: /^Almacén/ }))
    await user.click(await screen.findByRole('option', { name: 'ALM-01 · Almacén principal' }))
    const bin = within(dialog).getByRole('combobox', { name: /^Posición/ })
    await user.click(bin)
    await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(3))
    await user.click(screen.getByRole('option', { name: /B-09/ }))
    expect(bin).toHaveValue('B-09 · A')
    await user.click(within(dialog).getByRole('radio', { name: /Bajar/ }))
    await waitFor(() => expect(bin).toHaveValue(''))
  })

  it('la nota es obligatoria (también si solo trae espacios) y el 400 del API en errors.notes queda bajo el campo', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Ajuste de inventario' })
    expect(within(dialog).getByText('Notas').closest('label')).toHaveTextContent('*')
    await user.click(within(dialog).getByRole('radio', { name: /Subir/ }))
    await fillWhere(user, dialog)
    await pickReason(user, dialog, 'otro', 'Otro')
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '1')
    await user.type(within(dialog).getByLabelText(/^Notas/), '   ')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar ajuste' }))
    expect(await within(dialog).findByText('Escriba una nota que explique el ajuste.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)

    mock.adjust = () =>
      new Response(
        JSON.stringify({ title: 'Uno o más campos no son válidos.', status: 400, code: 'validation', errors: { notes: ['Escriba una nota que explique el ajuste.'] } }),
        { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
      )
    await user.type(within(dialog).getByLabelText(/^Notas/), 'x')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar ajuste' }))
    const notes = within(dialog).getByLabelText(/^Notas/)
    await waitFor(() => expect(posts()).toHaveLength(1))
    await waitFor(() => expect(notes).toHaveAttribute('aria-invalid', 'true'))
    expect(within(dialog).getByText('Escriba una nota que explique el ajuste.')).toHaveClass('ferr')
  })
})
