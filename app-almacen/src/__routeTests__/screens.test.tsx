// Comprobación visual ligera de la navegación (docs/mobile/app-almacen-plan.md): que la pantalla real de Inicio monte
// con el aparato y la sesión hidratados, y muestre los 5 botones y el nombre de quien entró. Sin profundizar en cada
// pantalla o interacción: eso ya está cubierto por receiveLogic/localLookup/sync (unitarias, puras o con SQL real).
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

describe('navegación', () => {
  it('con aparato y sesión guardados, la raíz muestra Inicio con los 5 botones y el usuario', async () => {
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

    renderRouter('src/app', { initialUrl: '/' })

    await waitFor(() => expect(screen.getByText('Teikem Almacén')).toBeTruthy())
    expect(screen.getByText('Ana Ruiz')).toBeTruthy()
    expect(screen.getByText('Recibir')).toBeTruthy()
    expect(screen.getByText('Acomodar')).toBeTruthy()
    expect(screen.getByText('Despacho')).toBeTruthy()
    expect(screen.getByText('Conteo')).toBeTruthy()
    expect(screen.getByText('Consultar')).toBeTruthy()
  })
})
