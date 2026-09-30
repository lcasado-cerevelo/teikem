// Lote 14 (P6) — 'Transferencias y ajustes': pestañas (?tab=transfers), cada una con TODOS los movimientos de su tipo
// (D12, "Solo manuales" como filtro), filtros → consulta del Kárdex, columna Dueño, resumen, reporte de ajustes y acciones
// protegidas por inventory.adjust. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import TransfersAdjustmentsScreen from './TransfersAdjustmentsScreen'

const mock = vi.hoisted(() => ({ requests: [] as URL[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    return new Response(JSON.stringify(route(url)), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const W6 = '66666666-0000-0000-0000-000000000006'
const W7 = '77777777-0000-0000-0000-000000000007'

function row(type: string) {
  return type === 'TRANSFER'
    ? { id: 2, createdAtUtc: '2026-09-30T10:00:00', typeCode: 'TRANSFER', type: 'Transferencia', sku: 'TORN-01', productName: 'Tornillo', quantity: 4, signedQuantity: 0, fromWarehouseCode: 'ALM-06', fromBinCode: 'A-01', toWarehouseCode: 'ALM-07', toBinCode: 'B-01', refEntityCode: 'WAREHOUSE_TASK', refLabel: 'Tarea #9', userName: 'Luis C.', ownerName: 'Propio' }
    : { id: 1, createdAtUtc: '2026-09-30T09:00:00', typeCode: 'ADJUSTMENT', type: 'Ajuste', sku: 'TORN-01', productName: 'Tornillo', quantity: 1, signedQuantity: -1, fromWarehouseCode: 'ALM-06', fromBinCode: 'A-01', reasonCode: 'DAMAGE', reason: 'Daño', notes: 'Caja rota', userName: 'Luis C.', ownerName: 'Cliente A' }
}

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/inventory/transactions') {
    const type = url.searchParams.get('types') ?? 'ADJUSTMENT'
    return { total: 1, skip: 0, take: 25, items: [row(type)] }
  }
  if (p === '/api/v1/inventory/transactions/summary') return { movements: 1, inCount: 0, inQty: 0, outCount: 1, outQty: 1, internalCount: 0 }
  if (p === '/api/v1/warehouses')
    return [
      { id: 6, publicId: W6, code: 'ALM-06', name: 'Seis', isActive: true },
      { id: 7, publicId: W7, code: 'ALM-07', name: 'Siete', isActive: true },
    ]
  if (p === '/api/v1/inventory/owners') return [{ name: 'Propio', isOwn: true }]
  if (p === '/api/v1/catalogs/AdjustmentReason') return [{ code: 'DAMAGE', label: 'Daño', sortOrder: 1 }]
  return []
}

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname + location.search}</output>
}

function wrap(url = '/warehouse/transfers-adjustments', permissions = ['inventory.view', 'inventory.adjust']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[url]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <TransfersAdjustmentsScreen />
          <LocationProbe />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const last = (path: string) => mock.requests.filter((u) => u.pathname === path).at(-1)
const waitLast = (path: string, check: (u: URL) => boolean) => waitFor(() => expect(check(last(path)!)).toBe(true), { timeout: 4000 })

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
})

describe('TransfersAdjustmentsScreen', () => {
  it('Ajustes (primera pestaña): lista TODOS los ajustes (sin manualOnly) con Dueño, Motivo y Nota; resumen; Reporte, Ajustar y Transferir', async () => {
    wrap()
    expect(await screen.findByRole('heading', { level: 1, name: 'Transferencias y ajustes' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Ajustes' })).toHaveAttribute('aria-selected', 'true')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('types').join() === 'ADJUSTMENT' && !u.searchParams.has('manualOnly'))
    expect(await screen.findByRole('columnheader', { name: 'Dueño' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Motivo' })).toBeInTheDocument()
    expect(await screen.findByText('Cliente A')).toBeInTheDocument()
    expect(screen.getByText('Caja rota')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Resumen de movimientos' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Reporte de ajustes/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ajustar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Transferir' })).toBeInTheDocument()
  })

  it('filtros de Ajustes al API: Bajar (direction=OUT), Motivo y Solo manuales', async () => {
    const user = userEvent.setup()
    wrap()
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.get('skip') === '0')
    await user.selectOptions(screen.getByLabelText('Tipo de ajuste'), 'OUT')
    await user.click(within(screen.getByRole('group', { name: 'Filtros de movimientos' })).getByRole('button', { name: /^Motivo/ }))
    await user.click(await screen.findByRole('option', { name: /Daño/ }))
    await user.click(screen.getByRole('switch', { name: 'Solo manuales' }))
    await waitLast(
      '/api/v1/inventory/transactions',
      (u) => u.searchParams.get('direction') === 'OUT' && u.searchParams.getAll('reasons').includes('DAMAGE') && u.searchParams.get('manualOnly') === 'true',
    )
    await waitLast('/api/v1/inventory/transactions/summary', (u) => u.searchParams.get('manualOnly') === 'true')
  })

  it('?tab=transfers: Transferencias con origen y destino; filtro por almacén de destino', async () => {
    const user = userEvent.setup()
    wrap('/warehouse/transfers-adjustments?tab=transfers')
    expect(await screen.findByRole('tab', { name: 'Transferencias' })).toHaveAttribute('aria-selected', 'true')
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('types').join() === 'TRANSFER')
    expect(await screen.findByRole('columnheader', { name: 'Destino' })).toBeInTheDocument()
    expect(await screen.findByText('ALM-07/B-01')).toBeInTheDocument()
    expect(screen.getByText(/Acomodo o reabasto/)).toBeInTheDocument()
    // sin Reporte de ajustes en Transferencias
    expect(screen.queryByRole('button', { name: /Reporte de ajustes/ })).toBeNull()
    await user.click(screen.getByRole('button', { name: /^Almacén de destino/ }))
    await user.click(await screen.findByRole('option', { name: /ALM-07/ }))
    await waitLast('/api/v1/inventory/transactions', (u) => u.searchParams.getAll('toWarehousePublicIds').includes(W7))
  })

  it('la pestaña va en la URL y cada pestaña conserva sus filtros', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('switch', { name: 'Solo manuales' }))
    await user.click(screen.getByRole('tab', { name: 'Transferencias' }))
    expect(screen.getByTestId('location')).toHaveTextContent(/\?tab=transfers$/)
    expect(screen.getByRole('switch', { name: 'Solo manuales' })).not.toBeChecked()
    await user.click(screen.getByRole('tab', { name: 'Ajustes' }))
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/warehouse\/transfers-adjustments$/)
    expect(screen.getByRole('switch', { name: 'Solo manuales' })).toBeChecked()
  })

  it('clic en una fila abre el detalle del movimiento', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('Caja rota'))
    expect(await screen.findByRole('dialog', { name: 'Movimiento #1' })).toBeInTheDocument()
    expect(mock.requests.some((u) => u.pathname === '/api/v1/inventory/transactions/1')).toBe(true)
  })

  it('sin inventory.adjust no hay Ajustar ni Transferir', async () => {
    wrap('/warehouse/transfers-adjustments', ['inventory.view'])
    await screen.findByRole('heading', { level: 1, name: 'Transferencias y ajustes' })
    expect(screen.queryByRole('button', { name: 'Ajustar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Transferir' })).toBeNull()
    expect(within(screen.getByRole('tablist')).getAllByRole('tab')).toHaveLength(2)
  })
})
