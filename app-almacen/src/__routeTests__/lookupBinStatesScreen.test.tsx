// Lote A8 — Consultar: estados de la lista de una posición en una sola pantalla: posición vacía, código que no es nada, posición
// dada de baja y sin señal sin consulta anterior. /me no responde (sin saber los permisos no se muestran cantidades). En archivo
// propio (renderRouter).
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

async function scan(code: string) {
  await fireEvent.changeText(screen.getByLabelText('Escanea un producto o una posición'), code)
  await fireEvent.press(screen.getByLabelText('Aceptar'))
}

describe('Consultar — estados de la lista de una posición', () => {
  it('vacía, no encontrada, desactivada y sin señal', async () => {
    await setupDevice()
    const db = getDb()
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, is_active) VALUES (5, 'A-01-01', 'wh-1', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, is_active) VALUES (6, 'OLD-01', 'wh-1', 0)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, is_active) VALUES (7, 'SIN-SENAL', 'wh-1', 1)")
    mockFetch([
      // el servidor no conoce ninguna otra posición activa
      (c) => (c.path === '/api/v1/warehouses/wh-1/bins' ? json(200, { total: 0, skip: 0, take: 200, items: [] }) : null),
      // A-01-01 existe y está vacía; la búsqueda libre no encuentra nada; SIN-SENAL (binIds=7) no responde: sin señal
      (c) =>
        c.path === '/api/v1/inventory/balances' && (c.search.includes('binIds=5') || c.search.includes('search='))
          ? json(200, { total: 0, skip: 0, take: 200, items: [] })
          : null,
    ])

    await renderRouter('src/app', { initialUrl: '/lookup' })
    await waitFor(() => expect(screen.getByText('Consultar')).toBeTruthy())

    await scan('A-01-01')
    await waitFor(() => expect(screen.getByText('No hay productos en esta posición.')).toBeTruthy())
    expect(screen.getByText('Qué hay en A-01-01')).toBeTruthy()
    expect(screen.queryByTestId('scan-message-error')).toBeNull()

    await scan('NADA-99')
    await waitFor(() => expect(screen.getByText('No hay un producto ni una posición con ese código.')).toBeTruthy())
    expect(screen.queryByText('Qué hay en A-01-01')).toBeNull()

    await scan('old-01')
    await waitFor(() => expect(screen.getByText('La posición OLD-01 está desactivada.')).toBeTruthy())

    await scan('SIN-SENAL')
    await waitFor(() => expect(screen.getByText('Sin señal y sin una consulta anterior de esto.')).toBeTruthy())
  })
})
