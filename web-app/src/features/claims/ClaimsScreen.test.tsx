// Pruebas de «Reclamaciones» (Lote 111) sobre un fetch simulado: la cola de faltantes, marcar perdidas, crear la reclamación, resolverla y los permisos.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ClaimsScreen from './ClaimsScreen'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  pieceStatus: 'TO_VERIFY',
  claimStatus: 'CLAIMED',
  claimed: false,
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    const m = req.method
    if (m === 'GET' && p === '/api/v1/claims/to-verify') return json({ items: mock.claimed ? [] : [ORDER()], total: mock.claimed ? 0 : 1, skip: 0, take: 25 })
    if (m === 'POST' && p === '/api/v1/claims/lost') {
      mock.pieceStatus = 'LOST'
      return json(ORDER())
    }
    if (m === 'POST' && p === '/api/v1/claims') {
      const b = body as { declaredValue?: number }
      if (b.declaredValue != null && b.declaredValue < 0) return problem(400, 'El valor declarado no puede ser negativo.')
      mock.claimed = true
      return json(CLAIM())
    }
    if (m === 'GET' && p === '/api/v1/claims') return json({ items: [LIST_ITEM()], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === `/api/v1/claims/${CLAIM_ID}`) return json(CLAIM())
    if (m === 'POST' && p === `/api/v1/claims/${CLAIM_ID}/resolve`) {
      mock.claimStatus = 'RESOLVED'
      return json(CLAIM())
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
function problem(status: number, title: string) {
  return new Response(JSON.stringify({ title, status, code: 'validation' }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

const ORD = '66666666-0000-0000-0000-000000000006'
const CLAIM_ID = '77777777-0000-0000-0000-000000000007'
const PIECE = { packageNumber: 'ACME00112345', lineDescription: 'Cajas' }
const ORDER = () => ({
  orderPublicId: ORD, orderNumber: 'ORD-00001', waybillNumber: 'ACME001251018001', clientName: 'Farmacia Central', orderStatus: 'IN_TRANSIT', orderStatusLabel: 'En tránsito', totalPieces: 4,
  pieces: [{ ...PIECE, status: mock.pieceStatus, statusLabel: mock.pieceStatus === 'LOST' ? 'Perdido' : 'Por verificar', missingAtUtc: '2026-10-18T10:00:00Z', claimNumber: null }],
})
const LIST_ITEM = () => ({
  publicId: CLAIM_ID, number: 'RCL-00001', status: mock.claimStatus, statusLabel: mock.claimStatus === 'RESOLVED' ? 'Resuelta' : 'Reclamada', orderPublicId: ORD, orderNumber: 'ORD-00001',
  waybillNumber: 'ACME001251018001', clientName: 'Farmacia Central', description: '1 × Cajas', pieces: 1, declaredValue: 250, createdAtUtc: '2026-10-18T11:00:00Z',
})
const CLAIM = () => ({
  ...LIST_ITEM(), notes: 'Llamar al cliente', resolution: mock.claimStatus === 'RESOLVED' ? 'PAID' : null, amountPaid: mock.claimStatus === 'RESOLVED' ? 80 : null, rowVersion: 'AAAA',
  pieces: [{ ...PIECE, status: 'CLAIMED', statusLabel: 'Reclamada', missingAtUtc: '2026-10-18T10:00:00Z', claimNumber: 'RCL-00001' }],
})

const ALL = ['claims.view', 'claims.manage']
const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(ALL),
  modules: new Set(['LTL_GROUND']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(permissions: string[] = ALL) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={new Set(permissions)} modules={SESSION.modules}>
          <MemoryRouter>
            <ClaimsScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

describe('Reclamaciones', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.pieceStatus = 'TO_VERIFY'
    mock.claimStatus = 'CLAIMED'
    mock.claimed = false
  })

  it('la cola muestra los envíos con faltantes y la búsqueda llega al API', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByText('ORD-00001')).toBeInTheDocument()
    expect(screen.getByText('1 de 4')).toBeInTheDocument()
    await user.type(screen.getByPlaceholderText('Buscar por envío, conduce o pieza'), 'ACME0011')
    await waitFor(() => expect(sent('GET', '/api/v1/claims/to-verify').some((r) => r.search.includes('search=ACME0011'))).toBe(true))
  })

  it('marcar como perdidas y crear la reclamación manda los cuerpos y abre la reclamación', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('ORD-00001'))
    const dialog = await screen.findByRole('dialog', { name: /Faltantes de ORD-00001/ })
    expect(await within(dialog).findByText('Por verificar')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Marcar 1 como perdidas' }))
    expect(sent('POST', '/api/v1/claims/lost')[0].body).toEqual({ orderPublicId: ORD })
    await user.type(await within(dialog).findByLabelText('Valor declarado'), '250')
    await user.type(within(dialog).getByLabelText('Notas'), 'Llamar al cliente')
    await user.click(within(dialog).getByRole('button', { name: 'Crear reclamación (1)' }))
    await waitFor(() => expect(sent('POST', '/api/v1/claims')).toHaveLength(1))
    expect(sent('POST', '/api/v1/claims')[0].body).toEqual({ orderPublicId: ORD, declaredValue: 250, notes: 'Llamar al cliente' })
    expect(await screen.findByRole('dialog', { name: /Reclamación RCL-00001/ })).toBeInTheDocument()
  })

  it('un valor declarado negativo se rechaza en la pantalla sin llamar al API', async () => {
    const user = userEvent.setup()
    mock.pieceStatus = 'LOST'
    wrap()
    await user.click(await screen.findByText('ORD-00001'))
    const dialog = await screen.findByRole('dialog', { name: /Faltantes de ORD-00001/ })
    await user.type(await within(dialog).findByLabelText('Valor declarado'), '-5')
    await user.click(within(dialog).getByRole('button', { name: 'Crear reclamación (1)' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El valor declarado no puede ser negativo.')
    expect(sent('POST', '/api/v1/claims')).toHaveLength(0)
  })

  it('resolver pide la resolución y, si es pagada, el monto', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Reclamaciones' }))
    await user.click(await screen.findByText('RCL-00001'))
    const dialog = await screen.findByRole('dialog', { name: /Reclamación RCL-00001/ })
    await user.click(await within(dialog).findByRole('button', { name: 'Resolver' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Indique la resolución: pagada, rechazada o cerrada.')
    await user.selectOptions(within(dialog).getByLabelText('Resolución'), 'PAID')
    await user.click(within(dialog).getByRole('button', { name: 'Resolver' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Indique el monto pagado.')
    await user.type(within(dialog).getByLabelText('Monto pagado'), '80')
    await user.click(within(dialog).getByRole('button', { name: 'Resolver' }))
    await waitFor(() => expect(sent('POST', `/api/v1/claims/${CLAIM_ID}/resolve`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/claims/${CLAIM_ID}/resolve`)[0].body).toEqual({ resolution: 'PAID', amountPaid: 80, notes: null, rowVersion: 'AAAA' })
  })

  it('sin claims.manage no se ven las acciones', async () => {
    const user = userEvent.setup()
    wrap(['claims.view'])
    await user.click(await screen.findByText('ORD-00001'))
    const dialog = await screen.findByRole('dialog', { name: /Faltantes de ORD-00001/ })
    await within(dialog).findByText('Por verificar')
    expect(within(dialog).queryByRole('button', { name: /Marcar/ })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: /Crear reclamación/ })).toBeNull()
  })
})
