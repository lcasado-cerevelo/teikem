// Despacho — listado de posiciones marcables (2026-10-07, unido al plan de salida de la tarea 24d): la sugerida se toca y llena la posición; con
// una cantidad que no cabe en ella, abajo salen las posiciones con existencia (las sugeridas primero), se marcan y "Usar estas posiciones" agrega todas.
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
const STOCK = [
  { binCode: 'P-01', zoneTypeCode: 'PICKING', available: 20 },
  { binCode: 'R-02', zoneTypeCode: 'RESERVE', available: 40 },
  { binCode: 'R-09', zoneTypeCode: 'RESERVE', available: 100 },
]

describe('Despacho — listado de posiciones marcables', () => {
  it('50 con 20 en P-01 y 40 en R-02: avisa que no alcanza sola, marca las sugeridas y usa las posiciones', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    mockFetch([stock(...STOCK)])
    await openDraft('7501')
    await waitFor(() => expect(screen.getByText('Sugerida: P-01')).toBeTruthy())
    // la cantidad cabe en la sugerida: no se ve el listado
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '10')
    expect(screen.queryByTestId('dispatch-plan')).toBeNull()

    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '50')
    await waitFor(() => expect(screen.getByTestId('dispatch-plan')).toBeTruthy())
    expect(screen.getByText('No alcanza sola: faltan 30. Marca más posiciones abajo.')).toBeTruthy()
    expect(screen.getByText('Tomado 0 de 50')).toBeTruthy()

    await fireEvent.press(screen.getByTestId('dispatch-plan-recommended'))
    await waitFor(() => expect(screen.getByText('Tomado 50 de 50')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('dispatch-plan-use'))
    await waitFor(() =>
      expect(getOpenPick()?.lineRows.map((l) => [l.fromBinCode, l.quantity])).toEqual([
        ['P-01', 20],
        ['R-02', 30],
      ]),
    )
  })

  it('se marca a mano otra posición, sin pasar del total, y al bajar la cantidad las marcas se recortan', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    mockFetch([stock(...STOCK)])
    await openDraft('7501')
    await waitFor(() => expect(screen.getByText('Sugerida: P-01')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '50')
    await waitFor(() => expect(screen.getByTestId('dispatch-plan')).toBeTruthy())

    await fireEvent.press(screen.getByLabelText('R-09'))
    await waitFor(() => expect(screen.getByText('Tomado 50 de 50')).toBeTruthy())
    // ya está completo: otra marca no agrega nada
    await fireEvent.press(screen.getByLabelText('P-01'))
    expect(screen.getByText('Tomado 50 de 50')).toBeTruthy()

    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '30')
    await waitFor(() => expect(screen.getByText('Tomado 30 de 30')).toBeTruthy())
  })

  it('con una cantidad que cabe, tocar la sugerida pone su posición en el campo y Aceptar agrega la línea', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    mockFetch([stock(...STOCK)])
    await openDraft('7501')
    await waitFor(() => expect(screen.getByText('Sugerida: P-01')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '10')
    await fireEvent.press(screen.getByTestId('dispatch-suggestion'))
    await waitFor(() => expect(screen.getByLabelText(BIN_FIELD).props.value).toBe('P-01'))
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(getOpenPick()?.lineRows.map((l) => [l.fromBinCode, l.quantity])).toEqual([['P-01', 10]]))
  })

  it('si no alcanza la existencia avisa cuánto falta y no deja usar las posiciones', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    mockFetch([stock({ binCode: 'P-01', zoneTypeCode: 'PICKING', available: 20 }, { binCode: 'R-02', zoneTypeCode: 'RESERVE', available: 10 })])
    await openDraft('7501')
    await waitFor(() => expect(screen.getByText('Sugerida: P-01')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '50')
    await waitFor(() => expect(screen.getByText('No alcanza: faltan 20. Baja la cantidad.')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('dispatch-plan-recommended'))
    await fireEvent.press(screen.getByTestId('dispatch-plan-use'))
    expect(getOpenPick()?.lineRows).toEqual([])
  })
})
