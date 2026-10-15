// Despachar contra una orden de venta (Lote 65, P4 S2): la lista de órdenes por despachar, el despacho guiado por lo reservado (posición → cantidad), y al terminar
// Retiro / Entrega / Dejar para después. Va por la cola de salida (kind salesDispatch) con la ruta de la orden; un rechazo del servidor muestra su mensaje exacto.
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { applySalesOrders } from '../kernel/sync/download'
import { __resetSyncEngineForTests } from '../kernel/sync/engine'
import { listOutbox } from '../kernel/sync/outbox'
import { json, mockFetch, setupDevice } from './countKit'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  __resetSyncEngineForTests()
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

const ORDER = {
  id: 5, publicId: 'so-5', number: 'OV-00005', warehousePublicId: 'wh-1', clientName: 'Cliente 1', consigneeName: 'Sucursal Norte', statusCode: 'REQUESTED', requiredDate: null, requestedAtUtc: null, isActive: true,
  lines: [
    {
      id: 50, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 4, qtyReserved: 4, qtyShipped: 0, qtyBackordered: 0,
      reservations: [{ binId: 55, binCode: 'A-01', lotId: null, lotNumber: null, quantity: 4 }],
    },
  ],
}

const ME = (c: { path: string }) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions: ['sales.view', 'warehouse.pick'] }) : null)

async function seed() {
  await setupDevice()
  const db = getDb()
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, is_active) VALUES (55, 'A-01', 'wh-1', 1, 1)")
  db.runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, qty_on_hand, qty_reserved, updated_at_utc) VALUES (1, 'wh-1', 55, 1, 'p1', 10, 4, '2026-10-15T10:00:00.000Z')`,
  )
  applySalesOrders(db, 'wh-1', [ORDER])
}

const onHand = () => getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE id = 1')?.q

async function openOrderAndTake() {
  await renderRouter('src/app', { initialUrl: '/sales-dispatch' })
  await waitFor(() => expect(screen.getByText('Despachar orden de venta')).toBeTruthy())
  await fireEvent.press(await screen.findByTestId('sales-order-OV-00005'))
  await waitFor(() => expect(screen.getByText('Ve a A-01')).toBeTruthy())
  // la cantidad viene puesta con lo reservado; se lee la posición
  expect((screen.getByTestId('sales-qty').props as { value: string }).value).toBe('4')
  await fireEvent.changeText(screen.getByLabelText('Posición de donde sacas'), 'a-01')
  await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
  await waitFor(() => expect(screen.getByText(/✓ 4 × MED · A-01/)).toBeTruthy())
}

describe('Despachar orden de venta en el aparato', () => {
  it('Retiro: manda POST /sales-orders/{id}/dispatch con posición y cantidad, y avisa con el número de la recolección', async () => {
    await seed()
    const calls = mockFetch([
      ME,
      (c) => (c.method === 'POST' && c.path === '/api/v1/sales-orders/so-5/dispatch' ? json(200, { pickBatchPublicId: 'b1', pickBatchNumber: 'EMP-00012', order: {} }) : null),
    ])
    await openOrderAndTake()
    await fireEvent.press(screen.getByTestId('sales-finish'))
    await fireEvent.press(await screen.findByTestId('sales-pickup'))
    await waitFor(() => expect(screen.getByText('Despacho enviado: EMP-00012.')).toBeTruthy())
    const post = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/sales-orders/so-5/dispatch')
    expect(post?.body).toEqual({ lines: [{ lineId: 50, quantity: 4, binId: 55 }] })
    expect(post?.idempotencyKey).toMatch(/^app-/)
    expect(listOutbox().map((r) => [r.kind, r.status])).toEqual([['salesDispatch', 'sent']])
    expect(onHand()).toBe(6)
  })

  it('Entrega: pide los bultos y manda también la entrega, en la misma llamada', async () => {
    await seed()
    const calls = mockFetch([
      ME,
      (c) => (c.method === 'POST' && c.path === '/api/v1/sales-orders/so-5/dispatch' ? json(200, { pickBatchPublicId: 'b1', pickBatchNumber: 'EMP-00013', order: {} }) : null),
    ])
    await openOrderAndTake()
    await fireEvent.press(screen.getByTestId('sales-finish'))
    await fireEvent.press(await screen.findByTestId('sales-deliver'))
    await fireEvent.changeText(await screen.findByTestId('sales-pieces'), '3')
    await fireEvent.press(screen.getByTestId('sales-deliver-confirm'))
    await waitFor(() => expect(screen.getByText('Despacho enviado: EMP-00013.')).toBeTruthy())
    const post = calls.find((c) => c.path === '/api/v1/sales-orders/so-5/dispatch')
    expect(post?.body).toEqual({
      lines: [{ lineId: 50, quantity: 4, binId: 55 }],
      delivery: { serviceType: 'STANDARD', packages: [{ packageType: 'BOX', pieces: 3 }], confirmNow: true },
    })
  })

  it('sin señal: queda en la cola y el saldo local ya bajó; el aviso lo dice', async () => {
    await seed()
    mockFetch([ME])
    await openOrderAndTake()
    await fireEvent.press(screen.getByTestId('sales-finish'))
    await fireEvent.press(await screen.findByTestId('sales-later'))
    await waitFor(() => expect(screen.getByText('Despacho guardado: se envía cuando haya señal.')).toBeTruthy())
    expect(listOutbox().map((r) => [r.kind, r.status])).toEqual([['salesDispatch', 'pending']])
    expect(onHand()).toBe(6)
  })

  it('el servidor lo rechaza: se muestra su mensaje, la fila no queda y el saldo vuelve', async () => {
    await seed()
    mockFetch([
      ME,
      (c) => (c.method === 'POST' && c.path === '/api/v1/sales-orders/so-5/dispatch' ? json(409, { title: 'No hay existencia suficiente en A-01.', status: 409, code: 'conflict' }) : null),
    ])
    await openOrderAndTake()
    await fireEvent.press(screen.getByTestId('sales-finish'))
    await fireEvent.press(await screen.findByTestId('sales-pickup'))
    await waitFor(() => expect(screen.getByText('No hay existencia suficiente en A-01.')).toBeTruthy())
    expect(listOutbox()).toEqual([])
    expect(onHand()).toBe(10)
  })

  it('no deja sacar más de lo reservado ni leer una posición que no existe', async () => {
    await seed()
    mockFetch([ME])
    await renderRouter('src/app', { initialUrl: '/sales-dispatch' })
    await fireEvent.press(await screen.findByTestId('sales-order-OV-00005'))
    await waitFor(() => expect(screen.getByText('Ve a A-01')).toBeTruthy())
    await fireEvent.changeText(screen.getByTestId('sales-qty'), '5')
    await fireEvent.changeText(screen.getByLabelText('Posición de donde sacas'), 'A-01')
    await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
    await waitFor(() => expect(screen.getByText('No puedes sacar más de lo reservado (4).')).toBeTruthy())
    await fireEvent.changeText(screen.getByTestId('sales-qty'), '2')
    await fireEvent.changeText(screen.getByLabelText('Posición de donde sacas'), 'ZZ-99')
    await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
    await waitFor(() => expect(screen.getByText('Esa posición no existe en este almacén.')).toBeTruthy())
    expect(listOutbox()).toEqual([])
  })
})
