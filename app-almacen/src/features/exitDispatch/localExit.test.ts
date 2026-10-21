import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { applyExitNotices, applySalesOrders } from '../../kernel/sync/download'
import { listOutbox } from '../../kernel/sync/outbox'
import { reapplyPendingSalesDispatches, findSalesOrderByNumber, getSalesOrderLines, listSalesOrders } from '../salesDispatch/localSales'
import { buildTasks } from '../salesDispatch/salesDispatchLogic'
import { dispatchScope } from '../dispatch/dispatchScope'
import { readDispatchRequirement, saveDispatchRequirement } from '../dispatch/stockExit'
import { findExitNoticeByNumber, getExitNoticeLines, getGroupTasks, listExitNotices, listGroupableOrders, queueExitDispatch, queueExitGroup, reapplyPendingExitDispatches } from './localExit'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

const WH = 'wh-1'
const NOTICE = {
  id: 7, publicId: 'sn-7', number: 'SAL-00007', warehousePublicId: WH, clientName: 'ASICS', reference: 'OC-9', destination: 'Muelle 2', statusCode: 'REQUESTED', requiredDate: '2026-10-25', requestedAtUtc: null, isActive: true,
  lines: [
    {
      id: 70, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 8, qtyReserved: 6, qtyShipped: 0, qtyBackordered: 2,
      reservations: [{ binId: 1, binCode: 'P-01', lotId: 3, lotNumber: 'L1', quantity: 4 }, { binId: 2, binCode: 'P-02', lotId: null, lotNumber: null, quantity: 2 }],
    },
  ],
}

