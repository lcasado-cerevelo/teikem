// Lote A8 — permisos de quien entró (GET /api/v1/me) para decidir si se ven las cantidades del sistema (regla del conteo:
// solo con warehouse.count). Se guardan por usuario en la base de la compañía; sin saberlos, no se muestran.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'

import { api } from '../api/client'
import { __resetDbForTests } from '../db/database'
import { canSeeSystemQty, getCachedPermissions, Perm, refreshMyPermissions } from './permissions'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from './session'

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client')
  return { ...actual, api: { GET: jest.fn() } }
})

const getMock = api.GET as jest.Mock

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

async function signIn(userId: number) {
  await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'Demo', defaultWarehousePublicId: 'wh-1', theme: null })
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId, fullName: 'Ana' })
}

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  getMock.mockReset()
})

describe('canSeeSystemQty', () => {
  it('solo con warehouse.count (el código exacto del API)', () => {
    expect(Perm.warehouseCount).toBe('warehouse.count')
    expect(canSeeSystemQty(['inventory.view', 'warehouse.count'])).toBe(true)
    expect(canSeeSystemQty(['inventory.view', 'warehouse.count.capture'])).toBe(false)
    expect(canSeeSystemQty([])).toBe(false)
  })

  it('sin saber los permisos, no', () => {
    expect(canSeeSystemQty(null)).toBe(false)
  })
})

describe('refreshMyPermissions', () => {
  it('sin sesión no pregunta nada', async () => {
    await expect(refreshMyPermissions()).resolves.toBeNull()
    expect(getMock).not.toHaveBeenCalled()
  })

  it('trae los permisos de /me y los guarda para ese usuario', async () => {
    await signIn(7)
    getMock.mockResolvedValueOnce(ok({ userId: 7, permissions: ['inventory.view', 'warehouse.count'] }))
    await expect(refreshMyPermissions()).resolves.toEqual(['inventory.view', 'warehouse.count'])
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/me')
    expect(getCachedPermissions(7)).toEqual(['inventory.view', 'warehouse.count'])
    expect(getCachedPermissions(8)).toBeNull()
  })

  it('cada usuario del aparato tiene los suyos', async () => {
    await signIn(7)
    getMock.mockResolvedValueOnce(ok({ permissions: ['warehouse.count'] }))
    await refreshMyPermissions()
    await signIn(8)
    getMock.mockResolvedValueOnce(ok({ permissions: ['warehouse.count.capture'] }))
    await refreshMyPermissions()
    expect(getCachedPermissions(7)).toEqual(['warehouse.count'])
    expect(getCachedPermissions(8)).toEqual(['warehouse.count.capture'])
  })

  it('si falla (sin señal) se conservan los guardados', async () => {
    await signIn(7)
    getMock.mockResolvedValueOnce(ok({ permissions: ['warehouse.count'] }))
    await refreshMyPermissions()
    getMock.mockResolvedValueOnce({ response: Response.error() })
    await expect(refreshMyPermissions()).resolves.toBeNull()
    expect(getCachedPermissions(7)).toEqual(['warehouse.count'])
  })

  it('un permiso quitado en la web se refleja en la siguiente consulta', async () => {
    await signIn(7)
    getMock.mockResolvedValueOnce(ok({ permissions: ['warehouse.count'] }))
    await refreshMyPermissions()
    getMock.mockResolvedValueOnce(ok({ permissions: [] }))
    await refreshMyPermissions()
    expect(canSeeSystemQty(getCachedPermissions(7))).toBe(false)
  })
})
