// Lote A5 — decisión del dueño 4: una fila de «Otra posición» también cuenta. Conteo vacío (producto sin existencia) → se agrega
// la posición → dejarla en blanco y Confirmar avisa "Escribe al menos una cantidad…" sin mandar nada; con la cantidad escrita en
// esa fila, se termina. En archivo propio: renderRouter() no aísla del todo su estado global de navegación.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { setKv, KvKeys } from '../kernel/db/kv'
import { listOutbox } from '../kernel/sync/outbox'
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

const ALL_BLANK = 'Escribe al menos una cantidad. Si no hay nada de este producto, escribe 0 en una posición.'

describe('Conteo por producto — «Otra posición» en blanco', () => {
  it('la fila nueva en blanco no basta; con su cantidad escrita se termina', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    setKv(KvKeys.countEntryMode, 'PRODUCT')
    mockFetch([
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts'
          ? json(200, { count: { id: 400, originCode: 'PRODUCT', lineCount: 0 }, isBlind: true, lines: [] })
          : null,
      (c) => (c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/zones' ? json(200, [{ id: 4, code: 'PCK', name: 'Picking', isActive: true }]) : null),
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts/400/bins'
          ? json(200, { id: 99, code: 'Z-09', zoneId: 4, zoneCode: 'PCK', isProvisional: true, provisionalCycleCountId: 400 })
          : null,
    ])
    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Escanea el producto a contar')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Escanea el producto a contar'), 'SKU-1')
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(screen.getByTestId('count-empty-block')).toBeTruthy())

    await fireEvent.press(screen.getByRole('button', { name: 'Otra posición' }))
    await waitFor(() => expect(screen.getByRole('radio', { name: 'PCK · Picking' })).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Código de la posición'), 'z-09')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar posición' }))
    await waitFor(() => expect(screen.getByText('Posición Z-09 agregada (pendiente de revisión).')).toBeTruthy())

    // la fila nueva está en blanco: no es un conteo vacío, pero tampoco tiene ningún número
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    expect(screen.getByText(ALL_BLANK)).toBeTruthy()
    expect(screen.queryByText(/No se puede terminar un conteo vacío/)).toBeNull()
    expect(listOutbox()).toEqual([])
    expect(getOpenCount()).not.toBeNull()

    await fireEvent.changeText(screen.getByLabelText('Cantidad en Z-09'), '2')
    expect(screen.queryByText(ALL_BLANK)).toBeNull()
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(JSON.parse(listOutbox()[0].body)).toEqual({ lines: [{ binId: 99, productPublicId: 'p1', countedQty: 2 }] })
  })
})
