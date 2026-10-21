// Inicio con «Ajustar» (2026-10-11): solo con el permiso warehouse.adjust y en el orden pedido por el dueño:
// Mis tareas, Consultar, Recibir, Acomodar, Despacho, Conteo, Ajustar y Daño (orden del dueño, 2026-10-21). «Transferir» ya NO está en el menú (se transfiere con «Mover» en Consultar).
// En archivo propio por el estado global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { KvKeys, setKv } from '../kernel/db/kv'
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

async function openHome(perms: string[]) {
  await setupDevice()
  setKv(KvKeys.myPermissions, JSON.stringify({ '7': { permissions: perms, fetchedAtUtc: '2026-10-11T00:00:00Z' } }))
  mockFetch([(c) => (c.path === '/api/v1/me' ? json(200, { permissions: perms }) : null)])
  await renderRouter('src/app', { initialUrl: '/home' })
  await waitFor(() => expect(screen.getByText('Consultar')).toBeTruthy())
}

describe('Inicio — Ajustar', () => {
  it('con el permiso aparece al final (antes de Daño), el orden es el pedido y lleva a Ajustar con el primer paso: la posición', async () => {
    await openHome(['warehouse.adjust', 'warehouse.damage'])
    await waitFor(() => expect(screen.getByTestId('home-adjust')).toBeTruthy())
    const labels = ['Consultar', 'Recibir', 'Acomodar', 'Despacho', 'Conteo', 'Ajustar', 'Daño']
    // el orden en la pantalla = el orden de aparición de los rótulos en el árbol
    const tree = JSON.stringify(screen.toJSON())
    const grid = tree.slice(tree.indexOf('home-grid'), tree.indexOf('Sincronizar ahora'))
    const positions = labels.map((l) => grid.indexOf(`"${l}"`))
    expect(positions.every((p) => p >= 0)).toBe(true)
    expect([...positions].sort((x, y) => x - y)).toEqual(positions)
    expect(screen.queryByTestId('home-transfer')).toBeNull()
    await fireEvent.press(screen.getByTestId('home-adjust'))
    await waitFor(() => expect(screen.getByText('Escanea la posición donde está')).toBeTruthy())
  })

  it('con solo warehouse.transfer ya no hay botón de Transferir ni de Ajustar en el menú', async () => {
    await openHome(['warehouse.transfer'])
    await waitFor(() => expect(screen.getByText('Recibir')).toBeTruthy())
    expect(screen.queryByTestId('home-transfer')).toBeNull()
    expect(screen.queryByTestId('home-adjust')).toBeNull()
    expect(screen.queryByText('Transferir')).toBeNull()
  })
})
