// Lote 245 — Cadenas: lista y ficha, alta (éxito y 409 de nombre), alta masiva «Agregar por nombre» (simular → excluir → aplicar; 409 de huella → volver a simular
// conservando las exclusiones) y solo lectura sin sales.chain. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ChainsScreen from './ChainsScreen'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], override: null as null | ((call: Call) => unknown) }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = mock.override?.(call) ?? route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ title, code, status }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const LIST = [
  { publicId: 'ch-1', name: 'Walgreens', notes: 'Farmacias', isActive: true, clientCount: 2 },
  { publicId: 'ch-2', name: 'CVS', notes: null, isActive: false, clientCount: 0 },
]
const DETAIL = { ...LIST[0], inactiveClientCount: 1, createdAtUtc: '2026-11-01T10:00:00', updatedAtUtc: null, rowVersion: 'AAA=' }
const CLIENTS = [
  { clientPublicId: 'c-1', code: 'W1', name: 'Walgreens 101', status: 'ACTIVE', statusLabel: 'Activo', isActive: true, salesRepName: 'Ana' },
  { clientPublicId: 'c-2', code: 'W2', name: 'Walgreens 102', status: 'ACTIVE', statusLabel: 'Activo', isActive: true, salesRepName: null },
]
const PREVIEW = {
  prefix: 'Walgreens',
  wholeWord: true,
  moveFromOtherChains: false,
  matched: 3,
  toAssign: 2,
  toMove: 0,
  alreadyInChain: 1,
  skippedOtherChain: 0,
  excluded: 0,
  tooMany: false,
  maxBulk: 2000,
  fingerprint: 'fp-1',
  rows: [
    { clientPublicId: 'n-1', code: 'W3', name: 'Walgreens 103', action: 'ASSIGN', currentChainPublicId: null, currentChainName: null },
    { clientPublicId: 'n-2', code: 'W4', name: 'Walgreens 104', action: 'ASSIGN', currentChainPublicId: null, currentChainName: null },
    { clientPublicId: 'c-1', code: 'W1', name: 'Walgreens 101', action: 'ALREADY', currentChainPublicId: 'ch-1', currentChainName: 'Walgreens' },
  ],
}

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/client-chains' && method === 'GET') return LIST.filter((c) => c.isActive || url.searchParams.get('includeInactive') === 'true')
  if (p === '/api/v1/client-chains' && method === 'POST') return { ...DETAIL, publicId: 'ch-new', name: 'Nueva', rowVersion: 'N=' }
  if (p === '/api/v1/client-chains/ch-1' && method === 'GET') return DETAIL
  if (p === '/api/v1/client-chains/ch-1' && method === 'PATCH') return { ...DETAIL, rowVersion: 'BBB=' }
  if (p === '/api/v1/client-chains/ch-1/clients' && method === 'GET') return { items: CLIENTS, total: CLIENTS.length }
  if (p === '/api/v1/client-chains/ch-1/bulk-assign/preview') return PREVIEW
  if (p === '/api/v1/client-chains/ch-1/bulk-assign/apply') return { assigned: 1, moved: 0, alreadyInChain: 1, skippedOtherChain: 0, excluded: 1 }
  if (p === '/api/v1/sales-analysis/by-chain/series') return { chainPublicId: 'ch-1', chainName: 'Walgreens', fromMonth: '2026-01', toMonth: '2026-10', months: [], topClients: [] }
  if (p === '/api/v1/clients') return []
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(permissions: string[], entry = '/catalog/chains') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['SALES_ORDERS']}>
          <ChainsScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const MANAGE = ['sales.chain', 'clients.read']
const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  window.localStorage.clear()
})

async function openBulk(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('heading', { name: 'Walgreens' })
  await user.click(await screen.findByRole('heading', { name: 'Agregar por nombre' }))
  await user.type(await screen.findByLabelText('El nombre empieza con'), 'Walgreens')
  await user.click(screen.getByRole('button', { name: 'Simular' }))
  await screen.findByText('Walgreens 103')
}