function seed(): void {
  applyExitNotices(getDb(), WH, [NOTICE])
  getDb().runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, lot_id, qty_on_hand, qty_reserved, updated_at_utc) VALUES (1, ?, 1, 1, 'p1', 3, 10, 4, '2026-10-20T00:00:00Z')`,
    [WH],
  )
}

describe('avisos de salida en el aparato', () => {
  it('se guardan con sus líneas y reservas, y se buscan por número (completo o solo el final)', () => {
    seed()
    expect(listExitNotices(WH).map((o) => [o.number, o.reserved, o.destination])).toEqual([['SAL-00007', 6, 'Muelle 2']])
    expect(findExitNoticeByNumber(WH, 'sal-00007')?.id).toBe(7)
    expect(findExitNoticeByNumber(WH, '00007')?.id).toBe(7)
    expect(findExitNoticeByNumber(WH, 'SAL-9')).toBeNull()
    const line = getExitNoticeLines(7)[0]
    expect(line.isKit).toBe(false)
    expect(line.reservations.map((r) => r.binCode)).toEqual(['P-01', 'P-02'])
  })

  it('un aviso inactivo que llega se borra de la copia', () => {
    seed()
    applyExitNotices(getDb(), WH, [{ ...NOTICE, isActive: false }])
    expect(listExitNotices(WH)).toEqual([])
    expect(getDb().getAllSync('SELECT * FROM exit_notice_line')).toEqual([])
  })

  it('despachar encola con la ruta del aviso y refleja el efecto al instante en el aviso y en los saldos', () => {
    seed()
    const notice = findExitNoticeByNumber(WH, 'SAL-00007')!
    const tasks = buildTasks(getExitNoticeLines(7))
    const id = queueExitDispatch(notice, tasks, [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 }])

    const row = listOutbox().find((r) => r.id === id)!
    expect(row.kind).toBe('exitDispatch')
    expect(row.path).toBe('/api/v1/exit-notices/sn-7/dispatch')
    expect(JSON.parse(row.body)).toEqual({ lines: [{ lineId: 70, quantity: 4, binId: 1, lotId: 3 }] })
    const line = getExitNoticeLines(7)[0]
    expect([line.qtyReserved, line.qtyShipped, line.qtyBackordered]).toEqual([2, 4, 2])
    expect(line.reservations.map((r) => [r.binCode, r.quantity])).toEqual([['P-02', 2]])
    expect(getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE id = 1')?.q).toBe(6)
  })

  it('si se despacha todo lo reservado el aviso sale de la lista', () => {
    seed()
    const notice = findExitNoticeByNumber(WH, 'SAL-00007')!
    const tasks = buildTasks(getExitNoticeLines(7))
    queueExitDispatch(notice, tasks, tasks.map((t) => ({ taskKey: t.key, binId: t.binId, binCode: t.binCode, qty: t.qty })))
    expect(listExitNotices(WH)).toEqual([])
  })

  it('al bajar el aviso otra vez, el despacho pendiente se vuelve a reflejar', () => {
    seed()
    const notice = findExitNoticeByNumber(WH, 'SAL-00007')!
    const tasks = buildTasks(getExitNoticeLines(7))
    queueExitDispatch(notice, tasks, [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 }])
    // llega la copia vieja del servidor (todavía sin el despacho)
    applyExitNotices(getDb(), WH, [NOTICE])
    expect(getExitNoticeLines(7)[0].qtyReserved).toBe(6)
    reapplyPendingExitDispatches(getDb())
    expect(getExitNoticeLines(7)[0].qtyReserved).toBe(2)
  })
})

describe('el despacho exige documento (copia en el aparato)', () => {
  it('sin copia es libre; guarda y lee el valor del servidor; uno desconocido vuelve a libre', () => {
    expect(readDispatchRequirement()).toBe('FREE')
    saveDispatchRequirement('EXIT_NOTICE')
    expect(readDispatchRequirement()).toBe('EXIT_NOTICE')
    saveDispatchRequirement('basura')
    expect(readDispatchRequirement()).toBe('FREE')
  })
})

describe('recolectar juntos (Lote 178)', () => {
  const NOTICE2 = {
    ...NOTICE, id: 8, publicId: 'sn-8', number: 'SAL-00008',
    lines: [{ id: 80, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 3, qtyReserved: 3, qtyShipped: 0, qtyBackordered: 0, reservations: [{ binId: 1, binCode: 'P-01', lotId: 3, lotNumber: 'L1', quantity: 3 }] }],
  }

  it('junta las tareas de varios avisos y manda UNA recolección maestra que se refleja en cada aviso y en los saldos', () => {
    seed()
    applyExitNotices(getDb(), WH, [NOTICE, NOTICE2])
    const notices = [findExitNoticeByNumber(WH, 'SAL-00007')!, findExitNoticeByNumber(WH, 'SAL-00008')!]
    const { tasks, lineNotice } = getGroupTasks(notices)
    expect(tasks.map((t) => t.lineId)).toEqual([70, 70, 80])
    expect(lineNotice.get(80)).toBe(8)

    const picks = [
      { taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 },
      { taskKey: tasks[2].key, binId: 1, binCode: 'P-01', qty: 3 },
    ]
    const id = queueExitGroup(notices, tasks, picks, lineNotice)!

    const row = listOutbox().find((r) => r.id === id)!
    expect([row.kind, row.path]).toEqual(['pickGroup', '/api/v1/pick-batches/groups'])
    const body = JSON.parse(row.body)
    expect(body.documents.map((d: { publicId: string; lines: unknown[] }) => [d.publicId, d.lines.length])).toEqual([['sn-7', 1], ['sn-8', 1]])
    expect(getExitNoticeLines(7)[0].qtyReserved).toBe(2)
    expect(listExitNotices(WH).map((o) => o.number)).toEqual(['SAL-00007'])   // el segundo se despachó completo
    expect(getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE id = 1')?.q).toBe(3)
  })

  it('con menos de dos avisos con algo recogido no encola nada', () => {
    seed()
    applyExitNotices(getDb(), WH, [NOTICE, NOTICE2])
    const notices = [findExitNoticeByNumber(WH, 'SAL-00007')!, findExitNoticeByNumber(WH, 'SAL-00008')!]
    const { tasks, lineNotice } = getGroupTasks(notices)
    expect(queueExitGroup(notices, tasks, [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 }], lineNotice)).toBeNull()
    expect(listOutbox()).toHaveLength(0)
  })

  it('al volver a bajar los avisos se reaplica la maestra que sigue en la cola', () => {
    seed()
    applyExitNotices(getDb(), WH, [NOTICE, NOTICE2])
    const notices = [findExitNoticeByNumber(WH, 'SAL-00007')!, findExitNoticeByNumber(WH, 'SAL-00008')!]
    const { tasks, lineNotice } = getGroupTasks(notices)
    queueExitGroup(notices, tasks, [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 }, { taskKey: tasks[2].key, binId: 1, binCode: 'P-01', qty: 3 }], lineNotice)
    applyExitNotices(getDb(), WH, [NOTICE, NOTICE2])   // llega la copia del servidor, que todavía no sabe de la cola
    expect(getExitNoticeLines(7)[0].qtyReserved).toBe(6)
    reapplyPendingExitDispatches(getDb())
    expect(getExitNoticeLines(7)[0].qtyReserved).toBe(2)
    expect(findExitNoticeByNumber(WH, 'SAL-00008')).toBeNull()
  })

  // Lote 186: una orden de venta (con el MISMO id de línea que un aviso) viaja en la misma maestra
  const ORDER = {
    id: 5, publicId: 'so-5', number: 'OV-00005', warehousePublicId: WH, clientName: 'ASICS', consigneeName: 'Sucursal Norte', statusCode: 'REQUESTED', requiredDate: '2026-10-20', requestedAtUtc: null, isActive: true,
    lines: [{ id: 70, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 5, qtyReserved: 5, qtyShipped: 0, qtyBackordered: 0, reservations: [{ binId: 1, binCode: 'P-01', lotId: 3, lotNumber: 'L1', quantity: 5 }] }],
  }

  it('Lote 186: junta un aviso y una orden de venta aunque sus líneas tengan el mismo id, y manda la orden con su tipo', () => {
    seed()
    applySalesOrders(getDb(), WH, [ORDER])
    const notice = findExitNoticeByNumber(WH, 'SAL-00007')!
    const order = listGroupableOrders(WH)[0]
    expect([order.kind, order.number, order.destination]).toEqual(['SALES_ORDER', 'OV-00005', 'Sucursal Norte'])
    const { tasks, lineNotice } = getGroupTasks([notice, order])
    expect(tasks.map((t) => t.lineId)).toEqual([70, 70, -70])   // las de la orden van en negativo
    const picks = [
      { taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 },
      { taskKey: tasks[2].key, binId: 1, binCode: 'P-01', qty: 5 },
    ]
    const id = queueExitGroup([notice, order], tasks, picks, lineNotice)!
    const body = JSON.parse(listOutbox().find((r) => r.id === id)!.body)
    expect(body.documents.map((d: { kind: string; publicId: string; lines: Array<{ lineId: number }> }) => [d.kind, d.publicId, d.lines[0].lineId])).toEqual([['EXIT_NOTICE', 'sn-7', 70], ['SALES_ORDER', 'so-5', 70]])
    expect(getExitNoticeLines(7)[0].qtyReserved).toBe(2)
    expect(listSalesOrders(WH)).toEqual([])   // la orden se despachó completa
    expect(getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE id = 1')?.q).toBe(1)
  })

  it('Lote 186: al bajar las órdenes se reaplica la parte de la orden de la maestra pendiente', () => {
    seed()
    applySalesOrders(getDb(), WH, [ORDER])
    const notice = findExitNoticeByNumber(WH, 'SAL-00007')!
    const order = listGroupableOrders(WH)[0]
    const { tasks, lineNotice } = getGroupTasks([notice, order])
    queueExitGroup([notice, order], tasks, [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 }, { taskKey: tasks[2].key, binId: 1, binCode: 'P-01', qty: 3 }], lineNotice)
    applySalesOrders(getDb(), WH, [ORDER])   // llega la copia del servidor, que todavía no sabe de la cola
    expect(getSalesOrderLines(5)[0].qtyReserved).toBe(5)
    reapplyPendingSalesDispatches(getDb())
    expect(getSalesOrderLines(5)[0].qtyReserved).toBe(2)
  })

  it('Lote 193: sin warehouse.pick.any solo se ven los avisos asignados a la persona y no hay órdenes de venta', () => {
    applyExitNotices(getDb(), WH, [{ ...NOTICE, assignedToUserId: 7 }, { ...NOTICE, id: 8, publicId: 'sn-8', number: 'SAL-00008', assignedToUserId: null, lines: [{ ...NOTICE.lines[0], id: 80 }] }])
    expect(listExitNotices(WH).map((o) => o.number)).toEqual(['SAL-00007', 'SAL-00008'])   // sin alcance (compatibilidad): todo
    expect(listExitNotices(WH, dispatchScope(['warehouse.pick'], 7)).map((o) => o.number)).toEqual(['SAL-00007'])
    expect(listExitNotices(WH, dispatchScope(['warehouse.pick'], 9))).toEqual([])
    expect(listExitNotices(WH, dispatchScope(['warehouse.pick', 'warehouse.pick.any'], 9)).map((o) => o.number)).toEqual(['SAL-00007', 'SAL-00008'])
    expect(findExitNoticeByNumber(WH, 'SAL-00008', dispatchScope(['warehouse.pick'], 7))).toBeNull()
    expect(findExitNoticeByNumber(WH, 'SAL-00007', dispatchScope(['warehouse.pick'], 7))?.number).toBe('SAL-00007')
  })

  it('Lote 195: sin warehouse.pick.any las órdenes de venta se ven solo si están asignadas a la persona', () => {
    applySalesOrders(getDb(), WH, [
      { id: 5, publicId: 'so-5', number: 'OV-00005', warehousePublicId: WH, clientName: 'ASICS', consigneeName: null, statusCode: 'REQUESTED', requiredDate: null, requestedAtUtc: null, isActive: true, assignedToUserId: 7,
        lines: [{ id: 50, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 2, qtyReserved: 2, qtyShipped: 0, qtyBackordered: 0, reservations: [{ binId: 1, binCode: 'P-01', lotId: null, lotNumber: null, quantity: 2 }] }] },
      { id: 6, publicId: 'so-6', number: 'OV-00006', warehousePublicId: WH, clientName: 'ASICS', consigneeName: null, statusCode: 'REQUESTED', requiredDate: null, requestedAtUtc: null, isActive: true, assignedToUserId: null,
        lines: [{ id: 60, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 2, qtyReserved: 2, qtyShipped: 0, qtyBackordered: 0, reservations: [{ binId: 1, binCode: 'P-01', lotId: null, lotNumber: null, quantity: 2 }] }] },
    ])
    expect(listSalesOrders(WH, dispatchScope(['warehouse.pick'], 7)).map((o) => o.number)).toEqual(['OV-00005'])
    expect(listSalesOrders(WH, dispatchScope(['warehouse.pick', 'warehouse.pick.any'], 9)).map((o) => o.number)).toEqual(['OV-00005', 'OV-00006'])
    expect(findSalesOrderByNumber(WH, 'OV-00006', dispatchScope(['warehouse.pick'], 7))).toBeNull()
    expect(listGroupableOrders(WH, dispatchScope(['warehouse.pick'], 7)).map((o) => o.number)).toEqual(['OV-00005'])
  })
})
