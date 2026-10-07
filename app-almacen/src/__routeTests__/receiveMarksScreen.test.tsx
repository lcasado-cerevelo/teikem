// Recibir directo — listado de posiciones marcables (2026-10-07): la sugerida se toca y llena la posición; con una cantidad que no cabe en ella,
// abajo salen las posiciones sugeridas con su espacio libre, se marcan y "Recibir en estas posiciones" agrega una línea por posición.
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenReceipt } from '../features/receive/localLookup'
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'

beforeEach(async () => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  setApiBaseUrl('http://api.test')
  jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = new URL((input as Request).url)
    if (url.pathname === '/api/v1/warehouse-tasks/putaway-suggestions') {
      return new Response(
        JSON.stringify([
          { binId: 1, binCode: 'RSV-A-01', reason: 'Reserva', freeQty: 30 },
          { binId: 2, binCode: 'RSV-A-02', reason: 'Reserva', freeQty: 50 },
          { binId: 3, binCode: 'RSV-A-03', reason: 'Reserva', freeQty: 100 },
        ]),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      )
    }
    throw new TypeError('Network request failed')
  })
  await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'Demo', defaultWarehousePublicId: 'wh-1', theme: null, defaultWarehouseReceivingMode: 'DIRECT' })
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
  const db = getDb()
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
  for (const [i, code] of ['RSV-A-01', 'RSV-A-02', 'RSV-A-03'].entries()) {
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (?, ?, 'wh-1', 'RESERVE', 1)", [i + 1, code])
  }
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

async function toTargetStep(qty: string) {
  await renderRouter('src/app', { initialUrl: '/receive' })
  await waitFor(() => expect(screen.getByText('Recibo ciego')).toBeTruthy())
  await fireEvent.press(screen.getByText('Recibo ciego'))
  await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
  await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: '7501' } })
  await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Cantidad'), qty)
  await fireEvent.press(screen.getByText('Siguiente'))
  await waitFor(() => expect(screen.getByText('Sugerida: RSV-A-01')).toBeTruthy())
}

describe('Recibir directo — listado de posiciones marcables', () => {
  it('60 con 30 libres en la sugerida: avisa, marca las sugeridas y recibe en las posiciones', async () => {
    await toTargetStep('60')
    expect(screen.getByText('No alcanza sola: faltan 30. Marca más posiciones abajo.')).toBeTruthy()
    expect(screen.getByText('Tomado 0 de 60')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('receive-marks-recommended'))
    await waitFor(() => expect(screen.getByText('Tomado 60 de 60')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('receive-marks-use'))
    await waitFor(() =>
      expect(getOpenReceipt()?.lines.map((l) => [l.targetBinCode, l.receivedQty])).toEqual([
        ['RSV-A-01', 30],
        ['RSV-A-02', 30],
      ]),
    )
  })

  it('con una cantidad que cabe, tocar la sugerida pone su posición en el campo y Aceptar agrega la línea', async () => {
    await toTargetStep('20')
    expect(screen.queryByTestId('receive-marks')).toBeNull()
    await fireEvent.press(screen.getByTestId('receive-suggestion'))
    await waitFor(() => expect(screen.getByLabelText('Escanea la posición destino').props.value).toBe('RSV-A-01'))
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(getOpenReceipt()?.lines.map((l) => [l.targetBinCode, l.receivedQty])).toEqual([['RSV-A-01', 20]]))
  })
})
