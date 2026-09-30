// Lote 1 (cambios de Almacén) — pruebas de Almacenes (lista maestro-detalle, alta con Ciudad/ZIP) y de la ficha (Zonas y
// Posiciones con paginación del servidor) sobre un fetch simulado que registra método, ruta y cuerpo.
// Lote 16: modo de recepción en el alta, columna Recepción y sección Recepción de la ficha (confirmación con conteos, D12).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import WarehouseDetailScreen from './WarehouseDetailScreen'
import WarehouseListScreen from './WarehouseListScreen'

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

const WH1 = '11111111-1111-1111-1111-111111111111'
const WH2 = '22222222-2222-2222-2222-222222222222'
const WAREHOUSES = [
  { id: 1, publicId: WH1, code: 'ALM-10', name: 'Central', line1: 'Calle Luna 5', city: 'San Juan', state: 'PR', postalCode: '00901', countryCode: 'PR', statusCode: 'ACTIVE', status: 'Activo', isActive: true, zoneCount: 2, zoneTypeCodes: ['STORAGE'], receivingModeCode: 'PUTAWAY', receivingMode: 'Con acomodo', defaultReceivingBinId: 20, defaultReceivingBinCode: 'STG-1', rowVersion: 'AQ==' },
  { id: 2, publicId: WH2, code: 'ALM-2', name: 'Norte', line1: null, city: 'Mayagüez', state: 'PR', postalCode: '00680', countryCode: 'PR', statusCode: 'ACTIVE', status: 'Activo', isActive: true, zoneCount: 0, zoneTypeCodes: [], receivingModeCode: 'DIRECT', receivingMode: 'Directo a posición' },
]
const ZONES = [
  { id: 1, code: 'A', name: 'Zona A', zoneTypeCode: 'STORAGE', zoneType: 'Almacenaje', isActive: true, binCount: 3, occupiedBinCount: 1 },
  { id: 2, code: 'B', name: 'Zona B', zoneTypeCode: 'STORAGE', zoneType: 'Almacenaje', isActive: true, binCount: 0, occupiedBinCount: 0 },
]
const STG_BIN = { id: 20, code: 'STG-1', zoneId: 3, zoneCode: 'R', zoneTypeCode: 'STAGING', isActive: true }
const BIN = { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', aisle: 'A', rack: '01', qtyOnHand: 5, productCount: 1, singleProductSku: 'TORN-01', maxCapacityQty: 10, occupancy: 'PARTIAL', isActive: true }
// catálogo USPS: ciudad postal en mayúsculas sin acentos; municipio (solo PR) con acentos
const LOCALITIES = [{ id: 7, city: 'MAYAGUEZ', postalCode: '00680', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Mayagüez' }]

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/warehouses') return WAREHOUSES
  if (method === 'POST' && p === '/api/v1/warehouses') return { ...WAREHOUSES[0], publicId: 'new' }
  if (method === 'GET' && p === `/api/v1/warehouses/${WH1}`) return { warehouse: WAREHOUSES[0], zones: ZONES, docks: [] }
  if (method === 'GET' && (p === `/api/v1/warehouses/${WH1}/zones` || p === `/api/v1/warehouses/${WH2}/zones`)) return p.includes(WH1) ? ZONES : []
  if (method === 'GET' && p === `/api/v1/warehouses/${WH1}/bins` && url.searchParams.getAll('binIds').includes('20'))
    return { total: 1, skip: 0, take: 1, items: [STG_BIN] }
  if (method === 'GET' && p === `/api/v1/warehouses/${WH1}/bins`) {
    const skip = Number(url.searchParams.get('skip') ?? 0)
    return { total: 120, skip, take: Number(url.searchParams.get('take') ?? 100), items: [BIN] }
  }
  if (method === 'PATCH' && p === `/api/v1/warehouses/${WH1}/zones/1`)
    return new Response(JSON.stringify({ title: 'Ya existe una zona con ese código en el almacén.', status: 409, code: 'conflict' }), { status: 409 })
  if (method === 'PATCH' && p === `/api/v1/warehouses/${WH1}/bins/10`) return BIN
  if (method === 'PATCH' && p === `/api/v1/warehouses/${WH1}`) return WAREHOUSES[0]
  // conteos del diálogo de cambio de modo (take=1 → total)
  if (method === 'GET' && p === '/api/v1/receipts')
    return { total: url.searchParams.get('phase') === 'OPEN' ? 3 : 1, skip: 0, take: 1, items: [] }
  if (method === 'POST' && p.endsWith('/deactivate')) return ZONES[1]
  if (p === '/api/v1/postal-localities') return LOCALITIES
  if (p === '/api/v1/catalogs/ZoneType')
    return [
      { code: 'STORAGE', label: 'Almacenaje', sortOrder: 1 },
      { code: 'STAGING', label: 'Preparación', sortOrder: 2 },
    ]
  if (p === '/api/v1/catalogs/Country') return [{ code: 'PR', label: 'Puerto Rico' }]
  if (p === '/api/v1/status/WarehouseStatus') return [{ code: 'ACTIVE', label: 'Activo', stageKind: 'PIPELINE', isInitial: true }]
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function LocationProbe() {
  const loc = useLocation()
  return <output data-testid="loc">{loc.search}</output>
}

function wrap(ui: ReactNode, permissions: string[], path: string, pattern: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route
              path={pattern}
              element={
                <>
                  {ui}
                  <LocationProbe />
                </>
              }
            />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const MANAGE = ['inventory.view', 'warehouse.manage']
const gets = (path: string) => mock.calls.filter((c) => c.method === 'GET' && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.handler = route
})

describe('Almacenes: lista maestro-detalle (maqueta almacenes())', () => {
  it('sin ?warehouse= elige el primero por código (orden natural) y muestra sus zonas; con él, ese almacén', async () => {
    const first = wrap(<WarehouseListScreen />, MANAGE, '/warehouse/warehouses', '/warehouse/warehouses')
    // ALM-2 va antes que ALM-10
    expect(await screen.findByRole('heading', { name: /Norte/ })).toBeInTheDocument()
    expect(await screen.findByText('Todavía no tiene zonas — cree la primera')).toBeInTheDocument()
    first.unmount()

    wrap(<WarehouseListScreen />, MANAGE, `/warehouse/warehouses?warehouse=${WH1}`, '/warehouse/warehouses')
    expect(await screen.findByRole('heading', { name: /Central/ })).toBeInTheDocument()
    const zones = await screen.findByRole('list', { name: 'Zonas de este almacén' })
    expect(within(zones).getByText('1/3 posiciones')).toBeInTheDocument()
    // en la tabla (columna Dirección) y en el panel del elegido
    expect(screen.getAllByText('Calle Luna 5, San Juan, PR 00901')).toHaveLength(2)
  })

  it('clic en una fila la elige y la guarda en la URL', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseListScreen />, MANAGE, '/warehouse/warehouses', '/warehouse/warehouses')
    await screen.findByRole('heading', { name: /Norte/ })
    await user.click(screen.getByRole('cell', { name: 'Central' }))
    expect(await screen.findByRole('heading', { name: /Central/ })).toBeInTheDocument()
    expect(screen.getByTestId('loc')).toHaveTextContent(`warehouse=${WH1}`)
  })

  it('papelera de zona: deshabilitada con posiciones (tooltip con el motivo); sin posiciones da de baja', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseListScreen />, MANAGE, `/warehouse/warehouses?warehouse=${WH1}`, '/warehouse/warehouses')
    const zones = await screen.findByRole('list', { name: 'Zonas de este almacén' })
    const blocked = within(zones).getByRole('button', { name: 'No se puede dar de baja: esta zona tiene posiciones creadas' })
    expect(blocked).toHaveAttribute('aria-disabled', 'true')
    await user.click(blocked)
    expect(screen.queryByRole('dialog')).toBeNull()

    await user.click(within(zones).getByRole('button', { name: 'Dar de baja la zona B' }))
    const dialog = await screen.findByRole('dialog', { name: 'Dar de baja la zona' })
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname === `/api/v1/warehouses/${WH1}/zones/2/deactivate`)).toBe(true))
  })

  it('clic en una zona abre su modal; sin warehouse.manage no hay papelera ni "+ Nueva zona"', async () => {
    const user = userEvent.setup()
    const manage = wrap(<WarehouseListScreen />, MANAGE, `/warehouse/warehouses?warehouse=${WH1}`, '/warehouse/warehouses')
    await user.click(await screen.findByRole('button', { name: 'Editar la zona A' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar zona' })
    expect(within(dialog).getByRole('textbox', { name: /Código/ })).toHaveValue('A')
    manage.unmount()

    wrap(<WarehouseListScreen />, ['inventory.view'], `/warehouse/warehouses?warehouse=${WH1}`, '/warehouse/warehouses')
    await screen.findByRole('list', { name: 'Zonas de este almacén' })
    expect(screen.queryByRole('button', { name: /Dar de baja la zona/ })).toBeNull()
    expect(screen.queryByRole('button', { name: '+ Nueva zona' })).toBeNull()
  })

  it('filtros encima: Dirección busca en ciudad/ZIP sin acentos; no hay buscador dentro de la tabla', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseListScreen />, MANAGE, '/warehouse/warehouses', '/warehouse/warehouses')
    const table = await screen.findByRole('table', { name: 'Almacenes' })
    expect(within(table).getAllByRole('row')).toHaveLength(3)
    expect(screen.queryByPlaceholderText('Buscar…')).toBeNull()
    await user.type(screen.getByRole('searchbox', { name: 'Dirección' }), 'mayaguez')
    await waitFor(() => expect(within(table).getAllByRole('row')).toHaveLength(2))
    expect(within(table).getByRole('cell', { name: 'Norte' })).toBeInTheDocument()
    const filters = screen.getByRole('group', { name: 'Filtros' })
    for (const name of ['Código', 'Nombre', 'Tipo', 'Estatus']) expect(within(filters).getByRole('button', { name })).toBeInTheDocument()
  })

  it('alta: Ciudad o código postal busca en postal-localities; la ciudad guardada es el municipio y se llenan ZIP, Estado y País; "Activo" marcada', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseListScreen />, MANAGE, '/warehouse/warehouses', '/warehouse/warehouses')
    await user.click(await screen.findByRole('button', { name: 'Nuevo almacén' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo almacén' })
    expect(within(dialog).getByRole('switch', { name: 'Activo' })).toBeChecked()
    expect(within(dialog).getByRole('switch', { name: 'Activo' })).toBeDisabled()
    await user.type(within(dialog).getByRole('textbox', { name: /^Código/ }), 'ALM-3')
    await user.type(within(dialog).getByRole('textbox', { name: /^Nombre/ }), 'Oeste')
    await user.type(within(dialog).getByRole('combobox', { name: /Ciudad o código postal/ }), '0068')
    await waitFor(() => expect(gets('/api/v1/postal-localities').some((c) => c.url.searchParams.get('search') === '0068')).toBe(true))
    await user.click(await screen.findByRole('option', { name: '00680 · MAYAGUEZ, PR' }))
    expect(within(dialog).getByRole('combobox', { name: /Ciudad o código postal/ })).toHaveValue('Mayagüez · 00680')
    expect(within(dialog).getByRole('textbox', { name: 'País' })).toHaveValue('Puerto Rico')
    expect(within(dialog).getByRole('textbox', { name: 'Estado/Provincia' })).toHaveValue('PR')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname === '/api/v1/warehouses')).toBe(true))
    const post = mock.calls.find((c) => c.method === 'POST' && c.url.pathname === '/api/v1/warehouses')
    // Lote 16: el modo de recepción va en el alta (por defecto Con acomodo)
    expect(post?.body).toEqual({ code: 'ALM-3', name: 'Oeste', line1: null, city: 'Mayagüez', postalCode: '00680', state: 'PR', country: 'PR', receivingMode: 'PUTAWAY' })
  })

  it('Lote 16: columna Recepción con el modo de cada almacén; el alta manda el modo elegido', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseListScreen />, MANAGE, '/warehouse/warehouses', '/warehouse/warehouses')
    const table = await screen.findByRole('table', { name: 'Almacenes' })
    expect(within(table).getByRole('columnheader', { name: /Recepción/ })).toBeInTheDocument()
    expect(within(table).getByText('Directo a posición')).toBeInTheDocument()
    expect(within(table).getByText('Con acomodo')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Nuevo almacén' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo almacén' })
    await user.type(within(dialog).getByRole('textbox', { name: /^Código/ }), 'ALM-4')
    await user.type(within(dialog).getByRole('textbox', { name: /^Nombre/ }), 'Sur')
    await user.selectOptions(within(dialog).getByRole('combobox', { name: /Modo de recepción/ }), 'DIRECT')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname === '/api/v1/warehouses')).toBe(true))
    expect(mock.calls.find((c) => c.method === 'POST' && c.url.pathname === '/api/v1/warehouses')?.body).toMatchObject({ code: 'ALM-4', receivingMode: 'DIRECT' })
  })
})

