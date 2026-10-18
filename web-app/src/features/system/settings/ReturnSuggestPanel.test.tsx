// Lote 113 — Ajustes → General → «Devolución»: se lee el valor, se guarda de 0 a 20 con admin.tenant y sin permiso es solo lectura.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { ReturnSuggestPanel } from './ReturnSuggestPanel'

const mock = vi.hoisted(() => ({ after: 2, requests: [] as { method: string; path: string; body: unknown }[] }))

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    if (url.pathname !== '/api/v1/tenant/return-settings') return new Response('{}', { status: 404 })
    if (req.method === 'PUT') mock.after = (body as { suggestAfterAttempts: number }).suggestAfterAttempts
    return new Response(JSON.stringify({ suggestAfterAttempts: mock.after }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function wrap(canEdit = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ReturnSuggestPanel canEdit={canEdit} />
    </QueryClientProvider>,
  )
}

describe('Devolver tras N intentos (Ajustes)', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.after = 2
    mock.requests.length = 0
  })

  it('muestra el valor de la compañía y lo guarda', async () => {
    const user = userEvent.setup()
    wrap()
    const input = await screen.findByLabelText('Sugerir devolver con este número de intentos fallidos')
    expect(input).toHaveValue(2)
    await user.clear(input)
    await user.type(input, '3')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PUT')?.body).toEqual({ suggestAfterAttempts: 3 })
  })

  it('un valor fuera de 0 a 20 no llama al API', async () => {
    const user = userEvent.setup()
    wrap()
    const input = await screen.findByLabelText('Sugerir devolver con este número de intentos fallidos')
    await user.clear(input)
    await user.type(input, '25')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('Escriba un número de 0 a 20 (0 = sin sugerencia).')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.method === 'PUT')).toBe(false)
  })

  it('sin admin.tenant es solo lectura', async () => {
    wrap(false)
    const input = await screen.findByLabelText('Sugerir devolver con este número de intentos fallidos')
    expect(input).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
  })
})
