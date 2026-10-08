// El tile «Daño» de Inicio solo se ofrece a quien tiene el permiso warehouse.damage (2026-10-08). En archivo propio por el estado global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { getKv, KvKeys, setKv } from '../kernel/db/kv'
import { __resetDbForTests } from '../kernel/db/database'
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

describe('Inicio — tile de Daño', () => {
  it('con el permiso aparece y lleva a la pantalla de Daño', async () => {
    await setupDevice()
    setKv(KvKeys.myPermissions, JSON.stringify({ '7': { permissions: ['warehouse.damage'], fetchedAtUtc: '2026-10-08T00:00:00Z' } }))
    mockFetch([(c) => (c.path === '/api/v1/me' ? json(200, { permissions: ['warehouse.damage'] }) : null)])
    await renderRouter('src/app', { initialUrl: '/home' })
    await waitFor(() => expect(screen.getByTestId('home-damage')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('home-damage'))
    await waitFor(() => expect(screen.getByText('¿Dónde se detectó el daño?')).toBeTruthy())
    expect(getKv(KvKeys.myPermissions)).toContain('warehouse.damage')
  })

  it('sin el permiso no se ofrece', async () => {
    await setupDevice()
    mockFetch([(c) => (c.path === '/api/v1/me' ? json(200, { permissions: ['inventory.view'] }) : null)])
    await renderRouter('src/app', { initialUrl: '/home' })
    await waitFor(() => expect(screen.getByText('Consultar')).toBeTruthy())
    expect(screen.queryByTestId('home-damage')).toBeNull()
  })
})
