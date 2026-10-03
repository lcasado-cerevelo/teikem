// Lote 14 (P8) — 'Conteo cíclico' en dos paneles (`CycleCountListScreen`) sobre un fetch simulado: lista paginada con el
// total (estatus, posición, origen y asignado), el conteo elegido con todas sus líneas y lo esperado, captura en la fila,
// escáner (código → línea → cantidad), "Confirmar conteo y ajustar" en un paso, asignar con ícono (tarea COUNT), eliminar
// un pendiente, a ciegas sin warehouse.count, sin pestaña 'Tareas de conteo' y "Conteo de lo cambiado" con vista previa
// y `problem`.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import CycleCountListScreen from './CycleCountListScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
type Json = Record<string, unknown>
const mock = vi.hoisted(() => ({ calls: [] as Call[], problem: null as string | null, lines: [] as Json[], status: 'OPEN', blind: false, createProblem: null as string | null }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.clone().text()
    const body = text ? JSON.parse(text) : null
    mock.calls.push({ method: req.method, url, body })
    const [status, out] = route(req.method, url, body)
    return new Response(out === undefined ? null : JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const PRODUCT = 'aaaaaaaa-0000-0000-0000-000000000001'
const COUNT1 = {
  id: 1,
  number: 'CC-00001',
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  statusCode: 'OPEN',
  status: 'Pendiente',
  lineCount: 2,
  countedLines: 0,
  binCount: 1,
  binCode: 'A-01',
  zoneCode: 'A',
  originCode: 'CHANGES',
  origin: 'Lo cambiado',
  taskId: 11,
  assignedToName: 'Juan Pérez',
  assignedToUserId: 7,
  createdAtUtc: '2026-09-30T14:00:00',
}
const COUNT2 = {
  ...COUNT1,
  id: 2,
  number: 'CC-00002',
  statusCode: 'RECONCILED_VARIANCE',
  status: 'Diferencia',
  binCode: 'B-01',
  zoneCode: 'B',
  originCode: 'MANUAL',
  origin: 'Selección',
  taskId: 12,
  assignedToName: null,
  assignedToUserId: null,
}
const BASE_LINES: Json[] = [
  { id: 101, binId: 10, binCode: 'A-01', sku: 'A-1', productName: 'Tornillo', trackingTypeCode: 'NONE', barcode: '7500001', systemQty: 5, countedQty: null },
  { id: 102, binId: 10, binCode: 'A-01', sku: 'B-2', productName: 'Tuerca', trackingTypeCode: 'NONE', systemQty: 2, countedQty: null },
]

function detail(id: number): Json {
  const count = id === 2 ? COUNT2 : id === 9 ? { ...COUNT1, id: 9, number: 'CC-00009', binCode: 'C-01' } : { ...COUNT1, statusCode: mock.status, status: mock.status === 'OPEN' ? 'Pendiente' : 'Diferencia' }
  const lines = mock.blind ? mock.lines.map((l) => ({ ...l, systemQty: null, varianceQty: null })) : mock.lines
  return { count, lines, rowVersion: `RV${mock.calls.length}`, isBlind: mock.blind }
}

function preview(): Json {
  const lines = mock.lines.map((l) => {
    const counted = l.countedQty as number | null
    const current = l.systemQty as number
    const adj = counted == null ? 0 : counted - current
    return { lineId: l.id, binId: l.binId, binCode: l.binCode, sku: l.sku, productName: l.productName, trackingTypeCode: 'NONE', systemQty: current, currentQty: current, reservedQty: 0, countedQty: counted, isPending: counted == null, adjustmentQty: adj, resultingQty: current + adj, movements: adj === 0 ? 0 : 1, error: null }
  })
  const pending = lines.filter((l) => l.isPending).length
  const movements = lines.reduce((a, l) => a + l.movements, 0)
  return {
    lines,
    totals: { lines: lines.length, pendingLines: pending, linesWithDifference: lines.filter((l) => l.adjustmentQty !== 0).length, movements, errorLines: 0, matches: pending === 0 && movements === 0, resultStatusCode: pending > 0 ? null : movements > 0 ? 'RECONCILED_VARIANCE' : 'RECONCILED' },
    blockingError: null,
    rowVersion: 'RVP',
  }
}

function route(method: string, url: URL, body: unknown): [number, unknown] {
  const p = url.pathname
  if (p === '/api/v1/cycle-counts/page') return [200, { total: 42, skip: Number(url.searchParams.get('skip') ?? 0), take: 25, items: [COUNT1, COUNT2] }]
  if (p === '/api/v1/cycle-counts/changes-preview')
    return [
      200,
      {
        fromUtc: '2026-09-30T04:00:00Z',
        toUtc: '2026-09-30T16:00:00Z',
        movements: 7,
        positions: mock.problem ? 0 : 3,
        positionsWithOpenCount: 1,
        positionsInactive: 0,
        positionsEmpty: 0,
        lines: mock.problem ? 0 : 4,
        maxPositions: 200,
        warehousePublicId: WH,
        warehouseCode: 'ALM-01',
        lastChangesToUtc: null,
        problem: mock.problem,
      },
    ]
  if (p === '/api/v1/cycle-counts' && method === 'POST') {
    if (mock.createProblem && (body as { productPublicIds?: unknown }).productPublicIds)
      return [400, { title: 'Hay errores de validación.', code: 'validation', errors: { filters: [mock.createProblem] } }]
    return [200, { count: { ...COUNT1, id: 15, number: 'CC-00015', originCode: 'PRODUCT', origin: 'Producto' }, lines: [{ id: 301 }, { id: 302 }], rowVersion: 'RV15', isBlind: false }]
  }
  if (p === '/api/v1/products') return [200, { total: 1, skip: 0, take: 50, items: [{ id: 1, publicId: PRODUCT, sku: 'TORN-01', name: 'Tornillo', isOwn: true, isActive: true, trackingTypeCode: 'NONE' }] }]
  if (p === '/api/v1/cycle-counts/from-changes' && method === 'POST') return [200, { counts: [{ ...COUNT1, id: 9, number: 'CC-00009' }] }]
  const m = /^\/api\/v1\/cycle-counts\/(\d+)(\/[\w-]+)?$/.exec(p)
  if (m) {
    const id = Number(m[1])
    // Lote F12: vista previa contra la existencia actual (aquí igual a la foto)
    if (m[2] === '/reconcile-preview') return [200, preview()]
    if (method === 'DELETE') return [204, undefined]
    if (m[2] === '/lines' && method === 'PUT') {
      const item = (body as { lines: { lineId: number; countedQty: number | null }[] }).lines[0]
      mock.lines = mock.lines.map((l) => (l.id === item.lineId ? { ...l, countedQty: item.countedQty, varianceQty: item.countedQty == null ? null : item.countedQty - (l.systemQty as number) } : l))
      return [200, detail(id)]
    }
    if (m[2] === '/reconcile') {
      mock.status = 'RECONCILED_VARIANCE'
      mock.lines = mock.lines.map((l) => ({ ...l, adjustedQty: (l.countedQty as number) - (l.systemQty as number) }))
      return [200, detail(id)]
    }
    return [200, detail(id)]
  }
  if (/^\/api\/v1\/warehouse-tasks\/\d+\/assign$/.test(p)) return [200, { id: 11 }]
  if (p === '/api/v1/users') return [200, [{ id: 7, fullName: 'Juan Pérez', isActive: true }, { id: 8, fullName: 'Ana Ruiz', isActive: true }]]
  if (p === '/api/v1/warehouses') return [200, [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]]
  if (p === `/api/v1/warehouses/${WH}/zones`) return [200, [{ id: 3, code: 'A', name: 'Zona A', isActive: true }]]
  if (p === '/api/v1/status/CycleCountStatus')
    return [
      200,
      [
        { code: 'OPEN', label: 'Pendiente', colorHex: '#9CA3AF', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 },
        { code: 'COUNTED', label: 'Contado', colorHex: '#F59E0B', stageKind: 'PIPELINE', isEnabled: true, sortOrder: 2 },
        { code: 'RECONCILED', label: 'Concordancia', colorHex: '#059669', stageKind: 'TERMINAL', isEnabled: true, sortOrder: 3 },
        { code: 'RECONCILED_VARIANCE', label: 'Diferencia', colorHex: '#F59E0B', stageKind: 'TERMINAL', isEnabled: true, sortOrder: 4 },
      ],
    ]
  if (p === '/api/v1/catalogs/CycleCountOrigin')
    return [
      200,
      [
        { code: 'MANUAL', label: 'Selección', sortOrder: 1 },
        { code: 'CHANGES', label: 'Lo cambiado', sortOrder: 2 },
      ],
    ]
  return [200, []]
}

function Where() {
  const { search } = useLocation()
  return <p data-testid="where">{search}</p>
}

function wrap(permissions: string[], path = '/warehouse/cycle-counts') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <Routes>
            <Route
              path="/warehouse/cycle-counts"
              element={
                <>
                  <CycleCountListScreen />
                  <Where />
                </>
              }
            />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const COUNTER = ['inventory.view', 'warehouse.count', 'warehouse.count.capture']
const calls = (method: string, path: string | RegExp) =>
  mock.calls.filter((c) => c.method === method && (typeof path === 'string' ? c.url.pathname === path : path.test(c.url.pathname)))
const detailPanel = () => document.querySelector('.cc-detail') as HTMLElement
const listPanel = () => document.querySelector('.cc-list') as HTMLElement
/** El panel del conteo ya cargado (mientras carga es otro elemento: el del indicador). */
const loadedDetail = () =>
  waitFor(() => {
    const el = detailPanel()
    expect(el?.querySelector('.cc-lines')).not.toBeNull()
    return el
  })

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.problem = null
  mock.lines = BASE_LINES.map((l) => ({ ...l }))
  mock.status = 'OPEN'
  mock.blind = false
  mock.createProblem = null
  localStorage.clear()
})

describe('Conteo cíclico en dos paneles', () => {
  it('lista paginada con el total (estatus, posición, origen, asignado) y el primero elegido con todas sus líneas y lo esperado; sin pestaña Tareas de conteo', async () => {
    wrap(COUNTER)
    const list = await waitFor(() => {
      const el = listPanel()
      expect(el).not.toBeNull()
      return el
    })
    await within(list).findByText('A-01')
    expect(within(list).getByText('42')).toBeInTheDocument()
    expect(within(list).getByText('Pendiente')).toBeInTheDocument()
    expect(within(list).getByText('Diferencia')).toBeInTheDocument()
    expect(within(list).getByText('Lo cambiado')).toBeInTheDocument()
    expect(within(list).getByText('Asignado a Juan Pérez')).toBeInTheDocument()
    expect(within(list).getByText('Sin asignar')).toBeInTheDocument()
    const req = calls('GET', '/api/v1/cycle-counts/page')[0]
    expect(req.url.searchParams.get('skip')).toBe('0')
    expect(req.url.searchParams.get('take')).toBe('25')
    // D9: todas las líneas desde el inicio, con lo esperado
    const panel = await loadedDetail()
    expect(await within(panel).findByText('Tornillo')).toBeInTheDocument()
    expect(within(panel).getByText('Tuerca')).toBeInTheDocument()
    expect(within(panel).getByRole('columnheader', { name: /Esperado/ })).toBeInTheDocument()
    expect(within(panel).getByText('CC-00001')).toBeInTheDocument()
    // D10: ya no hay pestaña 'Tareas de conteo'
    expect(screen.queryByRole('tab', { name: 'Tareas de conteo' })).toBeNull()
    // "Confirmar" no se puede con líneas sin contar (mismo texto del 422 del API)
    expect(within(panel).getByRole('button', { name: /Confirmar conteo y ajustar/ })).toBeDisabled()
    expect(within(panel).getByText('Faltan 2 línea(s) por contar.')).toBeInTheDocument()
  })

  it('los filtros van al API y vuelven a la página 1; clic en otro conteo lo elige en ?count=', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    const list = await waitFor(() => listPanel())
    await within(list).findByText('B-01')
    await user.click(screen.getByRole('button', { name: 'Origen' }))
    await user.click(await screen.findByRole('option', { name: /Lo cambiado/ }))
    await waitFor(() => expect(calls('GET', '/api/v1/cycle-counts/page').at(-1)!.url.searchParams.getAll('origins')).toEqual(['CHANGES']))
    await user.click(within(list).getByText('B-01'))
    expect(screen.getByTestId('where').textContent).toBe('?count=2')
    // cerrado: aviso y enlace a los ajustes del Kárdex; sin captura ni Confirmar
    const panel = await loadedDetail()
    expect(await within(panel).findByRole('link', { name: 'Ver los ajustes en el Kárdex' })).toHaveAttribute('href', '/warehouse/kardex?refEntity=CYCLE_COUNT&refId=2')
    expect(within(panel).queryByRole('button', { name: /Confirmar conteo y ajustar/ })).toBeNull()
    expect(within(panel).queryByRole('textbox', { name: /Contado de la línea/ })).toBeNull()
  })

  it('Lote 15: ?status=RECONCILED_VARIANCE&warehousePublicIds= (franja "Almacén hoy") abre filtrado por Diferencia y almacén', async () => {
    wrap(COUNTER, `/warehouse/cycle-counts?status=RECONCILED_VARIANCE&warehousePublicIds=${WH}`)
    await waitFor(() => expect(calls('GET', '/api/v1/cycle-counts/page').length).toBeGreaterThan(0))
    const first = calls('GET', '/api/v1/cycle-counts/page')[0].url.searchParams
    expect(first.getAll('status')).toEqual(['RECONCILED_VARIANCE'])
    expect(first.getAll('warehousePublicIds')).toEqual([WH])
    // la barra de filtros muestra lo elegido
    expect(await screen.findByRole('button', { name: /^Estatus/ })).toHaveTextContent('Diferencia')
  })

  it('captura en la fila: Enter guarda (PUT /lines con el rowVersion) y pasa a la línea siguiente; un texto que no es número se avisa sin guardar', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    const first = await screen.findByRole('textbox', { name: 'Contado de la línea 1 (A-1)' })
    await user.click(first)
    await user.keyboard('6{Enter}')
    const second = screen.getByRole('textbox', { name: 'Contado de la línea 2 (B-2)' })
    expect(second).toHaveFocus()
    await waitFor(() => expect(calls('PUT', '/api/v1/cycle-counts/1/lines')).toHaveLength(1))
    const put = calls('PUT', '/api/v1/cycle-counts/1/lines')[0].body as { rowVersion: string; lines: unknown[] }
    expect(put.lines).toEqual([{ lineId: 101, countedQty: 6, serialNumbers: null }])
    expect(put.rowVersion).toMatch(/^RV/)
    // varianza al vuelo con el valor guardado y (Lote F12) el ajuste de la vista previa recalculada tras guardar
    await waitFor(() => expect(within(detailPanel()).getAllByText('+1')).toHaveLength(2))
    await user.type(second, 'dos')
    await user.tab()
    expect(await within(detailPanel()).findByText('Escriba un número.')).toBeInTheDocument()
    expect(calls('PUT', '/api/v1/cycle-counts/1/lines')).toHaveLength(1)
  })

  it('escáner: el código de barras lleva a la línea y pide la cantidad (Enter guarda); un código ajeno se avisa', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    const scan = await screen.findByRole('combobox', { name: 'Escanear o buscar producto' })
    await user.type(scan, '7500001{Enter}')
    const dialog = await screen.findByRole('dialog', { name: 'Contar A-1 · Tornillo' })
    const qty = within(dialog).getByRole('textbox', { name: 'Cantidad contada' })
    expect(qty).toHaveFocus()
    expect(within(dialog).getByText(/Esperado: 5/)).toBeInTheDocument()
    await user.type(qty, '4{Enter}')
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect((calls('PUT', '/api/v1/cycle-counts/1/lines')[0].body as { lines: unknown[] }).lines).toEqual([{ lineId: 101, countedQty: 4, serialNumbers: null }])
    // la línea escaneada queda resaltada
    expect(document.querySelector('.cc-lines .cc-hit')).not.toBeNull()
    await user.type(scan, 'NO-EXISTE{Enter}')
    expect(
      await screen.findByText('Ese código no está en este conteo. Use "Agregar lo encontrado" si el producto está en la posición.'),
    ).toBeInTheDocument()
  })

  it('"Confirmar conteo y ajustar" en un paso desde Pendiente (pasa por la vista previa) termina en Diferencia', async () => {
    const user = userEvent.setup()
    mock.lines = BASE_LINES.map((l, i) => ({ ...l, countedQty: i === 0 ? 6 : 2, varianceQty: i === 0 ? 1 : 0 }))
    wrap(COUNTER)
    const panel = await loadedDetail()
    const button = await within(panel).findByRole('button', { name: /Confirmar conteo y ajustar/ })
    await waitFor(() => expect(button).toBeEnabled())
    await user.click(button)
    const dialog = await screen.findByRole('dialog', { name: 'Confirmar conteo y ajustar' })
    // Lote F12: la vista previa del efecto (1 movimiento) y se confirma con su rowVersion
    expect(await within(dialog).findByText('Al confirmar se asentarán 1 movimiento(s) en el Kárdex y el conteo terminará en Diferencia.')).toBeInTheDocument()
    const ok = within(dialog).getByRole('button', { name: 'Confirmar conteo y ajustar' })
    await waitFor(() => expect(ok).toBeEnabled())
    await user.click(ok)
    await waitFor(() => expect(calls('POST', '/api/v1/cycle-counts/1/reconcile')).toHaveLength(1))
    expect(calls('POST', '/api/v1/cycle-counts/1/reconcile')[0].body).toEqual({ rowVersion: 'RVP' })
    expect(calls('POST', '/api/v1/cycle-counts/1/finish')).toHaveLength(0)
    expect(await screen.findByText('Conteo CC-00001: Diferencia, 1 ajuste(s) en el Kárdex.')).toBeInTheDocument()
    expect(await within(panel).findByText('Conteo cerrado. Los ajustes ya están en el Kárdex de movimientos.', { exact: false })).toBeInTheDocument()
  })

  it('asignar con el ícono de la lista: la tarea COUNT del conteo (POST /warehouse-tasks/{taskId}/assign), solo con warehouse.manage y en abiertos', async () => {
    const user = userEvent.setup()
    const readOnly = wrap(COUNTER)
    await within(await waitFor(() => listPanel())).findByText('A-01')
    expect(screen.queryByRole('button', { name: /Asignar el conteo/ })).toBeNull()
    readOnly.unmount()

    wrap([...COUNTER, 'warehouse.manage'])
    const list = await waitFor(() => listPanel())
    await within(list).findByText('A-01')
    // el cerrado (CC-00002) no se asigna
    expect(within(list).queryByRole('button', { name: 'Asignar el conteo CC-00002' })).toBeNull()
    await user.click(within(list).getByRole('button', { name: 'Asignar el conteo CC-00001' }))
    const dialog = await screen.findByRole('dialog', { name: 'Asignar tarea' })
    await within(dialog).findByRole('option', { name: 'Ana Ruiz' })
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Usuario' }), 'Ana Ruiz')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(calls('POST', '/api/v1/warehouse-tasks/11/assign')).toHaveLength(1))
    expect(calls('POST', '/api/v1/warehouse-tasks/11/assign')[0].body).toEqual({ userId: 8 })
  })

  it('eliminar un conteo pendiente desde la lista, con confirmación (el cerrado no ofrece la papelera)', async () => {
    const user = userEvent.setup()
    wrap(COUNTER, '/warehouse/cycle-counts?count=1')
    const list = await waitFor(() => listPanel())
    await within(list).findByText('A-01')
    expect(within(list).queryByRole('button', { name: 'Eliminar el conteo CC-00002' })).toBeNull()
    await user.click(within(list).getByRole('button', { name: 'Eliminar el conteo CC-00001' }))
    const dialog = await screen.findByRole('dialog', { name: 'Eliminar conteo' })
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(calls('DELETE', '/api/v1/cycle-counts/1')).toHaveLength(1))
    await waitFor(() => expect(screen.getByTestId('where').textContent).toBe(''))
  })

  it('a ciegas (sin warehouse.count): sin lo esperado, sin captura, sin Confirmar, sin crear', async () => {
    mock.blind = true
    wrap(['inventory.view'])
    const panel = await loadedDetail()
    await within(panel).findByText('Tornillo')
    expect(within(panel).queryByRole('columnheader', { name: /Esperado/ })).toBeNull()
    expect(within(panel).queryByRole('textbox')).toBeNull()
    expect(within(panel).queryByRole('combobox', { name: 'Escanear o buscar producto' })).toBeNull()
    expect(within(panel).queryByRole('button', { name: /Confirmar conteo y ajustar/ })).toBeNull()
    expect(within(panel).getByText(/las cantidades esperadas no se muestran/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Nuevo conteo' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Conteo de lo cambiado' })).toBeNull()
  })
})

