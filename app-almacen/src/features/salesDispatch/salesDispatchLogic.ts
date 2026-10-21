// Lote 65 (P4, S2) — despachar contra una orden de venta en el aparato: lógica pura (sin base ni red). La orden trae, por línea, de qué posición y lote reservó el sistema cada parte; cada una es una
// TAREA guiada: ir a la posición, leerla y confirmar la cantidad. Se puede tomar de otra posición (la reserva se mueve en el servidor si ahí hay existencia) o menos de lo reservado (el resto queda
// reservado para otro despacho).
// Lote 71 (kits): una línea de KIT se pide en kits pero lo reservado es de sus COMPONENTES; cada reserva de un componente es una tarea (se recoge la pieza) y al terminar se manda
// «N kits» — los kits completos que alcanzan con lo recogido (el menor cociente recogido ÷ cantidad por kit entre los componentes); el servidor saca los componentes de lo reservado.
export interface SalesTask {
  /** Clave estable de la tarea (línea + posición + lote; en un kit, también el componente). */
  key: string
  lineId: number
  /** El producto que se recoge: el de la línea o, en un kit, el componente. */
  productPublicId: string
  sku: string
  productName: string
  binId: number
  binCode: string
  lotId: number | null
  lotNumber: string | null
  /** Lo reservado en esa posición y lote (piezas del producto que se recoge). */
  qty: number
  /** Lote 71: SKU del kit si esta tarea es una pieza de un kit; null = línea normal. */
  kitSku: string | null
  /** Lote 71: piezas de este componente por cada kit (solo en una pieza de kit). */
  perKit: number | null
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
  /** Lote 71: la línea es de un kit (cantidades en kits; las reservas son de los componentes). */
  isKit: boolean
  components: Array<{ productPublicId: string; sku: string; name: string; qtyPerKit: number }>
  reservations: Array<{ binId: number; binCode: string; lotId: number | null; lotNumber: string | null; quantity: number; productPublicId: string | null; sku: string | null; productName: string | null }>
}

/** Mensajes de bloqueo (clave i18n) al revisar lo que se va a mandar. */
export type SalesPickBlock = 'qtyInvalid' | 'qtyTooMuch'

export const SALES_STANDARD_SERVICE = 'STANDARD'
export const SALES_STANDARD_PACKAGE = 'BOX'

export function taskKey(lineId: number, binId: number, lotId: number | null, productPublicId: string | null = null): string {
  return `${lineId}|${productPublicId ? productPublicId + '|' : ''}${binId}|${lotId ?? ''}`
}

/** Las tareas de la orden, en el orden de las líneas y de las reservas (el orden de salida que dejó el servidor). Una línea sin reservas no tiene tarea. */
export function buildTasks(lines: readonly LocalSalesLine[]): SalesTask[] {
  const out: SalesTask[] = []
  for (const l of lines) {
    for (const r of l.reservations) {
      if (!(r.quantity > 0)) continue
      if (l.isKit) {
        const productPublicId = r.productPublicId ?? ''
        out.push({
          key: taskKey(l.id, r.binId, r.lotId, productPublicId),
          lineId: l.id,
          productPublicId,
          sku: r.sku ?? '',
          productName: r.productName ?? '',
          binId: r.binId,
          binCode: r.binCode,
          lotId: r.lotId,
          lotNumber: r.lotNumber,
          qty: r.quantity,
          kitSku: l.sku,
          perKit: l.components.find((c) => c.productPublicId === productPublicId)?.qtyPerKit ?? 1,
        })
        continue
      }
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
        kitSku: null,
        perKit: null,
      })
    }
  }
  // varias reservas de la misma posición y lote (productos iguales) son UNA tarea
  const merged = new Map<string, SalesTask>()
  for (const t of out) {
    const same = merged.get(t.key)
    if (same) same.qty += t.qty
    else merged.set(t.key, { ...t })
  }
  return [...merged.values()]
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

