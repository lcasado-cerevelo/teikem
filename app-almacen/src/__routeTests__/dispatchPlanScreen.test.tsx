// Despacho — plan de salida con varias posiciones (tarea 24d): con una cantidad que no cabe en la primera posición, la pantalla sugiere
// "20 de P-01 y 30 de R-02", se puede cambiar una posición escaneando otra (con existencia) y "Usar este plan" agrega todas las líneas.
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenPick } from '../features/dispatch/localPick'
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

const stock = (...items: Array<Record<string, unknown>>) => (c: { method: string; path: string }) =>
  c.method === 'GET' && c.path === '/api/v1/inventory/exit-options'
    ? json(200, { total: items.length, skip: 0, take: 500, serverTimeUtc: '2026-10-05T12:00:00Z', items: items.map((it, i) => ({ binId: i + 1, rank: i + 1, ...it })) })
    : null

async function openDraft(code: string) {
  await renderRouter('src/app', { initialUrl: '/dispatch' })
  await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
  await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: code } })
  await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
}

const BIN_FIELD = 'Escanea de qué posición sale'

describe('Despacho — plan de salida con varias posiciones', () => {
  it('50 con 20 en P-01 y 40 en R-02 → 20 + 30; cambiar la segunda a una con existencia y usar el plan', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    mockFetch([
      stock(
        { binCode: 'P-01', zoneTypeCode: 'PICKING', available: 20 },
        { binCode: 'R-02', zoneTypeCode: 'RESERVE', available: 40 },
        { binCode: 'R-09', zoneTypeCode: 'RESERVE', available: 100 },
      ),
    ])
    await openDraft('7501')
    await waitFor(() => expect(screen.getByText('Sugerida: P-01')).toBeTruthy())
    // con una cantidad que cabe en la primera posición no hay plan
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '10')
    expect(screen.queryByTestId('dispatch-plan')).toBeNull()

    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '50')
    await waitFor(() => expect(screen.getByTestId('dispatch-plan')).toBeTruthy())
    expect(screen.getByText('20 de P-01')).toBeTruthy()
    expect(screen.getByText('30 de R-02')).toBeTruthy()

    // cambiar el segundo renglón: una posición sin existencia suficiente se rechaza
    await fireEvent.press(screen.getAllByText('Cambiar')[1])
    jest.spyOn(Date, 'now').mockReturnValue(Date.now() + 5000)
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'P-01' } })
    await waitFor(() => expect(screen.getByText(/P-01 no tiene 30 disponibles/)).toBeTruthy())
    expect(screen.getByText('30 de R-02')).toBeTruthy()

    // una con existencia: la reemplaza
    jest.spyOn(Date, 'now').mockReturnValue(Date.now() + 10000)
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'R-09' } })
    await waitFor(() => expect(screen.getByText('30 de R-09')).toBeTruthy())

    await fireEvent.press(screen.getByText('Usar este plan'))
    await waitFor(() =>
      expect(getOpenPick()?.lineRows.map((l) => [l.fromBinCode, l.quantity])).toEqual([
        ['P-01', 20],
        ['R-09', 30],
      ]),
    )
  })

  it('si no alcanza la existencia avisa cuánto falta y no deja usar el plan', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    mockFetch([stock({ binCode: 'P-01', zoneTypeCode: 'PICKING', available: 20 }, { binCode: 'R-02', zoneTypeCode: 'RESERVE', available: 10 })])
    await openDraft('7501')
    await waitFor(() => expect(screen.getByText('Sugerida: P-01')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '50')
    await waitFor(() => expect(screen.getByText('No alcanza: faltan 20. Baja la cantidad.')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('dispatch-plan-use'))
    expect(getOpenPick()?.lineRows).toEqual([])
  })
})
