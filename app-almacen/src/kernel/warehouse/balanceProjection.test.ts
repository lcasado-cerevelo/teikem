import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../api/client'
import { __resetDbForTests, getDb } from '../db/database'
import { downloadBalances } from '../sync/download'
import { enqueue, retryRow, runOutbox } from '../sync/outbox'
import { buildAdjustRequest } from '../../features/adjust/adjustLogic'
import { buildTransferRequest } from '../../features/transfer/transferLogic'
import { queueAdjustment } from '../../features/adjust/adjustApi'
import { queueTransfer } from '../../features/transfer/transferApi'
import { localBalancesForBin } from './localBalances'
import { deltasOf, projectOperation } from './balanceProjection'

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client')
  return { ...actual, api: { GET: jest.fn(), POST: jest.fn(), PUT: jest.fn() } }
})

const getMock = api.GET as jest.Mock
const postMock = api.POST as jest.Mock
const WH = 'wh-1'

function seed() {
  const db = getDb()
  db.runSync(`INSERT INTO product (id, public_id, sku, name, is_active) VALUES (1, 'p1', 'SKU-1', 'Guantes', 1)`)
  db.runSync(`INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (10, 'A-01', '${WH}', 'PICKING', 1), (11, 'B-01', '${WH}', 'PICKING', 1)`)
  db.runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, qty_on_hand, qty_reserved, updated_at_utc)
     VALUES (1, '${WH}', 10, 1, 'p1', 10, 2, '2026-10-10T10:00:00.000Z')`,
  )
  db.runSync(`INSERT INTO sync_watermark (resource, since_utc, last_run_utc) VALUES ('balances:${WH}', '2026-10-10T09:55:00.000Z', '2026-10-10T10:00:00.000Z')`)
}
const A = () => localBalancesForBin(WH, 10)
const B = () => localBalancesForBin(WH, 11)
const T = { warehousePublicId: WH, productPublicId: 'p1', from: { id: 10, code: 'A-01' }, to: { id: 11, code: 'B-01' }, quantity: 3, lotId: null }

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  getMock.mockReset()
  postMock.mockReset()
  seed()
})

describe('efecto de las operaciones pendientes en los saldos locales (D2b)', () => {
  it('Transferir resta del origen y crea la fila del destino (id provisional negativo)', () => {
    queueTransfer(T)
    expect(A()[0]).toMatchObject({ qtyOnHand: 7, qtyAvailable: 5 })
    expect(B()).toEqual([expect.objectContaining({ id: expect.any(Number), qtyOnHand: 3, binCode: 'B-01', sku: 'SKU-1' })])
    expect(B()[0].id).toBeLessThan(0)
  })

  it('Ajustar sube o baja la cantidad (con signo)', () => {
    queueAdjustment({ warehousePublicId: WH, productPublicId: 'p1', binId: 10, lotId: null, direction: 'down', quantity: 4, reason: 'DAMAGE', note: 'x' })
    expect(A()[0].qtyOnHand).toBe(6)
    queueAdjustment({ warehousePublicId: WH, productPublicId: 'p1', binId: 10, lotId: null, direction: 'up', quantity: 1, reason: 'FOUND', note: 'x' })
    expect(A()[0].qtyOnHand).toBe(7)
  })

  it('deshacer (−1) restablece lo que había, también si la fila llegó a cero', () => {
    const body = buildTransferRequest({ ...T, quantity: 10 })
    projectOperation('transfer', body, 1)
    expect(A()[0]).toMatchObject({ qtyOnHand: 0, qtyAvailable: 0 }) // sigue a la vista solo por lo reservado (2)
    projectOperation('transfer', body, -1)
    expect(A()[0]).toMatchObject({ qtyOnHand: 10, qtyAvailable: 8 })
    expect(B()).toEqual([]) // la fila provisional del destino quedó en cero: no se muestra
  })

  it('lo rechazado por el servidor deshace el efecto; reintentarlo lo vuelve a aplicar', async () => {
    const id = queueTransfer(T)
    postMock.mockResolvedValueOnce({ error: { status: 409, title: 'No hay disponible.' }, response: new Response(JSON.stringify({ title: 'No hay disponible.' }), { status: 409 }) })
    const res = await runOutbox()
    expect(res.rejected).toBe(1)
    expect(A()[0].qtyOnHand).toBe(10)
    expect(B()).toEqual([])
    retryRow(id)
    expect(A()[0].qtyOnHand).toBe(7)
  })

  it('la bajada de saldos vuelve a sumar lo pendiente y reemplaza la fila provisional por la verdadera', async () => {
    queueTransfer(T)
    const server = (id: number, bin: number, qty: number) => ({
      id, warehousePublicId: WH, binId: bin, productId: 1, productPublicId: 'p1', lotId: null, lotNumber: null, lotExpiryDate: null,
      qtyOnHand: qty, qtyReserved: id === 1 ? 2 : 0, updatedAtUtc: '2026-10-10T10:20:00.000Z', isActive: true,
    })
    getMock.mockResolvedValueOnce({ data: { items: [server(1, 10, 10), server(2, 11, 1)], nextCursor: null, serverTimeUtc: '2026-10-10T10:20:00.000Z' }, response: new Response(null, { status: 200 }) })
    await downloadBalances(WH)
    // origen: servidor 10 − 3 pendientes = 7; destino: servidor 1 + 3 pendientes = 4; y ya no queda la fila provisional
    expect(A()[0].qtyOnHand).toBe(7)
    expect(B()).toEqual([expect.objectContaining({ id: 2, qtyOnHand: 4 })])
    expect(getDb().getAllSync('SELECT id FROM stock_balance WHERE id < 0')).toEqual([])
  })

  it('una vez enviada, la siguiente bajada trae el saldo verdadero sin sumar nada más', async () => {
    queueTransfer(T)
    postMock.mockResolvedValueOnce({ data: {}, response: new Response(null, { status: 200 }) })
    await runOutbox()
    getMock.mockResolvedValueOnce({
      data: { items: [{ id: 1, warehousePublicId: WH, binId: 10, productId: 1, productPublicId: 'p1', qtyOnHand: 7, qtyReserved: 2, updatedAtUtc: '2026-10-10T10:21:00.000Z', isActive: true }], nextCursor: null, serverTimeUtc: '2026-10-10T10:21:00.000Z' },
      response: new Response(null, { status: 200 }),
    })
    await downloadBalances(WH)
    expect(A()[0].qtyOnHand).toBe(7)
  })

  it('deltasOf: Daño y otros tipos no se proyectan', () => {
    expect(deltasOf('damage', {})).toEqual([])
    expect(deltasOf('adjust', buildAdjustRequest({ warehousePublicId: WH, productPublicId: 'p1', binId: 10, lotId: 5, direction: 'down', quantity: 2, reason: 'LOSS', note: 'n' }))).toEqual([
      { warehousePublicId: WH, binId: 10, productPublicId: 'p1', lotId: 5, delta: -2 },
    ])
  })

  it('Daño se encola con su ruta y sin efecto local', () => {
    const id = enqueue({ kind: 'damage', body: { origin: 'WAREHOUSE' } })
    expect(getDb().getFirstSync<{ path: string; method: string }>('SELECT path, method FROM outbox WHERE id = ?', [id])).toEqual({ path: '/api/v1/damage-reports', method: 'POST' })
    expect(A()[0].qtyOnHand).toBe(10)
  })
})
