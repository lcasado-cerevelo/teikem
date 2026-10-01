import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'

import { setApiBaseUrl } from '../api/client'
import { __resetDbForTests } from '../db/database'
import { enrollDevice, fetchDeviceUsers, loginWithPin, sendHeartbeat } from './deviceAuth'
import * as SecureStore from 'expo-secure-store'
import { __resetSessionForTests, clearUserSession, getSessionState, hydrateSession, selectDevice } from './session'

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
      defaultWarehouseReceivingMode: null,
      dbName: 'teikem_almacen_dev1.db',
    })
  })

  it('varias compañías: el segundo registro se agrega, manda los registros previos y cada uno tiene su base', async () => {
    const bodies: unknown[] = []
    let n = 0
    handlers['/api/v1/devices/enroll'] = (req) => {
      void req.clone().json().then((b) => bodies.push(b))
      n += 1
      return jsonResponse(200, { devicePublicId: `dev-${n}`, deviceSecret: `s-${n}`, tenantName: n === 1 ? 'Advance Depot' : 'Advance Solutions', defaultWarehousePublicId: null, theme: null })
    }
    await enrollDevice('AAAA1111')
    await enrollDevice('BBBB2222')
    await new Promise((r) => setTimeout(r, 0))
    expect(bodies[0]).toMatchObject({ registeredDevicePublicIds: null })
    expect(bodies[1]).toMatchObject({ registeredDevicePublicIds: ['dev-1'] })
    const st = getSessionState()
    expect(st.devices.map((d) => d.tenantName)).toEqual(['Advance Depot', 'Advance Solutions'])
    expect(st.device?.devicePublicId).toBe('dev-2')
    expect(new Set(st.devices.map((d) => d.dbName)).size).toBe(2)

    // Al salir el usuario, con varias compañías se vuelve a elegir; elegir una cierra la sesión anterior.
    await clearUserSession()
    expect(getSessionState().device).toBeNull()
    await selectDevice('dev-1')
    expect(getSessionState().device?.tenantName).toBe('Advance Depot')
    expect(getSessionState().session).toBeNull()
  })

  it('el servidor rechaza una compañía ya registrada en el teléfono: no se agrega nada', async () => {
    handlers['/api/v1/devices/enroll'] = () =>
      jsonResponse(200, { devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'Advance Depot', defaultWarehousePublicId: null, theme: null })
    await enrollDevice('AAAA1111')
    handlers['/api/v1/devices/enroll'] = () =>
      jsonResponse(409, { title: 'Este teléfono ya está registrado en Advance Depot como ZB-01. Pide al administrador un código de otra compañía.', status: 409 })
    await expect(enrollDevice('CCCC3333')).rejects.toMatchObject({ title: expect.stringContaining('ya está registrado en Advance Depot') })
    expect(getSessionState().devices).toHaveLength(1)
  })

  it('instalación de antes (un solo registro): al arrancar pasa a la lista con su base de siempre', async () => {
    await SecureStore.setItemAsync('teikem.device.identity', JSON.stringify({ devicePublicId: 'old', deviceSecret: 's', tenantName: 'Advance Depot', defaultWarehousePublicId: null, theme: null }))
    await hydrateSession()
    const st = getSessionState()
    expect(st.devices).toHaveLength(1)
    expect(st.device?.devicePublicId).toBe('old')
    expect(st.device?.dbName).toBeUndefined()
    expect(await SecureStore.getItemAsync('teikem.device.identity')).toBeNull()
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

  it('enrollDevice guarda el modo de recepción del almacén por defecto (Lote 16)', async () => {
    handlers['/api/v1/devices/enroll'] = () =>
      jsonResponse(200, { devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'T', defaultWarehousePublicId: 'wh-1', theme: null, defaultWarehouseReceivingMode: 'DIRECT' })
    await enrollDevice('ABC12345')
    expect(getSessionState().device?.defaultWarehouseReceivingMode).toBe('DIRECT')
  })

  it('sendHeartbeat actualiza almacén y modo de recepción; sin cambios no reescribe la identidad (Lote 16)', async () => {
    handlers['/api/v1/devices/enroll'] = () =>
      jsonResponse(200, { devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'T', defaultWarehousePublicId: 'wh-1', theme: null })
    await enrollDevice('ABC12345')
    expect(getSessionState().device?.defaultWarehouseReceivingMode).toBeNull()

    handlers['/api/v1/devices/heartbeat'] = () =>
      jsonResponse(200, { isActive: true, defaultWarehousePublicId: 'wh-2', defaultWarehouseReceivingMode: 'DIRECT', serverTimeUtc: '2026-01-01T00:00:00Z' })
    await sendHeartbeat()
    expect(getSessionState().device).toMatchObject({ defaultWarehousePublicId: 'wh-2', defaultWarehouseReceivingMode: 'DIRECT' })

    const before = getSessionState().device
    await sendHeartbeat()
    expect(getSessionState().device).toBe(before)

    handlers['/api/v1/devices/heartbeat'] = () =>
      jsonResponse(200, { isActive: true, defaultWarehousePublicId: 'wh-2', defaultWarehouseReceivingMode: 'PUTAWAY', serverTimeUtc: '2026-01-01T00:01:00Z' })
    await sendHeartbeat()
    expect(getSessionState().device?.defaultWarehouseReceivingMode).toBe('PUTAWAY')
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
