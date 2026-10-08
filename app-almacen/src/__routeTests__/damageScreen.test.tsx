// Daño en la app (2026-10-08): del almacén (posición → producto → cantidad → causa → cuarentena o desechar) y de un recibo (número del recibo). En archivo
// propio: renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
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

const BINS = (c: { method: string; path: string }) =>
  c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/bins' ? json(200, { total: 1, skip: 0, take: 200, items: [{ id: 41, code: 'A-01' }] }) : null
const RECEIPTS = (c: { method: string; path: string }) =>
  c.method === 'GET' && c.path === '/api/v1/receipts' ? json(200, { total: 1, skip: 0, take: 20, items: [{ id: 5, publicId: 'rec-uuid', number: 'REC-00005' }] }) : null
const REPORT = (c: { method: string; path: string }) =>
  c.method === 'POST' && c.path === '/api/v1/damage-reports' ? json(200, { id: 1, code: 'DAN-00001' }) : null

async function open() {
  await renderRouter('src/app', { initialUrl: '/damage' })
  await waitFor(() => expect(screen.getByText('¿Dónde se detectó el daño?')).toBeTruthy())
}

async function scan(label: string, text: string) {
  await waitFor(() => expect(screen.getByLabelText(label)).toBeTruthy())
  await fireEvent(screen.getByLabelText(label), 'submitEditing', { nativeEvent: { text } })
}

describe('Daño', () => {
  it('del almacén: posición, producto, cantidad y «Mandar a cuarentena» manda el cuerpo correcto', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    const calls = mockFetch([BINS, REPORT])
    await open()
    await fireEvent.press(screen.getByTestId('damage-origin-warehouse'))
    await scan('Escanea la posición donde está', 'a-01')
    await waitFor(() => expect(screen.getByText('Posición A-01')).toBeTruthy())
    await scan('Escanea el producto dañado', '7501')
    await waitFor(() => expect(screen.getByLabelText('Cantidad dañada')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad dañada'), '4')
    await fireEvent.press(screen.getByText('Accidente en el camino'))
    await fireEvent.press(screen.getByTestId('damage-quarantine'))

    await waitFor(() => expect(screen.getByText('DAN-00001: enviado a cuarentena.')).toBeTruthy())
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      origin: 'WAREHOUSE',
      warehousePublicId: 'wh-1',
      productPublicId: 'p1',
      fromBinId: 41,
      quantity: 4,
      cause: 'TRANSIT_ACCIDENT',
      disposition: 'QUARANTINE',
    })
  })

  it('una posición que no existe avisa y no avanza', async () => {
    await setupDevice()
    mockFetch([(c) => (c.path === '/api/v1/warehouses/wh-1/bins' ? json(200, { total: 0, skip: 0, take: 200, items: [] }) : null)])
    await open()
    await fireEvent.press(screen.getByTestId('damage-origin-warehouse'))
    await scan('Escanea la posición donde está', 'ZZ-99')
    await waitFor(() => expect(screen.getByText('No encontré esa posición en este almacén.')).toBeTruthy())
  })

  it('desechar de una vez pide confirmar; si llegó dañado en un recibo, manda el recibo', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    const alert = jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => {
      buttons?.find((b) => b.style === 'destructive')?.onPress?.()
    })
    const calls = mockFetch([RECEIPTS, REPORT])
    await open()
    await fireEvent.press(screen.getByTestId('damage-origin-receipt'))
    await scan('Número del recibo', 'rec-00005')
    await waitFor(() => expect(screen.getByText('Recibo REC-00005')).toBeTruthy())
    await scan('Escanea el producto dañado', '7501')
    await waitFor(() => expect(screen.getByLabelText('Cantidad dañada')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad dañada'), '2,5')
    await fireEvent.press(screen.getByTestId('damage-discard'))

    await waitFor(() => expect(screen.getByText('DAN-00001: desechado.')).toBeTruthy())
    expect(alert).toHaveBeenCalledWith('¿Desechar de una vez?', 'Se desechan 2,5 de SKU-1. No se puede deshacer.', expect.any(Array))
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      origin: 'RECEIPT',
      receiptPublicId: 'rec-uuid',
      quantity: 2.5,
      cause: 'ARRIVED_DAMAGED',
      disposition: 'DISCARD',
    })
  })

  it('una cantidad inválida o un producto por lote sin lote no llaman al servidor', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    insertProduct(2, 'p2', 'LOT-1', 'Cable', '7502', 'LOT')
    const calls = mockFetch([BINS, REPORT])
    await open()
    await fireEvent.press(screen.getByTestId('damage-origin-warehouse'))
    await scan('Escanea la posición donde está', 'A-01')
    await scan('Escanea el producto dañado', '7502')
    await waitFor(() => expect(screen.getByLabelText('Cantidad dañada')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('damage-quarantine'))
    await waitFor(() => expect(screen.getByText('Escribe una cantidad mayor que 0.')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad dañada'), '3')
    await fireEvent.press(screen.getByTestId('damage-quarantine'))
    await waitFor(() => expect(screen.getByText('Escribe el lote.')).toBeTruthy())
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })
})
