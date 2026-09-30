// Lote 11 — "Asignar cupo" (BinCapacityModal) sobre un fetch simulado que registra método, ruta y cuerpo: vista previa
// con los mismos filtros, exactamente una acción, allBins solo sin filtros, confirmación con muchas posiciones, cuerpo del
// POST, toast y errores del API en el modal, y el botón en Posiciones y en la ficha del almacén solo con warehouse.manage.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { toast } from '../../kernel/ui'
import { BinCapacityModal } from './BinCapacityModal'
import LocationsScreen from './LocationsScreen'
import WarehouseDetailScreen from './WarehouseDetailScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
type Handler = (call: Call) => unknown
const mock = vi.hoisted(() => ({ calls: [] as Call[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = (mock.handler as Handler)(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const ZONES = [
  { id: 1, code: 'A', name: 'Zona A', zoneTypeCode: 'STORAGE', zoneType: 'Almacenaje', isActive: true, binCount: 2, occupiedBinCount: 1, capacityQty: 0, qtyOnHand: 5, binsWithoutCapacity: 2 },
  { id: 2, code: 'B', name: 'Zona B', zoneTypeCode: 'PICKING', zoneType: 'Picking', isActive: true, binCount: 1, occupiedBinCount: 1, capacityQty: 100, qtyOnHand: 40, binsWithoutCapacity: 0 },
]
const BIN = { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', qtyOnHand: 5, productCount: 0, occupancy: 'NO_CAPACITY', isActive: true }

/** Total de posiciones que devuelve el listado según los filtros (lo que la vista previa debe mostrar). */
function binsTotal(url: URL): number {
  const q = url.searchParams
  if (q.get('position') === 'ZZ') return 0
  if (q.get('aisle')) return 150
  if (q.get('rack')) return 3
  if (q.getAll('zoneIds').length > 0) return 2
  return 3
}

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Principal', isActive: true }]
  if (method === 'GET' && p === `/api/v1/warehouses/${WH}`)
    return { warehouse: { id: 1, publicId: WH, code: 'ALM-01', name: 'Principal', isActive: true, statusCode: 'ACTIVE' }, zones: ZONES, docks: [] }
  if (method === 'GET' && p === `/api/v1/warehouses/${WH}/zones`) return ZONES
  if (method === 'GET' && p === `/api/v1/warehouses/${WH}/bins`) {
    const total = binsTotal(url)
    // con rack: tres posiciones, una con cupo (para contar las que no tienen al marcar "Solo posiciones sin cupo")
    const items = url.searchParams.get('rack')
      ? [
          { ...BIN, id: 1, maxCapacityQty: null },
          { ...BIN, id: 2, maxCapacityQty: 10 },
          { ...BIN, id: 3, maxCapacityQty: null },
        ]
      : [BIN]
    return { total, skip: Number(url.searchParams.get('skip') ?? 0), take: Number(url.searchParams.get('take') ?? 100), items }
  }
  if (method === 'POST' && p === `/api/v1/warehouses/${WH}/bins/capacity`) return { matched: 2, changed: 1 }
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function providers(ui: ReactNode, permissions: string[], path = '/', pattern = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path={pattern} element={ui} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const MANAGE = ['inventory.view', 'warehouse.manage']
const binGets = () => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === `/api/v1/warehouses/${WH}/bins`)
const posts = () => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === `/api/v1/warehouses/${WH}/bins/capacity`)

function openModal(initial?: Parameters<typeof BinCapacityModal>[0]['initial'], onClose = vi.fn()) {
  providers(<BinCapacityModal publicId={WH} open onClose={onClose} initial={initial} />, MANAGE)
  return { onClose, dialog: screen.getByRole('dialog', { name: 'Asignar cupo a posiciones' }) }
}
const preview = (dialog: HTMLElement) => within(dialog).getByRole('status')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.handler = route
})
afterEach(() => vi.restoreAllMocks())

describe('Asignar cupo: vista previa', () => {
  it('cuenta con los MISMOS filtros (take=1, solo activas) y se actualiza al escribir', async () => {
    const user = userEvent.setup()
    const { dialog } = openModal({ zoneIds: ['1'] })
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
    const first = binGets().at(-1)!
    expect(first.url.searchParams.getAll('zoneIds')).toEqual(['1'])
    expect(first.url.searchParams.get('take')).toBe('1')
    expect(first.url.searchParams.get('includeInactive')).toBe('false')
    // la zona precargada se ve elegida
    expect(within(dialog).getByText('A · Zona A')).toBeInTheDocument()

    await user.type(within(dialog).getByRole('searchbox', { name: 'Pasillo' }), 'A')
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 150 posiciones.'))
    const last = binGets().at(-1)!
    expect(last.url.searchParams.get('aisle')).toBe('A')
    expect(last.url.searchParams.getAll('zoneIds')).toEqual(['1'])
  })

  it('sin coincidencias, el botón de aplicar queda deshabilitado', async () => {
    const user = userEvent.setup()
    const { dialog } = openModal()
    await user.type(within(dialog).getByRole('searchbox', { name: 'Posición' }), 'ZZ')
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Ninguna posición coincide'))
    expect(within(dialog).getByRole('button', { name: 'Asignar cupo' })).toBeDisabled()
  })

  it('"Solo posiciones sin cupo": por zonas suma las posiciones sin cupo; con texto, cuenta las del listado sin cupo', async () => {
    const user = userEvent.setup()
    const { dialog } = openModal({ zoneIds: ['1'] })
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
    await user.click(within(dialog).getByRole('switch', { name: 'Solo posiciones sin cupo' }))
    // zona A: binsWithoutCapacity = 2
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
    await user.click(within(dialog).getByRole('button', { name: 'Zona' }))
    await user.click(screen.getByRole('checkbox', { name: 'B · Zona B' }))
    await user.click(screen.getByRole('checkbox', { name: 'A · Zona A' }))
    // solo zona B: ninguna sin cupo
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Ninguna posición coincide'))

    await user.type(within(dialog).getByRole('searchbox', { name: 'Rack' }), 'R')
    // rack → 3 posiciones, 2 sin cupo
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
  })
})

describe('Asignar cupo: alcance y acción', () => {
  it('sin filtros pide un filtro o "Todo el almacén"; la casilla desaparece al poner un filtro', async () => {
    const user = userEvent.setup()
    const { dialog } = openModal()
    expect(preview(dialog)).toHaveTextContent('Elija al menos un filtro')
    expect(within(dialog).getByRole('button', { name: 'Asignar cupo' })).toBeDisabled()
    expect(binGets()).toHaveLength(0)

    await user.click(within(dialog).getByRole('switch', { name: 'Todo el almacén' }))
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 3 posiciones.'))
    expect(binGets().at(-1)!.url.searchParams.getAll('zoneIds')).toEqual([])

    await user.type(within(dialog).getByRole('searchbox', { name: 'Nivel' }), '1')
    expect(within(dialog).queryByRole('switch', { name: 'Todo el almacén' })).toBeNull()
  })

  it('"Todo el almacén" pide confirmación y manda allBins: true con el cupo', async () => {
    const user = userEvent.setup()
    const { dialog } = openModal()
    await user.click(within(dialog).getByRole('switch', { name: 'Todo el almacén' }))
    await user.type(within(dialog).getByRole('spinbutton', { name: 'Cupo máximo (unidades)' }), '25')
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 3 posiciones.'))
    await user.click(within(dialog).getByRole('button', { name: 'Asignar cupo' }))
    const confirm = await screen.findByRole('dialog', { name: 'Confirmar cupo en bloque' })
    expect(confirm).toHaveTextContent('El cambio afecta a todo el almacén.')
    expect(posts()).toHaveLength(0)
    await user.click(within(confirm).getByRole('button', { name: 'Asignar cupo' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toEqual({ includeInactive: false, onlyWithoutCapacity: false, allBins: true, maxCapacityQty: 25 })
  })

  it('exactamente una acción: "Quitar cupo" oculta el cupo, desactiva "Solo sin cupo" y manda clear sin maxCapacityQty', async () => {
    const user = userEvent.setup()
    const { dialog } = openModal({ zoneIds: ['1'] })
    await user.click(within(dialog).getByRole('switch', { name: 'Solo posiciones sin cupo' }))
    await user.click(within(dialog).getByRole('radio', { name: 'Quitar cupo' }))
    expect(within(dialog).getByRole('radio', { name: 'Cupo máximo' })).not.toBeChecked()
    expect(within(dialog).queryByRole('spinbutton')).toBeNull()
    const only = within(dialog).getByRole('switch', { name: 'Solo posiciones sin cupo' })
    expect(only).toBeDisabled()
    expect(only).not.toBeChecked()
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
    await user.click(within(dialog).getByRole('button', { name: 'Quitar cupo' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    const body = posts()[0].body as Record<string, unknown>
    expect(body).toMatchObject({ zoneIds: [1], clear: true, onlyWithoutCapacity: false, allBins: false })
    expect(body).not.toHaveProperty('maxCapacityQty')
  })

  it('el cupo es obligatorio, entero y mayor que cero (no llama al API)', async () => {
    const user = userEvent.setup()
    const { dialog } = openModal({ zoneIds: ['1'] })
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
    await user.click(within(dialog).getByRole('button', { name: 'Asignar cupo' }))
    expect(within(dialog).getByText('Indique el cupo máximo.')).toBeInTheDocument()
    await user.type(within(dialog).getByRole('spinbutton', { name: 'Cupo máximo (unidades)' }), '0')
    expect(within(dialog).getByText('El cupo máximo de la posición debe ser mayor que cero.')).toBeInTheDocument()
    expect(posts()).toHaveLength(0)
  })

  it('más de 100 posiciones pide confirmación; cancelar no aplica', async () => {
    const user = userEvent.setup()
    const { dialog } = openModal()
    await user.type(within(dialog).getByRole('searchbox', { name: 'Pasillo' }), 'A')
    await user.type(within(dialog).getByRole('spinbutton', { name: 'Cupo máximo (unidades)' }), '40')
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 150 posiciones.'))
    await user.click(within(dialog).getByRole('button', { name: 'Asignar cupo' }))
    let confirm = await screen.findByRole('dialog', { name: 'Confirmar cupo en bloque' })
    expect(confirm).toHaveTextContent('Se asignará un cupo máximo de 40 unidades a 150 posiciones.')
    expect(confirm).not.toHaveTextContent('todo el almacén')
    await user.click(within(confirm).getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Confirmar cupo en bloque' })).toBeNull())
    expect(posts()).toHaveLength(0)

    await user.click(within(dialog).getByRole('button', { name: 'Asignar cupo' }))
    confirm = await screen.findByRole('dialog', { name: 'Confirmar cupo en bloque' })
    await user.click(within(confirm).getByRole('button', { name: 'Asignar cupo' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toEqual({ aisle: 'A', includeInactive: false, onlyWithoutCapacity: false, allBins: false, maxCapacityQty: 40 })
  })
})

describe('Asignar cupo: resultado', () => {
  it('pocas posiciones aplican sin confirmar: toast "Cupo aplicado a {changed} de {matched} posiciones" y cierra', async () => {
    const user = userEvent.setup()
    const success = vi.spyOn(toast, 'success').mockImplementation(() => {})
    const { dialog, onClose } = openModal({ zoneIds: ['1'] })
    await user.type(within(dialog).getByRole('spinbutton', { name: 'Cupo máximo (unidades)' }), '12')
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
    await user.click(within(dialog).getByRole('button', { name: 'Asignar cupo' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(screen.queryByRole('dialog', { name: 'Confirmar cupo en bloque' })).toBeNull()
    expect(posts()[0].body).toEqual({ zoneIds: [1], includeInactive: false, onlyWithoutCapacity: false, allBins: false, maxCapacityQty: 12 })
    expect(success).toHaveBeenCalledWith('Cupo aplicado a 1 de 2 posiciones')
  })

  it('los errores del API se quedan en el modal (422 arriba; el del cupo bajo su campo) y no cierra', async () => {
    const user = userEvent.setup()
    const error = vi.spyOn(toast, 'error').mockImplementation(() => {})
    mock.handler = (call: Call) =>
      call.method === 'POST'
        ? new Response(JSON.stringify({ title: 'El almacén está dado de baja; solo se consulta.', status: 422, code: 'status_rule' }), { status: 422 })
        : route(call)
    const { dialog, onClose } = openModal({ zoneIds: ['1'] })
    await user.type(within(dialog).getByRole('spinbutton', { name: 'Cupo máximo (unidades)' }), '12')
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
    await user.click(within(dialog).getByRole('button', { name: 'Asignar cupo' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El almacén está dado de baja; solo se consulta.')
    expect(onClose).not.toHaveBeenCalled()
    expect(error).not.toHaveBeenCalled()

    mock.handler = (call: Call) =>
      call.method === 'POST'
        ? new Response(
            JSON.stringify({ title: 'Hay errores de validación.', status: 400, code: 'validation', errors: { maxCapacityQty: ['El cupo máximo de la posición debe ser mayor que cero.'] } }),
            { status: 400 },
          )
        : route(call)
    await user.click(within(dialog).getByRole('button', { name: 'Asignar cupo' }))
    expect(await within(dialog).findByText('El cupo máximo de la posición debe ser mayor que cero.')).toBeInTheDocument()
    expect(within(dialog).getByRole('spinbutton', { name: 'Cupo máximo (unidades)' })).toHaveAttribute('aria-invalid', 'true')
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Hay errores de validación.')
  })
})

describe('Asignar cupo: botón en las pantallas', () => {
  it('Posiciones: solo con warehouse.manage; abre con la zona de ?zone= y al aplicar refresca posiciones y zonas', async () => {
    const user = userEvent.setup()
    vi.spyOn(toast, 'success').mockImplementation(() => {})
    const path = `/warehouse/locations?warehouse=${WH}&zone=1`
    const readOnly = providers(<LocationsScreen />, ['inventory.view'], path, '/warehouse/locations')
    await screen.findByRole('button', { name: /^Zona Zona A/ })
    expect(screen.queryByRole('button', { name: 'Asignar cupo' })).toBeNull()
    readOnly.unmount()

    providers(<LocationsScreen />, MANAGE, path, '/warehouse/locations')
    await screen.findByRole('button', { name: /^Zona Zona A/ })
    await user.click(screen.getByRole('button', { name: 'Asignar cupo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Asignar cupo a posiciones' })
    expect(within(dialog).getByText('A · Zona A')).toBeInTheDocument()
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))

    const zoneGets = () => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === `/api/v1/warehouses/${WH}/zones`).length
    const zonesBefore = zoneGets()
    const listBefore = binGets().filter((c) => c.url.searchParams.get('take') === '25').length
    await user.type(within(dialog).getByRole('spinbutton', { name: 'Cupo máximo (unidades)' }), '8')
    await user.click(within(dialog).getByRole('button', { name: 'Asignar cupo' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(zoneGets()).toBeGreaterThan(zonesBefore))
    expect(binGets().filter((c) => c.url.searchParams.get('take') === '25').length).toBeGreaterThan(listBefore)
  })

  it('ficha del almacén, pestaña Posiciones: solo con warehouse.manage; arranca con la zona del filtro', async () => {
    const user = userEvent.setup()
    const path = `/warehouse/warehouses/${WH}`
    const pattern = '/warehouse/warehouses/:publicId'
    const readOnly = providers(<WarehouseDetailScreen />, ['inventory.view'], path, pattern)
    await user.click(await screen.findByRole('tab', { name: 'Posiciones' }))
    await screen.findByRole('table', { name: 'Posiciones' })
    expect(screen.queryByRole('button', { name: 'Asignar cupo' })).toBeNull()
    readOnly.unmount()

    providers(<WarehouseDetailScreen />, MANAGE, path, pattern)
    await user.click(await screen.findByRole('tab', { name: 'Posiciones' }))
    await screen.findByRole('table', { name: 'Posiciones' })
    // el filtro Zona de la pestaña (no el encabezado ordenable de la tabla)
    const zoneFilter = screen.getAllByRole('button', { name: 'Zona' }).find((b) => b.getAttribute('aria-haspopup') === 'listbox')!
    await user.click(zoneFilter)
    await user.click(screen.getByRole('checkbox', { name: 'A · Zona A' }))
    await user.keyboard('{Escape}')
    await user.click(screen.getByRole('button', { name: 'Asignar cupo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Asignar cupo a posiciones' })
    await waitFor(() => expect(preview(dialog)).toHaveTextContent('Se aplicará a 2 posiciones.'))
    expect(binGets().at(-1)!.url.searchParams.getAll('zoneIds')).toEqual(['1'])
  })
})
