// Lote 16 (H11) — recibo directo contra un aviso de una sola línea: el mismo producto con un segundo destino distinto se
// rechaza al escanear la posición (el servidor rechazaría el recibo completo y un envío rechazado en la cola ya no se
// edita). En archivo propio: renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del
// mismo archivo (ver homeLock.test.tsx).
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
  jest.spyOn(globalThis, 'fetch').mockImplementation(async () => {
    throw new TypeError('Network request failed')
  })
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

async function captureLine(target: string): Promise<void> {
  await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
  await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: 'SKU-1' } })
  await waitFor(() => expect(screen.getByText('Siguiente')).toBeTruthy())
  await fireEvent.press(screen.getByText('Siguiente'))
  await waitFor(() => expect(screen.getByLabelText('Escanea la posición destino')).toBeTruthy())
  await fireEvent(screen.getByLabelText('Escanea la posición destino'), 'submitEditing', { nativeEvent: { text: target } })
}

describe('Recibir — directo contra aviso (H11)', () => {
  it('el mismo producto no puede quedar con dos destinos en la misma línea del aviso', async () => {
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
    db.runSync("INSERT INTO product (id, public_id, sku, name, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', 'NONE', 1)")
    db.runSync("INSERT INTO asn (id, warehouse_public_id, reference, is_active) VALUES (20, 'wh-1', 'ASN-9', 1)")
    db.runSync("INSERT INTO asn_line (id, asn_id, product_public_id, expected_qty) VALUES (1, 20, 'p1', 5)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (1, 'R-01', 'wh-1', 'RESERVE', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (2, 'R-02', 'wh-1', 'RESERVE', 1)")

    // RNTL 14: render es asíncrono; sin esperarlo, las actualizaciones de estado tras un toque no se aplican.
    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByLabelText('Escanea la orden o el aviso')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea la orden o el aviso'), 'submitEditing', { nativeEvent: { text: 'ASN-9' } })

    await captureLine('R-01')
    await waitFor(() => expect(screen.getByText('→ R-01')).toBeTruthy())

    await captureLine('R-02')
    await waitFor(() =>
      expect(
        screen.getByText('Ya se capturó SKU-1 con destino R-01; en un recibo con aviso u orden de compra cada línea entra a una sola posición.'),
      ).toBeTruthy(),
    )
    expect(getOpenReceipt()?.lines).toHaveLength(1)

    // Al mismo destino sí (el servidor las suma en la línea del aviso).
    await fireEvent(screen.getByLabelText('Escanea la posición destino'), 'submitEditing', { nativeEvent: { text: 'R-01' } })
    await waitFor(() => expect(getOpenReceipt()?.lines).toHaveLength(2))
  })
})
