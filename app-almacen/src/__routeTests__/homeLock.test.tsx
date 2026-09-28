// Comprobación visual ligera de la navegación (docs/mobile/app-almacen-plan.md): que un recibo en curso bloquee las
// demás acciones en Inicio (decisión de Luis, 2026-09-28: un recibo a la vez). En archivo propio porque expo-router's
// testing-library no aísla del todo su estado global de navegación entre dos renderRouter() del mismo archivo.
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { startLocalReceipt } from '../features/receive/localLookup'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
})

afterEach(cleanup)

async function loginToHome(): Promise<void> {
  await saveDeviceIdentity({
    devicePublicId: 'dev-1',
    deviceSecret: 'secret-1',
    tenantName: 'Teikem Demo',
    defaultWarehousePublicId: 'wh-1',
    theme: null,
  })
  await saveUserSession({
    accessToken: 'a',
    accessExpiresAtUtc: '',
    refreshToken: 'r',
    refreshExpiresAtUtc: '',
    tenantId: 1,
    userId: 7,
    fullName: 'Ana Ruiz',
  })
}

describe('navegación — Inicio bloqueado por un recibo en curso', () => {
  it('con un recibo en curso, tocar otra acción avisa en vez de dejar hacer otra cosa', async () => {
    await loginToHome()
    startLocalReceipt('wh-1', null)
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)

    renderRouter('src/app', { initialUrl: '/' })
    await waitFor(() => expect(screen.getByText('Teikem Almacén')).toBeTruthy())

    await fireEvent.press(screen.getByText('Acomodar'))

    expect(alertSpy).toHaveBeenCalledWith('Termina o cancela el recibo en curso antes de usar esto.')
    alertSpy.mockRestore()
  })
})
