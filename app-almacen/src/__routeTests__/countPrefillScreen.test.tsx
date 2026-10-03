// docs/mobile/mejoras-ux-zebra.md §3: en el conteo, tocar un producto de "Lo que se espera aquí" llena el campo del
// producto con su código (sin enviarlo); se confirma con Aceptar y pasa a la cantidad. Las cantidades esperadas se siguen
// mostrando solo si el servidor las manda (permiso warehouse.count): aquí no las manda. En archivo propio: renderRouter()
// no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { startLocalCount } from '../features/count/localCount'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'

jest.mock('../features/count/countApi', () => {
  const actual = jest.requireActual('../features/count/countApi')
  return {
    ...actual,
    fetchExpectedLines: jest.fn(async () => [
      { lineId: 11, productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', systemQty: null },
      { lineId: 12, productPublicId: 'p2', sku: 'SKU-2', productName: 'Tuerca', systemQty: null },
    ]),
  }
})

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
})

afterEach(cleanup)

describe('Conteo — tocar un producto de la lista', () => {
  it('llena el campo del producto con su código; Aceptar lo toma como si se hubiera escaneado', async () => {
    await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    const db = getDb()
    db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
    db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (2, 'p2', 'SKU-2', 'Tuerca', '7502', 'NONE', 1)")
    startLocalCount('wh-1', { id: 5, code: 'A-01-01' }, { countId: 99, isBlind: true })

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Lo que se espera aquí')).toBeTruthy())
    expect(screen.getByText('Toca un producto para ponerlo en el campo y confirma con Aceptar.')).toBeTruthy()

    await fireEvent.press(screen.getByLabelText('Poner SKU-2 en el campo'))
    expect(screen.getByLabelText('Escanea el producto contado').props.value).toBe('SKU-2')
    // todavía no pasó a la cantidad: se confirma con Aceptar
    expect(screen.queryByLabelText('Cantidad encontrada')).toBeNull()

    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(screen.getByLabelText('Cantidad encontrada')).toBeTruthy())
    expect(screen.getByText('Tuerca')).toBeTruthy()
    // conteo a ciegas (el servidor no mandó la cantidad del sistema): no se muestra "Esperado"
    expect(screen.queryByText(/^Esperado:/)).toBeNull()
  })
})
