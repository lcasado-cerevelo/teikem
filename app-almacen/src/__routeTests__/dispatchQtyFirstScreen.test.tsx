// Lote A5 — decisión del dueño 5 (docs/decisiones-del-dueno-2026-10-03.md): en Despacho la cantidad va primero. Escanear la
// posición sin cantidad no agrega nada y avisa en grande; con 0 o algo que no es un número tampoco; al escribir la cantidad y
// volver a escanear, la línea entra con ESA cantidad (nunca con 1 por omisión). En archivo propio: renderRouter() no aísla del
// todo su estado global de navegación (ver homeLock.test.tsx).
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

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

// ScanField toma una sola vez la misma lectura dentro de 400 ms (DUPLICATE_WINDOW_MS); aquí el operario vuelve a escanear la
// misma posición tras escribir la cantidad, así que el reloj avanza un segundo antes de cada lectura (renderRouter deja
// el reloj falso de Jest puesto).
function laterScan() {
  jest.setSystemTime(Date.now() + 1000)
}

const QTY_FIRST = 'Escribe la cantidad primero y luego escanea la posición.'
const QTY_INVALID = 'La cantidad debe ser un número mayor que 0. Corrígela y vuelve a escanear la posición.'
const BIN_FIELD = 'Escanea de qué posición sale'

describe('Despacho — la cantidad va primero', () => {
  it('sin cantidad no agrega y avisa; con 0 o texto tampoco; con cantidad escrita agrega al escanear', async () => {
    await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    getDb().runSync(
      "INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active, owner_client_public_id, owner_name) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1, 'c1', 'Cliente Uno')",
    )

    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: '7501' } })
    await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
    expect(screen.getByText('Escribe la cantidad y luego escanea la posición: la línea se agrega sola.')).toBeTruthy()

    // 1) sin cantidad: no se agrega nada, aviso grande (bloque rojo) y se sigue en el mismo paso
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'A-01' } })
    expect(screen.getByText(QTY_FIRST)).toBeTruthy()
    expect(screen.getByTestId('scan-message-error')).toBeTruthy()
    expect(getOpenPick()?.lineRows).toEqual([])
    expect(screen.getByLabelText('Cantidad')).toBeTruthy()
    expect(screen.queryByText('Líneas recolectadas')).toBeNull()

    // escribir en la cantidad quita el aviso
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '0')
    expect(screen.queryByText(QTY_FIRST)).toBeNull()

    // 2) cantidad 0: tampoco agrega
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'A-01' } })
    expect(screen.getByText(QTY_INVALID)).toBeTruthy()
    expect(getOpenPick()?.lineRows).toEqual([])

    // 3) algo que no es un número: tampoco
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), 'dos')
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'A-01' } })
    expect(screen.getByText(QTY_INVALID)).toBeTruthy()
    expect(getOpenPick()?.lineRows).toEqual([])

    // 4) con la cantidad escrita, volver a escanear agrega la línea con esa cantidad
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '3')
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'A-01' } })
    await waitFor(() => expect(screen.getByText('Líneas recolectadas')).toBeTruthy())
    expect(getOpenPick()?.lineRows).toEqual([expect.objectContaining({ sku: 'SKU-1', quantity: 3, fromBinCode: 'A-01' })])
    expect(screen.getByText('Agregado: 3 SKU-1 desde A-01')).toBeTruthy()

    // la siguiente línea vuelve a empezar sin cantidad: escanear la posición de una vez no agrega una línea con 1
    await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: '7501' } })
    await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
    expect(screen.getByLabelText('Cantidad').props.value).toBe('')
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'B-02' } })
    expect(screen.getByText(QTY_FIRST)).toBeTruthy()
    expect(getOpenPick()?.lineRows).toHaveLength(1)
  })
})
