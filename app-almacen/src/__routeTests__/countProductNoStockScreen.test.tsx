// Lote A4 (adenda 5d) — producto SIN existencia: la app abre el conteo con allowEmpty, el servidor lo crea vacío y la lista
// muestra el bloque "El sistema no tiene existencia…" con «Otra posición» en primer plano. En archivo propio: renderRouter()
// no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount, getProductCountRows, startLocalProductCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
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

const EMPTY_BLOCK = 'El sistema no tiene existencia de este producto. Si lo encontraste en alguna posición, usa «Otra posición».'

async function openEmptyCount() {
  await setupDevice()
  startLocalProductCount('wh-1', { publicId: 'p1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' }, { countId: 400, isBlind: true }, [])
  const calls = mockFetch([
    (c) => (c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/zones' ? json(200, [{ id: 4, code: 'PCK', name: 'Picking', isActive: true }]) : null),
    (c) =>
      c.method === 'POST' && c.path === '/api/v1/cycle-counts/400/bins'
        ? json(200, { id: 99, code: 'Z-09', zoneId: 4, zoneCode: 'PCK', isProvisional: true, provisionalCycleCountId: 400 })
        : null,
    (c) => (c.method === 'DELETE' && c.path === '/api/v1/cycle-counts/400' ? new Response(null, { status: 204 }) : null),
  ])
  await renderRouter('src/app', { initialUrl: '/count' })
  await waitFor(() => expect(screen.getByText(EMPTY_BLOCK)).toBeTruthy())
  return calls
}

describe('Conteo por producto — sin existencia', () => {
  it('un conteo vacío retomado avisa que no se puede terminar vacío y deja agregar «Otra posición»', async () => {
    await openEmptyCount()
    const open = getOpenCount()
    expect(open?.countId).toBe(400)
    expect(getProductCountRows(open!.id)).toEqual([])
    // «Otra posición» en primer plano (botón grande), no el enlace
    expect(screen.getByTestId('count-empty-block')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Otra posición' })).toBeTruthy()

    // confirmar sin ninguna fila: aviso y nada viaja
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    expect(
      screen.getByText('No se puede terminar un conteo vacío: agrega la posición donde lo encontraste con «Otra posición» o cancela el conteo.'),
    ).toBeTruthy()
    expect(listOutbox()).toEqual([])
    expect(getOpenCount()).not.toBeNull()

    // lo hallado donde el sistema no tenía nada: posición provisional, cantidad y confirmar
    await fireEvent.press(screen.getByRole('button', { name: 'Otra posición' }))
    await waitFor(() => expect(screen.getByRole('radio', { name: 'PCK · Picking' })).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Código de la posición'), 'z-09')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar posición' }))
    await waitFor(() => expect(screen.getByText('Posición Z-09 agregada (pendiente de revisión).')).toBeTruthy())
    // ya hay una fila: el bloque vacío desaparece y queda el enlace normal
    expect(screen.queryByTestId('count-empty-block')).toBeNull()
    await fireEvent.changeText(screen.getByLabelText('Cantidad en Z-09'), '3')
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(JSON.parse(listOutbox()[0].body)).toEqual({ lines: [{ binId: 99, productPublicId: 'p1', countedQty: 3 }] })
  })
})
