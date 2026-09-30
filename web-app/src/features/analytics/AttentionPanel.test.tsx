import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient, setAccessDeniedHandler } from '../../app/queryClient'
import { SessionContext, type MeDto, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { attentionHref, attentionRowText, attentionToneClass, formatQty, formatSigned, type AttentionDto, type AttentionItemDto } from './attention'
import Pulse from './Pulse'
import type { PulseDto, PulsePanelDto } from './pulseLayout'

// Lote 14 (D6) — panel "Necesita tu atención" del Pulso sobre un fetch simulado (misma política que el cliente real).
type Handler = (path: string, method: string, url: URL) => Response | unknown
const mock = vi.hoisted(() => ({ calls: [] as string[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.calls.push(url.pathname)
    const result = (mock.handler as Handler)(url.pathname, req.method, url)
    if (result instanceof Response) return result
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const ME: MeDto = { userId: 1, fullName: 'Ana Admin', email: 'admin@teikem.local', tenantId: 1, tenantName: 'Demo', lang: 'es' }
const session = (): Session => ({
  me: ME,
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 1,
  lang: 'es',
  setLang: vi.fn(),
  logout: vi.fn(async () => {}),
  switchTenant: vi.fn(async () => {}),
  permissions: new Set(),
  modules: new Set(),
  reloadMe: vi.fn(async () => {}),
})

/** Muestra la ruta a la que llevó "Revisar" o "Ver todos". */
function Where() {
  const loc = useLocation()
  return <p data-testid="where">{`${loc.pathname}${loc.search}`}</p>
}

function renderPulse() {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <SessionContext.Provider value={session()}>
          <AccessProvider permissions={['pulse.attention', 'inventory.view']} modules={['WMS_LOTSERIAL']}>
            <Routes>
              <Route path="/" element={<Pulse />} />
              <Route path="*" element={<Where />} />
            </Routes>
          </AccessProvider>
        </SessionContext.Provider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const panel = (key: string, sortOrder: number, isVisible = true): PulsePanelDto => ({ key, sortOrder, isVisible, source: 'default' })
const pulse = (panels: PulsePanelDto[]): PulseDto => ({
  panels,
  indicators: [{ id: 1, name: 'Ventas', value: 10, isVisible: true }],
  charts: [],
  hasPersonalLayout: false,
  canOrganizeCompany: false,
})
const paintedPanels = (container: HTMLElement) => Array.from(container.querySelectorAll('[data-panel]')).map((el) => el.getAttribute('data-panel'))

const PUBLIC_ID = '8f2c1d9e-1111-4a2b-9c3d-000000000001'
/** Descuadre por posición como lo manda InventoryDiscrepancyAttentionProvider. */
function discrepancy(over: Partial<Record<string, string>> = {}, sinceUtc = '2026-09-28T14:05:00Z'): AttentionItemDto {
  const publicId = over.publicId ?? PUBLIC_ID
  return {
    code: 'INVENTORY_DISCREPANCY',
    module: 'WAREHOUSE',
    tone: 'danger',
    count: 1,
    params: {
      publicId,
      kind: 'BALANCE',
      sku: 'L14-105022',
      productName: 'Guantes de nitrilo',
      warehouse: 'ALM-01',
      bin: 'A-01-02',
      lot: 'LT-7',
      where: 'A-01-02',
      ledgerQty: '1200',
      balanceQty: '1201.5',
      difference: '1.5',
      detectedAtUtc: sinceUtc,
      checkCount: '2',
      ...over,
    },
    route: '/warehouse/kardex',
    query: { tab: 'reconciliation', discrepancy: publicId },
    sinceUtc,
  }
}
const GROUP = { code: 'INVENTORY_DISCREPANCY', module: 'WAREHOUSE', total: 7, route: '/warehouse/kardex', query: { tab: 'reconciliation', status: 'OPEN' } }

let attention: AttentionDto | Response = { total: 0, items: [], groups: [] }
let pulseDto: PulseDto = pulse([panel('INDICATORS', 20), panel('ATTENTION', 5)])
const handler: Handler = (path) => {
  if (path === '/api/v1/analytics/pulse') return pulseDto
  if (path === '/api/v1/analytics/attention') return attention
  return []
}
const onAccessDenied = vi.fn()

beforeAll(() => {
  setLang('es')
  setAccessDeniedHandler(onAccessDenied)
})
afterAll(() => {
  setAccessDeniedHandler(null)
  setLang('es')
})
beforeEach(() => {
  mock.calls = []
  mock.handler = handler
  onAccessDenied.mockReset()
  pulseDto = pulse([panel('INDICATORS', 20), panel('ATTENTION', 5)])
  attention = { total: 0, items: [], groups: [] }
})

const attentionSection = () => screen.findByRole('region', { name: 'Necesita tu atención' })

describe('AttentionPanel en el Pulso', () => {
  it('con descuadres: primero por su orden 5, cabecera "N pendientes", fila con dónde, cifras y desde cuándo, y "Ver todos (N)"', async () => {
    attention = {
      total: 7,
      items: [
        discrepancy(),
        discrepancy({ publicId: 'p-2', kind: 'PRODUCT_TOTAL', bin: '', warehouse: '', lot: '', where: '', sku: 'SKU-9', productName: 'Cajas', ledgerQty: '5', balanceQty: '3', difference: '-2' }, '2026-09-29T10:00:00Z'),
      ],
      groups: [GROUP],
    }
    const { container } = renderPulse()
    const section = await attentionSection()
    await waitFor(() => expect(within(section).getByText('7 pendientes')).toBeInTheDocument())
    expect(paintedPanels(container)).toEqual(['ATTENTION', 'INDICATORS'])

    const rows = within(within(section).getByRole('list', { name: 'Pendientes más antiguos' })).getAllByRole('listitem')
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveClass('work', 'tone-danger')
    expect(within(rows[0]).getByText('Descuadre en L14-105022')).toBeInTheDocument()
    expect(within(rows[0]).getByText('Guantes de nitrilo · ALM-01 · A-01-02 · lote LT-7')).toBeInTheDocument()
    expect(rows[0].querySelector('.figs')?.textContent).toMatch(/^Kárdex 1200Saldo 1201,5Diferencia \+1,5desde /)
    // del total del producto: sin posición
    expect(within(rows[1]).getByText('Descuadre en el total de SKU-9')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Cajas · todas las posiciones')).toBeInTheDocument()
    expect(within(rows[1]).getByText('−2')).toBeInTheDocument()

    expect(within(section).getByRole('link', { name: 'Revisar el descuadre de L14-105022 (ALM-01 · A-01-02 · lote LT-7)' })).toHaveAttribute(
      'href',
      `/warehouse/kardex?tab=reconciliation&discrepancy=${PUBLIC_ID}`,
    )
    expect(within(section).getByRole('link', { name: 'Ver todos (7)' })).toHaveAttribute('href', '/warehouse/kardex?tab=reconciliation&status=OPEN')
    expect(screen.queryByText('Todo en orden')).not.toBeInTheDocument()
  })

  it('"Revisar" abre la ruta del aviso con sus parámetros (el Kárdex en Conciliación con ese descuadre)', async () => {
    attention = { total: 1, items: [discrepancy()], groups: [{ ...GROUP, total: 1 }] }
    const user = userEvent.setup()
    renderPulse()
    const section = await attentionSection()
    await user.click(await within(section).findByRole('link', { name: /^Revisar/ }))
    expect(screen.getByTestId('where')).toHaveTextContent(`/warehouse/kardex?tab=reconciliation&discrepancy=${PUBLIC_ID}`)
  })

  it('sin pendientes: "Todo en orden", sin conteo ni "Ver todos"', async () => {
    renderPulse()
    const section = await attentionSection()
    expect(await within(section).findByText('Todo en orden')).toBeInTheDocument()
    expect(within(section).getByText('No hay nada pendiente de revisar.')).toBeInTheDocument()
    expect(within(section).queryByText(/pendientes?$/)).not.toBeInTheDocument()
    expect(within(section).queryByRole('link')).not.toBeInTheDocument()
  })

  it('sin pulse.attention el API no devuelve ATTENTION: ni panel ni consulta de avisos', async () => {
    pulseDto = pulse([panel('INDICATORS', 20)])
    renderPulse()
    expect(await screen.findByText('Ventas')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Necesita tu atención' })).not.toBeInTheDocument()
    expect(mock.calls).not.toContain('/api/v1/analytics/attention')
  })

  it('un 403 de /analytics/attention no saca del Pulso: el panel no se pinta', async () => {
    attention = new Response(JSON.stringify({ status: 403, code: 'forbidden', title: 'No tiene permiso.' }), {
      status: 403,
      headers: { 'Content-Type': 'application/problem+json' },
    })
    renderPulse()
    expect(await screen.findByText('Ventas')).toBeInTheDocument()
    await waitFor(() => expect(mock.calls).toContain('/api/v1/analytics/attention'))
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Necesita tu atención' })).not.toBeInTheDocument())
    expect(onAccessDenied).not.toHaveBeenCalled()
  })

  it('oculto en el Pulso (Organizar): no se pinta ni consulta', async () => {
    pulseDto = pulse([panel('INDICATORS', 20), panel('ATTENTION', 5, false)])
    renderPulse()
    expect(await screen.findByText('Ventas')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Necesita tu atención' })).not.toBeInTheDocument()
    expect(mock.calls).not.toContain('/api/v1/analytics/attention')
  })
})

describe('attention.ts (lógica pura)', () => {
  const t = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
  const tEn = (key: string, params?: Record<string, string | number>) => translate('en', key, params)

  it('attentionHref: ruta + parámetros (sin vacíos); sin ruta, null', () => {
    expect(attentionHref('/warehouse/kardex', { tab: 'reconciliation', discrepancy: 'abc' })).toBe('/warehouse/kardex?tab=reconciliation&discrepancy=abc')
    expect(attentionHref('/x', { a: '', b: 'c d' })).toBe('/x?b=c+d')
    expect(attentionHref('/x', null)).toBe('/x')
    expect(attentionHref(null, { a: 'b' })).toBeNull()
  })

  it('tono → clase de la maqueta; cifras con separador del idioma y diferencia con signo', () => {
    expect(attentionToneClass('danger')).toBe('tone-danger')
    expect(attentionToneClass('warn')).toBe('tone-warn')
    expect(attentionToneClass('info')).toBe('tone-flow')
    expect(formatQty('1234.5', 'es')).toBe('1234,5')
    expect(formatQty('1234.5', 'en')).toBe('1,234.5')
    expect(formatSigned('2', 'en')).toBe('+2')
    expect(formatSigned('-0.25', 'en')).toBe('−0.25')
    expect(formatSigned('0', 'en')).toBe('0')
    expect(formatSigned('', 'en')).toBe('')
  })

  it('texto del descuadre en inglés, sin lote; tipo desconocido = su código', () => {
    const now = new Date('2026-09-28T20:00:00Z')
    const text = attentionRowText(discrepancy({ lot: '' }), tEn, 'en', now)
    expect(text.title).toBe('Discrepancy in L14-105022')
    expect(text.detail).toBe('Guantes de nitrilo · ALM-01 · A-01-02')
    expect(text.figures.map((f) => `${f.label} ${f.value}`)).toEqual(['Ledger 1,200', 'Balance 1,201.5', 'Difference +1.5'])
    expect(text.since).toMatch(/^since /)
    expect(text.reviewLabel).toBe('Review the discrepancy of L14-105022 (ALM-01 · A-01-02)')

    const other = attentionRowText({ code: 'COD_OVERDUE', tone: 'warn', params: {}, route: '/cod', sinceUtc: null }, t, 'es', now)
    expect(other).toEqual({ title: 'COD_OVERDUE', detail: '', figures: [], since: '', reviewLabel: 'Revisar COD_OVERDUE' })
  })
})