/** Lote 192: la tarea actual: la que el operario eligió de la lista (si sigue pendiente) o, si no, la siguiente de la ruta. */
export function currentTask(tasks: readonly SalesTask[], picks: readonly SalesPick[], skipped: ReadonlySet<string>, focusKey: string | null): SalesTask | null {
  if (focusKey) {
    const done = new Set(picks.map((p) => p.taskKey))
    const chosen = tasks.find((t) => t.key === focusKey && !done.has(t.key))
    if (chosen) return chosen
  }
  return nextTask(tasks, picks, skipped)
}

/** Lo total que se va a sacar (piezas). */
export function totalPicked(picks: readonly SalesPick[]): number {
  return picks.reduce((sum, p) => sum + p.qty, 0)
}

/**
 * Lote 71 — kits completos que alcanzan en una línea de kit con lo recogido: el menor cociente (recogido ÷ cantidad por kit) entre los componentes de la línea, hacia abajo.
 * Un componente sin nada recogido da 0 kits.
 */
export function kitsReady(tasks: readonly SalesTask[], picks: readonly SalesPick[], lineId: number): number {
  const parts = tasks.filter((t) => t.lineId === lineId && t.kitSku !== null)
  if (parts.length === 0) return 0
  const pickedBy = new Map<string, number>()
  for (const p of picks) {
    const t = parts.find((x) => x.key === p.taskKey)
    if (t) pickedBy.set(t.productPublicId, (pickedBy.get(t.productPublicId) ?? 0) + p.qty)
  }
  const perKit = new Map<string, number>()
  for (const t of parts) perKit.set(t.productPublicId, t.perKit ?? 1)
  let min = Infinity
  for (const [product, per] of perKit) min = Math.min(min, Math.floor(((pickedBy.get(product) ?? 0) + 1e-9) / per))
  return Number.isFinite(min) ? Math.max(0, min) : 0
}

/** Resumen por kit de lo recogido: kits completos y si quedó alguna pieza recogida que no completa un kit (se queda reservada). */
export function kitSummaries(tasks: readonly SalesTask[], picks: readonly SalesPick[]): Array<{ lineId: number; kitSku: string; kits: number; hasPicks: boolean }> {
  const lineIds = [...new Set(tasks.filter((t) => t.kitSku !== null).map((t) => t.lineId))]
  return lineIds.map((lineId) => {
    const kitSku = tasks.find((t) => t.lineId === lineId)?.kitSku ?? ''
    const taskKeys = new Set(tasks.filter((t) => t.lineId === lineId).map((t) => t.key))
    return { lineId, kitSku, kits: kitsReady(tasks, picks, lineId), hasPicks: picks.some((p) => taskKeys.has(p.taskKey)) }
  })
}

type DispatchLineBody = { lineId: number; quantity: number; binId?: number; lotId?: number }

/**
 * Líneas del cuerpo de POST /sales-orders/{id}/dispatch. Producto normal: una por tarea, con su posición (y el lote solo si se tomó de la reservada). Kit: UNA por línea con los kits
 * completos (sin posición ni lote: el servidor saca los componentes de lo reservado).
 */
export function buildDispatchLines(tasks: readonly SalesTask[], picks: readonly SalesPick[]): DispatchLineBody[] {
  const out: DispatchLineBody[] = []
  for (const p of picks) {
    const task = tasks.find((t) => t.key === p.taskKey)
    if (!task || task.kitSku !== null || !(p.qty > 0)) continue
    const line: DispatchLineBody = { lineId: task.lineId, quantity: p.qty, binId: p.binId }
    if (p.binId === task.binId && task.lotId != null) line.lotId = task.lotId
    out.push(line)
  }
  for (const k of kitSummaries(tasks, picks)) if (k.kits > 0) out.push({ lineId: k.lineId, quantity: k.kits })
  return out
}

/** Cuerpo del despacho. Con `bultos` también pide la entrega (la orden de transporte) en la misma transacción: cliente y consignatario salen de la orden de venta. */
export function buildDispatchBody(tasks: readonly SalesTask[], picks: readonly SalesPick[], delivery: { packages: number } | null) {
  const body: {
    lines: DispatchLineBody[]
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
