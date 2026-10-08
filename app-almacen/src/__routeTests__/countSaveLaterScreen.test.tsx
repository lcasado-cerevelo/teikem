// 2026-10-07 — «Guardar y seguir después»: manda lo contado SIN cerrar el conteo; queda abierto (copia en el aparato) y Inicio bloquea las demás
// acciones hasta retomarlo. En archivo propio por el estado global de navegación (ver homeLock.test.tsx).
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCountHints } from '../features/count/openCountHints'
import { getOpenCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { listOutbox } from '../kernel/sync/outbox'
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

const line = (id: number, p: string, sku: string, name: string, countedQty: number | null = null) => ({
  id, productPublicId: p, sku, productName: name, binId: 5, binCode: 'GENERAL', lotId: null, lotNumber: null, countedQty,
})

describe('Conteo por posición — guardar y seguir después', () => {
  it('manda solo el lote, deja el conteo abierto y Inicio bloquea las demás acciones', async () => {
    await setupDevice()
    getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (5, 'GENERAL', 'wh-1', 'GEN', 'General', 'PICKING', 1)")
    mockFetch([
      (c) => {
        if (c.method !== 'GET' || c.path !== '/api/v1/warehouses/wh-1/bins') return null
        const code = new URLSearchParams(c.search).get('search') ?? ''
        return json(200, { total: 1, skip: 0, take: 200, items: [{ id: code === 'A-01' ? 6 : 5, code }] })
      },
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts'
          ? json(200, { count: { id: 42, number: 'CC-00042' }, isBlind: true, lines: [line(11, 'p1', 'SKU-1', 'Tornillo', 12), line(12, 'p2', 'SKU-2', 'Tuerca')] })
          : null,
    ])
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)

    await renderRouter('src/app', { initialUrl: '/count' })
    await fireEvent.changeText(await screen.findByLabelText('Escanea la posición a contar'), 'GENERAL')
    await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
    await waitFor(() => expect(screen.getByText('Tornillo · 12')).toBeTruthy())

    await fireEvent.press(screen.getByRole('button', { name: 'Terminar esta posición' }))
    const buttons = alertSpy.mock.calls[0][2]
    buttons?.find((b) => b.text === 'Guardar y seguir después')?.onPress?.()

    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(listOutbox().map((r) => r.kind)).toEqual(['countBatch'])
    expect(getOpenCountHints('wh-1', 7)).toEqual([expect.objectContaining({ countId: 42, number: 'CC-00042', binCode: 'GENERAL', lines: 2, counted: 1 })])
    expect(alertSpy).toHaveBeenCalledWith('Guardado. El conteo CC-00042 sigue abierto: retómalo desde Conteo.')

    // Inicio: con el conteo abierto en el servidor, las otras acciones avisan en vez de navegar
    await waitFor(() => expect(screen.getByText('Acomodar')).toBeTruthy())
    await fireEvent.press(screen.getByText('Acomodar'))
    expect(alertSpy).toHaveBeenCalledWith('Termina o cancela el conteo en curso antes de usar esto.')
  })
})
