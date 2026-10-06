// Tarea 25 — conteo informado al capturar en "Contar por producto": al Confirmar, un contador (conteo a ciegas) verifica cada posición del servidor contra lo
// esperado; el resultado sale debajo de cada fila y no se confirma en ese toque (así se alcanza a leer lo que pide recontar). Las que piden recontar se
// corrigen y se verifican de nuevo; las cerradas ya no se editan; con todo cerrado, el toque siguiente confirma. En archivo propio: renderRouter() no aísla
// del todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { forgetChecks } from '../features/count/lineCheck'
import { getOpenCount, startLocalProductCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { listOutbox } from '../kernel/sync/outbox'
import { json, mockFetch, setupDevice, type FetchCall } from './countKit'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  forgetChecks()
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

const LINE = { productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', binIsProvisional: false, lotId: null, lotNumber: null, systemQty: null }

describe('Conteo por producto — informado al capturar', () => {
  it('verifica cada posición al confirmar, pide recontar sin decir el esperado y confirma en el toque siguiente', async () => {
    await setupDevice()
    startLocalProductCount('wh-1', { publicId: 'p1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' }, { countId: 300, isBlind: true }, [
      { ...LINE, lineId: 1, binId: 10, binCode: 'A-01' },
      { ...LINE, lineId: 2, binId: 11, binCode: 'B-02' },
    ])
    const state: Record<number, number> = {}
    const calls = mockFetch([
      (c: FetchCall) => {
        const m = /\/cycle-counts\/300\/lines\/(\d+)\/check$/.exec(c.path)
        if (c.method !== 'POST' || !m) return null
        const id = Number(m[1])
        state[id] = (state[id] ?? 0) + 1
        const counted = (c.body as { countedQty: number }).countedQty
        if (id === 1) return json(200, { lineId: 1, state: 'MATCH', matches: true, countedQty: counted, expectedQty: 4 })
        return state[id] === 1
          ? json(200, { lineId: 2, state: 'RECOUNT', matches: false, countedQty: counted })
          : json(200, { lineId: 2, state: 'FINAL', matches: false, countedQty: counted, expectedQty: 6 })
      },
    ])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Tornillo')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad en A-01'), '4')
    await fireEvent.changeText(screen.getByLabelText('Cantidad en B-02'), '5')

    // 1.er toque: verifica; no confirma todavía
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(screen.getByText('Coincide: contaste 4 y se esperaba 4.')).toBeTruthy())
    expect(screen.getByText('No coincide con lo esperado. Vuelve a contar y acepta de nuevo.')).toBeTruthy()
    expect(screen.queryByText(/se esperaba 6/)).toBeNull()
    expect(getOpenCount()).not.toBeNull()
    expect(listOutbox()).toHaveLength(0)
    // la posición cerrada ya no se edita; la que pide recontar sí
    expect(screen.getByLabelText('Cantidad en A-01').props.editable).toBe(false)
    expect(screen.getByLabelText('Cantidad en B-02').props.editable).not.toBe(false)

    // recuenta B-02 (la nota de esa fila se quita al escribir) y vuelve a confirmar: solo se verifica la que faltaba
    await fireEvent.changeText(screen.getByLabelText('Cantidad en B-02'), '6')
    expect(screen.queryByText('No coincide con lo esperado. Vuelve a contar y acepta de nuevo.')).toBeNull()
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(screen.getByText('Contaste 6 y se esperaba 6. Queda para revisión.')).toBeTruthy())
    expect(calls.filter((c) => c.path.endsWith('/lines/1/check'))).toHaveLength(1)
    expect(calls.filter((c) => c.path.endsWith('/lines/2/check'))).toHaveLength(2)
    expect(getOpenCount()).not.toBeNull()

    // todo cerrado: el toque siguiente confirma y encola el lote y el cierre
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(listOutbox().map((r) => r.kind)).toEqual(['countBatch', 'countFinish'])
  })

  it('un 403 deja confirmar como siempre, sin verificar', async () => {
    await setupDevice()
    startLocalProductCount('wh-1', { publicId: 'p1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' }, { countId: 300, isBlind: true }, [
      { ...LINE, lineId: 1, binId: 10, binCode: 'A-01' },
    ])
    mockFetch([(c: FetchCall) => (c.method === 'POST' && c.path.endsWith('/check') ? json(403, { title: 'No está habilitado ver lo esperado al contar.', status: 403 }) : null)])
    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Tornillo')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad en A-01'), '4')
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(listOutbox().map((r) => r.kind)).toEqual(['countBatch', 'countFinish'])
  })
})
