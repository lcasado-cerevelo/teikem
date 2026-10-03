// Comprobación visual ligera de que la pantalla real de Sincronización monta (docs/mobile/app-almacen-plan.md). En
// archivo propio: renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo
// archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
})

afterEach(cleanup)

describe('navegación — Sincronización', () => {
  it('muestra el título y la cuenta de pendientes', async () => {
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

    renderRouter('src/app', { initialUrl: '/sync' })

    await waitFor(() => expect(screen.getByText('Sincronización')).toBeTruthy())
    expect(screen.getByText('Pendientes (0)')).toBeTruthy()
    // indicador del lector (docs/mobile/mejoras-ux-zebra.md §2.3): en Jest no hay DataWedge
    expect(screen.getByText('Lector: no es un Zebra (se usa el teclado)')).toBeTruthy()
    expect(screen.getByText('Última vez: nunca')).toBeTruthy()
  })
})
