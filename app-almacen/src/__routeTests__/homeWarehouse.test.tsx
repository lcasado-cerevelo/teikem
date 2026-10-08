// 2026-10-07 — varios almacenes por aparato y compañía: Inicio muestra el almacén activo, deja cambiarlo y no lo permite con un documento abierto.
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { startLocalReceipt } from '../features/receive/localLookup'
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession, getSessionState, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { getKv, KvKeys } from '../kernel/db/kv'

const WAREHOUSES = [
  { publicId: 'wh-1', code: 'DEP', name: 'Depot', isActive: true, receivingModeCode: 'PUTAWAY' },
  { publicId: 'wh-2', code: 'SUR', name: 'Almacén Sur', isActive: true, receivingModeCode: 'DIRECT' },
]

beforeEach(async () => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  setApiBaseUrl('http://api.test')
  jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const path = new URL((input as Request).url).pathname
    if (path === '/api/v1/warehouses') return new Response(JSON.stringify(WAREHOUSES), { status: 200, headers: { 'Content-Type': 'application/json' } })
    throw new Error('sin red')
  })
  await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null })
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

describe('Inicio — cambiar de almacén', () => {
  it('con varios almacenes muestra el actual y «Cambiar»; elegir otro lo deja activo', async () => {
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)
    await renderRouter('src/app', { initialUrl: '/home' })
    await waitFor(() => expect(screen.getByText('Almacén: Depot')).toBeTruthy())
    await waitFor(() => expect(screen.getByLabelText('Cambiar')).toBeTruthy())
    expect(getKv(KvKeys.warehouseOptions)).toContain('wh-2')

    await fireEvent.press(screen.getByLabelText('Cambiar'))
    await waitFor(() => expect(screen.getByTestId('warehouse-picker')).toBeTruthy())
    await fireEvent.press(await screen.findByLabelText('Almacén Sur'))

    await waitFor(() => expect(getSessionState().device?.selectedWarehouse?.publicId).toBe('wh-2'))
    await waitFor(() => expect(screen.getByText('Almacén: Almacén Sur')).toBeTruthy())
    expect(alertSpy).toHaveBeenCalledWith('Ahora trabajas en Almacén Sur.')
    alertSpy.mockRestore()
  })
})
