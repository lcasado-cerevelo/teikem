// 2026-10-07 — pantalla de bloqueo: con el PIN correcto vuelve a Inicio y deja la sesión desbloqueada.
import { render, fireEvent, screen, waitFor } from '@testing-library/react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'

import LockScreen from '../app/lock'
import { setApiBaseUrl } from '../kernel/api/client'
import { __resetDbForTests } from '../kernel/db/database'
import { addDeviceIdentity, getSessionState, lockSession, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'

const mockReplace = jest.fn()
jest.mock('expo-router', () => ({ useRouter: () => ({ replace: mockReplace }) }))

beforeEach(async () => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  mockReplace.mockClear()
  setApiBaseUrl('http://api.test')
  await addDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'Demo', defaultWarehousePublicId: null, theme: null })
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
  lockSession()
})

afterEach(() => jest.restoreAllMocks())

describe('pantalla de bloqueo', () => {
  it('con el PIN correcto desbloquea y vuelve a Inicio', async () => {
    jest.spyOn(globalThis, 'fetch').mockImplementation(async () =>
      new Response(JSON.stringify({ accessToken: 'a2', refreshToken: 'r2', tenantId: 1 }), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    await render(<LockScreen />)
    expect(getSessionState().locked).toBe(true)
    for (const k of ['1', '2', '3', '4']) await fireEvent.press(screen.getByText(k))
    await fireEvent.press(screen.getByText('Desbloquear'))
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/home'))
    expect(getSessionState().locked).toBe(false)
  })

  it('con un PIN incorrecto muestra el error y sigue bloqueada', async () => {
    jest.spyOn(globalThis, 'fetch').mockImplementation(async () =>
      new Response(JSON.stringify({ title: 'PIN incorrecto.', status: 401 }), { status: 401, headers: { 'Content-Type': 'application/problem+json' } }),
    )
    await render(<LockScreen />)
    for (const k of ['1', '2', '3', '4']) await fireEvent.press(screen.getByText(k))
    await fireEvent.press(screen.getByText('Desbloquear'))
    await waitFor(() => expect(screen.getByText('PIN incorrecto.')).toBeTruthy())
    expect(getSessionState().locked).toBe(true)
    expect(mockReplace).not.toHaveBeenCalled()
  })
})
