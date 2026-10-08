// 2026-10-07 — Conteo: la lista «Conteos abiertos» (los que este usuario dejó abiertos en el servidor) se continúa por su id, y mientras haya uno no se
// abre otro de otra posición. En archivo propio por el estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount } from '../features/count/localCount'
import { addOpenCountHint } from '../features/count/openCountHints'
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

describe('Conteo — conteos abiertos', () => {
  it('los lista, no deja abrir otra posición y «Continuar» retoma el conteo con lo ya contado', async () => {
    await setupDevice()
    const db = getDb()
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (5, 'GENERAL', 'wh-1', 'GEN', 'General', 'PICKING', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (6, 'A-01', 'wh-1', 'GEN', 'General', 'PICKING', 1)")
    addOpenCountHint({ countId: 77, number: 'CC-00077', binCode: 'GENERAL', warehousePublicId: 'wh-1', userId: 7, lines: 2, counted: 1 })
    const calls = mockFetch([
      (c) => {
        if (c.method !== 'GET' || c.path !== '/api/v1/warehouses/wh-1/bins') return null
        const code = new URLSearchParams(c.search).get('search') ?? ''
        return json(200, { total: 1, skip: 0, take: 200, items: [{ id: code === 'A-01' ? 6 : 5, code }] })
      },
      (c) =>
        c.method === 'GET' && c.path === '/api/v1/cycle-counts/77'
          ? json(200, {
              count: { id: 77, number: 'CC-00077' },
              isBlind: true,
              lines: [
                { id: 21, productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', binId: 5, binCode: 'GENERAL', countedQty: 3 },
                { id: 22, productPublicId: 'p2', sku: 'SKU-2', productName: 'Tuerca', binId: 5, binCode: 'GENERAL', countedQty: null },
              ],
            })
          : null,
    ])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Conteos abiertos')).toBeTruthy())
    expect(screen.getByText('CC-00077 · GENERAL')).toBeTruthy()
    expect(screen.getByText('1 de 2 contados')).toBeTruthy()

    // otra posición: bloqueada
    await fireEvent.changeText(screen.getByLabelText('Escanea la posición a contar'), 'A-01')
    await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
    await waitFor(() => expect(screen.getByText('Tienes abierto el conteo CC-00077 (GENERAL). Termínalo o guárdalo antes de contar otra cosa.')).toBeTruthy())
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    // Continuar: se retoma por su id
    await fireEvent.press(screen.getByLabelText('Continuar CC-00077'))
    await waitFor(() => expect(getOpenCount()).toMatchObject({ countId: 77, binCode: 'GENERAL' }))
    await waitFor(() => expect(screen.getByText('Tornillo · 3')).toBeTruthy())
    expect(screen.getByText('Retomaste el conteo CC-00077 de GENERAL: ya llevas 1 de 2 contados.')).toBeTruthy()
  })
})
