// Lote A6 — Sincronización con una captura de conteo rechazada (409: el supervisor ya corrigió una línea). De punta a punta con la
// base SQL real y un `fetch` falso: Terminar encola lote + cierre, la pasada recibe el 409 (y el cierre, 422), la pantalla explica el
// rechazo en grande y "Actualizar el conteo" vuelve a pedir el conteo solo al tocarlo. En archivo propio: renderRouter() no aísla del
// todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { enqueueFinishCount } from '../features/count/countApi'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { listOutbox, runOutbox } from '../kernel/sync/outbox'
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

const LOCKED = 'La línea ya fue corregida por el supervisor; no se puede volver a capturar. Renglón(es) del lote: 2 (SKU-B). No se guardó nada.'

describe('Sincronización — captura de conteo rechazada por línea corregida', () => {
  it('explica el 409, separa el rechazo de los demás y actualiza el conteo al tocar el botón', async () => {
    await setupDevice()
    enqueueFinishCount(300, [
      { lineId: 1, productPublicId: 'p1', sku: 'SKU-A', productName: 'Tornillo', countedQty: 4, isExtra: false, binId: 10 },
      { lineId: 2, productPublicId: 'p2', sku: 'SKU-B', productName: 'Tuerca', countedQty: 6, isExtra: false, binId: 10 },
    ])

    let detailCalls = 0
    const calls = mockFetch([
      (c) => (c.method === 'PUT' && c.path === '/api/v1/cycle-counts/300/lines/batch' ? json(409, { title: LOCKED, status: 409, code: 'conflict' }) : null),
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts/300/finish'
          ? json(422, { title: 'Faltan 1 línea(s) por contar.', status: 422, code: 'status_rule' })
          : null,
      (c) => {
        if (c.method !== 'GET' || c.path !== '/api/v1/cycle-counts/300') return null
        detailCalls += 1
        // primera vez sin señal; la segunda, el conteo vigente (a ciegas: sin systemQty)
        if (detailCalls === 1) return null
        return json(200, {
          count: { id: 300, number: 'CC-0300', statusCode: 'OPEN', isActive: true },
          isBlind: true,
          lines: [
            { id: 1, sku: 'SKU-A', productName: 'Tornillo', binCode: 'A-01', systemQty: null, countedQty: null, wasCorrected: false },
            { id: 2, sku: 'SKU-B', productName: 'Tuerca', binCode: 'A-01', systemQty: null, countedQty: 5, wasCorrected: true, correctedByName: 'Beto Supervisor' },
          ],
        })
      },
    ])

    const result = await runOutbox()
    expect(result.rejected).toBe(2)
    expect(listOutbox().map((r) => [r.kind, r.status])).toEqual([
      ['countBatch', 'rejected'],
      ['countFinish', 'rejected'],
    ])

    await renderRouter('src/app', { initialUrl: '/sync' })
    await waitFor(() => expect(screen.getByText('Con error (2)')).toBeTruthy())

    // explicación grande del 409 con el renglón, lo que mandó y qué hacer
    expect(screen.getByText('El supervisor ya corrigió una línea de este conteo.')).toBeTruthy()
    expect(screen.getByText('No se guardó nada de este envío: ninguna de las cantidades que mandaste quedó en el conteo.')).toBeTruthy()
    expect(screen.getByText('• Renglón 2: SKU-B (mandaste 6)')).toBeTruthy()
    expect(
      screen.getByText('Vuelve a abrir el conteo y captura de nuevo solo las líneas que el supervisor no corrigió (o pide al supervisor que lo revise).'),
    ).toBeTruthy()
    expect(screen.getByText('El cierre de este mismo conteo también quedó con error: el conteo no se terminó desde este aparato.')).toBeTruthy()
    // el cierre sigue en la lista de siempre con su mensaje; el 409 no se repite ahí
    expect(screen.getByText('Faltan 1 línea(s) por contar.')).toBeTruthy()
    expect(screen.queryByText(LOCKED)).toBeNull()

    // nada se pidió solo
    expect(calls.some((c) => c.method === 'GET' && c.path === '/api/v1/cycle-counts/300')).toBe(false)

    // sin señal
    await fireEvent.press(screen.getByRole('button', { name: 'Actualizar el conteo' }))
    await waitFor(() => expect(screen.getByText('Sin señal: no se pudo actualizar el conteo. Inténtalo de nuevo cuando haya conexión.')).toBeTruthy())

    // con señal: estado vigente, sin cantidades esperadas
    await fireEvent.press(screen.getByRole('button', { name: 'Actualizar el conteo' }))
    await waitFor(() => expect(screen.getByText('El conteo CC-0300 sigue abierto (Pendiente).')).toBeTruthy())
    expect(screen.getByText('2 líneas: 1 corregidas por el supervisor, 1 sin contar.')).toBeTruthy()
    expect(screen.getByText('Corregida por el supervisor (Beto Supervisor) · Contado: 5')).toBeTruthy()
    expect(screen.getByText('Sin contar')).toBeTruthy()
    expect(screen.queryByText(/Esperado/)).toBeNull()
    expect(detailCalls).toBe(2)

    // descartar el envío rechazado: sale de la cola y de la pantalla; el cierre queda
    await fireEvent.press(screen.getByRole('button', { name: 'Descartar este envío' }))
    await waitFor(() => expect(screen.getByText('Con error (1)')).toBeTruthy())
    expect(screen.queryByText('El supervisor ya corrigió una línea de este conteo.')).toBeNull()
    expect(listOutbox().map((r) => r.kind)).toEqual(['countFinish'])
  })
})
