// docs/mobile/mejoras-ux-zebra.md §1 y §4: la raíz deja un margen inferior para la barra de navegación del aparato, e
// Inicio pone las acciones en dos columnas de botones altos, dentro de una pantalla desplazable. En archivo propio:
// renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { StyleSheet } from 'react-native'
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

describe('navegación — Inicio y márgenes de la app', () => {
  it('margen inferior ≥ 32 + 8 en la raíz; acciones en dos columnas de 88 dp; pantalla desplazable', async () => {
    await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })

    await renderRouter('src/app', { initialUrl: '/home' })
    await waitFor(() => expect(screen.getByText('Teikem Almacén')).toBeTruthy())

    // Raíz: en Jest el sistema reporta un margen de 0, así que se usa el mínimo (32) + spacing.sm (8).
    const root = StyleSheet.flatten(screen.getByTestId('app-root').props.style)
    expect(root.paddingBottom).toBe(40)

    // Grilla de dos columnas: fila que se parte, cada celda a la mitad (la quinta, sola, a todo el ancho).
    const grid = screen.getByTestId('home-grid')
    expect(StyleSheet.flatten(grid.props.style)).toMatchObject({ flexDirection: 'row', flexWrap: 'wrap' })
    expect(grid.props.children).toHaveLength(5)
    for (const label of ['Recibir', 'Acomodar', 'Despacho', 'Conteo', 'Consultar']) {
      const button = screen.getByRole('button', { name: new RegExp(label) })
      expect(StyleSheet.flatten(button.props.style).minHeight).toBe(88)
    }

    // Desplazable (por si la pantalla del aparato es más corta) y con Sincronizar y Cambiar de usuario debajo.
    expect(screen.getByTestId('home-scroll').type).toBe('RCTScrollView')
    expect(screen.getByText('Sincronizar ahora')).toBeTruthy()
    expect(screen.getByText('Cambiar de usuario')).toBeTruthy()
  })
})
