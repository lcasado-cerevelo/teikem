// Lote 172 — «Mis tareas»: lo asignado al usuario se ve en Inicio (con la cuenta), la pantalla lo lista y tocar uno abre Recibir / Despacho de ese documento; lo que nadie tiene asignado se puede tomar.
// En archivo propio por el estado global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { getOpenReceipt } from '../features/receive/localLookup'
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

function seed() {
  const db = getDb()
  db.runSync("INSERT INTO asn (id, warehouse_public_id, client_name, reference, expected_date, number, assigned_to_user_id, is_active) VALUES (1, 'wh-1', 'AxisCare', 'AX-4012', '2026-10-22', 'LLE-00001', 7, 1)")
  db.runSync("INSERT INTO asn (id, warehouse_public_id, client_name, reference, number, assigned_to_user_id, is_active) VALUES (2, 'wh-1', 'Otro', 'OT-1', 'LLE-00002', NULL, 1)")
}

describe('Mis tareas', () => {
  it('Inicio muestra la cuenta de lo asignado y la pantalla lista el aviso de llegada', async () => {
    await setupDevice()
    seed()
    mockFetch([(c) => (c.path === '/api/v1/warehouse-tasks' ? json(200, { items: [{ id: 5, sku: 'MED-1', productName: 'Medicina', quantity: 4, toBinCode: 'A-01' }], total: 1 }) : null)])
    await renderRouter('src/app', { initialUrl: '/home' })
    await waitFor(() => expect(screen.getByText('Mis tareas (1)')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('home-my-tasks'))
    await waitFor(() => expect(screen.getByTestId('my-asn-1')).toBeTruthy())
    expect(screen.getByText('LLE-00001 · AX-4012')).toBeTruthy()
    await waitFor(() => expect(screen.getByTestId('my-putaway-5')).toBeTruthy())   // el acomodo asignado, bajado en línea
    expect(screen.queryByTestId('take-asn-2')).toBeNull()   // lo que nadie tiene asignado está plegado hasta pedirlo
  })

  it('tocar el aviso abre Recibir con ese aviso', async () => {
    await setupDevice()
    seed()
    mockFetch([])
    await renderRouter('src/app', { initialUrl: '/my-tasks' })
    await waitFor(() => expect(screen.getByTestId('my-asn-1')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('my-asn-1'))
    await waitFor(() => expect(getOpenReceipt()).not.toBeNull())
    expect(getOpenReceipt()).toMatchObject({ doc: { asnId: 1, kind: 'asn', label: 'LLE-00001' } })
  })

  it('«Tomar» un aviso sin asignar lo llama al servidor y lo deja asignado a mí', async () => {
    await setupDevice()
    seed()
    const calls = mockFetch([
      (c) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions: ['warehouse.pick', 'warehouse.pick.any'] }) : null),
      (c) => (c.method === 'POST' && c.path === '/api/v1/asns/2/take' ? json(200, { id: 2 }) : null),
    ])
    await renderRouter('src/app', { initialUrl: '/my-tasks' })
    await waitFor(() => expect(screen.getByTestId('my-tasks-free-toggle')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('my-tasks-free-toggle'))
    await fireEvent.press(await screen.findByTestId('take-asn-2'))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/asns/2/take')).toBe(true))
    await waitFor(() => expect(screen.getByTestId('my-asn-2')).toBeTruthy())
  })

  it('Lote 193: sin warehouse.pick.any no se ofrece lo que nadie tiene asignado', async () => {
    await setupDevice()
    seed()
    mockFetch([(c) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions: ['warehouse.pick'] }) : null)])
    await renderRouter('src/app', { initialUrl: '/my-tasks' })
    await waitFor(() => expect(screen.getByTestId('my-asn-1')).toBeTruthy())
    expect(screen.queryByTestId('my-tasks-free-toggle')).toBeNull()
  })

  it('sin nada asignado lo dice', async () => {
    await setupDevice()
    mockFetch([])
    await renderRouter('src/app', { initialUrl: '/my-tasks' })
    await waitFor(() => expect(screen.getByTestId('my-tasks-empty')).toBeTruthy())
  })
})
