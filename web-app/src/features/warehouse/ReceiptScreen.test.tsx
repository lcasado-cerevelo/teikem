// Lote 13 — pantalla Recibo (`ReceiptListScreen`, maestro-detalle de la maqueta `recibo()`) sobre un fetch simulado:
// `?receipt=` elige el recibo; doble clic abre el encabezado; "Nuevo recibo" lo deja primero y elegido con la fila vacía y
// la copia recibido → esperado mientras se teclea; con documento lo esperado es de solo lectura (sin "Añadir ítem");
// confirmado, todo de solo lectura; borrar desde el modal; Avisos con sus filtros al API y sin buscador libre; Acomodo
// pendiente pide `phase=PENDING_PUTAWAY`.
// Lote 16 (recibo directo a posición): columna "Posición destino" solo en directo, pista de la sugerida, aviso de cupo,
// bloqueo de Confirmar, "Usar posiciones sugeridas", guardar al elegir, modo en el encabezado y aviso en Acomodo pendiente.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEffect } from 'react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ReceiptListScreen from './ReceiptListScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], created: false, newLines: [] as unknown[], applied: false }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.clone().text()
    const body = text ? JSON.parse(text) : null
    mock.calls.push({ method: req.method, url, body })
    const [status, out] = route(req.method, url, body)
    if (status === 204) return new Response(null, { status })
    return new Response(JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const WHD = '22222222-2222-2222-2222-222222222222'
const R4 = 'rrrrrrrr-0000-0000-0000-000000000004'
const R1 = 'rrrrrrrr-0000-0000-0000-000000000001'
const R2 = 'rrrrrrrr-0000-0000-0000-000000000002'
const R3 = 'rrrrrrrr-0000-0000-0000-000000000003'
const NEW = 'rrrrrrrr-0000-0000-0000-000000000009'
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'

const head = (over: Record<string, unknown>) => ({
  id: 1,
  publicId: R1,
  number: 'REC-0001',
  typeCode: 'BLIND',
  type: 'Ciego',
  origin: 'BLIND',
  originRef: null,
  senderName: null,
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  statusCode: 'EXPECTED',
  status: 'Esperado',
  lineCount: 0,
  expectedQty: 0,
  receivedQty: 0,
  varianceQty: 0,
  hasVariance: false,
  createdAtUtc: '2026-09-30T14:00:00',
  carrier: 'Vagón 20ft',
  reference: null,
  expectedDate: null,
  isOpen: true,
  pendingPutawayCount: 0,
  ...over,
})
const H1 = head({})
const H2 = head({
  id: 2,
  publicId: R2,
  number: 'REC-0002',
  typeCode: 'ASN',
  type: 'Aviso',
  origin: 'PO',
  originRef: 'OC-0007',
  senderName: 'MedSupply',
  statusCode: 'RECEIVING',
  status: 'Recibiendo',
  lineCount: 1,
})
const H3 = head({ id: 3, publicId: R3, number: 'REC-0003', statusCode: 'RECEIVED', status: 'Completado', isOpen: false, lineCount: 1, pendingPutawayCount: 1 })
const HNEW = head({ id: 9, publicId: NEW, number: 'REC-0009', carrier: 'Camión 1' })
// Lote 16: recibo ciego directo a posición en ALM-DIR (línea 41 sin destino; 42 en R-02 excediendo el cupo)
const H4 = head({
  id: 4,
  publicId: R4,
  number: 'REC-0004',
  warehousePublicId: WHD,
  warehouseCode: 'ALM-DIR',
  statusCode: 'RECEIVING',
  status: 'Recibiendo',
  lineCount: 2,
  receivingModeCode: 'DIRECT',
  receivingMode: 'Directo a posición',
})
const L41 = { id: 41, productPublicId: P1, sku: 'A-1', productName: 'Tornillo', trackingTypeCode: 'NONE', expectedQty: 4, receivedQty: 4, varianceQty: 0, allocatedToCrossDock: 0 }
const L42 = {
  id: 42,
  productPublicId: P1,
  sku: 'A-1',
  productName: 'Tornillo',
  trackingTypeCode: 'NONE',
  expectedQty: 8,
  receivedQty: 8,
  varianceQty: 0,
  allocatedToCrossDock: 0,
  targetBinId: 71,
  targetBinCode: 'R-02',
  targetZoneTypeCode: 'RESERVE',
  targetFreeQty: 3,
}
const WITH_TARGET_41 = { ...L41, targetBinId: 70, targetBinCode: 'RSV-A-01', targetZoneTypeCode: 'RESERVE', targetFreeQty: null }
const DIRECT_BINS = [
  { id: 70, code: 'RSV-A-01', zoneId: 5, zoneCode: 'RSV', zoneTypeCode: 'RESERVE', isActive: true },
  { id: 71, code: 'R-02', zoneId: 5, zoneCode: 'RSV', zoneTypeCode: 'RESERVE', isActive: true },
]

const detail = (h: Record<string, unknown>, lines: unknown[] = [], extra: Record<string, unknown> = {}) => ({
  header: h,
  lines,
  putawayTasks: [],
  rowVersion: 'AA==',
  canDelete: h.isOpen === true,
  ...extra,
})
const DETAILS: Record<string, unknown> = {
  [R1]: detail(H1),
  [R2]: detail(H2, [
    { id: 20, asnLineId: 5, productPublicId: P1, sku: 'A-1', productName: 'Tornillo', trackingTypeCode: 'NONE', expectedQty: 10, receivedQty: 10, varianceQty: 0, allocatedToCrossDock: 0 },
  ]),
  [R3]: detail(
    H3,
    [{ id: 30, productPublicId: P1, sku: 'A-1', productName: 'Tornillo', trackingTypeCode: 'NONE', expectedQty: null, receivedQty: 4, varianceQty: 0, allocatedToCrossDock: 0 }],
    {
      putawayTasks: [
        { id: 70, typeCode: 'PUTAWAY', type: 'Acomodo', statusCode: 'PENDING', status: 'Pendiente', sku: 'A-1', productName: 'Tornillo', quantity: 4, fromBinCode: 'STG-01', completableFromQueue: true },
      ],
    },
  ),
}

function route(method: string, url: URL, body: unknown): [number, unknown] {
  const p = url.pathname
  // Lote 16: recibo directo
  if (p === `/api/v1/receipts/${R4}/targets/suggest` && method === 'POST') {
    mock.applied = true
    return [200, { receipt: detail(H4, [WITH_TARGET_41, L42], { rowVersion: 'AC==' }), assigned: 1, withoutSuggestion: 0 }]
  }
  if (p === `/api/v1/receipts/${R4}/lines/41/target-suggestions`)
    return [200, [{ binId: 70, binCode: 'RSV-A-01', zoneCode: 'RSV', zoneTypeCode: 'RESERVE', reasonCode: 'SAME_PRODUCT', reason: 'Consolidar con el mismo producto', fits: true }]]
  if (p === `/api/v1/receipts/${R4}/lines/42/target-suggestions`)
    return [200, [{ binId: 71, binCode: 'R-02', zoneCode: 'RSV', zoneTypeCode: 'RESERVE', reasonCode: 'SAME_PRODUCT', reason: 'Consolidar con el mismo producto', fits: false }]]
  if (p === `/api/v1/receipts/${R4}/lines/41` && method === 'PUT') {
    const b = body as { targetBinId?: number }
    const bin = DIRECT_BINS.find((x) => x.id === b.targetBinId)
    return [200, detail(H4, [{ ...L41, targetBinId: bin?.id ?? null, targetBinCode: bin?.code ?? null, targetZoneTypeCode: 'RESERVE' }, L42], { rowVersion: 'AB==' })]
  }
  if (p === `/api/v1/receipts/${R4}`) return [200, mock.applied ? detail(H4, [WITH_TARGET_41, L42]) : detail(H4, [L41, L42])]
  if (p === `/api/v1/warehouses/${WHD}/zones`)
    return [
      200,
      [
        { id: 5, code: 'RSV', zoneTypeCode: 'RESERVE', isActive: true },
        { id: 6, code: 'STG', zoneTypeCode: 'STAGING', isActive: true },
      ],
    ]
  if (p === `/api/v1/warehouses/${WHD}/bins`) {
    const ids = url.searchParams.getAll('binIds').map(Number)
    const items = ids.length > 0 ? DIRECT_BINS.filter((b) => ids.includes(b.id)) : DIRECT_BINS
    return [200, { total: items.length, skip: 0, take: 50, items }]
  }
  if (p === '/api/v1/receipts' && method === 'POST') {
    mock.created = true
    return [200, detail(HNEW)]
  }
  if (p === '/api/v1/receipts') {
    const items = url.searchParams.get('phase') === 'PENDING_PUTAWAY' ? [H3] : [H1, H2, H3, H4]
    return [200, { total: items.length, skip: 0, take: 25, items }]
  }
  if (p === `/api/v1/receipts/${NEW}/lines` && method === 'POST') {
    const b = body as { productPublicId: string; receivedQty: number; expectedQty?: number }
    mock.newLines = [
      { id: 50, productPublicId: b.productPublicId, sku: 'A-1', productName: 'Tornillo', trackingTypeCode: 'NONE', expectedQty: b.expectedQty ?? null, receivedQty: b.receivedQty, varianceQty: 0, allocatedToCrossDock: 0 },
    ]
    return [200, detail(head({ ...HNEW, statusCode: 'RECEIVING', status: 'Recibiendo', lineCount: 1 }), mock.newLines)]
  }
  if (p.startsWith('/api/v1/receipts/') && method === 'DELETE') return [204, null]
  if (p.startsWith('/api/v1/receipts/')) {
    const id = p.split('/')[4]
    if (id === NEW) return [200, detail(HNEW, mock.newLines)]
    return DETAILS[id] ? [200, DETAILS[id]] : [404, { title: 'Recibo no encontrado.', code: 'not_found', status: 404 }]
  }
  if (p === '/api/v1/asns') return [200, []]
  if (p === '/api/v1/warehouses')
    return [
      200,
      [
        { id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true, receivingModeCode: 'PUTAWAY' },
        { id: 2, publicId: WHD, code: 'ALM-DIR', name: 'Directo', isActive: true, receivingModeCode: 'DIRECT' },
      ],
    ]
  if (p === `/api/v1/warehouses/${WH}`) return [200, { id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) return [200, { total: 0, skip: 0, take: 50, items: [] }]
  if (p === '/api/v1/products')
    return [200, { total: 1, skip: 0, take: 20, items: [{ id: 1, publicId: P1, sku: 'A-1', name: 'Tornillo', isOwn: true, trackingTypeCode: 'NONE', isActive: true }] }]
  if (p === '/api/v1/status/ReceiptStatus')
    return [
      200,
      [
        { code: 'EXPECTED', label: 'Esperado', color: '#9CA3AF', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 },
        { code: 'RECEIVING', label: 'Recibiendo', color: '#3B82F6', stageKind: 'PIPELINE', isEnabled: true, sortOrder: 2 },
        { code: 'RECEIVED', label: 'Completado', color: '#10B981', stageKind: 'PIPELINE', isEnabled: true, sortOrder: 4 },
      ],
    ]
  return [200, []]
}

// dirección actual (la escribe un efecto, no el render)
const probe = { location: '' }
function LocationProbe() {
  const l = useLocation()
  useEffect(() => {
    probe.location = `${l.pathname}${l.search}`
  }, [l])
  return null
}

function wrap(path = '/warehouse/receipts', permissions = ['inventory.view', 'warehouse.receive', 'warehouse.manage']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route path="/warehouse/receipts" element={<ReceiptListScreen />} />
          </Routes>
          <LocationProbe />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const rowButton = (number: string) => screen.findByRole('button', { name: new RegExp(`^${number}`) })
const calls = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.created = false
  mock.newLines = []
  mock.applied = false
  probe.location = ''
})

describe('Recibo: maestro-detalle', () => {
  it('?receipt= elige ese recibo; con orden de compra lo esperado es de solo lectura y no hay fila para añadir', async () => {
    wrap(`/warehouse/receipts?receipt=${R2}`)
    expect(await rowButton('REC-0002')).toHaveAttribute('aria-current', 'true')
    expect(await screen.findByText('Origen · Orden de compra: OC-0007')).toBeInTheDocument()
    expect(await screen.findByRole('textbox', { name: 'Recibido de la línea 1' })).toHaveValue('10')
    expect(screen.queryByRole('textbox', { name: 'Esperado de la línea 1' })).toBeNull()
    expect(screen.queryByRole('combobox', { name: 'Producto de la línea 1' })).toBeNull()
    expect(screen.queryByText('Línea nueva')).toBeNull()
    expect(screen.getByRole('button', { name: /Confirmar recibo/ })).toBeEnabled()
  })

  it('sin ?receipt= elige el primero; clic en otra fila lo pone en la URL', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await rowButton('REC-0001')).toHaveAttribute('aria-current', 'true')
    await user.click(await rowButton('REC-0002'))
    await waitFor(() => expect(probe.location).toContain(`receipt=${R2}`))
    expect(await rowButton('REC-0002')).toHaveAttribute('aria-current', 'true')
  })

  it('doble clic en la fila abre el modal del encabezado', async () => {
    const user = userEvent.setup()
    wrap()
    await user.dblClick(await rowButton('REC-0001'))
    const dialog = await screen.findByRole('dialog', { name: 'Encabezado del recibo REC-0001' })
    expect(within(dialog).getByRole('textbox', { name: 'Transporte' })).toHaveValue('Vagón 20ft')
  })

  it('confirmado: líneas de solo lectura, Confirmar deshabilitado con su motivo y las tareas de acomodo', async () => {
    wrap(`/warehouse/receipts?receipt=${R3}`)
    expect(await screen.findByText('El recibo ya está confirmado.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Confirmar recibo/ })).toBeDisabled()
    expect(screen.queryByRole('textbox', { name: 'Recibido de la línea 1' })).toBeNull()
    expect(screen.getByRole('heading', { name: /Tareas de acomodo/ })).toBeInTheDocument()
    expect(screen.getByText('STG-01')).toBeInTheDocument()
  })

  it('"Nuevo recibo": queda primero y elegido con la fila vacía; lo recibido se copia al esperado mientras se teclea', async () => {
    const user = userEvent.setup()
    wrap()
    await rowButton('REC-0001')
    await user.click(screen.getByRole('button', { name: 'Nuevo recibo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo recibo' })
    await user.click(within(dialog).getByRole('combobox', { name: /^Almacén/ }))
    await user.click(await within(dialog).findByRole('option', { name: /ALM-01/ }))
    await user.type(within(dialog).getByRole('textbox', { name: 'Transporte' }), 'Camión 1')
    await user.click(within(dialog).getByRole('button', { name: 'Crear recibo' }))

    await waitFor(() => expect(calls('POST', '/api/v1/receipts')).toHaveLength(1))
    expect(calls('POST', '/api/v1/receipts')[0].body).toMatchObject({ warehousePublicId: WH, type: 'BLIND', carrier: 'Camión 1', asnId: null })
    expect((calls('POST', '/api/v1/receipts')[0].body as { lines?: unknown }).lines).toBeUndefined()

    // primero en la lista (aunque el GET no lo traiga) y elegido
    const first = (await screen.findAllByRole('button', { name: /^REC-/ }))[0]
    expect(first).toHaveAccessibleName(/^REC-0009/)
    expect(first).toHaveAttribute('aria-current', 'true')
    await waitFor(() => expect(probe.location).toContain(`receipt=${NEW}`))
    expect(await screen.findByText('Línea nueva')).toBeInTheDocument()

    // producto + recibido: el esperado vacío sigue a lo recibido; al salir se guarda la fila y aparece otra vacía
    await user.click(screen.getByRole('combobox', { name: 'Producto de la línea 1' }))
    await user.click(await screen.findByRole('option', { name: /A-1/ }))
    const received = screen.getByRole('textbox', { name: 'Recibido de la línea 1' })
    await user.type(received, '12')
    expect(screen.getByRole('textbox', { name: 'Esperado de la línea 1' })).toHaveValue('12')
    await user.tab()
    await waitFor(() => expect(calls('POST', `/api/v1/receipts/${NEW}/lines`)).toHaveLength(1))
    expect(calls('POST', `/api/v1/receipts/${NEW}/lines`)[0].body).toEqual({ productPublicId: P1, receivedQty: 12, expectedQty: 12 })
    expect(await screen.findByRole('combobox', { name: 'Producto de la línea 2' })).toBeInTheDocument()
  })

  it('borrar desde el modal del encabezado (con confirmación)', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/receipts?receipt=${R1}`)
    await waitFor(() => expect(probe.location).toContain(`receipt=${R1}`))
    await user.dblClick(await rowButton('REC-0001'))
    const dialog = await screen.findByRole('dialog', { name: 'Encabezado del recibo REC-0001' })
    await user.click(within(dialog).getByRole('button', { name: 'Borrar recibo' }))
    const confirm = await screen.findByRole('dialog', { name: 'Borrar recibo' })
    expect(confirm).toHaveTextContent('la orden podrá recibirse de nuevo')
    await user.click(within(confirm).getByRole('button', { name: 'Borrar recibo' }))
    await waitFor(() => expect(calls('DELETE', `/api/v1/receipts/${R1}`)).toHaveLength(1))
    await waitFor(() => expect(probe.location).not.toContain('receipt='))
  })

  it('sin warehouse.receive: sin "Nuevo recibo", sin Confirmar y sin campos', async () => {
    wrap(`/warehouse/receipts?receipt=${R2}`, ['inventory.view'])
    expect(await screen.findByText('Origen · Orden de compra: OC-0007')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Nuevo recibo' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Confirmar recibo/ })).toBeNull()
    expect(screen.queryByRole('textbox', { name: 'Recibido de la línea 1' })).toBeNull()
  })
})

describe('Recibo: pestañas', () => {
  it('Avisos de llegada: filtros al API (referencia, llegada esperada) y sin buscador libre', async () => {
    const user = userEvent.setup()
    const { container } = wrap('/warehouse/receipts?tab=asns')
    await waitFor(() => expect(calls('GET', '/api/v1/asns').length).toBeGreaterThan(0))
    expect(container.querySelector('.qbox')).toBeNull()
    expect(screen.getByRole('combobox', { name: 'Cliente dueño' })).toBeInTheDocument()
    await user.type(screen.getByRole('searchbox', { name: 'Referencia' }), 'AX-1')
    await waitFor(() => expect(calls('GET', '/api/v1/asns').some((c) => c.url.searchParams.get('reference') === 'AX-1')).toBe(true))
    const range = screen.getByRole('group', { name: 'Llegada esperada' })
    fireEvent.change(range.querySelectorAll('input')[0], { target: { value: '2026-09-01' } })
    await waitFor(() => expect(calls('GET', '/api/v1/asns').some((c) => c.url.searchParams.get('expectedFrom') === '2026-09-01')).toBe(true))
    expect(calls('GET', '/api/v1/asns').every((c) => !c.url.searchParams.has('search'))).toBe(true)
  })

  it("'Acomodo pendiente' lista recibos con phase=PENDING_PUTAWAY y a la derecha sus tareas", async () => {
    wrap('/warehouse/receipts?tab=putaway')
    expect(await screen.findByRole('tab', { name: 'Acomodo pendiente' })).toHaveAttribute('aria-selected', 'true')
    await waitFor(() => expect(calls('GET', '/api/v1/receipts').length).toBeGreaterThan(0))
    expect(calls('GET', '/api/v1/receipts').every((c) => c.url.searchParams.get('phase') === 'PENDING_PUTAWAY')).toBe(true)
    expect(await screen.findByRole('heading', { name: /Tareas de acomodo · REC-0003/ })).toBeInTheDocument()
    expect(await screen.findByText('STG-01')).toBeInTheDocument()
  })

  it('cambiar de pestaña quita el recibo elegido de la URL', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/receipts?receipt=${R2}`)
    await rowButton('REC-0002')
    await user.click(screen.getByRole('tab', { name: 'Avisos de llegada' }))
    await waitFor(() => expect(probe.location).toBe('/warehouse/receipts?tab=asns'))
  })
})

