import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient, setAccessDeniedHandler } from '../../app/queryClient'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { activityLink, activityTabs, activityTone, formatEventTime, nextActivitySkip, type ActivityEventDto } from './activity'
import { ActivityPanel } from './ActivityPanel'

// Cliente de la app sobre un fetch simulado (misma política que el real).
type Handler = (url: URL) => Response | unknown
const mock = vi.hoisted(() => ({ urls: [] as URL[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.urls.push(url)
    const result = (mock.handler as Handler)(url)
    if (result instanceof Response) return result
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const FULL_ACCESS = {
  permissions: ['analytics.view', 'inventory.view', 'warehouse.count', 'purchasing.view'],
  modules: ['ANALYTICS', 'WMS_LOTSERIAL', 'PURCHASING', 'CROSSDOCK'],
}

function renderPanel(access = FULL_ACCESS) {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={access.permissions} modules={access.modules}>
          <ActivityPanel />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

function event(over: Partial<ActivityEventDto> = {}): ActivityEventDto {
  return {
    occurredAtUtc: '2026-09-27T13:40:00',
    code: 'RECEIPT_CONFIRMED',
    module: 'WAREHOUSE',
    mandatory: true,
    label: 'Recibo confirmado',
    entityType: 'RECEIPT',
    entityId: 318,
    publicId: '11111111-1111-1111-1111-111111111111',
    reference: 'REC-000318',
    detail: 'ALM-01 · 6 líneas',
    userId: 7,
    userName: 'Carlos Rivera',
    ...over,
  }
}

const activityCalls = () => mock.urls.filter((u) => u.pathname === '/api/v1/analytics/activity')
const lastActivity = () => activityCalls()[activityCalls().length - 1]

const onAccessDenied = vi.fn()
beforeAll(() => {
  setLang('es')
  setAccessDeniedHandler(onAccessDenied)
})
afterAll(() => setAccessDeniedHandler(null))
beforeEach(() => {
  mock.urls = []
  onAccessDenied.mockReset()
})

describe('activity (lógica pura)', () => {
  it('pestañas: solo las de visibleModules, en el orden Almacén, Operación, Contabilidad', () => {
    expect(activityTabs(['ACCOUNTING', 'WAREHOUSE'])).toEqual(['WAREHOUSE', 'ACCOUNTING'])
    expect(activityTabs(['operations', 'OTHER'])).toEqual(['OPERATIONS'])
    expect(activityTabs(null)).toEqual([])
  })

  it('enlace de la referencia según el tipo de entidad', () => {
    const pid = '22222222-2222-2222-2222-222222222222'
    const to = (entityType: string, over: Partial<ActivityEventDto> = {}) => activityLink(event({ entityType, publicId: pid, entityId: 27, ...over }))?.to
    // Lote 13: el recibo se elige en la lista (maestro-detalle) con ?receipt=
    expect(to('RECEIPT')).toBe(`/warehouse/receipts?receipt=${pid}`)
    // Lote 14: el conteo se elige en la lista de dos paneles con ?count=
    expect(to('CYCLE_COUNT', { publicId: null })).toBe('/warehouse/cycle-counts?count=27')
    expect(to('PICK_BATCH')).toBe(`/warehouse/pick-batches/${pid}`)
    expect(to('PURCHASE_ORDER')).toBe(`/warehouse/purchase-orders/${pid}`)
    // tareas: a la cola de la pantalla de su tipo (acomodo → Recibo, reabasto → Recolección); cancelada no dice el tipo
    expect(to('WAREHOUSE_TASK', { code: 'PUTAWAY_DONE' })).toBe('/warehouse/receipts?tab=putaway')
    expect(to('WAREHOUSE_TASK', { code: 'REPLENISH_DONE' })).toBe('/warehouse/pick-batches?tab=replenish')
    expect(to('WAREHOUSE_TASK', { code: 'TASK_CANCELLED' })).toBeUndefined()
    expect(to('PRODUCT')).toBe(`/warehouse/products/${pid}`)
    expect(to('WAREHOUSE')).toBe(`/warehouse/warehouses/${pid}`)
    expect(to('CROSSDOCK_PLAN')).toBe('/warehouse/cross-dock-plans/27')
    // sin pantalla (ASN) o sin el identificador que usa la ruta: sin enlace
    expect(to('ASN')).toBeUndefined()
    expect(to('INVENTORY_TRANSACTION', { reference: 'AJ 4471' })).toBeUndefined()
    expect(to('RECEIPT', { publicId: null })).toBeUndefined()
    // PRODUCT con ajuste/transferencia/movimiento de bin va al Kárdex filtrado por producto; los demás códigos a la ficha
    expect(to('PRODUCT', { code: 'INVENTORY_ADJUSTED' })).toBe(`/warehouse/kardex?product=${encodeURIComponent(pid)}`)
    expect(to('PRODUCT', { code: 'INVENTORY_TRANSFERRED' })).toBe(`/warehouse/kardex?product=${encodeURIComponent(pid)}`)
    expect(to('PRODUCT', { code: 'BIN_MOVED' })).toBe(`/warehouse/kardex?product=${encodeURIComponent(pid)}`)
    expect(to('PRODUCT', { code: 'PRODUCT_DEACTIVATED' })).toBe(`/warehouse/products/${pid}`)
    expect(to('PRODUCT', { code: 'INVENTORY_ADJUSTED', publicId: null })).toBeUndefined()
    expect(activityLink(event({ entityType: 'PRODUCT', code: 'INVENTORY_ADJUSTED', publicId: pid }))).toMatchObject({
      perm: 'inventory.view',
      module: 'WMS_LOTSERIAL',
    })
    // guarda de la ruta destino (como routes.tsx)
    expect(activityLink(event({ entityType: 'PURCHASE_ORDER' }))).toMatchObject({ perm: 'purchasing.view', module: 'PURCHASING' })
    expect(activityLink(event({ entityType: 'CYCLE_COUNT' }))).toMatchObject({ perm: 'inventory.view', module: 'WMS_LOTSERIAL' })
  })

  it('tono del chip por familia de evento', () => {
    expect(activityTone('RECEIPT_CONFIRMED')).toBe('deliv')
    expect(activityTone('RECEIPT_VARIANCE')).toBe('fail')
    expect(activityTone('COUNT_VARIANCE')).toBe('fail')
    expect(activityTone('COUNT_RECONCILED')).toBe('route')
    expect(activityTone('INVENTORY_ADJUSTED')).toBe('warn')
    expect(activityTone('PICK_CANCELLED')).toBe('cod')
    expect(activityTone('PO_SENT')).toBe('disp')
    expect(activityTone('PRODUCT_DEACTIVATED')).toBe('cap')
    expect(activityTone('WAREHOUSE_DEACTIVATED')).toBe('cap')
    expect(activityTone('BIN_MOVED')).toBe('wh')
    expect(activityTone('PUTAWAY_DONE')).toBe('wh')
    expect(activityTone('ALGO_NUEVO')).toBe('neutral')
  })

  it('siguiente skip: lo cargado mientras sea menor que el total', () => {
    const page = (n: number, total: number) => ({ total, visibleModules: ['WAREHOUSE'], items: Array.from({ length: n }, () => event()) })
    expect(nextActivitySkip(page(50, 120), [page(50, 120)])).toBe(50)
    expect(nextActivitySkip(page(20, 120), [page(50, 120), page(50, 120), page(20, 120)])).toBeUndefined()
    expect(nextActivitySkip(page(0, 120), [page(50, 120), page(0, 120)])).toBeUndefined()
  })

  it('hora local: de hoy solo la hora; de otro día con día y mes', () => {
    const now = new Date(2026, 8, 27, 18, 0)
    const today = new Date(2026, 8, 27, 9, 42)
    const yesterday = new Date(2026, 8, 26, 9, 42)
    expect(formatEventTime(today.toISOString(), 'es', now)).toBe(new Intl.DateTimeFormat('es', { hour: '2-digit', minute: '2-digit' }).format(today))
    expect(formatEventTime(yesterday.toISOString(), 'es', now)).toBe(
      new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(yesterday),
    )
    // sin zona = UTC
    expect(formatEventTime('2026-09-27T13:40:00', 'es', new Date('2026-09-27T13:40:00Z'))).toBe(
      new Intl.DateTimeFormat('es', { hour: '2-digit', minute: '2-digit' }).format(new Date('2026-09-27T13:40:00Z')),
    )
    expect(formatEventTime(null, 'es')).toBe('')
  })
})

describe('ActivityPanel', () => {
  it('pestañas según visibleModules; cambiar de pestaña pide ese módulo', async () => {
    mock.handler = () => ({ total: 1, visibleModules: ['WAREHOUSE', 'ACCOUNTING'], items: [event()] })
    const user = userEvent.setup()
    renderPanel()
    const tabs = await screen.findByRole('tablist', { name: 'Módulos de la actividad' })
    expect(within(tabs).getAllByRole('tab').map((t) => t.textContent)).toEqual(['Almacén', 'Contabilidad'])
    expect(within(tabs).getByRole('tab', { name: 'Almacén' })).toHaveAttribute('aria-selected', 'true')
    // la primera consulta no manda módulo (el servidor elige el primero visible) y usa 24 h
    expect(activityCalls()[0].searchParams.get('module')).toBeNull()
    expect(activityCalls()[0].searchParams.get('window')).toBe('24h')
    expect(activityCalls()[0].searchParams.get('take')).toBe('50')
    await user.click(within(tabs).getByRole('tab', { name: 'Contabilidad' }))
    await waitFor(() => expect(lastActivity().searchParams.get('module')).toBe('ACCOUNTING'))
  })

  it('sin módulos visibles el panel no se pinta', async () => {
    mock.handler = () => ({ total: 0, visibleModules: [], items: [] })
    const { container } = renderPanel()
    await waitFor(() => expect(activityCalls()).toHaveLength(1))
    await waitFor(() => expect(container).toBeEmptyDOMElement())
  })

  it('un 403 no saca de Pulso: el panel no se pinta', async () => {
    mock.handler = () =>
      new Response(JSON.stringify({ status: 403, code: 'forbidden', title: 'Sin permiso.' }), {
        status: 403,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    const { container } = renderPanel()
    await waitFor(() => expect(activityCalls()).toHaveLength(1))
    await waitFor(() => expect(container).toBeEmptyDOMElement())
    expect(onAccessDenied).not.toHaveBeenCalled()
  })

  it('otro error del servidor: se muestra su mensaje', async () => {
    mock.handler = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation', title: 'La ventana debe ser 24h, 48h o today.' }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    renderPanel()
    expect(await screen.findByText('La ventana debe ser 24h, 48h o today.')).toBeInTheDocument()
  })

  it('el interruptor "Solo obligatorios" y la ventana cambian la consulta', async () => {
    mock.handler = () => ({ total: 1, visibleModules: ['WAREHOUSE'], items: [event()] })
    const user = userEvent.setup()
    renderPanel()
    await screen.findByText('Recibo confirmado')
    expect(lastActivity().searchParams.get('onlyMandatory')).toBe('false')
    await user.click(screen.getByRole('switch', { name: 'Solo obligatorios' }))
    await waitFor(() => expect(lastActivity().searchParams.get('onlyMandatory')).toBe('true'))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Ventana de tiempo' }), 'today')
    await waitFor(() => expect(lastActivity().searchParams.get('window')).toBe('today'))
    expect(lastActivity().searchParams.get('onlyMandatory')).toBe('true')
    expect(await screen.findByText('1 evento · hoy')).toBeInTheDocument()
  })

  it('fila: chip con marca de obligatorio, enlace a la ficha según el tipo y quién', async () => {
    mock.handler = () => ({
      total: 3,
      visibleModules: ['WAREHOUSE'],
      items: [
        event(),
        event({ code: 'PICK_COLLECTED', label: 'Recolección creada', mandatory: false, entityType: 'PICK_BATCH', reference: 'EMP-00112', publicId: '33333333-3333-3333-3333-333333333333' }),
        event({ code: 'ASN_CANCELLED', label: 'Aviso de llegada cancelado', mandatory: false, entityType: 'ASN', reference: 'ASN #4', publicId: null, userId: null, userName: null }),
      ],
    })
    renderPanel()
    expect(await screen.findByRole('link', { name: 'REC-000318' })).toHaveAttribute('href', '/warehouse/receipts?receipt=11111111-1111-1111-1111-111111111111')
    expect(screen.getByRole('link', { name: 'EMP-00112' })).toHaveAttribute('href', '/warehouse/pick-batches/33333333-3333-3333-3333-333333333333')
    // sin pantalla: texto sin enlace; sin usuario: 'Sistema'
    expect(screen.getByText('ASN #4').closest('a')).toBeNull()
    expect(screen.getByText('Sistema')).toBeInTheDocument()
    // solo el obligatorio lleva la marca
    expect(screen.getAllByText('oblig.')).toHaveLength(1)
    expect(within(screen.getByText('Recibo confirmado').parentElement as HTMLElement).getByText('oblig.')).toBeInTheDocument()
    expect(screen.getByText('Recibo confirmado')).toHaveClass('chip', 's-deliv')
    expect(screen.getByText('Recolección creada')).toHaveClass('chip', 's-cod')
    expect(screen.getByText('3 eventos · últimas 24 h')).toBeInTheDocument()
  })

  it('buscador libre sobre lo cargado (sin ir al API) y Referencia ordenable', async () => {
    mock.handler = () => ({
      total: 3,
      visibleModules: ['WAREHOUSE'],
      items: [
        event({ reference: 'REC-000318' }),
        event({ code: 'PICK_COLLECTED', label: 'Recolección creada', mandatory: false, entityType: 'PICK_BATCH', reference: 'EMP-00112', detail: 'ALM-01 · 3 unidades' }),
        event({ reference: 'REC-000020', detail: 'ALM-02 · 1 línea', userName: 'Ana Pérez' }),
      ],
    })
    const user = userEvent.setup()
    renderPanel()
    await screen.findByText('REC-000318')
    const calls = activityCalls().length
    // Referencia es un código: se ordena con orden natural (REC-000020 antes que REC-000318)
    const table = screen.getByRole('table', { name: 'Eventos recientes' })
    await user.click(within(screen.getByRole('columnheader', { name: /Referencia/ })).getByRole('button'))
    const refs = within(table)
      .getAllByRole('row')
      .slice(1)
      .map((r) => within(r).getAllByRole('cell')[2].textContent)
    expect(refs).toEqual(['EMP-00112', 'REC-000020', 'REC-000318'])

    await user.type(screen.getByRole('searchbox'), 'recoleccion')
    await waitFor(() => expect(screen.queryByText('REC-000318')).not.toBeInTheDocument())
    expect(screen.getByText('EMP-00112')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'ana alm-02')
    await waitFor(() => expect(screen.queryByText('EMP-00112')).not.toBeInTheDocument())
    expect(screen.getByText('REC-000020')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'nada que coincida')
    expect(await screen.findByText('Sin resultados')).toBeInTheDocument()
    expect(activityCalls()).toHaveLength(calls)
  })

  it('sin permiso para la ficha destino (orden de compra sin purchasing.view): referencia sin enlace', async () => {
    mock.handler = () => ({
      total: 1,
      visibleModules: ['WAREHOUSE'],
      items: [event({ code: 'PO_SENT', label: 'Orden de compra enviada', entityType: 'PURCHASE_ORDER', reference: 'OC-000094' })],
    })
    renderPanel({ permissions: ['analytics.view', 'inventory.view'], modules: ['ANALYTICS', 'WMS_LOTSERIAL'] })
    expect(await screen.findByText('OC-000094')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'OC-000094' })).not.toBeInTheDocument()
  })

  it('vacío: "Sin actividad en esta ventana"', async () => {
    mock.handler = () => ({ total: 0, visibleModules: ['WAREHOUSE'], items: [] })
    renderPanel()
    expect(await screen.findByText('Sin actividad en esta ventana')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ver más' })).not.toBeInTheDocument()
  })

  it('"Ver más" pide la siguiente página (skip) y acumula las filas', async () => {
    const TOTAL = 52
    mock.handler = (url: URL) => {
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 50)
      const count = Math.max(0, Math.min(take, TOTAL - skip))
      return {
        total: TOTAL,
        visibleModules: ['WAREHOUSE'],
        items: Array.from({ length: count }, (_, i) =>
          event({ reference: `REC-${String(skip + i + 1).padStart(6, '0')}`, occurredAtUtc: `2026-09-27T${String(12 - Math.floor((skip + i) / 60)).padStart(2, '0')}:${String(59 - ((skip + i) % 60)).padStart(2, '0')}:00` }),
        ),
      }
    }
    const user = userEvent.setup()
    renderPanel()
    expect(await screen.findByText('REC-000001')).toBeInTheDocument()
    expect(screen.getByText('REC-000050')).toBeInTheDocument()
    expect(screen.queryByText('REC-000051')).not.toBeInTheDocument()
    expect(activityCalls()[0].searchParams.get('skip')).toBe('0')
    await user.click(screen.getByRole('button', { name: 'Ver más' }))
    expect(await screen.findByText('REC-000052')).toBeInTheDocument()
    expect(lastActivity().searchParams.get('skip')).toBe('50')
    // acumula: siguen las de la primera página
    expect(screen.getByText('REC-000001')).toBeInTheDocument()
    expect(screen.getByText('52 eventos · últimas 24 h')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Ver más' })).not.toBeInTheDocument())
  })
})
