// Lote 178 — «Recolectar juntos» en Despachar aviso de salida: se eligen dos avisos del mismo cliente, se recoge todo en una sola ruta guiada y se manda UNA recolección maestra
// (POST /pick-batches/groups) con las líneas de cada aviso. Un aviso de otro cliente no se puede elegir. En archivo propio: renderRouter() no aísla del todo su estado global de navegación.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { applyExitNotices, applySalesOrders } from '../kernel/sync/download'
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

const notice = (id: number, client: string, qty: number) => ({
  id, publicId: `sn-${id}`, number: `SAL-0000${id}`, warehousePublicId: 'wh-1', clientName: client, reference: null, destination: null, statusCode: 'REQUESTED', requiredDate: null, requestedAtUtc: null, isActive: true,
  lines: [
    { id: id * 10, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: qty, qtyReserved: qty, qtyShipped: 0, qtyBackordered: 0, reservations: [{ binId: 55, binCode: 'A-01', lotId: null, lotNumber: null, quantity: qty }] },
  ],
})

/** Lote 198: la lista muestra primero lo asignado a esta persona; los avisos de la prueba se le asignan. */
function assignAllToMe() {
  getDb().runSync('UPDATE exit_notice SET assigned_to_user_id = 7')
  getDb().runSync('UPDATE sales_order SET assigned_to_user_id = 7')
}

const ME = (c: { path: string }) => (c.path === '/api/v1/me' ? json(200, { userId: 7, permissions: ['warehouse.pick', 'warehouse.pick.any', 'inventory.view'] }) : null)

async function seed() {
  await setupDevice()
  const db = getDb()
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, is_active) VALUES (55, 'A-01', 'wh-1', 1, 1)")
  db.runSync(`INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, qty_on_hand, qty_reserved, updated_at_utc) VALUES (1, 'wh-1', 55, 1, 'p1', 20, 10, '2026-10-15T10:00:00.000Z')`)
  applyExitNotices(db, 'wh-1', [notice(1, 'ASICS', 4), notice(2, 'ASICS', 3), notice(3, 'NIKE', 3)])
}

