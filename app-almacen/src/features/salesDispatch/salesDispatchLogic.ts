// Lote 65 (P4, S2) — despachar contra una orden de venta en el aparato: lógica pura (sin base ni red). La orden trae, por línea, de qué posición y lote reservó el
// sistema cada parte; cada una es una TAREA guiada: ir a la posición, leerla y confirmar la cantidad. Se puede tomar de otra posición (la reserva se mueve en el
// servidor si ahí hay existencia) o menos de lo reservado (el resto queda reservado para otro despacho).
export interface SalesTask {
  /** Clave estable de la tarea (línea + posición + lote). */
  key: string
  lineId: number
  productPublicId: string
  sku: string
  productName: string
  binId: number
  binCode: string
  lotId: number | null
  lotNumber: string | null
  /** Lo reservado en esa posición y lote. */
  qty: number
}

/** Una tarea ya cumplida (o con lo tomado distinto): lo que el operario leyó y cuánto sacó. */
export interface SalesPick {
  taskKey: string
  binId: number
  binCode: string
  qty: number
}

export interface LocalSalesLine {
  id: number
  productPublicId: string
  sku: string
  productName: string
  qtyOrdered: number
  qtyReserved: number
  qtyShipped: number
  qtyBackordered: number
  reservations: Array<{ binId: number; binCode: string; lotId: number | null; lotNumber: string | null; quantity: number }>
}

/** Mensajes de bloqueo (clave i18n) al revisar lo que se va a mandar. */
export type SalesPickBlock = 'qtyInvalid' | 'qtyTooMuch'

export const SALES_STANDARD_SERVICE = 'STANDARD'
export const SALES_STANDARD_PACKAGE = 'BOX'

export function taskKey(lineId: number, binId: number, lotId: number | null): string {
  return `${lineId}|${binId}|${lotId ?? ''}`
}

/** Las tareas de la orden, en el orden de las líneas y de las reservas (el orden de salida que dejó el servidor). Una línea sin reservas no tiene tarea. */
export function buildTasks(lines: readonly LocalSalesLine[]): SalesTask[] {
  const out: SalesTask[] = []
  for (const l of lines) {
    for (const r of l.reservations) {
      if (!(r.quantity > 0)) continue
      out.push({
        key: taskKey(l.id, r.binId, r.lotId),
        lineId: l.id,
        productPublicId: l.productPublicId,
        sku: l.sku,
        productName: l.productName,
        binId: r.binId,
        binCode: r.binCode,
        lotId: r.lotId,
        lotNumber: r.lotNumber,
        qty: r.quantity,
      })
    }
  }
  return out
}

/** Texto de cantidad escrito por el operario → número > 0 o null (acepta coma decimal). */
export function parseQty(text: string): number | null {
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n > 0 ? n : null
}

/** ¿La cantidad se puede tomar para esta tarea? Nunca más de lo reservado en ella. */
export function checkPickQty(task: SalesTask, text: string): SalesPickBlock | null {
  const n = parseQty(text)
  if (n === null) return 'qtyInvalid'
  return n > task.qty + 1e-9 ? 'qtyTooMuch' : null
}

/** La siguiente tarea sin cumplir (o null si ya están todas). */
export function nextTask(tasks: readonly SalesTask[], picks: readonly SalesPick[], skipped: ReadonlySet<string> = new Set()): SalesTask | null {
  const done = new Set(picks.map((p) => p.taskKey))
  return tasks.find((t) => !done.has(t.key) && !skipped.has(t.key)) ?? null
}

/** Lo total que se va a sacar. */
export function totalPicked(picks: readonly SalesPick[]): number {
  return picks.reduce((sum, p) => sum + p.qty, 0)
}

/** Líneas del cuerpo de POST /sales-orders/{id}/dispatch. Si se tomó de una posición distinta a la reservada no se manda el lote (el servidor lo decide ahí). */
export function buildDispatchLines(tasks: readonly SalesTask[], picks: readonly SalesPick[]): Array<{ lineId: number; quantity: number; binId: number; lotId?: number }> {
  const out: Array<{ lineId: number; quantity: number; binId: number; lotId?: number }> = []
  for (const p of picks) {
    const task = tasks.find((t) => t.key === p.taskKey)
    if (!task || !(p.qty > 0)) continue
    const line: { lineId: number; quantity: number; binId: number; lotId?: number } = { lineId: task.lineId, quantity: p.qty, binId: p.binId }
    if (p.binId === task.binId && task.lotId != null) line.lotId = task.lotId
    out.push(line)
  }
  return out
}

/** Cuerpo del despacho. Con `bultos` también pide la entrega (la orden de transporte) en la misma transacción: cliente y consignatario salen de la orden de venta. */
export function buildDispatchBody(tasks: readonly SalesTask[], picks: readonly SalesPick[], delivery: { packages: number } | null) {
  const body: {
    lines: Array<{ lineId: number; quantity: number; binId: number; lotId?: number }>
    delivery?: { serviceType: string; packages: Array<{ packageType: string; pieces: number }>; confirmNow: boolean }
  } = { lines: buildDispatchLines(tasks, picks) }
  if (delivery) {
    // Sin selector de servicio en el aparato (como el empaque de Despacho): los catálogos STANDARD y BOX vienen sembrados.
    body.delivery = {
      serviceType: SALES_STANDARD_SERVICE,
      packages: [{ packageType: SALES_STANDARD_PACKAGE, pieces: Math.max(1, Math.round(delivery.packages)) }],
      confirmNow: true,
    }
  }
  return body
}

/** Pendiente por despachar de la orden: lo reservado aún en el servidor (lo que el aparato puede sacar ya). */
export function dispatchable(lines: readonly LocalSalesLine[]): number {
  return lines.reduce((sum, l) => sum + l.qtyReserved, 0)
}

/** Número de recolección (EMP-#####) de la respuesta del despacho enviado, o null. */
export function dispatchBatchNumber(result: unknown): string | null {
  const n = (result as { pickBatchNumber?: unknown } | null | undefined)?.pickBatchNumber
  return typeof n === 'string' && n.trim() ? n : null
}
