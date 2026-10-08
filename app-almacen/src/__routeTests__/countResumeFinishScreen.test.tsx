// 2026-10-07 — Conteo por posición: escanear una posición que ya tiene un conteo abierto lo RETOMA con lo ya contado, y «Terminar esta posición» con
// líneas sin contar pregunta qué hacer; «Dejar en 0 y terminar» manda todo en un lote y el cierre. En archivo propio: renderRouter() no aísla su estado
// global de navegación entre pruebas del mismo archivo (ver homeLock.test.tsx).
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

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

const line = (id: number, p: string, sku: string, name: string, countedQty: number | null = null) => ({
  id, productPublicId: p, sku, productName: name, binId: 5, binCode: 'GENERAL', lotId: null, lotNumber: null, countedQty,
})

describe('Conteo por posición — retomar y terminar con líneas sin contar', () => {
  it('retoma lo ya contado; con líneas sin contar ofrece seguir, guardar o dejar en 0; en 0 manda un lote con todo y el cierre', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (5, 'GENERAL', 'wh-1', 'GEN', 'General', 'PICKING', 1)")
    const calls = mockFetch([
      (c) => {
        if (c.method !== 'GET' || c.path !== '/api/v1/warehouses/wh-1/bins') return null
        const code = new URLSearchParams(c.search).get('search') ?? ''
        return json(200, { total: 1, skip: 0, take: 200, items: [{ id: code === 'A-01' ? 6 : 5, code }] })
      },
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts'
          ? json(200, { count: { id: 42, number: 'CC-00042' }, isBlind: true, resumed: true, lines: [line(11, 'p1', 'SKU-1', 'Tornillo', 12), line(12, 'p2', 'SKU-2', 'Tuerca'), line(13, 'p3', 'SKU-3', 'Arandela')] })
          : null,
    ])
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined)

    await renderRouter('src/app', { initialUrl: '/count' })
    await fireEvent.changeText(await screen.findByLabelText('Escanea la posición a contar'), 'GENERAL')
    await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])

    // retomó: lo ya contado vuelve y se avisa
    await waitFor(() => expect(screen.getByText('Retomaste el conteo CC-00042 de GENERAL: ya llevas 1 de 3 contados.')).toBeTruthy())
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({ binIds: [5], assignToMe: true, resumeOpen: true })
    expect(screen.getByText('Tornillo · 12')).toBeTruthy()
    expect(getOpenCount()).toMatchObject({ countId: 42, mode: 'BIN' })

    // faltan 2: pregunta qué hacer
    await fireEvent.press(screen.getByRole('button', { name: 'Terminar esta posición' }))
    expect(alertSpy).toHaveBeenCalledTimes(1)
    const [title, , buttons] = alertSpy.mock.calls[0]
    expect(title).toBe('Faltan 2 producto(s) por contar')
    expect((buttons ?? []).map((b) => b.text)).toEqual(['Seguir contando', 'Guardar y seguir después', 'Dejar en 0 y terminar'])
    expect(listOutbox()).toHaveLength(0)

    // dejar en 0 y terminar: un lote con las 3 líneas (la contada y las dos en 0) y el cierre
    buttons?.find((b) => b.text === 'Dejar en 0 y terminar')?.onPress?.()
    await waitFor(() => expect(getOpenCount()).toBeNull())
    const outbox = listOutbox()
    expect(outbox.map((r) => r.kind)).toEqual(['countBatch', 'countFinish'])
    expect(JSON.parse(outbox[0].body)).toEqual({ lines: [{ lineId: 11, countedQty: 12 }, { lineId: 12, countedQty: 0 }, { lineId: 13, countedQty: 0 }] })
    expect(outbox[1].path).toBe('/api/v1/cycle-counts/42/finish')
  })
})
