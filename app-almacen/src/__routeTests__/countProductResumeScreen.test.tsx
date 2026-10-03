// Lote A4 — un conteo por producto en curso se retoma al volver a abrir la app SIN señal (sus filas y lo ya escrito están en la
// base local); con más de 6 posiciones aparece el buscador; a ciegas no se muestra lo esperado; se puede cancelar (con señal).
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo.
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import type { ProductCountLine } from '../features/count/countLogic'
import { getOpenCount, getProductCountRows, setProductRowQty, startLocalProductCount } from '../features/count/localCount'
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

const PRODUCT = { publicId: 'p1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' as const }
const CODES = ['A-01', 'A-02', 'B-01', 'B-02', 'C-01', 'C-02', 'D-01']
const LINES: ProductCountLine[] = CODES.map((code, i) => ({
  lineId: i + 1,
  productPublicId: 'p1',
  sku: 'SKU-1',
  productName: 'Tornillo',
  systemQty: null,
  binId: 100 + i,
  binCode: code,
  lotId: null,
  lotNumber: null,
  binIsProvisional: false,
}))

describe('Conteo por producto — retomar, buscador y cancelar', () => {
  it('retoma sin señal con lo ya escrito; buscador con 7 posiciones; a ciegas; cancelar libera el aparato', async () => {
    await setupDevice()
    const id = startLocalProductCount('wh-1', PRODUCT, { countId: 300, isBlind: true }, LINES)
    setProductRowQty(getProductCountRows(id)[1].id, 8)
    const calls = mockFetch([(c) => (c.method === 'DELETE' && c.path === '/api/v1/cycle-counts/300' ? new Response(null, { status: 204 }) : null)])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Tornillo')).toBeTruthy())
    // nada se pidió al servidor para retomar
    expect(calls).toEqual([])
    expect(screen.getByLabelText('Cantidad en A-02').props.value).toBe('8')
    expect(screen.getByText('6 posiciones en blanco se toman como 0.')).toBeTruthy()
    // a ciegas: no hay "Esperado"
    expect(screen.getByText('Conteo a ciegas: no se muestran las cantidades del sistema.')).toBeTruthy()
    expect(screen.queryByText(/Esperado:/)).toBeNull()

    // más de 6 posiciones: buscador
    await fireEvent.changeText(screen.getByLabelText('Buscar posición o lote'), 'b-0')
    expect(screen.getByText('B-01')).toBeTruthy()
    expect(screen.getByText('B-02')).toBeTruthy()
    expect(screen.queryByText('A-01')).toBeNull()
    await fireEvent.changeText(screen.getByLabelText('Buscar posición o lote'), 'zz')
    expect(screen.getByText('Ninguna posición coincide con «zz».')).toBeTruthy()
    // el resumen sigue siendo de todas las posiciones, no solo de las filtradas
    expect(screen.getByText('6 posiciones en blanco se toman como 0.')).toBeTruthy()

    jest.spyOn(Alert, 'alert').mockImplementation((_title, _body, buttons) => {
      void buttons?.find((b) => b.style === 'destructive')?.onPress?.()
    })
    await fireEvent.press(screen.getByRole('button', { name: 'Cancelar conteo' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    expect(calls.map((c) => `${c.method} ${c.path}`)).toContain('DELETE /api/v1/cycle-counts/300')
  })
})
