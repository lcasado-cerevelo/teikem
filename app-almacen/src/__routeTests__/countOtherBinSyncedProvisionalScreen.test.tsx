// Lote A4 (adenda 5d) — «Otra posición» con una posición provisional YA sincronizada (la creó otro aparato o una sesión anterior):
// se usa tal cual, sin llamar al servidor, y la fila lleva la marca "Pendiente de revisión" que trae la sincronización (columna
// bin.is_provisional, esquema v5). En archivo propio: renderRouter() no aísla del todo su estado global entre dos llamadas.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { startLocalProductCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { mockFetch, setupDevice } from './countKit'

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

describe('Conteo por producto — posición provisional ya sincronizada', () => {
  it('entra a la lista con la marca "Pendiente de revisión" sin crear nada en el servidor', async () => {
    await setupDevice()
    startLocalProductCount('wh-1', { publicId: 'p1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' }, { countId: 300, isBlind: true }, [])
    getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, is_active, is_provisional) VALUES (9, 'Z-09', 'wh-1', 4, 1, 1)")
    const calls = mockFetch([])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('El sistema no tiene existencia de este producto. Si lo encontraste en alguna posición, usa «Otra posición».')).toBeTruthy())
    await fireEvent.press(screen.getByRole('button', { name: 'Otra posición' }))
    await waitFor(() => expect(screen.getByLabelText('Código de la posición')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Código de la posición'), 'z-09')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar posición' }))

    await waitFor(() => expect(screen.getByText('La posición Z-09 ya existía; se agregó a la lista.')).toBeTruthy())
    expect(screen.getByText('Pendiente de revisión')).toBeTruthy()
    expect(calls.filter((c) => c.method === 'POST')).toEqual([])
  })
})
