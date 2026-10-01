// 2026-09-30 (varias compañías): con el teléfono registrado en dos compañías, "¿Quién eres?" primero pide la compañía y
// luego muestra los usuarios de esa compañía. En archivo propio por el estado global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { setApiBaseUrl } from '../kernel/api/client'
import { addDeviceIdentity, dbNameFor, getSessionState, selectDevice, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

describe('entrada con varias compañías', () => {
  it('pide la compañía y luego pide los usuarios con el registro de esa compañía', async () => {
    setApiBaseUrl('http://api.test')
    for (const [id, name] of [['dev-1', 'Advance Depot'], ['dev-2', 'Advance Solutions']] as const) {
      await addDeviceIdentity({ devicePublicId: id, deviceSecret: `s-${id}`, tenantName: name, defaultWarehousePublicId: null, theme: null, dbName: dbNameFor(id) })
    }
    await selectDevice(null)
    const asked: string[] = []
    jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      asked.push(new URL((input as Request).url).pathname)
      return new Response(JSON.stringify([{ userId: 7, fullName: 'Ana Ruiz', initials: 'AR' }]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    })

    renderRouter('src/app', { initialUrl: '/' })
    await waitFor(() => expect(screen.getByText('¿En qué compañía vas a trabajar?')).toBeTruthy())
    expect(screen.getByText('Advance Depot')).toBeTruthy()

    await fireEvent.press(screen.getByText('Advance Solutions'))
    await waitFor(() => expect(screen.getByText('Cambiar de compañía')).toBeTruthy())
    expect(getSessionState().device?.devicePublicId).toBe('dev-2')
    await waitFor(() => expect(asked).toEqual(['/api/v1/auth/device/users']))
  })
})
