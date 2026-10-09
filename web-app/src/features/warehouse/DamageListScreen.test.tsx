// Daños (2026-10-08): lista con filtros, acciones solo en lo que está en cuarentena y solo con warehouse.damage, desechar (con nota) y recuperar (con
// posición de guardado), y el modal de reporte (validaciones por campo y el cuerpo que se manda). Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { DamagePanel } from './DamageListScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], reply: null as unknown }))
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

const WH = 'aaaaaaaa-0000-0000-0000-000000000001'
const damage = (id: number, over: Record<string, unknown> = {}) => ({
  id,
  publicId: `bbbbbbbb-0000-0000-0000-00000000000${id}`,
  code: `DAN-0000${id}`,
  originCode: 'WAREHOUSE',
  origin: 'Se dañó en el almacén',
  causeCode: 'WAREHOUSE_ACCIDENT',
  cause: 'Accidente en el almacén',
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  productPublicId: 'cccccccc-0000-0000-0000-000000000001',
  sku: 'SKU-1',
  productName: 'Tornillo',
  quantity: 4,
  fromBinCode: 'A-01',
  quarantineBinId: 9,
  quarantineBinCode: 'Q-01',
  statusCode: 'QUARANTINED',
  status: 'En cuarentena',
  reportedAtUtc: '2026-10-08T15:00:00Z',
  reportedByName: 'Ana Operadora',
  ...over,
})
const ITEMS = [damage(1), damage(2, { statusCode: 'DISCARDED', status: 'Desechado', quarantineBinCode: null })]

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/damage-reports') return { total: ITEMS.length, skip: 0, take: 200, items: ITEMS }
  if (method === 'POST' && p.startsWith('/api/v1/damage-reports')) return mock.reply ?? ITEMS[0]
  if (p === '/api/v1/catalogs/DamageCause')
    return [
      { code: 'ARRIVED_DAMAGED', label: 'Vino así' },
      { code: 'WAREHOUSE_ACCIDENT', label: 'Accidente en el almacén' },
    ]
  if (p === '/api/v1/catalogs/DamageFinalDestination')
    return [
      { code: 'DISCARDED_WASTE', label: 'Tirado' },
      { code: 'RETURNED_TO_SUPPLIER', label: 'Devuelto al proveedor' },
    ]
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  if (p === '/api/v1/warehouses') return [{ publicId: WH, code: 'ALM-01', name: 'Almacén', isActive: true }]
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } })
}

function wrap(permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <DamagePanel />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.reply = null
})

describe('Daños', () => {
  it('lista los reportes; desechar y recuperar solo aparecen en lo que está en cuarentena', async () => {
    wrap(['warehouse.damage'])
    const table = await screen.findByRole('table')
    expect(await within(table).findByText('DAN-00001')).toBeInTheDocument()
    expect(within(table).getByText('DAN-00002')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Dar salida' })).toHaveLength(1)
    expect(screen.getAllByRole('button', { name: 'Recuperar' })).toHaveLength(1)
  })

  it('sin warehouse.damage no hay botón de reportar ni acciones', async () => {
    wrap(['inventory.view'])
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: 'Reportar daño' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Dar salida' })).toBeNull()
  })

  it('los filtros viajan al API (estatus, origen y búsqueda)', async () => {
    const user = userEvent.setup()
    wrap(['warehouse.damage'])
    await screen.findByRole('table')
    await user.type(screen.getByRole('searchbox'), 'SKU-1')
    await waitFor(() => expect(mock.calls.some((c) => c.url.searchParams.get('q') === 'SKU-1')).toBe(true))
  })

  it('dar salida exige el destino final y lo manda con la nota al endpoint de desechar', async () => {
    const user = userEvent.setup()
    wrap(['warehouse.damage'])
    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: 'Dar salida' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Se sacan 4 de SKU-1 que están en Q-01/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Dar salida' }))
    expect(await within(dialog).findByText('Indique a dónde va lo que sale (tirado, devuelto al proveedor, donado…).')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)

    await user.click(within(dialog).getByRole('combobox', { name: /Destino final/ }))
    await user.click(await screen.findByRole('option', { name: 'Devuelto al proveedor' }))
    await user.type(within(dialog).getByLabelText('Nota (opcional)'), 'lo recogió el proveedor')
    await user.click(within(dialog).getByRole('button', { name: 'Dar salida' }))
    await waitFor(() => expect(mock.calls.find((c) => c.method === 'POST' && c.url.pathname === '/api/v1/damage-reports/1/discard')).toBeTruthy())
    expect(mock.calls.find((c) => c.url.pathname.endsWith('/discard'))?.body).toEqual({
      toBinId: null,
      finalDestination: 'RETURNED_TO_SUPPLIER',
      notes: 'lo recogió el proveedor',
    })
  })

  it('recuperar exige la posición de guardado antes de llamar al API', async () => {
    const user = userEvent.setup()
    wrap(['warehouse.damage'])
    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: 'Recuperar' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Recuperar' }))
    expect(await within(dialog).findByText('Indique la posición a la que vuelve lo recuperado.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('el modal de reportar valida los campos y no llama al API con errores', async () => {
    const user = userEvent.setup()
    wrap(['warehouse.damage'])
    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: 'Reportar daño' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Reportar' }))
    expect(await within(dialog).findByText('Elija el producto.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique si va a cuarentena o se le da salida.')).toBeInTheDocument()
    expect(within(dialog).getByText('Elija el almacén.')).toBeInTheDocument()
    expect(within(dialog).getByText('La cantidad dañada debe ser mayor que 0.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)
  })
})
