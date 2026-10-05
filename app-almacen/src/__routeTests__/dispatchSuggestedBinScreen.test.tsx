// Pedido del dueño 2026-10-05 — Despacho: al elegir el producto el cursor cae en la CANTIDAD; la posición se SUGIERE (la del primer lugar
// de salida con disponible: vence primero, luego zona y código) y se puede ignorar escaneando otra; con un producto con LOTE la posición
// deja de ser opcional: es la del próximo lote en salir (FEFO) y otra posición, o más de lo que hay ahí, se rechaza. Sin señal no hay
// sugerencia. En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenPick } from '../features/dispatch/localPick'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { insertProduct, json, mockFetch, setupDevice } from './countKit'

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

function laterScan() {
  jest.setSystemTime(Date.now() + 1000)
}

const BIN_FIELD = 'Escanea de qué posición sale'
const stock = (...items: Array<Record<string, unknown>>) => (c: { method: string; path: string }) =>
  c.method === 'GET' && c.path === '/api/v1/inventory/balances' ? json(200, { total: items.length, skip: 0, take: 200, items }) : null

async function openDraft(code: string) {
  await renderRouter('src/app', { initialUrl: '/dispatch' })
  await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
  await fireEvent(screen.getByLabelText('Escanea el producto'), 'submitEditing', { nativeEvent: { text: code } })
  await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
}

describe('Despacho — posición sugerida y lote obligatorio (FEFO)', () => {
  it('producto sin lote: el cursor cae en la cantidad y la posición se sugiere, pero se puede escanear otra', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    const calls = mockFetch([
      stock(
        { binCode: 'R-02', zoneTypeCode: 'RESERVE', qtyAvailable: 30 },
        { binCode: 'P-01', zoneTypeCode: 'PICKING', qtyAvailable: 8 },
        { binCode: 'Q-01', zoneTypeCode: 'QUARANTINE', qtyAvailable: 99 },
      ),
    ])
    await openDraft('7501')

    // consulta del disponible del producto en el almacén del aparato
    expect(calls.find((c) => c.path === '/api/v1/inventory/balances')?.search).toContain('productPublicIds=p1')
    // el cursor cae en la cantidad (no en la posición)
    expect(screen.getByLabelText('Cantidad').props.autoFocus).toBe(true)
    expect(screen.getByLabelText(BIN_FIELD).props.autoFocus).toBeFalsy()
    // la sugerida: picking antes que reserva; la cuarentena no cuenta
    await waitFor(() => expect(screen.getByText('Sugerida: P-01')).toBeTruthy())
    expect(screen.getByText('Escanea la posición o toca «Usar»; puedes escanear otra.')).toBeTruthy()

    // otra posición distinta de la sugerida se acepta (no es obligatoria)
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '3')
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'R-02' } })
    await waitFor(() => expect(getOpenPick()?.lineRows.map((l) => [l.fromBinCode, l.quantity])).toEqual([['R-02', 3]]))
  })

  it('«Usar» toma la sugerida y agrega la línea con la cantidad escrita', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    mockFetch([stock({ binCode: 'P-01', zoneTypeCode: 'PICKING', qtyAvailable: 8 })])
    await openDraft('7501')
    await waitFor(() => expect(screen.getByText('Sugerida: P-01')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '2')
    laterScan()
    await fireEvent.press(screen.getByLabelText('Usar P-01'))
    await waitFor(() => expect(getOpenPick()?.lineRows.map((l) => [l.fromBinCode, l.quantity])).toEqual([['P-01', 2]]))
  })

  it('producto con lote: la posición es la del lote que vence primero; otra se rechaza; más de lo que hay ahí también; la siguiente sugerencia descuenta lo sacado', async () => {
    await setupDevice()
    insertProduct(1, 'p-lot', 'LOT-1', 'Pintura', '7601', 'LOT')
    mockFetch([
      stock(
        { binCode: 'B-02', zoneTypeCode: 'RESERVE', lotNumber: 'L-9', expiryDate: '2027-03-01', qtyAvailable: 20 },
        { binCode: 'A-01', zoneTypeCode: 'RESERVE', lotNumber: 'L-3', expiryDate: '2026-12-31', qtyAvailable: 5 },
      ),
    ])
    await openDraft('7601')
    await waitFor(() => expect(screen.getByText('Debe salir de A-01')).toBeTruthy())
    expect(screen.getByText(/lote L-3 · vence 12\/31\/2026 · disponible 5/)).toBeTruthy()

    // otra posición: no se agrega nada y el aviso dice cuál
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '2')
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'B-02' } })
    await waitFor(() => expect(screen.getByText(/debe salir de A-01/)).toBeTruthy())
    expect(getOpenPick()?.lineRows).toEqual([])

    // más de lo que hay en ese lote
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '9')
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'A-01' } })
    await waitFor(() => expect(screen.getByText(/En A-01 solo hay 5 disponible/)).toBeTruthy())
    expect(getOpenPick()?.lineRows).toEqual([])

    // lo que hay, de A-01
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '5')
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'A-01' } })
    await waitFor(() => expect(getOpenPick()?.lineRows.map((l) => [l.fromBinCode, l.quantity])).toEqual([['A-01', 5]]))
  })

  it('sin señal no hay sugerencia ni se exige posición', async () => {
    await setupDevice()
    insertProduct(1, 'p-lot', 'LOT-1', 'Pintura', '7601', 'LOT')
    mockFetch([])
    await openDraft('7601')
    await waitFor(() => expect(screen.getByText('Sin señal: no se pudo buscar de dónde sale el producto. Escanea la posición.')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '1')
    laterScan()
    await fireEvent(screen.getByLabelText(BIN_FIELD), 'submitEditing', { nativeEvent: { text: 'X-99' } })
    await waitFor(() => expect(getOpenPick()?.lineRows.map((l) => l.fromBinCode)).toEqual(['X-99']))
  })
})
