import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { asnOpenCode, countMyDocuments, listAsnTasks, listExitTasks, listPoTasks, listSalesTasks, markTakenLocally } from './myTasksLogic'

const ME = 7
const ANA = 9

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  const db = getDb()
  const asn = (id: number, number: string, assigned: number | null, extra = '') =>
    db.runSync(
      `INSERT INTO asn (id, warehouse_public_id, client_name, reference, expected_date, number, assigned_to_user_id, is_active${extra ? ', purchase_order_public_id' : ''}) VALUES (?, 'wh-1', 'AxisCare', 'AX-1', '2026-10-22', ?, ?, 1${extra ? ", 'po-1'" : ''})`,
      [id, number, assigned],
    )
  asn(1, 'LLE-00001', ME)
  asn(2, 'LLE-00002', ANA)
  asn(3, 'LLE-00003', null)
  asn(4, 'LLE-00004', ME, 'po')   // nacido de una orden de compra: se recibe contra la orden, no sale aquí
  const notice = (id: number, number: string, assigned: number | null, reserved: number) => {
    db.runSync(
      `INSERT INTO exit_notice (id, public_id, number, warehouse_public_id, client_name, destination, required_date, assigned_to_user_id) VALUES (?, ?, ?, 'wh-1', 'ASICS', 'Muelle 2', '2026-10-23', ?)`,
      [id, `pub-${id}`, number, assigned],
    )
    db.runSync('INSERT INTO exit_notice_line (id, exit_notice_id, product_public_id, qty_ordered, qty_reserved, qty_shipped, qty_backordered) VALUES (?, ?, ?, 5, ?, 0, 0)', [id * 10, id, 'p1', reserved])
  }
  notice(1, 'SAL-00001', ME, 5)
  notice(2, 'SAL-00002', null, 5)
  notice(3, 'SAL-00003', ME, 0)   // sin nada reservado todavía: no se puede despachar, no sale
})

describe('Mis tareas — lógica local', () => {
  it('mis avisos de llegada: solo los asignados a mí, de cliente (no los de una orden de compra)', () => {
    expect(listAsnTasks('wh-1', ME, 'mine').map((a) => a.number)).toEqual(['LLE-00001'])
    expect(listAsnTasks('wh-1', ANA, 'mine').map((a) => a.number)).toEqual(['LLE-00002'])
    expect(listAsnTasks('wh-1', ME, 'mine')[0]).toMatchObject({ reference: 'AX-1', clientName: 'AxisCare', expectedDate: '2026-10-22' })
  })

  it('los sin asignar son los que cualquiera puede tomar', () => {
    expect(listAsnTasks('wh-1', ME, 'unassigned').map((a) => a.number)).toEqual(['LLE-00003'])
    expect(listExitTasks('wh-1', ME, 'unassigned').map((o) => o.number)).toEqual(['SAL-00002'])
  })

  it('mis avisos de salida: solo los míos con algo reservado', () => {
    expect(listExitTasks('wh-1', ME, 'mine').map((o) => o.number)).toEqual(['SAL-00001'])
  })

  it('la cuenta de Inicio suma mis documentos', () => {
    expect(countMyDocuments('wh-1', ME)).toBe(2)
    expect(countMyDocuments('wh-1', ANA)).toBe(1)
    expect(countMyDocuments('wh-2', ME)).toBe(0)
  })

  it('tomar uno sin asignar lo deja a mi nombre en la copia local', () => {
    markTakenLocally('asn', 3, ME)
    markTakenLocally('exit', 2, ME)
    expect(listAsnTasks('wh-1', ME, 'mine').map((a) => a.number)).toEqual(['LLE-00001', 'LLE-00003'])
    expect(listExitTasks('wh-1', ME, 'mine').map((o) => o.number)).toEqual(['SAL-00001', 'SAL-00002'])
    expect(listAsnTasks('wh-1', ME, 'unassigned')).toEqual([])
  })

  it('el código para abrir un aviso es su número, o su referencia si todavía no trae número', () => {
    const base = { id: 5, reference: 'AX-9', clientName: null, expectedDate: null, assignedToUserId: ME }
    expect(asnOpenCode({ ...base, number: 'LLE-00005' })).toBe('LLE-00005')
    expect(asnOpenCode({ ...base, number: '' })).toBe('AX-9')
    expect(asnOpenCode({ ...base, number: '', reference: null })).toBe('#5')
  })
})

