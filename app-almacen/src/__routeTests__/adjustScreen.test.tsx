// Ajustar desde Consultar (2026-10-10): con el permiso warehouse.adjust, cada producto de la posición consultada trae «Ajustar»; abre la pantalla con la posición y el
// producto puestos y manda un ajuste de CANTIDAD (con signo, motivo y nota). Sin el permiso no hay botón. En archivo propio por el estado global de renderRouter().
import { Alert } from 'react-native'
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
  { id: 1, binId: 5, binCode: 'A-01-01', zoneTypeCode: 'PICKING', productPublicId: 'p1', sku: 'TOR-1', productName: 'Tornillo', lotId: null, lotNumber: null, qtyOnHand: 10, qtyReserved: 2, qtyAvailable: 8 },
]

async function openLookup(permissions: string[]) {
  await setupDevice()
  getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, is_active) VALUES (5, 'A-01-01', 'wh-1', 1)")
  insertProduct(1, 'p1', 'TOR-1', 'Tornillo', '7501', 'NONE')
  const calls = mockFetch([
    (c) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions }) : null),
    (c) => (c.path === '/api/v1/inventory/balances' ? json(200, { total: 1, skip: 0, take: 200, items: ROWS }) : null),
    (c) => (c.method === 'POST' && c.path === '/api/v1/inventory/adjustments/quantity' ? json(200, { transactions: [], balances: [] }) : null),
  ])
  await renderRouter('src/app', { initialUrl: '/lookup' })
  await waitFor(() => expect(screen.getByText('Consultar')).toBeTruthy())
  await waitFor(() => expect(screen.getByLabelText('Escanea un producto o una posición')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Escanea un producto o una posición'), 'a-01-01')
  await fireEvent.press(screen.getByLabelText('Aceptar'))
  await waitFor(() => expect(screen.getByText('Qué hay en A-01-01')).toBeTruthy())
  return calls
}

describe('Consultar — Ajustar', () => {
  it('con warehouse.adjust: baja la cantidad con motivo y nota (la nota es obligatoria) y manda la cantidad con signo', async () => {
    jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => {
      buttons?.find((b) => b.style !== 'cancel')?.onPress?.()
    })
    const calls = await openLookup(['inventory.view', 'warehouse.adjust'])
    await fireEvent.press(screen.getByTestId('adjust-TOR-1'))
    await waitFor(() => expect(screen.getByText('Posición A-01-01')).toBeTruthy())
    await fireEvent.press(await screen.findByTestId('adjust-down'))
    await waitFor(() => expect(screen.getByText('Se puede bajar hasta 8 (lo reservado no sale).')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '3')
    await fireEvent.press(screen.getByTestId('adjust-reason-LOSS'))
    // sin nota no deja
    await fireEvent.press(screen.getByTestId('adjust-confirm'))
    await waitFor(() => expect(screen.getByText('Escribe una nota que explique el ajuste.')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Nota (obligatoria)'), 'se perdieron en el pasillo')
    await fireEvent.press(screen.getByTestId('adjust-confirm'))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      productPublicId: 'p1',
      warehousePublicId: 'wh-1',
      binId: 5,
      quantity: -3,
      reason: 'LOSS',
      notes: 'se perdieron en el pasillo',
    })
  })

  it('sin el permiso no hay «Ajustar»', async () => {
    await openLookup(['inventory.view', 'warehouse.transfer'])
    expect(screen.queryByTestId('adjust-TOR-1')).toBeNull()
    expect(screen.getByTestId('move-TOR-1')).toBeTruthy()
  })
})
