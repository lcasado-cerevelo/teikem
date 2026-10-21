// Selector de modo por recibo (tarea 23): la pantalla de Recibir deja cambiar el modo solo de ese recibo.
// (copia de la cabecera de receiveDirectScreen) Lote 16 — la pantalla real de Recibir en un almacén "Directo a posición": tras la cantidad pide la posición destino
// (con la pista "Sugerida: …" del servidor), rechaza sin señal una posición de recepción, agrega la línea con "→ {bin}"
// y el envío a la cola lleva el modo y la posición. En archivo propio: renderRouter() no aísla del todo su estado global
// de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenReceipt } from '../features/receive/localLookup'
import { setApiBaseUrl } from '../kernel/api/client'
import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'

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

describe('Recibir — modo por recibo', () => {
  async function setup(mode: string) {
    await saveDeviceIdentity({
      devicePublicId: 'dev-1',
      deviceSecret: 'secret-1',
      tenantName: 'Teikem Demo',
      defaultWarehousePublicId: 'wh-1',
      theme: null,
      defaultWarehouseReceivingMode: mode,
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
    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByText('Recibo ciego')).toBeTruthy())
  }

  it('con modo fijo en el almacén no se pregunta: el recibo abre de una vez con ese modo', async () => {
    await setup('DIRECT')
    expect(screen.queryByTestId('receive-mode-DIRECT')).toBeNull()
    await fireEvent.press(screen.getByText('Recibo ciego'))
    expect(getOpenReceipt()?.receivingMode).toBe('DIRECT')
  })

  it('sin modo fijo, escogido el documento (aquí el recibo ciego) se pregunta el modo en la misma pantalla y vale solo para ese recibo', async () => {
    await setup('')
    await fireEvent.press(screen.getByText('Recibo ciego'))
    expect(getOpenReceipt()).toBeNull()
    await fireEvent.press(await screen.findByTestId('receive-mode-DIRECT'))
    expect(getOpenReceipt()?.receivingMode).toBe('DIRECT')
  })

  it('un almacén con acomodo fijo abre con acomodo', async () => {
    await setup('PUTAWAY')
    await fireEvent.press(screen.getByText('Recibo ciego'))
    expect(getOpenReceipt()?.receivingMode).toBe('PUTAWAY')
  })
})
