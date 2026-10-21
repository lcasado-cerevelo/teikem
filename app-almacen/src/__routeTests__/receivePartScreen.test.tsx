// Lote 175 — «Recibir una parte»: contra un aviso, encola el envío de la parte (con clave de reintento, sin series ni daño) y deja el recibo
// abierto en el servidor; sin documento no se ofrece. En archivo propio: renderRouter() no aísla del todo su
// estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenReceipt, startLocalReceipt } from '../features/receive/localLookup'
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

describe('Recibir — una parte', () => {
  it('con un aviso encola la parte con su clave y vacía el recibo local', async () => {
    await saveDeviceIdentity({
      devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null,
      defaultWarehouseReceivingMode: 'PUTAWAY',
    })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    getDb().runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
    startLocalReceipt('wh-1', { kind: 'asn', purchaseOrderPublicId: null, asnId: 5, label: 'LLE-00005' }, 'PUTAWAY')

    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: 'SKU-1' } })
    await waitFor(() => expect(screen.getByText('Agregar')).toBeTruthy())
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('Recibir una parte')).toBeTruthy())

    await fireEvent.press(screen.getByText('Recibir una parte'))
    await waitFor(() => expect(listOutbox()).toHaveLength(1))
    const row = listOutbox()[0]
    expect(row.kind).toBe('receiptPart')
    expect(row.path).toBe('/api/v1/receipts/part')
    const body = JSON.parse(row.body)
    expect(body).toMatchObject({ asnId: 5, purchaseOrderPublicId: null })
    expect(body.clientKey).toMatch(/^[0-9a-f-]{36}$/)
    expect(body.lines).toHaveLength(1)
    expect(getOpenReceipt()).toBeNull()
  })

  it('Lote 186: con un aviso que ya lleva partes recibidas, el aparato muestra lo recibido de cada línea', async () => {
    await saveDeviceIdentity({
      devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null,
      defaultWarehouseReceivingMode: 'PUTAWAY',
    })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    getDb().runSync("INSERT INTO asn_line (id, asn_id, product_public_id, sku, expected_qty, posted_qty) VALUES (1, 5, 'p1', 'SKU-1', 10, 4)")
    startLocalReceipt('wh-1', { kind: 'asn', purchaseOrderPublicId: null, asnId: 5, label: 'LLE-00005' }, 'PUTAWAY')

    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByTestId('receive-doc-progress')).toBeTruthy())
    expect(screen.getByText('SKU-1: 4 de 10')).toBeTruthy()
  })

  it('Lote 191: con un aviso se ve la lista de lo esperado y tocar un producto abre su captura', async () => {
    await saveDeviceIdentity({
      devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null,
      defaultWarehouseReceivingMode: 'PUTAWAY',
    })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    getDb().runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
    getDb().runSync("INSERT INTO asn_line (id, asn_id, product_public_id, sku, product_name, expected_qty, posted_qty) VALUES (1, 5, 'p1', 'SKU-1', 'Tornillo', 10, 4)")
    startLocalReceipt('wh-1', { kind: 'asn', purchaseOrderPublicId: null, asnId: 5, label: 'LLE-00005' }, 'PUTAWAY')

    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByTestId('receive-expected')).toBeTruthy())
    expect(screen.getByText('Esperado 10 · Recibido 4 · Falta 6')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('receive-expected-SKU-1'))
    await waitFor(() => expect(screen.getByText('Agregar')).toBeTruthy())
  })

  it('Lote 196: contra un aviso no se acepta un producto que el aviso no trae', async () => {
    await saveDeviceIdentity({
      devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null,
      defaultWarehouseReceivingMode: 'PUTAWAY',
    })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    getDb().runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
    getDb().runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (2, 'p2', 'SKU-2', 'Tuerca', '7502', 'NONE', 1)")
    getDb().runSync("INSERT INTO asn_line (id, asn_id, product_public_id, sku, product_name, expected_qty, posted_qty) VALUES (1, 5, 'p1', 'SKU-1', 'Tornillo', 10, 0)")
    startLocalReceipt('wh-1', { kind: 'asn', purchaseOrderPublicId: null, asnId: 5, label: 'LLE-00005' }, 'PUTAWAY')

    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: 'SKU-2' } })
    await waitFor(() => expect(screen.getByText('Ese producto (SKU-2) no está en LLE-00005: solo se recibe lo que dice el documento.')).toBeTruthy())
    expect(screen.queryByText('Agregar')).toBeNull()
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: 'SKU-1' } })
    await waitFor(() => expect(screen.getByText('Agregar')).toBeTruthy())
  })
})
