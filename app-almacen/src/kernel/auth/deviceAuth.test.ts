import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'

import { setApiBaseUrl } from '../api/client'
import { __resetDbForTests } from '../db/database'
import { enrollDevice, fetchDeviceUsers, loginWithPin, sendHeartbeat } from './deviceAuth'
import { __resetSessionForTests, getSessionState } from './session'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

let handlers: Record<string, (req: Request) => Response>

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  setApiBaseUrl('http://api.test')
  handlers = {}
  jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const req = input as Request
    const path = new URL(req.url).pathname
    const handler = handlers[path]
    if (!handler) throw new Error(`sin handler de prueba para ${path}`)
    return handler(req)
  })
})

afterEach(() => {
  jest.restoreAllMocks()
})

describe('auth del aparato', () => {
  it('enrollDevice guarda la identidad del aparato', async () => {
    handlers['/api/v1/devices/enroll'] = () =>
      jsonResponse(200, {
        devicePublicId: 'dev-1',
        deviceSecret: 'secret-1',
        tenantName: 'Teikem Demo',
        defaultWarehousePublicId: 'wh-1',
        theme: 'light',
      })
    await enrollDevice('ABC12345')
    expect(getSessionState().device).toEqual({
      devicePublicId: 'dev-1',
      deviceSecret: 'secret-1',
      tenantName: 'Teikem Demo',
      defaultWarehousePublicId: 'wh-1',
      theme: 'light',
    })
  })

  it('fetchDeviceUsers mapea la lista de usuarios', async () => {
    handlers['/api/v1/auth/device/users'] = () =>
      jsonResponse(200, [{ userId: 1, fullName: 'Ana Ruiz', initials: 'AR' }])
    const users = await fetchDeviceUsers('dev-1', 'secret-1')
    expect(users).toEqual([{ userId: 1, fullName: 'Ana Ruiz', initials: 'AR' }])
  })

  it('loginWithPin guarda la sesión con los tokens y el nombre', async () => {
    handlers['/api/v1/auth/device/login'] = () =>
      jsonResponse(200, {
        accessToken: 'access-1',
        accessExpiresAtUtc: '2026-01-01T00:10:00Z',
        refreshToken: 'refresh-1',
        refreshExpiresAtUtc: '2026-02-01T00:00:00Z',
        tenantId: 3,
      })
    await loginWithPin('dev-1', 'secret-1', 7, '4821', 'Ana Ruiz')
    expect(getSessionState().session).toMatchObject({ accessToken: 'access-1', userId: 7, fullName: 'Ana Ruiz', tenantId: 3 })
  })

  it('sendHeartbeat sin aparato registrado no llama al API', async () => {
    const result = await sendHeartbeat()
    expect(result).toEqual({ isActive: false })
  })

  it('sendHeartbeat con isActive=false borra la identidad del aparato', async () => {
    handlers['/api/v1/devices/enroll'] = () =>
      jsonResponse(200, { devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'T', defaultWarehousePublicId: null, theme: null })
    await enrollDevice('ABC12345')
    handlers['/api/v1/devices/heartbeat'] = () => jsonResponse(200, { isActive: false, serverTimeUtc: '2026-01-01T00:00:00Z' })
    const result = await sendHeartbeat()
    expect(result).toEqual({ isActive: false })
    expect(getSessionState().device).toBeNull()
  })

  it('sendHeartbeat sin red no desactiva el aparato (sigue trabajando sin señal)', async () => {
    handlers['/api/v1/devices/enroll'] = () =>
      jsonResponse(200, { devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'T', defaultWarehousePublicId: null, theme: null })
    await enrollDevice('ABC12345')
    handlers['/api/v1/devices/heartbeat'] = () => {
      throw new TypeError('Network request failed')
    }
    const result = await sendHeartbeat()
    expect(result).toEqual({ isActive: true })
    expect(getSessionState().device).not.toBeNull()
  })
})
