// 2026-10-07: una compañía que el servidor ya no reconoce (401) se quita sola, sin preguntar. En archivo propio por el estado global de renderRouter().
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

describe('compañía que el servidor ya no conoce', () => {
  it('si el servidor ya no conoce el aparato de esa compañía (401), la quita sin preguntar y sigue con la que queda', async () => {
    setApiBaseUrl('http://api.test')
    for (const [id, name] of [['dev-1', 'Advance Depot'], ['dev-2', 'Advance Solutions']] as const) {
      await addDeviceIdentity({ devicePublicId: id, deviceSecret: `s-${id}`, tenantName: name, defaultWarehousePublicId: null, theme: null, dbName: dbNameFor(id) })
    }
    await selectDevice(null)
    let calls = 0
    jest.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      calls += 1
      // la primera compañía que se abre (Solutions) ya no existe en el servidor; la otra responde con sus usuarios
      return calls === 1
        ? new Response(JSON.stringify({ title: 'El aparato no está registrado o fue desactivado.', status: 401 }), { status: 401, headers: { 'Content-Type': 'application/problem+json' } })
        : new Response(JSON.stringify([{ userId: 7, fullName: 'Ana Ruiz', initials: 'AR' }]), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })

    renderRouter('src/app', { initialUrl: '/' })
    await waitFor(() => expect(screen.getByText('Advance Solutions')).toBeTruthy())
    await fireEvent.press(screen.getByText('Advance Solutions'))

    await waitFor(() => expect(getSessionState().devices.map((d) => d.devicePublicId)).toEqual(['dev-1']))
    // queda una sola compañía: es la activa y no se pidió ninguna confirmación
    expect(getSessionState().device?.devicePublicId).toBe('dev-1')
  })
})