describe('Conteo de lo cambiado', () => {
  it('vista previa con la ventana, conteos y líneas; crear manda el almacén e "incluir vacías" y elige el primero creado', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    await user.click(await screen.findByRole('button', { name: 'Conteo de lo cambiado' }))
    const dialog = await screen.findByRole('dialog', { name: 'Conteo de lo cambiado' })
    // un solo almacén: queda elegido (de solo lectura)
    expect(within(dialog).getByLabelText('Almacén')).toHaveValue('ALM-01 · Almacén principal')
    expect(await within(dialog).findByText('Se crearán 3 conteo(s), uno por posición, con 4 línea(s)')).toBeInTheDocument()
    expect(within(dialog).getByText('7 movimiento(s) en la ventana')).toBeInTheDocument()
    expect(within(dialog).getByText('1 posición(es) ya tienen un conteo pendiente (se saltan)')).toBeInTheDocument()
    expect(within(dialog).getByText('Primera vez en este almacén: desde el inicio del día.')).toBeInTheDocument()
    // fechas de la vista previa, en hora de Puerto Rico
    expect(within(dialog).getByLabelText('Desde')).toHaveValue('2026-09-30T00:00')
    expect(within(dialog).getByLabelText('Hasta')).toHaveValue('2026-09-30T12:00')
    expect(within(dialog).getByRole('switch', { name: 'Incluir posiciones vacías' })).toBeChecked()
    const prev = calls('GET', '/api/v1/cycle-counts/changes-preview').at(-1)!
    expect(prev.url.searchParams.get('warehousePublicId')).toBe(WH)
    expect(prev.url.searchParams.get('includeEmpty')).toBe('true')
    expect(prev.url.searchParams.get('fromUtc')).toBeNull()

    await user.click(within(dialog).getByRole('button', { name: 'Crear 3 conteo(s)' }))
    await waitFor(() => expect(calls('POST', '/api/v1/cycle-counts/from-changes')).toHaveLength(1))
    expect(calls('POST', '/api/v1/cycle-counts/from-changes')[0].body).toEqual({ warehousePublicId: WH, fromUtc: null, toUtc: null, zoneIds: null, includeEmpty: true })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(screen.getByTestId('where').textContent).toBe('?count=9')
    expect(await screen.findByText('Se crearon 1 conteo(s) de lo cambiado.')).toBeInTheDocument()
  })

  it('con `problem` (el 400 que daría crear) se muestra tal cual y no se puede crear; tocar las fechas las manda en UTC', async () => {
    const user = userEvent.setup()
    mock.problem = 'Hay 250 posiciones con cambios; se generan como máximo 200 a la vez. Acote el rango de fechas o las zonas.'
    wrap(COUNTER)
    await user.click(await screen.findByRole('button', { name: 'Conteo de lo cambiado' }))
    const dialog = await screen.findByRole('dialog', { name: 'Conteo de lo cambiado' })
    expect(await within(dialog).findByText(mock.problem)).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Crear conteos' })).toBeDisabled()
    const from = within(dialog).getByLabelText('Desde')
    await user.clear(from)
    await user.type(from, '2026-09-30T10:00')
    await waitFor(() => expect(calls('GET', '/api/v1/cycle-counts/changes-preview').at(-1)!.url.searchParams.get('fromUtc')).toBe('2026-09-30T14:00:00.000Z'))
    expect(calls('GET', '/api/v1/cycle-counts/changes-preview').at(-1)!.url.searchParams.get('toUtc')).toBe('2026-09-30T16:00:00.000Z')
    expect(calls('POST', '/api/v1/cycle-counts/from-changes')).toHaveLength(0)
  })
})

