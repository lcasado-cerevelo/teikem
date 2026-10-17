// Lote 104 — «Mensajes de clientes»: lista, abrir una conversación (marca leída), enviar con validación y error del servidor.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ClientMessagesScreen from './ClientMessagesScreen'

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
    if (body instanceof Response) return body
    if (body === null) return new Response(null, { status: 204 })
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const conversation = {
  publicId: '11111111-1111-1111-1111-111111111111',
  clientPublicId: 'c-1',
  clientName: 'Acme Corp',
  personId: 5,
  personName: 'Ana Pérez',
  personEmail: 'ana@acme.com',
  hasMore: false,
  messages: [
    { id: 1, senderKind: 'CLIENT', senderName: 'Ana Pérez', body: 'Hola, ¿cómo va mi orden?', createdAtUtc: '2026-10-10T15:00:00', mine: false },
    { id: 2, senderKind: 'STAFF', senderName: 'Lucía', body: 'En camino.', createdAtUtc: '2026-10-10T15:05:00', mine: true },
  ],
}
const summaries = [
  { publicId: conversation.publicId, clientPublicId: 'c-1', clientName: 'Acme Corp', personId: 5, personName: 'Ana Pérez', personEmail: 'ana@acme.com', lastMessagePreview: 'En camino.', lastMessageAtUtc: '2026-10-10T15:05:00', lastSenderKind: 'STAFF', unread: 3 },
  { publicId: '22222222-2222-2222-2222-222222222222', clientPublicId: 'c-2', clientName: 'Beta SA', personId: 6, personName: 'Beto Ruiz', personEmail: 'beto@beta.com', lastMessagePreview: 'Gracias', lastMessageAtUtc: '2026-10-09T09:00:00', lastSenderKind: 'CLIENT', unread: 0 },
]

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/client-messages/conversations' && method === 'GET') return { items: summaries, total: 2, skip: 0, take: 30 }
  if (p === `/api/v1/client-messages/conversations/${conversation.publicId}` && method === 'GET') return conversation
  if (p.endsWith('/read')) return null
  if (p.endsWith('/messages') && method === 'POST') return { id: 3, senderKind: 'STAFF', senderName: 'Lucía', body: 'Listo', createdAtUtc: '2026-10-10T16:00:00', mine: true }
  if (p === '/api/v1/clients') return []
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(initial = '/portal/messages') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[initial]}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={['clientmessages.reply']} modules={['CLIENT_PORTAL']}>
          <ClientMessagesScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
const callsTo = (method: string, suffix: string) => mock.calls.filter((c) => c.method === method && c.url.pathname.endsWith(suffix))

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
})

describe('Mensajes de clientes', () => {
  it('lista las conversaciones con cliente, vista previa y no leídos', async () => {
    wrap()
    expect(await screen.findByText('Ana Pérez')).toBeInTheDocument()
    expect(screen.getByText('Beto Ruiz')).toBeInTheDocument()
    expect(screen.getByText('Acme Corp')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('Elija una conversación de la lista.')).toBeInTheDocument()
  })

  it('al abrir una conversación muestra las burbujas y la marca como leída', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: /Ana Pérez/ }))
    expect(await screen.findByText('Hola, ¿cómo va mi orden?')).toBeInTheDocument()
    expect(screen.getByText('En camino.', { selector: '.cm-body' }).closest('.cm-msg')).toHaveClass('staff')
    expect(screen.getByText('Hola, ¿cómo va mi orden?').closest('.cm-msg')).toHaveClass('person')
    await waitFor(() => expect(callsTo('POST', `${conversation.publicId}/read`)).toHaveLength(1))
  })

  it('valida el mensaje vacío, envía el texto y agrega la burbuja', async () => {
    const user = userEvent.setup()
    wrap(`/portal/messages?conversation=${conversation.publicId}`)
    const box = await screen.findByPlaceholderText('Escriba su respuesta…')
    await user.click(screen.getByRole('button', { name: 'Enviar' }))
    expect(await screen.findByText('Escriba el mensaje.')).toBeInTheDocument()
    expect(callsTo('POST', '/messages')).toHaveLength(0)
    await user.type(box, 'Listo')
    await user.click(screen.getByRole('button', { name: 'Enviar' }))
    await waitFor(() => expect(callsTo('POST', '/messages')).toHaveLength(1))
    expect(callsTo('POST', '/messages')[0].body).toEqual({ body: 'Listo' })
    expect(await within(document.querySelector('.cm-log') as HTMLElement).findByText('Listo')).toBeInTheDocument()
  })

  it('un error del servidor al enviar se muestra con su título', async () => {
    const user = userEvent.setup()
    mock.override = (c) => (c.method === 'POST' && c.url.pathname.endsWith('/messages') ? problem(429, 'Demasiados mensajes seguidos; espere un rato para volver a escribir.', 'error') : undefined)
    wrap(`/portal/messages?conversation=${conversation.publicId}`)
    await user.type(await screen.findByPlaceholderText('Escriba su respuesta…'), 'Hola')
    await user.click(screen.getByRole('button', { name: 'Enviar' }))
    expect(await screen.findByText('Demasiados mensajes seguidos; espere un rato para volver a escribir.')).toBeInTheDocument()
  })
})
