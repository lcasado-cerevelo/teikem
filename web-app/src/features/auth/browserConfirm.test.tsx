// Computadoras de confianza (Lote 100): el login manda la marca de esta computadora y, si no se reconoce, avisa que se mandó un correo; el enlace del correo la confirma; y «Mis computadoras de confianza» las lista y las quita.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getBrowserToken } from '../../kernel/auth/browserToken'
import { clearTokens, setMfaChallenge } from '../../kernel/auth/tokens'
import { setLang } from '../../kernel/i18n/i18n'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' } })

let calls: { method: string; path: string; body: unknown; headers: Headers }[]
let loginReply: () => Response
let confirmReply: () => Response

async function renderAppAt(path: string) {
  window.history.pushState({}, '', path)
  vi.resetModules()
  const { default: App } = await import('../../app/App')
  return render(<App />)
}

describe('browserToken', () => {
  it('crea una marca de 64 caracteres y devuelve siempre la misma en este navegador', () => {
    localStorage.removeItem('teikem.browser')
    const a = getBrowserToken()
    expect(a).toMatch(/^[0-9a-f]{64}$/)
    expect(getBrowserToken()).toBe(a)
    expect(localStorage.getItem('teikem.browser')).toBe(a)
  })
})

describe('Confirmar la computadora', () => {
  beforeEach(() => {
    setLang('es')
    clearTokens()
    setMfaChallenge(null)
    calls = []
    loginReply = () => json({ status: 'browser_confirmation_required', mfaEnrollmentRequired: false, browser: { email: 'y***@t.local', sent: true } })
    confirmReply = () => new Response(null, { status: 204 })
    vi.stubEnv('VITE_API_URL', 'http://api.test')
    vi.stubGlobal(
      'fetch',
      vi.fn(async (req: Request) => {
        const path = new URL(req.url).pathname
        const text = await req.text()
        calls.push({ method: req.method, path, body: text ? JSON.parse(text) : undefined, headers: req.headers })
        if (path === '/api/v1/auth/login') return loginReply()
        if (path === '/api/v1/auth/confirm-browser') return confirmReply()
        return new Response(null, { status: 404 })
      }),
    )
  })
  afterEach(() => {
    clearTokens()
    setMfaChallenge(null)
    localStorage.setItem('teikem.lang', 'es')
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('el login manda la marca de la computadora y, si no se reconoce, dice que se mandó el correo', async () => {
    await renderAppAt('/login')
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('Correo electrónico'), 'yo@t.local')
    await user.type(screen.getByLabelText('Contraseña', { exact: true }), 'Clave-Segura-2026x')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByRole('heading', { name: 'Confirme esta computadora' })).toBeInTheDocument()
    expect(screen.getByText('Le enviamos un correo a y***@t.local.')).toBeInTheDocument()
    const login = calls.find((c) => c.path === '/api/v1/auth/login')!
    expect((login.body as { trustedBrowserToken: string }).trustedBrowserToken).toBe(getBrowserToken())
    expect(window.location.pathname).toBe('/login')
    // «Volver» regresa al formulario
    await user.click(screen.getByRole('button', { name: 'Volver a la pantalla de entrada' }))
    expect(await screen.findByLabelText('Correo electrónico')).toBeInTheDocument()
  })

  it('si el correo no pudo salir lo dice', async () => {
    loginReply = () => json({ status: 'browser_confirmation_required', browser: { email: 'y***@t.local', sent: false, message: 'No se pudo enviar el correo de confirmación.' } })
    await renderAppAt('/login')
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('Correo electrónico'), 'yo@t.local')
    await user.type(screen.getByLabelText('Contraseña', { exact: true }), 'Clave-Segura-2026x')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByText('No se pudo enviar el correo de confirmación.')).toBeInTheDocument()
  })

  it('el enlace del correo confirma la computadora y lleva de vuelta a la entrada', async () => {
    await renderAppAt('/confirm-browser?token=abc123')
    expect(await screen.findByText('Listo: esta computadora quedó confirmada.')).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/auth/confirm-browser')?.body).toEqual({ token: 'abc123' })
    expect(screen.getByRole('link', { name: 'Ir a la pantalla de entrada' })).toHaveAttribute('href', '/login')
  })

  it('un enlace vencido muestra el mensaje del servidor', async () => {
    confirmReply = () => json({ title: 'El enlace no es válido o venció. Vuelva a intentar entrar para recibir uno nuevo.' }, 400)
    await renderAppAt('/confirm-browser?token=viejo')
    expect(await screen.findByRole('alert')).toHaveTextContent('El enlace no es válido o venció.')
  })

  it('sin la marca en el enlace no llama al servidor', async () => {
    await renderAppAt('/confirm-browser')
    expect(await screen.findByRole('alert')).toHaveTextContent('El enlace no es válido o venció.')
    expect(calls.some((c) => c.path === '/api/v1/auth/confirm-browser')).toBe(false)
  })
})

describe('Mis computadoras de confianza', () => {
  beforeEach(() => {
    setLang('es')
    calls = []
  })
  afterEach(() => {
    vi.restoreAllMocks()
    vi.resetModules()
  })

  it('lista las computadoras, marca la actual y quita una', async () => {
    vi.doMock('../../kernel/api/client', async (importOriginal) => {
      const actual = await importOriginal<typeof import('../../kernel/api/client')>()
      const fetch = async (req: Request) => {
        const url = new URL(req.url)
        calls.push({ method: req.method, path: url.pathname, body: undefined, headers: req.headers })
        if (req.method === 'GET' && url.pathname === '/api/v1/auth/trusted-browsers')
          return json([
            { publicId: 'b1', label: 'Chrome en Windows', ipAddress: '10.0.0.1', createdAtUtc: '2026-10-01T10:00:00Z', lastUsedAtUtc: '2026-10-17T10:00:00Z', expiresAtUtc: '2026-11-16T10:00:00Z', isCurrent: true },
            { publicId: 'b2', label: 'Safari en iPhone', ipAddress: '10.0.0.2', createdAtUtc: '2026-10-02T10:00:00Z', lastUsedAtUtc: '2026-10-10T10:00:00Z', expiresAtUtc: '2026-11-09T10:00:00Z', isCurrent: false },
          ])
        if (req.method === 'DELETE') return new Response(null, { status: 204 })
        return new Response(null, { status: 404 })
      }
      return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
    })
    const { TrustedBrowsersPanel } = await import('../account/TrustedBrowsersPanel')
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <TrustedBrowsersPanel />
      </QueryClientProvider>,
    )
    expect(await screen.findByText('Safari en iPhone')).toBeInTheDocument()
    expect(screen.getByText('Esta computadora')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'GET')?.headers.get('X-Trusted-Browser')).toBe(getBrowserToken())

    const user = userEvent.setup()
    const row = screen.getAllByRole('row').find((r) => r.textContent?.includes('Safari en iPhone'))!
    await user.click(within(row).getByRole('button', { name: 'Quitar' }))
    // el diálogo de confirmación: el último botón «Quitar» es el suyo
    await user.click(screen.getAllByRole('button', { name: 'Quitar' }).at(-1)!)
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true))
  })
})
