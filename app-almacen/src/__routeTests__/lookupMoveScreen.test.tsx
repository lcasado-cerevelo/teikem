// Consultar → «Mover» (2026-10-10): con el permiso warehouse.transfer, cada producto de la posición consultada trae «Mover»; al tocarlo se abre Transferir con la
// posición de origen y el producto ya puestos. Sin el permiso no hay botón. En archivo propio por el estado global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
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

const ROWS = [
  { id: 1, binId: 5, binCode: 'A-01-01', zoneTypeCode: 'PICKING', productPublicId: 'p1', sku: 'TOR-1', productName: 'Tornillo', lotId: null, lotNumber: null, qtyOnHand: 10, qtyAvailable: 8 },
  { id: 2, binId: 5, binCode: 'A-01-01', zoneTypeCode: 'PICKING', productPublicId: 'p2', sku: 'TUE-2', productName: 'Tuerca', lotId: null, lotNumber: null, qtyOnHand: 3, qtyAvailable: 0 },
]

async function openLookup(permissions: string[]) {
  await setupDevice()
  getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, is_active) VALUES (5, 'A-01-01', 'wh-1', 1)")
  insertProduct(1, 'p1', 'TOR-1', 'Tornillo', '7501', 'NONE')
  mockFetch([
    (c) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions }) : null),
    (c) => (c.path === '/api/v1/inventory/balances' ? json(200, { total: 2, skip: 0, take: 200, items: ROWS }) : null),
  ])
  await renderRouter('src/app', { initialUrl: '/lookup' })
  await waitFor(() => expect(screen.getByText('Consultar')).toBeTruthy())
  await waitFor(() => expect(screen.getByLabelText('Escanea un producto o una posición')).toBeTruthy())
}

describe('Consultar — Mover', () => {
  it('con el permiso, «Mover» solo en lo que tiene disponible y abre Transferir con la posición y el producto puestos', async () => {
    await openLookup(['inventory.view', 'warehouse.transfer'])
    await fireEvent.changeText(screen.getByLabelText('Escanea un producto o una posición'), 'a-01-01')
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(screen.getByText('Qué hay en A-01-01')).toBeTruthy())
    expect(screen.getByTestId('move-TOR-1')).toBeTruthy()
    expect(screen.queryByTestId('move-TUE-2')).toBeNull() // nada disponible: no se ofrece
    await fireEvent.press(screen.getByTestId('move-TOR-1'))
    await waitFor(() => expect(screen.getByText('Origen: A-01-01')).toBeTruthy())
    await waitFor(() => expect(screen.getByText('TOR-1 · Tornillo')).toBeTruthy())
    expect(screen.getByLabelText('Cantidad a mover')).toBeTruthy()
  })

  it('sin el permiso no hay «Mover»', async () => {
    await openLookup(['inventory.view'])
    await fireEvent.changeText(screen.getByLabelText('Escanea un producto o una posición'), 'a-01-01')
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(screen.getByText('Qué hay en A-01-01')).toBeTruthy())
    expect(screen.queryByTestId('move-TOR-1')).toBeNull()
  })
})