describe('órdenes de compra y de venta asignadas (Lote 195)', () => {
  beforeEach(() => {
    const db = getDb()
    db.runSync("INSERT INTO purchase_order (id, public_id, number, warehouse_public_id, supplier_name, status_code, expected_date, is_active, assigned_to_user_id) VALUES (1, 'po-1', 'PO-00001', 'wh-1', 'Proveedor Sur', 'SENT', '2026-10-25', 1, 7)")
    db.runSync("INSERT INTO purchase_order (id, public_id, number, warehouse_public_id, supplier_name, status_code, is_active, assigned_to_user_id) VALUES (2, 'po-2', 'PO-00002', 'wh-1', 'Otro', 'SENT', 1, NULL)")
    db.runSync("INSERT INTO purchase_order (id, public_id, number, warehouse_public_id, supplier_name, status_code, is_active, assigned_to_user_id) VALUES (3, 'po-3', 'PO-00003', 'wh-1', 'Ya recibida', 'SENT', 1, 7)")
    for (const [id, po, pending] of [[1, 1, 5], [2, 2, 3], [3, 3, 0]] as const)
      db.runSync("INSERT INTO purchase_order_line (id, purchase_order_id, product_public_id, sku, product_name, qty_ordered, qty_received, qty_pending) VALUES (?, ?, 'p1', 'MED', 'Medicina', 5, ?, ?)", [id, po, 5 - pending, pending])
    db.runSync("INSERT INTO sales_order (id, public_id, number, warehouse_public_id, client_name, status_code, assigned_to_user_id) VALUES (1, 'so-1', 'OV-00001', 'wh-1', 'ASICS', 'REQUESTED', 7)")
    db.runSync("INSERT INTO sales_order (id, public_id, number, warehouse_public_id, client_name, status_code, assigned_to_user_id) VALUES (2, 'so-2', 'OV-00002', 'wh-1', 'NIKE', 'REQUESTED', NULL)")
    for (const [id, so] of [[1, 1], [2, 2]] as const)
      db.runSync("INSERT INTO sales_order_line (id, sales_order_id, product_public_id, sku, product_name, qty_ordered, qty_reserved, qty_shipped, qty_backordered, is_kit) VALUES (?, ?, 'p1', 'MED', 'Medicina', 4, 4, 0, 0, 0)", [id, so])
  })

  it('las mías y las sin asignar, solo con algo por recibir o por despachar; cuentan para la insignia y se pueden tomar', () => {
    expect(listPoTasks('wh-1', ME, 'mine').map((o) => o.number)).toEqual(['PO-00001'])   // PO-00003 no tiene nada pendiente
    expect(listPoTasks('wh-1', ME, 'unassigned').map((o) => o.number)).toEqual(['PO-00002'])
    expect(listSalesTasks('wh-1', ME, 'mine').map((o) => o.number)).toEqual(['OV-00001'])
    expect(listSalesTasks('wh-1', ME, 'unassigned').map((o) => o.number)).toEqual(['OV-00002'])
    markTakenLocally('po', 2, ME)
    markTakenLocally('sales', 2, ME)
    expect(listPoTasks('wh-1', ME, 'mine').map((o) => o.number)).toEqual(['PO-00001', 'PO-00002'])
    expect(listSalesTasks('wh-1', ME, 'mine').map((o) => o.number)).toEqual(['OV-00001', 'OV-00002'])
    expect(countMyDocuments('wh-1', ME)).toBeGreaterThanOrEqual(4)
  })
})
