// Lote 172 (B1) — «Mis tareas»: lo que el supervisor le asignó a este usuario (avisos de llegada por recibir, avisos de salida por despachar y acomodos), leído de la base local para que funcione
// sin señal, más lo que está sin asignar y cualquiera puede tomar. La asignación ordena el trabajo, no lo bloquea: Recibir y Despacho siguen mostrando todo.
import { getDb } from '../../kernel/db/database'

export interface MyAsnTask {
  id: number
  /** LLE-##### (vacío en un aviso que todavía no trae número: se baja con la próxima sincronización). */
  number: string
  reference: string | null
  clientName: string | null
  expectedDate: string | null
  assignedToUserId: number | null
}

export interface MyExitTask {
  id: number
  publicId: string
  number: string
  clientName: string | null
  destination: string | null
  reference: string | null
  requiredDate: string | null
  assignedToUserId: number | null
}

export type Owner = 'mine' | 'unassigned'

interface AsnRow {
  id: number
  number: string | null
  reference: string | null
  client_name: string | null
  expected_date: string | null
  assigned_to_user_id: number | null
}

/** Avisos de llegada de cliente esperados del almacén (los nacidos de una orden de compra se reciben contra la orden), de este usuario o sin asignar. */
export function listAsnTasks(warehousePublicId: string, userId: number, owner: Owner): MyAsnTask[] {
  const where = owner === 'mine' ? 'assigned_to_user_id = ?' : 'assigned_to_user_id IS NULL AND ? IS NOT NULL'
  return getDb()
    .getAllSync<AsnRow>(
      `SELECT id, number, reference, client_name, expected_date, assigned_to_user_id FROM asn
       WHERE is_active = 1 AND warehouse_public_id = ? AND purchase_order_public_id IS NULL AND ${where}
       ORDER BY expected_date IS NULL, expected_date, id`,
      [warehousePublicId, userId],
    )
    .map((r) => ({ id: r.id, number: r.number ?? '', reference: r.reference, clientName: r.client_name, expectedDate: r.expected_date, assignedToUserId: r.assigned_to_user_id }))
}

interface ExitRow {
  id: number
  public_id: string
  number: string
  client_name: string | null
  destination: string | null
  reference: string | null
  required_date: string | null
  assigned_to_user_id: number | null
}

/** Avisos de salida con algo reservado que se puede despachar ya (los demás no bajan al aparato), de este usuario o sin asignar; la fecha requerida manda. */
export function listExitTasks(warehousePublicId: string, userId: number, owner: Owner): MyExitTask[] {
  const where = owner === 'mine' ? 'o.assigned_to_user_id = ?' : 'o.assigned_to_user_id IS NULL AND ? IS NOT NULL'
  return getDb()
    .getAllSync<ExitRow>(
      `SELECT o.id, o.public_id, o.number, o.client_name, o.destination, o.reference, o.required_date, o.assigned_to_user_id FROM exit_notice o
       WHERE o.warehouse_public_id = ? AND ${where}
         AND (SELECT COALESCE(SUM(l.qty_reserved), 0) FROM exit_notice_line l WHERE l.exit_notice_id = o.id) > 0
       ORDER BY (o.required_date IS NULL), o.required_date, o.id`,
      [warehousePublicId, userId],
    )
    .map((r) => ({
      id: r.id,
      publicId: r.public_id,
      number: r.number,
      clientName: r.client_name,
      destination: r.destination,
      reference: r.reference,
      requiredDate: r.required_date,
      assignedToUserId: r.assigned_to_user_id,
    }))
}

export interface MyPoTask {
  id: number
  publicId: string
  number: string
  supplierName: string | null
  expectedDate: string | null
  assignedToUserId: number | null
}

