// Lote F12 — revisión rápida del conteo por producto sobre un fetch simulado: pestaña "Por revisar" (`GET /cycle-counts/review`:
// columnas, estado calculado, filtros, orden y selección), "Cerrar los que cuadran" (confirmación con cuántos, `POST
// /reconcile-matching` por ids y resumen con los motivos en español), detalle de un conteo Contado (solo las líneas que fallan,
// "Ver todas", evidencia y corrección en la fila, posición provisional), vista previa antes de confirmar (totales, errores,
// faltantes, Concordancia y confirmar con su rowVersion) y sin warehouse.count (sin pestaña ni vista previa: a ciegas).
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
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  lines: [] as Json[],
  status: 'COUNTED',
  blind: false,
  previewError: null as string | null,
  blockingError: null as string | null,
  reconciled: false,
}))
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
const header = (id: number, statusCode = 'COUNTED') => ({
  id,
  number: `CC-0000${id}`,
  warehousePublicId: WH,
  warehouseCode: 'ALM-01',
  statusCode,
  status: statusCode === 'COUNTED' ? 'Contado' : statusCode === 'RECONCILED' ? 'Concordancia' : 'Diferencia',
  originCode: 'PRODUCT',
  origin: 'Por producto',
  lineCount: 3,
  countedLines: 3,
  binCount: 3,
  correctedLines: 1,
  createdAtUtc: '2026-10-02T14:00:00',
})
// CC-00001 cuadra; CC-00002 tiene diferencia (es el que se abre); CC-00003 tiene un error de reservado
const REVIEW_ITEMS: Json[] = [
  { count: header(1), countedByUserId: 7, countedByName: 'Ana Operaria', countedByCount: 1, firstProductSku: 'P-1', firstProductName: 'Tornillo', otherProducts: 0, positions: 2, lines: 2, pendingLines: 0, differingLines: 0, errorLines: 0, movements: 0, correctedLines: 0, matches: true },
  { count: header(2), countedByUserId: 7, countedByName: 'Ana Operaria', countedByCount: 2, firstProductSku: 'P-2', firstProductName: 'Tuerca', otherProducts: 2, positions: 3, lines: 3, pendingLines: 0, differingLines: 1, errorLines: 0, movements: 1, correctedLines: 1, matches: false },
  { count: header(3), countedByUserId: 8, countedByName: 'Beto Operario', countedByCount: 1, firstProductSku: 'P-3', firstProductName: 'Arandela', otherProducts: 0, positions: 1, lines: 1, pendingLines: 0, differingLines: 0, errorLines: 1, movements: 0, correctedLines: 0, matches: false },
  { count: header(4), countedByUserId: 8, countedByName: 'Beto Operario', countedByCount: 1, firstProductSku: 'P-4', firstProductName: 'Clavo', otherProducts: 0, positions: 1, lines: 1, pendingLines: 0, differingLines: 0, errorLines: 0, movements: 0, correctedLines: 0, matches: true },
]
const BASE_LINES: Json[] = [
  // cuadra (no falla): se ve solo con "Ver todas"
  { id: 201, binId: 20, binCode: 'A-01', sku: 'P-2', productName: 'Tuerca', trackingTypeCode: 'NONE', systemQty: 4, currentQty: 4, countedQty: 4, capturedQty: 4, capturedByName: 'Ana Operaria', capturedAtUtc: '2026-10-02T13:30:00', wasCorrected: false },
  // falla: contó 3 y la existencia es 5
  { id: 202, binId: 21, binCode: 'A-02', sku: 'P-2', productName: 'Tuerca', trackingTypeCode: 'NONE', systemQty: 5, currentQty: 5, countedQty: 3, capturedQty: 3, capturedByName: 'Ana Operaria', capturedAtUtc: '2026-10-02T13:31:00', wasCorrected: false },
  // corregida por el supervisor (cuadra tras la corrección) en una posición provisional
  { id: 203, binId: 22, binCode: 'PROV-1', binIsProvisional: true, sku: 'P-2', productName: 'Tuerca', trackingTypeCode: 'NONE', systemQty: 0, currentQty: 0, countedQty: 0, capturedQty: 1, capturedByName: 'Ana Operaria', capturedAtUtc: '2026-10-02T13:32:00', correctedByName: 'Sofía Supervisora', correctedAtUtc: '2026-10-02T15:00:00', wasCorrected: true },
]

