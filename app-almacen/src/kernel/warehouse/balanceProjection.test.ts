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
import { deltasOf, issueDeltas, projectOperation } from './balanceProjection'
import { queueManualIssue } from '../../features/dispatch/dispatchApi'
import { listOutbox } from '../sync/outbox'

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

describe('409 «todavía se está procesando» (2026-10-11): la fila sigue pendiente y el saldo local NO se deshace', () => {
  it('Transferir: el efecto local se mantiene; al enviarse en la siguiente pasada tampoco se toca', async () => {
    const id = queueTransfer(T)
    const inFlight = 'La operación con esta clave todavía se está procesando.'
    postMock.mockResolvedValueOnce({ error: { status: 409, title: inFlight, code: 'conflict' }, response: new Response(null, { status: 409 }) })
    const res = await runOutbox()
    expect(res).toMatchObject({ rejected: 0, remaining: 1 })
    expect(listOutbox().find((r) => r.id === id)?.status).toBe('pending')
    expect(A()[0].qtyOnHand).toBe(7)
    expect(B()[0].qtyOnHand).toBe(3)

    postMock.mockResolvedValueOnce({ data: {}, response: new Response(null, { status: 200 }) })
    expect((await runOutbox()).sent).toBe(1)
    expect(A()[0].qtyOnHand).toBe(7)
  })
})

describe('despacho manual (DMA, 2026-10-11): resta lo que sale y lo deshace exacto', () => {
  const LINE = (quantity: number, binId = 10, productPublicId = 'p1') => ({ productPublicId, sku: 'SKU-1', productName: 'Guantes', quantity, fromBinCode: 'A-01', fromBinId: binId })

  function seedLots() {
    // p2 por lote en A-01: L-2 vence antes que L-1; sin lote no hay
    const db = getDb()
    db.runSync(`INSERT INTO product (id, public_id, sku, name, tracking_type_code, is_active) VALUES (2, 'p2', 'LOT-1', 'Cable', 'LOT', 1)`)
    db.runSync(
      `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, lot_id, lot_number, lot_expiry_date, qty_on_hand, qty_reserved, updated_at_utc)
       VALUES (20, '${WH}', 10, 2, 'p2', 1, 'L-1', '2027-06-30', 5, 0, '2026-10-10T10:00:00.000Z'),
              (21, '${WH}', 10, 2, 'p2', 2, 'L-2', '2026-12-31', 4, 1, '2026-10-10T10:00:00.000Z')`,
    )
  }
  const lotQty = (id: number) => getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE id = ?', [id])?.q

  it('sin lote: resta lo disponible de la posición; lo que el aparato no tiene no se resta', () => {
    expect(issueDeltas({ warehousePublicId: WH, lines: [{ productPublicId: 'p1', quantity: 3, binId: 10 }] })).toEqual([
      { warehousePublicId: WH, binId: 10, productPublicId: 'p1', lotId: null, delta: -3 },
    ])
    // disponible 8 (10 en mano − 2 reservados): de 12 solo se proyectan 8
    expect(issueDeltas({ warehousePublicId: WH, lines: [{ productPublicId: 'p1', quantity: 12, binId: 10 }] })[0].delta).toBe(-8)
    expect(issueDeltas({ warehousePublicId: WH, lines: [{ productPublicId: 'p1', quantity: 1, binId: 11 }] })).toEqual([])
  })

  it('con lote: reparte por vencimiento (FEFO) contra lo disponible, también entre dos líneas de la misma posición', () => {
    seedLots()
    const body = { warehousePublicId: WH, lines: [{ productPublicId: 'p2', quantity: 2, binId: 10 }, { productPublicId: 'p2', quantity: 3, binId: 10 }] }
    expect(issueDeltas(body)).toEqual([
      { warehousePublicId: WH, binId: 10, productPublicId: 'p2', lotId: 2, delta: -2 },
      { warehousePublicId: WH, binId: 10, productPublicId: 'p2', lotId: 2, delta: -1 },
      { warehousePublicId: WH, binId: 10, productPublicId: 'p2', lotId: 1, delta: -2 },
    ])
  })

  it('al encolar resta, guarda el efecto con la fila; el rechazo lo deshace exacto y reintentar lo vuelve a aplicar', async () => {
    seedLots()
    const id = queueManualIssue(WH, 'SAMPLE', '', [LINE(2), { ...LINE(5), productPublicId: 'p2' }])
    expect(A().find((r) => r.productPublicId === 'p1')?.qtyOnHand).toBe(8)
    expect([lotQty(21), lotQty(20)]).toEqual([1, 3])
    expect(JSON.parse(listOutbox()[0].projection_json ?? '[]')).toHaveLength(3)

    postMock.mockResolvedValueOnce({ error: { status: 409, title: 'Inventario insuficiente.', code: 'insufficient_stock' }, response: new Response(null, { status: 409 }) })
    expect((await runOutbox()).rejected).toBe(1)
    expect(A().find((r) => r.productPublicId === 'p1')?.qtyOnHand).toBe(10)
    expect([lotQty(21), lotQty(20)]).toEqual([4, 5])

    retryRow(id)
    expect([lotQty(21), lotQty(20)]).toEqual([1, 3])
  })

  it('la bajada de saldos vuelve a restar lo pendiente, también en los lotes', async () => {
    seedLots()
    queueManualIssue(WH, 'SAMPLE', '', [{ ...LINE(4), productPublicId: 'p2' }])
    const server = (id: number, lotId: number, qty: number) => ({
      id, warehousePublicId: WH, binId: 10, productId: 2, productPublicId: 'p2', lotId, lotNumber: `L-${lotId}`, lotExpiryDate: null,
      qtyOnHand: qty, qtyReserved: lotId === 2 ? 1 : 0, updatedAtUtc: '2026-10-11T10:20:00.000Z', isActive: true,
    })
    getMock.mockResolvedValueOnce({ data: { items: [server(20, 1, 5), server(21, 2, 4)], nextCursor: null, serverTimeUtc: '2026-10-11T10:20:00.000Z' }, response: new Response(null, { status: 200 }) })
    await downloadBalances(WH)
    // L-2 tenía 3 disponibles (4 − 1 reservado) → −3; el resto (1) sale de L-1
    expect([lotQty(21), lotQty(20)]).toEqual([1, 4])
  })
})
