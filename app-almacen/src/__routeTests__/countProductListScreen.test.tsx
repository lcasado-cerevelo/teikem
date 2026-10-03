// Lote A4 — "Contar por producto" de punta a punta en la pantalla real: escanear el producto abre el conteo en línea (sin
// posiciones), la lista muestra una fila por posición y lote (con lo esperado porque el servidor lo trajo), se escribe en un
// espacio, la línea de resumen avisa cuántos en blanco se toman como 0 y Confirmar (un solo toque) encola el lote con TODAS
// las líneas (blancos = 0) y el cierre. En archivo propio: renderRouter() no aísla del todo su estado global de navegación
// entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { setKv, KvKeys } from '../kernel/db/kv'
import { listOutbox } from '../kernel/sync/outbox'
import { insertProduct, json, mockFetch, setupDevice } from './countKit'

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

const LINE = { productPublicId: 'p-lot', sku: 'LOT-1', productName: 'Pintura', trackingTypeCode: 'LOT' }

describe('Conteo por producto — lista, captura y confirmar', () => {
  it('una fila por posición y lote; en blanco = 0 con resumen; Confirmar encola lote y cierre', async () => {
    await setupDevice()
    insertProduct(1, 'p-lot', 'LOT-1', 'Pintura', '7601', 'LOT')
    setKv(KvKeys.countEntryMode, 'PRODUCT')
    const calls = mockFetch([
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts'
          ? json(200, {
              count: { id: 300, originCode: 'PRODUCT' },
              isBlind: false,
              lines: [
                { ...LINE, id: 1, binId: 10, binCode: 'A-01', lotId: 3, lotNumber: 'L-3', systemQty: 4 },
                { ...LINE, id: 2, binId: 11, binCode: 'B-02', lotId: 4, lotNumber: 'L-4', systemQty: 1250 },
                { ...LINE, id: 3, binId: 12, binCode: 'C-03', lotId: 3, lotNumber: 'L-3', systemQty: 2 },
              ],
            })
          : null,
    ])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Escanea el producto a contar')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Escanea el producto a contar'), '7601')
    await fireEvent.press(screen.getByLabelText('Aceptar'))

    await waitFor(() => expect(screen.getByText('Pintura')).toBeTruthy())
    expect(calls.find((c) => c.path === '/api/v1/cycle-counts')?.body).toEqual({ warehousePublicId: 'wh-1', productPublicIds: ['p-lot'], allowEmpty: true })
    expect(getOpenCount()).toMatchObject({ countId: 300, mode: 'PRODUCT', binId: null })
    expect(screen.getByText('Este producto lleva lote: cada fila es una posición y un lote.')).toBeTruthy()
    // una fila por posición y lote, con el lote y lo esperado (con los separadores de la compañía)
    expect(screen.getByText('A-01')).toBeTruthy()
    expect(screen.getByText('Lote L-3 · Esperado: 4')).toBeTruthy()
    expect(screen.getByText('Lote L-4 · Esperado: 1,250')).toBeTruthy()
    expect(screen.getByText('C-03')).toBeTruthy()
    // 3 posiciones: sin buscador; sin "todo aquí"
    expect(screen.queryByLabelText('Buscar posición o lote')).toBeNull()
    expect(screen.queryByText(/todo aquí/i)).toBeNull()

    // todo en blanco: el resumen lo dice (con todo en blanco Confirmar avisa y no termina: countProductAllBlankScreen)
    expect(screen.getByText('3 posiciones en blanco se toman como 0.')).toBeTruthy()
    await fireEvent.changeText(screen.getByLabelText('Cantidad en A-01, lote L-3'), '4')
    expect(screen.getByText('2 posiciones en blanco se toman como 0.')).toBeTruthy()

    // algo que no es una cantidad no deja confirmar
    await fireEvent.changeText(screen.getByLabelText('Cantidad en B-02, lote L-4'), 'x')
    expect(screen.getByText('Hay cantidades que no son un número; corrígelas para confirmar.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Confirmar' }).props.accessibilityState).toMatchObject({ disabled: true })
    await fireEvent.changeText(screen.getByLabelText('Cantidad en B-02, lote L-4'), '')
    expect(screen.getByText('2 posiciones en blanco se toman como 0.')).toBeTruthy()

    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    const outbox = listOutbox()
    expect(outbox.map((r) => [r.kind, r.path])).toEqual([
      ['countBatch', '/api/v1/cycle-counts/300/lines/batch'],
      ['countFinish', '/api/v1/cycle-counts/300/finish'],
    ])
    expect(JSON.parse(outbox[0].body)).toEqual({
      lines: [
        { lineId: 1, countedQty: 4 },
        { lineId: 2, countedQty: 0 },
        { lineId: 3, countedQty: 0 },
      ],
    })
  })
})
