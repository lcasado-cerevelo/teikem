import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'

import { __resetSessionForTests, saveUserSession } from '../auth/session'
import { __resetDbForTests } from '../db/database'
import { createApiClient, setApiBaseUrl, setAuthLostHandler, unwrap } from './client'
import { ApiError } from './problem'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  setApiBaseUrl('http://api.test')
  setAuthLostHandler(null)
})

const SESSION = {
  accessToken: 'access-1',
  accessExpiresAtUtc: '2026-01-01T00:00:00Z',
  refreshToken: 'refresh-1',
  refreshExpiresAtUtc: '2026-02-01T00:00:00Z',
  tenantId: 1,
  userId: 7,
  fullName: 'Ana',
}

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('cliente del API', () => {
  it('reapunta al servidor configurado y manda el token de la sesión', async () => {
    const seen: Request[] = []
    const client = createApiClient({
      fetch: async (req) => {
        seen.push(req)
        return jsonResponse(200, { ok: true })
      },
    })
    await saveUserSession(SESSION)
    await unwrap(client.GET('/api/v1/warehouses', { params: { query: {} } }))
    expect(seen).toHaveLength(1)
    expect(seen[0].url).toBe('http://api.test/api/v1/warehouses')
    expect(seen[0].headers.get('Authorization')).toBe('Bearer access-1')
    expect(seen[0].headers.get('Accept-Language')).toBe('es')
  })

  it('sin red: unwrap lanza ApiError con code network', async () => {
    const client = createApiClient({
      fetch: async () => {
        throw new TypeError('Network request failed')
      },
    })
    await expect(unwrap(client.GET('/api/v1/warehouses', { params: { query: {} } }))).rejects.toMatchObject({
      code: 'network',
    })
  })

  it('401 refresca una sola vez y reintenta; peticiones concurrentes comparten el mismo refresh', async () => {
    await saveUserSession(SESSION)
    let refreshCalls = 0
    const client = createApiClient({
      fetch: async (req) => {
        const path = new URL(req.url).pathname
        if (path === '/api/v1/auth/refresh') {
          refreshCalls += 1
          return jsonResponse(200, {
            accessToken: 'access-2',
            accessExpiresAtUtc: SESSION.accessExpiresAtUtc,
            refreshToken: 'refresh-2',
            refreshExpiresAtUtc: SESSION.refreshExpiresAtUtc,
            tenantId: SESSION.tenantId,
          })
        }
        const auth = req.headers.get('Authorization')
        if (auth === 'Bearer access-1') return jsonResponse(401, { title: 'expirado' })
        return jsonResponse(200, { ok: true })
      },
    })
    const [a, b] = await Promise.all([
      unwrap(client.GET('/api/v1/warehouses', { params: { query: {} } })),
      unwrap(client.GET('/api/v1/warehouses', { params: { query: {} } })),
    ])
    expect(a).toEqual({ ok: true })
    expect(b).toEqual({ ok: true })
    expect(refreshCalls).toBe(1)
  })

  it('si el refresh falla, limpia la sesión y avisa (authLostHandler)', async () => {
    await saveUserSession(SESSION)
    let lost = false
    setAuthLostHandler(() => {
      lost = true
    })
    const client = createApiClient({
      fetch: async (req) => {
        const path = new URL(req.url).pathname
        if (path === '/api/v1/auth/refresh') return jsonResponse(401, { title: 'refresh inválido' })
        return jsonResponse(401, { title: 'expirado' })
      },
    })
    await expect(
      unwrap(client.GET('/api/v1/warehouses', { params: { query: {} } })),
    ).rejects.toBeInstanceOf(ApiError)
    expect(lost).toBe(true)
  })
})

describe('cliente del API — 403 sin cuerpo (2026-10-11)', () => {
  it('un 403 vacío de la política de permiso llega como «sin permiso»; uno con ProblemDetails conserva su mensaje', async () => {
    await saveUserSession(SESSION)
    const client = createApiClient({
      fetch: async (req) => {
        const path = new URL(req.url).pathname
        if (path === '/api/v1/warehouses') return new Response(null, { status: 403 })
        return jsonResponse(403, { title: "El módulo 'WMS' no está habilitado para esta compañía.", status: 403, code: 'module_disabled' })
      },
    })
    await expect(unwrap(client.GET('/api/v1/warehouses', { params: { query: {} } }))).rejects.toMatchObject({
      status: 403,
      code: 'forbidden',
      title: 'No tienes permiso para esta acción. Pide a tu administrador que te lo asigne.',
    })
    await expect(unwrap(client.GET('/api/v1/products', { params: { query: {} } }))).rejects.toMatchObject({
      status: 403,
      code: 'module_disabled',
      title: "El módulo 'WMS' no está habilitado para esta compañía.",
    })
  })
})