describe('Cadenas — lista y alta', () => {
  it('muestra la lista y abre la ficha de la primera con sus clientes', async () => {
    wrap(MANAGE)
    expect(await screen.findByRole('heading', { name: 'Walgreens' })).toBeInTheDocument()
    expect(await screen.findByText('Walgreens 101')).toBeInTheDocument()
    // las inactivas no vienen por omisión
    expect(callsTo('GET', '/api/v1/client-chains')[0].url.searchParams.get('includeInactive')).toBe('false')
  })

  it('crea una cadena y la deja elegida', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    await user.click(await screen.findByRole('button', { name: '+ Nueva cadena' }))
    const dlg = within(await screen.findByRole('dialog'))
    await user.type(dlg.getByLabelText(/Nombre/), 'Nueva')
    await user.click(dlg.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/client-chains')[0]?.body).toMatchObject({ name: 'Nueva' }))
  })

  it('el 409 de nombre repetido se muestra tal cual', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/client-chains' ? problem(409, 'Ya existe una cadena activa con ese nombre.', 'conflict') : undefined
    wrap(MANAGE)
    await user.click(await screen.findByRole('button', { name: '+ Nueva cadena' }))
    const dlg = within(await screen.findByRole('dialog'))
    await user.type(dlg.getByLabelText(/Nombre/), 'Walgreens')
    await user.click(dlg.getByRole('button', { name: 'Guardar' }))
    expect((await screen.findAllByText('Ya existe una cadena activa con ese nombre.')).length).toBeGreaterThan(0)
  })

  it('sin sales.chain es de solo lectura: no hay alta ni alta por nombre', async () => {
    wrap(['clients.read'])
    await screen.findByRole('heading', { name: 'Walgreens' })
    expect(screen.queryByRole('button', { name: '+ Nueva cadena' })).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Agregar por nombre' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Desactivar' })).toBeNull()
  })
})

describe('Cadenas — agregar por nombre', () => {
  it('simular → excluir → aplicar manda la huella y las exclusiones, sin volver a simular al excluir', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    await openBulk(user)
    expect(callsTo('POST', '/api/v1/client-chains/ch-1/bulk-assign/preview')).toHaveLength(1)
    expect(callsTo('POST', '/api/v1/client-chains/ch-1/bulk-assign/preview')[0].body).toEqual({ prefix: 'Walgreens', wholeWord: true, moveFromOtherChains: false })
    await user.click(screen.getByRole('checkbox', { name: 'Incluir a Walgreens 104' }))
    expect(await screen.findByText('Excluido')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/client-chains/ch-1/bulk-assign/preview')).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: 'Aplicar a 1 clientes' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar a 1 clientes' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/client-chains/ch-1/bulk-assign/apply')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/client-chains/ch-1/bulk-assign/apply')[0].body).toEqual({
      prefix: 'Walgreens',
      wholeWord: true,
      moveFromOtherChains: false,
      excludeClientPublicIds: ['n-2'],
      fingerprint: 'fp-1',
    })
  })

  it('sin simulación vigente «Aplicar» está deshabilitado', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    await openBulk(user)
    expect(screen.getByRole('button', { name: 'Aplicar a 2 clientes' })).toBeEnabled()
    await user.click(screen.getByRole('switch', { name: 'Mover también los que están en otra cadena' }))
    expect(screen.getByRole('button', { name: 'Aplicar a 2 clientes' })).toBeDisabled()
  })

  it('el 409 de huella muestra el mensaje y vuelve a simular conservando las exclusiones', async () => {
    const user = userEvent.setup()
    mock.override = ({ url }) => (url.pathname.endsWith('/bulk-assign/apply') ? problem(409, 'Los clientes cambiaron desde la simulación.', 'conflict') : undefined)
    wrap(MANAGE)
    await openBulk(user)
    await user.click(screen.getByRole('checkbox', { name: 'Incluir a Walgreens 104' }))
    await user.click(screen.getByRole('button', { name: 'Aplicar a 1 clientes' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar a 1 clientes' }))
    expect(await screen.findByText(/Los clientes cambiaron desde la simulación\./)).toBeInTheDocument()
    await waitFor(() => expect(callsTo('POST', '/api/v1/client-chains/ch-1/bulk-assign/preview')).toHaveLength(2))
    // la exclusión sigue vigente
    expect(screen.getByRole('checkbox', { name: 'Incluir a Walgreens 104' })).not.toBeChecked()
    expect(screen.getByRole('button', { name: 'Aplicar a 1 clientes' })).toBeEnabled()
  })
})
