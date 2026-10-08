// 2026-10-07 — varios almacenes: con un documento abierto Inicio no deja cambiar de almacén. En archivo propio: renderRouter() no aísla su estado entre dos pruebas (ver homeLock.test.tsx).
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { startLocalReceipt } from '../features/receive/localLookup'
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession, getSessionState, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'

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

describe('Inicio — cambiar de almacén con un documento abierto', () => {
  it('con un recibo en curso no deja cambiar de almacén', async () => {
    startLocalReceipt('wh-1', null)
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)
    await renderRouter('src/app', { initialUrl: '/home' })
    await waitFor(() => expect(screen.getByLabelText('Cambiar')).toBeTruthy())

    await fireEvent.press(screen.getByLabelText('Cambiar'))

    expect(alertSpy).toHaveBeenCalledWith('Termina o cancela el documento en curso antes de cambiar de almacén.')
    expect(screen.queryByTestId('warehouse-picker')).toBeNull()
    expect(getSessionState().device?.selectedWarehouse ?? null).toBeNull()
    alertSpy.mockRestore()
  })
})
