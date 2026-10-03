// Lote A8 — Consultar: quien NO tiene warehouse.count (conteo a ciegas, p. ej. solo warehouse.count.capture) ve la lista de lo
// que hay en la posición SIN las cantidades del sistema, igual que en el conteo. La posición no está sincronizada: se busca en el
// servidor. Con más de 6 productos aparece el buscador. En archivo propio (renderRouter).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { json, mockFetch, setupDevice } from './countKit'

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

const ROWS = Array.from({ length: 7 }, (_, i) => ({
  id: i + 1,
  binCode: 'R-05-04',
  productPublicId: `p${i + 1}`,
  sku: `SKU-${i + 1}`,
  productName: `Producto ${i + 1}`,
  lotNumber: i === 6 ? 'L-77' : null,
  qtyOnHand: 40 + i,
  qtyAvailable: 40 + i,
}))

describe('Consultar — lo que hay en una posición (a ciegas)', () => {
  it('lista los productos sin ninguna cantidad y con buscador porque son más de 6', async () => {
    await setupDevice()
    const calls = mockFetch([
      (c) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions: ['inventory.view', 'warehouse.count.capture'] }) : null),
      (c) =>
        c.path === '/api/v1/warehouses/wh-1/bins' ? json(200, { total: 1, skip: 0, take: 200, items: [{ id: 41, code: 'R-05-04' }] }) : null,
      (c) => (c.path === '/api/v1/inventory/balances' ? json(200, { total: 7, skip: 0, take: 200, items: ROWS }) : null),
    ])

    await renderRouter('src/app', { initialUrl: '/lookup' })
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/me')).toBe(true))
    await fireEvent.changeText(screen.getByLabelText('Escanea un producto o una posición'), 'R-05-04')
    await fireEvent.press(screen.getByLabelText('Aceptar'))

    await waitFor(() => expect(screen.getByText('Qué hay en R-05-04')).toBeTruthy())
    expect(screen.getByText('7 productos en esta posición')).toBeTruthy()
    expect(screen.getByText('SKU-1')).toBeTruthy()
    expect(screen.getByText('Lote L-77')).toBeTruthy()
    expect(screen.queryByText(/En mano/)).toBeNull()
    expect(screen.queryByText(/Disponible/)).toBeNull()
    expect(calls.find((c) => c.path === '/api/v1/inventory/balances')?.search).toContain('binIds=41')

    await fireEvent.changeText(screen.getByLabelText('Buscar producto o lote'), 'l-77')
    expect(screen.getByText('SKU-7')).toBeTruthy()
    expect(screen.queryByText('SKU-1')).toBeNull()
  })
})
