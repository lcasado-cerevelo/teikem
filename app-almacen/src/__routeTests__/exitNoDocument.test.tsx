// 2026-10-22 — «Despachar sin documento» solo existe cuando la compañía NO exige documento; con la exigencia puesta no sale ni hay otra pantalla de «¿Contra qué despachas?»: /dispatch manda a Despacho (avisos y órdenes). En archivo propio (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { saveDispatchRequirement } from '../features/dispatch/stockExit'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { mockFetch, setupDevice } from './countKit'

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

describe('Despachar sin documento', () => {
  it('con el despacho libre sale el botón', async () => {
    await setupDevice()
    saveDispatchRequirement('FREE')
    mockFetch([])
    await renderRouter('src/app', { initialUrl: '/exit-dispatch' })
    await waitFor(() => expect(screen.getByTestId('exit-no-document')).toBeTruthy())
  })

  it('con la exigencia de documento no sale el botón, y /dispatch lleva a Despacho en vez de a una pantalla de elegir', async () => {
    await setupDevice()
    saveDispatchRequirement('ANY_DOCUMENT')
    mockFetch([])
    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByText('Despachar aviso de salida')).toBeTruthy())
    expect(screen.queryByTestId('exit-no-document')).toBeNull()
    expect(screen.queryByText('¿Contra qué despachas?')).toBeNull()
  })
})
