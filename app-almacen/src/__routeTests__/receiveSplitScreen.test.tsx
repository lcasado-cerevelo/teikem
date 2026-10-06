// Recibo directo con reparto por posición (tarea 24c): con "Cantidad por posición" cada escaneo suma una posición; "Confirmar reparto" agrega
// una línea por posición y lo que no cupo en posiciones llenas queda en la captura para ubicarlo en una posición aparte.
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenReceipt } from '../features/receive/localLookup'
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  setApiBaseUrl('http://api.test')
  // solo responde la consulta de posiciones (cupo de RSV-A-01: 25 con 10 en existencia → 15 libres); lo demás, sin señal
  jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = new URL((input as Request).url)
    if (url.pathname === '/api/v1/warehouses/wh-1/bins') {
      const code = url.searchParams.get('search') ?? ''
      return new Response(JSON.stringify({ total: 1, skip: 0, take: 200, items: [{ id: 1, code, ...(code === 'RSV-A-01' ? { maxCapacityQty: 25, qtyOnHand: 10 } : {}) }] }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }
    throw new TypeError('Network request failed')
  })
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

const scan = (label: string, code: string) => fireEvent(screen.getByLabelText(label), 'submitEditing', { nativeEvent: { text: code } })

describe('Recibir directo — reparto por posición', () => {
  it('reparte 45 de 20 en 20: la tercera posición recibe los 5 que quedaban con alerta fija, y una cuarta no cabe', async () => {
    await saveDeviceIdentity({
      devicePublicId: 'dev-1',
      deviceSecret: 'secret-1',
      tenantName: 'Teikem Demo',
      defaultWarehousePublicId: 'wh-1',
      theme: null,
      defaultWarehouseReceivingMode: 'DIRECT',
    })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    const db = getDb()
    db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
    for (const [i, code] of ['RSV-A-01', 'RSV-A-02', 'RSV-A-03', 'RSV-A-04'].entries())
      db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (?, ?, 'wh-1', 'RESERVE', 1)", [i + 1, code])

    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByText('Recibo ciego')).toBeTruthy())
    await fireEvent.press(screen.getByText('Recibo ciego'))
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await scan('Escanea el producto', '7501')
    await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '45')
    await fireEvent.press(screen.getByText('Siguiente'))

    await waitFor(() => expect(screen.getByLabelText('Cantidad por posición (opcional)')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad por posición (opcional)'), '20')
    await scan('Escanea la siguiente posición destino', 'RSV-A-01')
    await waitFor(() => expect(screen.getByText('RSV-A-01 · 20')).toBeTruthy())
    await scan('Escanea la siguiente posición destino', 'RSV-A-02')
    await waitFor(() => expect(screen.getByText('RSV-A-02 · 20')).toBeTruthy())
    expect(screen.getByText('Repartido: 40 · quedan 5 sin ubicar')).toBeTruthy()

    expect(screen.queryByTestId('sticky-alert')).toBeNull()
    // el cupo de la primera (15 libres) no alcanza para 20: avisa sin bloquear
    await waitFor(() => expect(screen.getByText('Cupo para 15: recibirá 20. Se puede confirmar igual.')).toBeTruthy())
    expect(screen.getAllByText(/^Cupo para/)).toHaveLength(1)

    // la tercera recibe lo que quedaba (5) con la alerta fija, cerrable
    await scan('Escanea la siguiente posición destino', 'RSV-A-03')
    await waitFor(() => expect(screen.getByText('RSV-A-03 · 5')).toBeTruthy())
    expect(screen.getByText('RSV-A-03 recibe solo 5 (lo que quedaba), no 20.')).toBeTruthy()
    await fireEvent.press(screen.getByLabelText('Cerrar aviso'))
    await waitFor(() => expect(screen.queryByTestId('sticky-alert')).toBeNull())

    // una cuarta ya no cabe
    await scan('Escanea la siguiente posición destino', 'RSV-A-04')
    await waitFor(() => expect(screen.getByText(/Ya no hay unidades por ubicar/)).toBeTruthy())
    expect(getOpenReceipt()?.lines).toHaveLength(0) // nada se agrega hasta confirmar

    // cada posición se puede quitar: sin A-02 el resto pasa a A-03 con 20 y vuelve a caber una más
    await fireEvent.press(screen.getByLabelText('Quitar RSV-A-02'))
    await waitFor(() => expect(screen.queryByLabelText('Quitar RSV-A-02')).toBeNull())
    expect(screen.getByText('RSV-A-03 · 20')).toBeTruthy()
    jest.spyOn(Date, 'now').mockReturnValue(Date.now() + 5000)
    await scan('Escanea la siguiente posición destino', 'RSV-A-02')
    await waitFor(() => expect(screen.getByText('RSV-A-02 · 5')).toBeTruthy())

    await fireEvent.press(screen.getByText('Confirmar reparto'))
    await waitFor(() => expect(getOpenReceipt()?.lines).toHaveLength(3))
    expect(getOpenReceipt()?.lines.map((l) => [l.receivedQty, l.targetBinCode])).toEqual([
      [20, 'RSV-A-01'],
      [20, 'RSV-A-03'],
      [5, 'RSV-A-02'],
    ])
  })
})
