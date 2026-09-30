// Lote 16 — en un almacén "Con acomodo" Recibir queda igual que antes: "Agregar" agrega la línea sin pedir posición
// destino y el envío lleva el modo PUTAWAY sin posiciones. En archivo propio: renderRouter() no aísla del todo su
// estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
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
  jest.spyOn(globalThis, 'fetch').mockImplementation(async () => {
    throw new TypeError('Network request failed')
  })
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

describe('Recibir — con acomodo', () => {
  it('agrega la línea sin pedir posición destino y la manda con el modo PUTAWAY', async () => {
    await saveDeviceIdentity({
      devicePublicId: 'dev-1',
      deviceSecret: 'secret-1',
      tenantName: 'Teikem Demo',
      defaultWarehousePublicId: 'wh-1',
      theme: null,
      defaultWarehouseReceivingMode: 'PUTAWAY',
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
    getDb().runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")

    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByText('Recibo ciego')).toBeTruthy())
    await fireEvent.press(screen.getByText('Recibo ciego'))

    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: 'SKU-1' } })
    await waitFor(() => expect(screen.getByText('Agregar')).toBeTruthy())
    await fireEvent.press(screen.getByText('Agregar'))

    await waitFor(() => expect(screen.getByText('1 SKU-1')).toBeTruthy())
    expect(screen.queryByLabelText('Escanea la posición destino')).toBeNull()
    expect(screen.getByText('Cierra el recibo y crea las tareas de acomodo.')).toBeTruthy()
    expect(getOpenReceipt()).toMatchObject({ receivingMode: 'PUTAWAY' })

    await fireEvent.press(screen.getByText('Confirmar recibo'))
    await waitFor(() => expect(listOutbox()).toHaveLength(1))
    const body = JSON.parse(listOutbox()[0].body)
    expect(body.receivingMode).toBe('PUTAWAY')
    expect(body.lines[0].targetBinCode).toBeNull()
  })
})
