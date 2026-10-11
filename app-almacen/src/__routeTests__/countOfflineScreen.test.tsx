// 2026-10-11 (señal débil) — Conteo por posición SIN señal: si el servidor no responde al escanear la posición, el conteo se abre EN EL APARATO (sin número,
// siempre a ciegas, con lo que el aparato espera encontrar según sus saldos) y al terminar se manda entero en UNA fila de la cola (`countOffline`).
// En archivo propio por el estado global de navegación (ver homeLock.test.tsx).
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { offlineExpectedLines } from '../features/count/countApi'
import { getOpenCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
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

function seedBinWithStock() {
  insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
  insertProduct(2, 'p2', 'SKU-2', 'Tuerca', '7502', 'LOT')
  getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (5, 'GENERAL', 'wh-1', 'GEN', 'General', 'PICKING', 1)")
  getDb().runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, lot_id, lot_number, qty_on_hand, qty_reserved, updated_at_utc) VALUES
       (1, 'wh-1', 5, 1, 'p1', NULL, NULL, 12, 0, '2026-01-01T00:00:00Z'),
       (2, 'wh-1', 5, 2, 'p2', 9, 'L-9', 3, 0, '2026-01-01T00:00:00Z')`,
  )
}

async function openOffline() {
  await renderRouter('src/app', { initialUrl: '/count' })
  await fireEvent.changeText(await screen.findByLabelText('Escanea la posición a contar'), 'GENERAL')
  await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
  await waitFor(() => expect(getOpenCount()).not.toBeNull())
}

/** Captura por la pantalla, como lo hace el operario: escanea el producto, escribe la cantidad y toca Agregar. */
async function scanAndAdd(code: string, qty: string) {
  await fireEvent(screen.getByLabelText('Escanea el producto contado'), 'submitEditing', { nativeEvent: { text: code } })
  await waitFor(() => expect(screen.getByLabelText('Cantidad encontrada')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Cantidad encontrada'), qty)
  await fireEvent.press(screen.getByText('Agregar'))
}

describe('Conteo por posición sin señal', () => {
  it('abre el conteo en el aparato (countId 0, a ciegas) con lo esperado según los saldos locales y avisa que se mandará solo', async () => {
    await setupDevice()
    seedBinWithStock()
    mockFetch([]) // el servidor no responde nada: todo es «sin señal»
    jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)

    await openOffline()

    expect(getOpenCount()).toMatchObject({ countId: 0, mode: 'BIN', binId: 5, binCode: 'GENERAL', isBlind: true })
    await waitFor(() => expect(screen.getByText('Sin señal: estás contando en el aparato. Lo contado se enviará solo al volver la señal.')).toBeTruthy())
    expect(offlineExpectedLines('wh-1', 5).map((l) => [l.sku, l.lotNumber, l.systemQty])).toEqual([
      ['SKU-1', null, null],
      ['SKU-2', 'L-9', null],
    ])
    expect(listOutbox()).toHaveLength(0) // todavía nada que mandar
  })

  it('al terminar, encola UNA fila countOffline con todas las líneas por posición + producto (+ lote) y la hora en que se abrió', async () => {
    await setupDevice()
    seedBinWithStock()
    mockFetch([])
    jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)
    await openOffline()

    const open = getOpenCount()!
    await scanAndAdd('7501', '11')
    await scanAndAdd('7502', '3')
    await fireEvent.press(screen.getByRole('button', { name: 'Terminar esta posición' }))

    await waitFor(() => expect(getOpenCount()).toBeNull())
    const rows = listOutbox()
    expect(rows.map((r) => r.kind)).toEqual(['countOffline'])
    expect(rows[0].path).toBe('/api/v1/cycle-counts/offline-bin')
    expect(JSON.parse(rows[0].body)).toEqual({
      warehousePublicId: 'wh-1',
      binId: 5,
      lines: [
        { binId: 5, productPublicId: 'p1', countedQty: 11 },
        { binId: 5, productPublicId: 'p2', countedQty: 3, lot: { number: 'L-9' } },
      ],
      finish: true,
      countedAtUtc: open.createdAtUtc,
    })
  })

  it('si al sincronizar el servidor la suma al conteo abierto de otro contador, se lo dice', async () => {
    await setupDevice()
    seedBinWithStock()
    mockFetch([(c) => (c.method === 'POST' && c.path === '/api/v1/cycle-counts/offline-bin' ? json(200, { joined: true, finished: false, movedSinceCounted: false, created: false }) : null)])
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)
    await openOffline()

    await scanAndAdd('7501', '1')
    await scanAndAdd('7502', '1')
    await fireEvent.press(screen.getByRole('button', { name: 'Terminar esta posición' }))

    await waitFor(() =>
      expect(alertSpy).toHaveBeenCalledWith('Esa posición ya tenía un conteo abierto de otro contador: lo que contaste se sumó a ese conteo y lo sigue su dueño.'),
    )
  })

  it('cancelar un conteo hecho sin señal solo lo descarta en el aparato (no hay conteo en el servidor)', async () => {
    await setupDevice()
    seedBinWithStock()
    const calls = mockFetch([])
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)
    await openOffline()

    await fireEvent.press(screen.getByRole('button', { name: 'Cancelar conteo' }))
    const buttons = alertSpy.mock.calls[0][2]
    buttons?.find((b) => b.text === 'Cancelar conteo')?.onPress?.()

    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)
  })
})
