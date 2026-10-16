import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { applySalesOrders } from '../../kernel/sync/download'
import { listOutbox } from '../../kernel/sync/outbox'
import { reapplyPendingSalesDispatches, findSalesOrderByNumber, getSalesOrderLines, listSalesOrders, queueSalesDispatch } from './localSales'
import { buildTasks } from './salesDispatchLogic'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

const WH = 'wh-1'
const ORDER = {
  id: 5, publicId: 'so-5', number: 'OV-00005', warehousePublicId: WH, clientName: 'Cliente 1', consigneeName: 'Sucursal Norte', statusCode: 'REQUESTED', requiredDate: '2026-10-20', requestedAtUtc: null, isActive: true,
  lines: [
    {
      id: 50, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 6, qtyReserved: 6, qtyShipped: 0, qtyBackordered: 0,
      reservations: [{ binId: 1, binCode: 'P-01', lotId: null, lotNumber: null, quantity: 4 }, { binId: 2, binCode: 'P-02', lotId: null, lotNumber: null, quantity: 2 }],
    },
  ],
}

function seed(): void {
  applySalesOrders(getDb(), WH, [ORDER])
  getDb().runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, lot_id, qty_on_hand, qty_reserved, updated_at_utc) VALUES (1, ?, 1, 1, 'p1', NULL, 10, 4, '2026-10-15T00:00:00Z')`,
    [WH],
  )
}

describe('órdenes de venta en el aparato', () => {
  it('se guardan con sus líneas y reservas, y se buscan por número (completo o solo el final)', () => {
    seed()
    expect(listSalesOrders(WH).map((o) => [o.number, o.reserved])).toEqual([['OV-00005', 6]])
    expect(findSalesOrderByNumber(WH, 'ov-00005')?.id).toBe(5)
    expect(findSalesOrderByNumber(WH, '00005')?.id).toBe(5)
    expect(findSalesOrderByNumber(WH, 'OV-9')).toBeNull()
    expect(getSalesOrderLines(5)[0].reservations.map((r) => r.binCode)).toEqual(['P-01', 'P-02'])
  })

  it('una orden inactiva que llega se borra de la copia', () => {
    seed()
    applySalesOrders(getDb(), WH, [{ ...ORDER, isActive: false }])
    expect(listSalesOrders(WH)).toEqual([])
    expect(getDb().getAllSync('SELECT * FROM sales_order_line')).toEqual([])
  })

  it('despachar encola con la ruta de la orden y refleja el efecto al instante en la orden y en los saldos', () => {
    seed()
    const order = findSalesOrderByNumber(WH, 'OV-00005')!
    const tasks = buildTasks(getSalesOrderLines(5))
    const id = queueSalesDispatch(order, tasks, [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 }], { packages: 2 })

    const row = listOutbox().find((r) => r.id === id)!
    expect(row.kind).toBe('salesDispatch')
    expect(row.path).toBe('/api/v1/sales-orders/so-5/dispatch')
    expect(JSON.parse(row.body)).toEqual({
      lines: [{ lineId: 50, quantity: 4, binId: 1 }],
      delivery: { serviceType: 'STANDARD', packages: [{ packageType: 'BOX', pieces: 2 }], confirmNow: true },
    })
    const line = getSalesOrderLines(5)[0]
    expect([line.qtyReserved, line.qtyShipped]).toEqual([2, 4])
    expect(line.reservations.map((r) => [r.binCode, r.quantity])).toEqual([['P-02', 2]])
    expect(getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE id = 1')?.q).toBe(6)
  })

  it('si se despacha todo lo reservado la orden sale de la lista', () => {
    seed()
    const order = findSalesOrderByNumber(WH, 'OV-00005')!
    const tasks = buildTasks(getSalesOrderLines(5))
    queueSalesDispatch(order, tasks, tasks.map((t) => ({ taskKey: t.key, binId: t.binId, binCode: t.binCode, qty: t.qty })), null)
    expect(listSalesOrders(WH)).toEqual([])
  })

  it('al bajar la orden otra vez, el despacho pendiente se vuelve a reflejar', () => {
    seed()
    const order = findSalesOrderByNumber(WH, 'OV-00005')!
    const tasks = buildTasks(getSalesOrderLines(5))
    queueSalesDispatch(order, tasks, [{ taskKey: tasks[1].key, binId: 2, binCode: 'P-02', qty: 2 }], null)
    // llega la copia del servidor (todavía sin el despacho)
    applySalesOrders(getDb(), WH, [ORDER])
    expect(getSalesOrderLines(5)[0].qtyReserved).toBe(6)
    reapplyPendingSalesDispatches(getDb())
    expect(getSalesOrderLines(5)[0].qtyReserved).toBe(4)
  })
})

// ---------------------------------------------------------------- Lote 71: kits
const KIT_ORDER = {
  id: 9, publicId: 'so-9', number: 'OV-00009', warehousePublicId: WH, clientName: 'Cliente 1', consigneeName: null, statusCode: 'REQUESTED', requiredDate: null, requestedAtUtc: null, isActive: true,
  lines: [
    {
      id: 90, productPublicId: 'kit', sku: 'KIT-1', productName: 'Kit uno', qtyOrdered: 3, qtyReserved: 3, qtyShipped: 0, qtyBackordered: 0, isKit: true,
      components: [{ productPublicId: 'a', sku: 'A', productName: 'Tornillo', qtyPerKit: 2 }, { productPublicId: 'b', sku: 'B', productName: 'Tuerca', qtyPerKit: 1 }],
      reservations: [
        { binId: 1, binCode: 'P-01', lotId: null, lotNumber: null, quantity: 6, productPublicId: 'a', sku: 'A', productName: 'Tornillo' },
        { binId: 2, binCode: 'P-02', lotId: null, lotNumber: null, quantity: 3, productPublicId: 'b', sku: 'B', productName: 'Tuerca' },
      ],
    },
  ],
}

function seedKit(): void {
  applySalesOrders(getDb(), WH, [KIT_ORDER])
  getDb().runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, lot_id, qty_on_hand, qty_reserved, updated_at_utc) VALUES (11, ?, 1, 1, 'a', NULL, 10, 6, '2026-10-16T00:00:00Z')`,
    [WH],
  )
  getDb().runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, lot_id, qty_on_hand, qty_reserved, updated_at_utc) VALUES (12, ?, 2, 2, 'b', NULL, 5, 3, '2026-10-16T00:00:00Z')`,
    [WH],
  )
}

describe('kits en la copia local', () => {
  it('la línea sabe que es un kit, sus componentes y de qué componente es cada reserva', () => {
    seedKit()
    const line = getSalesOrderLines(9)[0]
    expect(line.isKit).toBe(true)
    expect(line.components.map((c) => [c.sku, c.qtyPerKit])).toEqual([['A', 2], ['B', 1]])
    expect(line.reservations.map((r) => [r.sku, r.binCode, r.quantity])).toEqual([['A', 'P-01', 6], ['B', 'P-02', 3]])
    expect(buildTasks([line]).map((t) => [t.sku, t.kitSku])).toEqual([['A', 'KIT-1'], ['B', 'KIT-1']])
  })

  it('despachar kits manda solo los kits completos, baja la existencia de cada componente y descuenta sus reservas', () => {
    seedKit()
    const order = findSalesOrderByNumber(WH, 'OV-00009')!
    const tasks = buildTasks(getSalesOrderLines(9))
    const id = queueSalesDispatch(order, tasks, [
      { taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 },
      { taskKey: tasks[1].key, binId: 2, binCode: 'P-02', qty: 3 },
    ], null)

    const row = listOutbox().find((r) => r.id === id)!
    expect(JSON.parse(row.body)).toEqual({ lines: [{ lineId: 90, quantity: 2 }] })   // 4 A / 2 = 2 kits (sobra 1 B)
    const line = getSalesOrderLines(9)[0]
    expect([line.qtyReserved, line.qtyShipped]).toEqual([1, 2])
    expect(line.reservations.map((r) => [r.sku, r.quantity])).toEqual([['A', 2], ['B', 1]])
    const onHand = (pid: string) => getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE product_public_id = ?', [pid])?.q
    expect([onHand('a'), onHand('b')]).toEqual([6, 3])   // salieron 4 A y 2 B (los kits completos), no las piezas leídas de más
  })

  it('al volver a bajar la orden, el despacho pendiente de kits se vuelve a reflejar', () => {
    seedKit()
    const order = findSalesOrderByNumber(WH, 'OV-00009')!
    const tasks = buildTasks(getSalesOrderLines(9))
    queueSalesDispatch(order, tasks, [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 2 }, { taskKey: tasks[1].key, binId: 2, binCode: 'P-02', qty: 1 }], null)   // 1 kit
    applySalesOrders(getDb(), WH, [KIT_ORDER])
    expect(getSalesOrderLines(9)[0].qtyReserved).toBe(3)
    reapplyPendingSalesDispatches(getDb())
    expect(getSalesOrderLines(9)[0].qtyReserved).toBe(2)
  })
})
