// Lote A4 — "Otra posición" en el conteo por producto (producto con lote): se elige la zona (una sola: ya elegida), se escribe
// la posición y el lote, se crea en el servidor como provisional ("pendiente de revisión") y entra a la lista como una fila más;
// al confirmar viaja como línea nueva (binId + productPublicId + lote). En archivo propio: renderRouter() no aísla del todo su
// estado global de navegación entre dos llamadas del mismo archivo.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenCount, startLocalProductCount } from '../features/count/localCount'
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

const PRODUCT = { publicId: 'p-lot', sku: 'LOT-1', name: 'Pintura', trackingTypeCode: 'LOT' as const }

describe('Conteo por producto — "Otra posición"', () => {
  it('crea la posición provisional, la agrega a la lista y la manda como línea nueva con su lote', async () => {
    await setupDevice()
    startLocalProductCount('wh-1', PRODUCT, { countId: 300, isBlind: true }, [
      { lineId: 1, productPublicId: 'p-lot', sku: 'LOT-1', productName: 'Pintura', systemQty: null, binId: 10, binCode: 'A-01', lotId: 3, lotNumber: 'L-3', binIsProvisional: false },
    ])
    const calls = mockFetch([
      (c) => (c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/zones' ? json(200, [{ id: 4, code: 'PCK', name: 'Picking', isActive: true }]) : null),
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts/300/bins'
          ? json(200, { id: 99, code: 'Z-09', zoneId: 4, zoneCode: 'PCK', isProvisional: true, provisionalCycleCountId: 300 })
          : null,
    ])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('A-01')).toBeTruthy())
    await fireEvent.press(screen.getByRole('button', { name: 'Otra posición' }))

    await waitFor(() => expect(screen.getByRole('radio', { name: 'PCK · Picking' })).toBeTruthy())
    // una sola zona: ya elegida
    expect(screen.getByRole('radio', { name: 'PCK · Picking' }).props.accessibilityState).toMatchObject({ selected: true })
    await fireEvent.changeText(screen.getByLabelText('Código de la posición'), 'z-09')
    // producto con lote: sin el número de lote no se puede agregar
    expect(screen.getByText('Este producto lleva lote: escribe el número de lote.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Agregar posición' }).props.accessibilityState).toMatchObject({ disabled: true })
    await fireEvent.changeText(screen.getByLabelText('Número de lote'), 'L-9')
    await fireEvent.changeText(screen.getByLabelText('Vencimiento del lote (opcional)'), '13/45/2027')
    expect(screen.getByText('La fecha no es válida. Escríbela así: MM/DD/AAAA')).toBeTruthy()
    await fireEvent.changeText(screen.getByLabelText('Vencimiento del lote (opcional)'), '01/31/2027')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar posición' }))

    await waitFor(() => expect(screen.getByText('Posición Z-09 agregada (pendiente de revisión).')).toBeTruthy())
    expect(calls.find((c) => c.path === '/api/v1/cycle-counts/300/bins')?.body).toEqual({ zoneId: 4, code: 'z-09' })
    expect(screen.getByText('Z-09')).toBeTruthy()
    expect(screen.getByText('Lote L-9 · Pendiente de revisión')).toBeTruthy()

    await fireEvent.changeText(screen.getByLabelText('Cantidad en Z-09, lote L-9'), '6')
    expect(screen.getByText('1 posición en blanco se toma como 0.')).toBeTruthy()
    await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(JSON.parse(listOutbox()[0].body)).toEqual({
      lines: [
        { lineId: 1, countedQty: 0 },
        { binId: 99, productPublicId: 'p-lot', countedQty: 6, lot: { number: 'L-9', expiryDate: '2027-01-31' } },
      ],
    })
  })
})