function detail(id: number): Json {
  const status = mock.reconciled ? 'RECONCILED_VARIANCE' : mock.status
  const lines = mock.blind ? mock.lines.map((l) => ({ ...l, systemQty: null, varianceQty: null, currentQty: null })) : mock.lines
  return { count: header(id, status), lines, rowVersion: `RV${mock.calls.length}`, isBlind: mock.blind }
}

function preview(): Json {
  const lines = mock.lines.map((l) => {
    const counted = l.countedQty as number | null
    const current = l.currentQty as number
    const adj = counted == null ? 0 : counted - current
    return {
      lineId: l.id,
      binId: l.binId,
      binCode: l.binCode,
      binIsProvisional: l.binIsProvisional ?? false,
      sku: l.sku,
      productName: l.productName,
      trackingTypeCode: 'NONE',
      systemQty: l.systemQty,
      currentQty: current,
      reservedQty: mock.previewError && l.id === 202 ? 4 : 0,
      countedQty: counted,
      isPending: counted == null,
      wasCorrected: l.wasCorrected ?? false,
      adjustmentQty: adj,
      resultingQty: current + adj,
      movements: adj === 0 ? 0 : 1,
      error: mock.previewError && l.id === 202 ? mock.previewError : null,
    }
  })
  const pending = lines.filter((l) => l.isPending).length
  const errors = lines.filter((l) => l.error).length
  const movements = lines.reduce((a, l) => a + l.movements, 0)
  const ok = pending === 0 && errors === 0 && !mock.blockingError
  return {
    count: header(2),
    lines,
    totals: {
      lines: lines.length,
      pendingLines: pending,
      linesWithDifference: lines.filter((l) => l.adjustmentQty !== 0).length,
      movements,
      errorLines: errors,
      matches: ok && movements === 0,
      resultStatusCode: ok ? (movements > 0 ? 'RECONCILED_VARIANCE' : 'RECONCILED') : null,
    },
    blockingError: mock.blockingError,
    rowVersion: 'RV-PREVIEW',
  }
}

function route(method: string, url: URL, body: unknown): [number, unknown] {
  const p = url.pathname
  if (p === '/api/v1/cycle-counts/review') {
    const by = url.searchParams.get('countedByUserId')
    const items = by ? REVIEW_ITEMS.filter((i) => String(i.countedByUserId) === by) : REVIEW_ITEMS
    return [200, { total: 30, skip: Number(url.searchParams.get('skip') ?? 0), take: Number(url.searchParams.get('take') ?? 50), items: Number(url.searchParams.get('skip') ?? 0) === 0 ? items : [] }]
  }
  if (p === '/api/v1/cycle-counts/reconcile-matching' && method === 'POST') {
    const ids = (body as { ids: number[] }).ids
    return [
      200,
      {
        examined: ids.length + 1,
        closed: ids.filter((id) => id !== 4).map((id) => ({ id, number: `CC-0000${id}`, statusCode: 'RECONCILED', lines: 2 })),
        skipped: [
          ...ids.filter((id) => id === 4).map((id) => ({ id, number: `CC-0000${id}`, reasonCode: 'Stale', reason: 'La existencia cambió mientras se cerraba; revíselo.' })),
          { id: 2, number: 'CC-00002', reasonCode: 'WouldPost', reason: 'Asentaría 1 movimiento(s); revíselo.', count: 1 },
        ],
        truncated: false,
      },
    ]
  }
  if (p === '/api/v1/cycle-counts/page') return [200, { total: 1, skip: 0, take: 25, items: [header(2)] }]
  const m = /^\/api\/v1\/cycle-counts\/(\d+)(\/[\w-]+)?$/.exec(p)
  if (m) {
    const id = Number(m[1])
    if (m[2] === '/reconcile-preview') return mock.blind ? [403, { title: 'Sin permiso.', code: 'forbidden' }] : [200, preview()]
    if (m[2] === '/lines' && method === 'PUT') {
      const item = (body as { lines: { lineId: number; countedQty: number | null }[] }).lines[0]
      mock.lines = mock.lines.map((l) =>
        l.id === item.lineId
          ? { ...l, countedQty: item.countedQty, wasCorrected: item.countedQty !== l.capturedQty, correctedByName: 'Sofía Supervisora', correctedAtUtc: '2026-10-02T16:00:00' }
          : l,
      )
      return [200, detail(id)]
    }
    if (m[2] === '/reconcile' && method === 'POST') {
      mock.reconciled = true
      return [200, detail(id)]
    }
    return [200, detail(id)]
  }
  const confirm = /^\/api\/v1\/warehouses\/[\w-]+\/bins\/(\d+)\/confirm-provisional$/.exec(p)
  if (confirm && method === 'POST') {
    mock.lines = mock.lines.map((l) => (l.binId === Number(confirm[1]) ? { ...l, binIsProvisional: false } : l))
    return [200, { id: Number(confirm[1]), code: 'PROV-1', isProvisional: false }]
  }
  if (p === '/api/v1/users') return [200, [{ id: 9, fullName: 'Carla Usuaria', isActive: true }]]
  if (p === '/api/v1/warehouses') return [200, [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]]
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
  return [200, []]
}

