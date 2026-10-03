// docs/mobile/mejoras-ux-zebra.md §2 ("escanear = aceptar"): en Despacho, con la cantidad ya escrita, escanear la posición
// de donde sale agrega la línea (no hay botón "Agregar") y deja un aviso verde de lo agregado. Sin cantidad no agrega:
// dispatchQtyFirstScreen.test.tsx (decisión del dueño 5, lote A5). En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenPick } from '../features/dispatch/localPick'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
})

afterEach(cleanup)

describe('Despacho — la lectura de la posición agrega la línea', () => {
  it('producto → cantidad → escanear posición = línea agregada al instante', async () => {
    await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    getDb().runSync(
      "INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active, owner_client_public_id, owner_name) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1, 'c1', 'Cliente Uno')",
    )

    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: '7501' } })

    await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
    // la cantidad arranca vacía y no hay botón "Agregar": la lectura de la posición es la que agrega
    expect(screen.getByLabelText('Cantidad').props.value).toBe('')
    expect(screen.queryByRole('button', { name: 'Agregar' })).toBeNull()
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '1250')
    await fireEvent(screen.getByLabelText('Escanea de qué posición sale'), 'submitEditing', { nativeEvent: { text: 'A-01' } })

    await waitFor(() => expect(screen.getByText('Líneas recolectadas')).toBeTruthy())
    expect(getOpenPick()?.lineRows).toEqual([expect.objectContaining({ sku: 'SKU-1', quantity: 1250, fromBinCode: 'A-01' })])
    expect(screen.getByText('Agregado: 1,250 SKU-1 desde A-01')).toBeTruthy()
  })
})