/** Lote 195: órdenes de compra abiertas (enviadas o recibidas en parte) del almacén que se pueden recibir, de este usuario o sin asignar. */
export function listPoTasks(warehousePublicId: string, userId: number, owner: Owner): MyPoTask[] {
  const where = owner === 'mine' ? 'o.assigned_to_user_id = ?' : 'o.assigned_to_user_id IS NULL AND ? IS NOT NULL'
  return getDb()
    .getAllSync<{ id: number; public_id: string; number: string; supplier_name: string | null; expected_date: string | null; assigned_to_user_id: number | null }>(
      `SELECT o.id, o.public_id, o.number, o.supplier_name, o.expected_date, o.assigned_to_user_id FROM purchase_order o
       WHERE o.is_active = 1 AND o.warehouse_public_id = ? AND ${where}
         AND (SELECT COALESCE(SUM(l.qty_pending), 0) FROM purchase_order_line l WHERE l.purchase_order_id = o.id) > 0
       ORDER BY (o.expected_date IS NULL), o.expected_date, o.id`,
      [warehousePublicId, userId],
    )
    .map((r) => ({ id: r.id, publicId: r.public_id, number: r.number, supplierName: r.supplier_name, expectedDate: r.expected_date, assignedToUserId: r.assigned_to_user_id }))
}

export interface MySalesTask {
  id: number
  publicId: string
  number: string
  clientName: string | null
  consigneeName: string | null
  requiredDate: string | null
  assignedToUserId: number | null
}

/** Lote 195: órdenes de venta con algo reservado que se puede despachar ya, de este usuario o sin asignar; la fecha requerida manda. */
export function listSalesTasks(warehousePublicId: string, userId: number, owner: Owner): MySalesTask[] {
  const where = owner === 'mine' ? 'o.assigned_to_user_id = ?' : 'o.assigned_to_user_id IS NULL AND ? IS NOT NULL'
  return getDb()
    .getAllSync<{ id: number; public_id: string; number: string; client_name: string | null; consignee_name: string | null; required_date: string | null; assigned_to_user_id: number | null }>(
      `SELECT o.id, o.public_id, o.number, o.client_name, o.consignee_name, o.required_date, o.assigned_to_user_id FROM sales_order o
       WHERE o.warehouse_public_id = ? AND ${where}
         AND (SELECT COALESCE(SUM(l.qty_reserved), 0) FROM sales_order_line l WHERE l.sales_order_id = o.id) > 0
       ORDER BY (o.required_date IS NULL), o.required_date, o.id`,
      [warehousePublicId, userId],
    )
    .map((r) => ({ id: r.id, publicId: r.public_id, number: r.number, clientName: r.client_name, consigneeName: r.consignee_name, requiredDate: r.required_date, assignedToUserId: r.assigned_to_user_id }))
}

/** Cuántas tareas de documento tiene asignadas este usuario en el almacén (la insignia de Inicio). Los acomodos asignados se suman aparte (se leen en línea). */
export function countMyDocuments(warehousePublicId: string, userId: number): number {
  return (
    listAsnTasks(warehousePublicId, userId, 'mine').length +
    listExitTasks(warehousePublicId, userId, 'mine').length +
    listPoTasks(warehousePublicId, userId, 'mine').length +
    listSalesTasks(warehousePublicId, userId, 'mine').length
  )
}

/** Tras «Tomar» con señal, la copia local queda de una vez a nombre de este usuario (la próxima sincronización lo confirma). */
export function markTakenLocally(kind: 'asn' | 'exit' | 'po' | 'sales', id: number, userId: number): void {
  const table = kind === 'asn' ? 'asn' : kind === 'exit' ? 'exit_notice' : kind === 'po' ? 'purchase_order' : 'sales_order'
  getDb().runSync(`UPDATE ${table} SET assigned_to_user_id = ? WHERE id = ?`, [userId, id])
}

/** El texto con el que se encuentra el documento al abrirlo en Recibir o Despacho (se escanea o se teclea igual): el número del aviso, o su referencia. */
export function asnOpenCode(a: MyAsnTask): string {
  return a.number || a.reference || `#${a.id}`
}
