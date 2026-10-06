// Tarea 26 — "Buscar en la lista" en los campos de posición y producto: abre el buscador, escribir filtra, tocar una fila equivale a escanear su código.
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { insertProduct, json, mockFetch, setupDevice, type FetchCall } from './countKit'

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

describe('Buscar en la lista', () => {
  it('Acomodar: elegir una posición de la lista la usa como destino', async () => {
    await setupDevice()
    const db = getDb()
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (1, 'A-01', 'wh-1', 'RSV', 'Reserva', 'RESERVE', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (2, 'B-02', 'wh-1', 'PCK', 'Picking', 'PICKING', 1)")
    const calls = mockFetch([
      (c: FetchCall) =>
        c.method === 'GET' && c.path === '/api/v1/warehouse-tasks'
          ? json(200, { total: 1, skip: 0, take: 100, items: [{ id: 5, sku: 'SKU-1', productName: 'Tornillo', quantity: 10, toBinCode: null, assignedToUserId: null }] })
          : null,
      (c: FetchCall) => (c.method === 'POST' && c.path === '/api/v1/warehouse-tasks/5/start' ? json(200, { id: 5 }) : null),
      (c: FetchCall) => (c.method === 'GET' && c.path === '/api/v1/warehouse-tasks/putaway-suggestions' ? json(200, []) : null),
      (c: FetchCall) => {
        if (c.method !== 'GET' || c.path !== '/api/v1/warehouses/wh-1/bins') return null
        const code = new URLSearchParams(c.search).get('search') ?? ''
        return json(200, { total: 1, skip: 0, take: 200, items: [{ id: code === 'B-02' ? 2 : 1, code }] })
      },
      (c: FetchCall) => (c.method === 'POST' && c.path === '/api/v1/warehouse-tasks/5/complete' ? json(200, { id: 5 }) : null),
    ])
    await renderRouter('src/app', { initialUrl: '/putaway' })
    await waitFor(() => expect(screen.getByText('SKU-1')).toBeTruthy())
    await fireEvent.press(screen.getByText('SKU-1'))
    await waitFor(() => expect(screen.getByLabelText('Escanea la posición destino')).toBeTruthy())

    await fireEvent.press(screen.getByLabelText('Buscar en la lista'))
    await waitFor(() => expect(screen.getByTestId('picker-modal')).toBeTruthy())
    // sin escribir salen todas; escribir filtra
    expect(screen.getByLabelText('A-01')).toBeTruthy()
    expect(screen.getByLabelText('B-02')).toBeTruthy()
    await fireEvent.changeText(screen.getByLabelText('Buscar'), 'pick')
    await waitFor(() => expect(screen.queryByLabelText('A-01')).toBeNull())
    await fireEvent.press(screen.getByLabelText('B-02'))

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/warehouse-tasks/5/complete')).toBe(true))
    expect(calls.find((c) => c.path.endsWith('/complete'))?.body).toEqual({ toBinId: 2, quantity: 10 })
  })

  it('Despacho: elegir un producto de la lista equivale a escanearlo', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    insertProduct(2, 'p2', 'SKU-2', 'Tuerca', '7502', 'NONE')
    mockFetch([])
    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await fireEvent.press(screen.getByLabelText('Buscar en la lista'))
    await waitFor(() => expect(screen.getByTestId('picker-modal')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Buscar'), 'tuerca')
    await waitFor(() => expect(screen.queryByLabelText('SKU-1')).toBeNull())
    await fireEvent.press(screen.getByLabelText('SKU-2'))
    // abrió el borrador del producto elegido: pide la cantidad
    await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
    expect(screen.getAllByText(/Tuerca/).length).toBeGreaterThan(0)
  })
})
