// Ajustar desde el menú (2026-10-11): se escanea la POSICIÓN y el PRODUCTO y se sube o baja la cantidad. Sirve para un producto que se ve en una posición y que el
// sistema da en 0 (o nunca estuvo): Consultar no lo lista, y aquí se puede SUBIR con el motivo «Encontrado». Un producto con lote sin saldo en la posición ofrece
// los lotes que ya tiene registrados. En archivo propio por el estado global de renderRouter().
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
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

const page = (items: unknown[]) => json(200, { total: items.length, skip: 0, take: 200, items })
const LOT_ELSEWHERE = [
  { id: 7, binId: 6, binCode: 'B-02-01', zoneTypeCode: 'STORAGE', productPublicId: 'p2', sku: 'MED-1', productName: 'Medicina', lotId: 9, lotNumber: 'L-9', qtyOnHand: 3, qtyReserved: 0, qtyAvailable: 3 },
]

/** Abre Ajustar «desde el menú» (sin posición ni producto): la posición A-01-01 (id 5) está vacía para el sistema. */
async function openAdjust(opts: { lots?: unknown[] } = {}) {
  await setupDevice()
  getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, is_active) VALUES (5, 'A-01-01', 'wh-1', 1)")
  insertProduct(1, 'p1', 'TOR-1', 'Tornillo', '7501', 'NONE')
  insertProduct(2, 'p2', 'MED-1', 'Medicina', '7502', 'LOT')
  insertProduct(3, 'p3', 'SER-1', 'Equipo', '7503', 'SERIAL')
  const calls = mockFetch([
    (c) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions: ['inventory.view', 'warehouse.adjust'] }) : null),
    // lo que el sistema dice que hay en la posición: nada
    (c) => (c.path === '/api/v1/inventory/balances' && c.search.includes('binIds') ? page([]) : null),
    // los lotes que el producto ya tiene registrados (en otras posiciones)
    (c) => (c.path === '/api/v1/inventory/balances' && c.search.includes('productPublicIds') ? page(opts.lots ?? []) : null),
    (c) => (c.method === 'POST' && c.path === '/api/v1/inventory/adjustments/quantity' ? json(200, { transactions: [], balances: [] }) : null),
  ])
  await renderRouter('src/app', { initialUrl: '/adjust' })
  await waitFor(() => expect(screen.getByText('Escanea la posición donde está')).toBeTruthy())
  return calls
}

async function scanBin(code: string) {
  await fireEvent.changeText(screen.getByLabelText('Escanea la posición donde está'), code)
  await fireEvent.press(screen.getByLabelText('Aceptar'))
}
async function scanProduct(code: string) {
  await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Escanea el producto'), code)
  await fireEvent.press(screen.getByLabelText('Aceptar'))
}
const autoConfirm = () =>
  jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => {
    buttons?.find((b) => b.style !== 'cancel')?.onPress?.()
  })

