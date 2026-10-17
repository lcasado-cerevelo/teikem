// Lote 83 (P6) — recibir una devolución de venta en el aparato: lógica pura (sin base ni red). La devolución viene autorizada con lo pendiente de cada línea; por línea el operario
// anota lo que llegó BUENO (vuelve al inventario) y lo que llegó DAÑADO (con la descripción de lo que pasó y, si quiere, desecharlo), y el lote si el producto lo lleva. Lo que el servidor
// valida otra vez al sincronizar (no pasarse de lo pendiente, la descripción del daño) se dice aquí antes de enviar.
export interface ReturnLine {
  id: number
  productPublicId: string
  sku: string
  productName: string
  /** NONE o LOT (la serie no se devuelve en esta versión). */
  trackingTypeCode: string
  qtyRequested: number
  qtyReceived: number
  qtyDamaged: number
  /** Lo que falta por recibir. */
  pending: number
}

/** Lo que el operario captura de una línea (texto tal cual, para poder escribirlo a medias). */
export interface LineEntry {
  good: string
  damaged: string
  note: string
  discard: boolean
  lot: string
}

export type ReceiveBlockKind = 'nothing' | 'qtyInvalid' | 'exceeds' | 'damageNote' | 'lot'
export interface ReceiveBlock {
  kind: ReceiveBlockKind
  lineId?: number
  sku?: string
  pending?: number
}

/** Cantidad: vacío = 0; coma o punto; no negativa, hasta 3 decimales. null = no es una cantidad válida. */
export function parseQty(text: string): number | null {
  const clean = text.trim().replace(',', '.')
  if (clean === '') return 0
  if (!/^\d+(\.\d{1,3})?$/.test(clean)) return null
  return Number(clean)
}

/** Por omisión llega todo bueno (lo pendiente); el operario baja lo bueno y sube lo dañado. */
export function initialEntries(lines: readonly ReturnLine[]): Record<number, LineEntry> {
  const out: Record<number, LineEntry> = {}
  for (const l of lines) out[l.id] = { good: l.pending > 0 ? String(l.pending) : '', damaged: '', note: '', discard: false, lot: '' }
  return out
}

/** Lo que bloquea el envío (la primera línea con problema) o null si se puede enviar. Las líneas con 0 bueno y 0 dañado no cuentan. */
export function checkReceive(lines: readonly ReturnLine[], entries: Readonly<Record<number, LineEntry>>): ReceiveBlock | null {
  let any = false
  for (const l of lines) {
    const e = entries[l.id]
    if (!e) continue
    const good = parseQty(e.good)
    const damaged = parseQty(e.damaged)
    if (good === null || damaged === null) return { kind: 'qtyInvalid', lineId: l.id, sku: l.sku }
    if (good + damaged === 0) continue
    any = true
    if (good + damaged > l.pending + 1e-9) return { kind: 'exceeds', lineId: l.id, sku: l.sku, pending: l.pending }
    if (damaged > 0 && e.note.trim() === '') return { kind: 'damageNote', lineId: l.id, sku: l.sku }
    if (l.trackingTypeCode === 'LOT' && e.lot.trim() === '') return { kind: 'lot', lineId: l.id, sku: l.sku }
  }
  return any ? null : { kind: 'nothing' }
}

export interface ReceiveBodyLine {
  lineId: number
  goodQty: number
  damagedQty: number
  damageNote?: string
  damageDiscard?: boolean
  lot?: { number: string }
}

/** El cuerpo de POST /sales-returns/{id}/receive: solo las líneas con algo recibido. `closeRest` da por no devuelto lo que no llegó. */
export function buildReceiveBody(lines: readonly ReturnLine[], entries: Readonly<Record<number, LineEntry>>, closeRest: boolean): { lines: ReceiveBodyLine[]; closeRest?: boolean } {
  const out: ReceiveBodyLine[] = []
  for (const l of lines) {
    const e = entries[l.id]
    if (!e) continue
    const good = parseQty(e.good) ?? 0
    const damaged = parseQty(e.damaged) ?? 0
    if (good + damaged === 0) continue
    out.push({
      lineId: l.id,
      goodQty: good,
      damagedQty: damaged,
      ...(damaged > 0 ? { damageNote: e.note.trim(), ...(e.discard ? { damageDiscard: true } : {}) } : {}),
      ...(l.trackingTypeCode === 'LOT' && e.lot.trim() !== '' ? { lot: { number: e.lot.trim() } } : {}),
    })
  }
  return { lines: out, ...(closeRest ? { closeRest: true } : {}) }
}

/** Lo que se va a recibir en total (bueno y dañado), para el resumen antes de enviar. */
export function totalReceived(lines: readonly ReturnLine[], entries: Readonly<Record<number, LineEntry>>): { good: number; damaged: number } {
  let good = 0
  let damaged = 0
  for (const l of lines) {
    const e = entries[l.id]
    if (!e) continue
    good += parseQty(e.good) ?? 0
    damaged += parseQty(e.damaged) ?? 0
  }
  return { good, damaged }
}
