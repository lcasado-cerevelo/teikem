// Inicio con «Transferir» (2026-10-10): solo con el permiso warehouse.transfer y en el orden pedido por el dueño:
// Consultar, Transferir, Recibir, Acomodar, Despacho, Conteo y Daño. En archivo propio por el estado global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { getKv, KvKeys, setKv } from '../kernel/db/kv'
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

describe('Inicio — Transferir', () => {
  it('con el permiso aparece segundo (tras Consultar), el orden es el pedido y lleva a Transferir', async () => {
    await setupDevice()
    const perms = ['warehouse.transfer', 'warehouse.damage']
    setKv(KvKeys.myPermissions, JSON.stringify({ '7': { permissions: perms, fetchedAtUtc: '2026-10-10T00:00:00Z' } }))
    mockFetch([(c) => (c.path === '/api/v1/me' ? json(200, { permissions: perms }) : null)])
    await renderRouter('src/app', { initialUrl: '/home' })
    await waitFor(() => expect(screen.getByTestId('home-transfer')).toBeTruthy())
    const labels = ['Consultar', 'Transferir', 'Recibir', 'Acomodar', 'Despacho', 'Conteo', 'Daño']
    // el orden en la pantalla = el orden de aparición de los rótulos en el árbol
    const tree = JSON.stringify(screen.toJSON())
    const grid = tree.slice(tree.indexOf('home-grid'), tree.indexOf('Sincronizar ahora'))
    const positions = labels.map((l) => grid.indexOf(`"${l}"`))
    expect(positions.every((p) => p >= 0)).toBe(true)
    expect([...positions].sort((x, y) => x - y)).toEqual(positions)
    await fireEvent.press(screen.getByTestId('home-transfer'))
    await waitFor(() => expect(screen.getByText('Escanea la posición de origen')).toBeTruthy())
    expect(getKv(KvKeys.myPermissions)).toContain('warehouse.transfer')
  })
})
