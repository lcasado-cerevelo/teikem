// Lote A7 — Sincronización con un lote de conteo PARCIAL (200 con skippedLines: el servidor guardó las libres y omitió la que el
// supervisor ya había corregido). De punta a punta con la base SQL real y un `fetch` falso: la pasada deja el envío como enviado, el
// aviso aparece en Sincronización, sobrevive a "cerrar la app" (se vuelve a abrir la pantalla) y se quita con Descartar. En archivo
// propio por la misma razón que syncCorrectedLineScreen.test.tsx.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { enqueueFinishCount } from '../features/count/countApi'
import { listSkippedNotices } from '../features/count/countSkipped'
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

describe('Sincronización — lote de conteo parcial', () => {
  it('muestra el aviso persistente con lo no guardado, deja la fila enviada y se descarta', async () => {
    await setupDevice()
    enqueueFinishCount(300, [
      { lineId: 1, productPublicId: 'p1', sku: 'SKU-A', productName: 'Tornillo', countedQty: 4, isExtra: false, binId: 10 },
      { lineId: 2, productPublicId: 'p2', sku: 'SKU-B', productName: 'Tuerca', countedQty: 6, isExtra: false, binId: 10 },
    ])
    mockFetch([
      (c) =>
        c.method === 'PUT' && c.path === '/api/v1/cycle-counts/300/lines/batch'
          ? json(200, {
              count: { id: 300, number: 'CC-0300', statusCode: 'OPEN', isActive: true },
              isBlind: true,
              lines: [],
              skippedLines: [
                { lineId: 2, binCode: 'A-01', sku: 'SKU-B', lotNumber: null, sentQty: 6, currentQty: 5, reasonCode: 'CORRECTED_BY_SUPERVISOR', message: 'x' },
              ],
            })
          : null,
      (c) => (c.method === 'POST' && c.path === '/api/v1/cycle-counts/300/finish' ? json(200, { count: { id: 300, statusCode: 'COUNTED' }, lines: [] }) : null),
    ])

    const result = await runOutbox()
    expect(result).toMatchObject({ sent: 2, rejected: 0 })
    expect(listOutbox().map((r) => [r.kind, r.status])).toEqual([
      ['countBatch', 'sent'],
      ['countFinish', 'sent'],
    ])
    expect(listSkippedNotices()).toHaveLength(1)

    await renderRouter('src/app', { initialUrl: '/sync' })
    await waitFor(() => expect(screen.getByText('Se guardaron las demás líneas de tu conteo.')).toBeTruthy())
    expect(screen.getByText('Estas no se guardaron porque el supervisor ya las corrigió:')).toBeTruthy()
    expect(screen.getByText('• SKU-B · A-01 (mandaste 6 → el supervisor dejó 5)')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Actualizar el conteo' })).toBeTruthy()
    // no es un rechazo: la cola no tiene filas con error
    expect(screen.queryByText('El supervisor ya corrigió todas las líneas de este envío.')).toBeNull()

    await fireEvent.press(screen.getByRole('button', { name: 'Descartar este aviso' }))
    await waitFor(() => expect(screen.queryByText('Se guardaron las demás líneas de tu conteo.')).toBeNull())
    expect(listSkippedNotices()).toEqual([])
  })
})
