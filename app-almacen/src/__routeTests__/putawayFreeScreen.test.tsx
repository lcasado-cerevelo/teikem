// Lote 198 — Acomodar: primero los acomodos asignados a mí; los que nadie tiene asignado salen con «Ver lo que nadie tiene asignado» y, al escoger uno, se inicia (el servidor lo asigna a quien lo inicia). En archivo propio (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { json, mockFetch, setupDevice, type FetchCall } from './countKit'

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

describe('Acomodar — lo asignado a mí y lo que nadie tiene', () => {
  it('muestra lo mío; lo sin asignar sale con la opción; lo de otra persona no se ofrece; al escoger uno se inicia', async () => {
    await setupDevice()
    const calls = mockFetch([
      (c: FetchCall) =>
        c.method === 'GET' && c.path === '/api/v1/warehouse-tasks'
          ? json(200, {
              total: 3,
              skip: 0,
              take: 100,
              items: [
                { id: 1, sku: 'MIO-1', productName: 'Mío', quantity: 1, toBinCode: null, assignedToUserId: 7 },
                { id: 2, sku: 'LIBRE-2', productName: 'Libre', quantity: 2, toBinCode: null, assignedToUserId: null },
                { id: 3, sku: 'AJENO-3', productName: 'Ajeno', quantity: 3, toBinCode: null, assignedToUserId: 9 },
              ],
            })
          : null,
      (c: FetchCall) => (c.method === 'POST' && c.path === '/api/v1/warehouse-tasks/2/start' ? json(200, { id: 2 }) : null),
      (c: FetchCall) => (c.method === 'GET' && c.path === '/api/v1/warehouse-tasks/putaway-suggestions' ? json(200, []) : null),
    ])
    await renderRouter('src/app', { initialUrl: '/putaway' })
    await waitFor(() => expect(screen.getByText('MIO-1')).toBeTruthy())
    expect(screen.queryByText('LIBRE-2')).toBeNull()
    expect(screen.queryByText('AJENO-3')).toBeNull()

    await fireEvent.press(screen.getByTestId('putaway-free-toggle'))
    expect(screen.getByText('LIBRE-2')).toBeTruthy()
    expect(screen.queryByText('AJENO-3')).toBeNull()

    await fireEvent.press(screen.getByText('LIBRE-2'))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/warehouse-tasks/2/start')).toBe(true))
  })
})
