// Lote 16 — la pantalla real de Recibir en un almacén "Directo a posición": tras la cantidad pide la posición destino
// (con la pista "Sugerida: …" del servidor), rechaza sin señal una posición de recepción, agrega la línea con "→ {bin}"
// y el envío a la cola lleva el modo y la posición. En archivo propio: renderRouter() no aísla del todo su estado global
// de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenReceipt } from '../features/receive/localLookup'
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { listOutbox } from '../kernel/sync/outbox'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  setApiBaseUrl('http://api.test')
  // Solo responde la sugerencia de posición; todo lo demás "sin señal" (el recibo se queda en la cola, pendiente).
  jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const req = input as Request
    const url = new URL(req.url)
    if (url.pathname === '/api/v1/warehouse-tasks/putaway-suggestions') {
      return new Response(JSON.stringify([{ binId: 1, binCode: 'RSV-A-01', reason: 'Reserva con espacio' }]), {
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

describe('Recibir — directo a posición', () => {
  it('pide la posición destino, valida sin señal, muestra la línea con su destino y la manda con el modo', async () => {
    await saveDeviceIdentity({
      devicePublicId: 'dev-1',
      deviceSecret: 'secret-1',
      tenantName: 'Teikem Demo',
      defaultWarehousePublicId: 'wh-1',
      theme: null,
      defaultWarehouseReceivingMode: 'DIRECT',
    })
    await saveUserSession({
      accessToken: 'a',
      accessExpiresAtUtc: '',
      refreshToken: 'r',
      refreshExpiresAtUtc: '',
      tenantId: 1,
      userId: 7,
      fullName: 'Ana Ruiz',
    })
    const db = getDb()
    db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (1, 'RSV-A-01', 'wh-1', 'RESERVE', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (2, 'STG-01', 'wh-1', 'STAGING', 1)")

    // RNTL 14: render es asíncrono; sin esperarlo, las actualizaciones de estado tras un toque no se aplican.
    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByText('Recibo ciego')).toBeTruthy())
    await fireEvent.press(screen.getByText('Recibo ciego'))
    expect(getOpenReceipt()?.receivingMode).toBe('DIRECT')

    // Producto → cantidad → "Siguiente" (no "Agregar": falta la posición).
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: '7501' } })
    await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '1250')
    expect(screen.queryByText('Agregar')).toBeNull()
    await fireEvent.press(screen.getByText('Siguiente'))

    // Paso de destino: cantidad con coma de miles y la pista del servidor.
    await waitFor(() => expect(screen.getByLabelText('Escanea la posición destino')).toBeTruthy())
    expect(screen.getByText('1,250 × SKU-1')).toBeTruthy()
    await waitFor(() => expect(screen.getByText('Sugerida: RSV-A-01')).toBeTruthy())

    // Posición de recepción: rechazada, la línea no se agrega.
    await fireEvent(screen.getByLabelText('Escanea la posición destino'), 'submitEditing', { nativeEvent: { text: 'STG-01' } })
    await waitFor(() =>
      expect(screen.getByText('Esa posición es de recepción o de cruce de muelle; escanea dónde se guarda.')).toBeTruthy(),
    )
    // Una que no existe en el almacén.
    await fireEvent(screen.getByLabelText('Escanea la posición destino'), 'submitEditing', { nativeEvent: { text: 'ZZ-99' } })
    await waitFor(() => expect(screen.getByText('La posición no existe en este almacén.')).toBeTruthy())
    expect(getOpenReceipt()?.lines).toHaveLength(0)

    // Posición de guardado (sin distinguir mayúsculas): se agrega y vuelve a la lista con "→ RSV-A-01".
    await fireEvent(screen.getByLabelText('Escanea la posición destino'), 'submitEditing', { nativeEvent: { text: 'rsv-a-01' } })
    await waitFor(() => expect(screen.getByText('→ RSV-A-01')).toBeTruthy())
    expect(screen.getByText('1,250 × SKU-1')).toBeTruthy()
    expect(screen.getByText('Cierra el recibo y deja cada línea en su posición destino, sin tareas de acomodo.')).toBeTruthy()
    expect(getOpenReceipt()?.lines[0]).toMatchObject({ receivedQty: 1250, targetBinCode: 'RSV-A-01' })

    // Confirmar: a la cola con el modo y la posición destino de la línea.
    await fireEvent.press(screen.getByText('Confirmar recibo'))
    await waitFor(() => expect(listOutbox()).toHaveLength(1))
    const body = JSON.parse(listOutbox()[0].body)
    expect(body).toMatchObject({ warehousePublicId: 'wh-1', confirm: true, receivingMode: 'DIRECT' })
    expect(body.lines).toEqual([
      { productPublicId: 'p1', receivedQty: 1250, lot: null, serialNumbers: null, targetBinCode: 'RSV-A-01', damagedQty: null, damageCause: null, damageNote: null, damageBinCode: null, damageDiscard: null },
    ])
  })
})
