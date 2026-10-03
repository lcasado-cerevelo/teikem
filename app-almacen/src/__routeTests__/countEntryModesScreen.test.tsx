// Lote A4 — Conteo ofrece dos caminos: "Por posición" (como antes) y "Por producto". Un producto con serie no se cuenta por
// producto en la app: aviso claro y NO se crea ningún conteo. La forma elegida se recuerda. En archivo propio: renderRouter()
// no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { getKv, KvKeys } from '../kernel/db/kv'
import { insertProduct, mockFetch, setupDevice } from './countKit'

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

describe('Conteo — dos caminos', () => {
  it('por posición de entrada; "Por producto" cambia el campo; un producto con serie avisa y no crea el conteo', async () => {
    await setupDevice()
    insertProduct(1, 'p-ser', 'SER-1', 'Escáner', '7509', 'SERIAL')
    const calls = mockFetch([])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Escanea la posición a contar')).toBeTruthy())
    expect(screen.getByRole('radio', { name: 'Por posición' }).props.accessibilityState).toMatchObject({ selected: true })
    expect(screen.getByRole('radio', { name: 'Por producto' }).props.accessibilityState).toMatchObject({ selected: false })

    await fireEvent.press(screen.getByRole('radio', { name: 'Por producto' }))
    expect(screen.getByText('Escanea el producto a contar')).toBeTruthy()
    expect(screen.queryByText('Escanea la posición a contar')).toBeNull()
    expect(getKv(KvKeys.countEntryMode)).toBe('PRODUCT')

    await fireEvent.changeText(screen.getByLabelText('Escanea el producto a contar'), '7509')
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(screen.getByText('Este producto se cuenta por número de serie; cuéntalo desde la web por ahora.')).toBeTruthy())
    expect(calls.filter((c) => c.path === '/api/v1/cycle-counts')).toEqual([])
    expect(getOpenCount()).toBeNull()

    // un código que no es de ningún producto
    await fireEvent.changeText(screen.getByLabelText('Escanea el producto a contar'), 'NADA')
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(screen.getByText('No hay un producto con ese código.')).toBeTruthy())

    // volver a "Por posición"
    await fireEvent.press(screen.getByRole('radio', { name: 'Por posición' }))
    expect(screen.getByText('Escanea la posición a contar')).toBeTruthy()
    expect(getKv(KvKeys.countEntryMode)).toBe('BIN')
  })
})
