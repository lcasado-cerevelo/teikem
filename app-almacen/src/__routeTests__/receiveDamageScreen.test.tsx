// Recibir — unidades dañadas en la captura (2026-10-08): el checkbox «Vinieron unidades dañadas» muestra la cantidad dañada y la razón (catálogo; «Otra» pide
// escribirla); después de la posición de lo bueno, un paso aparte pregunta dónde se dejan (sugerida: cuarentena si existe; si no, donde quedó lo bueno) o se desechan.
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenReceipt } from '../features/receive/localLookup'
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'

async function setup(withQuarantine = true) {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  setApiBaseUrl('http://api.test')
  jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = new URL((input as Request).url)
    if (url.pathname === '/api/v1/warehouse-tasks/putaway-suggestions') {
      return new Response(JSON.stringify([{ binId: 1, binCode: 'RSV-A-01', reason: 'Reserva', freeQty: 500 }]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }
    throw new TypeError('Network request failed')
  })
  await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'Demo', defaultWarehousePublicId: 'wh-1', theme: null, defaultWarehouseReceivingMode: 'DIRECT' })
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
  const db = getDb()
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (1, 'RSV-A-01', 'wh-1', 'RESERVE', 1)")
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (2, 'STG-01', 'wh-1', 'STAGING', 1)")
  if (withQuarantine) db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (3, 'Q-01', 'wh-1', 'QUARANTINE', 1)")
}

beforeEach(() => {
  jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
    buttons?.find((b) => b.style === 'destructive')?.onPress?.()
  })
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

async function toCapture(mode?: 'PUTAWAY') {
  await renderRouter('src/app', { initialUrl: '/receive' })
  await waitFor(() => expect(screen.getByText('Recibo ciego')).toBeTruthy())
  if (mode) await fireEvent.press(screen.getByTestId(`receive-mode-${mode}`))
  await fireEvent.press(screen.getByText('Recibo ciego'))
  await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
  await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: '7501' } })
  await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
}

async function markDamaged(qty: string, cause: 'Vino así' | 'Otra') {
  await fireEvent.press(screen.getByTestId('receive-damage-check'))
  await waitFor(() => expect(screen.getByLabelText('Cantidad dañada')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Cantidad dañada'), qty)
  await fireEvent.press(screen.getByTestId('receive-damage-cause'))
  await fireEvent.press(await screen.findByText(cause))
}

describe('Recibir — unidades dañadas', () => {
  it('sin marcar el checkbox no se piden cantidad dañada ni razón', async () => {
    await setup()
    await toCapture()
    expect(screen.getByTestId('receive-damage-check')).toBeTruthy()
    expect(screen.queryByLabelText('Cantidad dañada')).toBeNull()
    expect(screen.queryByTestId('receive-damage-cause')).toBeNull()
  })

  it('recibo directo: la posición de lo bueno y luego la de lo dañado (sugerida: cuarentena); la línea lleva el daño', async () => {
    await setup()
    await toCapture()
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '10')
    await markDamaged('3', 'Vino así')
    await fireEvent.press(screen.getByText('Siguiente'))
    await waitFor(() => expect(screen.getByText('Sugerida: RSV-A-01')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('receive-suggestion'))
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() => expect(screen.getByText('Sugerida: Q-01')).toBeTruthy())
    expect(getOpenReceipt()?.lines).toHaveLength(0)
    await fireEvent.press(screen.getByTestId('receive-damage-suggestion'))
    await waitFor(() => expect(screen.getByLabelText('Escanea la posición de lo dañado').props.value).toBe('Q-01'))
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() =>
      expect(getOpenReceipt()?.lines).toMatchObject([
        { receivedQty: 10, targetBinCode: 'RSV-A-01', damagedQty: 3, damageCause: 'ARRIVED_DAMAGED', damageBinCode: 'Q-01', damageDiscard: false, damageNote: null },
      ]),
    )
  })

  it('«Otra» pide escribir la razón: sin ella no se puede seguir; con ella viaja en la línea', async () => {
    await setup()
    await toCapture('PUTAWAY')
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '8')
    await markDamaged('2', 'Otra')
    await waitFor(() => expect(screen.getByLabelText('Escribe la razón')).toBeTruthy())
    expect(screen.getByRole('button', { name: 'Agregar' }).props.accessibilityState.disabled).toBe(true)
    await fireEvent.changeText(screen.getByLabelText('Escribe la razón'), 'se mojó en el camión')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('Sugerida: Q-01')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('receive-damage-suggestion'))
    await fireEvent.press(screen.getByLabelText('Aceptar'))
    await waitFor(() =>
      expect(getOpenReceipt()?.lines).toMatchObject([{ receivedQty: 8, damagedQty: 2, damageCause: 'OTHER', damageNote: 'se mojó en el camión', damageBinCode: 'Q-01' }]),
    )
  })

  it('sin posición de cuarentena la sugerida es la de recepción (con acomodo)', async () => {
    await setup(false)
    await toCapture('PUTAWAY')
    await markDamaged('1', 'Vino así')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('Sugerida: STG-01')).toBeTruthy())
  })

  it('Dar salida de una vez no pide posición: la línea queda marcada para dar salida', async () => {
    await setup()
    await toCapture('PUTAWAY')
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '5')
    await markDamaged('4', 'Vino así')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('Dar salida')).toBeTruthy())
    await fireEvent.press(screen.getByText('Dar salida'))
    await fireEvent.press(await screen.findByText('Donado'))
    await waitFor(() =>
      expect(getOpenReceipt()?.lines).toMatchObject([{ receivedQty: 5, damagedQty: 4, damageDiscard: true, damageBinCode: null, damageDestination: 'DONATED' }]),
    )
  })

  it('Volver en el paso de la posición dañada vuelve a la captura sin agregar nada', async () => {
    await setup()
    await toCapture('PUTAWAY')
    await markDamaged('1', 'Vino así')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('Sugerida: Q-01')).toBeTruthy())
    await fireEvent.press(screen.getByText('Volver'))
    await waitFor(() => expect(screen.getByLabelText('Cantidad dañada')).toBeTruthy())
    expect(getOpenReceipt()?.lines).toHaveLength(0)
  })
})