describe('Nuevo conteo: por posiciones o por producto (Lote F13)', () => {
  const NO_STOCK = 'Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano.'

  async function openModal(user: ReturnType<typeof userEvent.setup>) {
    await user.click(await screen.findByRole('button', { name: 'Nuevo conteo' }))
    return screen.findByRole('dialog', { name: 'Nuevo conteo' })
  }
  /** El almacén queda elegido a mano (con un solo almacén el selector puede venir ya con él). */
  async function pickWarehouse(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
    const box = within(dialog).getByRole('combobox', { name: /Almacén/ })
    if ((box as HTMLInputElement).value === '') {
      await user.click(box)
      await user.click(await screen.findByRole('option', { name: /ALM-01/ }))
    }
  }

  it('el modal ofrece las dos opciones; la de siempre sigue mandando almacén, zonas y posiciones', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    const dialog = await openModal(user)
    expect(within(dialog).getByRole('tab', { name: 'Por posiciones' })).toHaveAttribute('aria-selected', 'true')
    expect(within(dialog).getByRole('tab', { name: 'Por producto' })).toHaveAttribute('aria-selected', 'false')
    expect(within(dialog).queryByRole('combobox', { name: /Producto/ })).toBeNull()
    await pickWarehouse(user, dialog)
    await user.click(within(dialog).getByRole('button', { name: 'Crear conteo' }))
    await waitFor(() => expect(calls('POST', '/api/v1/cycle-counts')).toHaveLength(1))
    expect(calls('POST', '/api/v1/cycle-counts')[0].body).toEqual({ warehousePublicId: WH, zoneIds: null, binIds: null })
  })

  it('por producto: exige almacén y producto, manda productPublicIds sin posiciones ni allowEmpty y abre el conteo creado', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    const dialog = await openModal(user)
    await user.click(within(dialog).getByRole('tab', { name: 'Por producto' }))
    expect(within(dialog).queryByText('Zonas')).toBeNull()
    expect(within(dialog).queryByText('Posiciones')).toBeNull()

    // sin producto: el formulario no se envía y el error sale bajo el selector
    await user.click(within(dialog).getByRole('button', { name: 'Crear conteo' }))
    expect(await within(dialog).findByText('Elija el producto.')).toBeInTheDocument()
    expect(calls('POST', '/api/v1/cycle-counts')).toHaveLength(0)

    await pickWarehouse(user, dialog)
    await user.type(within(dialog).getByRole('combobox', { name: /Producto/ }), 'torn')
    await user.click(await screen.findByRole('option', { name: /TORN-01/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Crear conteo' }))
    await waitFor(() => expect(calls('POST', '/api/v1/cycle-counts')).toHaveLength(1))
    expect(calls('POST', '/api/v1/cycle-counts')[0].body).toEqual({ warehousePublicId: WH, productPublicIds: [PRODUCT] })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(screen.getByTestId('where').textContent).toBe('?count=15')
    expect(await screen.findByText('Conteo CC-00015 creado con 2 línea(s).')).toBeInTheDocument()
  })

  /** Última consulta de productos del selector (la lista se pide al abrir el desplegable). */
  const lastProductQuery = () => {
    const list = calls('GET', '/api/v1/products')
    return list[list.length - 1]?.url.searchParams
  }

  it('el switch "Solo con existencia" nace encendido y el selector pide los productos con existencia en el almacén elegido', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    const dialog = await openModal(user)
    expect(within(dialog).queryByRole('switch', { name: 'Solo con existencia' })).toBeNull() // solo en "Por producto"
    await user.click(within(dialog).getByRole('tab', { name: 'Por producto' }))
    const sw = within(dialog).getByRole('switch', { name: 'Solo con existencia' })
    expect(sw).toBeChecked()

    await pickWarehouse(user, dialog)
    await user.type(within(dialog).getByRole('combobox', { name: /Producto/ }), 'torn')
    await screen.findByRole('option', { name: /TORN-01/ })
    const q = lastProductQuery()
    expect(q?.get('onlyOnHand')).toBe('true')
    expect(q?.get('warehousePublicId')).toBe(WH)
    expect(q?.get('activeOnly')).toBe('true')
  })

  it('con el switch apagado el selector trae todos los productos (sin filtro de existencia) y vuelve a filtrar al encenderlo', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    const dialog = await openModal(user)
    await user.click(within(dialog).getByRole('tab', { name: 'Por producto' }))
    await pickWarehouse(user, dialog)
    await user.click(within(dialog).getByRole('switch', { name: 'Solo con existencia' }))
    expect(within(dialog).getByRole('switch', { name: 'Solo con existencia' })).not.toBeChecked()

    await user.type(within(dialog).getByRole('combobox', { name: /Producto/ }), 'torn')
    await screen.findByRole('option', { name: /TORN-01/ })
    const off = lastProductQuery()
    expect(off?.has('onlyOnHand')).toBe(false)
    expect(off?.has('onlyAvailable')).toBe(false)
    expect(off?.has('warehousePublicId')).toBe(false)

    await user.click(within(dialog).getByRole('switch', { name: 'Solo con existencia' }))
    await user.clear(within(dialog).getByRole('combobox', { name: /Producto/ }))
    await user.type(within(dialog).getByRole('combobox', { name: /Producto/ }), 'tor')
    await waitFor(() => expect(lastProductQuery()?.get('onlyOnHand')).toBe('true'))
  })

  it('la preferencia no se guarda: al abrir otra vez el switch vuelve a nacer encendido', async () => {
    const user = userEvent.setup()
    wrap(COUNTER)
    let dialog = await openModal(user)
    await user.click(within(dialog).getByRole('tab', { name: 'Por producto' }))
    await user.click(within(dialog).getByRole('switch', { name: 'Solo con existencia' }))
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    dialog = await openModal(user)
    await user.click(within(dialog).getByRole('tab', { name: 'Por producto' }))
    expect(within(dialog).getByRole('switch', { name: 'Solo con existencia' })).toBeChecked()
    expect(localStorage.length).toBe(0)
  })

  it('un producto sin existencia (switch apagado): el 400 del servidor sale junto al selector y el modal sigue abierto', async () => {
    const user = userEvent.setup()
    mock.createProblem = NO_STOCK
    wrap(COUNTER)
    const dialog = await openModal(user)
    await user.click(within(dialog).getByRole('tab', { name: 'Por producto' }))
    await user.click(within(dialog).getByRole('switch', { name: 'Solo con existencia' }))
    await pickWarehouse(user, dialog)
    await user.type(within(dialog).getByRole('combobox', { name: /Producto/ }), 'torn')
    await user.click(await screen.findByRole('option', { name: /TORN-01/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Crear conteo' }))
    expect(await within(dialog).findByText(NO_STOCK)).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Nuevo conteo' })).toBeInTheDocument()
    expect(screen.getByTestId('where').textContent).toBe('')
    expect((calls('POST', '/api/v1/cycle-counts')[0].body as Json).allowEmpty).toBeUndefined()
  })
})