describe('Ficha del almacén', () => {
  const path = `/warehouse/warehouses/${WH1}`
  const pattern = '/warehouse/warehouses/:publicId'

  it('Posiciones: paginación del servidor (skip/take), filtros al API y columna Cupo', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseDetailScreen />, MANAGE, path, pattern)
    await user.click(await screen.findByRole('tab', { name: 'Posiciones' }))
    const table = await screen.findByRole('table', { name: 'Posiciones' })
    expect(within(table).getByRole('columnheader', { name: /Cupo/ })).toBeInTheDocument()
    expect(within(table).getByRole('cell', { name: '10' })).toBeInTheDocument()
    let last = gets(`/api/v1/warehouses/${WH1}/bins`).at(-1)
    expect(last?.url.searchParams.get('skip')).toBe('0')
    expect(last?.url.searchParams.get('take')).toBe('25')
    expect(screen.getByText('1–25 de 120')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }))
    await waitFor(() => expect(gets(`/api/v1/warehouses/${WH1}/bins`).at(-1)?.url.searchParams.get('skip')).toBe('25'))

    await user.type(screen.getByRole('searchbox', { name: 'Pasillo' }), 'A')
    await waitFor(() => expect(gets(`/api/v1/warehouses/${WH1}/bins`).at(-1)?.url.searchParams.get('aisle')).toBe('A'))
    last = gets(`/api/v1/warehouses/${WH1}/bins`).at(-1)
    // cambiar un filtro vuelve a la página 1
    expect(last?.url.searchParams.get('skip')).toBe('0')
    expect(screen.queryByPlaceholderText('Buscar…')).toBeNull()
  })

  it('Posiciones: abrir y cerrar el modal no vuelve a pedir la lista; vaciar el cupo manda clearMaxCapacity', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseDetailScreen />, MANAGE, path, pattern)
    await user.click(await screen.findByRole('tab', { name: 'Posiciones' }))
    const table = await screen.findByRole('table', { name: 'Posiciones' })
    const before = gets(`/api/v1/warehouses/${WH1}/bins`).length

    await user.click(within(table).getByRole('cell', { name: 'A-01' }))
    let dialog = await screen.findByRole('dialog', { name: 'Editar' })
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(gets(`/api/v1/warehouses/${WH1}/bins`).length).toBe(before)

    await user.click(within(table).getByRole('cell', { name: 'A-01' }))
    dialog = await screen.findByRole('dialog', { name: 'Editar' })
    const capacity = within(dialog).getByRole('spinbutton', { name: /Cupo máximo/ })
    expect(capacity).toHaveValue(10)
    await user.clear(capacity)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'PATCH')).toBe(true))
    expect(mock.calls.find((c) => c.method === 'PATCH')?.body).toMatchObject({ clearMaxCapacity: true })
  })

  it('Posiciones: el cupo máximo debe ser entero mayor que cero (mensaje en español)', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseDetailScreen />, MANAGE, path, pattern)
    await user.click(await screen.findByRole('tab', { name: 'Posiciones' }))
    await user.click(await screen.findByRole('button', { name: 'Nueva posición' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva posición' })
    await user.type(within(dialog).getByRole('spinbutton', { name: /Cupo máximo/ }), '0')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El cupo máximo de la posición debe ser mayor que cero.')).toBeInTheDocument()
    expect(within(dialog).getByText('Seleccione la zona.')).toBeInTheDocument()
  })

  it('Zonas: sin botón Editar (clic en la fila), baja como ícono, columna Estatus; el 409 del código va bajo el campo', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseDetailScreen />, MANAGE, path, pattern)
    await user.click(await screen.findByRole('tab', { name: 'Zonas' }))
    const table = await screen.findByRole('table', { name: 'Zonas' })
    expect(within(table).queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(within(table).getAllByRole('button', { name: 'Dar de baja' })[0]).toHaveAttribute('title', 'Dar de baja')
    expect(within(table).getByRole('columnheader', { name: /Estatus/ })).toBeInTheDocument()

    await user.click(within(table).getByRole('cell', { name: 'Zona A' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar zona' })
    const code = within(dialog).getByRole('textbox', { name: /Código/ })
    await user.clear(code)
    await user.type(code, 'B')
    // Tipo: combobox con buscador sobre el catálogo ZoneType
    await user.click(within(dialog).getByRole('combobox', { name: /Tipo/ }))
    await user.click(await screen.findByRole('option', { name: 'Preparación' }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Ya existe una zona con ese código en el almacén.')).toBeInTheDocument()
    expect(code).toHaveAttribute('aria-invalid', 'true')
    expect(mock.calls.find((c) => c.method === 'PATCH')?.body).toEqual({ code: 'B', name: 'Zona A', zoneType: 'STAGING' })
  })

  it('Lote 16: Datos → Recepción: cambiar el modo pide confirmación con los recibos abiertos y con acomodo pendiente', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseDetailScreen />, MANAGE, path, pattern)
    const mode = await screen.findByRole('combobox', { name: /Modo de recepción/ })
    expect(screen.getByRole('group', { name: 'Recepción' })).toBeInTheDocument()
    expect(mode).toHaveValue('PUTAWAY')
    await user.selectOptions(mode, 'DIRECT')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    const dialog = await screen.findByRole('dialog', { name: '¿Cambiar el modo de recepción?' })
    expect(
      await within(dialog).findByText(
        'Los recibos nuevos de ALM-10 entrarán directo a posición. Los 3 recibos abiertos conservan su modo y los 1 recibos con acomodo pendiente siguen igual.',
      ),
    ).toBeInTheDocument()
    expect(gets('/api/v1/receipts').map((c) => c.url.searchParams.get('phase')).sort()).toEqual(['OPEN', 'PENDING_PUTAWAY'])
    expect(gets('/api/v1/receipts').every((c) => c.url.searchParams.get('warehousePublicId') === WH1)).toBe(true)
    // sin confirmar no se guarda nada
    expect(mock.calls.some((c) => c.method === 'PATCH')).toBe(false)
    await user.click(within(dialog).getByRole('button', { name: 'Cambiar el modo' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'PATCH' && c.url.pathname === `/api/v1/warehouses/${WH1}`)).toBe(true))
    const body = mock.calls.find((c) => c.method === 'PATCH')?.body as Record<string, unknown>
    expect(body).toMatchObject({ receivingMode: 'DIRECT', rowVersion: 'AQ==' })
    expect(body.defaultReceivingBinId).toBeUndefined()
    expect(body.clearDefaultReceivingBin).toBeUndefined()
  })

  it('Lote 16 (D12): la posición de recepción por defecto se muestra y al quitarla se manda clearDefaultReceivingBin (sin diálogo)', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseDetailScreen />, MANAGE, path, pattern)
    const bin = await screen.findByRole('combobox', { name: /Posición de recepción por defecto/ })
    await waitFor(() => expect(bin).toHaveValue('STG-1 · R'))
    await user.click(screen.getByRole('button', { name: 'Quitar posición' }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'PATCH')).toBe(true))
    expect(screen.queryByRole('dialog')).toBeNull()
    const body = mock.calls.find((c) => c.method === 'PATCH')?.body as Record<string, unknown>
    expect(body).toMatchObject({ clearDefaultReceivingBin: true })
    expect(body.receivingMode).toBeUndefined()
  })

  it('Datos: Ciudad/ZIP en un solo combobox; País derivado y de solo lectura', async () => {
    wrap(<WarehouseDetailScreen />, MANAGE, path, pattern)
    const city = await screen.findByRole('combobox', { name: /Ciudad o código postal/ })
    expect(city).toHaveValue('San Juan · 00901')
    expect(screen.getByRole('textbox', { name: 'País' })).toBeDisabled()
    expect(screen.queryByRole('textbox', { name: 'Código postal' })).toBeNull()
  })
})
