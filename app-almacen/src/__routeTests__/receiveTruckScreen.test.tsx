// Lote 184 — «Recibir juntos» (la carga): los recibos de una carga viajan a la cola con su llave y el primero fija el dueño; un documento de otro dueño no entra. En archivo propio (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenReceipt, startLocalReceipt } from '../features/receive/localLookup'
import { getTruck, startTruck } from '../features/receive/receiveTruck'
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { listOutbox } from '../kernel/sync/outbox'

async function signIn() {
  await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null, defaultWarehouseReceivingMode: 'PUTAWAY' })
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
}

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

describe('Recibir — carga (recibir juntos)', () => {
  it('el recibo de un documento del camión se encola con la llave del camión, fija el dueño y cuenta el documento', async () => {
    await signIn()
    getDb().runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
    getDb().runSync("INSERT INTO asn (id, number, reference, client_name, warehouse_public_id, is_active) VALUES (5, 'LLE-00005', 'R-5', 'ASICS Care', 'wh-1', 1)")
    startTruck('wh-1', 'truck-key-1')
    startLocalReceipt('wh-1', { kind: 'asn', purchaseOrderPublicId: null, asnId: 5, label: 'LLE-00005' }, 'PUTAWAY')
    expect(getOpenReceipt()?.groupKey).toBe('truck-key-1')
    expect(getTruck()?.ownerKey).toBe('C:asics care')

    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: 'SKU-1' } })
    await waitFor(() => expect(screen.getByText('Agregar')).toBeTruthy())
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('Confirmar recibo')).toBeTruthy())
    await fireEvent.press(screen.getByText('Confirmar recibo'))

    await waitFor(() => expect(listOutbox()).toHaveLength(1))
    expect(JSON.parse(listOutbox()[0].body)).toMatchObject({ asnId: 5, groupKey: 'truck-key-1', confirm: true })
    expect(getTruck()?.docs).toBe(1)
  })

  it('con un camión de un dueño, el documento de otro dueño no entra', async () => {
    await signIn()
    getDb().runSync("INSERT INTO asn (id, number, reference, client_name, warehouse_public_id, is_active) VALUES (5, 'LLE-00005', 'R-5', 'ASICS Care', 'wh-1', 1)")
    getDb().runSync("INSERT INTO asn (id, number, reference, client_name, warehouse_public_id, is_active) VALUES (6, 'LLE-00006', 'R-6', 'Otra Marca', 'wh-1', 1)")
    startTruck('wh-1', 'truck-key-2')
    getDb().runSync("UPDATE receive_truck SET owner_key = 'C:asics care'")

    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByTestId('receive-truck')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea la orden o el aviso'), 'submitEditing', { nativeEvent: { text: 'LLE-00006' } })
    await waitFor(() => expect(screen.getByText(/otro dueño que los de esta carga/)).toBeTruthy())
    expect(getOpenReceipt()).toBeNull()
  })
})