function Where() {
  const { search } = useLocation()
  return <p data-testid="where">{search}</p>
}

function wrap(permissions: string[], path = '/warehouse/cycle-counts?tab=review') {
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

const SUPERVISOR = ['inventory.view', 'warehouse.count', 'warehouse.count.capture']
const calls = (method: string, path: string | RegExp) =>
  mock.calls.filter((c) => c.method === method && (typeof path === 'string' ? c.url.pathname === path : path.test(c.url.pathname)))
const reviewPanel = () => document.querySelector('.cc-review') as HTMLElement
const detailPanel = () => document.querySelector('.cc-detail') as HTMLElement
const reviewRows = () => Array.from(reviewPanel().querySelectorAll('tbody tr')) as HTMLElement[]
const loadedDetail = () =>
  waitFor(() => {
    const el = detailPanel()
    expect(el?.querySelector('.cc-lines')).not.toBeNull()
    return el
  })

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.lines = BASE_LINES.map((l) => ({ ...l }))
  mock.status = 'COUNTED'
  mock.blind = false
  mock.previewError = null
  mock.blockingError = null
  mock.reconciled = false
  localStorage.clear()
})

describe('Por revisar: lista', () => {
  it('pestaña con warehouse.count: columnas, estado calculado, producto "y N más", quién contó y el primero abierto a la derecha', async () => {
    const user = userEvent.setup()
    wrap(SUPERVISOR, '/warehouse/cycle-counts')
    await user.click(await screen.findByRole('tab', { name: 'Por revisar' }))
    expect(screen.getByTestId('where').textContent).toBe('?tab=review')
    const panel = await waitFor(() => reviewPanel())
    await within(panel).findByText('CC-00001')
    for (const h of ['Conteo', 'Contó', 'Producto', 'Posiciones', 'Líneas', 'Con diferencia', 'Correcciones', 'Estado'])
      expect(within(panel).getByRole('columnheader', { name: new RegExp(h) })).toBeInTheDocument()
    expect(within(panel).getAllByText('Cuadra')).toHaveLength(2)
    expect(within(panel).getByText('Con diferencia', { selector: '.chip' })).toBeInTheDocument()
    expect(within(panel).getByText('Con errores')).toBeInTheDocument()
    expect(within(panel).getByText('P-2 · Tuerca y 2 más')).toBeInTheDocument()
    expect(within(panel).getByText('Ana Operaria y 1 más')).toBeInTheDocument()
    expect(within(panel).getByText('30')).toBeInTheDocument()
    const req = calls('GET', '/api/v1/cycle-counts/review')[0].url.searchParams
    expect(req.get('skip')).toBe('0')
    expect(req.get('take')).toBe('25')
    // sin ?count= se abre el primero de la página
    const d = await loadedDetail()
    expect(within(d).getByText('CC-00001')).toBeInTheDocument()
  })

  it('filtros al API (Contó y buscador), orden por columna y clic en la fila abre el conteo', async () => {
    const user = userEvent.setup()
    wrap([...SUPERVISOR, 'admin.users'])
    const panel = await waitFor(() => reviewPanel())
    await within(panel).findByText('CC-00003')
    // "Contó": los vistos en la página y los usuarios de la compañía
    const by = within(panel).getByRole('combobox', { name: 'Contó' })
    await waitFor(() => expect(within(by).getByRole('option', { name: 'Carla Usuaria' })).toBeInTheDocument())
    await user.selectOptions(by, 'Beto Operario')
    await waitFor(() => expect(calls('GET', '/api/v1/cycle-counts/review').at(-1)!.url.searchParams.get('countedByUserId')).toBe('8'))
    await waitFor(() => expect(within(panel).queryByText('CC-00001')).toBeNull())
    await user.selectOptions(by, '')
    await within(panel).findByText('CC-00001')
    await user.type(within(panel).getByRole('searchbox'), 'tuerca')
    await waitFor(() => expect(calls('GET', '/api/v1/cycle-counts/review').at(-1)!.url.searchParams.get('search')).toBe('tuerca'))
    // orden: "Con diferencia" ascendente y luego descendente (CC-00002 es el único con 1)
    await user.click(within(panel).getByRole('button', { name: /Con diferencia/ }))
    await user.click(within(panel).getByRole('button', { name: /Con diferencia/ }))
    await waitFor(() => expect(reviewRows()[0]).toHaveTextContent('CC-00002'))
    await user.click(within(panel).getByText('CC-00003'))
    expect(screen.getByTestId('where').textContent).toBe('?tab=review&count=3')
  })

  it('"Cerrar los que cuadran" sin elegir: confirma cuántos y cuáles, manda los ids de la página y resume lo cerrado y lo omitido', async () => {
    const user = userEvent.setup()
    wrap(SUPERVISOR)
    const panel = await waitFor(() => reviewPanel())
    await within(panel).findByText('CC-00004')
    expect(within(panel).getByText(/2 conteo\(s\) de esta página cuadran/)).toBeInTheDocument()
    await user.click(within(panel).getByRole('button', { name: 'Cerrar los que cuadran' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar los que cuadran' })
    expect(within(dialog).getByText(/Se van a cerrar 2 conteo\(s\) que cuadran: CC-00001, CC-00004\./)).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Comentario (opcional)'), 'Cierre del lunes')
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar 2 conteo(s)' }))
    await waitFor(() => expect(calls('POST', '/api/v1/cycle-counts/reconcile-matching')).toHaveLength(1))
    expect(calls('POST', '/api/v1/cycle-counts/reconcile-matching')[0].body).toEqual({ ids: [1, 4], comment: 'Cierre del lunes' })
    const result = await screen.findByRole('dialog', { name: 'Resultado del cierre' })
    expect(within(result).getByText('Cerrados (1)')).toBeInTheDocument()
    expect(within(result).getByText('CC-00001: Concordancia, 2 línea(s), sin movimientos')).toBeInTheDocument()
    expect(within(result).getByText('Quedan para revisar (2)')).toBeInTheDocument()
    expect(within(result).getByText(/La existencia cambió mientras se cerraba; revíselo\./)).toBeInTheDocument()
    expect(within(result).getByText(/Asentaría 1 movimiento\(s\); revíselo\./)).toBeInTheDocument()
    expect(await screen.findByText('Se cerraron 1 conteo(s); 2 quedan para revisar.')).toBeInTheDocument()
    // abrir un omitido lleva a su detalle
    await user.click(within(result).getByRole('button', { name: 'CC-00004' }))
    expect(screen.getByTestId('where').textContent).toBe('?tab=review&count=4')
    // la lista se refresca
    await waitFor(() => expect(calls('GET', '/api/v1/cycle-counts/review').length).toBeGreaterThan(1))
  })

  it('con casillas: solo se eligen los que cuadran y se cierran solo los elegidos; el comentario largo se avisa', async () => {
    const user = userEvent.setup()
    wrap(SUPERVISOR)
    const panel = await waitFor(() => reviewPanel())
    await within(panel).findByText('CC-00004')
    expect(within(panel).getByRole('checkbox', { name: 'Elegir CC-00002 para cerrar' })).toBeDisabled()
    await user.click(within(panel).getByRole('checkbox', { name: 'Elegir CC-00004 para cerrar' }))
    // la casilla no abre el conteo
    expect(screen.getByTestId('where').textContent).toBe('?tab=review')
    await user.click(within(panel).getByRole('button', { name: 'Cerrar los elegidos (1)' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar los que cuadran' })
    expect(within(dialog).getByText('Solo los conteos elegidos.')).toBeInTheDocument()
    const comment = within(dialog).getByLabelText('Comentario (opcional)')
    await user.click(comment)
    await user.paste('x'.repeat(501))
    expect(within(dialog).getByText('El comentario admite como máximo 500 caracteres.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Cerrar 1 conteo(s)' })).toBeDisabled()
    await user.clear(comment)
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar 1 conteo(s)' }))
    await waitFor(() => expect(calls('POST', '/api/v1/cycle-counts/reconcile-matching')).toHaveLength(1))
    expect(calls('POST', '/api/v1/cycle-counts/reconcile-matching')[0].body).toEqual({ ids: [4], comment: null })
  })
})

describe('Por revisar: detalle, corrección y vista previa', () => {
  it('un conteo Contado abre con SOLO las líneas que fallan; "Ver todas" muestra el resto; evidencia y chip de corrección', async () => {
    const user = userEvent.setup()
    wrap(SUPERVISOR, '/warehouse/cycle-counts?tab=review&count=2')
    const d = await loadedDetail()
    // la vista previa llega: falla la A-02 (ajuste −2); la corregida en la provisional y la A-01 cuadran
    await within(d).findByText('1 de 3 líneas: solo las que fallan')
    expect(within(d).getByText('A-02')).toBeInTheDocument()
    expect(within(d).queryByText('A-01')).toBeNull()
    // varianza contra la foto y ajuste contra la existencia actual (en rojo: sale)
    expect(within(d).getAllByText('-2').some((el) => el.classList.contains('qty-out'))).toBe(true)
    expect(within(d).getByText(/Revisión: si cambia una cantidad queda como corrección/)).toBeInTheDocument()
    expect(within(d).getByText(/^Contó 3 \(Ana Operaria, .+\)$/)).toBeInTheDocument()
    await user.click(within(d).getByRole('switch', { name: 'Ver todas' }))
    expect(within(d).getByText('A-01')).toBeInTheDocument()
    expect(within(d).getByText('3 líneas')).toBeInTheDocument()
    // evidencia de la corregida: Contó 1 (Ana) · Corrección · Corregido a 0 (Sofía)
    expect(within(d).getByText(/^Contó 1 \(Ana Operaria, .+\)$/)).toBeInTheDocument()
    expect(within(d).getByText(/^Corregido a 0 \(Sofía Supervisora, .+\)$/)).toBeInTheDocument()
    expect(within(d).getByText('Corrección')).toBeInTheDocument()
    // posición provisional: chip, sin "Confirmar posición" sin warehouse.manage
    expect(within(d).getByText('Posición pendiente de revisión')).toBeInTheDocument()
    expect(within(d).queryByRole('button', { name: /Confirmar la posición/ })).toBeNull()
  })

  it('corregir en la fila: PUT /lines, la línea no desaparece al dejar de fallar y la vista previa se recalcula', async () => {
    const user = userEvent.setup()
    wrap(SUPERVISOR, '/warehouse/cycle-counts?tab=review&count=2')
    const d = await loadedDetail()
    await within(d).findByText('1 de 3 líneas: solo las que fallan')
    const before = calls('GET', '/api/v1/cycle-counts/2/reconcile-preview').length
    const qty = within(d).getByRole('textbox', { name: 'Contado de la línea 2 (P-2)' })
    await user.clear(qty)
    await user.type(qty, '5')
    await user.tab()
    await waitFor(() => expect(calls('PUT', '/api/v1/cycle-counts/2/lines')).toHaveLength(1))
    expect((calls('PUT', '/api/v1/cycle-counts/2/lines')[0].body as { lines: unknown[] }).lines).toEqual([{ lineId: 202, countedQty: 5, serialNumbers: null }])
    await waitFor(() => expect(calls('GET', '/api/v1/cycle-counts/2/reconcile-preview').length).toBeGreaterThan(before))
    // ya no falla, pero se queda a la vista (se acaba de corregir) con su evidencia
    expect(await within(d).findByText(/^Corregido a 5 \(Sofía Supervisora, .+\)$/)).toBeInTheDocument()
    expect(within(d).getByText('A-02')).toBeInTheDocument()
  })

  it('con warehouse.manage: "Confirmar posición" de la provisional (POST confirm-provisional del almacén del conteo)', async () => {
    const user = userEvent.setup()
    wrap([...SUPERVISOR, 'warehouse.manage'], '/warehouse/cycle-counts?tab=review&count=2')
    const d = await loadedDetail()
    await user.click(await within(d).findByRole('switch', { name: 'Ver todas' }))
    await user.click(within(d).getByRole('button', { name: 'Confirmar la posición PROV-1' }))
    await waitFor(() => expect(calls('POST', `/api/v1/warehouses/${WH}/bins/22/confirm-provisional`)).toHaveLength(1))
    expect(await screen.findByText('Posición PROV-1 confirmada.')).toBeInTheDocument()
  })

  it('vista previa con un error de reservado: totales, el error en su línea y Confirmar deshabilitado explicando por qué', async () => {
    const user = userEvent.setup()
    mock.previewError = 'El conteo de P-2 en A-02 (3) es menor que lo reservado (4); libere la reserva antes de reconciliar.'
    wrap(SUPERVISOR, '/warehouse/cycle-counts?tab=review&count=2')
    const d = await loadedDetail()
    await user.click(await within(d).findByRole('button', { name: /Confirmar conteo y ajustar/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Confirmar conteo y ajustar' })
    await within(dialog).findByText('Existencia actual')
    const totals = within(dialog).getByRole('group', { name: 'Totales de la vista previa' })
    expect(totals).toHaveTextContent(/Líneas\s*3/)
    expect(totals).toHaveTextContent(/Con error\s*1/)
    expect(within(dialog).getByText(mock.previewError)).toBeInTheDocument()
    expect(within(dialog).getByText('Hay 1 línea(s) con error; corríjalas antes de confirmar.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Confirmar conteo y ajustar' })).toBeDisabled()
    expect(within(dialog).getByText('Posición pendiente de revisión')).toBeInTheDocument()
    expect(calls('POST', '/api/v1/cycle-counts/2/reconcile')).toHaveLength(0)
  })

  it('vista previa con un error del conteo entero (blockingError) y con líneas sin contar', async () => {
    const user = userEvent.setup()
    mock.blockingError = 'La serie S-1 está en dos líneas del conteo; corrija una antes de reconciliar.'
    mock.status = 'OPEN'
    mock.lines = mock.lines.map((l) => (l.id === 201 ? { ...l, countedQty: null } : l))
    wrap(SUPERVISOR, '/warehouse/cycle-counts?tab=review&count=2')
    const d = await loadedDetail()
    // con una línea sin contar, el botón de la ficha ya se bloquea (mismo 422); se completa para abrir la vista previa
    expect(await within(d).findByText('Faltan 1 línea(s) por contar.')).toBeInTheDocument()
    mock.lines = mock.lines.map((l) => (l.id === 201 ? { ...l, countedQty: 4 } : l))
    const qty = within(d).getByRole('textbox', { name: 'Contado de la línea 1 (P-2)' })
    await user.type(qty, '4')
    await user.tab()
    await user.click(within(d).getByRole('button', { name: /Confirmar conteo y ajustar/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Confirmar conteo y ajustar' })
    expect(await within(dialog).findAllByText(mock.blockingError)).toHaveLength(2)
    expect(within(dialog).getByRole('button', { name: 'Confirmar conteo y ajustar' })).toBeDisabled()
  })

  it('vista previa que se puede confirmar: aviso del efecto y confirmar todo o nada con el rowVersion de la vista previa', async () => {
    const user = userEvent.setup()
    wrap(SUPERVISOR, '/warehouse/cycle-counts?tab=review&count=2')
    const d = await loadedDetail()
    await user.click(await within(d).findByRole('button', { name: /Confirmar conteo y ajustar/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Confirmar conteo y ajustar' })
    expect(await within(dialog).findByText('Al confirmar se asentarán 1 movimiento(s) en el Kárdex y el conteo terminará en Diferencia.')).toBeInTheDocument()
    expect(within(dialog).getByText('Saldo resultante')).toBeInTheDocument()
    const ok = within(dialog).getByRole('button', { name: 'Confirmar conteo y ajustar' })
    await waitFor(() => expect(ok).toBeEnabled())
    await user.click(ok)
    await waitFor(() => expect(calls('POST', '/api/v1/cycle-counts/2/reconcile')).toHaveLength(1))
    expect(calls('POST', '/api/v1/cycle-counts/2/reconcile')[0].body).toEqual({ rowVersion: 'RV-PREVIEW' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(await within(d).findByText('Conteo cerrado. Los ajustes ya están en el Kárdex de movimientos.', { exact: false })).toBeInTheDocument()
  })

  it('un conteo que cuadra: "Concordancia: … no se ajustará nada."', async () => {
    const user = userEvent.setup()
    mock.lines = mock.lines.map((l) => (l.id === 202 ? { ...l, countedQty: 5 } : l))
    wrap(SUPERVISOR, '/warehouse/cycle-counts?tab=review&count=2')
    const d = await loadedDetail()
    expect(await within(d).findByText('Ninguna línea falla: todo cuadra contra la existencia actual.')).toBeInTheDocument()
    await user.click(within(d).getByRole('button', { name: /Confirmar conteo y ajustar/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Confirmar conteo y ajustar' })
    expect(await within(dialog).findByText('Concordancia: el conteo cuadra con la existencia actual; al confirmar no se ajustará nada.')).toBeInTheDocument()
  })
})

describe('sin warehouse.count (a ciegas)', () => {
  it('sin pestaña "Por revisar", ?tab=review abre la lista de siempre, sin vista previa ni lo esperado; la evidencia sí se ve', async () => {
    mock.blind = true
    wrap(['inventory.view'], '/warehouse/cycle-counts?tab=review&count=2')
    expect(screen.queryByRole('tab', { name: 'Por revisar' })).toBeNull()
    const d = await loadedDetail()
    await within(d).findAllByText('Tuerca')
    expect(document.querySelector('.cc-review')).toBeNull()
    expect(calls('GET', '/api/v1/cycle-counts/review')).toHaveLength(0)
    expect(calls('GET', /reconcile-preview$/)).toHaveLength(0)
    expect(within(d).queryByRole('columnheader', { name: /Esperado/ })).toBeNull()
    expect(within(d).queryByRole('columnheader', { name: /Ajuste/ })).toBeNull()
    expect(within(d).queryByRole('switch', { name: 'Ver todas' })).toBeNull()
    expect(within(d).queryByRole('button', { name: /Confirmar conteo y ajustar/ })).toBeNull()
    // todas las líneas (no hay filtro de "las que fallan" sin lo esperado) con su evidencia
    expect(within(d).getByText('A-01')).toBeInTheDocument()
    expect(within(d).getByText(/^Contó 3 \(Ana Operaria, .+\)$/)).toBeInTheDocument()
  })
})
