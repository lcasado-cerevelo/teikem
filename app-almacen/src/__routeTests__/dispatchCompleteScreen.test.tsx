// Pedido del dueño 2026-10-05 — Despacho: «Completar despacho» saca el inventario SIN empacar (POST /pick-batches con las líneas, sin orden ni
// consignatario); empacar queda como opción aparte. Las posiciones se resuelven primero con las del aparato (sin señal) y, sin red, el despacho
// se encola (kind collect). En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { addLocalPickLine, getOpenPick, startLocalPick } from '../features/dispatch/localPick'
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

function seedPick() {
  getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, is_active) VALUES (55, 'A-01', 'wh-1', 1, 1)")
  const id = startLocalPick('wh-1', null)
  addLocalPickLine(id, { productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', quantity: 3, fromBinCode: 'A-01' })
}

describe('Despacho — completar sin empacar', () => {
  it('ofrece «Completar despacho» antes de «Empacar»; completar recolecta sin orden y vuelve a Inicio', async () => {
    await setupDevice()
    seedPick()
    const calls = mockFetch([(c) => (c.method === 'POST' && c.path === '/api/v1/pick-batches' ? json(200, { id: 1 }) : null)])
    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Completar despacho' })).toBeTruthy())
    expect(screen.getByRole('button', { name: 'Empacar' })).toBeTruthy()
    expect(screen.getByText('Saca el inventario sin empacar. Se manda cuando haya señal; empacar es opcional.')).toBeTruthy()

    await fireEvent.press(screen.getByRole('button', { name: 'Completar despacho' }))
    await waitFor(() => expect(getOpenPick()).toBeNull())
    const post = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/pick-batches')
    // la posición se resolvió con la del aparato (no hizo falta pedirla al servidor) y no lleva orden ni consignatario
    expect(post?.body).toEqual({ warehousePublicId: 'wh-1', lines: [{ productPublicId: 'p1', quantity: 3, binId: 55 }] })
    expect(calls.some((c) => c.path.includes('/bins'))).toBe(false)
    expect(calls.some((c) => c.path.includes('collect-and-pack'))).toBe(false)
  })

  it('sin señal queda en la cola (collect) y el despacho local se cierra', async () => {
    await setupDevice()
    seedPick()
    mockFetch([])
    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Completar despacho' })).toBeTruthy())
    await fireEvent.press(screen.getByRole('button', { name: 'Completar despacho' }))
    await waitFor(() => expect(getOpenPick()).toBeNull())
    const row = listOutbox()[0]
    expect([row.kind, row.path]).toEqual(['collect', '/api/v1/pick-batches'])
    expect(JSON.parse(row.body)).toEqual({ warehousePublicId: 'wh-1', lines: [{ productPublicId: 'p1', quantity: 3, binId: 55 }] })
  })

  it('una posición que no existe no completa nada y lo dice', async () => {
    await setupDevice()
    const id = startLocalPick('wh-1', null)
    addLocalPickLine(id, { productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', quantity: 1, fromBinCode: 'NO-EXISTE' })
    mockFetch([(c) => (c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/bins' ? json(200, { total: 0, skip: 0, take: 200, items: [] }) : null)])
    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Completar despacho' })).toBeTruthy())
    await fireEvent.press(screen.getByRole('button', { name: 'Completar despacho' }))
    await waitFor(() => expect(screen.getByText(/No hay una posición con ese código\. NO-EXISTE/)).toBeTruthy())
    expect(getOpenPick()).not.toBeNull()
    expect(listOutbox()).toEqual([])
  })
})
