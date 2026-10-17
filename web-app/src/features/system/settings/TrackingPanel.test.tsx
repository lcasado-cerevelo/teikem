// Lote 103 — Ajustes → General → «Seguimiento público»: encender con un identificador, apagar, mostrar el enlace y los errores del servidor, y solo lectura sin admin.tenant.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { TrackingPanel } from './TrackingPanel'

const mock = vi.hoisted(() => ({ slug: null as string | null, requests: [] as { method: string; path: string; body: unknown }[], failWith: null as null | { status: number; body: unknown } }))

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    if (url.pathname !== '/api/v1/tenant/tracking') return new Response('{}', { status: 404 })
    if (req.method === 'GET') return json({ slug: mock.slug, enabled: mock.slug !== null })
    if (mock.failWith) return new Response(JSON.stringify(mock.failWith.body), { status: mock.failWith.status, headers: { 'Content-Type': 'application/problem+json' } })
    const slug = (body as { slug: string }).slug
    mock.slug = slug === '' ? null : slug
    return json({ slug: mock.slug, enabled: mock.slug !== null })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function wrap(canEdit = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <TrackingPanel canEdit={canEdit} />
    </QueryClientProvider>,
  )
}

describe('Seguimiento público (Ajustes)', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.slug = null
    mock.requests.length = 0
    mock.failWith = null
  })

  it('apagado por omisión: se enciende con un identificador (en minúsculas) y muestra el enlace', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByText('Apagado')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Encender' })).toBeDisabled()   // sin identificador no hay qué encender
    await user.type(screen.getByLabelText('Identificador de la compañía'), 'Mi-Compania')
    await user.click(screen.getByRole('button', { name: 'Encender' }))
    await waitFor(() => expect(mock.requests.find((r) => r.method === 'PUT')?.body).toEqual({ slug: 'mi-compania' }))
    expect(await screen.findByText('Encendido')).toBeInTheDocument()
    expect((screen.getByLabelText('Enlace para compartir') as HTMLInputElement).value).toBe('/track/mi-compania')
  })

  it('encendido: «Apagar» quita el identificador', async () => {
    mock.slug = 'advance'
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByText('Encendido')).toBeInTheDocument()
    expect((screen.getByLabelText('Identificador de la compañía') as HTMLInputElement).value).toBe('advance')
    await user.click(screen.getByRole('button', { name: 'Apagar' }))
    await waitFor(() => expect(mock.requests.find((r) => r.method === 'PUT')?.body).toEqual({ slug: '' }))
    expect(await screen.findByText('Apagado')).toBeInTheDocument()
  })

  it('un identificador que no sirve muestra el mensaje exacto del servidor', async () => {
    mock.failWith = { status: 400, body: { title: 'Datos inválidos.', errors: { slug: ['Ese identificador no está disponible.'] } } }
    const user = userEvent.setup()
    wrap()
    await user.type(await screen.findByLabelText('Identificador de la compañía'), 'api')
    await user.click(screen.getByRole('button', { name: 'Encender' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Ese identificador no está disponible.')
  })

  it('un identificador de otra compañía (409) también se explica', async () => {
    mock.failWith = { status: 409, body: { title: 'Ese identificador ya lo usa otra compañía.' } }
    const user = userEvent.setup()
    wrap()
    await user.type(await screen.findByLabelText('Identificador de la compañía'), 'otra')
    await user.click(screen.getByRole('button', { name: 'Encender' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Ese identificador ya lo usa otra compañía.')
  })

  it('sin admin.tenant es solo lectura', async () => {
    mock.slug = 'advance'
    wrap(false)
    expect(await screen.findByText('Encendido')).toBeInTheDocument()
    expect(screen.getByLabelText('Identificador de la compañía')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Apagar' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Encender' })).not.toBeInTheDocument()
  })
})
