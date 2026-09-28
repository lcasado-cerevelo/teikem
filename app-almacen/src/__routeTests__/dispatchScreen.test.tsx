// Comprobación visual ligera de que la pantalla real de Despacho monta sin nada en curso (docs/mobile/app-almacen-
// plan.md). En archivo propio: renderRouter() no aísla del todo su estado global de navegación entre dos llamadas
// del mismo archivo (ver homeLock.test.tsx).
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

describe('navegación — Despacho', () => {
  it('sin despacho en curso, muestra el campo para escanear el producto', async () => {
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

    renderRouter('src/app', { initialUrl: '/dispatch' })

    await waitFor(() => expect(screen.getByText('Escanea el producto')).toBeTruthy())
  })
})
