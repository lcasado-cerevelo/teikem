// Lote A8 — Consultar: escanear una POSICIÓN muestra en vivo lo que el sistema dice que hay en ella, una fila por producto
// (lotes juntos), con las cantidades del sistema porque quien entró tiene warehouse.count (GET /api/v1/me). En archivo propio:
// renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
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

const ROWS = [
  { id: 1, binCode: 'A-01-01', productPublicId: 'p1', sku: 'TOR-1', productName: 'Tornillo', lotNumber: 'A', qtyOnHand: 1000, qtyAvailable: 900 },
  { id: 2, binCode: 'A-01-01', productPublicId: 'p1', sku: 'TOR-1', productName: 'Tornillo', lotNumber: 'B', qtyOnHand: 250, qtyAvailable: 250 },
  { id: 3, binCode: 'A-01-01', productPublicId: 'p2', sku: 'TUE-2', productName: 'Tuerca', lotNumber: null, qtyOnHand: 3, qtyAvailable: 3 },
]

describe('Consultar — lo que hay en una posición (con warehouse.count)', () => {
  it('escanear la posición lista sus productos con lotes agregados y las cantidades del sistema', async () => {
    await setupDevice()
    getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, is_active) VALUES (5, 'A-01-01', 'wh-1', 1)")
    const calls = mockFetch([
      (c) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions: ['inventory.view', 'warehouse.count'] }) : null),
      (c) => (c.path === '/api/v1/inventory/balances' ? json(200, { total: 3, skip: 0, take: 200, items: ROWS }) : null),
    ])

    await renderRouter('src/app', { initialUrl: '/lookup' })
    await waitFor(() => expect(screen.getByText('Consultar')).toBeTruthy())
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/me')).toBe(true))

    // el lector escribe el código y acepta (aquí: escribir + Aceptar, que es lo mismo)
    await fireEvent.changeText(screen.getByLabelText('Escanea un producto o una posición'), 'a-01-01')
    await fireEvent.press(screen.getByLabelText('Aceptar'))

    await waitFor(() => expect(screen.getByText('Qué hay en A-01-01')).toBeTruthy())
    expect(screen.getByText('2 productos en esta posición')).toBeTruthy()
    expect(screen.getByText('TOR-1')).toBeTruthy()
    expect(screen.getByText('Tornillo')).toBeTruthy()
    expect(screen.getByText('Lote A, B')).toBeTruthy()
    expect(screen.getByText('En mano: 1,250 · Disponible: 1,150')).toBeTruthy()
    expect(screen.getByText('TUE-2')).toBeTruthy()
    expect(screen.getByText('En mano: 3 · Disponible: 3')).toBeTruthy()
    // sin buscador con 2 productos
    expect(screen.queryByLabelText('Buscar producto o lote')).toBeNull()

    // la posición se resolvió en el aparato (sin preguntar al servidor) y los saldos se pidieron por su id, no por texto libre
    expect(calls.some((c) => c.path.includes('/bins'))).toBe(false)
    const balances = calls.find((c) => c.path === '/api/v1/inventory/balances')
    expect(balances?.search).toContain('binIds=5')
    expect(balances?.search).not.toContain('search=')
  })
})
