import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearTokens, getTokens, setTokens } from '../auth/tokens'
import { createApiClient, setAuthLostHandler, setStepUpHandler, unwrap } from './client'
import { ApiError } from './problem'

const BASE = 'http://api.test'

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const ME = { userId: 1, fullName: 'Admin', permissions: [], enabledModules: [] }

describe('cliente del API', () => {
  beforeEach(() => {
    setTokens({ accessToken: 'old-access', refreshToken: 'old-refresh', tenantId: 1 })
  })
  afterEach(() => {
    clearTokens()
    setAuthLostHandler(null)
    setStepUpHandler(null)
    vi.restoreAllMocks()
  })

  it('pone Authorization y Accept-Language', async () => {
    const fetch = vi.fn(async (_r: Request) => json(ME))
    const api = createApiClient({ baseUrl: BASE, fetch })
    await unwrap(api.GET('/api/v1/me'))
    const req = fetch.mock.calls[0][0]
    expect(req.headers.get('Authorization')).toBe('Bearer old-access')
    expect(req.headers.get('Accept-Language')).toMatch(/^(es|en)$/)
  })

  it('ante 401 refresca UNA sola vez (aunque fallen varias a la vez), rota tokens y reintenta', async () => {
    let refreshCalls = 0
    const fetch = vi.fn(async (req: Request) => {
      const path = new URL(req.url).pathname
      if (path === '/api/v1/auth/refresh') {
        refreshCalls++
        const body = (await req.json()) as { refreshToken: string }
        expect(body.refreshToken).toBe('old-refresh')
        return json({ accessToken: 'new-access', refreshToken: 'new-refresh', tenantId: 1 })
      }
      return req.headers.get('Authorization') === 'Bearer new-access' ? json(ME) : json({ title: 'No autenticado.', code: 'unauthorized' }, 401)
    })
    const api = createApiClient({ baseUrl: BASE, fetch })
    const [a, b] = await Promise.all([unwrap(api.GET('/api/v1/me')), unwrap(api.GET('/api/v1/me'))])
    expect(a.userId).toBe(1)
    expect(b.userId).toBe(1)
    expect(refreshCalls).toBe(1)
    expect(getTokens()?.refreshToken).toBe('new-refresh')
  })

  it('si el reintento vuelve a dar 401 no entra en bucle de refresh', async () => {
    let refreshCalls = 0
    const fetch = vi.fn(async (req: Request) => {
      if (new URL(req.url).pathname === '/api/v1/auth/refresh') {
        refreshCalls++
        return json({ accessToken: 'new-access', refreshToken: 'new-refresh', tenantId: 1 })
      }
      return json({ title: 'No autenticado.', code: 'unauthorized' }, 401)
    })
    const api = createApiClient({ baseUrl: BASE, fetch })
    await expect(unwrap(api.GET('/api/v1/me'))).rejects.toBeInstanceOf(ApiError)
    expect(refreshCalls).toBe(1)
    expect(fetch).toHaveBeenCalledTimes(3) // original + refresh + reintento
  })

  it('si el refresh falla avisa al shell (vuelve al login)', async () => {
    const lost = vi.fn()
    setAuthLostHandler(lost)
    const fetch = vi.fn(async (req: Request) =>
      new URL(req.url).pathname === '/api/v1/auth/refresh'
        ? json({ title: 'Sesión inválida.', code: 'unauthorized' }, 401)
        : json({ title: 'No autenticado.', code: 'unauthorized' }, 401),
    )
    const api = createApiClient({ baseUrl: BASE, fetch })
    await expect(unwrap(api.GET('/api/v1/me'))).rejects.toMatchObject({ status: 401 })
    expect(lost).toHaveBeenCalledTimes(1)
  })

  it('no refresca en el 401 del propio login (credenciales inválidas)', async () => {
    const fetch = vi.fn(async () => json({ title: 'Credenciales inválidas.', code: 'unauthorized' }, 401))
    const api = createApiClient({ baseUrl: BASE, fetch })
    await expect(unwrap(api.POST('/api/v1/auth/login', { body: { email: 'a@b.c', password: 'x' } }))).rejects.toMatchObject({
      title: 'Credenciales inválidas.',
    })
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('en /auth/reauth no refresca el 401 del servicio (contraseña incorrecta, con code)', async () => {
    const fetch = vi.fn(async () => json({ title: 'Contraseña incorrecta.', code: 'unauthorized' }, 401))
    const api = createApiClient({ baseUrl: BASE, fetch })
    await expect(unwrap(api.POST('/api/v1/auth/reauth', { body: { password: 'x' } }))).rejects.toMatchObject({
      title: 'Contraseña incorrecta.',
    })
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('en /auth/reauth refresca el 401 del esquema (access token vencido, sin cuerpo) y reintenta', async () => {
    const paths: string[] = []
    const fetch = vi.fn(async (req: Request) => {
      const path = new URL(req.url).pathname
      paths.push(path)
      if (path === '/api/v1/auth/refresh') return json({ accessToken: 'new-access', refreshToken: 'new-refresh', tenantId: 1 })
      return req.headers.get('Authorization') === 'Bearer new-access'
        ? json({ accessToken: 'aal2-access', refreshToken: 'new-refresh', tenantId: 1 })
        : new Response(null, { status: 401 })
    })
    const api = createApiClient({ baseUrl: BASE, fetch })
    await unwrap(api.POST('/api/v1/auth/reauth', { body: { password: 'ok' } }))
    expect(paths).toEqual(['/api/v1/auth/reauth', '/api/v1/auth/refresh', '/api/v1/auth/reauth'])
  })

  it('ante 403 aal2_required pide reautenticación y reintenta la acción', async () => {
    let aal2 = false
    setStepUpHandler(async () => {
      aal2 = true
      return true
    })
    const fetch = vi.fn(async () =>
      aal2 ? new Response(null, { status: 204 }) : json({ title: 'Requiere AAL2.', code: 'aal2_required' }, 403),
    )
    const api = createApiClient({ baseUrl: BASE, fetch })
    const { response } = await api.DELETE('/api/v1/auth/mfa/totp')
    expect(response.status).toBe(204)
    expect(fetch).toHaveBeenCalledTimes(2)
  })
})
