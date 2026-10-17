// Lote 104 — panel «Equipo de servicio al cliente» de la ficha: permisos y módulo, fallback, designar equipo y escribir primero.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientServiceTeamPanel } from './ClientServiceTeamPanel'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], team: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = route(call)
    return new Response(JSON.stringify(body), { status: body && (body as { status?: number }).status === 404 ? 404 : 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/client-messages/clients/c-1/team') return mock.team
  if (p === '/api/v1/client-messages/team-candidates')
    return [
      { userId: 1, fullName: 'Lucía Soto', email: 'lucia@teikem.com' },
      { userId: 2, fullName: 'Mario Díaz', email: 'mario@teikem.com' },
    ]
  if (p === '/api/v1/client-messages/clients/c-1/people') return [{ personId: 5, fullName: 'Ana Pérez', email: 'ana@acme.com' }]
  if (p === '/api/v1/client-messages/conversations' && method === 'POST') return { publicId: 'conv-9', messages: [] }
  return { status: 404 }
}

function Where() {
  const loc = useLocation()
  return <div data-testid="where">{loc.pathname + loc.search}</div>
}

function wrap(permissions: string[], modules: string[] = ['CLIENT_PORTAL']) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={['/catalog/clients']}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={modules}>
          <ClientServiceTeamPanel clientPublicId="c-1" />
          <Where />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.team = { members: [{ userId: 1, fullName: 'Lucía Soto', email: 'lucia@teikem.com' }], usesFallback: false, fallbackCount: 0 }
})

describe('Equipo de servicio al cliente', () => {
  it('sin el permiso o sin el módulo no se pinta ni se pide nada', async () => {
    const { unmount } = wrap(['clients.read'])
    await new Promise((r) => setTimeout(r, 30))
    expect(screen.queryByText('Equipo de servicio al cliente')).toBeNull()
    unmount()
    wrap(['clientmessages.reply'], ['CATALOG'])
    await new Promise((r) => setTimeout(r, 30))
    expect(screen.queryByText('Equipo de servicio al cliente')).toBeNull()
    expect(mock.calls).toHaveLength(0)
  })

  it('con solo reply lista el equipo y no ofrece «Designar equipo»', async () => {
    wrap(['clientmessages.reply'])
    expect(await screen.findByText('Lucía Soto')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Designar equipo' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Escribirle a una persona' })).toBeInTheDocument()
  })

  it('sin equipo designado avisa que lo atiende quien puede responder, con la cantidad', async () => {
    mock.team = { members: [], usesFallback: true, fallbackCount: 4 }
    wrap(['clientmessages.reply'])
    expect(await screen.findByText(/sus mensajes los atiende cualquiera con permiso para responder \(4 persona\(s\)\)/)).toBeInTheDocument()
  })

  it('designar el equipo manda los usuarios marcados con PUT', async () => {
    const user = userEvent.setup()
    wrap(['clientmessages.reply', 'clientmessages.manage'])
    await user.click(await screen.findByRole('button', { name: 'Designar equipo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Designar el equipo de servicio' })
    const mario = await within(dialog).findByLabelText(/Mario Díaz/)
    expect(within(dialog).getByLabelText(/Lucía Soto/)).toBeChecked()
    await user.click(mario)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar equipo' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/client-messages/clients/c-1/team')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/client-messages/clients/c-1/team')[0].body).toEqual({ userIds: [1, 2] })
  })

  it('escribirle a una persona valida, abre la conversación y navega a Mensajes de clientes', async () => {
    const user = userEvent.setup()
    wrap(['clientmessages.reply'])
    await user.click(await screen.findByRole('button', { name: 'Escribirle a una persona' }))
    const dialog = await screen.findByRole('dialog', { name: 'Escribirle a una persona del cliente' })
    await user.click(within(dialog).getByRole('button', { name: 'Enviar' }))
    expect(await within(dialog).findByText('Elija a la persona.')).toBeInTheDocument()
    expect(within(dialog).getByText('Escriba el mensaje.')).toBeInTheDocument()
    await user.selectOptions(await within(dialog).findByLabelText('Persona'), '5')
    await user.type(within(dialog).getByLabelText('Mensaje'), 'Buen día')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/client-messages/conversations')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/client-messages/conversations')[0].body).toEqual({ clientPublicId: 'c-1', personId: 5, body: 'Buen día' })
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/portal/messages?conversation=conv-9'))
  })
})
