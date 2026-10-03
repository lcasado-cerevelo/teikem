// Lote A4 — un producto sin existencia en ningún lado: el servidor no abre el conteo (400 con el mensaje exacto); la app lo
// muestra claro, no deja nada abierto y ofrece contar por posición (donde sí se puede agregar lo encontrado). En archivo propio:
// renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { setKv, KvKeys } from '../kernel/db/kv'
import { insertProduct, json, mockFetch, setupDevice } from './countKit'

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

const NOTHING = 'Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano.'

describe('Conteo por producto — sin existencia', () => {
  it('muestra el 400 del servidor, no abre nada y ofrece contar por posición', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    setKv(KvKeys.countEntryMode, 'PRODUCT')
    mockFetch([
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts'
          ? json(400, { title: NOTHING, status: 400, code: 'validation', errors: { filters: [NOTHING] } })
          : null,
    ])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Escanea el producto a contar')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Escanea el producto a contar'), 'SKU-1')
    await fireEvent.press(screen.getByLabelText('Aceptar'))

    await waitFor(() => expect(screen.getByText(NOTHING)).toBeTruthy())
    expect(getOpenCount()).toBeNull()
    expect(
      screen.getByText('Si lo encontraste en una posición que tiene otros productos, cuéntala «Por posición» y agrégalo ahí. Si no, avisa al supervisor.'),
    ).toBeTruthy()
    await fireEvent.press(screen.getByRole('button', { name: 'Contar por posición' }))
    expect(screen.getByText('Escanea la posición a contar')).toBeTruthy()
  })
})