describe('Lote 16: recibo directo a posición', () => {
  it('solo en directo: chip, columna "Posición destino", pista de la sugerida, aviso de cupo y Confirmar bloqueado', async () => {
    wrap(`/warehouse/receipts?receipt=${R4}`)
    expect(await screen.findByRole('heading', { name: /REC-0004.*Directo a posición/ })).toBeInTheDocument()
    const table = await screen.findByRole('table', { name: 'Líneas del recibo' })
    expect(within(table).getByRole('columnheader', { name: /Posición destino/ })).toBeInTheDocument()
    expect(await screen.findByRole('combobox', { name: 'Posición destino de la línea 1' })).toHaveValue('')
    expect(await screen.findByText('Sugerida: RSV-A-01 · Consolidar con el mismo producto')).toBeInTheDocument()
    expect(screen.getByText('Excede el cupo de R-02: caben 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Confirmar recibo/ })).toBeDisabled()
    expect(screen.getByText('Falta la posición destino en 1 línea(s).')).toBeInTheDocument()
    // en la lista, la etiqueta "Directo"
    expect(await rowButton('REC-0004')).toHaveTextContent('Directo')
  })

  it('con acomodo no hay columna "Posición destino" ni "Usar posiciones sugeridas"', async () => {
    wrap(`/warehouse/receipts?receipt=${R2}`)
    await screen.findByRole('textbox', { name: 'Recibido de la línea 1' })
    expect(screen.queryByRole('columnheader', { name: /Posición destino/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Usar posiciones sugeridas' })).toBeNull()
  })

  it('"Usar posiciones sugeridas" manda el rowVersion, avisa cuántas se asignaron y desbloquea Confirmar', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/receipts?receipt=${R4}`)
    await user.click(await screen.findByRole('button', { name: 'Usar posiciones sugeridas' }))
    await waitFor(() => expect(calls('POST', `/api/v1/receipts/${R4}/targets/suggest`)).toHaveLength(1))
    expect(calls('POST', `/api/v1/receipts/${R4}/targets/suggest`)[0].body).toEqual({ rowVersion: 'AA==' })
    expect(await screen.findByText('Se asignó posición a 1 línea(s); 0 sin sugerencia.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Posición destino de la línea 1' })).toHaveValue('RSV-A-01 · RSV'))
    expect(screen.getByRole('button', { name: /Confirmar recibo/ })).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Usar posiciones sugeridas' })).toBeNull()
  })

  it('elegir la posición destino la guarda (PUT con targetBinId); la sugerida va primero', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/receipts?receipt=${R4}`)
    await screen.findByText('Sugerida: RSV-A-01 · Consolidar con el mismo producto')
    const box = screen.getByRole('combobox', { name: 'Posición destino de la línea 1' })
    await user.click(box)
    const list = await waitFor(() => {
      const el = document.getElementById(box.getAttribute('aria-controls') ?? '')
      if (!el) throw new Error('sin lista')
      return el
    })
    const first = (await within(list).findAllByRole('option'))[0]
    expect(first).toHaveTextContent('RSV-A-01')
    expect(first).toHaveTextContent('Sugerida')
    await user.click(first)
    await waitFor(() => expect(calls('PUT', `/api/v1/receipts/${R4}/lines/41`)).toHaveLength(1))
    expect(calls('PUT', `/api/v1/receipts/${R4}/lines/41`)[0].body).toEqual({ targetBinId: 70 })
    await waitFor(() => expect(screen.queryByText('Falta la posición destino en 1 línea(s).')).toBeNull())
    // las zonas de recepción no se ofrecen: las posiciones se piden solo de las zonas de guardado
    const binGets = calls('GET', `/api/v1/warehouses/${WHD}/bins`).filter((c) => !c.url.searchParams.has('binIds'))
    expect(binGets.length).toBeGreaterThan(0)
    expect(binGets.every((c) => c.url.searchParams.getAll('zoneIds').join() === '5')).toBe(true)
  })

  it('encabezado: en un almacén directo el modo nace "Directo a posición" y se oculta la posición de recepción', async () => {
    const user = userEvent.setup()
    wrap()
    await rowButton('REC-0001')
    await user.click(screen.getByRole('button', { name: 'Nuevo recibo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo recibo' })
    expect(within(dialog).getByRole('combobox', { name: /Posición de recepción/ })).toBeInTheDocument()
    await user.click(within(dialog).getByRole('combobox', { name: /^Almacén/ }))
    await user.click(await within(dialog).findByRole('option', { name: /ALM-DIR/ }))
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /Modo de recepción/ })).toHaveValue('DIRECT'))
    expect(within(dialog).queryByRole('combobox', { name: /Posición de recepción/ })).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Crear recibo' }))
    await waitFor(() => expect(calls('POST', '/api/v1/receipts')).toHaveLength(1))
    expect(calls('POST', '/api/v1/receipts')[0].body).toMatchObject({ warehousePublicId: WHD, receivingMode: 'DIRECT', stagingBinId: null })
  })

  it('encabezado de un recibo abierto: cambiar el modo manda solo receivingMode en el PATCH', async () => {
    const user = userEvent.setup()
    wrap(`/warehouse/receipts?receipt=${R4}`)
    await user.click(await screen.findByRole('button', { name: 'Editar el encabezado del recibo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Encabezado del recibo REC-0004' })
    const mode = within(dialog).getByRole('combobox', { name: /Modo de recepción/ })
    expect(mode).toHaveValue('DIRECT')
    expect(within(dialog).queryByRole('combobox', { name: /Posición de recepción/ })).toBeNull()
    await user.selectOptions(mode, 'PUTAWAY')
    expect(within(dialog).getByRole('combobox', { name: /Posición de recepción/ })).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(calls('PATCH', `/api/v1/receipts/${R4}`)).toHaveLength(1))
    expect(calls('PATCH', `/api/v1/receipts/${R4}`)[0].body).toEqual({ receivingMode: 'PUTAWAY', rowVersion: 'AA==' })
  })

  it("'Acomodo pendiente' con un almacén directo filtrado: aviso de que ahí solo aparecen recibos anteriores o con cruce", async () => {
    const user = userEvent.setup()
    wrap('/warehouse/receipts?tab=putaway')
    await screen.findByRole('heading', { name: /Tareas de acomodo · REC-0003/ })
    expect(screen.queryByText(/recibe directo a posición/)).toBeNull()
    await user.click(screen.getByRole('combobox', { name: /^Almacén/ }))
    await user.click(await screen.findByRole('option', { name: /ALM-DIR/ }))
    expect(
      await screen.findByText('ALM-DIR recibe directo a posición: aquí solo aparecen recibos anteriores al cambio o con cruce de muelle.'),
    ).toBeInTheDocument()
  })
})
