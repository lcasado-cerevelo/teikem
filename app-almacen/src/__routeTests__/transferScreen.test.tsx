// Transferir en la app (2026-10-10): posición de origen → producto → cantidad → posición destino → confirmar. En archivo propio por el estado global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
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

const BINS = (c: { method: string; path: string; search: string }) =>
  c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/bins'
    ? json(200, {
        total: 2,
        skip: 0,
        take: 200,
        items: [
          { id: 41, code: 'A-01' },
          { id: 42, code: 'B-02' },
        ],
      })
    : null
const BALANCES = (c: { method: string; path: string }) =>
  c.method === 'GET' && c.path === '/api/v1/inventory/balances'
    ? json(200, {
        total: 1,
        skip: 0,
        take: 200,
        items: [{ id: 1, binId: 41, binCode: 'A-01', zoneTypeCode: 'PICKING', productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', lotId: null, qtyOnHand: 10, qtyReserved: 4, qtyAvailable: 6 }],
      })
    : null
const TRANSFER = (c: { method: string; path: string }) => (c.method === 'POST' && c.path === '/api/v1/inventory/transfers/in-warehouse' ? json(200, { transactions: [], balances: [] }) : null)

async function scan(label: string, text: string) {
  await waitFor(() => expect(screen.getByLabelText(label)).toBeTruthy())
  await fireEvent(screen.getByLabelText(label), 'submitEditing', { nativeEvent: { text } })
}

describe('Transferir', () => {
  it('mueve lo disponible de una posición a otra y manda el cuerpo correcto', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    const calls = mockFetch([BINS, BALANCES, TRANSFER])
    await renderRouter('src/app', { initialUrl: '/transfer' })
    await scan('Escanea la posición de origen', 'a-01')
    await waitFor(() => expect(screen.getByText('Origen: A-01')).toBeTruthy())
    await scan('Escanea el producto a mover', '7501')
    await waitFor(() => expect(screen.getByText('Se puede mover: 6')).toBeTruthy())
    // más de lo movible no avanza
    await fireEvent.changeText(screen.getByLabelText('Cantidad a mover'), '7')
    await waitFor(() => expect(screen.getByText('Solo se pueden mover 6 (lo reservado no se mueve).')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad a mover'), '5')
    await fireEvent.press(screen.getByTestId('transfer-next'))
    await scan('Escanea la posición de destino', 'a-01')
    await waitFor(() => expect(screen.getByText('El destino es la misma posición de origen.')).toBeTruthy())
    await scan('Escanea la posición de destino', 'b-02')
    await waitFor(() => expect(screen.getByTestId('transfer-confirm')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('transfer-confirm'))
    await waitFor(() => expect(screen.getByText(/Listo: se movieron 5 de SKU-1 de A-01 a B-02/)).toBeTruthy())
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      productPublicId: 'p1',
      fromBinId: 41,
      toBinId: 42,
      quantity: 5,
      fromWarehousePublicId: 'wh-1',
    })
  })

  it('un producto con serie no se transfiere desde el aparato', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Medidor', '7501', 'SERIAL')
    mockFetch([BINS, BALANCES])
    await renderRouter('src/app', { initialUrl: '/transfer' })
    await scan('Escanea la posición de origen', 'a-01')
    await scan('Escanea el producto a mover', '7501')
    await waitFor(() => expect(screen.getByText('SKU-1 lleva número de serie: por ahora se transfiere desde la web.')).toBeTruthy())
  })
})
