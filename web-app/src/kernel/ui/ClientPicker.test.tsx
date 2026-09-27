import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { ClientPicker } from './ClientPicker'

// El cliente de la app se sustituye por uno con la misma política sobre un fetch simulado.
const mock = vi.hoisted(() => ({ requests: [] as URL[], handler: (_url: URL): unknown => [] }))
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    const body = mock.handler(url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const CLIENTS = [
  { id: 1, publicId: '11111111-1111-1111-1111-111111111111', code: 'ACME', name: 'Acme Corp', isActive: true },
  { id: 2, publicId: '22222222-2222-2222-2222-222222222222', code: 'ACMX', name: 'Acme México', isActive: false },
  { id: 3, publicId: '33333333-3333-3333-3333-333333333333', code: 'GLOB', name: 'Globex', isActive: true },
]

function route(url: URL): unknown {
  if (url.pathname === '/api/v1/clients') {
    const search = (url.searchParams.get('search') ?? '').toLowerCase()
    const inactive = url.searchParams.get('includeInactive') === 'true'
    return CLIENTS.filter((c) => (inactive || c.isActive) && (`${c.code} ${c.name}`.toLowerCase().includes(search)))
  }
  const detail = CLIENTS.find((c) => url.pathname === `/api/v1/clients/${c.publicId}`)
  if (detail) return detail
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.handler = route
})

describe('ClientPicker', () => {
  it('busca en el API con search= y muestra "Code · Name"; al elegir devuelve el publicId', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<ClientPicker value={null} onChange={onChange} aria-label="Cliente" />)

    await user.type(screen.getByRole('combobox', { name: 'Cliente' }), 'acm')
    await waitFor(() => expect(mock.requests.some((u) => u.searchParams.get('search') === 'acm')).toBe(true))
    await waitFor(() => expect(screen.queryByText('Globex')).toBeNull())
    const option = screen.getByRole('option', { name: /ACME · Acme Corp/ })
    const last = mock.requests.filter((u) => u.pathname === '/api/v1/clients').at(-1)
    expect(last?.searchParams.get('includeInactive')).toBe('false')
    // sin includeInactive el inactivo no aparece
    expect(screen.queryByText(/Acme México/)).toBeNull()

    await user.click(option)
    expect(onChange).toHaveBeenCalledWith(CLIENTS[0].publicId, expect.objectContaining({ code: 'ACME' }))
  })

  it('respeta includeInactive y marca el inactivo', async () => {
    const user = userEvent.setup()
    wrap(<ClientPicker value={null} onChange={vi.fn()} includeInactive aria-label="Cliente" />)
    await user.type(screen.getByRole('combobox', { name: 'Cliente' }), 'acm')
    await waitFor(() => expect(mock.requests.some((u) => u.searchParams.get('search') === 'acm')).toBe(true))
    const option = await screen.findByRole('option', { name: /ACMX · Acme México/ })
    expect(option).toHaveTextContent('Inactivo')
    expect(mock.requests.filter((u) => u.pathname === '/api/v1/clients').at(-1)?.searchParams.get('includeInactive')).toBe('true')
  })

  it('con un valor inicial pide la ficha para mostrar "Code · Name"', async () => {
    wrap(<ClientPicker value={CLIENTS[2].publicId} onChange={vi.fn()} aria-label="Cliente" />)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Cliente' })).toHaveValue('GLOB · Globex'))
  })

  it('sin coincidencias lo dice', async () => {
    const user = userEvent.setup()
    wrap(<ClientPicker value={null} onChange={vi.fn()} aria-label="Cliente" />)
    await user.type(screen.getByRole('combobox', { name: 'Cliente' }), 'zzz')
    expect(await screen.findByText('No hay clientes que coincidan.')).toBeInTheDocument()
  })
})