/** Lote 198: producto → posición → cantidad (la cantidad viene rellena con lo reservado). */
async function pickLine(sku: string, bin: string) {
  await waitFor(() => expect(screen.getByLabelText('Producto a sacar')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Producto a sacar'), sku)
  await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
  await waitFor(() => expect(screen.getByLabelText('Posición de donde sacas')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Posición de donde sacas'), bin)
  await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
  await waitFor(() => expect(screen.getByTestId('exit-qty')).toBeTruthy())
  await fireEvent.press(screen.getByTestId('exit-qty-accept'))
}

const onHand = () => getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE id = 1')?.q

describe('Recolectar juntos (aviso de salida)', () => {
  it('elige dos avisos del mismo cliente, recoge la ruta de ambos y manda UNA maestra', async () => {
    await seed()
    const calls = mockFetch([
      ME,
      (c) => (c.method === 'POST' && c.path === '/api/v1/pick-batches/groups' ? json(200, { publicId: 'g1', number: 'EMP-00020', documents: [], movements: [] }) : null),
    ])
    assignAllToMe()
    await renderRouter('src/app', { initialUrl: '/exit-dispatch' })
    await fireEvent.press(await screen.findByTestId('exit-group-toggle'))
    await fireEvent.press(await screen.findByTestId('exit-notice-SAL-00001'))
    await fireEvent.press(screen.getByTestId('exit-notice-SAL-00003'))   // otro cliente: no se elige
    await waitFor(() => expect(screen.getByText('Es de otro cliente: una recolección maestra es de un solo cliente.')).toBeTruthy())
    expect(screen.getByTestId('exit-group-go')).toBeDisabled?.()
    await fireEvent.press(screen.getByTestId('exit-notice-SAL-00002'))
    await fireEvent.press(await screen.findByTestId('exit-group-go'))

    // el producto sirve para cualquiera de los avisos: primero toma la línea del aviso 1, luego la del 2 (el orden en que se marcaron)
    await waitFor(() => expect(screen.getByText('SAL-00001 + SAL-00002')).toBeTruthy())
    await pickLine('MED', 'a-01')
    await waitFor(() => expect(screen.getByText(/✓ 4 × MED · A-01/)).toBeTruthy())
    await pickLine('MED', 'A-01')
    await waitFor(() => expect(screen.getByText(/✓ 3 × MED · A-01/)).toBeTruthy())

    await fireEvent.press(screen.getByTestId('exit-finish'))
    await fireEvent.press(await screen.findByTestId('exit-send'))
    await waitFor(() => expect(screen.getByText('Recolección maestra EMP-00020 enviada.')).toBeTruthy())
    const post = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/pick-batches/groups')
    expect(post?.body).toMatchObject({
      documents: [
        { kind: 'EXIT_NOTICE', publicId: 'sn-1', lines: [{ lineId: 10, quantity: 4, binId: 55 }] },
        { kind: 'EXIT_NOTICE', publicId: 'sn-2', lines: [{ lineId: 20, quantity: 3, binId: 55 }] },
      ],
    })
    expect(listOutbox().map((r) => [r.kind, r.status])).toEqual([['pickGroup', 'sent']])
    expect(onHand()).toBe(13)
  })

  it('el servidor lo rechaza: se muestra su mensaje y el saldo vuelve', async () => {
    await seed()
    mockFetch([
      ME,
      (c) => (c.method === 'POST' && c.path === '/api/v1/pick-batches/groups' ? json(409, { title: 'No hay existencia suficiente en A-01.', status: 409, code: 'conflict' }) : null),
    ])
    assignAllToMe()
    await renderRouter('src/app', { initialUrl: '/exit-dispatch' })
    await fireEvent.press(await screen.findByTestId('exit-group-toggle'))
    await fireEvent.press(await screen.findByTestId('exit-notice-SAL-00001'))
    await fireEvent.press(screen.getByTestId('exit-notice-SAL-00002'))
    await fireEvent.press(await screen.findByTestId('exit-group-go'))
    for (const [text, code] of [[/✓ 4 × MED/, 'a-01'], [/✓ 3 × MED/, 'A-01']] as const) {
      await pickLine('MED', code)
      await waitFor(() => expect(screen.getByText(text)).toBeTruthy())
    }
    await fireEvent.press(screen.getByTestId('exit-finish'))
    await fireEvent.press(await screen.findByTestId('exit-send'))
    await waitFor(() => expect(screen.getByText('No hay existencia suficiente en A-01.')).toBeTruthy())
    expect(listOutbox()).toEqual([])
    expect(onHand()).toBe(20)
  })

  it('Lote 186: elige un aviso y una orden de venta del mismo cliente y manda UNA maestra con los dos tipos', async () => {
    await seed()
    const n = notice(5, 'ASICS', 2)
    applySalesOrders(getDb(), 'wh-1', [{
      id: 9, publicId: 'so-9', number: 'OV-00009', warehousePublicId: 'wh-1', clientName: 'ASICS', consigneeName: 'Sucursal Norte', statusCode: 'REQUESTED', requiredDate: null, requestedAtUtc: null, isActive: true,
      lines: [{ id: 50, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 3, qtyReserved: 3, qtyShipped: 0, qtyBackordered: 0, reservations: [{ binId: 55, binCode: 'A-01', lotId: null, lotNumber: null, quantity: 3 }] }],
    }])
    applyExitNotices(getDb(), 'wh-1', [n])
    const calls = mockFetch([
      ME,
      (c) => (c.method === 'POST' && c.path === '/api/v1/pick-batches/groups' ? json(200, { publicId: 'g2', number: 'EMP-00021', documents: [], movements: [] }) : null),
    ])
    assignAllToMe()
    await renderRouter('src/app', { initialUrl: '/exit-dispatch' })
    await fireEvent.press(await screen.findByTestId('exit-group-toggle'))
    await fireEvent.press(await screen.findByTestId('exit-notice-SAL-00005'))
    await fireEvent.press(await screen.findByTestId('exit-order-OV-00009'))
    await fireEvent.press(await screen.findByTestId('exit-group-go'))

    await pickLine('MED', 'A-01')
    await waitFor(() => expect(screen.getByText(/✓ 2 × MED · A-01/)).toBeTruthy())
    await pickLine('MED', 'a-01')
    await waitFor(() => expect(screen.getByText(/✓ 3 × MED · A-01/)).toBeTruthy())
    await fireEvent.press(screen.getByTestId('exit-finish'))
    await fireEvent.press(await screen.findByTestId('exit-send'))
    await waitFor(() => expect(screen.getByText('Recolección maestra EMP-00021 enviada.')).toBeTruthy())
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/pick-batches/groups')?.body).toMatchObject({
      documents: [
        { kind: 'EXIT_NOTICE', publicId: 'sn-5', lines: [{ lineId: 50, quantity: 2, binId: 55 }] },
        { kind: 'SALES_ORDER', publicId: 'so-9', lines: [{ lineId: 50, quantity: 3, binId: 55 }] },
      ],
    })
  })

  it('Lote 198: un producto que no falta por sacar se rechaza, y Volver en la posición regresa al producto', async () => {
    await seed()
    mockFetch([ME])
    assignAllToMe()
    await renderRouter('src/app', { initialUrl: '/exit-dispatch' })
    await fireEvent.press(await screen.findByTestId('exit-group-toggle'))
    await fireEvent.press(await screen.findByTestId('exit-notice-SAL-00001'))
    await fireEvent.press(screen.getByTestId('exit-notice-SAL-00002'))
    await fireEvent.press(await screen.findByTestId('exit-group-go'))
    await waitFor(() => expect(screen.getByLabelText('Producto a sacar')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Producto a sacar'), 'NOPE')
    await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
    await waitFor(() => expect(screen.getByText('Ese producto no está entre lo que falta por sacar de estos documentos.')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Producto a sacar'), 'MED')
    await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
    await waitFor(() => expect(screen.getByLabelText('Posición de donde sacas')).toBeTruthy())
    expect(screen.queryByLabelText('Producto a sacar')).toBeNull()   // el campo del producto desaparece mientras se pide la posición
    await fireEvent.press(screen.getByTestId('exit-back-product'))
    await waitFor(() => expect(screen.getByLabelText('Producto a sacar')).toBeTruthy())
  })
})
