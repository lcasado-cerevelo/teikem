// Lote A5 — decisión del dueño 4 (docs/decisiones-del-dueno-2026-10-03.md): en "Contar por producto", Confirmar exige al menos
// una posición con un número escrito (0 vale). Con todo en blanco no viaja nada y sale el aviso grande; con un 0 en una sola
// posición se termina y las demás viajan como 0. En archivo propio: renderRouter() no aísla del todo su estado global de
// navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount, startLocalProductCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { listOutbox } from '../kernel/sync/outbox'
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

const ALL_BLANK = 'Escribe al menos una cantidad. Si no hay nada de este producto, escribe 0 en una posición.'
const LINE = { productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', lotId: null, lotNumber: null }

describe('Conteo por producto — Confirmar con todo en blanco', () => {
  it('todo en blanco: no viaja nada y avisa; un 0 en una posición basta y el resto viaja como 0', async () => {
    await setupDevice()
    startLocalProductCount('wh-1', { publicId: 'p1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' }, { countId: 500, isBlind: true }, [
      { ...LINE, lineId: 1, binId: 10, binCode: 'A-01', systemQty: null, binIsProvisional: false },
      { ...LINE, lineId: 2, binId: 11, binCode: 'B-02', systemQty: null, binIsProvisional: false },
      { ...LINE, lineId: 3, binId: 12, binCode: 'C-03', systemQty: null, binIsProvisional: false },
    ])
    mockFetch([])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('A-01')).toBeTruthy())

    // el resumen sigue encima de Confirmar y el botón está encendido (para poder explicar por qué no termina)
    expect(screen.getByText('3 posiciones en blanco se toman como 0.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Confirmar' }).props.accessibilityState?.disabled).toBeFalsy()
    expect(screen.queryByText(ALL_BLANK)).toBeNull()

    // todo en blanco: aviso grande (bloque rojo), nada en la cola y el conteo sigue abierto
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    expect(screen.getByText(ALL_BLANK)).toBeTruthy()
    expect(screen.getByTestId('scan-message-error')).toBeTruthy()
    expect(listOutbox()).toEqual([])
    expect(getOpenCount()).toMatchObject({ countId: 500, mode: 'PRODUCT' })

    // escribir un número (0 vale) quita el aviso; el resumen cuenta las otras dos
    await fireEvent.changeText(screen.getByLabelText('Cantidad en B-02'), '0')
    expect(screen.queryByText(ALL_BLANK)).toBeNull()
    expect(screen.getByText('2 posiciones en blanco se toman como 0.')).toBeTruthy()

    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    const outbox = listOutbox()
    expect(outbox.map((r) => r.kind)).toEqual(['countBatch', 'countFinish'])
    expect(JSON.parse(outbox[0].body)).toEqual({
      lines: [
        { lineId: 1, countedQty: 0 },
        { lineId: 2, countedQty: 0 },
        { lineId: 3, countedQty: 0 },
      ],
    })
  })
})