describe('Ajustar desde el menú', () => {
  it('producto que el sistema da en 0 en la posición: escanea posición y producto, SUBE con «Encontrado» y se queda listo para la siguiente posición', async () => {
    autoConfirm()
    const calls = await openAdjust()
    await scanBin('a-01-01')
    await scanProduct('TOR-1')
    await waitFor(() => expect(screen.getByText('Posición A-01-01')).toBeTruthy())
    expect(screen.getByText('TOR-1 · Tornillo')).toBeTruthy()
    await fireEvent.press(await screen.findByTestId('adjust-up'))
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '4')
    await fireEvent.press(screen.getByTestId('adjust-reason-FOUND'))
    await fireEvent.changeText(screen.getByLabelText('Nota (obligatoria)'), 'lo encontré en el rack')
    await fireEvent.press(screen.getByTestId('adjust-confirm'))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      productPublicId: 'p1',
      warehousePublicId: 'wh-1',
      binId: 5,
      quantity: 4,
      reason: 'FOUND',
      notes: 'lo encontré en el rack',
      lotId: null,
    })
    // desde el menú no se vuelve a Consultar: se queda en Ajustar, con el aviso y pidiendo otra posición
    await waitFor(() => expect(screen.getByText('Listo: Subir 4 de TOR-1 en A-01-01.')).toBeTruthy())
    expect(screen.getByText('Escanea la posición donde está')).toBeTruthy()
  })

  it('bajar con 0 en la posición no deja: «Solo se puede bajar hasta 0» y no manda nada', async () => {
    autoConfirm()
    const calls = await openAdjust()
    await scanBin('A-01-01')
    await scanProduct('TOR-1')
    await fireEvent.press(await screen.findByTestId('adjust-down'))
    await waitFor(() => expect(screen.getByText('Se puede bajar hasta 0 (lo reservado no sale).')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '1')
    await fireEvent.press(screen.getByTestId('adjust-reason-LOSS'))
    await fireEvent.changeText(screen.getByLabelText('Nota (obligatoria)'), 'x')
    await fireEvent.press(screen.getByTestId('adjust-confirm'))
    await waitFor(() => expect(screen.getByText('Solo se puede bajar hasta 0 (lo reservado no sale).')).toBeTruthy())
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('posición o producto que no existen: el mensaje de cada paso, sin avanzar', async () => {
    await openAdjust()
    await scanBin('ZZZ-9')
    await waitFor(() => expect(screen.getByText('No hay una posición con ese código.')).toBeTruthy())
    await scanBin('A-01-01')
    await scanProduct('NOEXISTE')
    await waitFor(() => expect(screen.getByText('No hay un producto con ese código.')).toBeTruthy())
    expect(screen.getByText('Posición A-01-01')).toBeTruthy()
  })

  it('producto con número de serie: no se ajusta desde aquí (se explica) y no avanza', async () => {
    await openAdjust()
    await scanBin('A-01-01')
    await scanProduct('SER-1')
    await waitFor(() => expect(screen.getByText('SER-1 lleva número de serie: por ahora se ajusta desde la web.')).toBeTruthy())
    expect(screen.queryByTestId('adjust-up')).toBeNull()
  })

  it('producto con lote sin saldo en la posición, con un solo lote registrado: lo usa y manda su lotId', async () => {
    autoConfirm()
    const calls = await openAdjust({ lots: LOT_ELSEWHERE })
    await scanBin('A-01-01')
    await scanProduct('MED-1')
    await fireEvent.press(await screen.findByTestId('adjust-up'))
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '2')
    await fireEvent.press(screen.getByTestId('adjust-reason-FOUND'))
    await fireEvent.changeText(screen.getByLabelText('Nota (obligatoria)'), 'apareció')
    await fireEvent.press(screen.getByTestId('adjust-confirm'))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({ productPublicId: 'p2', binId: 5, quantity: 2, reason: 'FOUND', lotId: 9 })
  })

  it('producto con lote que todavía no tiene ningún lote registrado: lo dice y manda a la web', async () => {
    await openAdjust({ lots: [] })
    await scanBin('A-01-01')
    await scanProduct('MED-1')
    await waitFor(() => expect(screen.getByText('MED-1 lleva lote y todavía no tiene lotes registrados: se ajusta desde la web.')).toBeTruthy())
    expect(screen.queryByTestId('adjust-up')).toBeNull()
  })

  it('«Empezar de nuevo» vuelve a pedir la posición', async () => {
    await openAdjust()
    await scanBin('A-01-01')
    await scanProduct('TOR-1')
    await waitFor(() => expect(screen.getByText('Posición A-01-01')).toBeTruthy())
    await fireEvent.press(screen.getByTestId('adjust-restart'))
    await waitFor(() => expect(screen.getByText('Escanea la posición donde está')).toBeTruthy())
    expect(screen.queryByText('Posición A-01-01')).toBeNull()
  })
})
