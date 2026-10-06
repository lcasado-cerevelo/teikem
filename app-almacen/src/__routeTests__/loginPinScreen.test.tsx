// Lote A9 (pruebas del dueño en el Zebra, 2026-10-06): en la pantalla de poner el PIN, Volver y Entrar quedaban cortados a la mitad
// porque el contenido (nombre, puntos y teclado de 4 × 72 dp) medía más que la pantalla chica y no había desplazamiento. Ahora la
// pantalla es un ScrollView (centrado si cabe, desplazable si no) y en pantallas bajas el teclado del PIN se compacta. En archivo
// propio por el estado global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor, within } from 'expo-router/testing-library'
import { Dimensions, StyleSheet } from 'react-native'

import { setApiBaseUrl } from '../kernel/api/client'
import { addDeviceIdentity, dbNameFor, selectDevice, __resetSessionForTests } from '../kernel/auth/session'
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

async function openPin() {
  setApiBaseUrl('http://api.test')
  await addDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 's-1', tenantName: 'Advance Depot', defaultWarehousePublicId: null, theme: null, dbName: dbNameFor('dev-1') })
  await selectDevice('dev-1')
  jest.spyOn(globalThis, 'fetch').mockImplementation(async () =>
    new Response(JSON.stringify([{ userId: 7, fullName: 'Ana Ruiz', initials: 'AR' }]), { status: 200, headers: { 'Content-Type': 'application/json' } }),
  )
  await renderRouter('src/app', { initialUrl: '/' })
  await waitFor(() => expect(screen.getByText('Ana Ruiz')).toBeTruthy())
  await fireEvent.press(screen.getByRole('button', { name: /Ana Ruiz/ }))
  await waitFor(() => expect(screen.getByTestId('pin-scroll')).toBeTruthy())
}

describe('poner el PIN en una pantalla chica', () => {
  it('es desplazable (ScrollView que crece, no un View fijo) y Volver y Entrar están dentro, al final', async () => {
    // pantalla de un Zebra de 4": 320 × 533 dp
    jest.spyOn(Dimensions, 'get').mockReturnValue({ width: 320, height: 533, scale: 1.5, fontScale: 1 })
    await openPin()
    const scroll = screen.getByTestId('pin-scroll')
    expect(scroll.type).toBe('RCTScrollView')
    expect(scroll.props.keyboardShouldPersistTaps).toBe('handled')
    // flexGrow (no flex: 1 ni justifyContent sobre un View fijo): centra si cabe y se desplaza si no
    const content = StyleSheet.flatten(scroll.props.contentContainerStyle)
    expect(content.flexGrow).toBe(1)
    expect(content.flex).toBeUndefined()
    expect(content.paddingBottom).toBeGreaterThan(0)
    expect(within(scroll).getByRole('button', { name: 'Volver' })).toBeTruthy()
    expect(within(scroll).getByRole('button', { name: 'Entrar' })).toBeTruthy()
    // pantalla baja: teclas de 60 dp (siguen siendo más grandes que el mínimo de 56)
    expect(StyleSheet.flatten(within(scroll).getByRole('button', { name: '5' }).props.style)).toMatchObject({ width: 60, height: 60 })
  })

  it('en una pantalla alta el teclado del PIN conserva las teclas de 72 dp', async () => {
    jest.spyOn(Dimensions, 'get').mockReturnValue({ width: 412, height: 915, scale: 2.625, fontScale: 1 })
    await openPin()
    expect(StyleSheet.flatten(screen.getByRole('button', { name: '5' }).props.style)).toMatchObject({ width: 72, height: 72 })
  })
})
