// Lote A4 (adenda 5d) — producto SIN existencia: la app abre el conteo con allowEmpty, el servidor lo crea vacío y la lista
// muestra el bloque "El sistema no tiene existencia…" con «Otra posición» en primer plano. En archivo propio: renderRouter()
// no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo.
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { setKv, KvKeys } from '../kernel/db/kv'
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

const EMPTY_BLOCK = 'El sistema no tiene existencia de este producto. Si lo encontraste en alguna posición, usa «Otra posición».'

async function openEmptyCount() {
  await setupDevice()
  insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
  setKv(KvKeys.countEntryMode, 'PRODUCT')
  const calls = mockFetch([
    (c) =>
      c.method === 'POST' && c.path === '/api/v1/cycle-counts'
        ? json(200, { count: { id: 400, originCode: 'PRODUCT', lineCount: 0 }, isBlind: true, lines: [] })
        : null,
    (c) => (c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/zones' ? json(200, [{ id: 4, code: 'PCK', name: 'Picking', isActive: true }]) : null),
    (c) =>
      c.method === 'POST' && c.path === '/api/v1/cycle-counts/400/bins'
        ? json(200, { id: 99, code: 'Z-09', zoneId: 4, zoneCode: 'PCK', isProvisional: true, provisionalCycleCountId: 400 })
        : null,
    (c) => (c.method === 'DELETE' && c.path === '/api/v1/cycle-counts/400' ? new Response(null, { status: 204 }) : null),
  ])
  await renderRouter('src/app', { initialUrl: '/count' })
  await waitFor(() => expect(screen.getByText('Escanea el producto a contar')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Escanea el producto a contar'), 'SKU-1')
  await fireEvent.press(screen.getByLabelText('Aceptar'))
  await waitFor(() => expect(screen.getByText(EMPTY_BLOCK)).toBeTruthy())
  return calls
}

describe('Conteo por producto — sin existencia, cancelar', () => {
  it('un conteo vacío se cancela con DELETE y no deja nada abierto', async () => {
    const calls = await openEmptyCount()
    expect(getOpenCount()).not.toBeNull()
    jest.spyOn(Alert, 'alert').mockImplementation((_title, _body, buttons) => {
      void buttons?.find((b) => b.style === 'destructive')?.onPress?.()
    })
    await fireEvent.press(screen.getByRole('button', { name: 'Cancelar conteo' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(calls.map((c) => `${c.method} ${c.path}`)).toContain('DELETE /api/v1/cycle-counts/400')
    expect(listOutbox()).toEqual([])
  })
})
